using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Ventas/factura")]
    [ApiController]
    public class VentasFacturaController : ControllerBase
    {
        private readonly IFacturaService _facturaService;

        public VentasFacturaController(IFacturaService facturaService)
        {
            _facturaService = facturaService;
        }

        [HttpGet("{id}")]
        [Authorize]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> GetById(int id)
        {
            var result = await _facturaService.GetByIdAsync(id);
            if (result == null)
                return NotFound();
            return Ok(result);
        }

        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(List<FacturaResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<List<FacturaResponseDto>>> GetByPedidoId([FromQuery] Guid pedidoId)
        {
            if (pedidoId == Guid.Empty)
                return BadRequest(new { mensaje = "El parámetro pedidoId es requerido." });

            var result = await _facturaService.GetByPedidoIdAsync(pedidoId);
            return Ok(result);
        }
    }
}
