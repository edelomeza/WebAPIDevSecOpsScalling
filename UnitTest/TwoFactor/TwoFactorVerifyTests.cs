using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OtpNet;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.TwoFactor
{
    public class TwoFactorVerifyTests
    {
        [Fact]
        public async Task Verify_ValidCode_Returns200AndEnables2FA()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            var totp = new Totp(Base32Encoding.ToBytes(secret), step: 30, totpSize: 6);
            var validCode = totp.ComputeTotp();

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                str2FASecreto = secret,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "testuser")
                    }, "test"))
                }
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest(validCode), CancellationToken.None);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<TwoFactorVerifyResponse>().Subject;
            response.Mensaje.Should().Be("2FA habilitado correctamente.");

            var usuario = context.SegUsuario.First();
            usuario.bln2FAHabilitado.Should().BeTrue();
        }

        [Fact]
        public async Task Verify_InvalidCode_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                str2FASecreto = secret,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "testuser")
                    }, "test"))
                }
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest("000000"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify_WithoutAuth_Returns401()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest("123456"), CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Verify_WithoutSetup_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "testuser")
                    }, "test"))
                }
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest("123456"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify_AlreadyEnabled_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                str2FASecreto = secret,
                bln2FAHabilitado = true,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "testuser")
                    }, "test"))
                }
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest("123456"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify_EmptyCode_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "testuser")
                    }, "test"))
                }
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest(""), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify_CodeWithLetters_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "testuser")
                    }, "test"))
                }
            };

            var result = await controller.Verify(new TwoFactorVerifyRequest("12345a"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
