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

namespace UnitTest.Usuarios
{
    public class UpdateTests
    {
        private readonly Mock<IPasswordHasherService> _hasherMock;
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private const string FakeHash = "$argon2id$v=19$m=16384,t=2,p=1$test$hash";

        public UpdateTests()
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

        private const string OriginalPassword = "original123";

        private static SegUsuario CreateSeedUser(string nombre = "usuario", string email = "usuario@test.com", string? passwordHash = null)
        {
            return new SegUsuario
            {
                strNombre = nombre,
                strCorreoElectronico = email,
                strPWD = passwordHash ?? FakeHash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
        }

        private static UsuarioUpdateDto CreateUpdateDto(int id, string nombre = "nuevo", string email = "nuevo@test.com", string? password = null, byte[]? rowVersion = null)
        {
            return new UsuarioUpdateDto
            {
                id = id,
                strNombre = nombre,
                strCorreoElectronico = email,
                strPWD = password,
                RowVersion = rowVersion ?? new byte[] { 1, 0, 0, 0 }
            };
        }

        // ============ Success Cases ============

        [Fact]
        public async Task Update_ReturnsNoContent_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id);

            var result = await controller.Update(user.id, dto);

            result.Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task Update_ChangesNombre_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, nombre: "nombreActualizado");

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strNombre.Should().Be("nombreActualizado");
        }

        [Fact]
        public async Task Update_ChangesCorreo_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, email: "actualizado@test.com");

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strCorreoElectronico.Should().Be("actualizado@test.com");
        }

        [Fact]
        public async Task Update_DoesNotChangePassword_WhenPasswordIsNull()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser(passwordHash: FakeHash));
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, password: null);

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strPWD.Should().Be(FakeHash);
            _hasherMock.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Update_DoesNotChangePassword_WhenPasswordIsEmpty()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser(passwordHash: FakeHash));
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, password: "");

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strPWD.Should().Be(FakeHash);
            _hasherMock.Verify(h => h.HashPassword(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Update_HashesPassword_WhenProvided()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser(passwordHash: FakeHash));
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, password: "newPassword123");

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strPWD.Should().Be(FakeHash);
            _hasherMock.Verify(h => h.HashPassword("newPassword123"), Times.Once);
        }

        [Fact]
        public async Task Update_TrimsNombre()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, nombre: "  conEspacios  ");

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strNombre.Should().Be("conEspacios");
        }

        [Fact]
        public async Task Update_TrimsCorreo()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, email: "  correo@test.com  ");

            await controller.Update(user.id, dto);

            var updated = context.SegUsuario.First();
            updated.strCorreoElectronico.Should().Be("correo@test.com");
        }

        [Fact]
        public async Task Update_AllowsSelfRename()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser(nombre: "usuario"));
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, nombre: "usuario");

            var result = await controller.Update(user.id, dto);

            result.Should().BeOfType<NoContentResult>();
        }

        // ============ Error Cases ============

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenIdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(id: 2);

            var result = await controller.Update(1, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WithCorrectErrorMessage_IdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(id: 2);

            var result = await controller.Update(1, dto);

            var badRequest = result as BadRequestObjectResult;
            badRequest!.Value.Should().Be("El ID del usuario no coincide.");
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenUsernameAlreadyExists()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser(nombre: "usuario1", email: "u1@test.com"));
            context.SegUsuario.Add(CreateSeedUser(nombre: "usuario2", email: "u2@test.com"));
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First(u => u.strNombre == "usuario1");
            var controller = CreateController(context);
            var dto = CreateUpdateDto(user.id, nombre: "usuario2");

            var result = await controller.Update(user.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsNotFound_WhenUserDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(id: 999);

            var result = await controller.Update(999, dto);

            result.Should().BeOfType<NotFoundResult>();
        }

        // ============ Concurrency ============

        [Fact]
        public async Task Update_ReturnsConflict_WhenRowVersionMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateSeedUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = CreateUpdateDto(user.id, rowVersion: wrongRowVersion);

            var result = await controller.Update(user.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
