using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.TwoFactor
{
    public class TwoFactorSetupTests
    {
        [Fact]
        public async Task Setup_AuthenticatedUser_Returns200WithQrUri()
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

            var result = await controller.Setup(CancellationToken.None);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<TwoFactorSetupResponse>().Subject;
            response.SharedKey.Should().NotBeNullOrEmpty();
            response.QrCodeUri.Should().StartWith("otpauth://totp/");
            response.QrCodeUri.Should().Contain("testuser");
        }

        [Fact]
        public async Task Setup_AlreadyEnabled_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "testuser",
                strCorreoElectronico = "test@test.com",
                strPWD = "hash",
                str2FASecreto = "SECRET",
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

            var result = await controller.Setup(CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Setup_WithoutAuth_Returns401()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);

            var controller = new TwoFactorController(context, service);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            var result = await controller.Setup(CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Setup_NonExistentUser_Returns401()
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
                        new Claim(ClaimTypes.NameIdentifier, "nonexistent")
                    }, "test"))
                }
            };

            var result = await controller.Setup(CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Setup_SavesSecretInDatabase()
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

            await controller.Setup(CancellationToken.None);

            var usuario = context.SegUsuario.First();
            usuario.str2FASecreto.Should().NotBeNullOrEmpty();
            usuario.bln2FAHabilitado.Should().BeFalse();
        }
    }
}
