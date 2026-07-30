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
    public class GetTests
    {
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private readonly Mock<IUserAccessor> _userAccessorMock;

        public GetTests()
        {
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<ICacheService>();
            _userAccessorMock = new Mock<IUserAccessor>();
            _userAccessorMock.Setup(u => u.IsAdmin()).Returns(true);
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
            return new ClienteController(new ClienteService(context, _dbResilience, _cacheMock.Object, _userAccessorMock.Object));
        }

        // ============ GetAll() ============

        [Fact]
        public async Task GetAll_ReturnsEmptyPagedResult_WhenNoClientes()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

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
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(5));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

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
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(50));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

            pagedResult!.Items.Should().HaveCount(10);
            pagedResult.TotalCount.Should().Be(50);
            pagedResult.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(5);
        }

        [Fact]
        public async Task GetAll_ReturnsCorrectPageNumber()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(30));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 2, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

            pagedResult!.Items.Should().HaveCount(10);
            pagedResult.TotalCount.Should().Be(30);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);

            pagedResult.Items.First().strNombreCliente.Should().Be("cliente11");
            pagedResult.Items.Last().strNombreCliente.Should().Be("cliente20");
        }

        [Fact]
        public async Task GetAll_ReturnsRemainingItems_OnLastPage()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(25));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 3, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(25);
            pagedResult.PageNumber.Should().Be(3);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);

            pagedResult.Items.First().strNombreCliente.Should().Be("cliente21");
            pagedResult.Items.Last().strNombreCliente.Should().Be("cliente25");
        }

        [Fact]
        public async Task GetAll_ReturnsEmpty_WhenPageExceedsTotal()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(5));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 10, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

            pagedResult!.Items.Should().BeEmpty();
            pagedResult.TotalCount.Should().Be(5);
            pagedResult.PageNumber.Should().Be(10);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(1);
        }

        [Fact]
        public async Task GetAll_WithNullQueryParams_UsesDefaults()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(3));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(null);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<CliClienteDto>;

            pagedResult!.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.Items.Should().HaveCount(3);
        }

        // ============ GetById() ============

        [Fact]
        public async Task GetById_ReturnsCliente_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var cliente = new CliCliente
            {
                strNombreCliente = "Cliente Test",
                strCorreoElectronico = "test@test.com",
                strNumeroTelefono = "5512345678",
                RowVersion = new byte[] { 1, 0, 0, 0 },
            };
            context.CliCliente.Add(cliente);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Get(cliente.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as CliClienteDto;

            dto!.id.Should().Be(cliente.id);
            dto.strNombreCliente.Should().Be("Cliente Test");
            dto.strCorreoElectronico.Should().Be("test@test.com");
            dto.strNumeroTelefono.Should().Be("5512345678");
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
        public async Task GetById_ReturnsCorrectCliente_WhenMultipleClientesExist()
        {
            var context = DbContextMock.GetDbContext();
            context.CliCliente.AddRange(TestDataFactory.CreateClientes(5));
            await context.SaveChangesAsync();

            var target = context.CliCliente.First(c => c.strNombreCliente == "cliente3");

            var controller = CreateController(context);

            var result = await controller.Get(target.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as CliClienteDto;

            dto!.id.Should().Be(target.id);
            dto.strNombreCliente.Should().Be("cliente3");
            dto.strCorreoElectronico.Should().Be("cliente3@test.com");
            dto.strNumeroTelefono.Should().Be("5500000003");
        }
    }
}
