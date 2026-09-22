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

        private VentaController CreateController(AppDbContext context)
        {
            return new VentaController(new VentaService(context, _dbResilience, Moq.Mock.Of<WebAPIDevSecOps.Interfaces.IEventPublisher>(), SagaBridgeTestConfig.BridgeOff(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<WebAPIDevSecOps.Services.VentaService>>()));
        }

        private async Task<(CliCliente cliente, SegUsuario usuario)> SeedDependenciesAsync(AppDbContext context)
        {
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
            return (cliente, usuario);
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
        public async Task Delete_ReturnsOk_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(venta.id);

            var result = await controller.Delete(venta.id, dto);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task Delete_RemovesVentaFromDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(venta.id);

            await controller.Delete(venta.id, dto);

            context.Set<VenVenta>().Count().Should().Be(0);
        }

        [Fact]
        public async Task Delete_RemovesOnlyTargetVenta()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var v1 = await SeedVentaAsync(context, cliente.id, usuario.id);
            var v2 = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(v1.id);

            await controller.Delete(v1.id, dto);

            context.Set<VenVenta>().Count().Should().Be(1);
            context.Set<VenVenta>().Single().id.Should().Be(v2.id);
        }

        [Fact]
        public async Task Delete_ReturnsBadRequest_WhenIdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(999);

            var result = await controller.Delete(venta.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Delete_ReturnsNotFound_WhenVentaDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = TestDataFactory.CreateVentaDeleteDto(999);

            var result = await controller.Delete(999, dto);

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Delete_ReturnsConflict_WhenRowVersionMismatch()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var venta = await SeedVentaAsync(context, cliente.id, usuario.id);
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = TestDataFactory.CreateVentaDeleteDto(venta.id, wrongRowVersion);

            var result = await controller.Delete(venta.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
