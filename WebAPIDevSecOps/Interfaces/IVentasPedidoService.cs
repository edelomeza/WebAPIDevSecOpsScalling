using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface IVentasPedidoService
    {
        Task<PedidoResponseDto> CrearPedidoAsync(PedidoCreateDto dto);
        Task<PedidoResponseDto?> GetByIdAsync(Guid id);
        Task<PagedResult<PedidoResponseDto>> GetAllAsync(QueryParams? queryParams = null);

        /// <summary>
        /// PostDespliegue9 (criterio 14): recupera pedidos "Pendiente" cuyo PublishAsync falló
        /// tras el commit (PUT 1->2). Recalcula el total desde la BD y republica exactamente un
        /// PedidoCreadoEvent. Solo opera sobre pedidos en "Pendiente" (idempotente).
        /// </summary>
        Task<PedidoResponseDto> RepublicarPendienteAsync(Guid id);

        /// <summary>
        /// Lista pedidos espejo/puros aún en "Pendiente" (candidatos a republicación).
        /// </summary>
        Task<IReadOnlyList<PedidoResponseDto>> GetPendientesAsync(int max = 50);
    }
}