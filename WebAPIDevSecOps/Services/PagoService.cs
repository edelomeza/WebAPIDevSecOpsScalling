using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class PagoService : IPagoService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<PagoService> _logger;

        public PagoService(
            AppDbContext context,
            DbResilienceService dbResilience,
            IEventPublisher eventPublisher,
            ILogger<PagoService> logger)
        {
            _context = context;
            _dbResilience = dbResilience;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<PagoResponseDto> ProcesarPagoAsync(Guid pedidoId, string metodoPago, decimal monto)
        {
            var pedido = await _context.Set<VenPedido>()
                .FirstOrDefaultAsync(p => p.id == pedidoId);

            if (pedido == null)
                throw new ArgumentException($"El pedido con ID {pedidoId} no existe.");

            if (pedido.strEstadoSaga != "Pendiente" && pedido.strEstadoSaga != "StockValidado")
                throw new InvalidOperationException(
                    $"El pedido no está en estado Pendiente ni StockValidado (actual: {pedido.strEstadoSaga}).");

            if (monto != pedido.decTotal)
                throw new ArgumentException(
                    $"El monto {monto} no coincide con el total del pedido {pedido.decTotal}.");

            var exito = RandomNumberGenerator.GetInt32(100) < 90;
            var idTransaccion = $"TXN-{Guid.NewGuid():N}";

            var pago = new VenPedidoPago
            {
                idVenPedido = pedidoId,
                decMonto = monto,
                strMetodoPago = metodoPago,
                strIdTransaccion = idTransaccion,
                strEstado = exito ? "Completado" : "Rechazado",
                dteFechaPago = DateTime.UtcNow,
            };

            _context.Set<VenPedidoPago>().Add(pago);
            pedido.strEstadoSaga = exito ? "Pagado" : "PagoRechazado";

            if (!exito)
                pedido.strMotivoRechazo = "El procesador de pago rechazó la transacción.";

            await _dbResilience.SaveChangesAsync(_context);

            if (exito)
            {
                _logger.LogInformation("Pago procesado: Pedido {PedidoId}, Transacción {IdTransaccion}, Monto {Monto}",
                    pedidoId, idTransaccion, monto);

                await _eventPublisher.PublishAsync(new PagoProcesadoEvent
                {
                    PedidoId = pedidoId,
                    IdTransaccion = idTransaccion,
                    Monto = monto,
                });
            }
            else
            {
                _logger.LogWarning("Pago rechazado: Pedido {PedidoId}", pedidoId);

                await _eventPublisher.PublishAsync(new PagoRechazadoEvent
                {
                    PedidoId = pedidoId,
                    Motivo = pedido.strMotivoRechazo!,
                });
            }

            return new PagoResponseDto
            {
                id = pago.id,
                idVenPedido = pago.idVenPedido,
                decMonto = pago.decMonto,
                strMetodoPago = pago.strMetodoPago,
                strIdTransaccion = pago.strIdTransaccion,
                strEstado = pago.strEstado,
                dteFechaPago = pago.dteFechaPago,
            };
        }

        public async Task<PagoResponseDto?> GetByIdAsync(int id)
        {
            return await _context.Set<VenPedidoPago>()
                .AsNoTracking()
                .Where(p => p.id == id)
                .Select(p => new PagoResponseDto
                {
                    id = p.id,
                    idVenPedido = p.idVenPedido,
                    decMonto = p.decMonto,
                    strMetodoPago = p.strMetodoPago,
                    strIdTransaccion = p.strIdTransaccion,
                    strEstado = p.strEstado,
                    dteFechaPago = p.dteFechaPago,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<List<PagoResponseDto>> GetByPedidoIdAsync(Guid pedidoId)
        {
            return await _context.Set<VenPedidoPago>()
                .AsNoTracking()
                .Where(p => p.idVenPedido == pedidoId)
                .OrderByDescending(p => p.dteFechaPago)
                .Select(p => new PagoResponseDto
                {
                    id = p.id,
                    idVenPedido = p.idVenPedido,
                    decMonto = p.decMonto,
                    strMetodoPago = p.strMetodoPago,
                    strIdTransaccion = p.strIdTransaccion,
                    strEstado = p.strEstado,
                    dteFechaPago = p.dteFechaPago,
                })
                .ToListAsync();
        }

        public async Task<bool> ReembolsarPagoAsync(int pagoId)
        {
            var pago = await _context.Set<VenPedidoPago>()
                .FirstOrDefaultAsync(p => p.id == pagoId);

            if (pago == null)
                return false;

            if (pago.strEstado != "Completado")
                return false;

            pago.strEstado = "Reembolsado";
            await _dbResilience.SaveChangesAsync(_context);

            _logger.LogInformation("Pago reembolsado: PagoId {PagoId}, Transacción {IdTransaccion}",
                pagoId, pago.strIdTransaccion);

            return true;
        }
    }
}
