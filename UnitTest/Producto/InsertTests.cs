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

namespace UnitTest.Producto
{
    public class InsertTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private readonly Mock<IUserAccessor> _userAccessorMock;

        public InsertTests()
        {
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
            _userAccessorMock = new Mock<IUserAccessor>();
            _userAccessorMock.Setup(u => u.IsAdmin()).Returns(true);
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
            return new ProductoController(new ProductoService(context, _dbResilience, _cacheMock.Object, _userAccessorMock.Object));
        }

        // ============ Controller Response ============

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteName()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            createdResult!.ActionName.Should().Be(nameof(ProductoController.Get));
        }

        [Fact]
        public async Task Create_ReturnsCreatedWithCorrectRouteValues()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
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
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as ProProductoDto;
            dtoResult!.id.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectNombre()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as ProProductoDto;
            dtoResult!.strNombreProducto.Should().Be("Producto Nuevo");
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectExistencia()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 25,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as ProProductoDto;
            dtoResult!.intNumeroExistencia.Should().Be(25);
        }

        [Fact]
        public async Task Create_ReturnsDto_WithCorrectPrecio()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Nuevo",
                intNumeroExistencia = 10,
                decPrecio = 149.99m,
            };

            var result = await controller.Create(dto);

            var createdResult = result.Result as CreatedAtActionResult;
            var dtoResult = createdResult!.Value as ProProductoDto;
            dtoResult!.decPrecio.Should().Be(149.99m);
        }

        // ============ Persistence ============

        [Fact]
        public async Task Create_PersistsProductoInDatabase()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Persistente",
                intNumeroExistencia = 5,
                decPrecio = 50.00m,
            };

            await controller.Create(dto);

            context.ProProducto.Count().Should().Be(1);
            var saved = context.ProProducto.First();
            saved.strNombreProducto.Should().Be("Producto Persistente");
            saved.intNumeroExistencia.Should().Be(5);
            saved.decPrecio.Should().Be(50.00m);
        }

        // ============ Trimming ============

        [Fact]
        public async Task Create_TrimsNombreProducto()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "  Producto Con Espacios  ",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            await controller.Create(dto);

            var saved = context.ProProducto.First();
            saved.strNombreProducto.Should().Be("Producto Con Espacios");
        }

        // ============ Optional Fields ============

        [Fact]
        public async Task Create_WithImagenNull_Succeeds()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Sin Imagen",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
            var saved = context.ProProducto.First();
            saved.strURLImagen.Should().BeNull();
        }

        [Fact]
        public async Task Create_WithDescripcionNull_Succeeds()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);
            var dto = new ProductoCreateDto
            {
                strNombreProducto = "Producto Sin Descripcion",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var result = await controller.Create(dto);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
            var saved = context.ProProducto.First();
            saved.strDescripcion.Should().BeNull();
        }

        // ============ Duplicates (allowed) ============

        [Fact]
        public async Task Create_AllowsDuplicateNombreProducto()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var dto1 = new ProductoCreateDto
            {
                strNombreProducto = "NombreComun",
                intNumeroExistencia = 10,
                decPrecio = 99.99m,
            };

            var dto2 = new ProductoCreateDto
            {
                strNombreProducto = "NombreComun",
                intNumeroExistencia = 20,
                decPrecio = 199.99m,
            };

            await controller.Create(dto1);
            var result = await controller.Create(dto2);

            result.Result.Should().BeOfType<CreatedAtActionResult>();
            context.ProProducto.Count().Should().Be(2);
        }
    }
}
