using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using WebAPIDevSecOps.Services;
using UnitTest.Common;

namespace SecurityTest.Login
{
    public class LoginSecurityTest : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public LoginSecurityTest(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
                builder.UseSetting("Jwt:Issuer", "edelmeza.com");
                builder.UseSetting("Jwt:Audience", "edelmeza.com");
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"LoginSecurityDb_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        //Fuerza bruta (Rate Limiting)
        [Fact]
        public async Task Login_Should_Block_After_TooMany_Attempts()
        {
            for (int i = 0; i < 6; i++)
            {
                await _client.PostAsJsonAsync("/api/v1/login/login", new
                {
                    user = "admin",
                    password = "wrong"
                });
            }

            var response = await _client.PostAsJsonAsync("/api/v1/login/login", new
            {
                user = "admin",
                password = "wrong"
            });

            Assert.Equal(429, (int)response.StatusCode);
        }


        //Token valido
        [Fact]
        public async Task Should_Reject_Invalid_Token()
        {
            var invalidToken = "esto.no.es.un.jwt";

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", invalidToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        //Token backlist
        [Fact]
        public async Task Should_Block_Blacklisted_Token()
        {
            var token = await UnitTest.Common.BlacklistHelper.GenerateAndBlacklistTokenAsync(_factory.Services, "01123581321345589144233377610987", "edelmeza.com", "edelmeza.com");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }


        //Token expirado
        [Fact]
        public async Task Should_Reject_Expired_Token()
        {
            var token = GenerateExpiredToken();

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }


        private string GenerateExpiredToken()
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("01123581321345589144233377610987"));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: "edelmeza.com",
                audience: "edelmeza.com",
                claims: new[]
                {
            new Claim(JwtRegisteredClaimNames.Sub, "admin"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                },
                notBefore: DateTime.UtcNow.AddMinutes(-10),
                expires: DateTime.UtcNow.AddMinutes(-1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        //Headers de seguridad
        [Fact]
        public async Task Should_Contain_Security_Headers()
        {
            var response = await _client.GetAsync("/swagger");

            Assert.True(response.Headers.Contains("X-Content-Type-Options"));
            Assert.True(response.Headers.Contains("X-Frame-Options"));
            Assert.True(response.Headers.Contains("X-XSS-Protection"));
            Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        }

        [Fact]
        public async Task Should_Reject_Request_Without_Token()
        {
            var response = await _client.GetAsync("/api/v1/test/secure");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Endpoint_Should_Exist()
        {
            var response = await _client.GetAsync("/api/v1/test/secure");

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Logout_Double_Logout_Does_Not_Error()
        {
            var token = GenerateExpiredToken();

            var firstLogout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logout/logout");
            firstLogout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var firstResponse = await _client.SendAsync(firstLogout);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            var secondLogout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logout/logout");
            secondLogout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var secondResponse = await _client.SendAsync(secondLogout);

            Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
        }

        [Fact]
        public async Task Logout_With_Expired_Token_Returns_Ok()
        {
            var token = GenerateExpiredToken();

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logout/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }


    }
}
