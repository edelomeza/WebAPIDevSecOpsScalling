using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[EnableRateLimiting("AdminPolicy")]
public class ProductoController : ControllerBase
{
    private readonly IProductoService _productoService;

    public ProductoController(IProductoService productoService)
    {
        _productoService = productoService;
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<ProProductoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ProProductoDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
    {
        var productos = await _productoService.GetAllAsync(queryParams);
        return Ok(productos);
    }

    [HttpGet("buscar")]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<ProProductoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ProProductoDto>>> SearchByName([FromQuery][StringLength(50)] string texto, [FromQuery] QueryParams? queryParams = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return BadRequest("El texto de búsqueda es requerido.");

        var productos = await _productoService.SearchByNameAsync(texto, queryParams);
        return Ok(productos);
    }

    [HttpGet("{id}")]
    [Authorize]
    [ProducesResponseType(typeof(ProProductoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProProductoDto>> Get(int id)
    {
        var producto = await _productoService.GetByIdAsync(id);

        if (producto == null)
            return NotFound();

        return Ok(producto);
    }

    [HttpPut("{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, ProductoUpdateDto dto)
    {
        try
        {
            await _productoService.UpdateAsync(id, dto);
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

    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ProProductoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProProductoDto>> Create(ProductoCreateDto dto)
    {
        try
        {
            var result = await _productoService.CreateAsync(dto);
            return CreatedAtAction(nameof(Get), new { id = result.id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, ProductoDeleteDto dto)
    {
        try
        {
            if (id != dto.id)
                return BadRequest("El ID de la ruta no coincide con el ID del cuerpo.");

            await _productoService.DeleteAsync(id, dto);
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
