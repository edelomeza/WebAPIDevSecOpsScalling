using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Services;

namespace UnitTest.Venta
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
            await context.SaveChangesAsync();
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
                await context.SaveChangesAsync();
            }
            return (cliente, usuario);
        }

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteName()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            createdResult!.ActionName.Should().Be(nameof(VentaController.Get));
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteValues()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

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
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as VenVentaDto;
            dtoResult!.id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithGeneratedFields()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as VenVentaDto;

            dtoResult!.idCliCliente.Should().Be(cliente.id);
            dtoResult.idSegUsuario.Should().Be(usuario.id);
            dtoResult.idVenCatEstado.Should().Be(1);
            dtoResult.dteFechaHoraCompra.Should().NotBeNull();
            dtoResult.strClaveVenta.Should().NotBeNullOrEmpty();
            dtoResult.strClaveVenta.Length.Should().Be(10);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithClientName()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as VenVentaDto;
            dtoResult!.strNombreCliente.Should().Be("Test Cliente");
            dtoResult.strNombreUsuario.Should().Be("Test User");
            dtoResult.strEstado.Should().Be("En compra");
        }

        [Fact]
        public async Task Create_PersistsVentaInDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            await controller.Create(dto);

            context.Set<VenVenta>().Count().Should().Be(1);
            var saved = context.Set<VenVenta>().First();
            saved.idCliCliente.Should().Be(cliente.id);
            saved.idSegUsuario.Should().Be(usuario.id);
            saved.idVenCatEstado.Should().Be(1);
            saved.strClaveVenta.Length.Should().Be(10);
            saved.dteFechaHoraCompra.Should().NotBeNull();
        }

        [Fact]
        public async Task Create_WithNonExistentCliente_ReturnsBadRequest()
        {
            var context = DbContextMock.GetDbContext();
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = 9999, idSegUsuario = usuario.id };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = result.Result as BadRequestObjectResult;
            var error = badRequest!.Value;
            var mensaje = error!.GetType().GetProperty("mensaje")?.GetValue(error) as string;
            Assert.Equal("El cliente especificado no existe.", mensaje);
        }

        [Fact]
        public async Task Create_WithNonExistentUsuario_ReturnsBadRequest()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = 9999 };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = result.Result as BadRequestObjectResult;
            var error = badRequest!.Value;
            var mensaje = error!.GetType().GetProperty("mensaje")?.GetValue(error) as string;
            Assert.Equal("El usuario especificado no existe.", mensaje);
        }

        [Fact]
        public async Task Create_GeneratesUniqueClaveVenta()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);
            var controller = CreateController(context);

            var dto1 = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };
            var dto2 = new VenVentaCreateDto { idCliCliente = cliente.id, idSegUsuario = usuario.id };

            var result1 = await controller.Create(dto1);
            var result2 = await controller.Create(dto2);

            var created1 = (result1.Result as CreatedAtActionResult)!.Value as VenVentaDto;
            var created2 = (result2.Result as CreatedAtActionResult)!.Value as VenVentaDto;

            created1!.strClaveVenta.Should().NotBe(created2!.strClaveVenta);
        }
    }
}