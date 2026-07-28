using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using WebAPIDevSecOps.Services;

namespace UnitTest.Login;

public class TokenBlacklistServiceTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly TokenBlacklistService _service;

    public TokenBlacklistServiceTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _service = new TokenBlacklistService(_cacheMock.Object);
    }

    [Fact]
    public async Task AddAsync_Should_Store_Jti_With_Blacklist_Prefix()
    {
        var jti = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromMinutes(60);

        await _service.AddAsync(jti, expiry);

        _cacheMock.Verify(c => c.SetAsync(
            $"blacklist:{jti}",
            It.Is<byte[]>(b => b.Length == 1 && b[0] == 1),
            It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == expiry),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsBlacklistedAsync_Returns_True_When_Jti_Exists()
    {
        var jti = Guid.NewGuid().ToString();
        _cacheMock.Setup(c => c.GetAsync($"blacklist:{jti}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1 });

        var result = await _service.IsBlacklistedAsync(jti);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsBlacklistedAsync_Returns_False_When_Jti_Does_Not_Exist()
    {
        var jti = Guid.NewGuid().ToString();
        _cacheMock.Setup(c => c.GetAsync($"blacklist:{jti}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await _service.IsBlacklistedAsync(jti);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_Then_IsBlacklistedAsync_Returns_True()
    {
        var jti = Guid.NewGuid().ToString();
        var expiry = TimeSpan.FromMinutes(30);

        _cacheMock.Setup(c => c.SetAsync(
            $"blacklist:{jti}",
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _cacheMock.Setup(c => c.GetAsync($"blacklist:{jti}", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new byte[] { 1 });
            })
            .Returns(Task.CompletedTask);

        await _service.AddAsync(jti, expiry);
        var result = await _service.IsBlacklistedAsync(jti);

        result.Should().BeTrue();
    }
}
