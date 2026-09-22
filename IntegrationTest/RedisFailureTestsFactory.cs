using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using UnitTest.Common;

namespace IntegrationTest;

/// <summary>
/// PostDespliegue9 Fase 2 (reserva, SIN cablear): aislamiento del host para RedisFailureTests.
/// Si la instrumentación de Coverlet sigue exponiendo la carrera de hilos tras la Fase 1,
/// activar cambiando RedisFailureTests a IClassFixture&lt;RedisFailureTestsFactory&gt; y
/// eliminando su WithWebHostBuilder inline.
/// InMemoryDatabaseName se genera UNA vez en el constructor por instancia de fixture.
/// Sin IDisposable propio: WebApplicationFactory&lt;T&gt; ya implementa IDisposable/IAsyncDisposable
/// y xUnit libera TestServer + root provider al destruir el fixture.
/// </summary>
public class RedisFailureTestsFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName;

    public RedisFailureTestsFactory()
    {
        _dbName = $"RedisFailureDb_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=.;Database=Test;Trusted_Connection=True;");
        builder.UseSetting("Jwt:Key", JwtTestConfig.Key);
        builder.UseSetting("Jwt:Issuer", JwtTestConfig.Issuer);
        builder.UseSetting("Jwt:Audience", JwtTestConfig.Audience);
        builder.UseSetting("UseInMemoryDatabase", "true");
        builder.UseSetting("InMemoryDatabaseName", _dbName);

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDistributedCache));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IDistributedCache>(_ => new FailingDistributedCache());
            services.AddMemoryCache();
        });
    }

    private sealed class FailingDistributedCache : IDistributedCache
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
