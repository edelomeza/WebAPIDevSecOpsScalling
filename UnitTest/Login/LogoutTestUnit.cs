using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Services;

namespace UnitTest.Login
{
    public class LogoutTestUnit
    {
        private static string GenerateTestToken()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("z9WkJ4l2m9VQX1x8bYl+q3hR0Fz9uT7e5K2pL8sD4fA="));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                claims: [new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: creds
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static LogoutController CreateController(string? token, Mock<IDistributedCache>? cacheMock = null)
        {
            cacheMock ??= new Mock<IDistributedCache>();
            var service = new TokenBlacklistService(cacheMock.Object);
            var controller = new LogoutController(service, NullLogger<LogoutController>.Instance);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            if (!string.IsNullOrEmpty(token))
            {
                controller.Request.Headers["Authorization"] = $"Bearer {token}";
            }

            return controller;
        }

        [Fact]
        public async Task Logout_ReturnsOk_WhenTokenProvided()
        {
            var token = GenerateTestToken();
            var controller = CreateController(token);

            var result = await controller.Logout();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Logout_ReturnsUnauthorized_WhenNoToken()
        {
            var controller = CreateController(null);

            var result = await controller.Logout();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Logout_AddsTokenToBlacklist()
        {
            var token = GenerateTestToken();
            var cacheMock = new Mock<IDistributedCache>();
            var controller = CreateController(token, cacheMock);

            await controller.Logout();

            cacheMock.Verify(c => c.SetAsync(
                It.Is<string>(k => k.StartsWith("blacklist:")),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
