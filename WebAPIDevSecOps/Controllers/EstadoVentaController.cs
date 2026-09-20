using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
public class EstadoVentaController : ControllerBase
{
    private readonly IVenCatEstadoService _venCatEstadoService;

    public EstadoVentaController(IVenCatEstadoService venCatEstadoService)
    {
        _venCatEstadoService = venCatEstadoService;
    }

    [HttpGet]
    [Authorize]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(PagedResult<VenCatEstadoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<VenCatEstadoDto>>> GetAll([FromQuery] QueryParams? queryParams = null)
    {
        var result = await _venCatEstadoService.GetAllAsync(queryParams);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(typeof(VenCatEstadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VenCatEstadoDto>> Get(int id)
    {
        var result = await _venCatEstadoService.GetByIdAsync(id);

        if (result == null)
            return NotFound();

        return Ok(result);
    }
}
