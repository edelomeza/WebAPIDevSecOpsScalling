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
    public class QueryTests
    {
        private readonly DbResilienceService _dbResilience;

        public QueryTests()
        {
            _dbResilience = CreateDbResilience();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private VentaController CreateController(AppDbContext context)
        {
            return new VentaController(new VentaService(context, _dbResilience, Moq.Mock.Of<WebAPIDevSecOps.Interfaces.IEventPublisher>(), SagaBridgeTestConfig.BridgeOff(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<WebAPIDevSecOps.Services.VentaService>>()));
        }

        private async Task<(CliCliente cliente, SegUsuario usuario)> SeedDependenciesAsync(AppDbContext context)
        {
            var cliente = new CliCliente { strNombreCliente = "Cliente Busqueda", strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "User Test", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
                await context.SaveChangesAsync();
            }
            return (cliente, usuario);
        }

        [Fact]
        public async Task Search_ByClaveVenta_ReturnsMatching()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);

            context.Set<VenVenta>().AddRange(
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "ABC123XYZ1"),
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "DEF456UVW2"),
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "ABC789RST3")
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(strClaveVenta: "ABC");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(2);
            pagedResult.TotalCount.Should().Be(2);
        }

        [Fact]
        public async Task Search_ByClaveVenta_ReturnsEmpty_WhenNoMatch()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);

            context.Set<VenVenta>().Add(TestDataFactory.CreateVenta(cliente.id, usuario.id, "UNICO12345"));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(strClaveVenta: "ZZZZ");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task Search_ByNombreCliente_ReturnsMatching()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Juan Perez", strCorreoElectronico = $"juan{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var otroCliente = new CliCliente { strNombreCliente = "Maria Lopez", strCorreoElectronico = $"maria{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.AddRange(cliente, otroCliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
                await context.SaveChangesAsync();
            }

            context.Set<VenVenta>().AddRange(
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "CLAVEJUAN1"),
                TestDataFactory.CreateVenta(otroCliente.id, usuario.id, "CLAVEMARIA")
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(strNombreCliente: "Juan");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(1);
            pagedResult.TotalCount.Should().Be(1);
            pagedResult.Items.First().strClaveVenta.Should().Be("CLAVEJUAN1");
        }

        [Fact]
        public async Task Search_ByDateRange_ReturnsMatching()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);

            var ventas = new List<VenVenta>
            {
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "FECHA00111"),
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "FECHA00222"),
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "FECHA00333"),
            };
            ventas[0].dteFechaHoraCompra = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);
            ventas[1].dteFechaHoraCompra = new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc);
            ventas[2].dteFechaHoraCompra = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

            context.Set<VenVenta>().AddRange(ventas);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(dteFechaInicio: new DateTime(2026, 1, 1), dteFechaFin: new DateTime(2026, 1, 20));

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(2);
            pagedResult.TotalCount.Should().Be(2);
            pagedResult.Items.Select(i => i.strClaveVenta).Should().Contain(["FECHA00111", "FECHA00222"]);
        }

        [Fact]
        public async Task Search_ByDateRange_NoTime_ReturnsEntireDay()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);

            var venta = TestDataFactory.CreateVenta(cliente.id, usuario.id, "FECHA00444");
            venta.dteFechaHoraCompra = new DateTime(2026, 1, 15, 23, 59, 59, DateTimeKind.Utc);

            context.Set<VenVenta>().Add(venta);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(dteFechaInicio: new DateTime(2026, 1, 15), dteFechaFin: new DateTime(2026, 1, 15));

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(1);
        }

        [Fact]
        public async Task Search_CombinedFilters_ReturnsMatching()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente { strNombreCliente = "Carlos Ruiz", strCorreoElectronico = $"carlos{Guid.NewGuid():N}@test.com", strNumeroTelefono = "5512345678", RowVersion = new byte[] { 1, 0, 0, 0 } };
            var usuario = new SegUsuario { strNombre = "User", strPWD = "hash", strCorreoElectronico = $"user{Guid.NewGuid():N}@test.com", RowVersion = new byte[] { 1, 0, 0, 0 } };
            context.CliCliente.Add(cliente);
            context.SegUsuario.Add(usuario);
            await context.SaveChangesAsync();
            if (!await context.VenCatEstado.AnyAsync())
            {
                context.VenCatEstado.AddRange(
                    new VenCatEstado { id = 1, strValor = "En compra", strDescripcion = "Compra en proceso" },
                    new VenCatEstado { id = 2, strValor = "Pagado", strDescripcion = "Compra pagada" },
                    new VenCatEstado { id = 3, strValor = "Cancelado", strDescripcion = "Compra cancelada" }
                );
                await context.SaveChangesAsync();
            }

            var venta = TestDataFactory.CreateVenta(cliente.id, usuario.id, "CARLOS9999");
            venta.dteFechaHoraCompra = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
            context.Set<VenVenta>().Add(venta);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(
                strClaveVenta: "CARLOS",
                strNombreCliente: "Ruiz",
                dteFechaInicio: new DateTime(2026, 3, 1),
                dteFechaFin: new DateTime(2026, 3, 31));

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(1);
            pagedResult.TotalCount.Should().Be(1);
        }

        [Fact]
        public async Task Search_NoFilters_ReturnsAll()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);

            context.Set<VenVenta>().AddRange(
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "ALLDATA001"),
                TestDataFactory.CreateVenta(cliente.id, usuario.id, "ALLDATA002")
            );
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(null, null, null, null);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(2);
            pagedResult.TotalCount.Should().Be(2);
        }

        [Fact]
        public async Task Search_WithPagination_ReturnsCorrectPage()
        {
            var context = DbContextMock.GetDbContext();
            var (cliente, usuario) = await SeedDependenciesAsync(context);

            for (int i = 1; i <= 5; i++)
                context.Set<VenVenta>().Add(TestDataFactory.CreateVenta(cliente.id, usuario.id, $"PAGINA{i:D4}"));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Search(strClaveVenta: "PAGINA", queryParams: new QueryParams { PageNumber = 2, PageSize = 2 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<VenVentaDto>;

            pagedResult!.Items.Should().HaveCount(2);
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(2);
        }
    }
}
