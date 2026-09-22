using System.Collections.Concurrent;
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

        // Criterio 12 (concurrencia): serializa validación+decremento por producto dentro de la instancia.
        // Orden determinístico de adquisición (OrderBy id) para evitar deadlock entre pedidos.
        // Nota: en multi-instancia el guard es la transición Pendiente->X (idempotencia) + RowVersion.
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _productLocks = new();

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

            // Criterio 11 (idempotencia): redelivery o doble publish del finalize no reprocesa.
            var pedido = await _context.Set<VenPedido>()
                .FirstOrDefaultAsync(p => p.id == evento.PedidoId);
            if (pedido == null)
            {
                _logger.LogWarning("StockValidator: pedido {PedidoId} no encontrado; no-op", evento.PedidoId);
                return;
            }

            if (!string.Equals(pedido.strEstadoSaga, "Pendiente", StringComparison.Ordinal))
            {
                _logger.LogInformation("StockValidator: pedido {PedidoId} ya en {Estado}; redelivery no-op", evento.PedidoId, pedido.strEstadoSaga);
                return;
            }

            var productIds = evento.Detalles.Select(d => d.idProProducto).Distinct().OrderBy(id => id).ToList();
            var acquired = new List<SemaphoreSlim>(productIds.Count);
            try
            {
                foreach (var productId in productIds)
                {
                    var gate = _productLocks.GetOrAdd(productId, _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync();
                    acquired.Add(gate);
                }

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

                    pedido.strEstadoSaga = "StockValidado";

                    await _dbResilience.SaveChangesAsync(_context);

                    await context.Publish(new StockValidadoEvent
                    {
                        PedidoId = evento.PedidoId,
                    });
                }
                else
                {
                    pedido.strEstadoSaga = "StockRechazado";
                    pedido.strMotivoRechazo = $"Productos sin stock: {string.Join(", ", productosSinStock)}";

                    await _dbResilience.SaveChangesAsync(_context);

                    await context.Publish(new StockRechazadoEvent
                    {
                        PedidoId = evento.PedidoId,
                        Motivo = $"Productos sin stock: {string.Join(", ", productosSinStock)}",
                    });
                }
            }
            finally
            {
                foreach (var gate in acquired)
                {
                    gate.Release();
                }
            }
        }
    }
}
