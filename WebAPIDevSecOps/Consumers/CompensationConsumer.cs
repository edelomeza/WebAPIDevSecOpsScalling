using MassTransit;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Consumers
{
    public class CompensationConsumer :
        IConsumer<PagoRechazadoEvent>,
        IConsumer<FacturaRechazadaEvent>
    {
        private readonly ICompensationService _compensationService;
        private readonly ILogger<CompensationConsumer> _logger;

        public CompensationConsumer(ICompensationService compensationService, ILogger<CompensationConsumer> logger)
        {
            _compensationService = compensationService;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<PagoRechazadoEvent> context)
        {
            var evento = context.Message;
            _logger.LogInformation("Compensando por pago rechazado: Pedido {PedidoId}", evento.PedidoId);

            await _compensationService.CompensarPorPagoRechazadoAsync(evento.PedidoId);
        }

        public async Task Consume(ConsumeContext<FacturaRechazadaEvent> context)
        {
            var evento = context.Message;
            _logger.LogInformation("Compensando por factura rechazada: Pedido {PedidoId}", evento.PedidoId);

            await _compensationService.CompensarPorFacturaRechazadaAsync(evento.PedidoId);
        }
    }
}
