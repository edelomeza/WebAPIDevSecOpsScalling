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

namespace UnitTest.Producto
{
    public class UpdateTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;

        public UpdateTests()
        {
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
            _cacheMock
                .Setup(x => x.GetOrCreateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<PagedResult<ProProductoDto>>>>(),
                    It.IsAny<TimeSpan?>()))
                .Returns<string, Func<Task<PagedResult<ProProductoDto>>>, TimeSpan?>(
                    (_, factory, _) => factory());
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private ProductoController CreateController(AppDbContext context)
        {
            return new ProductoController(new ProductoService(context, _dbResilience, _cacheMock.Object));
        }

        private static ProProducto CreateSeedProducto(string nombre = "Producto Test", int existencia = 10, decimal precio = 99.99m)
        {
            return new ProProducto
            {
                strNombreProducto = nombre,
                strURLImagen = "https://ejemplo.com/imagen.jpg",
                strDescripcion = "Descripción original",
                intNumeroExistencia = existencia,
                decPrecio = precio,
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
        }

        private static ProductoUpdateDto CreateUpdateDto(int id, string nombre = "Actualizado", int existencia = 20, decimal precio = 199.99m, byte[]? rowVersion = null)
        {
            return new ProductoUpdateDto
            {
                id = id,
                strNombreProducto = nombre,
                intNumeroExistencia = existencia,
                decPrecio = precio,
                RowVersion = rowVersion ?? new byte[] { 1, 0, 0, 0 },
            };
        }

        // ============ Success Cases ============

        [Fact]
        public async Task Update_ReturnsNoContent_WhenSuccessful()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateSeedProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(producto.id);

            var result = await controller.Update(producto.id, dto);

            result.Should().BeOfType<NoContentResult>();
        }

        [Fact]
        public async Task Update_ChangesNombre_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateSeedProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(producto.id, nombre: "Nuevo Nombre");

            await controller.Update(producto.id, dto);

            var updated = context.ProProducto.First();
            updated.strNombreProducto.Should().Be("Nuevo Nombre");
        }

        [Fact]
        public async Task Update_ChangesExistencia_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateSeedProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(producto.id, existencia: 50);

            await controller.Update(producto.id, dto);

            var updated = context.ProProducto.First();
            updated.intNumeroExistencia.Should().Be(50);
        }

        [Fact]
        public async Task Update_ChangesPrecio_InDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateSeedProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(producto.id, precio: 250.00m);

            await controller.Update(producto.id, dto);

            var updated = context.ProProducto.First();
            updated.decPrecio.Should().Be(250.00m);
        }

        [Fact]
        public async Task Update_TrimsNombreProducto()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateSeedProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateUpdateDto(producto.id, nombre: "  Con Espacios  ");

            await controller.Update(producto.id, dto);

            var updated = context.ProProducto.First();
            updated.strNombreProducto.Should().Be("Con Espacios");
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
            badRequest!.Value.Should().Be("El ID del producto no coincide.");
        }

        [Fact]
        public async Task Update_ReturnsNotFound_WhenProductoDoesNotExist()
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
            context.ProProducto.Add(CreateSeedProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = CreateUpdateDto(producto.id, rowVersion: wrongRowVersion);

            var result = await controller.Update(producto.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
