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

namespace UnitTest.Clientes
{
    public class UpdateTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;

        public UpdateTests()
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

        private static CliCliente CreateSeedCliente(string nombre = "Cliente Test", string email = "cliente@test.com", string telefono = "5512345678")
        {
            return new CliCliente
            {
                strNombreCliente = nombre,
                strDireccionCliente = "Dirección original",
                strCorreoElectronico = email,
                strNumeroTelefono = telefono,
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
        }

        private static CliClienteUpdateDto CreateUpdateDto(int id, string nombre = "Actualizado", string email = "actualizado@test.com", string? telefono = null, string? direccion = null, byte[]? rowVersion = null)
        {
            return new CliClienteUpdateDto
            {
                id = id,
                strNombreCliente = nombre,
                strCorreoElectronico = email,
                strNumeroTelefono = telefono ?? "5598765432",
                strDireccionCliente = direccion,
                RowVersion = rowVersion ?? new byte[] { 1, 0, 0, 0 },
            };
        }

        // ============ Success Cases ============

        [Fact]
        public async Task Update_ReturnsNoContent_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id);

            var result = await controller.Update(cliente.id, dto);

            result.Should().BeOfType<NoContentResult>();
            _cacheMock.Verify(x => x.RemoveAsync($"cache:cliente:{cliente.id}"), Times.Once);
        }

        [Fact]
        public async Task Update_ChangesNombre_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, nombre: "Nuevo Nombre");

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strNombreCliente.Should().Be("Nuevo Nombre");
        }

        [Fact]
        public async Task Update_ChangesCorreo_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, email: "nuevo.correo@test.com");

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strCorreoElectronico.Should().Be("nuevo.correo@test.com");
        }

        [Fact]
        public async Task Update_ChangesTelefono_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, telefono: "5511111111");

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strNumeroTelefono.Should().Be("5511111111");
        }

        [Fact]
        public async Task Update_ChangesDireccion_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, direccion: "Nueva dirección");

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strDireccionCliente.Should().Be("Nueva dirección");
        }

        [Fact]
        public async Task Update_SetsDireccionToNull()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, direccion: null);

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strDireccionCliente.Should().BeNull();
        }

        [Fact]
        public async Task Update_TrimsNombreCliente()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, nombre: "  Con Espacios  ");

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strNombreCliente.Should().Be("Con Espacios");
        }

        [Fact]
        public async Task Update_TrimsCorreo()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, email: "  correo@test.com  ");

            await controller.Update(cliente.id, dto);

            var updated = context.CliCliente.First();
            updated.strCorreoElectronico.Should().Be("correo@test.com");
        }

        [Fact]
        public async Task Update_AllowsSelfRename_WithSameCorreo()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente(email: "mismocorreo@test.com"));
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, email: "mismocorreo@test.com");

            var result = await controller.Update(cliente.id, dto);

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
            badRequest!.Value.Should().Be("El ID del cliente no coincide.");
        }

        [Fact]
        public async Task Update_ReturnsBadRequest_WhenCorreoAlreadyExists()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateSeedCliente(nombre: "cliente1", email: "u1@test.com"));
            context.CliCliente.Add(CreateSeedCliente(nombre: "cliente2", email: "u2@test.com"));
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First(c => c.strNombreCliente == "cliente1");
            var controller = CreateController(context);
            var dto = CreateUpdateDto(cliente.id, email: "u2@test.com");

            var result = await controller.Update(cliente.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Update_ReturnsNotFound_WhenClienteDoesNotExist()
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
            context.CliCliente.Add(CreateSeedCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = CreateUpdateDto(cliente.id, rowVersion: wrongRowVersion);

            var result = await controller.Update(cliente.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
