using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Login
{
    public class LoginSecurityTests
    {
        private const string JwtKey = "z9WkJ4l2m9VQX1x8bYl+q3hR0Fz9uT7e5K2pL8sD4fA=";

        private static IConfiguration CreateConfig(bool withKey = true)
        {
            var settings = new Dictionary<string, string>();
            if (withKey)
            {
                settings["Jwt:Key"] = JwtKey;
                settings["Jwt:Issuer"] = "test";
                settings["Jwt:Audience"] = "test";
            }
            return new ConfigurationBuilder().AddInMemoryCollection(settings!).Build();
        }

        private static DbResilienceService CreateDbResilience()
        {
            return new DbResilienceService(
                Options.Create(new ResilienceOptions()),
                new Mock<ILogger<DbResilienceService>>().Object);
        }

        private static async Task<AppDbContext> CreateContextWithUser(string username = "admin")
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = username,
                strCorreoElectronico = "admin@test.com",
                strPWD = "$argon2id$v=19$m=16384,t=2,p=1$KxY6z3Y9eG7EqJtq98hPqEX7nZaFWoOhiu7z8K7Z4Vwaki3P6KyHRxY6z3Y9eG",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();
            return context;
        }

        private static (LoginService Service, Mock<IDistributedCache> Cache, Mock<ILogger<LoginService>> Logger, IMemoryCache Memory) BuildService(
            AppDbContext context, IConfiguration config, Mock<IPasswordHasherService> hasher)
        {
            var cache = new Mock<IDistributedCache>();
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);
            var logger = new Mock<ILogger<LoginService>>();
            var memory = new MemoryCache(new MemoryCacheOptions());
            var refresh = new Mock<IRefreshTokenService>();
            refresh.Setup(s => s.GenerateTokenAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(("refresh-token", DateTime.UtcNow.AddDays(7)));
            var service = new LoginService(context, config, hasher.Object, CreateDbResilience(), logger.Object, cache.Object, memory, refresh.Object);
            return (service, cache, logger, memory);
        }

        private static Mock<IPasswordHasherService> CreateHasher(bool valid = true)
        {
            var hasher = new Mock<IPasswordHasherService>();
            hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(valid);
            hasher.Setup(h => h.NeedsRehash(It.IsAny<string>())).Returns(false);
            return hasher;
        }

        [Fact]
        public async Task Login_CuentaBloqueadaEnRedis_LanzaExcepcion()
        {
            var context = await CreateContextWithUser();
            var (service, cache, _, _) = BuildService(context, CreateConfig(), CreateHasher());
            cache.Setup(c => c.GetAsync("lockout:admin", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes("1"));

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None));

            ex.Message.Should().Contain("bloqueada");
        }

        [Fact]
        public async Task Login_RedisCaido_UsaMemoriaParaLockout()
        {
            var context = await CreateContextWithUser();
            var (service, cache, _, memory) = BuildService(context, CreateConfig(), CreateHasher());
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            memory.Set("lockout:admin", "1");

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None));

            ex.Message.Should().Contain("bloqueada");
        }

        [Fact]
        public async Task Login_CincoIntentosFallidos_BloqueaCuenta()
        {
            var context = await CreateContextWithUser();
            var hasher = CreateHasher(valid: false);
            var (service, cache, _, _) = BuildService(context, CreateConfig(), hasher);
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) =>
                    key.Contains("attempts") ? Encoding.UTF8.GetBytes("4") : null);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("admin", "wrong"), CancellationToken.None));

            cache.Verify(c => c.SetAsync("lockout:admin",
                It.IsAny<byte[]>(),
                It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow != null),
                It.IsAny<CancellationToken>()), Times.Once());
            cache.Verify(c => c.RemoveAsync("attempts:admin", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task Login_RedisCaido_EnIntentoFallido_AcumulaEnMemoria()
        {
            var context = await CreateContextWithUser();
            var hasher = CreateHasher(valid: false);
            var (service, cache, _, memory) = BuildService(context, CreateConfig(), hasher);
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            memory.Set("attempts:admin", "2");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("admin", "wrong"), CancellationToken.None));

            memory.Get<string>("attempts:admin").Should().Be("3");
        }

        [Fact]
        public async Task Login_RedisCaido_AlRegistrarIntento_UsaMemoria()
        {
            var context = await CreateContextWithUser();
            var hasher = CreateHasher(valid: false);
            var (service, cache, _, memory) = BuildService(context, CreateConfig(), hasher);
            cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("admin", "wrong"), CancellationToken.None));

            memory.Get<string>("attempts:admin").Should().Be("1");
        }

        [Fact]
        public async Task Login_IntentoFallido_RegistraIntentosEnRedis()
        {
            var context = await CreateContextWithUser();
            var hasher = CreateHasher(valid: false);
            var (service, cache, _, _) = BuildService(context, CreateConfig(), hasher);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("admin", "wrong"), CancellationToken.None));

            cache.Verify(c => c.SetAsync("attempts:admin",
                Encoding.UTF8.GetBytes("1"),
                It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow != null),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task Login_Exitoso_LimpiaLockoutEIntentos()
        {
            var context = await CreateContextWithUser();
            var (service, cache, _, _) = BuildService(context, CreateConfig(), CreateHasher());

            await service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None);

            cache.Verify(c => c.RemoveAsync("lockout:admin", It.IsAny<CancellationToken>()), Times.Once());
            cache.Verify(c => c.RemoveAsync("attempts:admin", It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task Login_Exitoso_RedisCaido_LimpiaMemoria()
        {
            var context = await CreateContextWithUser();
            var (service, cache, _, memory) = BuildService(context, CreateConfig(), CreateHasher());
            cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            memory.Set("lockout:admin", "1");
            memory.Set("attempts:admin", "3");

            await service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None);

            memory.Get<string>("lockout:admin").Should().BeNull();
            memory.Get<string>("attempts:admin").Should().BeNull();
        }

        [Fact]
        public async Task Login_NecesitaRehash_ActualizaHashEnBD()
        {
            var context = await CreateContextWithUser();
            var hasher = CreateHasher();
            hasher.Setup(h => h.NeedsRehash(It.IsAny<string>())).Returns(true);
            hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("$argon2id$nuevo-hash");
            var (service, _, logger, _) = BuildService(context, CreateConfig(), hasher);

            await service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None);

            context.SegUsuario.First().strPWD.Should().Be("$argon2id$nuevo-hash");
            LogVerifier.VerifyLog(logger, LogLevel.Information, "[TIMING] Rehash + Save", Times.Once());
        }

        [Fact]
        public async Task Login_SinJwtKey_LanzaExcepcion()
        {
            var context = await CreateContextWithUser();
            var (service, _, _, _) = BuildService(context, CreateConfig(withKey: false), CreateHasher());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None));
        }

        [Fact]
        public async Task Login_Exitoso_TokenConIssuerYAudienceConfigurados()
        {
            var context = await CreateContextWithUser();
            var (service, _, _, _) = BuildService(context, CreateConfig(), CreateHasher());

            var response = await service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None);

            var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
            token.Issuer.Should().Be("test");
            token.Audiences.Should().Contain("test");
        }

        [Fact]
        public async Task Login_Exitoso_RegistraTiemposDeConsultaYVerificacion()
        {
            var context = await CreateContextWithUser();
            var (service, _, logger, _) = BuildService(context, CreateConfig(), CreateHasher());

            await service.LoginAsync(new LoginRequest("admin", "12345678"), CancellationToken.None);

            LogVerifier.VerifyLog(logger, LogLevel.Information, "[TIMING] DB query", Times.Once());
            LogVerifier.VerifyLog(logger, LogLevel.Information, "[TIMING] VerifyPassword", Times.Once());
        }
    }
}
