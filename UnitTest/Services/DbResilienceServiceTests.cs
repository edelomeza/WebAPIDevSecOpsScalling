using System.Diagnostics;
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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string message)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(25);
        }
        Assert.Fail($"Timeout esperando estado: {message}");
    }

    [Fact]
    public async Task CircuitBreaker_Opens_After_MinimumThroughput_Failures()
    {
        // Break largo (30s): el assert de rechazo inmediato queda libre de flake
        // de scheduling sin añadir tiempo (no se espera al break). Este test es el
        // único que cubre "rechaza mientras está abierto"; los demás no lo duplican.
        var (service, logger) = CreateService(breakDurationSeconds: 30);

        var dbMock = CreateDbContextMock();
        dbMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated DB failure"));

        for (int i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.SaveChangesAsync(dbMock.Object));
        }

        await WaitUntilAsync(() => service.CircuitState == CircuitState.Open,
            TimeSpan.FromSeconds(5), "circuito abierto tras mínimo de fallos");

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        LogVerifier.VerifyLog(logger, LogLevel.Warning, "Circuit breaker abierto", Times.Once());
    }

    [Fact]
    public async Task CircuitBreaker_Closes_After_HalfOpen_Success()
    {
        // Sin assert intermedio de BrokenCircuit: con break corto, un stall del runner
        // mayor al break deja entrar la llamada como trial half-open (flake "No exception
        // was thrown"). El rechazo en abierto ya lo cubre Opens_After_MinimumThroughput.
        var (service, logger) = CreateService(breakDurationSeconds: 3);

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

        await WaitUntilAsync(() => service.CircuitState == CircuitState.Open,
            TimeSpan.FromSeconds(5), "circuito abierto tras mínimo de fallos");

        // Delay > break en dirección segura (los delays no disparan antes; llegar tarde
        // al trial no rompe nada): la siguiente llamada es el trial half-open.
        await Task.Delay(3500);

        var result = await service.SaveChangesAsync(dbMock.Object);
        Assert.Equal(1, result);
        Assert.Equal(CircuitState.Closed, service.CircuitState);

        LogVerifier.VerifyLog(logger, LogLevel.Information, "Circuit breaker en modo half-open", Times.Once());
        LogVerifier.VerifyLog(logger, LogLevel.Information, "Circuit breaker cerrado tras recuperación", Times.Once());
    }

    [Fact]
    public async Task CircuitBreaker_Reopens_After_HalfOpen_Failure()
    {
        // Sin asserts inmediatos de BrokenCircuit (mismo race que Closes_After_HalfOpen_Success,
        // cubiertos por Opens_After_MinimumThroughput). El re-open se prueba de forma
        // determinista con WaitUntil(Open) + "abierto Exactly(2)" en logs.
        var (service, logger) = CreateService(breakDurationSeconds: 3);

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

        await WaitUntilAsync(() => service.CircuitState == CircuitState.Open,
            TimeSpan.FromSeconds(5), "circuito abierto tras mínimo de fallos");

        await Task.Delay(3500);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.SaveChangesAsync(dbMock.Object));

        await WaitUntilAsync(() => service.CircuitState == CircuitState.Open,
            TimeSpan.FromSeconds(5), "circuito reabierto tras fallo en half-open");

        LogVerifier.VerifyLog(logger, LogLevel.Information, "Circuit breaker en modo half-open", Times.Once());
        LogVerifier.VerifyLog(logger, LogLevel.Warning, "Circuit breaker abierto", Times.Exactly(2));
    }
}
