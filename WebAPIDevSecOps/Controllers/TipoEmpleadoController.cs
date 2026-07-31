using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Interfaces;

/// <summary>
/// Controlador para consultar tipos de empleado del catálogo.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[EnableRateLimiting("AdminPolicy")]
public class TipoEmpleadoController : ControllerBase
{
    private readonly ITipoEmpleadoService _tipoEmpleadoService;

    /// <summary>
    /// Inicializa una nueva instancia del controlador de tipos de empleado.
    /// </summary>
    /// <param name="tipoEmpleadoService">Servicio de tipos de empleado.</param>
    public TipoEmpleadoController(ITipoEmpleadoService tipoEmpleadoService)
    {
        _tipoEmpleadoService = tipoEmpleadoService;
    }

    /// <summary>
    /// Obtiene todos los tipos de empleado con paginación.
    /// </summary>
    /// <param name="queryParams">Parámetros de consulta (paginación, filtros).</param>
    /// <returns>Lista paginada de tipos de empleado.</returns>
    /// <response code="200">Tipos de empleado obtenidos correctamente.</response>
    /// <response code="401">No autenticado.</response>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<EmpCatTipoEmpleadoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<EmpCatTipoEmpleadoDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
    {
        var result = await _tipoEmpleadoService.GetAllAsync(queryParams);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene un tipo de empleado por su ID.
    /// </summary>
    /// <param name="id">Identificador único del tipo de empleado.</param>
    /// <returns>Tipo de empleado solicitado.</returns>
    /// <response code="200">Tipo de empleado encontrado.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="404">Tipo de empleado no encontrado.</response>
    [HttpGet("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(EmpCatTipoEmpleadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmpCatTipoEmpleadoDto>> Get(int id)
    {
        var result = await _tipoEmpleadoService.GetByIdAsync(id);

        if (result == null)
            return NotFound();

        return Ok(result);
    }
}
