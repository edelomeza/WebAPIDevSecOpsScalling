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
    public class DeleteTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;

        public DeleteTests()
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

        private static CliCliente CreateCliente(string nombre = "Cliente Test", string email = "cliente@test.com")
        {
            return new CliCliente
            {
                strNombreCliente = nombre,
                strCorreoElectronico = email,
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
        }

        private static CliClienteDeleteDto CreateDeleteDto(int id, byte[]? rowVersion = null)
        {
            return new CliClienteDeleteDto
            {
                id = id,
                RowVersion = rowVersion ?? new byte[] { 1, 0, 0, 0 },
            };
        }

        // ============ Success Cases ============

        [Fact]
        public async Task Delete_ReturnsOk_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(cliente.id);

            var result = await controller.Delete(cliente.id, dto);

            result.Should().BeOfType<OkResult>();
            _cacheMock.Verify(x => x.RemoveAsync($"cache:cliente:{cliente.id}"), Times.Once);
        }

        [Fact]
        public async Task Delete_RemovesClienteFromDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(cliente.id);

            await controller.Delete(cliente.id, dto);

            context.CliCliente.Count().Should().Be(0);
        }

        [Fact]
        public async Task Delete_RemovesOnlyTargetCliente()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateCliente(nombre: "cliente1", email: "u1@test.com"));
            context.CliCliente.Add(CreateCliente(nombre: "cliente2", email: "u2@test.com"));
            await context.SaveChangesAsync();

            var target = context.CliCliente.First(c => c.strNombreCliente == "cliente1");
            var controller = CreateController(context);
            var dto = CreateDeleteDto(target.id);

            await controller.Delete(target.id, dto);

            context.CliCliente.Count().Should().Be(1);
            context.CliCliente.Single().strNombreCliente.Should().Be("cliente2");
        }

        // ============ Error Cases ============

        [Fact]
        public async Task Delete_ReturnsBadRequest_WhenIdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(cliente.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Delete_ReturnsBadRequest_WithCorrectErrorMessage_IdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.Add(CreateCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(cliente.id, dto);

            var badRequest = result as BadRequestObjectResult;
            badRequest!.Value.Should().Be("El ID de la ruta no coincide con el ID del cuerpo.");
        }

        [Fact]
        public async Task Delete_ReturnsNotFound_WhenClienteDoesNotExist()
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
            context.CliCliente.Add(CreateCliente());
            await context.SaveChangesAsync();

            var cliente = context.CliCliente.First();
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = CreateDeleteDto(cliente.id, rowVersion: wrongRowVersion);

            var result = await controller.Delete(cliente.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
