using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace UnitTest.Login2fa
{
    public class Login2faControllerTests
    {
        [Fact]
        public async Task Login2fa_No2FA_ReturnsToken()
        {
            var service = new Mock<ILogin2faService>();
            service.Setup(s => s.Login2faAsync(It.IsAny<Login2faRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Login2faResponse
                {
                    Token = "jwt-token",
                    Requires2fa = false
                });

            var controller = new Login2faController(service.Object);
            var result = await controller.Login2fa(new Login2faRequest("user", "password"), CancellationToken.None);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task Login2fa_With2FA_ReturnsTempToken()
        {
            var service = new Mock<ILogin2faService>();
            service.Setup(s => s.Login2faAsync(It.IsAny<Login2faRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Login2faResponse
                {
                    Requires2fa = true,
                    TempToken = "temp-token-2fa"
                });

            var controller = new Login2faController(service.Object);
            var result = await controller.Login2fa(new Login2faRequest("user", "password"), CancellationToken.None);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task Login2fa_InvalidCredentials_Returns401()
        {
            var service = new Mock<ILogin2faService>();
            service.Setup(s => s.Login2faAsync(It.IsAny<Login2faRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException("Credenciales inválidas."));

            var controller = new Login2faController(service.Object);
            var result = await controller.Login2fa(new Login2faRequest("user", "wrong"), CancellationToken.None);

            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task Login2fa_EmptyUser_Returns400()
        {
            var service = new Mock<ILogin2faService>();
            var controller = new Login2faController(service.Object);
            var result = await controller.Login2fa(new Login2faRequest("", "password"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Login2fa_EmptyPassword_Returns400()
        {
            var service = new Mock<ILogin2faService>();
            var controller = new Login2faController(service.Object);
            var result = await controller.Login2fa(new Login2faRequest("user", ""), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Login2fa_NullRequest_Returns400()
        {
            var service = new Mock<ILogin2faService>();
            var controller = new Login2faController(service.Object);
            var result = await controller.Login2fa(null!, CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify2fa_ValidCode_ReturnsToken()
        {
            var service = new Mock<ILogin2faService>();
            service.Setup(s => s.Verify2faAsync(It.IsAny<Login2faVerifyRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Login2faVerifyResponse
                {
                    Token = "jwt-token"
                });

            var controller = new Login2faController(service.Object);
            var result = await controller.Verify2fa(new Login2faVerifyRequest("temp-token", "123456"), CancellationToken.None);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task Verify2fa_InvalidCode_Returns401()
        {
            var service = new Mock<ILogin2faService>();
            service.Setup(s => s.Verify2faAsync(It.IsAny<Login2faVerifyRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException("Código TOTP inválido."));

            var controller = new Login2faController(service.Object);
            var result = await controller.Verify2fa(new Login2faVerifyRequest("temp-token", "000000"), CancellationToken.None);

            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().NotBeNull();
        }

        [Fact]
        public async Task Verify2fa_EmptyTempToken_Returns400()
        {
            var service = new Mock<ILogin2faService>();
            var controller = new Login2faController(service.Object);
            var result = await controller.Verify2fa(new Login2faVerifyRequest("", "123456"), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify2fa_EmptyCode_Returns400()
        {
            var service = new Mock<ILogin2faService>();
            var controller = new Login2faController(service.Object);
            var result = await controller.Verify2fa(new Login2faVerifyRequest("temp-token", ""), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify2fa_NullRequest_Returns400()
        {
            var service = new Mock<ILogin2faService>();
            var controller = new Login2faController(service.Object);
            var result = await controller.Verify2fa(null!, CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Verify2fa_InvalidTempToken_Returns401()
        {
            var service = new Mock<ILogin2faService>();
            service.Setup(s => s.Verify2faAsync(It.IsAny<Login2faVerifyRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException("Token temporal inválido o expirado."));

            var controller = new Login2faController(service.Object);
            var result = await controller.Verify2fa(new Login2faVerifyRequest("invalid-token", "123456"), CancellationToken.None);

            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().NotBeNull();
        }
    }
}
