using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;

namespace IntegrationTest.TwoFactor
{
    public class TwoFactorVerifyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly string _adminToken;

        public TwoFactorVerifyIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
                builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
                builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"TwoFactorVerifyIntegrationDb_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
            _adminToken = JwtTestConfig.AdminToken;
        }

        public async Task InitializeAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.SegUsuario.Any(u => u.strNombre == "admin"))
            {
                var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "admin",
                    strCorreoElectronico = "admin@test.com",
                    strPWD = "hash",
                    str2FASecreto = secret,
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
                await db.SaveChangesAsync();
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Verify_WithoutAuth_Returns401()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/2fa/verify",
                new TwoFactorVerifyRequest("123456"));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Verify_EmptyCode_Returns400()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/verify")
            {
                Content = JsonContent.Create(new TwoFactorVerifyRequest(""))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Verify_InvalidCode_Returns400()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/verify")
            {
                Content = JsonContent.Create(new TwoFactorVerifyRequest("000000"))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Verify_ValidCode_Returns200AndEnables2FA()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usuario = db.SegUsuario.First(u => u.strNombre == "admin");
            var totp = new Totp(Base32Encoding.ToBytes(usuario.str2FASecreto!), step: 30, totpSize: 6);
            var validCode = totp.ComputeTotp();

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/verify")
            {
                Content = JsonContent.Create(new TwoFactorVerifyRequest(validCode))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response = await _client.SendAsync(request);

            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");
            var content = await response.Content.ReadFromJsonAsync<TwoFactorVerifyResponse>();
            content.Should().NotBeNull();
            content!.Mensaje.Should().Be("2FA habilitado correctamente.");

            using var checkScope = _factory.Services.CreateScope();
            var checkDb = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var updatedUser = checkDb.SegUsuario.First(u => u.strNombre == "admin");
            updatedUser.bln2FAHabilitado.Should().BeTrue();
        }

        [Fact]
        public async Task Verify_WithoutSetup_Returns400()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userWithoutSetup = new SegUsuario
            {
                strNombre = $"no2fa_{Guid.NewGuid():N}"[..30],
                strCorreoElectronico = "no2fa@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            db.SegUsuario.Add(userWithoutSetup);
            await db.SaveChangesAsync();

            var token = UnitTest.Common.TokenHelper.GenerateValidToken(
                JwtTestConfig.Key, JwtTestConfig.Issuer, JwtTestConfig.Audience,
                sub: userWithoutSetup.strNombre);

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/verify")
            {
                Content = JsonContent.Create(new TwoFactorVerifyRequest("123456"))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Verify_AfterEnabled_Returns400()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usuario = db.SegUsuario.First(u => u.strNombre == "admin");

            var totp = new Totp(Base32Encoding.ToBytes(usuario.str2FASecreto!), step: 30, totpSize: 6);
            var validCode = totp.ComputeTotp();

            var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/verify")
            {
                Content = JsonContent.Create(new TwoFactorVerifyRequest(validCode))
            };
            request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response1 = await _client.SendAsync(request1);
            response1.StatusCode.Should().Be(HttpStatusCode.OK);

            var totp2 = new Totp(Base32Encoding.ToBytes(usuario.str2FASecreto!), step: 30, totpSize: 6);
            var validCode2 = totp2.ComputeTotp();

            var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/verify")
            {
                Content = JsonContent.Create(new TwoFactorVerifyRequest(validCode2))
            };
            request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response2 = await _client.SendAsync(request2);
            response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
