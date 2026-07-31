using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface IRefreshTokenService
    {
        Task<(string refreshToken, DateTime expiresAt)> GenerateTokenAsync(int usuarioId, CancellationToken ct);

        Task<RefreshRotationResult?> ValidateAndRotateAsync(string refreshToken, CancellationToken ct);

        Task RevokeAllAsync(int usuarioId, CancellationToken ct);
    }
}
