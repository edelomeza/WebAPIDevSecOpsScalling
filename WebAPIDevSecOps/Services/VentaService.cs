using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class VentaService : IVentaService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly IEventPublisher _eventPublisher;
        private readonly IConfiguration _configuration;
        private readonly ILogger<VentaService> _logger;

        // Serializa el finalize 1->2 por venta: evita doble PublishAsync ante PUT concurrentes/reintentos.
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _finalizeLocks = new();

        // VenCatEstado del catálogo: 1 = "En compra usuario", 2 = "Fin compra usuario".
        private const int EstadoEnCompra = 1;
        private const int EstadoFinCompra = 2;

        public VentaService(
            AppDbContext context,
            DbResilienceService dbResilience,
            IEventPublisher eventPublisher,
            IConfiguration configuration,
            ILogger<VentaService> logger)
        {
            _context = context;
            _dbResilience = dbResilience;
            _eventPublisher = eventPublisher;
            _configuration = configuration;
            _logger = logger;
        }

        private bool IsSagaBridgeEnabled() =>
            _configuration.GetValue<bool>("Feature:SagaBridge");

        public async Task<PagedResult<VenVentaDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.Set<VenVenta>()
                .AsNoTracking()
                .Include(v => v.CliCliente)
                .Include(v => v.SegUsuario)
                .Include(v => v.VenCatEstado)
                .Select(v => new VenVentaDto
                {
                    id = v.id,
                    idCliCliente = v.idCliCliente,
                    strNombreCliente = v.CliCliente != null ? v.CliCliente.strNombreCliente : null,
                    idSegUsuario = v.idSegUsuario,
                    strNombreUsuario = v.SegUsuario != null ? v.SegUsuario.strNombre : null,
                    idVenCatEstado = v.idVenCatEstado,
                    strEstado = v.VenCatEstado != null ? v.VenCatEstado.strValor : null,
                    dteFechaHoraCompra = v.dteFechaHoraCompra,
                    strClaveVenta = v.strClaveVenta,
                    RowVersion = v.RowVersion,
                });

            var totalCount = await query.CountAsync();
            query = query.ApplyPagination(p);
            var items = await query.ToListAsync();

            return new PagedResult<VenVentaDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize,
            };
        }

        public async Task<VenVentaDto?> GetByIdAsync(int id)
        {
            return await _context.Set<VenVenta>()
                .AsNoTracking()
                .Include(v => v.CliCliente)
                .Include(v => v.SegUsuario)
                .Include(v => v.VenCatEstado)
                .Where(v => v.id == id)
                .Select(v => new VenVentaDto
                {
                    id = v.id,
                    idCliCliente = v.idCliCliente,
                    strNombreCliente = v.CliCliente != null ? v.CliCliente.strNombreCliente : null,
                    idSegUsuario = v.idSegUsuario,
                    strNombreUsuario = v.SegUsuario != null ? v.SegUsuario.strNombre : null,
                    idVenCatEstado = v.idVenCatEstado,
                    strEstado = v.VenCatEstado != null ? v.VenCatEstado.strValor : null,
                    dteFechaHoraCompra = v.dteFechaHoraCompra,
                    strClaveVenta = v.strClaveVenta,
                    RowVersion = v.RowVersion,
                })
                .FirstOrDefaultAsync();
        }

        public async Task<PagedResult<VenVentaDto>> SearchAsync(string? strClaveVenta, string? strNombreCliente, DateTime? dteFechaInicio, DateTime? dteFechaFin, QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.Set<VenVenta>()
                .AsNoTracking()
                .Include(v => v.CliCliente)
                .Include(v => v.SegUsuario)
                .Include(v => v.VenCatEstado)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(strClaveVenta))
            {
                query = query.Where(v => v.strClaveVenta.ToLower().Contains(strClaveVenta.ToLower()));
            }

            if (!string.IsNullOrWhiteSpace(strNombreCliente))
            {
                query = query.Where(v => v.CliCliente != null && v.CliCliente.strNombreCliente.ToLower().Contains(strNombreCliente.ToLower()));
            }

            if (dteFechaInicio.HasValue)
            {
                var startDate = dteFechaInicio.Value.Date;
                query = query.Where(v => v.dteFechaHoraCompra >= startDate);
            }

            if (dteFechaFin.HasValue)
            {
                var endDate = dteFechaFin.Value.Date.AddDays(1);
                query = query.Where(v => v.dteFechaHoraCompra < endDate);
            }

            var selectQuery = query.Select(v => new VenVentaDto
            {
                id = v.id,
                idCliCliente = v.idCliCliente,
                strNombreCliente = v.CliCliente != null ? v.CliCliente.strNombreCliente : null,
                idSegUsuario = v.idSegUsuario,
                strNombreUsuario = v.SegUsuario != null ? v.SegUsuario.strNombre : null,
                idVenCatEstado = v.idVenCatEstado,
                strEstado = v.VenCatEstado != null ? v.VenCatEstado.strValor : null,
                dteFechaHoraCompra = v.dteFechaHoraCompra,
                strClaveVenta = v.strClaveVenta,
                RowVersion = v.RowVersion,
            });

            var totalCount = await selectQuery.CountAsync();
            selectQuery = selectQuery.ApplyPagination(p);
            var items = await selectQuery.ToListAsync();

            return new PagedResult<VenVentaDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize,
            };
        }

        public async Task UpdateAsync(int id, VenVentaUpdateDto dto)
        {
            if (id != dto.id)
                throw new ArgumentException("El ID de la venta no coincide.");

            var venta = await _context.Set<VenVenta>()
                .FirstOrDefaultAsync(v => v.id == id);

            if (venta == null)
                throw new KeyNotFoundException("Venta no encontrada.");

            var clienteExiste = await _context.CliCliente.AnyAsync(c => c.id == dto.idCliCliente);
            if (!clienteExiste)
                throw new ArgumentException("El cliente especificado no existe.");

            var usuarioExiste = await _context.SegUsuario.AnyAsync(u => u.id == dto.idSegUsuario);
            if (!usuarioExiste)
                throw new ArgumentException("El usuario especificado no existe.");

            var estadoExiste = await _context.VenCatEstado.AnyAsync(e => e.id == dto.idVenCatEstado);
            if (!estadoExiste)
                throw new ArgumentException("El estado especificado no existe.");

            if (dto.RowVersion is { Length: > 0 })
                _context.Entry(venta).Property("RowVersion").OriginalValue = dto.RowVersion;

            // Criterio 10 (idempotencia PUT): la finalización saga solo ocurre en la transición 1->2.
            // Se captura el estado previo ANTES de mutar para distinguir re-PUT 2->2 (no-op).
            var estadoPrevio = venta.idVenCatEstado;

            venta.idCliCliente = dto.idCliCliente;
            venta.idSegUsuario = dto.idSegUsuario;
            venta.idVenCatEstado = dto.idVenCatEstado;

            _context.Entry(venta).State = EntityState.Modified;
            await _dbResilience.SaveChangesAsync(_context);

            // Finalize: PUT 1->2 dispara la saga (una sola vez). Cualquier otra transición es no-op.
            if (IsSagaBridgeEnabled() && estadoPrevio == EstadoEnCompra && dto.idVenCatEstado == EstadoFinCompra)
            {
                await FinalizarSagaAsync(id);
            }
        }

        /// <summary>
        /// PostDespliegue9: publica exactamente un PedidoCreadoEvent para el pedido espejo.
        /// Idempotente: solo publica si el pedido sigue en "Pendiente". Reintentos concurrentes se serializan.
        /// </summary>
        private async Task FinalizarSagaAsync(int idVenVenta)
        {
            var gate = _finalizeLocks.GetOrAdd(idVenVenta, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var pedido = await _context.Set<VenPedido>()
                    .Include(p => p.Detalles)
                    .FirstOrDefaultAsync(p => p.LegacyVentaId == idVenVenta);

                if (pedido == null)
                {
                    _logger.LogWarning("SagaBridge: venta {VentaId} pasó a estado 2 sin pedido espejo; no se publica evento", idVenVenta);
                    return;
                }

                // Criterio 8: exactamente un evento. Si ya avanzó (o ya se publicó), no republicar.
                if (!string.Equals(pedido.strEstadoSaga, "Pendiente", StringComparison.Ordinal))
                {
                    _logger.LogInformation("SagaBridge: pedido {PedidoId} ya en estado {Estado}; finalize no-op", pedido.id, pedido.strEstadoSaga);
                    return;
                }

                if (pedido.Detalles.Count == 0)
                {
                    throw new ArgumentException("La venta no tiene detalles; no se puede finalizar la compra.");
                }

                // Criterio 7: recalcular el total desde la BD (fuente de verdad), no del DTO.
                var precios = await _context.Set<ProProducto>()
                    .Where(p => pedido.Detalles.Select(d => d.idProProducto).Contains(p.id))
                    .ToDictionaryAsync(p => p.id, p => p.decPrecio);

                decimal total = 0;
                var items = new List<PedidoCreadoDetalleItem>(pedido.Detalles.Count);
                foreach (var d in pedido.Detalles)
                {
                    if (!precios.TryGetValue(d.idProProducto, out var precio))
                        throw new ArgumentException($"El producto con ID {d.idProProducto} no existe.");
                    total += d.intCantidad * precio;
                    items.Add(new PedidoCreadoDetalleItem
                    {
                        idProProducto = d.idProProducto,
                        intCantidad = d.intCantidad,
                        decPrecioUnitario = precio,
                    });
                }

                pedido.decTotal = total;
                await _dbResilience.SaveChangesAsync(_context);

                var evento = new PedidoCreadoEvent
                {
                    PedidoId = pedido.id,
                    ClienteId = pedido.idCliCliente,
                    Total = total,
                    Detalles = items,
                    FechaCreacion = DateTime.UtcNow,
                };

                // Criterio 13: retry de publicación (el pedido ya está commiteado).
                await PublishWithRetryAsync(evento, pedido.id);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task PublishWithRetryAsync(PedidoCreadoEvent evento, Guid pedidoId)
        {
            const int maxIntentos = 3;
            for (var intento = 1; intento <= maxIntentos; intento++)
            {
                try
                {
                    await _eventPublisher.PublishAsync(evento);
                    _logger.LogInformation("SagaBridge: PedidoCreadoEvent publicado para pedido {PedidoId} (intento {Intento})", pedidoId, intento);
                    return;
                }
                catch (Exception ex) when (intento < maxIntentos)
                {
                    _logger.LogWarning(ex, "SagaBridge: fallo al publicar PedidoCreadoEvent pedido {PedidoId} intento {Intento}/{Max}; reintentando", pedidoId, intento, maxIntentos);
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * intento));
                }
            }

            // Último intento sin catch: si falla, la excepción sube y el operador usa /admin republish (criterio 14).
            try
            {
                await _eventPublisher.PublishAsync(evento);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SagaBridge: PedidoCreadoEvent NO publicado tras {Max} intentos pedido {PedidoId}; usar POST /admin republish", maxIntentos, pedidoId);
                throw;
            }
        }

        public async Task DeleteAsync(int id, VenVentaDeleteDto dto)
        {
            var venta = await _context.Set<VenVenta>()
                .FirstOrDefaultAsync(v => v.id == id);

            if (venta == null)
                throw new KeyNotFoundException("Venta no encontrada.");

            if (dto.RowVersion is { Length: > 0 })
                _context.Entry(venta).Property("RowVersion").OriginalValue = dto.RowVersion;

            _context.Set<VenVenta>().Remove(venta);
            await _dbResilience.SaveChangesAsync(_context);
        }

        public async Task<VenVentaDto> CreateAsync(VenVentaCreateDto dto)
        {
            var clienteExiste = await _context.CliCliente.AnyAsync(c => c.id == dto.idCliCliente);
            if (!clienteExiste)
            {
                throw new ArgumentException("El cliente especificado no existe.");
            }

            var usuarioExiste = await _context.SegUsuario.AnyAsync(u => u.id == dto.idSegUsuario);
            if (!usuarioExiste)
            {
                throw new ArgumentException("El usuario especificado no existe.");
            }

            var claveVenta = await GenerarClaveVentaUnicaAsync();

            var venta = new VenVenta
            {
                idCliCliente = dto.idCliCliente,
                idSegUsuario = dto.idSegUsuario,
                idVenCatEstado = 1,
                dteFechaHoraCompra = DateTime.UtcNow,
                strClaveVenta = claveVenta,
            };

            _context.Set<VenVenta>().Add(venta);
            await _dbResilience.SaveChangesAsync(_context);

            // PostDespliegue9 (criterio 4): dual-write transparente. Misma transacción lógica
            // (mismo DbContext): VenVenta legacy + VenPedido espejo en "Pendiente", SIN publicar evento.
            // El evento se publica solo en el finalize PUT 1->2.
            if (IsSagaBridgeEnabled())
            {
                var pedido = new VenPedido
                {
                    id = Guid.NewGuid(),
                    LegacyVentaId = venta.id,
                    idCliCliente = dto.idCliCliente,
                    dteFechaPedido = venta.dteFechaHoraCompra ?? DateTime.UtcNow,
                    decTotal = 0,
                    strEstadoSaga = "Pendiente",
                };
                _context.Set<VenPedido>().Add(pedido);
                await _dbResilience.SaveChangesAsync(_context);
                _logger.LogInformation("SagaBridge: pedido espejo {PedidoId} creado para venta {VentaId}", pedido.id, venta.id);
            }

            return (await GetByIdAsync(venta.id))!;
        }

        private async Task<string> GenerarClaveVentaUnicaAsync()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var maxAttempts = 10;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var data = new byte[10];
                RandomNumberGenerator.Fill(data);
                var clave = new string(data.Select(b => chars[b % chars.Length]).ToArray());
                var existe = await _context.Set<VenVenta>().AnyAsync(v => v.strClaveVenta == clave);
                if (!existe)
                {
                    return clave;
                }
            }

            throw new InvalidOperationException("No se pudo generar una clave de venta única después de varios intentos.");
        }
    }
}
