using FluentAssertions;
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

namespace UnitTest.Empleado;

public class GetTests
{
    private readonly DbResilienceService _dbResilience;
    private readonly Mock<ICacheService> _cacheMock;

    public GetTests()
    {
        _dbResilience = CreateDbResilience();
        _cacheMock = new Mock<ICacheService>();
        _cacheMock
            .Setup(x => x.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<PagedResult<EmpEmpleadoDto>>>>(),
                It.IsAny<TimeSpan?>()))
            .Returns<string, Func<Task<PagedResult<EmpEmpleadoDto>>>, TimeSpan?>(
                (_, factory, _) => factory());
    }

    private static DbResilienceService CreateDbResilience()
    {
        var options = Options.Create(new ResilienceOptions());
        var logger = new Mock<ILogger<DbResilienceService>>();
        return new DbResilienceService(options, logger.Object);
    }

    private EmpleadoController CreateController(AppDbContext context)
    {
        return new EmpleadoController(new EmpleadoService(context, _dbResilience, _cacheMock.Object));
    }

    // ============ GET ALL ============

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().BeEmpty();
        pagedResult.TotalCount.Should().Be(0);
        pagedResult.PageNumber.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetAll_WithData_ReturnsItemsWithDefaultPagination()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Uno" });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Dos" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(2);
        pagedResult.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_WithCustomPageSize_ReturnsCorrectSize()
    {
        var context = DbContextMock.GetDbContext();
        for (int i = 0; i < 10; i++)
            context.EmpEmpleado.Add(new EmpEmpleado { strNombre = $"User{i}" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.GetAll(new QueryParams { PageSize = 3 });

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(3);
        pagedResult.PageSize.Should().Be(3);
    }

    // ============ GET BY ID ============

    [Fact]
    public async Task GetById_ExistingId_ReturnsEmpleado()
    {
        var context = DbContextMock.GetDbContext();
        var emp = new EmpEmpleado { strNombre = "Juan", strAPaterno = "Pérez" };
        context.EmpEmpleado.Add(emp);
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Get(emp.id);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var dto = okResult!.Value as EmpEmpleadoDto;
        dto!.strNombre.Should().Be("Juan");
        dto.strAPaterno.Should().Be("Pérez");
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNotFound()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.Get(999);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.NotFoundResult>();
    }

    [Fact]
    public async Task GetById_NegativeId_ReturnsNotFound()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.Get(-1);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.NotFoundResult>();
    }

    [Fact]
    public async Task GetById_ZeroId_ReturnsNotFound()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.Get(0);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.NotFoundResult>();
    }

    // ============ SEARCH ============

    [Fact]
    public async Task Search_ByText_ReturnsMatchingRecords()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan", strAPaterno = "Pérez" });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "María", strAPaterno = "López" });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Pedro", strAPaterno = "Juan" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("Juan", null, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_ByTextInAPaterno_ReturnsMatchingRecords()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan", strAPaterno = "Pérez" });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "María", strAPaterno = "López" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("Pérez", null, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(1);
        pagedResult.Items.First().strNombre.Should().Be("Juan");
    }

    [Fact]
    public async Task Search_ByTextInAMaterno_ReturnsMatchingRecords()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan", strAMaterno = "García" });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "María", strAMaterno = "López" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("García", null, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Search_ByTipoEmpleado_ReturnsFiltered()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan", idEmpCatTipoEmpleado = 1 });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "María", idEmpCatTipoEmpleado = 2 });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Pedro", idEmpCatTipoEmpleado = 1 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search(null, 1, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_ByTextAndTipo_ReturnsIntersection()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan", idEmpCatTipoEmpleado = 1 });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan", idEmpCatTipoEmpleado = 2 });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "María", idEmpCatTipoEmpleado = 1 });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("Juan", 1, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("NoExiste", null, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_EmptyText_ReturnsAll()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "Juan" });
        context.EmpEmpleado.Add(new EmpEmpleado { strNombre = "María" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("", null, null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_AppliesPagination()
    {
        var context = DbContextMock.GetDbContext();
        for (int i = 0; i < 10; i++)
            context.EmpEmpleado.Add(new EmpEmpleado { strNombre = $"Juan{i}" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Search("Juan", null, new QueryParams { PageNumber = 1, PageSize = 3 });

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(3);
        pagedResult.TotalCount.Should().Be(10);
    }
}
