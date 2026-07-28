// El paso crea la interfaz ITokenBlacklistService en Interfaces/ITokenBlacklistService.cs.
// Servirá para abstraer el blacklist de tokens JWT usando IDistributedCache (Redis)
// en lugar de la clase estática TokenBlacklist actual. Métodos esperados: AddAsync(jti, expiry)
// para agregar un token a la blacklist y IsBlacklistedAsync(jti) para verificar si está bloqueado.
// Esto permite que el blacklist sea compartido entre instancias EC2 al estar en Redis.
namespace WebAPIDevSecOps.Interfaces;

public interface ITokenBlacklistService
{
    Task AddAsync(string jti, TimeSpan expiry);
    Task<bool> IsBlacklistedAsync(string jti);
}
