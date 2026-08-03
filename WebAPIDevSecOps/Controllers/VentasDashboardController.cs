using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Ventas")]
    [ApiController]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting("AdminPolicy")]
    public class VentasDashboardController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public VentasDashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [HttpGet("dashboard")]
        [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<DashboardDto>> GetDashboard()
        {
            var dashboard = await _dashboardService.GetDashboardAsync();
            return Ok(dashboard);
        }

        [HttpGet("saga/{id:guid}/diagrama")]
        [ProducesResponseType(typeof(SagaTimelineDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<SagaTimelineDto>> GetTimeline(Guid id)
        {
            if (id == Guid.Empty)
                return BadRequest(new { mensaje = "El parámetro id es requerido." });

            var timeline = await _dashboardService.GetTimelineAsync(id);
            if (timeline == null)
                return NotFound(new { mensaje = "El pedido no existe." });

            return Ok(timeline);
        }
    }
}
