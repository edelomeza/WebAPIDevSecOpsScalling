using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace IntegrationTest.Login2fa
{
    public class Login2faIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public Login2faIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
                builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
                builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"Login2faIntegrationDb_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

            var passwordHash = await Task.Run(() => hasher.HashPassword("Test1234$"));

            if (!db.SegUsuario.Any(u => u.strNombre == "testuser_nofa"))
            {
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "testuser_nofa",
                    strCorreoElectronico = "nofa@test.com",
                    strPWD = passwordHash,
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
            }

            if (!db.SegUsuario.Any(u => u.strNombre == "testuser_2fa"))
            {
                var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "testuser_2fa",
                    strCorreoElectronico = "tfa@test.com",
                    strPWD = passwordHash,
                    str2FASecreto = secret,
                    bln2FAHabilitado = true,
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
            }

            if (!db.SegUsuario.Any(u => u.strNombre == "testuser_2fa_notverified"))
            {
                var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "testuser_2fa_notverified",
                    strCorreoElectronico = "tfanv@test.com",
                    strPWD = passwordHash,
                    str2FASecreto = secret,
                    bln2FAHabilitado = false,
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
            }

            if (!db.SegUsuario.Any(u => u.strNombre == "testuser_2fa_lockout"))
            {
                var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "testuser_2fa_lockout",
                    strCorreoElectronico = "tfalock@test.com",
                    strPWD = passwordHash,
                    str2FASecreto = secret,
                    bln2FAHabilitado = true,
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
            }

            await db.SaveChangesAsync();
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Login2fa_No2FA_Returns200WithToken()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_nofa", "Test1234$"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            content.Should().ContainKey("token");
            content!["token"].Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Login2fa_With2FA_Returns200WithRequires2fa()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa", "Test1234$"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            content.Should().ContainKey("requires_2fa");
            bool.Parse(content!["requires_2fa"].ToString()!).Should().BeTrue();
            content.Should().ContainKey("tempToken");
            content["tempToken"].ToString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Login2fa_InvalidCredentials_Returns401()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_nofa", "WrongPassword1$"));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Login2fa_EmptyCredentials_Returns400()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("", ""));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Login2fa_NonexistentUser_Returns401()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("nonexistent", "SomePass1$"));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Verify2fa_FiveWrongCodes_TriggersLockout()
        {
            var loginResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa_lockout", "Test1234$"));
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var loginContent = await loginResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var tempToken = loginContent!["tempToken"].ToString()!;

            for (int i = 0; i < 5; i++)
            {
                var verifyResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/verify",
                    new Login2faVerifyRequest(tempToken, "000000"));
                verifyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }

            var lockoutResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/verify",
                new Login2faVerifyRequest(tempToken, "111111"));
            lockoutResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var lockoutLogin = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa_lockout", "Test1234$"));
            lockoutLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Verify2fa_ValidCode_Returns200WithToken()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var usuario = db.SegUsuario.First(u => u.strNombre == "testuser_2fa");

            var loginResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa", "Test1234$"));
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var loginContent = await loginResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var tempToken = loginContent!["tempToken"].ToString()!;

            var totp = new Totp(Base32Encoding.ToBytes(usuario.str2FASecreto!), step: 30, totpSize: 6);
            var validCode = totp.ComputeTotp();

            var verifyResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/verify",
                new Login2faVerifyRequest(tempToken, validCode));

            verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var verifyContent = await verifyResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            verifyContent.Should().ContainKey("token");
            verifyContent!["token"].Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Verify2fa_InvalidCode_Returns401()
        {
            var loginResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa", "Test1234$"));
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var loginContent = await loginResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var tempToken = loginContent!["tempToken"].ToString()!;

            var verifyResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/verify",
                new Login2faVerifyRequest(tempToken, "000000"));

            verifyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Verify2fa_InvalidTempToken_Returns401()
        {
            var verifyResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/verify",
                new Login2faVerifyRequest("invalid-token-that-wont-work", "123456"));

            verifyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Verify2fa_EmptyFields_Returns400()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/verify",
                new Login2faVerifyRequest("", ""));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Verify2fa_UserWithout2FAEnabled_Returns401()
        {
            var loginResponse = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa_notverified", "Test1234$"));
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var loginContent = await loginResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            loginContent.Should().NotContainKey("requires_2fa", "user does not have 2FA enabled");
            loginContent.Should().ContainKey("token");
        }

        [Fact]
        public async Task Login2fa_With2FA_ReturnsTokenForUserWithout2FA()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/Login2fa/login",
                new Login2faRequest("testuser_2fa_notverified", "Test1234$"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            content.Should().ContainKey("token");
            content!["token"].Should().NotBeNullOrEmpty();
        }
    }
}
