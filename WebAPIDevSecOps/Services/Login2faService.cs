using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class Login2faService : ILogin2faService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly DbResilienceService _dbResilience;
        private readonly ILogger<Login2faService> _logger;
        private readonly IDistributedCache _cache;
        private readonly IMemoryCache _memoryCache;
        private readonly IRefreshTokenService _refreshTokenService;

        private const string FakeHash = "$argon2id$v=19$m=65536,t=3,p=1$KxY6z3Y9eG7EqJtq98hPqEX7nZaFWoOhiu7z8K7Z4Vwaki3P6KyHRxY6z3Y9eG";
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(30);

        public Login2faService(AppDbContext context, IConfiguration configuration, IPasswordHasherService passwordHasher, DbResilienceService dbResilience, ILogger<Login2faService> logger, IDistributedCache cache, IMemoryCache memoryCache, IRefreshTokenService refreshTokenService)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
            _dbResilience = dbResilience;
            _logger = logger;
            _cache = cache;
            _memoryCache = memoryCache;
            _refreshTokenService = refreshTokenService;
        }

        public async Task<Login2faResponse> Login2faAsync(Login2faRequest request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            var username = request.User.Trim();

            await CheckLockoutAsync(username, ct);

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

            await ClearAttemptsAsync(username, ct);

            if (await Task.Run(() => _passwordHasher.NeedsRehash(usuario.strPWD), ct))
            {
                sw.Restart();
                usuario.strPWD = await Task.Run(() =>
                    _passwordHasher.HashPassword(request.Password), ct);
                await _dbResilience.SaveChangesAsync(_context, ct);
                sw.Stop();
                _logger.LogInformation("[TIMING] Rehash + Save: {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }

            if (usuario.bln2FAHabilitado)
            {
                var tempToken = GenerateTempToken(usuario.strNombre);
                return new Login2faResponse
                {
                    Requires2fa = true,
                    TempToken = tempToken
                };
            }

            var token = GenerateJwtToken(usuario.strNombre);
            var (refreshToken, expiresAt) = await _refreshTokenService.GenerateTokenAsync(usuario.id, ct);
            return new Login2faResponse
            {
                Token = token,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                Requires2fa = false
            };
        }

        public async Task<Login2faVerifyResponse> Verify2faAsync(Login2faVerifyRequest request, CancellationToken ct)
        {
            ClaimsPrincipal principal;
            try
            {
                principal = ValidateTempToken(request.TempToken);
            }
            catch (Exception)
            {
                throw new UnauthorizedAccessException("Token temporal inválido o expirado.");
            }

            var username = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(username))
            {
                throw new UnauthorizedAccessException("Token temporal inválido.");
            }

            await CheckLockoutAsync(username, ct);

            var usuario = await _context.SegUsuario
                .Where(u => u.strNombre == username)
                .FirstOrDefaultAsync(ct);

            if (usuario is null)
            {
                throw new UnauthorizedAccessException("Usuario no encontrado.");
            }

            if (!usuario.bln2FAHabilitado)
            {
                throw new UnauthorizedAccessException("El 2FA no está habilitado para este usuario.");
            }

            if (string.IsNullOrEmpty(usuario.str2FASecreto))
            {
                throw new UnauthorizedAccessException("Debe configurar el 2FA primero usando /auth/2fa/setup.");
            }

            if (string.IsNullOrEmpty(request.Code) || request.Code.Length != 6 || !request.Code.All(char.IsDigit))
            {
                throw new UnauthorizedAccessException("El código TOTP debe tener exactamente 6 dígitos.");
            }

            var secretBytes = Base32Encoding.ToBytes(usuario.str2FASecreto);
            var totp = new Totp(secretBytes, step: 30, totpSize: 6);

            var isValid = totp.VerifyTotp(request.Code, out _, new VerificationWindow(previous: 1, future: 1));

            if (!isValid)
            {
                await RecordFailedAttemptAsync(username, ct);
                throw new UnauthorizedAccessException("Código TOTP inválido. Verifique el código e intente de nuevo.");
            }

            await ClearAttemptsAsync(username, ct);

            var token = GenerateJwtToken(usuario.strNombre);
            var (refreshToken, expiresAt) = await _refreshTokenService.GenerateTokenAsync(usuario.id, ct);
            return new Login2faVerifyResponse
            {
                Token = token,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt
            };
        }

        private async Task CheckLockoutAsync(string username, CancellationToken ct)
        {
            string? lockoutValue;
            try
            {
                lockoutValue = await _cache.GetStringAsync($"lockout:{username}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable for lockout check, falling back to memory cache");
                lockoutValue = _memoryCache.Get<string>($"lockout:{username}");
            }
            if (lockoutValue is not null)
            {
                throw new UnauthorizedAccessException("Cuenta bloqueada temporalmente por múltiples intentos fallidos. Intente de nuevo más tarde.");
            }
        }

        private async Task ClearAttemptsAsync(string username, CancellationToken ct)
        {
            try
            {
                await _cache.RemoveAsync($"lockout:{username}", ct);
                await _cache.RemoveAsync($"attempts:{username}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable for cache cleanup, falling back to memory cache");
                _memoryCache.Remove($"lockout:{username}");
                _memoryCache.Remove($"attempts:{username}");
            }
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
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
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

        private string GenerateTempToken(string username)
        {
            var secretKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key no configurada");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("2fa_temp", "true")
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private ClaimsPrincipal ValidateTempToken(string tempToken)
        {
            var secretKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key no configurada");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(tempToken, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out var validatedToken);

            var jwtToken = validatedToken as JwtSecurityToken;
            if (jwtToken == null || !jwtToken.Claims.Any(c => c.Type == "2fa_temp" && c.Value == "true"))
            {
                throw new SecurityTokenException("Token no es un token temporal 2FA válido.");
            }

            return principal;
        }
    }
}
