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
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Venta
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

        private VentaController CreateController(AppDbContext context)
        {
            return new VentaController(new VentaService(context, _dbResilience, Moq.Mock.Of<WebAPIDevSecOps.Interfaces.IEventPublisher>(), SagaBridgeTestConfig.BridgeOff(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<WebAPIDevSecOps.Services.VentaService>>()));
        }

        private async Task<(CliCliente cliente, SegUsuario usuario, VenCatEstado estado1, VenCatEstado estado2)> SeedDependenciesAsync(AppDbContext context)
        {
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            context.VenCatEstado.AddRange(
                new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
            );
            await context.SaveChangesAsync();
            var estado1 = context.VenCatEstado.First(e => e.id == 1);
            var estado2 = context.VenCatEstado.First(e => e.id == 2);
            return (cliente, usuario, estado1, estado2);
        }

        private async Task<VenVenta> SeedVentaAsync(AppDbContext context, int idCliCliente, int idSegUsuario)
        {
            var venta = new VenVenta
            {
                idCliCliente = idCliCliente,
                idSegUsuario = idSegUsuario,
                idVenCatEstado = 1,
                dteFechaHoraCompra = DateTime.UtcNow,
                strClaveVenta = Guid.NewGuid().ToString("N")[..10],
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.Set<VenVenta>().Add(venta);
            await context.SaveChangesAsync();
            return venta;
        }

        [Fact]
        public async Task Update_ReturnsNoContent_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _, estado2) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario.id, estado2.id);

            var result = await controller.Update(venta.id, dto);

            result.Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task Update_ChangesCliente_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente1, usuario, _, estado2) = await SeedDependenciesAsync(context);
            var cliente2 = new CliCliente { strNombreCliente = "Otro Cliente", strCorreoElectronico = $"cli2{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345679", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente2);
            await context.SaveChangesAsync();
            var venta = await SeedVentaAsync(context, cliente1.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente2.id, usuario.id, estado2.id);

            await controller.Update(venta.id, dto);

            var updated = context.Set<VenVenta>().First();
            updated.idCliCliente.Should().Be(cliente2.id);
        }

        [Fact]
        public async Task Update_ChangesUsuario_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario1, _, estado2) = await SeedDependenciesAsync(context);
            var usuario2 = new SegUsuario { strNombre = "Otro User", strPWD = "hash", strCorreoElectronico = $"user2{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.SegUsuario.Add(usuario2);
            await context.SaveChangesAsync();
            var venta = await SeedVentaAsync(context, cliente.id, usuario1.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario2.id, estado2.id);

            await controller.Update(venta.id, dto);

            var updated = context.Set<VenVenta>().First();
            updated.idSegUsuario.Should().Be(usuario2.id);
        }

        [Fact]
        public async Task Update_ChangesEstado_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _, estado2) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario.id, estado2.id);

            await controller.Update(venta.id, dto);

            var updated = context.Set<VenVenta>().First();
            updated.idVenCatEstado.Should().Be(estado2.id);
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenIdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(2, 1, 1, 1);

            var result = await controller.Update(1, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsNotFound_WhenVentaDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(999, 1, 1, 1);

            var result = await controller.Update(999, dto);

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenClienteDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _, _) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, 9999, usuario.id, 2);

            var result = await controller.Update(venta.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenUsuarioDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _, _) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, 9999, 2);

            var result = await controller.Update(venta.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenEstadoDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _, _) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario.id, 9999);

            var result = await controller.Update(venta.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsConflict_WhenRowVersionMismatch()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario, _, estado2) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = TestDataFactory.CreateVentaUpdateDto(venta.id, cliente.id, usuario.id, estado2.id, wrongRowVersion);

            var result = await controller.Update(venta.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
