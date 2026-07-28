using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class LogoutController : ControllerBase
    {
        private readonly ITokenBlacklistService _blacklistService;

        public LogoutController(ITokenBlacklistService blacklistService)
        {
            _blacklistService = blacklistService;
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
                catch
                {
                }

                return Ok(new { mensaje = "Sesión cerrada correctamente." });
            }

            return Unauthorized(new { mensaje = "No se proporcionó un token." });
        }
    }
}
