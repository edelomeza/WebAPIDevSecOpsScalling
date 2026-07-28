using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

public class DeleteTests
{
    private readonly DbResilienceService _dbResilience;
    private readonly Mock<ICacheService> _cacheMock;

    public DeleteTests()
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

    private async Task<EmpEmpleado> SeedEmpleadoAsync(AppDbContext context, string nombre = "Juan")
    {
        var emp = new EmpEmpleado { strNombre = nombre };
        context.EmpEmpleado.Add(emp);
        await context.SaveChangesAsync();
        return emp;
    }

    [Fact]
    public async Task Delete_ValidDelete_ReturnsOk()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = emp.id, RowVersion = emp.RowVersion };
        var result = await controller.Delete(emp.id, dto);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Delete_RemovesEmpleadoFromDatabase()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = emp.id, RowVersion = emp.RowVersion };
        await controller.Delete(emp.id, dto);

        context.EmpEmpleado.Count().Should().Be(0);
        _cacheMock.Verify(x => x.RemoveAsync($"cache:empleado:{emp.id}"), Times.Once);
    }

    [Fact]
    public async Task Delete_OnlyRemovesTargetEmpleado()
    {
        var context = DbContextMock.GetDbContext();
        var emp1 = await SeedEmpleadoAsync(context, "Uno");
        var emp2 = await SeedEmpleadoAsync(context, "Dos");
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = emp1.id, RowVersion = emp1.RowVersion };
        await controller.Delete(emp1.id, dto);

        context.EmpEmpleado.Count().Should().Be(1);
        context.EmpEmpleado.First().strNombre.Should().Be("Dos");
    }

    [Fact]
    public async Task Delete_RouteIdMismatch_ReturnsBadRequest()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = 999, RowVersion = emp.RowVersion };
        var result = await controller.Delete(emp.id, dto);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_RouteIdMismatch_CheckedBeforeExistence()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = 999, RowVersion = new byte[] { 1, 0, 0, 0 } };
        var result = await controller.Delete(1, dto);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_NonExistentId_ReturnsNotFound()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = 999, RowVersion = new byte[] { 1, 0, 0, 0 } };
        var result = await controller.Delete(999, dto);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Delete_ThrowsDbUpdateConcurrencyException_WhenRowVersionMismatch()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoDeleteDto { id = emp.id, RowVersion = new byte[] { 2, 0, 0, 0 } };
        var result = await controller.Delete(emp.id, dto);

        result.Should().BeOfType<ConflictObjectResult>();
    }
}
