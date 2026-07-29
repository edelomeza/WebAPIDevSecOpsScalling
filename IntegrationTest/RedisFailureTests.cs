using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Models;
using UnitTest.Common;

namespace IntegrationTest;

public class RedisFailureTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;

    public RedisFailureTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
            builder.UseSetting("Jwt:Key", "01123581321345589144233377610987");
            builder.UseSetting("Jwt:Issuer", "edelmeza.com");
            builder.UseSetting("Jwt:Audience", "edelmeza.com");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", $"RedisFailureDb_{Guid.NewGuid():N}");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDistributedCache));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddSingleton<IDistributedCache>(_ => new FailingDistributedCache());
                services.AddMemoryCache();
            });
        });
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SegUsuario.Add(new SegUsuario
        {
            strNombre = "testuser",
            strPWD = BCrypt.Net.BCrypt.HashPassword("TestPass123!"),
            strCorreoElectronico = "testuser@test.com",
            RowVersion = new byte[] { 1 }
        });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_Should_Succeed_When_Redis_Is_Down()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/login/login", new
        {
            User = "testuser",
            Password = "TestPass123!"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(content);
        Assert.False(string.IsNullOrWhiteSpace(content.token));
    }

    [Fact]
    public async Task Login_Should_Reject_Invalid_Credentials_When_Redis_Is_Down()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/login/login", new
        {
            User = "testuser",
            Password = "WrongPass1!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private class LoginResponse
    {
        public string token { get; set; } = "";
    }

    private class FailingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Simulated Redis connection failure");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("Simulated Redis connection failure");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => throw new InvalidOperationException("Simulated Redis connection failure");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => throw new InvalidOperationException("Simulated Redis connection failure");
        public void Refresh(string key) => throw new InvalidOperationException("Simulated Redis connection failure");
        public Task RefreshAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("Simulated Redis connection failure");
        public void Remove(string key) => throw new InvalidOperationException("Simulated Redis connection failure");
        public Task RemoveAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("Simulated Redis connection failure");
    }
}
