using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Controllers;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;


namespace UnitTest.Login
{
    public class LoginTestUnit
    {
        private readonly Mock<AppDbContext> _contextMock;
        private readonly Mock<IConfiguration> _configMock;
        private readonly Mock<WebAPIDevSecOps.Interfaces.IPasswordHasherService> _hasherMock;
        private readonly Mock<IValidator<LoginRequest>> _validatorMock;
        private readonly DbResilienceService _dbResilience;
        private readonly Mock<IDistributedCache> _cacheMock;
        private readonly Mock<IRefreshTokenService> _refreshTokenMock;

        private const string FakeArgon2Hash = "$argon2id$v=19$m=16384,t=2,p=1$KxY6z3Y9eG7EqJtq98hPqEX7nZaFWoOhiu7z8K7Z4Vwaki3P6KyHRxY6z3Y9eG";

        public LoginTestUnit()
        {
            _contextMock = new Mock<AppDbContext>();
            _configMock = new Mock<IConfiguration>();
            _hasherMock = new Mock<IPasswordHasherService>();
            _validatorMock = new Mock<IValidator<LoginRequest>>();
            _validatorMock.Setup(v => v.ValidateAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _dbResilience = CreateDbResilience();
            _cacheMock = new Mock<IDistributedCache>();
            _refreshTokenMock = new Mock<IRefreshTokenService>();
            _refreshTokenMock.Setup(s => s.GenerateTokenAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(("refresh-token-test", DateTime.UtcNow.AddDays(7)));
        }

        private static DbResilienceService CreateDbResilience()
        {
            var options = Options.Create(new ResilienceOptions());
            var logger = new Mock<ILogger<DbResilienceService>>();
            return new DbResilienceService(options, logger.Object);
        }


        //Caso: Login exitoso
        [Fact]
        public async Task Login_ReturnsToken_WhenCredentialsAreValid()
        {
            // Arrange
            var context = DbContextMock.GetDbContext();

            var password = "12345678";
            var hash = BCrypt.Net.BCrypt.HashPassword(password);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "admin",
                strCorreoElectronico = "admin@test.com",
                strPWD = hash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });

            await context.SaveChangesAsync();

            var inMemorySettings = new Dictionary<string, string> {
                {"Jwt:Key", "z9WkJ4l2m9VQX1x8bYl+q3hR0Fz9uT7e5K2pL8sD4fA="},
                {"Jwt:Issuer", "test"},
                {"Jwt:Audience", "test"}
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _hasherMock.Setup(h => h.VerifyPassword(password, It.IsAny<string>())).Returns(true);
            _hasherMock.Setup(h => h.NeedsRehash(It.IsAny<string>())).Returns(false);

            _cacheMock.Setup(c => c.GetAsync("lockout:admin", It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);

            var controller = new LoginController(new LoginService(context, configuration, _hasherMock.Object, _dbResilience, Mock.Of<ILogger<LoginService>>(), _cacheMock.Object, Mock.Of<IMemoryCache>(), _refreshTokenMock.Object), _validatorMock.Object);

            var request = new LoginRequest("admin", password);

            // Act
            var result = await controller.Login(request, CancellationToken.None);

            // Assert
            result.Should().BeOfType<OkObjectResult>();
        }

        //Caso: Login exitoso emite refresh token
        [Fact]
        public async Task Login_ReturnsRefreshToken_WhenCredentialsAreValid()
        {
            var context = DbContextMock.GetDbContext();

            var password = "12345678";
            var hash = BCrypt.Net.BCrypt.HashPassword(password);

            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "admin",
                strCorreoElectronico = "admin@test.com",
                strPWD = hash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string> {
                    {"Jwt:Key", "z9WkJ4l2m9VQX1x8bYl+q3hR0Fz9uT7e5K2pL8sD4fA="},
                    {"Jwt:Issuer", "test"},
                    {"Jwt:Audience", "test"}
                }).Build();

            _hasherMock.Setup(h => h.VerifyPassword(password, It.IsAny<string>())).Returns(true);
            _hasherMock.Setup(h => h.NeedsRehash(It.IsAny<string>())).Returns(false);

            _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);

            var service = new LoginService(context, configuration, _hasherMock.Object, _dbResilience, Mock.Of<ILogger<LoginService>>(), _cacheMock.Object, Mock.Of<IMemoryCache>(), _refreshTokenMock.Object);

            var response = await service.LoginAsync(new LoginRequest("admin", password), CancellationToken.None);

            response.Token.Should().NotBeNullOrEmpty();
            response.RefreshToken.Should().NotBeNullOrEmpty();
            response.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }

        //Caso: Usuario no existe (anti-enumeration attack)
        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenUserDoesNotExist()
        {
            var context = DbContextMock.GetDbContext();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string> {
                    {"Jwt:Key", "keykeykeykeykeykeykey"},
                    {"Jwt:Issuer", "test"},
                    {"Jwt:Audience", "test"}
                }).Build();

            _hasherMock.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

            _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);

            var controller = new LoginController(new LoginService(context, configuration, _hasherMock.Object, _dbResilience, Mock.Of<ILogger<LoginService>>(), _cacheMock.Object, Mock.Of<IMemoryCache>(), _refreshTokenMock.Object), _validatorMock.Object);

            var result = await controller.Login(
                new LoginRequest("fake", "12345678"),
                CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        //Caso: Password incorrecto
        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenPasswordIsWrong()
        {
            var context = DbContextMock.GetDbContext();

            var password = "12345678";
            var hash = BCrypt.Net.BCrypt.HashPassword(password);
            context.SegUsuario.Add(new SegUsuario
            {
                strNombre = "admin",
                strCorreoElectronico = "admin@test.com",
                strPWD = hash,
                RowVersion = new byte[] { 1, 0, 0, 0 }
            });
            await context.SaveChangesAsync();

            var config = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string> {
                     {"Jwt:Key", "z9WkJ4l2m9VQX1x8bYl+q3hR0Fz9uT7e5K2pL8sD4fA="},
                     {"Jwt:Issuer", "test"},     
                     {"Jwt:Audience", "test"}
                }).Build();

            _hasherMock.Setup(h => h.VerifyPassword("wrong", It.IsAny<string>())).Returns(false);
            _hasherMock.Setup(h => h.VerifyPassword(password, It.IsAny<string>())).Returns(true);

            _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);

            var controller = new LoginController(new LoginService(context, config, _hasherMock.Object, _dbResilience, Mock.Of<ILogger<LoginService>>(), _cacheMock.Object, Mock.Of<IMemoryCache>(), _refreshTokenMock.Object), _validatorMock.Object);

            var result = await controller.Login(
                new LoginRequest("admin", "wrong"),
                CancellationToken.None);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

     
    }
}

