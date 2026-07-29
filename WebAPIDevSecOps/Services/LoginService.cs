
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class LoginService : ILoginService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly DbResilienceService _dbResilience;
        private readonly ILogger<LoginService> _logger;
        private readonly IDistributedCache _cache;
        private readonly IMemoryCache _memoryCache;
        private static bool _redisHealthy = true;

        private const string FakeHash = "$argon2id$v=19$m=65536,t=3,p=1$KxY6z3Y9eG7EqJtq98hPqEX7nZaFWoOhiu7z8K7Z4Vwaki3P6KyHRxY6z3Y9eG";
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(30);

        public LoginService(AppDbContext context, IConfiguration configuration, IPasswordHasherService passwordHasher, DbResilienceService dbResilience, ILogger<LoginService> logger, IDistributedCache cache, IMemoryCache memoryCache)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
            _dbResilience = dbResilience;
            _logger = logger;
            _cache = cache;
            _memoryCache = memoryCache;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            var username = request.User.Trim();

            string? lockoutValue;
            try
            {
                lockoutValue = await _cache.GetStringAsync($"lockout:{username}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable for lockout check, falling back to memory cache");
                _redisHealthy = false;
                lockoutValue = _memoryCache.Get<string>($"lockout:{username}");
            }
            if (lockoutValue is not null)
            {
                throw new UnauthorizedAccessException("Cuenta bloqueada temporalmente por múltiples intentos fallidos. Intente de nuevo más tarde.");
            }

            var usuario = await _context.SegUsuario
                .Where(u => u.strNombre == username)
                .FirstOrDefaultAsync(ct);

            sw.Stop();
            _logger.LogInformation("[TIMING] DB query: {ElapsedMs}ms", sw.ElapsedMilliseconds);

            var hash = usuario?.strPWD ?? FakeHash;

            sw.Restart();
            var isValid = await Task.Run(() =>
                _passwordHasher.VerifyPassword(request.Password, hash), ct);
            sw.Stop();
            _logger.LogInformation("[TIMING] VerifyPassword: {ElapsedMs}ms", sw.ElapsedMilliseconds);

            if (usuario == null || !isValid)
            {
                await RecordFailedAttemptAsync(username, ct);
                throw new UnauthorizedAccessException("Credenciales inválidas.");
            }

            try
            {
                await _cache.RemoveAsync($"lockout:{username}", ct);
                await _cache.RemoveAsync($"attempts:{username}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable for cache cleanup, falling back to memory cache");
                _redisHealthy = false;
                _memoryCache.Remove($"lockout:{username}");
                _memoryCache.Remove($"attempts:{username}");
            }

            if (await Task.Run(() => _passwordHasher.NeedsRehash(usuario.strPWD), ct))
            {
                sw.Restart();
                usuario.strPWD = await Task.Run(() =>
                    _passwordHasher.HashPassword(request.Password), ct);
                await _dbResilience.SaveChangesAsync(_context, ct);
                sw.Stop();
                _logger.LogInformation("[TIMING] Rehash + Save: {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }

            var token = GenerateJwtToken(usuario.strNombre);

            return new LoginResponse { Token = token };
        }

        private async Task RecordFailedAttemptAsync(string username, CancellationToken ct)
        {
            var attemptsKey = $"attempts:{username}";
            var attempts = 1;

            string? currentValue;
            try
            {
                currentValue = await _cache.GetStringAsync(attemptsKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable for failed attempt check, falling back to memory cache");
                _redisHealthy = false;
                currentValue = _memoryCache.Get<string>(attemptsKey);
            }
            if (currentValue is not null && int.TryParse(currentValue, out var currentAttempts))
            {
                attempts = currentAttempts + 1;
            }

            if (attempts >= MaxFailedAttempts)
            {
                try
                {
                    await _cache.SetStringAsync($"lockout:{username}", "1", new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = LockoutDuration
                    }, ct);
                    await _cache.RemoveAsync(attemptsKey, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Redis unavailable for lockout set, falling back to memory cache");
                    _redisHealthy = false;
                    _memoryCache.Set($"lockout:{username}", "1", LockoutDuration);
                    _memoryCache.Remove(attemptsKey);
                }
            }
            else
            {
                try
                {
                    await _cache.SetStringAsync(attemptsKey, attempts.ToString(), new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = AttemptWindow
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Redis unavailable for attempt tracking, falling back to memory cache");
                    _redisHealthy = false;
                    _memoryCache.Set(attemptsKey, attempts.ToString(), AttemptWindow);
                }
            }
        }

        private string GenerateJwtToken(string username)
        {
            var secretKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key no configurada");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
