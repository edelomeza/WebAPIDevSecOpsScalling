using MassTransit;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Consumers
{
    public class FacturaConsumer : IConsumer<PagoProcesadoEvent>
    {
        private readonly IFacturaService _facturaService;
        private readonly ILogger<FacturaConsumer> _logger;

        public FacturaConsumer(IFacturaService facturaService, ILogger<FacturaConsumer> logger)
        {
            _facturaService = facturaService;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<PagoProcesadoEvent> context)
        {
            var evento = context.Message;
            _logger.LogInformation("Generando factura para pedido {PedidoId}", evento.PedidoId);

            await _facturaService.GenerarFacturaAsync(evento.PedidoId);
        }
    }
}
