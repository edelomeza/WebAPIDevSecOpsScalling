using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Consumers;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace UnitTest.Consumers
{
    public class PagoConsumerTests
    {
        [Fact]
        public async Task Consume_PedidoEncontrado_ProcesaPago()
        {
            var contextDb = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            contextDb.VenPedido.Add(new VenPedido
            {
                id = pedidoId,
                decTotal = 250m,
                strEstadoSaga = "StockValidado",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await contextDb.SaveChangesAsync();

            var pagoServiceMock = new Mock<IPagoService>();
            pagoServiceMock.Setup(s => s.ProcesarPagoAsync(pedidoId, "Tarjeta", 250m))
                .ReturnsAsync(new WebAPIDevSecOps.Dto.PagoResponseDto());

            var loggerMock = new Mock<ILogger<PagoConsumer>>();
            var ctx = new Mock<ConsumeContext<StockValidadoEvent>>();
            ctx.SetupGet(c => c.Message).Returns(new StockValidadoEvent { PedidoId = pedidoId });
            var consumer = new PagoConsumer(pagoServiceMock.Object, contextDb, loggerMock.Object);

            await consumer.Consume(ctx.Object);

            pagoServiceMock.Verify(s => s.ProcesarPagoAsync(pedidoId, "Tarjeta", 250m), Times.Once());
            LogVerifier.VerifyLog(loggerMock, LogLevel.Information, "Procesando pago para pedido", Times.Once());
        }

        [Fact]
        public async Task Consume_PedidoNoEncontrado_NoProcesaPago()
        {
            var contextDb = DbContextMock.GetDbContext();
            var pagoServiceMock = new Mock<IPagoService>();
            var loggerMock = new Mock<ILogger<PagoConsumer>>();
            var ctx = new Mock<ConsumeContext<StockValidadoEvent>>();
            ctx.SetupGet(c => c.Message).Returns(new StockValidadoEvent { PedidoId = Guid.NewGuid() });
            var consumer = new PagoConsumer(pagoServiceMock.Object, contextDb, loggerMock.Object);

            await consumer.Consume(ctx.Object);

            pagoServiceMock.Verify(s => s.ProcesarPagoAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>()), Times.Never());
            LogVerifier.VerifyLog(loggerMock, LogLevel.Warning, "no encontrado para procesar pago", Times.Once());
        }
    }
}
