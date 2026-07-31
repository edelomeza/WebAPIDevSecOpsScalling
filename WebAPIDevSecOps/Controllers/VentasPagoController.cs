using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Ventas/pago")]
    [ApiController]
    public class VentasPagoController : ControllerBase
    {
        private readonly IPagoService _pagoService;

        public VentasPagoController(IPagoService pagoService)
        {
            _pagoService = pagoService;
        }

        [HttpGet("{id}")]
        [Authorize]
        [ProducesResponseType(typeof(PagoResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PagoResponseDto>> GetById(int id)
        {
            var result = await _pagoService.GetByIdAsync(id);
            if (result == null)
                return NotFound();
            return Ok(result);
        }

        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(List<PagoResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<List<PagoResponseDto>>> GetByPedidoId([FromQuery] Guid pedidoId)
        {
            if (pedidoId == Guid.Empty)
                return BadRequest(new { mensaje = "El parámetro pedidoId es requerido." });

            var result = await _pagoService.GetByPedidoIdAsync(pedidoId);
            return Ok(result);
        }
    }
}
