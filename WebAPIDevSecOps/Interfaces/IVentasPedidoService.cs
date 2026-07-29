using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface IVentasPedidoService
    {
        Task<PedidoResponseDto> CrearPedidoAsync(PedidoCreateDto dto);
        Task<PedidoResponseDto?> GetByIdAsync(Guid id);
        Task<PagedResult<PedidoResponseDto>> GetAllAsync(QueryParams? queryParams = null);
    }
}