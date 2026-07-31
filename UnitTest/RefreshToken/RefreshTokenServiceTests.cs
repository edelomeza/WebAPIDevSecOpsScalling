using FluentAssertions;
using Microsoft.Extensions.Logging;
using UnitTest.Common;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.RefreshToken
{
    public class RefreshTokenServiceTests
    {
        [Fact]
        public async Task GenerateTokenAsync_CreatesTokenInDatabase_ReturnsTokenAndExpiry()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var (token, expiresAt) = await service.GenerateTokenAsync(1, CancellationToken.None);

            token.Should().NotBeNullOrEmpty();
            expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(5));

            var storedToken = context.SegRefreshToken.FirstOrDefault();
            storedToken.Should().NotBeNull();
            storedToken!.idSegUsuario.Should().Be(1);
            storedToken.dteExpiresAt.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(1));
            storedToken.dteRevokedAt.Should().BeNull();
            storedToken.strReplacedByTokenHash.Should().BeNull();
        }

        [Fact]
        public async Task GenerateTokenAsync_StoresHashNotPlaintext()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var (token, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            var storedToken = context.SegRefreshToken.First();
            storedToken.strTokenHash.Should().NotBe(token);
            storedToken.strTokenHash.Should().MatchRegex("^[a-f0-9]{64}$");
        }

        [Fact]
        public async Task ValidateAndRotateAsync_ValidToken_ReturnsUserIdAndRotates()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var (originalToken, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            var result = await service.ValidateAndRotateAsync(originalToken, CancellationToken.None);

            result.Should().NotBeNull();
            result!.UsuarioId.Should().Be(1);
            result.NewRefreshToken.Should().NotBeNullOrEmpty();
            result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

            var tokens = context.SegRefreshToken.ToList();
            tokens.Should().HaveCount(2);
            var oldToken = tokens.First(t => t.dteRevokedAt != null);
            oldToken.dteRevokedAt.Should().NotBeNull();
            oldToken.strReplacedByTokenHash.Should().NotBeNull();
        }

        [Fact]
        public async Task ValidateAndRotateAsync_ExpiredToken_ReturnsNull()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            context.SegRefreshToken.Add(new SegRefreshToken
            {
                idSegUsuario = 1,
                strTokenHash = "aa",
                dteExpiresAt = DateTime.UtcNow.AddDays(-1),
                dteCreatedAt = DateTime.UtcNow.AddDays(-8)
            });
            await context.SaveChangesAsync();

            var result = await service.ValidateAndRotateAsync("invalid-token", CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ValidateAndRotateAsync_RevokedToken_ReturnsNull()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var (token, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            await service.ValidateAndRotateAsync(token, CancellationToken.None);

            var result = await service.ValidateAndRotateAsync(token, CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task ValidateAndRotateAsync_NonExistentToken_ReturnsNull()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var result = await service.ValidateAndRotateAsync("nonexistent", CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public async Task RevokeAllAsync_RevokesAllActiveTokens()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            await service.GenerateTokenAsync(1, CancellationToken.None);
            await service.GenerateTokenAsync(1, CancellationToken.None);
            await service.GenerateTokenAsync(1, CancellationToken.None);

            await service.RevokeAllAsync(1, CancellationToken.None);

            var activeTokens = context.SegRefreshToken.Where(t => t.dteRevokedAt == null).ToList();
            activeTokens.Should().BeEmpty();
        }

        [Fact]
        public async Task RevokeAllAsync_OnlyRevokesTargetUserTokens()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "user1",
                strCorreoElectronico = "u1@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "user2",
                strCorreoElectronico = "u2@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            await service.GenerateTokenAsync(1, CancellationToken.None);
            await service.GenerateTokenAsync(2, CancellationToken.None);

            await service.RevokeAllAsync(1, CancellationToken.None);

            context.SegRefreshToken.Count(t => t.dteRevokedAt == null).Should().Be(1);
            context.SegRefreshToken.Count(t => t.dteRevokedAt != null).Should().Be(1);
        }

        [Fact]
        public async Task GenerateTokenAsync_MultipleTokensPerUser_AllowsMultiple()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            await service.GenerateTokenAsync(1, CancellationToken.None);
            await service.GenerateTokenAsync(1, CancellationToken.None);
            await service.GenerateTokenAsync(1, CancellationToken.None);

            context.SegRefreshToken.Count().Should().Be(3);
        }
    }
}
