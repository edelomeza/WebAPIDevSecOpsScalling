using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Consumers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;

namespace UnitTest.Consumers
{
    public class FacturaConsumerTests
    {
        [Fact]
        public async Task Consume_GeneraFactura_ParaElPedidoDelEvento()
        {
            var pedidoId = Guid.NewGuid();
            var facturaServiceMock = new Mock<IFacturaService>();
            facturaServiceMock
                .Setup(s => s.GenerarFacturaAsync(pedidoId, It.IsAny<string?>()))
                .ReturnsAsync(new FacturaResponseDto { id = 1, idVenPedido = pedidoId });

            var loggerMock = new Mock<ILogger<FacturaConsumer>>();
            var ctx = new Mock<ConsumeContext<PagoProcesadoEvent>>();
            ctx.SetupGet(c => c.Message).Returns(new PagoProcesadoEvent
            {
                PedidoId = pedidoId,
                IdTransaccion = "TXN-123",
                Monto = 250m,
            });
            var consumer = new FacturaConsumer(facturaServiceMock.Object, loggerMock.Object);

            await consumer.Consume(ctx.Object);

            facturaServiceMock.Verify(s => s.GenerarFacturaAsync(pedidoId, It.IsAny<string?>()), Times.Once());
            LogVerifier.VerifyLog(loggerMock, LogLevel.Information, "Generando factura para pedido", Times.Once());
        }

        [Fact]
        public async Task Consume_ServicioLanzaExcepcion_PropagaAlBus()
        {
            var pedidoId = Guid.NewGuid();
            var facturaServiceMock = new Mock<IFacturaService>();
            facturaServiceMock
                .Setup(s => s.GenerarFacturaAsync(pedidoId, It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("El pedido no está en estado Pagado"));

            var loggerMock = new Mock<ILogger<FacturaConsumer>>();
            var ctx = new Mock<ConsumeContext<PagoProcesadoEvent>>();
            ctx.SetupGet(c => c.Message).Returns(new PagoProcesadoEvent { PedidoId = pedidoId });
            var consumer = new FacturaConsumer(facturaServiceMock.Object, loggerMock.Object);

            var act = () => consumer.Consume(ctx.Object);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("El pedido no está en estado Pagado");
            facturaServiceMock.Verify(s => s.GenerarFacturaAsync(pedidoId, It.IsAny<string?>()), Times.Once());
        }
    }
}
