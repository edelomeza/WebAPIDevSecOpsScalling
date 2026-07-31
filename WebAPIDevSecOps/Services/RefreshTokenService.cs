using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly AppDbContext _context;
        private readonly TimeSpan _expiration = TimeSpan.FromDays(7);

        public RefreshTokenService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(string refreshToken, DateTime expiresAt)> GenerateTokenAsync(int usuarioId, CancellationToken ct)
        {
            var refreshToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var tokenHash = HashToken(refreshToken);
            var expiresAt = DateTime.UtcNow.Add(_expiration);

            _context.SegRefreshToken.Add(new SegRefreshToken
            {
                idSegUsuario = usuarioId,
                strTokenHash = tokenHash,
                dteExpiresAt = expiresAt,
                dteCreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);

            return (refreshToken, expiresAt);
        }

        public async Task<RefreshRotationResult?> ValidateAndRotateAsync(string refreshToken, CancellationToken ct)
        {
            var tokenHash = HashToken(refreshToken);

            var storedToken = await _context.SegRefreshToken
                .FirstOrDefaultAsync(t => t.strTokenHash == tokenHash, ct);

            if (storedToken is null)
                return null;

            if (storedToken.dteExpiresAt < DateTime.UtcNow)
                return null;

            if (storedToken.dteRevokedAt is not null)
                return null;

            var usuarioId = storedToken.idSegUsuario;
            var (newToken, newExpiresAt) = await GenerateTokenAsync(usuarioId, ct);

            storedToken.dteRevokedAt = DateTime.UtcNow;
            storedToken.strReplacedByTokenHash = HashToken(newToken);

            await _context.SaveChangesAsync(ct);

            return new RefreshRotationResult
            {
                UsuarioId = usuarioId,
                NewRefreshToken = newToken,
                ExpiresAt = newExpiresAt
            };
        }

        public async Task RevokeAllAsync(int usuarioId, CancellationToken ct)
        {
            var activeTokens = await _context.SegRefreshToken
                .Where(t => t.idSegUsuario == usuarioId && t.dteRevokedAt == null)
                .ToListAsync(ct);

            var now = DateTime.UtcNow;
            foreach (var token in activeTokens)
            {
                token.dteRevokedAt = now;
            }

            await _context.SaveChangesAsync(ct);
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
