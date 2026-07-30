using MassTransit;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace WebAPIDevSecOps.Consumers
{
    public class StockValidatorConsumer : IConsumer<PedidoCreadoEvent>
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly ILogger<StockValidatorConsumer> _logger;

        public StockValidatorConsumer(AppDbContext context, DbResilienceService dbResilience, ILogger<StockValidatorConsumer> logger)
        {
            _context = context;
            _dbResilience = dbResilience;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<PedidoCreadoEvent> context)
        {
            var evento = context.Message;
            _logger.LogInformation("Validando stock para pedido {PedidoId}", evento.PedidoId);

            var productosSinStock = new List<int>();

            foreach (var detalle in evento.Detalles)
            {
                var producto = await _context.ProProducto
                    .FirstOrDefaultAsync(p => p.id == detalle.idProProducto);

                if (producto == null || producto.intNumeroExistencia < detalle.intCantidad)
                {
                    productosSinStock.Add(detalle.idProProducto);
                }
            }

            if (productosSinStock.Count == 0)
            {
                foreach (var detalle in evento.Detalles)
                {
                    var producto = await _context.ProProducto
                        .FirstAsync(p => p.id == detalle.idProProducto);
                    producto.intNumeroExistencia -= detalle.intCantidad;
                }

                var pedido = await _context.Set<VenPedido>()
                    .FirstOrDefaultAsync(p => p.id == evento.PedidoId);
                if (pedido != null)
                {
                    pedido.strEstadoSaga = "StockValidado";
                }

                await _dbResilience.SaveChangesAsync(_context);

                await context.Publish(new StockValidadoEvent
                {
                    PedidoId = evento.PedidoId,
                });
            }
            else
            {
                var pedido = await _context.Set<VenPedido>()
                    .FirstOrDefaultAsync(p => p.id == evento.PedidoId);
                if (pedido != null)
                {
                    pedido.strEstadoSaga = "StockRechazado";
                    pedido.strMotivoRechazo = $"Productos sin stock: {string.Join(", ", productosSinStock)}";
                }

                await _dbResilience.SaveChangesAsync(_context);

                await context.Publish(new StockRechazadoEvent
                {
                    PedidoId = evento.PedidoId,
                    Motivo = $"Productos sin stock: {string.Join(", ", productosSinStock)}",
                });
            }
        }
    }
}
