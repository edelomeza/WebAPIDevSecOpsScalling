using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Controllers
{
    /// <summary>
    /// Controlador para autenticación de usuarios mediante login.
    /// </summary>
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly ILoginService _loginService;
        private readonly IValidator<LoginRequest> _validator;

        /// <summary>
        /// Inicializa una nueva instancia del controlador de login.
        /// </summary>
        /// <param name="loginService">Servicio de autenticación.</param>
        /// <param name="validator">Validador de credenciales.</param>
        public LoginController(ILoginService loginService, IValidator<LoginRequest> validator)
        {
            _loginService = loginService;
            _validator = validator;
        }

        /// <summary>
        /// Autentica un usuario y devuelve un token JWT.
        /// </summary>
        /// <param name="request">Credenciales del usuario.</param>
        /// <param name="ct">Token de cancelación.</param>
        /// <returns>Token JWT si las credenciales son válidas.</returns>
        /// <response code="200">Autenticación exitosa. Devuelve el token JWT.</response>
        /// <response code="400">Solicitud inválida (credenciales vacías o nulas).</response>
        /// <response code="401">Credenciales inválidas.</response>
        [HttpPost("login")]
        [EnableRateLimiting("LoginPolicy")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        {
            var validationResult = await _validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors.Select(e => e.ErrorMessage));
            }

            try
            {
                var result = await _loginService.LoginAsync(request, ct);
                return Ok(new
                {
                    token = result.Token,
                    refreshToken = result.RefreshToken,
                    expiresAt = result.ExpiresAt
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        }
    }
}
