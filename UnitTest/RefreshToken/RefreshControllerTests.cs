using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.RefreshToken
{
    public class RefreshControllerTests
    {
        private readonly Mock<IValidator<RefreshRequest>> _validatorMock;

        public RefreshControllerTests()
        {
            _validatorMock = new Mock<IValidator<RefreshRequest>>();
            _validatorMock.Setup(v => v.ValidateAsync(It.IsAny<RefreshRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
        }

        [Fact]
        public async Task Refresh_ValidToken_Returns200WithTokens()
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

            var (originalToken, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    {"Jwt:Key", "01123581321345589144233377610987"},
                    {"Jwt:Issuer", "test"},
                    {"Jwt:Audience", "test"}
                })
                .Build();

            var controller = new RefreshController(service, config, context, _validatorMock.Object);

            var result = await controller.Refresh(new RefreshRequest(originalToken), CancellationToken.None);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<RefreshResponse>().Subject;
            response.Token.Should().NotBeNullOrEmpty();
            response.RefreshToken.Should().NotBeNullOrEmpty();
            response.RefreshToken.Should().NotBe(originalToken);
            response.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }

        [Fact]
        public async Task Refresh_InvalidToken_Returns401()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);
            var config = new ConfigurationBuilder().Build();

            var controller = new RefreshController(service, config, context, _validatorMock.Object);

            var result = await controller.Refresh(new RefreshRequest("invalid-token"), CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Refresh_ExpiredToken_Returns401()
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

            context.SegRefreshToken.Add(new SegRefreshToken
            {
                idSegUsuario = 1,
                strTokenHash = "aa",
                dteExpiresAt = DateTime.UtcNow.AddDays(-1),
                dteCreatedAt = DateTime.UtcNow.AddDays(-8)
            });
            await context.SaveChangesAsync();

            var config = new ConfigurationBuilder().Build();
            var controller = new RefreshController(service, config, context, _validatorMock.Object);

            var result = await controller.Refresh(new RefreshRequest("any-token"), CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Refresh_RevokedToken_Returns401()
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

            var (token, _) = await service.GenerateTokenAsync(1, CancellationToken.None);
            await service.ValidateAndRotateAsync(token, CancellationToken.None);

            var config = new ConfigurationBuilder().Build();
            var controller = new RefreshController(service, config, context, _validatorMock.Object);

            var result = await controller.Refresh(new RefreshRequest(token), CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Refresh_EmptyToken_Returns400()
        {
            var context = DbContextMock.GetDbContext();
            var service = new RefreshTokenService(context);
            var config = new ConfigurationBuilder().Build();

            _validatorMock.Setup(v => v.ValidateAsync(It.IsAny<RefreshRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("RefreshToken", "El refresh token es requerido.") }));

            var controller = new RefreshController(service, config, context, _validatorMock.Object);

            var result = await controller.Refresh(new RefreshRequest(""), CancellationToken.None);

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Refresh_RotatesToken_OldTokenRevoked()
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

            var (originalToken, _) = await service.GenerateTokenAsync(1, CancellationToken.None);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    {"Jwt:Key", "01123581321345589144233377610987"},
                    {"Jwt:Issuer", "test"},
                    {"Jwt:Audience", "test"}
                })
                .Build();

            var controller = new RefreshController(service, config, context, _validatorMock.Object);

            await controller.Refresh(new RefreshRequest(originalToken), CancellationToken.None);

            var tokens = context.SegRefreshToken.ToList();
            tokens.Should().HaveCount(2);
            tokens.Count(t => t.dteRevokedAt != null).Should().Be(1);
            tokens.Count(t => t.dteRevokedAt == null).Should().Be(1);
        }
    }
}
