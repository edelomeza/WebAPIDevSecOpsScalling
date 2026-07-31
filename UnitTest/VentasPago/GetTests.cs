using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.VentasPago
{
    public class GetTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<IEventPublisher> _eventPublisherMock;

        public GetTests()
        {
            _dbResilience = CreateDbResilience();
            _eventPublisherMock = new Mock<IEventPublisher>();
            _eventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<object>()))
                .Returns(Task.CompletedTask);
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private VentasPagoController CreateController(AppDbContext context)
        {
            return new VentasPagoController(
                new PagoService(context, _dbResilience, _eventPublisherMock.Object, new Mock<ILogger<PagoService>>().Object));
        }

        [Fact]
        public async Task GetById_ReturnsPago_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var pago = new VenPedidoPago
            {
                idVenPedido = pedidoId,
                decMonto = 199.98m,
                strMetodoPago = "Tarjeta",
                strIdTransaccion = "TXN-123",
                strEstado = "Completado",
                dteFechaPago = DateTime.UtcNow,
            };
            context.Set<VenPedidoPago>().Add(pago);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetById(pago.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as PagoResponseDto;

            dto!.id.Should().Be(pago.id);
            dto.idVenPedido.Should().Be(pedidoId);
            dto.decMonto.Should().Be(199.98m);
            dto.strEstado.Should().Be("Completado");
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WhenNotExists()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetById(999);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WithNegativeId()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetById(-1);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WithZeroId()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetById(0);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsCorrectPago_WhenMultiplePagosExist()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var pagos = new List<VenPedidoPago>
            {
                new() { idVenPedido = pedidoId, decMonto = 100m, strMetodoPago = "Tarjeta", strIdTransaccion = "TXN-001", strEstado = "Completado", dteFechaPago = DateTime.UtcNow },
                new() { idVenPedido = pedidoId, decMonto = 200m, strMetodoPago = "Efectivo", strIdTransaccion = "TXN-002", strEstado = "Rechazado", dteFechaPago = DateTime.UtcNow },
                new() { idVenPedido = pedidoId, decMonto = 300m, strMetodoPago = "Transferencia", strIdTransaccion = "TXN-003", strEstado = "Completado", dteFechaPago = DateTime.UtcNow },
            };
            context.Set<VenPedidoPago>().AddRange(pagos);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetById(pagos[1].id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as PagoResponseDto;

            dto!.id.Should().Be(pagos[1].id);
            dto.strEstado.Should().Be("Rechazado");
            dto.strIdTransaccion.Should().Be("TXN-002");
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsPagos_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var pagos = new List<VenPedidoPago>
            {
                new() { idVenPedido = pedidoId, decMonto = 100m, strMetodoPago = "Tarjeta", strIdTransaccion = "TXN-001", strEstado = "Completado", dteFechaPago = DateTime.UtcNow },
                new() { idVenPedido = pedidoId, decMonto = 200m, strMetodoPago = "Efectivo", strIdTransaccion = "TXN-002", strEstado = "Rechazado", dteFechaPago = DateTime.UtcNow },
            };
            context.Set<VenPedidoPago>().AddRange(pagos);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(pedidoId);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<PagoResponseDto>;

            dtoList.Should().HaveCount(2);
            dtoList.Should().Contain(p => p.strEstado == "Completado");
            dtoList.Should().Contain(p => p.strEstado == "Rechazado");
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsEmpty_WhenNoPagos()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(Guid.NewGuid());

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<PagoResponseDto>;

            dtoList.Should().BeEmpty();
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsOnlyPagosForGivenPedido()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoA = Guid.NewGuid();
            var pedidoB = Guid.NewGuid();
            context.Set<VenPedidoPago>().AddRange(
                new VenPedidoPago { idVenPedido = pedidoA, decMonto = 100m, strMetodoPago = "Tarjeta", strIdTransaccion = "TXN-A1", strEstado = "Completado", dteFechaPago = DateTime.UtcNow },
                new VenPedidoPago { idVenPedido = pedidoA, decMonto = 50m, strMetodoPago = "Efectivo", strIdTransaccion = "TXN-A2", strEstado = "Rechazado", dteFechaPago = DateTime.UtcNow },
                new VenPedidoPago { idVenPedido = pedidoB, decMonto = 300m, strMetodoPago = "Transferencia", strIdTransaccion = "TXN-B1", strEstado = "Completado", dteFechaPago = DateTime.UtcNow }
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(pedidoA);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<PagoResponseDto>;

            dtoList.Should().HaveCount(2);
            dtoList.Should().OnlyContain(p => p.idVenPedido == pedidoA);
        }

        [Fact]
        public async Task GetByPedidoId_EmptyGuid_ReturnsBadRequest()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(Guid.Empty);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsPagosOrderedByDateDescending()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            context.Set<VenPedidoPago>().AddRange(
                new VenPedidoPago { idVenPedido = pedidoId, decMonto = 100m, strMetodoPago = "Tarjeta", strIdTransaccion = "TXN-001", strEstado = "Completado", dteFechaPago = now.AddMinutes(-10) },
                new VenPedidoPago { idVenPedido = pedidoId, decMonto = 200m, strMetodoPago = "Efectivo", strIdTransaccion = "TXN-002", strEstado = "Completado", dteFechaPago = now },
                new VenPedidoPago { idVenPedido = pedidoId, decMonto = 150m, strMetodoPago = "Transferencia", strIdTransaccion = "TXN-003", strEstado = "Rechazado", dteFechaPago = now.AddMinutes(-5) }
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(pedidoId);

            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<PagoResponseDto>;

            dtoList![0].dteFechaPago.Should().Be(now);
            dtoList[1].dteFechaPago.Should().Be(now.AddMinutes(-5));
            dtoList[2].dteFechaPago.Should().Be(now.AddMinutes(-10));
        }
    }
}
