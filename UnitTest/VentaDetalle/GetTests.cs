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
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Services;

namespace UnitTest.VentaDetalle
{
    public class GetTests
    {
        private readonly DbResilienceService _dbResilience;

        public GetTests()
        {
            _dbResilience = CreateDbResilience();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private VentaDetalleController CreateController(AppDbContext context)
        {
            var userMock = new Mock<IUserAccessor>();
            userMock.Setup(u => u.GetCurrentUsername()).Returns("Test User");
            return new VentaDetalleController(
                new VentaDetalleService(context, _dbResilience, userMock.Object));
        }

        private async Task<(AppDbContext context, int ventaId)> SeedAsync(int detalleCount = 0)
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
            }

            var producto = new ProProducto { strNombreProducto = "Producto Test", intNumeroExistencia = 10, decPrecio = 99.99m, RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.ProProducto.Add(producto);
            await context.SaveChangesAsync();

            var venta = new VenVenta { idCliCliente = cliente.id, idSegUsuario = usuario.id, idVenCatEstado = 1, dteFechaHoraCompra = DateTime.UtcNow, strClaveVenta = "CLV0000001", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.VenVenta.Add(venta);
            await context.SaveChangesAsync();

            if (detalleCount > 0)
            {
                context.Set<VenVentaDetalle>().AddRange(
                    Enumerable.Range(1, detalleCount).Select(i => new VenVentaDetalle
                    {
                        idVenVenta = venta.id,
                        idProProducto = producto.id,
                        intPiezaVenta = i,
                        decTotalVenta = i * producto.decPrecio,
                        RowVersion = new byte[] { 1, 0, 0, 0 },
                    })
                );
                await context.SaveChangesAsync();
            }

            return (context, venta.id);
        }

        [Fact]
        public async Task GetAll_ReturnsEmptyPagedResult_WhenNoDetalles()
        {
            var (context, _) = await SeedAsync();
            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(0);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.TotalPages.Should().Be(0);
        }

        [Fact]
        public async Task GetAll_ReturnsPagedResult_WithDefaultPagination()
        {
            var (context, ventaId) = await SeedAsync(5);
            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.TotalPages.Should().Be(1);
        }

        [Fact]
        public async Task GetAll_ReturnsCorrectPageSize()
        {
            var (context, ventaId) = await SeedAsync(15);
            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageSize = 5 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(15);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(5);
            pagedResult.TotalPages.Should().Be(3);
        }

        [Fact]
        public async Task GetAll_ReturnsCorrectPageNumber()
        {
            var (context, ventaId) = await SeedAsync(25);
            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 2, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.Items.Should().HaveCount(10);
            pagedResult.TotalCount.Should().Be(25);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);
        }

        [Fact]
        public async Task GetAll_ReturnsRemainingItems_OnLastPage()
        {
            var (context, ventaId) = await SeedAsync(25);
            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 3, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(25);
            pagedResult.PageNumber.Should().Be(3);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);
        }

        [Fact]
        public async Task GetAll_ReturnsEmpty_WhenPageExceedsTotal()
        {
            var (context, ventaId) = await SeedAsync(1);
            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 10, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(1);
            pagedResult.PageNumber.Should().Be(10);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(1);
        }

        [Fact]
        public async Task GetAll_WithNullQueryParams_UsesDefaults()
        {
            var (context, ventaId) = await SeedAsync(3);
            var controller = CreateController(context);

            var result = await controller.GetAll(null);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDetalleDto>;

            pagedResult!.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.Items.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetById_ReturnsDetalle_WhenExists()
        {
            var (context, ventaId) = await SeedAsync(1);
            var detalle = context.Set<VenVentaDetalle>().First();
            var controller = CreateController(context);

            var result = await controller.Get(detalle.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as VenVentaDetalleDto;

            dto!.id.Should().Be(detalle.id);
            dto.idVenVenta.Should().Be(ventaId);
            dto.idProProducto.Should().Be(detalle.idProProducto);
            dto.intPiezaVenta.Should().Be(detalle.intPiezaVenta);
            dto.decTotalVenta.Should().Be(detalle.decTotalVenta);
            dto.strNombreProducto.Should().Be("Producto Test");
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WhenNotExists()
        {
            var (context, _) = await SeedAsync();
            var controller = CreateController(context);

            var result = await controller.Get(999);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WithNegativeId()
        {
            var (context, _) = await SeedAsync();
            var controller = CreateController(context);

            var result = await controller.Get(-1);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WithZeroId()
        {
            var (context, _) = await SeedAsync();
            var controller = CreateController(context);

            var result = await controller.Get(0);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsCorrectDetalle_WhenMultipleDetallesExist()
        {
            var (context, ventaId) = await SeedAsync(5);
            var target = context.Set<VenVentaDetalle>().First(vd => vd.intPiezaVenta == 3);
            var controller = CreateController(context);

            var result = await controller.Get(target.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as VenVentaDetalleDto;

            dto!.id.Should().Be(target.id);
            dto.intPiezaVenta.Should().Be(3);
        }
    }
}
