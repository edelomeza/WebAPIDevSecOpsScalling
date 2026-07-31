using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiController]
    public class RefreshController : ControllerBase
    {
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly IValidator<RefreshRequest> _validator;

        public RefreshController(
            IRefreshTokenService refreshTokenService,
            IConfiguration configuration,
            AppDbContext context,
            IValidator<RefreshRequest> validator)
        {
            _refreshTokenService = refreshTokenService;
            _configuration = configuration;
            _context = context;
            _validator = validator;
        }

        [HttpPost("refresh")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
        {
            var validationResult = await _validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors.Select(e => e.ErrorMessage));
            }

            var rotationResult = await _refreshTokenService.ValidateAndRotateAsync(request.RefreshToken, ct);

            if (rotationResult is null)
            {
                return Unauthorized(new { mensaje = "Refresh token inválido, expirado o revocado." });
            }

            var usuario = await _context.SegUsuario
                .Where(u => u.id == rotationResult.UsuarioId)
                .FirstOrDefaultAsync(ct);

            if (usuario is null)
            {
                return Unauthorized(new { mensaje = "Usuario no encontrado." });
            }

            var jwt = GenerateJwtToken(usuario.strNombre);

            return Ok(new RefreshResponse
            {
                Token = jwt,
                RefreshToken = rotationResult.NewRefreshToken,
                ExpiresAt = rotationResult.ExpiresAt
            });
        }

        private string GenerateJwtToken(string username)
        {
            var secretKey = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT Key no configurada");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(ClaimTypes.NameIdentifier, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
