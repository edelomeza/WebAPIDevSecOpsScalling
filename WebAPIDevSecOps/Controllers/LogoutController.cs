using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class LogoutController : ControllerBase
    {
        private readonly ITokenBlacklistService _blacklistService;
        private readonly ILogger<LogoutController> _logger;

        public LogoutController(ITokenBlacklistService blacklistService, ILogger<LogoutController> logger)
        {
            _blacklistService = blacklistService;
            _logger = logger;
        }

        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Logout()
        {
            var token = Request.Headers["Authorization"]
                .ToString()
                .Replace("Bearer ", "");

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    var jti = jwt?.Id;
                    if (!string.IsNullOrEmpty(jti))
                    {
                        await _blacklistService.AddAsync(jti, TimeSpan.FromMinutes(60));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al blacklistear token");
                }

                return Ok(new { mensaje = "Sesión cerrada correctamente." });
            }

            return Unauthorized(new { mensaje = "No se proporcionó un token." });
        }
    }
}
