using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface IFacturaService
    {
        Task<FacturaResponseDto> GenerarFacturaAsync(Guid pedidoId, string? rfc = null);
        Task<bool> CancelarFacturaAsync(int facturaId);
    }
}
