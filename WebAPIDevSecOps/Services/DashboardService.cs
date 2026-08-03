using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DashboardService> _logger;

        public DashboardService(AppDbContext context, ILogger<DashboardService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<DashboardDto> GetDashboardAsync()
        {
            var inicioHoy = DateTime.UtcNow.Date;
            var finHoy = inicioHoy.AddDays(1);

            var pedidosHoy = await _context.Set<VenPedido>()
                .AsNoTracking()
                .Where(p => p.dteFechaPedido >= inicioHoy && p.dteFechaPedido < finHoy)
                .ToListAsync();

            var pedidosPorEstado = await _context.Set<VenPedido>()
                .AsNoTracking()
                .GroupBy(p => p.strEstadoSaga)
                .Select(g => new EstadoSagaCountDto
                {
                    strEstadoSaga = g.Key,
                    intCantidad = g.Count(),
                })
                .OrderByDescending(e => e.intCantidad)
                .ToListAsync();

            var dashboard = new DashboardDto
            {
                intTotalPedidosHoy = pedidosHoy.Count,
                decVentasHoy = pedidosHoy
                    .Where(p => p.strEstadoSaga == "Facturado")
                    .Sum(p => p.decTotal),
                lstPedidosPorEstado = pedidosPorEstado,
                dctProfundidadColas = GetQueueDepth(),
            };

            return dashboard;
        }

        public async Task<SagaTimelineDto?> GetTimelineAsync(Guid id)
        {
            var pedido = await _context.Set<VenPedido>()
                .AsNoTracking()
                .Include(p => p.Pagos)
                .Include(p => p.Facturas)
                .FirstOrDefaultAsync(p => p.id == id);

            if (pedido == null)
                return null;

            var eventos = new List<SagaEventDto>
            {
                new()
                {
                    strEtapa = "PedidoCreado",
                    dteFecha = pedido.dteFechaPedido,
                    strEstado = "Pendiente",
                    strDetalle = $"Total del pedido: {pedido.decTotal:N2}",
                },
            };

            var estadosConStockValidado = new[]
            {
                "StockValidado", "Pagado", "PagoRechazado", "Facturado",
                "CompensadoPago", "CompensadoFactura",
            };

            if (pedido.strEstadoSaga == "StockRechazado")
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "StockRechazado",
                    dteFecha = null,
                    strEstado = pedido.strEstadoSaga,
                    strDetalle = pedido.strMotivoRechazo,
                });
            }
            else if (estadosConStockValidado.Contains(pedido.strEstadoSaga))
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "StockValidado",
                    dteFecha = null,
                    strEstado = "StockValidado",
                    strDetalle = "Stock descontado (sin timestamp persistido)",
                });
            }

            if (pedido.Pagos is not null)
            {
                foreach (var pago in pedido.Pagos.OrderBy(p => p.dteFechaPago))
                {
                    eventos.Add(new SagaEventDto
                    {
                        strEtapa = pago.strEstado switch
                        {
                            "Reembolsado" => "ReembolsoPago",
                            "Rechazado" => "PagoRechazado",
                            _ => "PagoProcesado",
                        },
                        dteFecha = pago.dteFechaPago,
                        strEstado = pago.strEstado,
                        strDetalle = $"Monto: {pago.decMonto:N2}; Transacción: {pago.strIdTransaccion ?? "N/A"}",
                    });
                }
            }

            if (pedido.Facturas is not null)
            {
                foreach (var factura in pedido.Facturas.OrderBy(f => f.dteFechaEmision))
                {
                    eventos.Add(new SagaEventDto
                    {
                        strEtapa = "FacturaGenerada",
                        dteFecha = factura.dteFechaEmision,
                        strEstado = factura.strEstado,
                        strDetalle = $"Folio: {factura.strFolioFactura}",
                    });
                }
            }

            if (pedido.strEstadoSaga == "CompensadoPago")
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "CompensacionPago",
                    dteFecha = null,
                    strEstado = pedido.strEstadoSaga,
                    strDetalle = "Liberar stock descontado",
                });
            }
            else if (pedido.strEstadoSaga == "CompensadoFactura")
            {
                eventos.Add(new SagaEventDto
                {
                    strEtapa = "CompensacionFactura",
                    dteFecha = null,
                    strEstado = pedido.strEstadoSaga,
                    strDetalle = "Reembolso de pago + liberar stock",
                });
            }

            return new SagaTimelineDto
            {
                id = pedido.id,
                strEstadoSaga = pedido.strEstadoSaga,
                strMotivoRechazo = pedido.strMotivoRechazo,
                lstEventos = eventos,
            };
        }

        private Dictionary<string, int> GetQueueDepth()
        {
            _logger.LogWarning(
                "Profundidad de colas no disponible: el transporte actual es MassTransit InMemory. " +
                "Con el transporte AmazonSQS (paso 2.11) se consultará ApproximateNumberOfMessages por cola.");
            return new Dictionary<string, int>();
        }
    }
}
