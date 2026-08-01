using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Moq;
using Polly.CircuitBreaker;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Services;

namespace UnitTest.Services;

public class DbResilienceServiceTests
{
    private static (DbResilienceService Service, Mock<ILogger<DbResilienceService>> Logger) CreateService(int breakDurationSeconds = 5)
    {
        var options = Options.Create(new ResilienceOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = 2,
            SamplingDurationSeconds = 60,
            BreakDurationSeconds = breakDurationSeconds
        });
        var logger = new Mock<ILogger<DbResilienceService>>();
        return (new DbResilienceService(options, logger.Object), logger);
    }

    private static Mock<AppDbContext> CreateDbContextMock()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Mock<AppDbContext>(opts) { CallBase = true };
    }

    [Fact]
    public async Task CircuitBreaker_Opens_After_MinimumThroughput_Failures()
    {
        var (service, logger) = CreateService();

        var dbMock = CreateDbContextMock();
        dbMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated DB failure"));

        for (int i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.SaveChangesAsync(dbMock.Object));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        LogVerifier.VerifyLog(logger, LogLevel.Warning, "Circuit breaker abierto", Times.Once());
    }

    [Fact]
    public async Task CircuitBreaker_Closes_After_HalfOpen_Success()
    {
        var (service, logger) = CreateService(breakDurationSeconds: 1);

        var dbMock = CreateDbContextMock();
        dbMock.SetupSequence(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("fail1"))
            .ThrowsAsync(new DbUpdateException("fail2"))
            .ReturnsAsync(1);

        for (int i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.SaveChangesAsync(dbMock.Object));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        await Task.Delay(1500);

        var result = await service.SaveChangesAsync(dbMock.Object);
        Assert.Equal(1, result);

        LogVerifier.VerifyLog(logger, LogLevel.Information, "Circuit breaker en modo half-open", Times.Once());
        LogVerifier.VerifyLog(logger, LogLevel.Information, "Circuit breaker cerrado tras recuperación", Times.Once());
    }

    [Fact]
    public async Task CircuitBreaker_Reopens_After_HalfOpen_Failure()
    {
        var (service, logger) = CreateService(breakDurationSeconds: 1);

        var dbMock = CreateDbContextMock();
        dbMock.SetupSequence(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("fail1"))
            .ThrowsAsync(new DbUpdateException("fail2"))
            .ThrowsAsync(new DbUpdateException("half-open fails"));

        for (int i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.SaveChangesAsync(dbMock.Object));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        await Task.Delay(1500);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        LogVerifier.VerifyLog(logger, LogLevel.Information, "Circuit breaker en modo half-open", Times.Once());
        LogVerifier.VerifyLog(logger, LogLevel.Warning, "Circuit breaker abierto", Times.Exactly(2));
    }
}
