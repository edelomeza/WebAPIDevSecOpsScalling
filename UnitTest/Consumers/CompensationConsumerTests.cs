using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Consumers;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;

namespace UnitTest.Consumers
{
    public class CompensationConsumerTests
    {
        [Fact]
        public async Task Consume_PagoRechazado_LlamaCompensacionPorPago()
        {
            var pedidoId = Guid.NewGuid();
            var compensationServiceMock = new Mock<ICompensationService>();
            compensationServiceMock
                .Setup(s => s.CompensarPorPagoRechazadoAsync(pedidoId))
                .Returns(Task.CompletedTask);

            var loggerMock = new Mock<ILogger<CompensationConsumer>>();
            var ctx = new Mock<ConsumeContext<PagoRechazadoEvent>>();
            ctx.SetupGet(c => c.Message).Returns(new PagoRechazadoEvent
            {
                PedidoId = pedidoId,
                Motivo = "Fondos insuficientes",
            });
            var consumer = new CompensationConsumer(compensationServiceMock.Object, loggerMock.Object);

            await consumer.Consume(ctx.Object);

            compensationServiceMock.Verify(s => s.CompensarPorPagoRechazadoAsync(pedidoId), Times.Once());
            LogVerifier.VerifyLog(loggerMock, LogLevel.Information, "Compensando por pago rechazado: Pedido", Times.Once());
        }

        [Fact]
        public async Task Consume_FacturaRechazada_LlamaCompensacionPorFactura()
        {
            var pedidoId = Guid.NewGuid();
            var compensationServiceMock = new Mock<ICompensationService>();
            compensationServiceMock
                .Setup(s => s.CompensarPorFacturaRechazadaAsync(pedidoId))
                .Returns(Task.CompletedTask);

            var loggerMock = new Mock<ILogger<CompensationConsumer>>();
            var ctx = new Mock<ConsumeContext<FacturaRechazadaEvent>>();
            ctx.SetupGet(c => c.Message).Returns(new FacturaRechazadaEvent
            {
                PedidoId = pedidoId,
                Motivo = "Error al generar folio",
            });
            var consumer = new CompensationConsumer(compensationServiceMock.Object, loggerMock.Object);

            await consumer.Consume(ctx.Object);

            compensationServiceMock.Verify(s => s.CompensarPorFacturaRechazadaAsync(pedidoId), Times.Once());
            LogVerifier.VerifyLog(loggerMock, LogLevel.Information, "Compensando por factura rechazada: Pedido", Times.Once());
        }
    }
}
