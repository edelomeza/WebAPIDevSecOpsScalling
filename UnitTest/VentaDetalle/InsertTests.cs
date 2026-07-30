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
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Services;

namespace UnitTest.VentaDetalle
{
    public class InsertTests
    {
        private readonly DbResilienceService _dbResilience;

        public InsertTests()
        {
            _dbResilience = CreateDbResilience();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private VentaDetalleController CreateController(AppDbContext context)
        {
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns("Test User");
            return new VentaDetalleController(
                new VentaDetalleService(context, _dbResilience, userMock.Object));
        }

        private async Task<(AppDbContext context, int ventaId, int productoId, decimal precio)> SeedDependenciesAsync()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
            }
            var producto = new ProProducto { strNombreProducto = "Producto Test", intNumeroExistencia = 10, decPrecio = 149.99m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(producto);
            await context.SaveChangesAsync();

            var venta = new VenVenta { idCliCliente = cliente.id, idSegUsuario = usuario.id, idVenCatEstado = 1, dteFechaHoraCompra = DateTime.UtcNow, strClaveVenta = "CLV0000001", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.VenVenta.Add(venta);
            await context.SaveChangesAsync();

            return (context, venta.id, producto.id, producto.decPrecio);
        }

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteName()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            createdResult!.ActionName.Should().Be(nameof(VentaDetalleController.Get));
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteValues()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var routeValues = createdResult!.RouteValues;
            routeValues.Should().ContainKey("id");
            var idValue = routeValues["id"] as int?;
            idValue.Should().NotBeNull();
            idValue!.Value.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithIdGreaterThanZero()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as VenVentaDetalleDto;
            dtoResult!.id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Create_CalculatesDecTotalVentaCorrectly()
        {
            var (context, ventaId, productoId, precio) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var piezas = 5;
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = piezas };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as VenVentaDetalleDto;
            dtoResult!.intPiezaVenta.Should().Be(piezas);
            dtoResult.decTotalVenta.Should().Be(piezas * precio);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithProductoName()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as VenVentaDetalleDto;
            dtoResult!.strNombreProducto.Should().Be("Producto Test");
        }

        [Fact]
        public async Task Create_PersistsDetalleInDatabase()
        {
            var (context, ventaId, productoId, precio) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            await controller.Create(dto);

            context.Set<VenVentaDetalle>().Count().Should().Be(1);
            var saved = context.Set<VenVentaDetalle>().First();
            saved.idVenVenta.Should().Be(ventaId);
            saved.idProProducto.Should().Be(productoId);
            saved.intPiezaVenta.Should().Be(3);
            saved.decTotalVenta.Should().Be(3 * precio);
        }

        [Fact]
        public async Task Create_WithNonExistentVenta_ReturnsBadRequest()
        {
            var (context, _, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = 9999, idProProducto = productoId, intPiezaVenta = 1 };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = result.Result as BadRequestObjectResult;
            var error = badRequest!.Value;
            var mensaje = error!.GetType().GetProperty("mensaje")?.GetValue(error) as string;
            Assert.Equal("La venta especificada no existe.", mensaje);
        }

        [Fact]
        public async Task Create_WithNonExistentProducto_ReturnsBadRequest()
        {
            var (context, ventaId, _, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = 9999, intPiezaVenta = 1 };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = result.Result as BadRequestObjectResult;
            var error = badRequest!.Value;
            var mensaje = error!.GetType().GetProperty("mensaje")?.GetValue(error) as string;
            Assert.Equal("El producto especificado no existe.", mensaje);
        }

        [Fact]
        public async Task Create_WithInsufficientStock_ReturnsBadRequest()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 999 };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = result.Result as BadRequestObjectResult;
            var error = badRequest!.Value;
            var mensaje = error!.GetType().GetProperty("mensaje")?.GetValue(error) as string;
            Assert.Equal("El producto no tiene las suficientes existencias.", mensaje);
        }

        [Fact]
        public async Task Create_UpdatesProductoExistencias()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var controller = CreateController(context);
            var dto = new VenVentaDetalleCreateDto { idVenVenta = ventaId, idProProducto = productoId, intPiezaVenta = 3 };

            await controller.Create(dto);

            var producto = context.ProProducto.First();
            producto.intNumeroExistencia.Should().Be(7);
        }
    }
}
