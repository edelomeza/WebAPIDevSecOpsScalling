using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using UnitTest.Common;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Services;

namespace UnitTest.Pago
{
    public class PagoServiceConsistencyTests
    {
        private static PagoService CreateService(
            AppDbContext context,
            out Mock<IEventPublisher> eventPublisherMock,
            out Mock<ILogger<PagoService>> loggerMock)
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
            loggerMock = new Mock<ILogger<PagoService>>();

            return new PagoService(
                context,
                dbResilience,
                eventPublisherMock.Object,
                loggerMock.Object);
        }

        private static VenPedido SeedPedido(AppDbContext context, decimal total = 100.50m)
        {
            var cliente = new CliCliente
            {
                strNombreCliente = "Test",
                strCorreoElectronico = $"cli{Guid.NewGuid():N}@test.com",
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
                strEstadoSaga = "Pendiente",
            };
            context.VenPedido.Add(pedido);
            context.SaveChanges();
            return pedido;
        }

        [Fact]
        public async Task ProcesarPagoAsync_MontoNoCoincide_ThrowsWithMessage()
        {
            var context = DbContextMock.GetDbContext();
            var pedido = SeedPedido(context, 100.50m);
            var service = CreateService(context, out _, out _);

            var act = () => service.ProcesarPagoAsync(pedido.id, "Tarjeta", 50m);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("El monto 50 no coincide con el total del pedido 100.50.");
        }

        [Fact]
        public async Task ProcesarPagoAsync_AmbasRamas_ConsistentesYConEventos()
        {
            var context = DbContextMock.GetDbContext();
            var service = CreateService(context, out var eventPublisherMock, out var loggerMock);
            const decimal monto = 100.50m;
            var completados = 0;
            var rechazados = 0;

            for (var i = 0; i < 300 && (completados == 0 || rechazados == 0); i++)
            {
                var pedido = SeedPedido(context, monto);
                var result = await service.ProcesarPagoAsync(pedido.id, "Tarjeta", monto);

                var pedidoActualizado = context.VenPedido.Find(pedido.id)!;
                var pagoGuardado = context.Set<VenPedidoPago>().First(p => p.id == result.id);

                pagoGuardado.strEstado.Should().Be(result.strEstado);
                pagoGuardado.strIdTransaccion.Should().StartWith("TXN-");

                if (result.strEstado == "Completado")
                {
                    completados++;
                    pedidoActualizado.strEstadoSaga.Should().Be("Pagado");
                    pedidoActualizado.strMotivoRechazo.Should().BeNull();
                    pagoGuardado.strEstado.Should().Be("Completado");
                }
                else
                {
                    rechazados++;
                    pedidoActualizado.strEstadoSaga.Should().Be("PagoRechazado");
                    pedidoActualizado.strMotivoRechazo.Should().Be("El procesador de pago rechazó la transacción.");
                    pagoGuardado.strEstado.Should().Be("Rechazado");
                }
            }

            completados.Should().BeGreaterThan(0, "deben observarse pagos completados");
            rechazados.Should().BeGreaterThan(0, "deben observarse pagos rechazados");

            eventPublisherMock.Verify(
                p => p.PublishAsync(It.Is<PagoProcesadoEvent>(e =>
                    e.Monto == monto && !string.IsNullOrEmpty(e.IdTransaccion))),
                Times.Exactly(completados));
            eventPublisherMock.Verify(
                p => p.PublishAsync(It.Is<PagoRechazadoEvent>(e =>
                    e.Motivo == "El procesador de pago rechazó la transacción.")),
                Times.Exactly(rechazados));

            LogVerifier.VerifyLog(loggerMock, LogLevel.Information, "Pago procesado", Times.Exactly(completados));
            LogVerifier.VerifyLog(loggerMock, LogLevel.Warning, "Pago rechazado", Times.Exactly(rechazados));
        }

        [Fact]
        public async Task ReembolsarPagoAsync_Completado_RegistraLog()
        {
            var context = DbContextMock.GetDbContext();
            var pedido = SeedPedido(context);
            var pago = new VenPedidoPago
            {
                idVenPedido = pedido.id,
                decMonto = 100m,
                strMetodoPago = "Tarjeta",
                strIdTransaccion = "TXN-reembolso-test",
                strEstado = "Completado",
                dteFechaPago = DateTime.UtcNow,
            };
            context.Set<VenPedidoPago>().Add(pago);
            await context.SaveChangesAsync();
            var service = CreateService(context, out _, out var loggerMock);

            var result = await service.ReembolsarPagoAsync(pago.id);

            result.Should().BeTrue();
            LogVerifier.VerifyLog(loggerMock, LogLevel.Information, "Pago reembolsado", Times.Once());
        }
    }
}
