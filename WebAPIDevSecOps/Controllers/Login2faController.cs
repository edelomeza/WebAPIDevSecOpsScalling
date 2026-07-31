using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    public class Login2faController : ControllerBase
    {
        private readonly ILogin2faService _login2faService;

        public Login2faController(ILogin2faService login2faService)
        {
            _login2faService = login2faService;
        }

        [HttpPost("login")]
        [EnableRateLimiting("LoginPolicy")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Login2fa([FromBody] Login2faRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.User) || string.IsNullOrWhiteSpace(request?.Password))
            {
                return BadRequest(new { mensaje = "Usuario y contraseña son requeridos." });
            }

            try
            {
                var result = await _login2faService.Login2faAsync(request, ct);

                if (result.Requires2fa)
                {
                    return Ok(new
                    {
                        requires_2fa = true,
                        tempToken = result.TempToken
                    });
                }

                return Ok(new
                {
                    token = result.Token,
                    refreshToken = result.RefreshToken,
                    expiresAt = result.ExpiresAt
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { mensaje = ex.Message });
            }
        }

        [HttpPost("verify")]
        [EnableRateLimiting("Login2faVerifyPolicy")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Verify2fa([FromBody] Login2faVerifyRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.TempToken) || string.IsNullOrWhiteSpace(request?.Code))
            {
                return BadRequest(new { mensaje = "Token temporal y código son requeridos." });
            }

            try
            {
                var result = await _login2faService.Verify2faAsync(request, ct);
                return Ok(new
                {
                    token = result.Token,
                    refreshToken = result.RefreshToken,
                    expiresAt = result.ExpiresAt
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { mensaje = ex.Message });
            }
        }
    }
}
