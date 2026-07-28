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
    public class QueryTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;

        public QueryTests()
        {
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
            _cacheMock
                .Setup(x => x.GetOrCreateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<PagedResult<CliClienteDto>>>>(),
                    It.IsAny<TimeSpan?>()))
                .Returns<string, Func<Task<PagedResult<CliClienteDto>>>, TimeSpan?>(
                    (_, factory, _) => factory());
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

        // ============ GET /buscar ============

        [Fact]
        public async Task SearchByName_ReturnsMatchingClientes()
        {
            var context = SeedClientes("Eduardo", "Edel", "Freddy", "Maria", "Jose");
            var controller = CreateController(context);

            var result = await controller.SearchByName("ed");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;
            pagedResult!.Items.Select(i => i.strNombreCliente).Should().BeEquivalentTo(["Eduardo", "Edel", "Freddy"]);
            pagedResult.TotalCount.Should().Be(3);
        }

        [Fact]
        public async Task SearchByName_ReturnsEmpty_WhenNoMatch()
        {
            var context = SeedClientes("Eduardo", "Edel");
            var controller = CreateController(context);

            var result = await controller.SearchByName("xyz");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;
            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task SearchByName_IsCaseInsensitive()
        {
            var context = SeedClientes("Eduardo", "edel", "EDUARDO");
            var controller = CreateController(context);

            var result = await controller.SearchByName("eduardo");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;
            pagedResult!.Items.Should().HaveCount(2);
        }

        [Fact]
        public async Task SearchByName_WithPagination_ReturnsCorrectPage()
        {
            var context = SeedClientes("admin1", "admin2", "admin3", "admin4", "admin5", "user1", "user2");
            var controller = CreateController(context);

            var result = await controller.SearchByName("admin", new QueryParams { PageNumber = 2, PageSize = 2 });

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;
            pagedResult!.Items.Should().HaveCount(2);
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(2);
            pagedResult.Items.First().strNombreCliente.Should().Be("admin3");
            pagedResult.Items.Last().strNombreCliente.Should().Be("admin4");
        }

        [Fact]
        public async Task SearchByName_EmptyTexto_ReturnsBadRequest()
        {
            var context = SeedClientes("Eduardo", "Edel", "Maria");
            var controller = CreateController(context);

            var result = await controller.SearchByName("");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SearchByName_WithWhitespace_ReturnsBadRequest()
        {
            var context = SeedClientes("Eduardo", "Edel", "Maria");
            var controller = CreateController(context);

            var result = await controller.SearchByName("   ");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SearchByName_SpecialChars_ReturnsMatch()
        {
            var context = SeedClientes("José", "Maria", "Jose");
            var controller = CreateController(context);

            var result = await controller.SearchByName("Jos");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;
            pagedResult!.Items.Should().HaveCount(2);
        }
    }
}
