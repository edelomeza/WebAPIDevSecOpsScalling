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

namespace UnitTest.VentaDetalle
{
    public class AutocompleteTests
    {
        private readonly DbResilienceService _dbResilience;

        public AutocompleteTests()
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

        private AppDbContext SeedProductos(params (string nombre, int existencia, decimal precio)[] productos)
        {
            var context = DbContextMock.GetDbContext();
            foreach (var (nombre, existencia, precio) in productos)
            {
                context.ProProducto.Add(new ProProducto
                {
                    strNombreProducto = nombre,
                    intNumeroExistencia = existencia,
                    decPrecio = precio,
                    RowVersion = new byte[] { 1, 0, 0, 0 },
                });
            }
            context.SaveChanges();
            return context;
        }

        [Fact]
        public async Task BuscarProducto_ReturnsMatchingProductos()
        {
            var context = SeedProductos(
                ("Laptop HP", 10, 15000.00m),
                ("Laptop Dell", 5, 18000.00m),
                ("Mouse USB", 20, 250.00m)
            );
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Laptop");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.strTextoAutocomplete.StartsWith("Laptop"));
        }

        [Fact]
        public async Task BuscarProducto_ReturnsEmpty_WhenNoMatch()
        {
            var context = SeedProductos(("Laptop HP", 10, 15000.00m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Zzzz");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().BeEmpty();
        }

        [Fact]
        public async Task BuscarProducto_IsCaseInsensitive()
        {
            var context = SeedProductos(("laptop HP", 10, 15000.00m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("LAPTOP");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(1);
        }

        [Fact]
        public async Task BuscarProducto_ReturnsFormattedText()
        {
            var context = SeedProductos(("Laptop HP", 10, 15000.00m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Laptop");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(1);
            var item = items!.First();
            item.strTextoAutocomplete.Should().Be("Laptop HP | #: 10 | $: 15000.00");
        }

        [Fact]
        public async Task BuscarProducto_RespectsMaxResultados()
        {
            var productos = Enumerable.Range(1, 15)
                .Select(i => ("Producto" + i.ToString("D2"), i * 10, i * 100.00m))
                .ToArray();
            var context = SeedProductos(productos);
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Producto", 5);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(5);
        }

        [Fact]
        public async Task BuscarProducto_DefaultMaxResultados_Is10()
        {
            var productos = Enumerable.Range(1, 15)
                .Select(i => ("Prod" + i.ToString("D2"), i * 10, i * 100.00m))
                .ToArray();
            var context = SeedProductos(productos);
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Prod");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(10);
        }

        [Fact]
        public async Task BuscarProducto_ReturnsOrderedByName()
        {
            var context = SeedProductos(
                ("Zebra Mouse", 5, 300.00m),
                ("Apple MacBook", 3, 25000.00m)
            );
            var controller = CreateController(context);

            var result = await controller.Autocomplete("a");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.First().strTextoAutocomplete.Should().StartWith("Apple");
            items.Last().strTextoAutocomplete.Should().StartWith("Zebra");
        }

        [Fact]
        public async Task BuscarProducto_ReturnsIdAndFormattedText()
        {
            var context = SeedProductos(("Producto Único", 10, 99.99m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Único");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(1);
            items.First().id.Should().BeGreaterThan(0);
            items.First().strTextoAutocomplete.Should().Be("Producto Único | #: 10 | $: 99.99");
        }

        [Fact]
        public async Task BuscarProducto_WithEmptyTexto_ReturnsBadRequest()
        {
            var context = SeedProductos(("Laptop", 10, 15000.00m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task BuscarProducto_WithWhitespaceTexto_ReturnsBadRequest()
        {
            var context = SeedProductos(("Laptop", 10, 15000.00m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("   ");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task BuscarProducto_MaxResultadosClamped_WhenExceeds50()
        {
            var productos = Enumerable.Range(1, 60)
                .Select(i => ("Prod" + i.ToString("D2"), i * 10, i * 100.00m))
                .ToArray();
            var context = SeedProductos(productos);
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Prod", 100);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(10);
        }

        [Fact]
        public async Task BuscarProducto_MaxResultadosClamped_WhenLessThan1()
        {
            var context = SeedProductos(("Laptop", 10, 15000.00m));
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Laptop", 0);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<ProProductoAutocompleteDto>;

            items.Should().HaveCount(1);
        }
    }
}
