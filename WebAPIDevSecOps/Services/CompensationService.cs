using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class CompensationService : ICompensationService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly IPagoService _pagoService;
        private readonly ILogger<CompensationService> _logger;

        public CompensationService(
            AppDbContext context,
            DbResilienceService dbResilience,
            IPagoService pagoService,
            ILogger<CompensationService> logger)
        {
            _context = context;
            _dbResilience = dbResilience;
            _pagoService = pagoService;
            _logger = logger;
        }

        public async Task CompensarPorPagoRechazadoAsync(Guid pedidoId)
        {
            var pedido = await _context.Set<VenPedido>()
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.id == pedidoId);

            if (pedido == null)
            {
                _logger.LogWarning("Compensación por pago rechazado: pedido {PedidoId} no encontrado", pedidoId);
                return;
            }

            foreach (var detalle in pedido.Detalles)
            {
                var producto = await _context.Set<ProProducto>()
                    .FirstOrDefaultAsync(p => p.id == detalle.idProProducto);

                if (producto != null)
                {
                    producto.intNumeroExistencia += detalle.intCantidad;
                    _logger.LogInformation(
                        "Stock restaurado: Producto {ProductoId}, Cantidad {Cantidad}, Nuevo stock {Stock}",
                        detalle.idProProducto, detalle.intCantidad, producto.intNumeroExistencia);
                }
            }

            if (pedido.strEstadoSaga == "PagoRechazado")
            {
                pedido.strEstadoSaga = "CompensadoPago";
            }

            await _dbResilience.SaveChangesAsync(_context);
            _logger.LogInformation("Compensación por pago rechazado completada: Pedido {PedidoId}", pedidoId);
        }

        public async Task CompensarPorFacturaRechazadaAsync(Guid pedidoId)
        {
            await CompensarPorPagoRechazadoAsync(pedidoId);

            var pagos = await _context.Set<VenPedidoPago>()
                .Where(p => p.idVenPedido == pedidoId && p.strEstado == "Completado")
                .ToListAsync();

            foreach (var pago in pagos)
            {
                await _pagoService.ReembolsarPagoAsync(pago.id);
            }

            var pedido = await _context.Set<VenPedido>()
                .FirstOrDefaultAsync(p => p.id == pedidoId);

            if (pedido != null)
            {
                pedido.strEstadoSaga = "CompensadoFactura";
                await _dbResilience.SaveChangesAsync(_context);
            }

            _logger.LogInformation("Compensación por factura rechazada completada: Pedido {PedidoId}", pedidoId);
        }
    }
}
