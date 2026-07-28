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

namespace IntegrationTest.Login
{
    public class LoginIntegrationTest : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public LoginIntegrationTest(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
                builder.UseSetting("Jwt:Issuer", "edelmeza.com");
                builder.UseSetting("Jwt:Audience", "edelmeza.com");
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"LoginIntegrationDb_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync() => Task.CompletedTask;

        //INTEGRIDAD DE JWT (NO TAMPERING)
        //Token alterado
        //Verifica que el token no pueda modificarse
        [Fact]
        public async Task Should_Reject_Tampered_Token()
        {
            var token = GenerateValidToken();

            // Alterar el token (simular ataque)
            var tamperedToken = token.Substring(0, token.Length - 2) + "xx";

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", tamperedToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        //INTEGRIDAD DE CLAIMS
        //Nadie debe poder cambiar roles o identidad
        //Test: Token con rol alterado
        [Fact]
        public async Task Should_Reject_Token_With_Invalid_Signature()
        {
            var fakeKey = "clave_falsa_super_insegura_123456789";

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fakeKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: "edelmeza.com",
                audience: "edelmeza.com",
                claims: new[] { new Claim("role", "Admin") },
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: creds
            );

            var fakeToken = new JwtSecurityTokenHandler().WriteToken(token);

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", fakeToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }


        //INTEGRIDAD DE CONFIGURACIÓN JWT
        //Evita que el sistema funcione con configuración inválida
        //Validación removida: GenerateJwtToken está en LoginService,
        //no en LoginController. El Program.cs ya valida Jwt:Key al iniciar.


        //INTEGRIDAD DE RATE LIMITING
        //Verifica que no se pueda abusar del login
        //Test: Bloqueo por múltiples intentos
        [Fact]
        public async Task Should_Block_After_Multiple_Login_Attempts()
        {
            for (int i = 0; i < 6; i++)
            {
                await _client.PostAsJsonAsync("/api/v1/login/login",
                    new { User = "admin", Password = "wrong" });
            }

            var response = await _client.PostAsJsonAsync("/api/v1/login/login",
                new { User = "admin", Password = "wrong" });

            Assert.Equal((HttpStatusCode)429, response.StatusCode);
        }


        [Fact]
        public async Task Should_Reject_Blacklisted_Token()
        {
            var token = await UnitTest.Common.BlacklistHelper.GenerateAndBlacklistTokenAsync(_factory.Services, "01123581321345589144233377610987", "edelmeza.com", "edelmeza.com");

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Logout_Invalidates_Token()
        {
            var token = GenerateValidToken();

            var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logout/logout");
            logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var logoutResponse = await _client.SendAsync(logoutRequest);
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

            var secureRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/secure");
            secureRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var secureResponse = await _client.SendAsync(secureRequest);

            Assert.Equal(HttpStatusCode.Unauthorized, secureResponse.StatusCode);
        }

        [Fact]
        public async Task Logout_Without_Token_Returns_401()
        {
            var response = await _client.PostAsync("/api/v1/logout/logout", null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

                
        
        private string GenerateValidToken()
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("01123581321345589144233377610987")); // misma que appsettings

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
        new Claim(JwtRegisteredClaimNames.Sub, "admin"),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim("role", "Admin")
    };

            var token = new JwtSecurityToken(
                issuer: "edelmeza.com",
                audience: "edelmeza.com",
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

    }
}
