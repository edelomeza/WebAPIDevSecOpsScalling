using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;

namespace IntegrationTest.TwoFactor
{
    public class TwoFactorIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly string _adminToken;

        public TwoFactorIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
                builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
                builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"TwoFactorIntegrationDb_{Guid.NewGuid():N}");
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
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "admin",
                    strCorreoElectronico = "admin@test.com",
                    strPWD = "hash",
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
                await db.SaveChangesAsync();
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Setup_WithoutAuth_Returns401()
        {
            var response = await _client.PostAsync("/api/v1/auth/2fa/setup", null);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Setup_WithAuth_Returns200WithQrUri()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/setup");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadFromJsonAsync<TwoFactorSetupResponse>();
            content.Should().NotBeNull();
            content!.SharedKey.Should().NotBeNullOrEmpty();
            content.QrCodeUri.Should().StartWith("otpauth://totp/");
        }

        [Fact]
        public async Task Setup_WithExpiredToken_Returns401()
        {
            var expiredToken = UnitTest.Common.TokenHelper.GenerateExpiredToken(
                JwtTestConfig.Key, JwtTestConfig.Issuer, JwtTestConfig.Audience);

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/setup");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

            var response = await _client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Setup_Twice_Returns200WithNewSecret()
        {
            var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/setup");
            request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response1 = await _client.SendAsync(request1);
            response1.StatusCode.Should().Be(HttpStatusCode.OK);
            var content1 = await response1.Content.ReadFromJsonAsync<TwoFactorSetupResponse>();

            var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/2fa/setup");
            request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
            var response2 = await _client.SendAsync(request2);
            response2.StatusCode.Should().Be(HttpStatusCode.OK);
            var content2 = await response2.Content.ReadFromJsonAsync<TwoFactorSetupResponse>();

            content2!.SharedKey.Should().NotBe(content1!.SharedKey);
        }
    }
}
