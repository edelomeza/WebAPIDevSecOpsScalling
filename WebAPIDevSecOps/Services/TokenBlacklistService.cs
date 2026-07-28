// El paso 1.5 crea la implementación TokenBlacklistService en Services/TokenBlacklistService.cs.
// Implementa ITokenBlacklistService usando IDistributedCache (Redis) con clave blacklist:{jti}.
// AddAsync guarda el JTI en Redis con TTL = lo que falte para expirar el token;
// IsBlacklistedAsync verifica si existe. Reemplazará a la clase estática TokenBlacklist actual.
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
