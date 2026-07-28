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

namespace UnitTest.Empleado;

public class InsertTests
{
    private readonly DbResilienceService _dbResilience;
    private readonly Mock<ICacheService> _cacheMock;

    public InsertTests()
    {
        _dbResilience = CreateDbResilience();
        _cacheMock = new Mock<ICacheService>();
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

    [Fact]
    public async Task Create_ReturnsCreatedAtActionResult()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Juan",
            strAPaterno = "Pérez",
            strCURP = "CURP1234567890"
        };

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Create_ReturnsCreatedWithCorrectRouteName()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Juan",
            strAPaterno = "Pérez"
        };

        var result = await controller.Create(dto);

        var createdResult = result.Result as CreatedAtActionResult;
        createdResult!.ActionName.Should().Be(nameof(EmpleadoController.Get));
    }

    [Fact]
    public async Task Create_ReturnsCreatedWithCorrectRouteValues()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Juan",
            strAPaterno = "Pérez"
        };

        var result = await controller.Create(dto);

        var createdResult = result.Result as CreatedAtActionResult;
        var routeValues = createdResult!.RouteValues;
        routeValues.Should().ContainKey("id");
        var idValue = routeValues["id"] as int?;
        idValue.Should().NotBeNull();
        idValue!.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ReturnsDto_WithIdGreaterThanZero()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Juan",
            strAPaterno = "Pérez"
        };

        var result = await controller.Create(dto);

        var createdResult = result.Result as CreatedAtActionResult;
        var dtoResult = createdResult!.Value as EmpEmpleadoDto;
        dtoResult!.id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_ReturnsDto_WithCorrectNombre()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Juan",
            strAPaterno = "Pérez"
        };

        var result = await controller.Create(dto);

        var createdResult = result.Result as CreatedAtActionResult;
        var dtoResult = createdResult!.Value as EmpEmpleadoDto;
        dtoResult!.strNombre.Should().Be("Juan");
    }

    [Fact]
    public async Task Create_PersistsEmpleadoInDatabase()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Juan",
            strAPaterno = "Pérez",
            strCURP = "CURP1234567890"
        };

        await controller.Create(dto);

        context.EmpEmpleado.Count().Should().Be(1);
        var saved = context.EmpEmpleado.First();
        saved.strNombre.Should().Be("Juan");
        saved.strAPaterno.Should().Be("Pérez");
        saved.strCURP.Should().Be("CURP1234567890");
    }

    [Fact]
    public async Task Create_TrimsNombre()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "  Juan  ",
            strAPaterno = "  Pérez  "
        };

        await controller.Create(dto);

        var saved = context.EmpEmpleado.First();
        saved.strNombre.Should().Be("Juan");
        saved.strAPaterno.Should().Be("Pérez");
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenCURPExists()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado
        {
            strNombre = "Existente",
            strCURP = "CURP1234567890"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Nuevo",
            strCURP = "CURP1234567890"
        };

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_DoesNotPersistDuplicateCURP()
    {
        var context = DbContextMock.GetDbContext();
        context.EmpEmpleado.Add(new EmpEmpleado
        {
            strNombre = "Existente",
            strCURP = "CURP1234567890"
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context);
        var dto = new EmpEmpleadoCreateDto
        {
            strNombre = "Nuevo",
            strCURP = "CURP1234567890"
        };

        await controller.Create(dto);

        context.EmpEmpleado.Count().Should().Be(1);
    }

    [Fact]
    public async Task Create_NullCURP_AllowsMultipleNulls()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var dto1 = new EmpEmpleadoCreateDto { strNombre = "Uno" };
        var dto2 = new EmpEmpleadoCreateDto { strNombre = "Dos" };

        await controller.Create(dto1);
        var result = await controller.Create(dto2);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        context.EmpEmpleado.Count().Should().Be(2);
    }
}
