using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Consumers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Consumers
{
    public class StockValidatorConsumerTests
    {
        private readonly Mock<ILogger<StockValidatorConsumer>> _loggerMock;
        private readonly DbResilienceService _dbResilience;

        public StockValidatorConsumerTests()
        {
            _loggerMock = new Mock<ILogger<StockValidatorConsumer>>();
            _dbResilience = new DbResilienceService(
                Options.Create(new ResilienceOptions()),
                new Mock<ILogger<DbResilienceService>>().Object);
        }

        private static Mock<ConsumeContext<PedidoCreadoEvent>> CreateContext(PedidoCreadoEvent evento)
        {
            var ctx = new Mock<ConsumeContext<PedidoCreadoEvent>>();
            ctx.SetupGet(c => c.Message).Returns(evento);
            ctx.Setup(c => c.Publish(It.IsAny<StockValidadoEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            ctx.Setup(c => c.Publish(It.IsAny<StockRechazadoEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return ctx;
        }

        private static PedidoCreadoEvent CreateEvento(params (int ProductoId, int Cantidad)[] detalles)
        {
            return new PedidoCreadoEvent
            {
                PedidoId = Guid.NewGuid(),
                Detalles = detalles.Select(d => new PedidoCreadoDetalleItem
                {
                    idProProducto = d.ProductoId,
                    intCantidad = d.Cantidad,
                    decPrecioUnitario = 10m
                }).ToList()
            };
        }

        private static ProProducto CreateProducto(int id, int existencia)
        {
            return new ProProducto
            {
                id = id,
                strNombreProducto = $"Producto {id}",
                intNumeroExistencia = existencia,
                decPrecio = 10m,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
        }

        private static VenPedido CreatePedido(Guid id, decimal total)
        {
            return new VenPedido
            {
                id = id,
                decTotal = total,
                strEstadoSaga = "Creado",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
        }

        [Fact]
        public async Task Consume_StockSuficiente_DecrementaExistenciasYPideValidado()
        {
            var contextDb = DbContextMock.GetDbContext();
            contextDb.ProProducto.AddRange(
                CreateProducto(1, 10),
                CreateProducto(2, 5));
            var evento = CreateEvento((1, 3), (2, 2));
            contextDb.VenPedido.Add(CreatePedido(evento.PedidoId, 20m));
            await contextDb.SaveChangesAsync();

            var ctx = CreateContext(evento);
            var consumer = new StockValidatorConsumer(contextDb, _dbResilience, _loggerMock.Object);

            await consumer.Consume(ctx.Object);

            contextDb.ProProducto.First(p => p.id == 1).intNumeroExistencia.Should().Be(7);
            contextDb.ProProducto.First(p => p.id == 2).intNumeroExistencia.Should().Be(3);
            contextDb.VenPedido.First().strEstadoSaga.Should().Be("StockValidado");
            ctx.Verify(c => c.Publish(
                It.Is<StockValidadoEvent>(e => e.PedidoId == evento.PedidoId),
                It.IsAny<CancellationToken>()), Times.Once());
            ctx.Verify(c => c.Publish(It.IsAny<StockRechazadoEvent>(), It.IsAny<CancellationToken>()), Times.Never());
            LogVerifier.VerifyLog(_loggerMock, LogLevel.Information, "Validando stock para pedido", Times.Once());
        }

        [Fact]
        public async Task Consume_StockInsuficiente_RechazaPedidoConMotivo()
        {
            var contextDb = DbContextMock.GetDbContext();
            contextDb.ProProducto.Add(CreateProducto(1, 2));
            var evento = CreateEvento((1, 5));
            contextDb.VenPedido.Add(CreatePedido(evento.PedidoId, 10m));
            await contextDb.SaveChangesAsync();

            var ctx = CreateContext(evento);
            var consumer = new StockValidatorConsumer(contextDb, _dbResilience, _loggerMock.Object);

            await consumer.Consume(ctx.Object);

            contextDb.ProProducto.First(p => p.id == 1).intNumeroExistencia.Should().Be(2);
            contextDb.VenPedido.First().strEstadoSaga.Should().Be("StockRechazado");
            contextDb.VenPedido.First().strMotivoRechazo.Should().Be($"Productos sin stock: {1}");
            ctx.Verify(c => c.Publish(
                It.Is<StockRechazadoEvent>(e =>
                    e.PedidoId == evento.PedidoId &&
                    e.Motivo == $"Productos sin stock: {1}"),
                It.IsAny<CancellationToken>()), Times.Once());
            ctx.Verify(c => c.Publish(It.IsAny<StockValidadoEvent>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task Consume_ProductoNoExiste_RechazaPedido()
        {
            var contextDb = DbContextMock.GetDbContext();
            contextDb.ProProducto.Add(CreateProducto(1, 10));
            var evento = CreateEvento((1, 1), (999, 1));
            contextDb.VenPedido.Add(CreatePedido(evento.PedidoId, 10m));
            await contextDb.SaveChangesAsync();

            var ctx = CreateContext(evento);
            var consumer = new StockValidatorConsumer(contextDb, _dbResilience, _loggerMock.Object);

            await consumer.Consume(ctx.Object);

            contextDb.ProProducto.First(p => p.id == 1).intNumeroExistencia.Should().Be(10);
            ctx.Verify(c => c.Publish(It.IsAny<StockValidadoEvent>(), It.IsAny<CancellationToken>()), Times.Never());
            ctx.Verify(c => c.Publish(
                It.Is<StockRechazadoEvent>(e => e.Motivo.Contains("999")),
                It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task Consume_PedidoNoExiste_IgualPublicaEventoValidado()
        {
            var contextDb = DbContextMock.GetDbContext();
            contextDb.ProProducto.Add(CreateProducto(1, 10));
            await contextDb.SaveChangesAsync();

            var evento = CreateEvento((1, 1));
            var ctx = CreateContext(evento);
            var consumer = new StockValidatorConsumer(contextDb, _dbResilience, _loggerMock.Object);

            await consumer.Consume(ctx.Object);

            contextDb.ProProducto.First(p => p.id == 1).intNumeroExistencia.Should().Be(9);
            ctx.Verify(c => c.Publish(
                It.Is<StockValidadoEvent>(e => e.PedidoId == evento.PedidoId),
                It.IsAny<CancellationToken>()), Times.Once());
        }
    }
}
