using FluentAssertions;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace UnitTest.TipoEmpleado;

public class GetTests
{
    private readonly Mock<ICacheService> _cacheMock;

    public GetTests()
    {
        _cacheMock = new Mock<ICacheService>();
        _cacheMock
            .Setup(x => x.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<PagedResult<EmpCatTipoEmpleadoDto>>>>(),
                It.IsAny<TimeSpan?>()))
            .Returns<string, Func<Task<PagedResult<EmpCatTipoEmpleadoDto>>>, TimeSpan?>(
                (_, factory, _) => factory());
    }

    private TipoEmpleadoController CreateController(AppDbContext context)
    {
        return new TipoEmpleadoController(new WebAPIDevSecOps.Services.TipoEmpleadoService(context, _cacheMock.Object));
    }

    // ============ GET ALL ============

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpCatTipoEmpleadoDto>;
        pagedResult!.Items.Should().BeEmpty();
        pagedResult.TotalCount.Should().Be(0);
        pagedResult.PageNumber.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetAll_ReturnsItemsWithDefaultPagination()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpCatTipoEmpleado.Add(new EmpCatTipoEmpleado { strValor = "Tipo1", strDescripcion = "Desc1" });
        context.EmpCatTipoEmpleado.Add(new EmpCatTipoEmpleado { strValor = "Tipo2", strDescripcion = "Desc2" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpCatTipoEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(2);
        pagedResult.TotalCount.Should().Be(2);
        pagedResult.PageNumber.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetAll_WithCustomPageSize_ReturnsCorrectSize()
    {
        var context = DbContextMock.GetDbContext();
        for (int i = 0; i < 5; i++)
            context.EmpCatTipoEmpleado.Add(new EmpCatTipoEmpleado { strValor = $"Tipo{i}", strDescripcion = $"Desc{i}" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.GetAll(new QueryParams { PageSize = 2 });

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpCatTipoEmpleadoDto>;
        pagedResult!.Items.Should().HaveCount(2);
        pagedResult.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task GetAll_NullQueryParams_UsesDefaults()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<EmpCatTipoEmpleadoDto>;
        pagedResult!.PageNumber.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }

    // ============ GET BY ID ============

    [Fact]
    public async Task GetById_ExistingId_ReturnsDto()
    {
        var context = DbContextMock.GetDbContext();
        var tipo = new EmpCatTipoEmpleado { strValor = "Admin", strDescripcion = "Administrativo" };
        context.EmpCatTipoEmpleado.Add(tipo);
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Get(tipo.id);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var dto = okResult!.Value as EmpCatTipoEmpleadoDto;
        dto!.strValor.Should().Be("Admin");
        dto.strDescripcion.Should().Be("Administrativo");
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
}
