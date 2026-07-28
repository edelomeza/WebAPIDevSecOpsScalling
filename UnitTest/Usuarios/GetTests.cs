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
    public class GetTests
    {
        private readonly Mock<IPasswordHasherService> _hasherMock;
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<ICacheService> _cacheMock;
        private const string FakeHash = "$argon2id$v=19$m=16384,t=2,p=1$test$hash";

        public GetTests()
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

        // ============ GetAll() ============

        [Fact]
        public async Task GetAll_ReturnsEmptyPagedResult_WhenNoUsers()
        {
            var context = DbContextMock.GetDbContext();
            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

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
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(5, FakeHash));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll();

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

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
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(50, FakeHash));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

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
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(30, FakeHash));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 2, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

            pagedResult!.Items.Should().HaveCount(10);
            pagedResult.TotalCount.Should().Be(30);
            pagedResult.PageNumber.Should().Be(2);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);

            pagedResult.Items.First().strNombre.Should().Be("user11");
            pagedResult.Items.Last().strNombre.Should().Be("user20");
        }

        [Fact]
        public async Task GetAll_ReturnsRemainingItems_OnLastPage()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(25, FakeHash));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 3, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

            pagedResult!.Items.Should().HaveCount(5);
            pagedResult.TotalCount.Should().Be(25);
            pagedResult.PageNumber.Should().Be(3);
            pagedResult.PageSize.Should().Be(10);
            pagedResult.TotalPages.Should().Be(3);

            pagedResult.Items.First().strNombre.Should().Be("user21");
            pagedResult.Items.Last().strNombre.Should().Be("user25");
        }

        [Fact]
        public async Task GetAll_ReturnsEmpty_WhenPageExceedsTotal()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(5, FakeHash));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(new QueryParams { PageNumber = 10, PageSize = 10 });

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

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
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(3, FakeHash));
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll(null);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

            pagedResult!.PageNumber.Should().Be(1);
            pagedResult.PageSize.Should().Be(20);
            pagedResult.Items.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetAll_DoesNotExposePassword()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "user1",
                strCorreoElectronico = "user1@test.com",
                strPWD = FakeHash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.GetAll();

            var okResult = result.Result as OkObjectResult;
            var pagedResult = okResult!.Value as PagedResult<SegUsuarioDto>;

            var dto = pagedResult!.Items.First();
            dto.id.Should().BeGreaterThan(0);
            dto.strNombre.Should().Be("user1");
            dto.strCorreoElectronico.Should().Be("user1@test.com");
            dto.GetType().GetProperty("strPWD").Should().BeNull();
        }

        // ============ GetById() ============

        [Fact]
        public async Task GetById_ReturnsUser_WhenExists()
        {
            var context = DbContextMock.GetDbContext();
            var user = new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = FakeHash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            };
            context.SegUsuario.Add(user);
            await context.SaveChangesAsync();

            var controller = CreateController(context);

            var result = await controller.Get(user.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as SegUsuarioDto;

            dto!.id.Should().Be(user.id);
            dto.strNombre.Should().Be("testuser");
            dto.strCorreoElectronico.Should().Be("test@test.com");
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
        public async Task GetById_ReturnsCorrectUser_WhenMultipleUsersExist()
        {
            var context = DbContextMock.GetDbContext();
            context.SegUsuario.AddRange(TestDataFactory.CreateUsers(5, FakeHash));
            await context.SaveChangesAsync();

            var targetUser = context.SegUsuario.First(u => u.strNombre == "user3");

            var controller = CreateController(context);

            var result = await controller.Get(targetUser.id);

            result.Result.Should().BeOfType<OkObjectResult>();
            var okResult = result.Result as OkObjectResult;
            var dto = okResult!.Value as SegUsuarioDto;

            dto!.id.Should().Be(targetUser.id);
            dto.strNombre.Should().Be("user3");
            dto.strCorreoElectronico.Should().Be("user3@test.com");
        }
    }
}
