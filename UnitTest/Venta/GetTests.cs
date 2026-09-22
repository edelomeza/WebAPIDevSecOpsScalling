using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Services;

namespace UnitTest.Venta
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

        private async Task SeedEstadoAsync(AppDbContext context)
        {
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
                await context.SaveChangesAsync();
            }
        }

        private VentaController CreateController(AppDbContext context)
        {
            return new VentaController(new VentaService(context, _dbResilience, Moq.Mock.Of<WebAPIDevSecOps.Interfaces.IEventPublisher>(), SagaBridgeTestConfig.BridgeOff(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<WebAPIDevSecOps.Services.VentaService>>()));
        }

        [Fact]
        public async Task GetAll_ReturnsEmptyPagedResult_WhenNoVentas()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(0);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.TotalPages.Should().Be(0);
        }

        [Fact]
        public async Task GetAll_ReturnsPagedResult_WithDefaultPagination()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().AddRange(TestDataFactory.CreateVentas(5, cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.TotalPages.Should().Be(1);
        }

        [Fact]
        public async Task GetAll_ReturnsCorrectPageSize()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli2@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user2@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().AddRange(TestDataFactory.CreateVentas(15, cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageSize = 5 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(15);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(5);
            pagedResult.TotalPages.Should().Be(3);
        }

        [Fact]
        public async Task GetAll_ReturnsCorrectPageNumber()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli3@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user3@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().AddRange(TestDataFactory.CreateVentas(25, cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 2, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(10);
            pagedResult.TotalCount.Should().Be(25);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);
        }

        [Fact]
        public async Task GetAll_ReturnsRemainingItems_OnLastPage()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli4@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user4@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().AddRange(TestDataFactory.CreateVentas(25, cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 3, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(25);
            pagedResult.PageNumber.Should().Be(3);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);
        }

        [Fact]
        public async Task GetAll_ReturnsEmpty_WhenPageExceedsTotal()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli5@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user5@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().Add(TestDataFactory.CreateVenta(cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 10, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(1);
            pagedResult.PageNumber.Should().Be(10);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(1);
        }

        [Fact]
        public async Task GetAll_WithNullQueryParams_UsesDefaults()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli6@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user6@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().AddRange(TestDataFactory.CreateVentas(3, cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(null);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.Items.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetById_ReturnsVenta_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli7@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user7@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            var venta = TestDataFactory.CreateVenta(cliente.id, usuario.id, "CLAVE12345");
            context.Set<WebAPIDevSecOps.Models.VenVenta>().Add(venta);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Get(venta.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as VenVentaDto;

            dto!.id.Should().Be(venta.id);
            dto.idCliCliente.Should().Be(cliente.id);
            dto.idSegUsuario.Should().Be(usuario.id);
            dto.strClaveVenta.Should().Be("CLAVE12345");
            dto.idVenCatEstado.Should().Be(1);
            dto.dteFechaHoraCompra.Should().NotBeNull();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WhenNotExists()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.Get(999);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WithNegativeId()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.Get(-1);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsNotFound_WithZeroId()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.Get(0);

            result.Result.Should().BeOfType<NotFoundResult>();
        }

        [Fact]
        public async Task GetById_ReturnsCorrectVenta_WhenMultipleVentasExist()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Test Cliente", strCorreoElectronico = "cli8@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "Test User", strPWD = "hash", strCorreoElectronico = "user8@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            await SeedEstadoAsync(context);

            context.Set<WebAPIDevSecOps.Models.VenVenta>().AddRange(TestDataFactory.CreateVentas(5, cliente.id, usuario.id));
            await context.SaveChangesAsync();

            var target = context.Set<WebAPIDevSecOps.Models.VenVenta>().First(v => v.strClaveVenta == "CLAVE0003");

            var controller = CreateController(context);

            var result = await controller.Get(target.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as VenVentaDto;

            dto!.id.Should().Be(target.id);
            dto.strClaveVenta.Should().Be("CLAVE0003");
        }
    }
}
