using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;
using UnitTest.Common;
using FluentAssertions;

namespace UnitTest.Services;

public class CompensationServiceTests
{
    private static CompensationService CreateService(
        AppDbContext context,
        out Mock<IPagoService> pagoServiceMock)
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

        pagoServiceMock = new Mock<IPagoService>();
        var loggerMock = new Mock<ILogger<CompensationService>>();

        return new CompensationService(context, dbResilience, pagoServiceMock.Object, loggerMock.Object);
    }

    private static (CliCliente Cliente, ProProducto Producto) SeedData(AppDbContext context)
    {
        var cliente = new CliCliente
        {
            strNombreCliente = "Test",
            strCorreoElectronico = "test@test.com",
            strNumeroTelefono = "555-1234"
        };
        context.CliCliente.Add(cliente);

        var producto = new ProProducto
        {
            strNombreProducto = "Producto Test",
            decPrecio = 100m,
            intNumeroExistencia = 10,
        };
        context.ProProducto.Add(producto);
        context.SaveChanges();

        return (cliente, producto);
    }

    [Fact]
    public async Task CompensarPorPagoRechazado_RestauraStock_Y_CambiaEstado()
    {
        var context = DbContextMock.GetDbContext();
        var (cliente, producto) = SeedData(context);

        var pedido = new VenPedido
        {
            id = Guid.NewGuid(),
            idCliCliente = cliente.id,
            dteFechaPedido = DateTime.UtcNow,
            decTotal = 200m,
            strEstadoSaga = "PagoRechazado",
        };
        context.VenPedido.Add(pedido);

        var detalle = new VenPedidoDetalle
        {
            idVenPedido = pedido.id,
            idProProducto = producto.id,
            intCantidad = 3,
            decPrecioUnitario = 100m,
        };
        context.VenPedidoDetalle.Add(detalle);
        context.SaveChanges();

        var service = CreateService(context, out _);

        await service.CompensarPorPagoRechazadoAsync(pedido.id);

        var productoActualizado = context.ProProducto.Find(producto.id);
        productoActualizado!.intNumeroExistencia.Should().Be(13);

        var pedidoActualizado = context.VenPedido.Find(pedido.id);
        pedidoActualizado!.strEstadoSaga.Should().Be("CompensadoPago");
    }

    [Fact]
    public async Task CompensarPorPagoRechazado_WithNonExistentPedido_DoesNotThrow()
    {
        var context = DbContextMock.GetDbContext();
        var service = CreateService(context, out _);

        var act = () => service.CompensarPorPagoRechazadoAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CompensarPorFacturaRechazada_RestauraStock_ReembolsaPago_Y_CambiaEstado()
    {
        var context = DbContextMock.GetDbContext();
        var (cliente, producto) = SeedData(context);

        var pedido = new VenPedido
        {
            id = Guid.NewGuid(),
            idCliCliente = cliente.id,
            dteFechaPedido = DateTime.UtcNow,
            decTotal = 200m,
            strEstadoSaga = "FacturaRechazada",
        };
        context.VenPedido.Add(pedido);

        var detalle = new VenPedidoDetalle
        {
            idVenPedido = pedido.id,
            idProProducto = producto.id,
            intCantidad = 2,
            decPrecioUnitario = 100m,
        };
        context.VenPedidoDetalle.Add(detalle);

        var pago = new VenPedidoPago
        {
            idVenPedido = pedido.id,
            decMonto = 200m,
            strMetodoPago = "Tarjeta",
            strIdTransaccion = "TXN-test",
            strEstado = "Completado",
            dteFechaPago = DateTime.UtcNow,
        };
        context.VenPedidoPago.Add(pago);
        context.SaveChanges();

        var service = CreateService(context, out var pagoServiceMock);
        pagoServiceMock.Setup(p => p.ReembolsarPagoAsync(pago.id))
            .ReturnsAsync(true);

        await service.CompensarPorFacturaRechazadaAsync(pedido.id);

        var productoActualizado = context.ProProducto.Find(producto.id);
        productoActualizado!.intNumeroExistencia.Should().Be(12);

        var pedidoActualizado = context.VenPedido.Find(pedido.id);
        pedidoActualizado!.strEstadoSaga.Should().Be("CompensadoFactura");

        pagoServiceMock.Verify(p => p.ReembolsarPagoAsync(pago.id), Times.Once);
    }
}
