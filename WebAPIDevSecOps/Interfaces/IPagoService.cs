using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface IPagoService
    {
        Task<PagoResponseDto> ProcesarPagoAsync(Guid pedidoId, string metodoPago, decimal monto);
        Task<bool> ReembolsarPagoAsync(int pagoId);
        Task<PagoResponseDto?> GetByIdAsync(int id);
        Task<List<PagoResponseDto>> GetByPedidoIdAsync(Guid pedidoId);
    }
}
