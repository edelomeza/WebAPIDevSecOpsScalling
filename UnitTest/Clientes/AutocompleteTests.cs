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

namespace UnitTest.Clientes
{
    public class AutocompleteTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private readonly Mock<IUserAccessor> _userAccessorMock;

        public AutocompleteTests()
        {
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
            _userAccessorMock = new Mock<IUserAccessor>();
            _userAccessorMock.Setup(u => u.IsAdmin()).Returns(true);
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private ClienteController CreateController(AppDbContext context)
        {
            return new ClienteController(new ClienteService(context, _dbResilience, _cacheMock.Object, _userAccessorMock.Object));
        }

        private AppDbContext SeedClientes(params string[] nombres)
        {
            var context = DbContextMock.GetDbContext();
            foreach (var nombre in nombres)
            {
                context.CliCliente.Add(new CliCliente
                {
                    strNombreCliente = nombre,
                    strCorreoElectronico = $"{nombre.ToLowerInvariant()}@test.com",
                    strNumeroTelefono = "5512345678",
                });
            }
            context.SaveChanges();
            return context;
        }

        [Fact]
        public async Task Autocomplete_ReturnsMatchingClientes()
        {
            var context = SeedClientes("Juan Perez", "Juan Ramirez", "Maria Lopez");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Juan");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.strNombreCliente.StartsWith("Juan"));
        }

        [Fact]
        public async Task Autocomplete_ReturnsEmpty_WhenNoMatch()
        {
            var context = SeedClientes("Juan Perez", "Maria Lopez");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Zzzz");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().BeEmpty();
        }

        [Fact]
        public async Task Autocomplete_IsCaseInsensitive()
        {
            var context = SeedClientes("Eduardo Sanchez", "MARIA LOPEZ");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("eduardo");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(1);
            items.First().strNombreCliente.Should().Be("Eduardo Sanchez");
        }

        [Fact]
        public async Task Autocomplete_RespectsMaxResultados()
        {
            var context = SeedClientes("Ana Lopez", "Ana Martinez", "Ana Garcia", "Ana Torres", "Ana Ruiz");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Ana", 3);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(3);
        }

        [Fact]
        public async Task Autocomplete_DefaultMaxResultados_Is10()
        {
            var nombres = Enumerable.Range(1, 15).Select(i => $"Cliente{i:D2}").ToArray();
            var context = SeedClientes(nombres);
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Cliente");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(10);
        }

        [Fact]
        public async Task Autocomplete_ReturnsOrderedByName()
        {
            var context = SeedClientes("Zara Uno", "Ana Dos");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("a");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.First().strNombreCliente.Should().Be("Ana Dos");
            items.Last().strNombreCliente.Should().Be("Zara Uno");
        }

        [Fact]
        public async Task Autocomplete_ReturnsIdAndNombreCliente()
        {
            var context = SeedClientes("Test Cliente");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Test");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(1);
            items.First().id.Should().BeGreaterThan(0);
            items.First().strNombreCliente.Should().Be("Test Cliente");
        }

        [Fact]
        public async Task Autocomplete_WithEmptyTexto_ReturnsBadRequest()
        {
            var context = SeedClientes("Juan Perez");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Autocomplete_WithWhitespaceTexto_ReturnsBadRequest()
        {
            var context = SeedClientes("Juan Perez");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("   ");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Autocomplete_SpecialChars_ReturnsMatch()
        {
            var context = SeedClientes("José Hernández", "Jorge García");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Jos");

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(1);
            items.First().strNombreCliente.Should().Be("José Hernández");
        }

        [Fact]
        public async Task Autocomplete_MaxResultadosClamped_WhenExceeds50()
        {
            var nombres = Enumerable.Range(1, 60).Select(i => $"Cliente{i:D2}").ToArray();
            var context = SeedClientes(nombres);
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Cliente", 100);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(10);
        }

        [Fact]
        public async Task Autocomplete_MaxResultadosClamped_WhenLessThan1()
        {
            var context = SeedClientes("Juan Perez");
            var controller = CreateController(context);

            var result = await controller.Autocomplete("Juan", 0);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var items = okResult!.Value as IEnumerable<CliClienteAutocompleteDto>;

            items.Should().HaveCount(1);
        }
    }
}
