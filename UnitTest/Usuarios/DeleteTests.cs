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
    public class DeleteTests
    {
        private readonly Mock<IPasswordHasherService> _hasherMock;
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private const string FakeHash = "$argon2id$v=19$m=16384,t=2,p=1$test$hash";

        public DeleteTests()
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

        private static SegUsuario CreateUser(string nombre = "usuario", string email = "usuario@test.com")
        {
            return new SegUsuario
            {
                strNombre = nombre,
                strCorreoElectronico = email,
                strPWD = FakeHash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
        }

        private static UsuarioDeleteDto CreateDeleteDto(int id, byte[]? rowVersion = null)
        {
            return new UsuarioDeleteDto
            {
                id = id,
                RowVersion = rowVersion ?? new byte[] { 1, 0, 0, 0 }
            };
        }

        // ============ Success Cases ============

        [Fact]
        public async Task Delete_ReturnsOk_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(user.id);

            var result = await controller.Delete(user.id, dto);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task Delete_RemovesUserFromDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(user.id);

            await controller.Delete(user.id, dto);

            context.SegUsuario.Count().Should().Be(0);
        }

        [Fact]
        public async Task Delete_RemovesOnlyTargetUser()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateUser(nombre: "user1", email: "u1@test.com"));
            context.SegUsuario.Add(CreateUser(nombre: "user2", email: "u2@test.com"));
            await context.SaveChangesAsync();

            var target = context.SegUsuario.First(u => u.strNombre == "user1");
            var controller = CreateController(context);
            var dto = CreateDeleteDto(target.id);

            await controller.Delete(target.id, dto);

            context.SegUsuario.Count().Should().Be(1);
            context.SegUsuario.Single().strNombre.Should().Be("user2");
        }

        // ============ Error Cases ============

        [Fact]
        public async Task Delete_ReturnsBadRequest_WhenIdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(user.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Delete_ReturnsBadRequest_WithCorrectErrorMessage_IdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(user.id, dto);

            var badRequest = result as BadRequestObjectResult;
            badRequest!.Value.Should().Be("El ID de la ruta no coincide con el ID del cuerpo.");
        }

        [Fact]
        public async Task Delete_ReturnsNotFound_WhenUserDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(999, dto);

            result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task Delete_ReturnsBadRequest_WhenIdMismatch_BeforeCheckingExistence()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 2);

            var result = await controller.Delete(1, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        // ============ Concurrency ============

        [Fact]
        public async Task Delete_ReturnsConflict_WhenRowVersionMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(CreateUser());
            await context.SaveChangesAsync();

            var user = context.SegUsuario.First();
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = CreateDeleteDto(user.id, rowVersion: wrongRowVersion);

            var result = await controller.Delete(user.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
