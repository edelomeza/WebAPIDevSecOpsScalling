using MassTransit;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Consumers
{
    public class PagoConsumer : IConsumer<StockValidadoEvent>
    {
        private readonly IPagoService _pagoService;
        private readonly AppDbContext _context;
        private readonly ILogger<PagoConsumer> _logger;

        public PagoConsumer(IPagoService pagoService, AppDbContext context, ILogger<PagoConsumer> logger)
        {
            _pagoService = pagoService;
            _context = context;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<StockValidadoEvent> context)
        {
            var evento = context.Message;
            _logger.LogInformation("Procesando pago para pedido {PedidoId}", evento.PedidoId);

            var pedido = await _context.Set<VenPedido>()
                .FirstOrDefaultAsync(p => p.id == evento.PedidoId);

            if (pedido == null)
            {
                _logger.LogWarning("Pedido {PedidoId} no encontrado para procesar pago", evento.PedidoId);
                return;
            }

            await _pagoService.ProcesarPagoAsync(evento.PedidoId, "Tarjeta", pedido.decTotal);
        }
    }
}
