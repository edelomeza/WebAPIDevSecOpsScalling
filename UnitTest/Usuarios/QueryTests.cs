using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Usuarios
{
    public class QueryTests
    {
        private readonly Mock<IPasswordHasherService> _hasherMock;
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private const string FakeHash = "$argon2id$v=19$m=16384,t=2,p=1$test$hash";

        public QueryTests()
        {
            _hasherMock = new Mock<IPasswordHasherService>();
            _hasherMock.Setup(h => h.HashPassword(It.IsAny<string>())).Returns(FakeHash);
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }

        private UsuarioController CreateController(AppDbContext context)
        {
            return new UsuarioController(new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object));
        }

        private AppDbContext SeedUsers(params string[] nombres)
        {
            var context = DbContextMock.GetDbContext();
            foreach (var nombre in nombres)
            {
                context.SegUsuario.Add(new SegUsuario
                {
                    strNombre = nombre,
                    strPWD = FakeHash,
                    strCorreoElectronico = $"{nombre.ToLowerInvariant()}@test.com"
                });
            }
            context.SaveChanges();
            return context;
        }

        // ============ GET /buscar ============

        [Fact]
        public async Task SearchByName_ReturnsMatchingUsers()
        {
            var context = SeedUsers("Eduardo", "Edel", "Freddy", "Maria", "Jose");
            var controller = CreateController(context);

            var result = await controller.SearchByName("ed");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;
            pagedResult!.Items.Select(i => i.strNombre).Should().BeEquivalentTo(["Eduardo", "Edel", "Freddy"]);
            pagedResult.TotalCount.Should().Be(3);
        }

        [Fact]
        public async Task SearchByName_ReturnsEmpty_WhenNoMatch()
        {
            var context = SeedUsers("Eduardo", "Edel");
            var controller = CreateController(context);

            var result = await controller.SearchByName("xyz");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;
            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(0);
        }

        [Fact]
        public async Task SearchByName_IsCaseInsensitive()
        {
            var context = SeedUsers("Eduardo", "edel", "EDUARDO");
            var controller = CreateController(context);

            var result = await controller.SearchByName("eduardo");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;
            pagedResult!.Items.Should().HaveCount(2);
        }

        [Fact]
        public async Task SearchByName_WithPagination_ReturnsCorrectPage()
        {
            var context = SeedUsers("admin1", "admin2", "admin3", "admin4", "admin5", "user1", "user2");
            var controller = CreateController(context);

            var result = await controller.SearchByName("admin", new QueryParams { PageNumber = 2, PageSize = 2 });

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;
            pagedResult!.Items.Should().HaveCount(2);
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(2);
            pagedResult.Items.First().strNombre.Should().Be("admin3");
            pagedResult.Items.Last().strNombre.Should().Be("admin4");
        }

        [Fact]
        public async Task SearchByName_EmptyTexto_ReturnsBadRequest()
        {
            var context = SeedUsers("Eduardo", "Edel", "Maria");
            var controller = CreateController(context);

            var result = await controller.SearchByName("");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SearchByName_WithWhitespace_ReturnsBadRequest()
        {
            var context = SeedUsers("Eduardo", "Edel", "Maria");
            var controller = CreateController(context);

            var result = await controller.SearchByName("   ");

            result.Result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task SearchByName_SpecialChars_ReturnsMatch()
        {
            var context = SeedUsers("José", "Maria", "Jose");
            var controller = CreateController(context);

            var result = await controller.SearchByName("Jos");

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;
            pagedResult!.Items.Should().HaveCount(2);
        }

        // ============ GET /autocomplete ============

        [Fact]
        public async Task Autocomplete_ReturnsMatchingUsers()
        {
            var context = SeedUsers("Eduardo", "Edel", "Maria", "Jose");
            var service = new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object);

            var result = await service.AutocompleteAsync("ed");

            result.Should().HaveCount(2);
            result.Select(r => r.strNombre).Should().BeEquivalentTo(["Edel", "Eduardo"]);
        }

        [Fact]
        public async Task Autocomplete_RespectsMaxResultados()
        {
            var context = SeedUsers("admin1", "admin2", "admin3", "user1");
            var service = new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object);

            var result = await service.AutocompleteAsync("admin", 2);

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task Autocomplete_ReturnsEmpty_WhenNoMatch()
        {
            var context = SeedUsers("Eduardo", "Edel");
            var service = new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object);

            var result = await service.AutocompleteAsync("xyz");

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task Autocomplete_IsCaseInsensitive()
        {
            var context = SeedUsers("Eduardo", "edel", "EDUARDO");
            var service = new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object);

            var result = await service.AutocompleteAsync("EDUARDO");

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task Autocomplete_SpecialChars_ReturnsMatch()
        {
            var context = SeedUsers("José", "Maria", "Jose");
            var service = new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object);

            var result = await service.AutocompleteAsync("Jos");

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task Autocomplete_ReturnsOrderedByName()
        {
            var context = SeedUsers("Zeta", "Alpha", "Beta");
            var service = new UsuarioService(context, _hasherMock.Object, _dbResilience, _cacheMock.Object);

            var result = await service.AutocompleteAsync("a");

            result.Select(r => r.strNombre).Should().BeInAscendingOrder();
        }
    }
}
