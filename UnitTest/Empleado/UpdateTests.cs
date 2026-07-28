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

public class UpdateTests
{
    private readonly DbResilienceService _dbResilience;
    private readonly Mock<ICacheService> _cacheMock;

    public UpdateTests()
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

    private async Task<EmpEmpleado> SeedEmpleadoAsync(AppDbContext context, string nombre = "Original", string? curp = null)
    {
        var emp = new EmpEmpleado
        {
            strNombre = nombre,
            strAPaterno = "Apellido",
            strCURP = curp
        };
        context.EmpEmpleado.Add(emp);
        await context.SaveChangesAsync();
        return emp;
    }

    [Fact]
    public async Task Update_ValidUpdate_ReturnsNoContent()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = emp.id,
            strNombre = "Modificado",
            RowVersion = emp.RowVersion
        };

        var result = await controller.Update(emp.id, dto);

        result.Should().BeOfType<NoContentResult>();
        _cacheMock.Verify(x => x.RemoveAsync($"cache:empleado:{emp.id}"), Times.Once);
    }

    [Fact]
    public async Task Update_ChangesNombreInDatabase()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = emp.id,
            strNombre = "Modificado",
            RowVersion = emp.RowVersion
        };

        await controller.Update(emp.id, dto);

        var updated = context.EmpEmpleado.Find(emp.id);
        updated!.strNombre.Should().Be("Modificado");
    }

    [Fact]
    public async Task Update_TrimsNombre()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = emp.id,
            strNombre = "  Modificado  ",
            RowVersion = emp.RowVersion
        };

        await controller.Update(emp.id, dto);

        var updated = context.EmpEmpleado.Find(emp.id);
        updated!.strNombre.Should().Be("Modificado");
    }

    [Fact]
    public async Task Update_RouteIdMismatch_ReturnsBadRequest()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = 999,
            strNombre = "Test",
            RowVersion = emp.RowVersion
        };

        var result = await controller.Update(emp.id, dto);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_DuplicateCURP_ReturnsBadRequest()
    {
        var context = DbContextMock.GetDbContext();
        var emp1 = await SeedEmpleadoAsync(context, "Uno", "CURP123");
        var emp2 = await SeedEmpleadoAsync(context, "Dos", "CURP456");
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = emp2.id,
            strNombre = "Dos",
            strCURP = "CURP123",
            RowVersion = emp2.RowVersion
        };

        var result = await controller.Update(emp2.id, dto);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_NonExistentId_ReturnsNotFound()
    {
        var context = DbContextMock.GetDbContext();
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = 999,
            strNombre = "Test",
            RowVersion = new byte[] { 1, 0, 0, 0 }
        };

        var result = await controller.Update(999, dto);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Update_ThrowsDbUpdateConcurrencyException_WhenRowVersionMismatch()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context);
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = emp.id,
            strNombre = "Test",
            RowVersion = new byte[] { 2, 0, 0, 0 }
        };

        var result = await controller.Update(emp.id, dto);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Update_AllowsSelfRename()
    {
        var context = DbContextMock.GetDbContext();
        var emp = await SeedEmpleadoAsync(context, "Original");
        var controller = CreateController(context);

        var dto = new EmpEmpleadoUpdateDto
        {
            id = emp.id,
            strNombre = "Original",
            RowVersion = emp.RowVersion
        };

        var result = await controller.Update(emp.id, dto);

        result.Should().BeOfType<NoContentResult>();
    }
}
