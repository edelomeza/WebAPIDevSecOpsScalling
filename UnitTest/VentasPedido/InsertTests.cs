using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.VentasPedido
{
    public class InsertTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<IEventPublisher> _eventPublisherMock;

        public InsertTests()
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

        private async Task<(AppDbContext context, int clienteId, int productoId)> SeedBasicsAsync()
        {
            var context = DbContextMock.GetDbContext();

            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente Test",
                strCorreoElectronico = "cliente@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);

            var producto = new ProProducto
            {
                strNombreProducto = "Producto Test",
                intNumeroExistencia = 100,
                decPrecio = 99.99m,
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.ProProducto.Add(producto);

            await context.SaveChangesAsync();
            return (context, cliente.id, producto.id);
        }

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteName()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            createdResult!.ActionName.Should().Be(nameof(VentasPedidoController.GetById));
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteValues()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var routeValues = createdResult!.RouteValues;
            routeValues.Should().ContainKey("id");
            var idValue = routeValues["id"] as Guid?;
            idValue.Should().NotBeNull();
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectCliente()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as PedidoResponseDto;
            dtoResult!.idCliCliente.Should().Be(clienteId);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectTotal()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as PedidoResponseDto;
            dtoResult!.decTotal.Should().Be(199.98m);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithPendingState()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as PedidoResponseDto;
            dtoResult!.strEstadoSaga.Should().Be("Pendiente");
        }

        [Fact]
        public async Task Create_ReturnsDto_WithDetalles()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as PedidoResponseDto;
            dtoResult!.Detalles.Should().HaveCount(1);
            dtoResult.Detalles[0].idProProducto.Should().Be(productoId);
            dtoResult.Detalles[0].intCantidad.Should().Be(2);
            dtoResult.Detalles[0].decPrecioUnitario.Should().Be(99.99m);
        }

        [Fact]
        public async Task Create_PersistsPedidoInDatabase()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            await controller.Create(dto);

            context.Set<VenPedido>().Count().Should().Be(1);
            context.Set<VenPedidoDetalle>().Count().Should().Be(1);
        }

        [Fact]
        public async Task Create_PublishesEvent()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 2 },
                },
            };

            await controller.Create(dto);

            _eventPublisherMock.Verify(
                x => x.PublishAsync(It.IsAny<PedidoCreadoEvent>()),
                Times.Once);
        }

        [Fact]
        public async Task Create_NonExistentCliente_Returns400()
        {
            var (context, _, productoId) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = 9999,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 1 },
                },
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_NonExistentProducto_Returns400()
        {
            var (context, clienteId, _) = await SeedBasicsAsync();
            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = 9999, intCantidad = 1 },
                },
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_WithMultipleDetalles_CalculatesCorrectTotal()
        {
            var (context, clienteId, productoId) = await SeedBasicsAsync();
            var producto2 = new ProProducto
            {
                strNombreProducto = "Producto 2",
                intNumeroExistencia = 50,
                decPrecio = 49.99m,
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.ProProducto.Add(producto2);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new PedidoCreateDto
            {
                idCliCliente = clienteId,
                Detalles = new List<PedidoDetalleCreateDto>
                {
                    new() { idProProducto = productoId, intCantidad = 3 },
                    new() { idProProducto = producto2.id, intCantidad = 2 },
                },
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as PedidoResponseDto;
            dtoResult!.Detalles.Should().HaveCount(2);
            dtoResult.decTotal.Should().Be(3 * 99.99m + 2 * 49.99m);
        }
    }
}
