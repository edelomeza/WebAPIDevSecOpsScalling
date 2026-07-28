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
    public class DeleteTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;

        public DeleteTests()
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

        private static ProProducto CreateProducto(string nombre = "Producto Test")
        {
            return new ProProducto
            {
                strNombreProducto = nombre,
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
        }

        private static ProductoDeleteDto CreateDeleteDto(int id, byte[]? rowVersion = null)
        {
            return new ProductoDeleteDto
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
            context.ProProducto.Add(CreateProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(producto.id);

            var result = await controller.Delete(producto.id, dto);

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task Delete_RemovesProductoFromDatabase()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(producto.id);

            await controller.Delete(producto.id, dto);

            context.ProProducto.Count().Should().Be(0);
        }

        [Fact]
        public async Task Delete_RemovesOnlyTargetProducto()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateProducto(nombre: "producto1"));
            context.ProProducto.Add(CreateProducto(nombre: "producto2"));
            await context.SaveChangesAsync();

            var target = context.ProProducto.First(p => p.strNombreProducto == "producto1");
            var controller = CreateController(context);
            var dto = CreateDeleteDto(target.id);

            await controller.Delete(target.id, dto);

            context.ProProducto.Count().Should().Be(1);
            context.ProProducto.Single().strNombreProducto.Should().Be("producto2");
        }

        // ============ Error Cases ============

        [Fact]
        public async Task Delete_ReturnsBadRequest_WhenIdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(producto.id, dto);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Delete_ReturnsBadRequest_WithCorrectErrorMessage_IdMismatch()
        {
            var context = DbContextMock.GetDbContext();
            context.ProProducto.Add(CreateProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var dto = CreateDeleteDto(id: 999);

            var result = await controller.Delete(producto.id, dto);

            var badRequest = result as BadRequestObjectResult;
            badRequest!.Value.Should().Be("El ID de la ruta no coincide con el ID del cuerpo.");
        }

        [Fact]
        public async Task Delete_ReturnsNotFound_WhenProductoDoesNotExist()
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
            context.ProProducto.Add(CreateProducto());
            await context.SaveChangesAsync();

            var producto = context.ProProducto.First();
            var controller = CreateController(context);
            var wrongRowVersion = new byte[] { 9, 9, 9 };
            var dto = CreateDeleteDto(producto.id, rowVersion: wrongRowVersion);

            var result = await controller.Delete(producto.id, dto);

            result.Should().BeOfType<ConflictResult>();
        }
    }
}
