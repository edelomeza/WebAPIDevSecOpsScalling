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

namespace UnitTest.Usuarios
{
    public class InsertTests
    {
        private readonly Mock<IPasswordHasherService> _hasherMock;
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private const string FakeHash = "$argon2id$v=19$m=16384,t=2,p=1$test$hash";

        public InsertTests()
        {
            _hasherMock = new Mock<IPasswordHasherService>();
            _hasherMock.Setup(h => h.HashPassword(It.IsAny<string>())).Returns(FakeHash);
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private UsuarioController CreateController(AppDbContext context)
        {
            return new UsuarioController(new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object));
        }

        // ============ Controller Response ============

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteName()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            createdResult!.ActionName.Should().Be(nameof(UsuarioController.Get));
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteValues()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
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
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as SegUsuarioDto;
            dtoResult!.id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectNombre()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as SegUsuarioDto;
            dtoResult!.strNombre.Should().Be("nuevo");
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectCorreo()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as SegUsuarioDto;
            dtoResult!.strCorreoElectronico.Should().Be("nuevo@test.com");
        }

        [Fact]
        public async Task Create_DtoDoesNotContainStrPWD()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as SegUsuarioDto;
            dtoResult!.GetType().GetProperty("strPWD").Should().BeNull();
        }

        // ============ Persistence & Security ============

        [Fact]
        public async Task Create_PersistsUserInDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            await controller.Create(dto);

            context.SegUsuario.Count().Should().Be(1);
            var saved = context.SegUsuario.First();
            saved.strNombre.Should().Be("nuevo");
            saved.strCorreoElectronico.Should().Be("nuevo@test.com");
        }

        [Fact]
        public async Task Create_SetsFechaRegistro()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            await controller.Create(dto);

            var saved = context.SegUsuario.First();
            saved.dteFechaRegistro.Should().NotBeNull();
            saved.dteFechaRegistro.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task Create_PasswordIsHashed_WhenCreated()
        {
            var originalPassword = "12345678";
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = originalPassword,
                strCorreoElectronico = "nuevo@test.com"
            };

            await controller.Create(dto);

            var saved = context.SegUsuario.First();
            saved.strPWD.Should().NotBe(originalPassword);
            saved.strPWD.Should().Be(FakeHash);
        }

        [Fact]
        public async Task Create_TrimsNombre()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "  usuario  ",
                strPWD = "12345678",
                strCorreoElectronico = "nuevo@test.com"
            };

            await controller.Create(dto);

            var saved = context.SegUsuario.First();
            saved.strNombre.Should().Be("usuario");
        }

        [Fact]
        public async Task Create_TrimsCorreoElectronico()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "nuevo",
                strPWD = "12345678",
                strCorreoElectronico = "  nuevo@test.com  "
            };

            await controller.Create(dto);

            var saved = context.SegUsuario.First();
            saved.strCorreoElectronico.Should().Be("nuevo@test.com");
        }

        // ============ Error Handling ============

        [Fact]
        public async Task Create_ReturnsBadRequest_WhenUsernameExists()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "existente",
                strPWD = FakeHash,
                strCorreoElectronico = "existente@test.com"
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "existente",
                strPWD = "12345678",
                strCorreoElectronico = "otro@test.com"
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Create_ReturnsBadRequest_WithCorrectErrorMessage()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "existente",
                strPWD = FakeHash,
                strCorreoElectronico = "existente@test.com"
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "existente",
                strPWD = "12345678",
                strCorreoElectronico = "otro@test.com"
            };

            var result = await controller.Create(dto);

            var badRequest = result.Result as BadRequestObjectResult;
            var mensaje = badRequest!.Value!.GetType().GetProperty("mensaje")!.GetValue(badRequest.Value);
            mensaje.Should().Be("El nombre de usuario ya existe.");
        }

        [Fact]
        public async Task Create_DoesNotPersistDuplicateUser()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "existente",
                strPWD = FakeHash,
                strCorreoElectronico = "existente@test.com"
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var dto = new UsuarioCreateDto
            {
                strNombre = "existente",
                strPWD = "12345678",
                strCorreoElectronico = "otro@test.com"
            };

            await controller.Create(dto);

            context.SegUsuario.Count().Should().Be(1);
        }
    }
}
