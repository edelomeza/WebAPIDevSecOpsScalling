using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;
using UnitTest.Common;
using FluentAssertions;

namespace UnitTest.Pago;

public class PagoServiceTests
{
    private static PagoService CreateService(
        AppDbContext context,
        out Mock<IEventPublisher> eventPublisherMock)
    {
        var resilienceOptions = Options.Create(new ResilienceOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = 2,
            SamplingDurationSeconds = 60,
            BreakDurationSeconds = 5
        });
        var resilienceLogger = new Mock<ILogger<DbResilienceService>>();
        var dbResilience = new DbResilienceService(resilienceOptions, resilienceLogger.Object);

        eventPublisherMock = new Mock<IEventPublisher>();

        var loggerMock = new Mock<ILogger<PagoService>>();

        return new PagoService(
            context,
            dbResilience,
            eventPublisherMock.Object,
            loggerMock.Object);
    }

    private static VenPedido SeedPedido(AppDbContext context, string estadoSaga = "Pendiente")
    {
        var cliente = new CliCliente
        {
            strNombreCliente = "Test",
            strCorreoElectronico = "test@test.com",
            strNumeroTelefono = "555-1234"
        };
        context.CliCliente.Add(cliente);
        context.SaveChanges();

        var pedido = new VenPedido
        {
            id = Guid.NewGuid(),
            idCliCliente = cliente.id,
            dteFechaPedido = DateTime.UtcNow,
            decTotal = 100.50m,
            strEstadoSaga = estadoSaga,
        };
        context.VenPedido.Add(pedido);
        context.SaveChanges();
        return pedido;
    }

    [Fact]
    public async Task ProcesarPagoAsync_WithNonExistentPedido_ThrowsArgumentException()
    {
        var context = DbContextMock.GetDbContext();
        var service = CreateService(context, out _);

        var act = () => service.ProcesarPagoAsync(Guid.NewGuid(), "Tarjeta", 100m);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no existe*");
    }

    [Fact]
    public async Task ProcesarPagoAsync_WithInvalidEstadoSaga_ThrowsInvalidOperationException()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedido(context, "Pagado");
        var service = CreateService(context, out _);

        var act = () => service.ProcesarPagoAsync(pedido.id, "Tarjeta", 100m);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no está en estado Pendiente*");
    }

    [Fact]
    public async Task ProcesarPagoAsync_WithStockValidadoEstadoSaga_DoesNotThrow()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedido(context, "StockValidado");
        var service = CreateService(context, out var eventPublisherMock);

        var act = () => service.ProcesarPagoAsync(pedido.id, "Tarjeta", 100.50m);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcesarPagoAsync_PublishesEvent_And_CreatesPagoRecord()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedido(context);
        var service = CreateService(context, out var eventPublisherMock);

        var result = await service.ProcesarPagoAsync(pedido.id, "Tarjeta", 100.50m);

        result.Should().NotBeNull();
        result.idVenPedido.Should().Be(pedido.id);
        result.decMonto.Should().Be(100.50m);
        result.strMetodoPago.Should().Be("Tarjeta");
        result.strIdTransaccion.Should().StartWith("TXN-");
        result.dteFechaPago.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var savedPago = context.Set<VenPedidoPago>().FirstOrDefault(p => p.id == result.id);
        savedPago.Should().NotBeNull();

        var updatedPedido = context.VenPedido.Find(pedido.id);
        updatedPedido.Should().NotBeNull();

        if (result.strEstado == "Completado")
        {
            updatedPedido!.strEstadoSaga.Should().Be("Pagado");
            eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<PagoProcesadoEvent>()), Times.Once);
        }
        else
        {
            updatedPedido!.strEstadoSaga.Should().Be("PagoRechazado");
            updatedPedido.strMotivoRechazo.Should().NotBeNull();
            eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<PagoRechazadoEvent>()), Times.Once);
        }
    }

    [Fact]
    public async Task ReembolsarPagoAsync_WithNonExistentPago_ReturnsFalse()
    {
        var context = DbContextMock.GetDbContext();
        var service = CreateService(context, out _);

        var result = await service.ReembolsarPagoAsync(999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReembolsarPagoAsync_WithNonCompletedPago_ReturnsFalse()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedido(context);
        var pago = new VenPedidoPago
        {
            idVenPedido = pedido.id,
            decMonto = 100m,
            strMetodoPago = "Tarjeta",
            strIdTransaccion = "TXN-test",
            strEstado = "Rechazado",
            dteFechaPago = DateTime.UtcNow,
        };
        context.Set<VenPedidoPago>().Add(pago);
        await context.SaveChangesAsync();

        var service = CreateService(context, out _);

        var result = await service.ReembolsarPagoAsync(pago.id);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReembolsarPagoAsync_WithCompletedPago_ReturnsTrue()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedido(context);
        var pago = new VenPedidoPago
        {
            idVenPedido = pedido.id,
            decMonto = 100m,
            strMetodoPago = "Tarjeta",
            strIdTransaccion = "TXN-test",
            strEstado = "Completado",
            dteFechaPago = DateTime.UtcNow,
        };
        context.Set<VenPedidoPago>().Add(pago);
        await context.SaveChangesAsync();

        var service = CreateService(context, out _);

        var result = await service.ReembolsarPagoAsync(pago.id);

        result.Should().BeTrue();

        var savedPago = await context.Set<VenPedidoPago>().FindAsync(pago.id);
        savedPago!.strEstado.Should().Be("Reembolsado");
    }
}
