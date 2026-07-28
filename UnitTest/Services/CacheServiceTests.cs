using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Services;

namespace UnitTest.Services;

public class CacheServiceTests
{
    private readonly ICacheService _cache;
    private readonly MemoryDistributedCache _memoryCache;

    public CacheServiceTests()
    {
        _memoryCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        _cache = new CacheService(_memoryCache);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotFound()
    {
        var result = await _cache.GetAsync<string>("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotFound_ForValueType()
    {
        var result = await _cache.GetAsync<int>("nonexistent");

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesAndCaches_WhenCacheMiss()
    {
        var factoryCalls = 0;

        var result = await _cache.GetOrCreateAsync("miss-key", () =>
        {
            factoryCalls++;
            return Task.FromResult("factory-value");
        });

        result.Should().Be("factory-value");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsCached_WhenCacheHit()
    {
        await _cache.SetAsync("hit-key", "original-value");

        var factoryCalls = 0;
        var result = await _cache.GetOrCreateAsync("hit-key", () =>
        {
            factoryCalls++;
            return Task.FromResult("should-not-call");
        });

        result.Should().Be("original-value");
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsCached_AfterMiss()
    {
        var factoryCalls = 0;

        var first = await _cache.GetOrCreateAsync("miss-then-hit", () =>
        {
            factoryCalls++;
            return Task.FromResult("first-value");
        });

        var second = await _cache.GetOrCreateAsync("miss-then-hit", () =>
        {
            factoryCalls++;
            return Task.FromResult("second-value");
        });

        first.Should().Be("first-value");
        second.Should().Be("first-value");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_UsesCustomTTL()
    {
        var shortTtl = TimeSpan.FromMilliseconds(50);

        await _cache.GetOrCreateAsync("ttl-key", () => Task.FromResult("ttl-value"), shortTtl);

        var result = await _cache.GetAsync<string>("ttl-key");
        result.Should().Be("ttl-value");

        await Task.Delay(100);

        var expired = await _cache.GetAsync<string>("ttl-key");
        expired.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_SetsValue()
    {
        await _cache.SetAsync("set-key", "set-value");

        var result = await _cache.GetAsync<string>("set-key");
        result.Should().Be("set-value");
    }

    [Fact]
    public async Task SetAsync_SetsComplexType()
    {
        var value = new TestDto { Id = 1, Name = "test" };
        await _cache.SetAsync("complex-key", value);

        var result = await _cache.GetAsync<TestDto>("complex-key");

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("test");
    }

    [Fact]
    public async Task RemoveAsync_RemovesValue()
    {
        await _cache.SetAsync("remove-key", "to-remove");
        (await _cache.GetAsync<string>("remove-key")).Should().NotBeNull();

        await _cache.RemoveAsync("remove-key");

        var result = await _cache.GetAsync<string>("remove-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenKeyNotExists()
    {
        var act = async () => await _cache.RemoveAsync("not-exists");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetOrCreateAsync_Throws_WhenFactoryThrows()
    {
        var act = async () => await _cache.GetOrCreateAsync<int>("error-key", () =>
            throw new InvalidOperationException("factory error"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("factory error");
    }

    [Fact]
    public async Task SetAsync_UsesCustomTTL()
    {
        var shortTtl = TimeSpan.FromMilliseconds(50);

        await _cache.SetAsync("custom-ttl", "expires-fast", shortTtl);

        (await _cache.GetAsync<string>("custom-ttl")).Should().Be("expires-fast");

        await Task.Delay(100);

        (await _cache.GetAsync<string>("custom-ttl")).Should().BeNull();
    }

    private class TestDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
