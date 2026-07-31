using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services;

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly IDistributedCache _cache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<TokenBlacklistService> _logger;

    public TokenBlacklistService(IDistributedCache cache, IMemoryCache memoryCache, ILogger<TokenBlacklistService> logger)
    {
        _cache = cache;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task AddAsync(string jti, TimeSpan expiry)
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            };
            await _cache.SetAsync($"blacklist:{jti}", [1], options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for token blacklist, falling back to memory cache");
            _memoryCache.Set($"blacklist:{jti}", new byte[] { 1 }, expiry);
        }
    }

    public async Task<bool> IsBlacklistedAsync(string jti)
    {
        try
        {
            var value = await _cache.GetAsync($"blacklist:{jti}");
            return value is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for blacklist check, falling back to memory cache");
            var value = _memoryCache.Get<byte[]>($"blacklist:{jti}");
            return value is not null;
        }
    }
}
