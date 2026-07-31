using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    [EnableRateLimiting("AdminPolicy")]
    public class EmpleadoController : ControllerBase
    {
        private readonly IEmpleadoService _empleadoService;

        public EmpleadoController(IEmpleadoService empleadoService)
        {
            _empleadoService = empleadoService;
        }

        [HttpGet]
        [Authorize(Policy = "AdminOnly")]
        [ResponseCache(NoStore = true)]
        [ProducesResponseType(typeof(PagedResult<EmpEmpleadoDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PagedResult<EmpEmpleadoDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
        {
            var result = await _empleadoService.GetAllAsync(queryParams);
            return Ok(result);
        }

        [HttpGet("{id}")]
        [Authorize(Policy = "AdminOnly")]
        [ProducesResponseType(typeof(EmpEmpleadoDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<EmpEmpleadoDto>> Get(int id)
        {
            var result = await _empleadoService.GetByIdAsync(id);
            if (result == null)
                return NotFound();
            return Ok(result);
        }

        [HttpGet("buscar")]
        [Authorize(Policy = "AdminOnly")]
        [ResponseCache(NoStore = true)]
        [ProducesResponseType(typeof(PagedResult<EmpEmpleadoDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PagedResult<EmpEmpleadoDto>>> Search(
            [FromQuery] string? texto,
            [FromQuery] int? idTipoEmpleado,
            [FromQuery] QueryParams? queryParams = null)
        {
            var result = await _empleadoService.SearchAsync(texto, idTipoEmpleado, queryParams);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        [ProducesResponseType(typeof(EmpEmpleadoDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<EmpEmpleadoDto>> Create(EmpEmpleadoCreateDto dto)
        {
            try
            {
                var result = await _empleadoService.CreateAsync(dto);
                return CreatedAtAction(nameof(Get), new { id = result.id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (DbUpdateException)
            {
                return Conflict(new { mensaje = "Conflicto de duplicidad al crear el empleado." });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "AdminOnly")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(int id, EmpEmpleadoUpdateDto dto)
        {
            try
            {
                await _empleadoService.UpdateAsync(id, dto);
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
                return Conflict(new { mensaje = "El registro fue modificado por otro usuario. Intente nuevamente." });
            }
            catch (DbUpdateException)
            {
                return Conflict(new { mensaje = "Conflicto de duplicidad al actualizar el empleado." });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminOnly")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id, EmpEmpleadoDeleteDto dto)
        {
            try
            {
                if (id != dto.id)
                    return BadRequest(new { mensaje = "El ID de la ruta no coincide con el ID del cuerpo." });

                await _empleadoService.DeleteAsync(id, dto);
                return Ok(new { mensaje = "Empleado eliminado correctamente." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { mensaje = "El registro fue modificado por otro usuario. Intente nuevamente." });
            }
        }
    }
}
