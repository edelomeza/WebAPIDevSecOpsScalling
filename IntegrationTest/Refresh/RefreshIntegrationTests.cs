using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace IntegrationTest.Refresh
{
    public class RefreshIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public RefreshIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
                builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
                builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
                builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("InMemoryDatabaseName", $"RefreshIntegrationDb_{Guid.NewGuid():N}");
            });
            _client = _factory.CreateClient();
        }

        public async Task InitializeAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.SegUsuario.Any())
            {
                db.SegUsuario.Add(new SegUsuario
                {
                    strNombre = "testuser",
                    strCorreoElectronico = "test@test.com",
                    strPWD = "hash",
                    RowVersion = new byte[] { 1, 0, 0, 0 }
                });
                await db.SaveChangesAsync();
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task Refresh_WithInvalidToken_Returns401()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/refresh/refresh",
                new RefreshRequest("invalid-token"));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Refresh_WithEmptyToken_Returns400()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/refresh/refresh",
                new RefreshRequest(""));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Refresh_WithNullToken_Returns400()
        {
            var response = await _client.PostAsJsonAsync("/api/v1/refresh/refresh",
                new { refreshToken = (string?)null });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Refresh_ValidToken_Returns200WithNewTokens()
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var (refreshToken, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            var response = await _client.PostAsJsonAsync("/api/v1/refresh/refresh",
                new RefreshRequest(refreshToken));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadFromJsonAsync<RefreshResponse>();
            content.Should().NotBeNull();
            content!.Token.Should().NotBeNullOrEmpty();
            content.RefreshToken.Should().NotBeNullOrEmpty();
            content.RefreshToken.Should().NotBe(refreshToken);
            content.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }

        [Fact]
        public async Task Refresh_RevokedToken_Returns401()
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var (refreshToken, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            await service.ValidateAndRotateAsync(refreshToken, CancellationToken.None);

            var response = await _client.PostAsJsonAsync("/api/v1/refresh/refresh",
                new RefreshRequest(refreshToken));

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Refresh_MultipleRequests_NotRateLimited()
        {
            for (int i = 0; i < 5; i++)
            {
                var response = await _client.PostAsJsonAsync("/api/v1/refresh/refresh",
                    new RefreshRequest("some-token"));
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
        }
    }
}
