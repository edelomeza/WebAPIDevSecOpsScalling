using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Ventas/pedido")]
    [ApiController]
    [EnableRateLimiting("ConcurrentWritesPolicy")]
    public class VentasPedidoController : ControllerBase
    {
        private readonly IVentasPedidoService _ventasPedidoService;

        public VentasPedidoController(IVentasPedidoService ventasPedidoService)
        {
            _ventasPedidoService = ventasPedidoService;
        }

        [HttpPost]
        [Authorize]
        [ProducesResponseType(typeof(PedidoResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<PedidoResponseDto>> Create(PedidoCreateDto dto)
        {
            try
            {
                var result = await _ventasPedidoService.CrearPedidoAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = result.id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { mensaje = ex.Message });
            }
        }

        [HttpGet("{id}")]
        [Authorize]
        [ProducesResponseType(typeof(PedidoResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PedidoResponseDto>> GetById(Guid id)
        {
            var result = await _ventasPedidoService.GetByIdAsync(id);
            if (result == null)
                return NotFound();
            return Ok(result);
        }

        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(PagedResult<PedidoResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<PagedResult<PedidoResponseDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
        {
            var result = await _ventasPedidoService.GetAllAsync(queryParams);
            return Ok(result);
        }
    }
}
