using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Venta
{
    public class VentaServiceTests
    {
        private readonly DbResilienceService _dbResilience;

        public VentaServiceTests()
        {
            _dbResilience = CreateDbResilience();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private static VentaService CreateService(AppDbContext context)
        {
            return new VentaService(context, CreateDbResilience());
        }

        [Fact]
        public async Task GetAll_ConRelaciones_DevuelveNombresYEstado()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);

            var result = await service.GetAllAsync();

            var item = result.Items.Should().ContainSingle().Subject;
            item.id.Should().Be(venta.id);
            item.idCliCliente.Should().Be(cliente.id);
            item.strNombreCliente.Should().Be(cliente.strNombreCliente);
            item.idSegUsuario.Should().Be(usuario.id);
            item.strNombreUsuario.Should().Be(usuario.strNombre);
            item.idVenCatEstado.Should().Be(estado.id);
            item.strEstado.Should().Be(estado.strValor);
            item.strClaveVenta.Should().Be(venta.strClaveVenta);
        }

        [Fact]
        public async Task GetById_ConRelaciones_DevuelveNombresYEstado()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);

            var item = await service.GetByIdAsync(venta.id);

            item.Should().NotBeNull();
            item!.strNombreCliente.Should().Be(cliente.strNombreCliente);
            item.strNombreUsuario.Should().Be(usuario.strNombre);
            item.strEstado.Should().Be(estado.strValor);
        }

        [Fact]
        public async Task Search_DateInicioBoundary_IncludesVentaExactlyAtStartDate()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = TestDataFactory.CreateVenta(cliente.id, usuario.id, "LIMITEINI1");
            venta.idVenCatEstado = estado.id;
            venta.dteFechaHoraCompra = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
            context.Set<VenVenta>().Add(venta);
            await context.SaveChangesAsync();
            var service = CreateService(context);

            var result = await service.SearchAsync(null, null, new DateTime(2026, 1, 15), null);

            result.Items.Should().ContainSingle(i => i.strClaveVenta == "LIMITEINI1");
        }

        [Fact]
        public async Task Search_DateFinBoundary_ExcludesVentaExactlyAtEndDate()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var dentro = TestDataFactory.CreateVenta(cliente.id, usuario.id, "LIMITEFIN1");
            dentro.idVenCatEstado = estado.id;
            dentro.dteFechaHoraCompra = new DateTime(2026, 1, 15, 23, 59, 59, DateTimeKind.Utc);
            var fuera = TestDataFactory.CreateVenta(cliente.id, usuario.id, "LIMITEFIN2");
            fuera.idVenCatEstado = estado.id;
            fuera.dteFechaHoraCompra = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
            context.Set<VenVenta>().AddRange(dentro, fuera);
            await context.SaveChangesAsync();
            var service = CreateService(context);

            var result = await service.SearchAsync(null, null, null, new DateTime(2026, 1, 15));

            result.Items.Should().ContainSingle(i => i.strClaveVenta == "LIMITEFIN1");
            result.Items.Should().NotContain(i => i.strClaveVenta == "LIMITEFIN2");
        }

        [Fact]
        public async Task Search_PorClaveYCliente_DevuelveNombresYEstado()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = TestDataFactory.CreateVenta(cliente.id, usuario.id, "BUSQUEDA1");
            venta.idVenCatEstado = estado.id;
            context.Set<VenVenta>().Add(venta);
            await context.SaveChangesAsync();
            var service = CreateService(context);

            var result = await service.SearchAsync("BUSQUEDA1", cliente.strNombreCliente[..5], null, null);

            var item = result.Items.Should().ContainSingle().Subject;
            item.strNombreCliente.Should().Be(cliente.strNombreCliente);
            item.strNombreUsuario.Should().Be(usuario.strNombre);
            item.strEstado.Should().Be(estado.strValor);
        }

        [Fact]
        public async Task Update_IdNoCoincide_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(2, 1, 1, 1);

            var act = () => service.UpdateAsync(1, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El ID de la venta no coincide.");
        }

        [Fact]
        public async Task Update_VentaNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(999, 1, 1, 1);

            var act = () => service.UpdateAsync(999, dto);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Venta no encontrada.");
        }

        [Fact]
        public async Task Update_ClienteNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, 9999, usuario.id, estado.id);

            var act = () => service.UpdateAsync(venta.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El cliente especificado no existe.");
        }

        [Fact]
        public async Task Update_UsuarioNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, 9999, estado.id);

            var act = () => service.UpdateAsync(venta.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El usuario especificado no existe.");
        }

        [Fact]
        public async Task Update_EstadoNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario.id, 9999);

            var act = () => service.UpdateAsync(venta.id, dto);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El estado especificado no existe.");
        }

        [Fact]
        public async Task Update_RowVersionVacio_NoLanzaConflicto()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario.id, estado.id, Array.Empty<byte>());

            var act = () => service.UpdateAsync(venta.id, dto);

            await act.Should().NotThrowAsync();
            context.Set<VenVenta>().First(v => v.id == venta.id).idCliCliente.Should().Be(cliente.id);
        }

        [Fact]
        public async Task Delete_VentaNoExiste_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(999);

            var act = () => service.DeleteAsync(999, dto);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Venta no encontrada.");
        }

        [Fact]
        public async Task Delete_RowVersionVacio_EliminaVenta()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, estado) = await SeedFullAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id, estado.id);
            var service = CreateService(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(venta.id, Array.Empty<byte>());

            var act = () => service.DeleteAsync(venta.id, dto);

            await act.Should().NotThrowAsync();
            context.Set<VenVenta>().Should().NotContain(v => v.id == venta.id);
        }

        private static async Task<(CliCliente cliente, SegUsuario usuario, VenCatEstado estado)> SeedFullAsync(AppDbContext context)
        {
            var cliente = TestDataFactory.CreateCliente();
            var usuario = new SegUsuario
            {
                strNombre = "Test User",
                strPWD = "hash",
                strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            var estado = new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            context.VenCatEstado.Add(estado);
            await context.SaveChangesAsync();
            return (cliente, usuario, estado);
        }

        private static async Task<VenVenta> SeedVentaAsync(AppDbContext context, int idCliCliente, int idSegUsuario, int idVenCatEstado)
        {
            var venta = TestDataFactory.CreateVenta(idCliCliente, idSegUsuario);
            venta.idVenCatEstado = idVenCatEstado;
            context.Set<VenVenta>().Add(venta);
            await context.SaveChangesAsync();
            return venta;
        }
    }
}
