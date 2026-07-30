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

namespace UnitTest.Factura;

public class FacturaServiceTests
{
    private static FacturaService CreateService(
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
        var loggerMock = new Mock<ILogger<FacturaService>>();

        return new FacturaService(context, dbResilience, eventPublisherMock.Object, loggerMock.Object);
    }

    private static VenPedido SeedPedidoPagado(AppDbContext context, decimal total = 500m)
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
            decTotal = total,
            strEstadoSaga = "Pagado",
        };
        context.VenPedido.Add(pedido);
        context.SaveChanges();
        return pedido;
    }

    [Fact]
    public async Task GenerarFacturaAsync_WithNonExistentPedido_ThrowsArgumentException()
    {
        var context = DbContextMock.GetDbContext();
        var service = CreateService(context, out _);

        var act = () => service.GenerarFacturaAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no existe*");
    }

    [Fact]
    public async Task GenerarFacturaAsync_WithInvalidEstadoSaga_ThrowsInvalidOperationException()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedidoPagado(context);
        pedido.strEstadoSaga = "Pendiente";
        await context.SaveChangesAsync();

        var service = CreateService(context, out _);

        var act = () => service.GenerarFacturaAsync(pedido.id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no está en estado Pagado*");
    }

    [Fact]
    public async Task GenerarFacturaAsync_WithValidPedido_CreatesFacturaAndUpdatesEstado()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedidoPagado(context, 750m);
        var service = CreateService(context, out var eventPublisherMock);

        var result = await service.GenerarFacturaAsync(pedido.id, "XAXX010101000");

        result.Should().NotBeNull();
        result.idVenPedido.Should().Be(pedido.id);
        result.strFolioFactura.Should().StartWith($"F-{DateTime.UtcNow.Year}-");
        result.strRFC.Should().Be("XAXX010101000");
        result.decTotal.Should().Be(750m);
        result.strEstado.Should().Be("Emitida");

        var savedFactura = context.Set<VenPedidoFactura>().FirstOrDefault(f => f.id == result.id);
        savedFactura.Should().NotBeNull();

        var updatedPedido = context.VenPedido.Find(pedido.id);
        updatedPedido!.strEstadoSaga.Should().Be("Facturado");

        eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<FacturaGeneradoEvent>()), Times.Once);
    }

    [Fact]
    public async Task GenerarFacturaAsync_FolioSequence_Increments()
    {
        var context = DbContextMock.GetDbContext();
        var pedido1 = SeedPedidoPagado(context, 100m);
        var pedido2 = SeedPedidoPagado(context, 200m);
        var service = CreateService(context, out _);

        var factura1 = await service.GenerarFacturaAsync(pedido1.id);
        var factura2 = await service.GenerarFacturaAsync(pedido2.id);

        factura1.strFolioFactura.Should().Be($"F-{DateTime.UtcNow.Year}-00001");
        factura2.strFolioFactura.Should().Be($"F-{DateTime.UtcNow.Year}-00002");
    }

    [Fact]
    public async Task CancelarFacturaAsync_WithNonExistentFactura_ReturnsFalse()
    {
        var context = DbContextMock.GetDbContext();
        var service = CreateService(context, out _);

        var result = await service.CancelarFacturaAsync(999);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelarFacturaAsync_WithNonEmitidaFactura_ReturnsFalse()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedidoPagado(context);
        var factura = new VenPedidoFactura
        {
            idVenPedido = pedido.id,
            strFolioFactura = "F-2026-00001",
            decTotal = 500m,
            dteFechaEmision = DateTime.UtcNow,
            strEstado = "Cancelada",
        };
        context.Set<VenPedidoFactura>().Add(factura);
        await context.SaveChangesAsync();

        var service = CreateService(context, out _);

        var result = await service.CancelarFacturaAsync(factura.id);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelarFacturaAsync_WithEmitidaFactura_ReturnsTrue()
    {
        var context = DbContextMock.GetDbContext();
        var pedido = SeedPedidoPagado(context);
        var factura = new VenPedidoFactura
        {
            idVenPedido = pedido.id,
            strFolioFactura = "F-2026-00001",
            decTotal = 500m,
            dteFechaEmision = DateTime.UtcNow,
            strEstado = "Emitida",
        };
        context.Set<VenPedidoFactura>().Add(factura);
        await context.SaveChangesAsync();

        var service = CreateService(context, out _);

        var result = await service.CancelarFacturaAsync(factura.id);

        result.Should().BeTrue();

        var savedFactura = await context.Set<VenPedidoFactura>().FindAsync(factura.id);
        savedFactura!.strEstado.Should().Be("Cancelada");
    }
}
