using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services;

public class CacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public CacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var bytes = await _cache.GetAsync(key);
        if (bytes is null) return default;
        return Deserialize<T>(bytes);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        var bytes = await _cache.GetAsync(key);
        if (bytes is not null)
        {
            var cached = Deserialize<T>(bytes);
            if (cached is not null) return cached;
        }

        var value = await factory();
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
        };
        await _cache.SetAsync(key, Serialize(value), options);
        return value;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
        };
        await _cache.SetAsync(key, Serialize(value), options);
    }

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    private static byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    }

    private static T? Deserialize<T>(byte[] bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
    }
}
