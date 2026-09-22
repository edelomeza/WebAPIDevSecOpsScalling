using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    /// <summary>
    /// PostDespliegue9 (criterio 14): recuperación de pedidos "Pendiente" cuyo PublishAsync
    /// falló tras el commit del finalize (PUT 1->2). Solo Admin.
    /// </summary>
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Ventas/admin")]
    [ApiController]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting("AdminPolicy")]
    public class VentasAdminController : ControllerBase
    {
        private readonly IVentasPedidoService _ventasPedidoService;

        public VentasAdminController(IVentasPedidoService ventasPedidoService)
        {
            _ventasPedidoService = ventasPedidoService;
        }

        [HttpGet("pedidos/pendientes")]
        [ProducesResponseType(typeof(IReadOnlyList<PedidoResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<PedidoResponseDto>>> GetPendientes([FromQuery] int max = 50)
        {
            var result = await _ventasPedidoService.GetPendientesAsync(max);
            return Ok(result);
        }

        [HttpPost("pedidos/{id:guid}/republish")]
        [ProducesResponseType(typeof(PedidoResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PedidoResponseDto>> Republish(Guid id)
        {
            if (id == Guid.Empty)
                return BadRequest(new { mensaje = "El parámetro id es requerido." });

            try
            {
                var result = await _ventasPedidoService.RepublicarPendienteAsync(id);
                return Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { mensaje = "El pedido no existe." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
        }
    }
}
