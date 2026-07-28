namespace WebAPIDevSecOps.Interfaces;

public interface ITokenBlacklistService
{
    Task AddAsync(string jti, TimeSpan expiry);
    Task<bool> IsBlacklistedAsync(string jti);
}
