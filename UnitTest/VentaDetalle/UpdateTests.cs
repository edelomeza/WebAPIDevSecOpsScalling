using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

namespace UnitTest.VentaDetalle
{
    public class UpdateTests
    {
        private readonly DbResilienceService _dbResilience;

        public UpdateTests()
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

        private async Task<VenVentaDetalle> SeedDetalleAsync(AppDbContext context, int ventaId, int productoId, int piezas = 2)
        {
            var precio = context.ProProducto.First(p => p.id == productoId).decPrecio;
            var detalle = new VenVentaDetalle
            {
                idVenVenta = ventaId,
                idProProducto = productoId,
                intPiezaVenta = piezas,
                decTotalVenta = piezas * precio,
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.Set<VenVentaDetalle>().Add(detalle);
            await context.SaveChangesAsync();
            return detalle;
        }

        [Fact]
        public async Task Update_ReturnsNoContent_WhenSuccessful()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId);

            var result = await controller.Update(detalle.id, dto);

            result.Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task Update_ChangesProducto_InDatabase()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var otroProducto = new ProProducto { strNombreProducto = "Otro Producto", intNumeroExistencia = 5, decPrecio = 299.99m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(otroProducto);
            await context.SaveChangesAsync();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, otroProducto.id);

            await controller.Update(detalle.id, dto);

            var updated = context.Set<VenVentaDetalle>().First();
            updated.idProProducto.Should().Be(otroProducto.id);
        }

        [Fact]
        public async Task Update_ChangesPiezas_InDatabase()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, piezas: 10);

            await controller.Update(detalle.id, dto);

            var updated = context.Set<VenVentaDetalle>().First();
            updated.intPiezaVenta.Should().Be(10);
        }

        [Fact]
        public async Task Update_RecalculatesTotalVenta_WhenProductoChanges()
        {
            var (context, ventaId, productoId, precioOriginal) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var otroProducto = new ProProducto { strNombreProducto = "Otro Producto", intNumeroExistencia = 5, decPrecio = 299.99m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(otroProducto);
            await context.SaveChangesAsync();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, otroProducto.id, piezas: 2);

            await controller.Update(detalle.id, dto);

            var updated = context.Set<VenVentaDetalle>().First();
            updated.decTotalVenta.Should().Be(2 * otroProducto.decPrecio);
        }

        [Fact]
        public async Task Update_RecalculatesTotalVenta_WhenPiezasChange()
        {
            var (context, ventaId, productoId, precioOriginal) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, piezas: 7);

            await controller.Update(detalle.id, dto);

            var updated = context.Set<VenVentaDetalle>().First();
            updated.decTotalVenta.Should().Be(7 * precioOriginal);
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenIdMismatch()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(999, ventaId, productoId);

            var result = await controller.Update(detalle.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsNotFound_WhenDetalleDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(999, 1, 1);

            var result = await controller.Update(999, dto);

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenVentaDoesNotExist()
        {
            var (context, _, productoId, _) = await SeedDependenciesAsync();
            var ventaIdValido = context.VenVenta.First().id;
            var productoIdValido = context.ProProducto.First().id;
            var detalle = await SeedDetalleAsync(context, ventaIdValido, productoIdValido);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, 9999, productoIdValido);

            var result = await controller.Update(detalle.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenProductoDoesNotExist()
        {
            var (context, ventaId, _, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, context.ProProducto.First().id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, 9999);

            var result = await controller.Update(detalle.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsConflict_WhenRowVersionMismatch()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, rowVersion: wrongRowVersion);

            var result = await controller.Update(detalle.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }

        [Fact]
        public async Task Update_AdjustsStock_WhenPiezasIncrease()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, piezas: 2);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, piezas: 5);

            await controller.Update(detalle.id, dto);

            var producto = context.ProProducto.First();
            producto.intNumeroExistencia.Should().Be(7);
        }

        [Fact]
        public async Task Update_AdjustsStock_WhenPiezasDecrease()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, piezas: 5);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, piezas: 2);

            await controller.Update(detalle.id, dto);

            var producto = context.ProProducto.First();
            producto.intNumeroExistencia.Should().Be(13);
        }

        [Fact]
        public async Task Update_AdjustsStock_WhenProductoChanges()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, piezas: 2);

            var otroProducto = new ProProducto { strNombreProducto = "Otro Producto", intNumeroExistencia = 5, decPrecio = 299.99m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(otroProducto);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, otroProducto.id, piezas: 2);

            await controller.Update(detalle.id, dto);

            var productoOriginal = context.ProProducto.First(p => p.id == productoId);
            var productoNuevo = context.ProProducto.First(p => p.id == otroProducto.id);
            productoOriginal.intNumeroExistencia.Should().Be(12);
            productoNuevo.intNumeroExistencia.Should().Be(3);
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenInsufficientStock()
        {
            var (context, ventaId, productoId, _) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, piezas: 1);

            var otroProducto = new ProProducto { strNombreProducto = "Otro Producto", intNumeroExistencia = 2, decPrecio = 50m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(otroProducto);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, otroProducto.id, piezas: 5);

            var result = await controller.Update(detalle.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
