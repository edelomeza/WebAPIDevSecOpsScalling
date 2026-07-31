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

namespace UnitTest.VentasFactura
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

        private VentasFacturaController CreateController(AppDbContext context)
        {
            return new VentasFacturaController(
                new FacturaService(context, _dbResilience, _eventPublisherMock.Object, new Mock<ILogger<FacturaService>>().Object));
        }

        [Fact]
        public async Task GetById_ReturnsFactura_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var factura = new VenPedidoFactura
            {
                idVenPedido = pedidoId,
                strFolioFactura = "F-2026-00001",
                strRFC = "XAXX010101000",
                decTotal = 199.98m,
                dteFechaEmision = DateTime.UtcNow,
                strEstado = "Emitida",
            };
            context.Set<VenPedidoFactura>().Add(factura);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetById(factura.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as FacturaResponseDto;

            dto!.id.Should().Be(factura.id);
            dto.idVenPedido.Should().Be(pedidoId);
            dto.strFolioFactura.Should().Be("F-2026-00001");
            dto.strRFC.Should().Be("XAXX010101000");
            dto.decTotal.Should().Be(199.98m);
            dto.strEstado.Should().Be("Emitida");
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
        public async Task GetById_ReturnsCorrectFactura_WhenMultipleFacturasExist()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var facturas = new List<VenPedidoFactura>
            {
                new() { idVenPedido = pedidoId, strFolioFactura = "F-2026-00001", strRFC = "RFC001", decTotal = 100m, dteFechaEmision = DateTime.UtcNow, strEstado = "Emitida" },
                new() { idVenPedido = pedidoId, strFolioFactura = "F-2026-00002", strRFC = "RFC002", decTotal = 200m, dteFechaEmision = DateTime.UtcNow, strEstado = "Cancelada" },
                new() { idVenPedido = pedidoId, strFolioFactura = "F-2026-00003", strRFC = "RFC003", decTotal = 300m, dteFechaEmision = DateTime.UtcNow, strEstado = "Emitida" },
            };
            context.Set<VenPedidoFactura>().AddRange(facturas);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetById(facturas[1].id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as FacturaResponseDto;

            dto!.id.Should().Be(facturas[1].id);
            dto.strEstado.Should().Be("Cancelada");
            dto.strFolioFactura.Should().Be("F-2026-00002");
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsFacturas_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var facturas = new List<VenPedidoFactura>
            {
                new() { idVenPedido = pedidoId, strFolioFactura = "F-2026-00001", strRFC = "RFC001", decTotal = 100m, dteFechaEmision = DateTime.UtcNow, strEstado = "Emitida" },
                new() { idVenPedido = pedidoId, strFolioFactura = "F-2026-00002", strRFC = "RFC002", decTotal = 200m, dteFechaEmision = DateTime.UtcNow, strEstado = "Cancelada" },
            };
            context.Set<VenPedidoFactura>().AddRange(facturas);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(pedidoId);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<FacturaResponseDto>;

            dtoList.Should().HaveCount(2);
            dtoList.Should().Contain(f => f.strEstado == "Emitida");
            dtoList.Should().Contain(f => f.strEstado == "Cancelada");
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsEmpty_WhenNoFacturas()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(Guid.NewGuid());

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<FacturaResponseDto>;

            dtoList.Should().BeEmpty();
        }

        [Fact]
        public async Task GetByPedidoId_ReturnsOnlyFacturasForGivenPedido()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoA = Guid.NewGuid();
            var pedidoB = Guid.NewGuid();
            context.Set<VenPedidoFactura>().AddRange(
                new VenPedidoFactura { idVenPedido = pedidoA, strFolioFactura = "F-2026-00001", strRFC = "RFC-A1", decTotal = 100m, dteFechaEmision = DateTime.UtcNow, strEstado = "Emitida" },
                new VenPedidoFactura { idVenPedido = pedidoA, strFolioFactura = "F-2026-00002", strRFC = "RFC-A2", decTotal = 50m, dteFechaEmision = DateTime.UtcNow, strEstado = "Cancelada" },
                new VenPedidoFactura { idVenPedido = pedidoB, strFolioFactura = "F-2026-00003", strRFC = "RFC-B1", decTotal = 300m, dteFechaEmision = DateTime.UtcNow, strEstado = "Emitida" }
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(pedidoA);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<FacturaResponseDto>;

            dtoList.Should().HaveCount(2);
            dtoList.Should().OnlyContain(f => f.idVenPedido == pedidoA);
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
        public async Task GetByPedidoId_ReturnsFacturasOrderedByDateDescending()
        {
            var context = DbContextMock.GetDbContext();
            var pedidoId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            context.Set<VenPedidoFactura>().AddRange(
                new VenPedidoFactura { idVenPedido = pedidoId, strFolioFactura = "F-2026-00001", strRFC = "RFC001", decTotal = 100m, dteFechaEmision = now.AddMinutes(-10), strEstado = "Emitida" },
                new VenPedidoFactura { idVenPedido = pedidoId, strFolioFactura = "F-2026-00002", strRFC = "RFC002", decTotal = 200m, dteFechaEmision = now, strEstado = "Emitida" },
                new VenPedidoFactura { idVenPedido = pedidoId, strFolioFactura = "F-2026-00003", strRFC = "RFC003", decTotal = 150m, dteFechaEmision = now.AddMinutes(-5), strEstado = "Cancelada" }
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetByPedidoId(pedidoId);

            var okResult = result.Result as OkObjectResult;
            var dtoList = okResult!.Value as List<FacturaResponseDto>;

            dtoList![0].dteFechaEmision.Should().Be(now);
            dtoList[1].dteFechaEmision.Should().Be(now.AddMinutes(-5));
            dtoList[2].dteFechaEmision.Should().Be(now.AddMinutes(-10));
        }
    }
}
