using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.VentaDetalle
{
    public class VentaDetalleServiceTests
    {
        private readonly DbResilienceService _dbResilience;

        public VentaDetalleServiceTests()
        {
            _dbResilience = CreateDbResilience();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private static VentaDetalleService CreateService(AppDbContext context, string username)
        {
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns(username);
            return new VentaDetalleService(context, CreateDbResilience(), userMock.Object);
        }

        private static async Task<(AppDbContext context, VentaDetalleService service, int ventaId, int productoId, decimal precio, SegUsuario usuario)> SeedAsync(string username = "Test User")
        {
            var context = DbContextMock.GetDbContext();
            var cliente = TestDataFactory.CreateCliente();
            var usuario = new SegUsuario
            {
                strNombre = username,
                strPWD = "hash",
                strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            var producto = TestDataFactory.CreateProducto(nombre: "Producto Test", existencia: 10, precio: 149.99m);
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            context.ProProducto.Add(producto);
            await context.SaveChangesAsync();
            var venta = new VenVenta
            {
                idCliCliente = cliente.id,
                idSegUsuario = usuario.id,
                idVenCatEstado = 1,
                dteFechaHoraCompra = DateTime.UtcNow,
                strClaveVenta = "CLV0000001",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.VenVenta.Add(venta);
            await context.SaveChangesAsync();
            return (context, CreateService(context, username), venta.id, producto.id, producto.decPrecio, usuario);
        }

        private static async Task<VenVentaDetalle> SeedDetalleAsync(AppDbContext context, int ventaId, int productoId, int piezas, decimal precio)
        {
            var detalle = TestDataFactory.CreateVentaDetalle(ventaId, productoId, piezas, precio);
            context.Set<VenVentaDetalle>().Add(detalle);
            await context.SaveChangesAsync();
            return detalle;
        }

        [Fact]
        public async Task GetAll_ConProducto_DevuelveDatosCompletos()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = TestDataFactory.CreateVentaDetalle(ventaId, productoId, 2, precio);
            context.Set<VenVentaDetalle>().Add(detalle);
            await context.SaveChangesAsync();

            var result = await service.GetAllAsync();

            var item = result.Items.Should().ContainSingle().Subject;
            item.id.Should().Be(detalle.id);
            item.idVenVenta.Should().Be(ventaId);
            item.idProProducto.Should().Be(productoId);
            item.strNombreProducto.Should().Be("Producto Test");
            item.decPrecio.Should().Be(precio);
            item.intPiezaVenta.Should().Be(2);
            item.decTotalVenta.Should().Be(2 * precio);
        }

        [Fact]
        public async Task GetById_ConProducto_DevuelveDatosCompletos()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = TestDataFactory.CreateVentaDetalle(ventaId, productoId, 2, precio);
            context.Set<VenVentaDetalle>().Add(detalle);
            await context.SaveChangesAsync();

            var item = await service.GetByIdAsync(detalle.id);

            item.Should().NotBeNull();
            item!.strNombreProducto.Should().Be("Producto Test");
            item.decPrecio.Should().Be(precio);
            item.intPiezaVenta.Should().Be(2);
            item.decTotalVenta.Should().Be(2 * precio);
        }

        [Fact]
        public async Task GetById_NotOwner_ThrowsUnauthorized()
        {
            var (context, _, ventaId, _, _, _) = await SeedAsync("Otro User");
            var service = CreateService(context, "Test User");
            var detalle = await SeedDetalleAsync(context, ventaId, 1, 1, 10m);

            var act = () => service.GetByIdAsync(detalle.id);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("No tiene permiso para acceder a este detalle de venta.");
        }

        [Fact]
        public async Task Create_NotOwner_ThrowsUnauthorized()
        {
            var (context, _, ventaId, productoId, _, _) = await SeedAsync("Dueno Real");
            var service = CreateService(context, "Intruso");
            var dto = TestDataFactory.CreateVentaDetalleCreateDto(ventaId, productoId, 1);

            var act = () => service.CreateAsync(dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("No tiene permiso para agregar detalles a esta venta.");
        }

        [Fact]
        public async Task Update_IdNoCoincide_ThrowsWithMessage()
        {
            var (context, service, ventaId, productoId, _, _) = await SeedAsync();
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(2, ventaId, productoId);

            var act = () => service.UpdateAsync(1, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El ID del detalle no coincide.");
        }

        [Fact]
        public async Task Update_DetalleNoExiste_ThrowsWithMessage()
        {
            var (_, service, ventaId, productoId, _, _) = await SeedAsync();
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(999, ventaId, productoId);

            var act = () => service.UpdateAsync(999, dto);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Detalle no encontrado.");
        }

        [Fact]
        public async Task Update_NotOwner_ThrowsUnauthorized()
        {
            var (context, _, ventaId, productoId, precio, _) = await SeedAsync("Dueno Real");
            var service = CreateService(context, "Intruso");
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, 3);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task Update_VentaNoExiste_ThrowsWithMessage()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, 9999, productoId, 3);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("La venta especificada no existe.");
        }

        [Fact]
        public async Task Update_ProductoNoExiste_ThrowsWithMessage()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, 9999, 3);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El producto especificado no existe.");
        }

        [Fact]
        public async Task Update_RowVersionVacio_NoLanzaConflicto()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, 3, Array.Empty<byte>());

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task Update_MismoProducto_IncrementoExactoAlLimite_NoLanza()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, 15);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().NotThrowAsync();
            var producto = context.ProProducto.First(p => p.id == productoId);
            producto.intNumeroExistencia.Should().Be(0);
        }

        [Fact]
        public async Task Update_MismoProducto_IncrementoConStockDisponible_NoLanza()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, 12);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().NotThrowAsync();
            var producto = context.ProProducto.First(p => p.id == productoId);
            producto.intNumeroExistencia.Should().Be(3);
        }

        [Fact]
        public async Task Update_MismoProducto_SinExistencias_ThrowsWithMessage()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoId, 20);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El producto no tiene las suficientes existencias.");
        }

        [Fact]
        public async Task Update_ProductoDistinto_IgualAlStock_NoLanza()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var productoNuevo = TestDataFactory.CreateProducto(nombre: "Producto Nuevo", existencia: 5, precio: 50m);
            context.ProProducto.Add(productoNuevo);
            await context.SaveChangesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoNuevo.id, 5);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().NotThrowAsync();
            context.ProProducto.First(p => p.id == productoId).intNumeroExistencia.Should().Be(15);
            context.ProProducto.First(p => p.id == productoNuevo.id).intNumeroExistencia.Should().Be(0);
        }

        [Fact]
        public async Task Update_ProductoDistinto_SinExistencias_ThrowsWithMessage()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var productoNuevo = TestDataFactory.CreateProducto(nombre: "Producto Nuevo", existencia: 5, precio: 50m);
            context.ProProducto.Add(productoNuevo);
            await context.SaveChangesAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleUpdateDto(detalle.id, ventaId, productoNuevo.id, 6);

            var act = () => service.UpdateAsync(detalle.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El producto no tiene las suficientes existencias.");
        }

        [Fact]
        public async Task Delete_DetalleNoExiste_ThrowsWithMessage()
        {
            var (_, service, _, _, _, _) = await SeedAsync();
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(999);

            var act = () => service.DeleteAsync(999, dto);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Detalle no encontrado.");
        }

        [Fact]
        public async Task Delete_NotOwner_ThrowsUnauthorized()
        {
            var (context, _, ventaId, productoId, precio, _) = await SeedAsync("Dueno Real");
            var service = CreateService(context, "Intruso");
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(detalle.id);

            var act = () => service.DeleteAsync(detalle.id, dto);

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public async Task Delete_RowVersionVacio_EliminaYRestauraStock()
        {
            var (context, service, ventaId, productoId, precio, _) = await SeedAsync();
            var detalle = await SeedDetalleAsync(context, ventaId, productoId, 5, 5 * precio);
            var dto = TestDataFactory.CreateVentaDetalleDeleteDto(detalle.id, Array.Empty<byte>());

            var act = () => service.DeleteAsync(detalle.id, dto);

            await act.Should().NotThrowAsync();
            context.Set<VenVentaDetalle>().Should().NotContain(d => d.id == detalle.id);
            context.ProProducto.First(p => p.id == productoId).intNumeroExistencia.Should().Be(15);
        }
    }
}
