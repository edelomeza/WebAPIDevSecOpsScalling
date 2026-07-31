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

namespace UnitTest.VentasPedido
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

        private VentasPedidoController CreateController(AppDbContext context)
        {
            return new VentasPedidoController(
                new VentasPedidoService(context, _dbResilience, _eventPublisherMock.Object));
        }

        [Fact]
        public async Task GetAll_ReturnsEmptyPagedResult_WhenNoPedidos()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<PedidoResponseDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(0);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.TotalPages.Should().Be(0);
        }

        [Fact]
        public async Task GetAll_ReturnsPagedResult_WithDefaultPagination()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente",
                strCorreoElectronico = "c@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            context.Set<VenPedido>().AddRange(TestDataFactory.CreatePedidos(5, cliente.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<PedidoResponseDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.TotalPages.Should().Be(1);
        }

        [Fact]
        public async Task GetAll_ReturnsCorrectPageSize()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente",
                strCorreoElectronico = "c@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            context.Set<VenPedido>().AddRange(TestDataFactory.CreatePedidos(10, cliente.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageSize = 3 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<PedidoResponseDto>;

            pagedResult!.Items.Should().HaveCount(3);
            pagedResult.TotalCount.Should().Be(10);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(3);
            pagedResult.TotalPages.Should().Be(4);
        }

        [Fact]
        public async Task GetAll_WithNullQueryParams_UsesDefaults()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente",
                strCorreoElectronico = "c@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            context.Set<VenPedido>().AddRange(TestDataFactory.CreatePedidos(3, cliente.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(null);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<PedidoResponseDto>;

            pagedResult!.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.Items.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetById_ReturnsPedido_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente",
                strCorreoElectronico = "c@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            var pedidoId = Guid.NewGuid();
            var pedido = TestDataFactory.CreatePedido(pedidoId, cliente.id);
            context.Set<VenPedido>().Add(pedido);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetById(pedidoId);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as PedidoResponseDto;

            dto!.id.Should().Be(pedidoId);
            dto.strEstadoSaga.Should().Be("Pendiente");
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WhenNotExists()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetById(Guid.NewGuid());

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsCorrectPedido_WhenMultiplePedidosExist()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente",
                strCorreoElectronico = "c@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            context.Set<VenPedido>().AddRange(TestDataFactory.CreatePedidos(5, cliente.id));
            await context.SaveChangesAsync();

            var target = context.Set<VenPedido>().Skip(2).First();

            var controller = CreateController(context);

            var result = await controller.GetById(target.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as PedidoResponseDto;

            dto!.id.Should().Be(target.id);
        }
    }
}
