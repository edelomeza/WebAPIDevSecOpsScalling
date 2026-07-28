using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.DependencyInjection;
using WebAPIDevSecOps.Interfaces;

namespace UnitTest.Common;

public static class BlacklistHelper
{
    public static async Task<string> GenerateAndBlacklistTokenAsync(IServiceProvider services, string key, string issuer, string audience)
    {
        var token = TokenHelper.GenerateValidToken(key, issuer, audience);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var jti = jwt?.Id;

        if (!string.IsNullOrEmpty(jti))
        {
            var blacklistService = services.GetRequiredService<ITokenBlacklistService>();
            await blacklistService.AddAsync(jti, TimeSpan.FromMinutes(60));
        }

        return token;
    }
}
