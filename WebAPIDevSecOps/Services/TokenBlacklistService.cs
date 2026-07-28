using Microsoft.Extensions.Caching.Distributed;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services;

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly IDistributedCache _cache;

    public TokenBlacklistService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task AddAsync(string jti, TimeSpan expiry)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry
        };
        await _cache.SetAsync($"blacklist:{jti}", [1], options);
    }

    public async Task<bool> IsBlacklistedAsync(string jti)
    {
        var value = await _cache.GetAsync($"blacklist:{jti}");
        return value is not null;
    }
}
