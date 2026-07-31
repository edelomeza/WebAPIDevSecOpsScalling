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
[EnableRateLimiting("ConcurrentWritesPolicy")]
public class VentaController : ControllerBase
{
    private readonly IVentaService _ventaService;

    public VentaController(IVentaService ventaService)
    {
        _ventaService = ventaService;
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<VenVentaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<VenVentaDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
    {
        var result = await _ventaService.GetAllAsync(queryParams);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(VenVentaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VenVentaDto>> Get(int id)
    {
        var result = await _ventaService.GetByIdAsync(id);
        if (result == null)
            return NotFound();
        return Ok(result);
    }

    [HttpGet("buscar")]
    [Authorize(Policy = "AdminOnly")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<VenVentaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<VenVentaDto>>> Search(
        [FromQuery][StringLength(10)] string? strClaveVenta = null,
        [FromQuery][StringLength(100)] string? strNombreCliente = null,
        [FromQuery] DateTime? dteFechaInicio = null,
        [FromQuery] DateTime? dteFechaFin = null,
        [FromQuery] QueryParams? queryParams = null)
    {
        var result = await _ventaService.SearchAsync(strClaveVenta, strNombreCliente, dteFechaInicio, dteFechaFin, queryParams);
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, VenVentaUpdateDto dto)
    {
        try
        {
            await _ventaService.UpdateAsync(id, dto);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, VenVentaDeleteDto dto)
    {
        try
        {
            if (id != dto.id)
                return BadRequest(new { mensaje = "El ID de la ruta no coincide con el ID del cuerpo." });

            await _ventaService.DeleteAsync(id, dto);
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

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(VenVentaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VenVentaDto>> Create(VenVentaCreateDto dto)
    {
        try
        {
            var result = await _ventaService.CreateAsync(dto);
            return CreatedAtAction(nameof(Get), new { id = result.id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
