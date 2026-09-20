using FluentAssertions;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace UnitTest.EstadoVenta;

public class GetTests
{
    private readonly Mock<ICacheService> _cacheMock;

    public GetTests()
    {
        _cacheMock = new Mock<ICacheService>();
        _cacheMock
            .Setup(x => x.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<PagedResult<VenCatEstadoDto>>>>(),
                It.IsAny<TimeSpan?>()))
            .Returns<string, Func<Task<PagedResult<VenCatEstadoDto>>>, TimeSpan?>(
                (_, factory, _) => factory());
        _cacheMock
            .Setup(x => x.GetAsync<VenCatEstadoDto>(It.IsAny<string>()))
            .ReturnsAsync((VenCatEstadoDto?)null);
    }

    private EstadoVentaController CreateController(AppDbContext context)
    {
        return new EstadoVentaController(new WebAPIDevSecOps.Services.VenCatEstadoService(context, _cacheMock.Object));
    }

    [Fact]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyPagedResult()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<VenCatEstadoDto>;
        pagedResult!.Items.Should().BeEmpty();
        pagedResult.TotalCount.Should().Be(0);
        pagedResult.PageNumber.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetAll_ReturnsItemsWithDefaultPagination()
    {
        var context = DbContextMock.GetDbContext();
        context.VenCatEstado.Add(new VenCatEstado { strValor = "Activo", strDescripcion = "Activo" });
        context.VenCatEstado.Add(new VenCatEstado { strValor = "Inactivo", strDescripcion = "Inactivo" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.GetAll(null);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<VenCatEstadoDto>;
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
            context.VenCatEstado.Add(new VenCatEstado { strValor = $"Estado{i}", strDescripcion = $"Desc{i}" });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.GetAll(new QueryParams { PageSize = 2 });

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var pagedResult = okResult!.Value as PagedResult<VenCatEstadoDto>;
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
        var pagedResult = okResult!.Value as PagedResult<VenCatEstadoDto>;
        pagedResult!.PageNumber.Should().Be(1);
        pagedResult.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsDto()
    {
        var context = DbContextMock.GetDbContext();
        var estado = new VenCatEstado { strValor = "Activo", strDescripcion = "Estado activo" };
        context.VenCatEstado.Add(estado);
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        var result = await controller.Get(estado.id);

        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        var dto = okResult!.Value as VenCatEstadoDto;
        dto!.strValor.Should().Be("Activo");
        dto.strDescripcion.Should().Be("Estado activo");
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
