using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class FacturaService : IFacturaService
    {
        private static readonly ConcurrentDictionary<int, long> _folioCounters = new();
        private static readonly object _initLock = new();
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<FacturaService> _logger;

        public FacturaService(
            AppDbContext context,
            DbResilienceService dbResilience,
            IEventPublisher eventPublisher,
            ILogger<FacturaService> logger)
        {
            _context = context;
            _dbResilience = dbResilience;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<FacturaResponseDto> GenerarFacturaAsync(Guid pedidoId, string? rfc = null)
        {
            try
            {
                var pedido = await _context.Set<VenPedido>()
                    .Include(p => p.Detalles)
                    .FirstOrDefaultAsync(p => p.id == pedidoId);

                if (pedido == null)
                    throw new ArgumentException($"El pedido con ID {pedidoId} no existe.");

                if (pedido.strEstadoSaga != "Pagado")
                    throw new InvalidOperationException(
                        $"El pedido no está en estado Pagado (actual: {pedido.strEstadoSaga}).");

                var year = DateTime.UtcNow.Year;

                var counter = _folioCounters.GetOrAdd(year, y =>
                {
                    lock (_initLock)
                    {
                        return _context.Set<VenPedidoFactura>()
                            .Where(f => f.strFolioFactura.StartsWith($"F-{y}-"))
                            .Count();
                    }
                });

                var folioNum = Interlocked.Increment(ref counter);
                _folioCounters.TryUpdate(year, folioNum, counter - 1);

                var folio = $"F-{year}-{folioNum:D5}";

                var factura = new VenPedidoFactura
                {
                    idVenPedido = pedidoId,
                    strFolioFactura = folio,
                    strRFC = rfc,
                    decTotal = pedido.decTotal,
                    dteFechaEmision = DateTime.UtcNow,
                    strEstado = "Emitida",
                };

                _context.Set<VenPedidoFactura>().Add(factura);
                pedido.strEstadoSaga = "Facturado";

                await _dbResilience.SaveChangesAsync(_context);

                _logger.LogInformation("Factura generada: Pedido {PedidoId}, Folio {Folio}", pedidoId, folio);

                await _eventPublisher.PublishAsync(new FacturaGeneradoEvent
                {
                    PedidoId = pedidoId,
                    FolioFactura = folio,
                });

                return new FacturaResponseDto
                {
                    id = factura.id,
                    idVenPedido = factura.idVenPedido,
                    strFolioFactura = factura.strFolioFactura,
                    strRFC = factura.strRFC,
                    decTotal = factura.decTotal,
                    dteFechaEmision = factura.dteFechaEmision,
                    strEstado = factura.strEstado,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar factura para pedido {PedidoId}", pedidoId);

                try
                {
                    await _eventPublisher.PublishAsync(new FacturaRechazadaEvent
                    {
                        PedidoId = pedidoId,
                        Motivo = ex.Message,
                    });
                }
                catch (Exception pubEx)
                {
                    _logger.LogError(pubEx, "Error al publicar FacturaRechazadaEvent para pedido {PedidoId}", pedidoId);
                }

                throw;
            }
        }

        public async Task<FacturaResponseDto?> GetByIdAsync(int id)
        {
            return await _context.Set<VenPedidoFactura>()
                .AsNoTracking()
                .Where(f => f.id == id)
                .Select(f => new FacturaResponseDto
                {
                    id = f.id,
                    idVenPedido = f.idVenPedido,
                    strFolioFactura = f.strFolioFactura,
                    strRFC = f.strRFC,
                    decTotal = f.decTotal,
                    dteFechaEmision = f.dteFechaEmision,
                    strEstado = f.strEstado,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<List<FacturaResponseDto>> GetByPedidoIdAsync(Guid pedidoId)
        {
            return await _context.Set<VenPedidoFactura>()
                .AsNoTracking()
                .Where(f => f.idVenPedido == pedidoId)
                .OrderByDescending(f => f.dteFechaEmision)
                .Select(f => new FacturaResponseDto
                {
                    id = f.id,
                    idVenPedido = f.idVenPedido,
                    strFolioFactura = f.strFolioFactura,
                    strRFC = f.strRFC,
                    decTotal = f.decTotal,
                    dteFechaEmision = f.dteFechaEmision,
                    strEstado = f.strEstado,
                })
                .ToListAsync();
        }

        public async Task<bool> CancelarFacturaAsync(int facturaId)
        {
            var factura = await _context.Set<VenPedidoFactura>()
                .FirstOrDefaultAsync(f => f.id == facturaId);

            if (factura == null)
                return false;

            if (factura.strEstado != "Emitida")
                return false;

            factura.strEstado = "Cancelada";
            await _dbResilience.SaveChangesAsync(_context);

            _logger.LogInformation("Factura cancelada: FacturaId {FacturaId}, Folio {Folio}",
                facturaId, factura.strFolioFactura);

            return true;
        }
    }
}
