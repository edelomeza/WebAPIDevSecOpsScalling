using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

/// <summary>
/// Controlador para la gestión de usuarios del sistema.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[EnableRateLimiting("AdminPolicy")]
public class UsuarioController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;

    /// <summary>
    /// Inicializa una nueva instancia del controlador de usuarios.
    /// </summary>
    /// <param name="usuarioService">Servicio de usuarios.</param>
    public UsuarioController(IUsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    /// <summary>
    /// Obtiene todos los usuarios con paginación y filtros opcionales.
    /// </summary>
    /// <param name="queryParams">Parámetros de consulta (paginación, filtros).</param>
    /// <returns>Lista paginada de usuarios.</returns>
    /// <response code="200">Usuarios obtenidos correctamente.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<SegUsuarioDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<SegUsuarioDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
    {
        var usuarios = await _usuarioService.GetAllAsync(queryParams);
        return Ok(usuarios);
    }

    /// <summary>
    /// Busca usuarios por nombre (búsqueda parcial, case-insensitive).
    /// </summary>
    /// <param name="texto">Texto a buscar en el nombre.</param>
    /// <param name="queryParams">Parámetros de paginación.</param>
    /// <returns>Lista paginada de usuarios que coinciden.</returns>
    /// <response code="200">Resultados de búsqueda.</response>
    /// <response code="400">Texto de búsqueda vacío.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    [HttpGet("buscar")]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<SegUsuarioDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<SegUsuarioDto>>> SearchByName([FromQuery][StringLength(50)] string texto, [FromQuery] QueryParams? queryParams = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return BadRequest("El texto de búsqueda es requerido.");

        var usuarios = await _usuarioService.SearchByNameAsync(texto, queryParams);
        return Ok(usuarios);
    }

    /// <summary>
    /// Autocompletado de usuarios por nombre (búsqueda parcial, case-insensitive).
    /// </summary>
    /// <param name="texto">Texto a buscar en el nombre.</param>
    /// <param name="maxResultados">Máximo de resultados (1-50, default 10).</param>
    /// <returns>Lista de usuarios que coinciden para autocompletado.</returns>
    /// <response code="200">Resultados de autocompletado.</response>
    /// <response code="400">Texto de búsqueda vacío.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    [HttpGet("autocomplete")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IEnumerable<SegUsuarioAutocompleteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SegUsuarioAutocompleteDto>>> Autocomplete(
        [FromQuery][StringLength(50)] string texto,
        [FromQuery] int maxResultados = 10)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return BadRequest("El texto de búsqueda es requerido.");

        if (maxResultados < 1 || maxResultados > 50)
            maxResultados = 10;

        var result = await _usuarioService.AutocompleteAsync(texto, maxResultados);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene un usuario por su ID.
    /// </summary>
    /// <param name="id">Identificador único del usuario.</param>
    /// <returns>Usuario solicitado.</returns>
    /// <response code="200">Usuario encontrado.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    /// <response code="404">Usuario no encontrado.</response>
    [HttpGet("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(SegUsuarioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SegUsuarioDto>> Get(int id)
    {
        var segUsuario = await _usuarioService.GetByIdAsync(id);

        if (segUsuario == null)
            return NotFound();

        return Ok(segUsuario);
    }

    /// <summary>
    /// Actualiza un usuario existente.
    /// </summary>
    /// <param name="id">Identificador único del usuario.</param>
    /// <param name="dto">Datos actualizados del usuario.</param>
    /// <response code="204">Usuario actualizado correctamente.</response>
    /// <response code="400">Solicitud inválida.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    /// <response code="404">Usuario no encontrado.</response>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, UsuarioUpdateDto dto)
    {
        try
        {
            await _usuarioService.UpdateAsync(id, dto);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
    }

    /// <summary>
    /// Crea un nuevo usuario.
    /// </summary>
    /// <param name="dto">Datos del usuario a crear.</param>
    /// <returns>Usuario creado.</returns>
    /// <response code="201">Usuario creado correctamente.</response>
    /// <response code="400">Datos inválidos.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(SegUsuarioDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SegUsuarioDto>> Create(UsuarioCreateDto dto)
    {
        try
        {
            var result = await _usuarioService.CreateAsync(dto);
            return CreatedAtAction(nameof(Get), new { id = result.id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Elimina un usuario por su ID.
    /// </summary>
    /// <param name="id">Identificador único del usuario.</param>
    /// <param name="dto">Datos para confirmar la eliminación (RowVersion).</param>
    /// <response code="200">Usuario eliminado correctamente.</response>
    /// <response code="400">El ID de la ruta no coincide con el cuerpo de la solicitud.</response>
    /// <response code="401">No autenticado.</response>
    /// <response code="403">No tiene permisos de administrador.</response>
    /// <response code="404">Usuario no encontrado.</response>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, UsuarioDeleteDto dto)
    {
        try
        {
            if (id != dto.id)
                return BadRequest("El ID de la ruta no coincide con el ID del cuerpo.");

            await _usuarioService.DeleteAsync(id, dto);
            return Ok();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
    }
}
