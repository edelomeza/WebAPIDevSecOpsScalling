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

namespace UnitTest.Clientes
{
    public class InsertTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;

        public InsertTests()
        {
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private ClienteController CreateController(AppDbContext context)
        {
            return new ClienteController(new ClienteService(context, _dbResilience, _cacheMock.Object));
        }

        // ============ Controller Response ============

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteName()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            createdResult!.ActionName.Should().Be(nameof(ClienteController.Get));
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteValues()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

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
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as CliClienteDto;
            dtoResult!.id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectNombre()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as CliClienteDto;
            dtoResult!.strNombreCliente.Should().Be("Cliente Nuevo");
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectCorreo()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as CliClienteDto;
            dtoResult!.strCorreoElectronico.Should().Be("nuevo@test.com");
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectTelefono()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as CliClienteDto;
            dtoResult!.strNumeroTelefono.Should().Be("5512345678");
        }

        // ============ Persistence ============

        [Fact]
        public async Task Create_PersistsClienteInDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            await controller.Create(dto);

            context.CliCliente.Count().Should().Be(1);
            var saved = context.CliCliente.First();
            saved.strNombreCliente.Should().Be("Cliente Nuevo");
            saved.strCorreoElectronico.Should().Be("nuevo@test.com");
            saved.strNumeroTelefono.Should().Be("5512345678");
        }

        // ============ Trimming ============

        [Fact]
        public async Task Create_TrimsNombreCliente()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "  Cliente Con Espacios  ",
                strCorreoElectronico = "nuevo@test.com",
                strNumeroTelefono = "5512345678",
            };

            await controller.Create(dto);

            var saved = context.CliCliente.First();
            saved.strNombreCliente.Should().Be("Cliente Con Espacios");
        }

        [Fact]
        public async Task Create_TrimsCorreoElectronico()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Nuevo",
                strCorreoElectronico = "  nuevo@test.com  ",
                strNumeroTelefono = "5512345678",
            };

            await controller.Create(dto);

            var saved = context.CliCliente.First();
            saved.strCorreoElectronico.Should().Be("nuevo@test.com");
        }

        // ============ Error Handling ============

        [Fact]
        public async Task Create_ReturnsBadRequest_WhenCorreoExists()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(new CliCliente
            {
                strNombreCliente = "Existente",
                strCorreoElectronico = "existente@test.com",
                strNumeroTelefono = "5512345678",
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Nuevo",
                strCorreoElectronico = "existente@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_ReturnsBadRequest_WithCorrectErrorMessage_WhenCorreoExists()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(new CliCliente
            {
                strNombreCliente = "Existente",
                strCorreoElectronico = "existente@test.com",
                strNumeroTelefono = "5512345678",
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Nuevo",
                strCorreoElectronico = "existente@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            var badRequest = result.Result as BadRequestObjectResult;
            var mensaje = badRequest!.Value!.GetType().GetProperty("mensaje")!.GetValue(badRequest.Value);
            mensaje.Should().Be("El correo electrónico ya está registrado.");
        }

        [Fact]
        public async Task Create_DoesNotPersist_WhenCorreoDuplicado()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(new CliCliente
            {
                strNombreCliente = "Existente",
                strCorreoElectronico = "existente@test.com",
                strNumeroTelefono = "5512345678",
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Nuevo",
                strCorreoElectronico = "existente@test.com",
                strNumeroTelefono = "5512345678",
            };

            await controller.Create(dto);

            context.CliCliente.Count().Should().Be(1);
        }

        [Fact]
        public async Task Create_AllowsDuplicateNombreCliente()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(new CliCliente
            {
                strNombreCliente = "NombreComun",
                strCorreoElectronico = "primero@test.com",
                strNumeroTelefono = "5512345678",
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "NombreComun",
                strCorreoElectronico = "segundo@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
            context.CliCliente.Count().Should().Be(2);
        }

        [Fact]
        public async Task Create_WithDireccionNull_Succeeds()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new CliClienteCreateDto
            {
                strNombreCliente = "Cliente Sin Direccion",
                strCorreoElectronico = "sin.direccion@test.com",
                strNumeroTelefono = "5512345678",
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
            var saved = context.CliCliente.First();
            saved.strDireccionCliente.Should().BeNull();
        }
    }
}
