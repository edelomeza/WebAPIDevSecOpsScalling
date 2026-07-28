
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    // El paso 1.11 migra LoginService de IMemoryCache a IDistributedCache.
    // Actualmente usa IMemoryCache para trackear intentos fallidos y lockout por usuario (líneas 21, 28).
    // Con IDistributedCache (Redis), el conteo de intentos fallidos y bloqueos será compartido entre todas las
    // instancias EC2, evitando que un atacante pueda reintentar por otra instancia.
    // Las claves Redis a usar (según sección 9 del plan):
    // - attempts:{user} — contador de intentos fallidos (TTL 30 min)
    // - lockout:{user} — flag de bloqueo (TTL 15 min)
    // Cambios necesarios:
    // 1. IMemoryCache _cache → IDistributedCache _cache
    // 2. AddMemoryCache() ya fue reemplazado por AddStackExchangeRedisCache() (paso 1.2)
    // 3. Usar SetStringAsync/GetStringAsync con DistributedCacheEntryOptions en lugar de Set/Get de IMemoryCache
    // 4. Serialización/deserialización manual de enteros (string → int)
    //    LoginService migrado de IMemoryCache a IDistributedCache:
    //Lockout check: TryGetValue → GetStringAsync
    //Failed attempts: contador serializado como string con int.TryParse
    //Removes: Remove → RemoveAsync
    //Sets: Set → SetStringAsync con DistributedCacheEntryOptions
    //RecordFailedAttempt renombrado a RecordFailedAttemptAsync(async)
    public class LoginService : ILoginService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly DbResilienceService _dbResilience;
        private readonly ILogger<LoginService> _logger;
        private readonly IDistributedCache _cache;

        private const string FakeHash = "$argon2id$v=19$m=65536,t=3,p=1$KxY6z3Y9eG7EqJtq98hPqEX7nZaFWoOhiu7z8K7Z4Vwaki3P6KyHRxY6z3Y9eG";
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(30);

        public LoginService(AppDbContext context, IConfiguration configuration, IPasswordHasherService passwordHasher, DbResilienceService dbResilience, ILogger<LoginService> logger, IDistributedCache cache)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
            _dbResilience = dbResilience;
            _logger = logger;
            _cache = cache;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();

            var username = request.User.Trim();

            var lockoutValue = await _cache.GetStringAsync($"lockout:{username}", ct);
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

            await _cache.RemoveAsync($"lockout:{username}", ct);
            await _cache.RemoveAsync($"attempts:{username}", ct);

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

            var currentValue = await _cache.GetStringAsync(attemptsKey, ct);
            if (currentValue is not null && int.TryParse(currentValue, out var currentAttempts))
            {
                attempts = currentAttempts + 1;
            }

            if (attempts >= MaxFailedAttempts)
            {
                await _cache.SetStringAsync($"lockout:{username}", "1", new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = LockoutDuration
                }, ct);
                await _cache.RemoveAsync(attemptsKey, ct);
            }
            else
            {
                await _cache.SetStringAsync(attemptsKey, attempts.ToString(), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = AttemptWindow
                }, ct);
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
