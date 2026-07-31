using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using System.Security.Claims;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/auth")]
    [ApiController]
    public class TwoFactorController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IRefreshTokenService _refreshTokenService;

        public TwoFactorController(AppDbContext context, IRefreshTokenService refreshTokenService)
        {
            _context = context;
            _refreshTokenService = refreshTokenService;
        }

        [HttpPost("2fa/setup")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Setup(CancellationToken ct)
        {
            var username = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { mensaje = "Usuario no identificado." });
            }

            var usuario = await _context.SegUsuario
                .Where(u => u.strNombre == username)
                .FirstOrDefaultAsync(ct);

            if (usuario is null)
            {
                return Unauthorized(new { mensaje = "Usuario no encontrado." });
            }

            if (usuario.bln2FAHabilitado)
            {
                return BadRequest(new { mensaje = "El 2FA ya está habilitado para este usuario." });
            }

            var secretKey = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(secretKey);

            var issuer = "WebAPIDevSecOps";
            var qrCodeUri = $"otpauth://totp/{issuer}:{username}?secret={base32Secret}&issuer={issuer}";

            usuario.str2FASecreto = base32Secret;
            await _context.SaveChangesAsync(ct);

            return Ok(new TwoFactorSetupResponse
            {
                SharedKey = base32Secret,
                QrCodeUri = qrCodeUri
            });
        }

        [HttpPost("2fa/verify")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> Verify([FromBody] TwoFactorVerifyRequest request, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(request?.Code) || request.Code.Length != 6 || !request.Code.All(char.IsDigit))
            {
                return BadRequest(new { mensaje = "El código TOTP debe tener exactamente 6 dígitos." });
            }

            var username = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { mensaje = "Usuario no identificado." });
            }

            var usuario = await _context.SegUsuario
                .Where(u => u.strNombre == username)
                .FirstOrDefaultAsync(ct);

            if (usuario is null)
            {
                return Unauthorized(new { mensaje = "Usuario no encontrado." });
            }

            if (string.IsNullOrEmpty(usuario.str2FASecreto))
            {
                return BadRequest(new { mensaje = "Debe configurar el 2FA primero usando /auth/2fa/setup." });
            }

            if (usuario.bln2FAHabilitado)
            {
                return BadRequest(new { mensaje = "El 2FA ya está habilitado para este usuario." });
            }

            var secretBytes = Base32Encoding.ToBytes(usuario.str2FASecreto);
            var totp = new Totp(secretBytes, step: 30, totpSize: 6);

            var isValid = totp.VerifyTotp(request.Code, out _, new VerificationWindow(previous: 1, future: 1));

            if (!isValid)
            {
                return BadRequest(new { mensaje = "Código TOTP inválido. Verifique el código e intente de nuevo." });
            }

            usuario.bln2FAHabilitado = true;
            await _context.SaveChangesAsync(ct);

            return Ok(new TwoFactorVerifyResponse
            {
                Mensaje = "2FA habilitado correctamente."
            });
        }
    }
}
