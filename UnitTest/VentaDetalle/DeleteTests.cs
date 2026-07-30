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
    public class DeleteTests
    {
        private readonly DbResilienceService _dbResilience;

        public DeleteTests()
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

        private async Task<(AppDbContext context, int ventaId, int productoId)> SeedDependenciesAsync()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" }
                );
                await context.SaveChangesAsync();
            }
            await context.SaveChangesAsync();

            var producto = new ProProducto { strNombreProducto = "Producto Test", intNumeroExistencia = 10, decPrecio = 99.99m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(producto);
            await context.SaveChangesAsync();

            var venta = new VenVenta { idCliCliente = cliente.id, idSegUsuario = usuario.id, idVenCatEstado = 1, dteFechaHoraCompra = DateTime.UtcNow, strClaveVenta = Guid.NewGuid().ToString("N")[..10], RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.VenVenta.Add(venta);
            await context.SaveChangesAsync();

            return (context, venta.id, producto.id);
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
        public async Task Delete_ReturnsOk_WhenSuccessful()
        {
            var (context, ventaId, productoId) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(detalle.id);

            var result = await controller.Delete(detalle.id, dto);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task Delete_RemovesDetalleFromDatabase()
        {
            var (context, ventaId, productoId) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(detalle.id);

            await controller.Delete(detalle.id, dto);

            context.Set<VenVentaDetalle>().Count().Should().Be(0);
        }

        [Fact]
        public async Task Delete_RemovesOnlyTargetDetalle()
        {
            var (context, ventaId, productoId) = await SeedDependenciesAsync();
            var d1 = await SeedDetalleAsync(context, ventaId, productoId);
            var d2 = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(d1.id);

            await controller.Delete(d1.id, dto);

            context.Set<VenVentaDetalle>().Count().Should().Be(1);
            context.Set<VenVentaDetalle>().Single().id.Should().Be(d2.id);
        }

        [Fact]
        public async Task Delete_ReturnsNotFound_WhenDetalleDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(999);

            var result = await controller.Delete(999, dto);

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Delete_ReturnsConflict_WhenRowVersionMismatch()
        {
            var (context, ventaId, productoId) = await SeedDependenciesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId);
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(detalle.id, wrongRowVersion);

            var result = await controller.Delete(detalle.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }

        [Fact]
        public async Task Delete_RestoresProductoExistencias()
        {
            var (context, ventaId, productoId) = await SeedDependenciesAsync();
            var controller = CreateController(context);

            var createDto = new VenVentaDetalleCreateDto
            {
                idVenVenta = ventaId,
                idProProducto = productoId,
                intPiezaVenta = 3
            };

            await controller.Create(createDto);

            var detalle = context.Set<VenVentaDetalle>().First();
            var deleteDto = TestDataFactory.CreateVentaDetalleDeleteDto(detalle.id);

            await controller.Delete(detalle.id, deleteDto);

            var producto = context.ProProducto.First();
            producto.intNumeroExistencia.Should().Be(10);
        }
    }
}
