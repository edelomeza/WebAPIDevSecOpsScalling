using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Events;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class VentasPedidoService : IVentasPedidoService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<VentasPedidoService> _logger;

        public VentasPedidoService(
            AppDbContext context,
            DbResilienceService dbResilience,
            IEventPublisher eventPublisher,
            ILogger<VentasPedidoService> logger)
        {
            _context = context;
            _dbResilience = dbResilience;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<PedidoResponseDto> CrearPedidoAsync(PedidoCreateDto dto)
        {
            var cliente = await _context.CliCliente.AsNoTracking().FirstOrDefaultAsync(c => c.id == dto.idCliCliente);
            if (cliente == null)
                throw new ArgumentException("El cliente especificado no existe.");

            var detalles = new List<(int IdProducto, int Cantidad, decimal PrecioUnitario)>();
            decimal total = 0;

            foreach (var detalleDto in dto.Detalles)
            {
                var producto = await _context.ProProducto.AsNoTracking().FirstOrDefaultAsync(p => p.id == detalleDto.idProProducto);
                if (producto == null)
                    throw new ArgumentException($"El producto con ID {detalleDto.idProProducto} no existe.");

                var precio = producto.decPrecio;
                detalles.Add((detalleDto.idProProducto, detalleDto.intCantidad, precio));
                total += detalleDto.intCantidad * precio;
            }

            var pedidoId = Guid.NewGuid();
            var pedido = new VenPedido
            {
                id = pedidoId,
                idCliCliente = dto.idCliCliente,
                dteFechaPedido = DateTime.UtcNow,
                decTotal = total,
                strEstadoSaga = "Pendiente",
            };

            _context.Set<VenPedido>().Add(pedido);

            foreach (var (idProducto, cantidad, precio) in detalles)
            {
                var detalle = new VenPedidoDetalle
                {
                    idVenPedido = pedidoId,
                    idProProducto = idProducto,
                    intCantidad = cantidad,
                    decPrecioUnitario = precio,
                };
                _context.Set<VenPedidoDetalle>().Add(detalle);
            }

            await _dbResilience.SaveChangesAsync(_context);

            var evento = new PedidoCreadoEvent
            {
                PedidoId = pedidoId,
                ClienteId = dto.idCliCliente,
                Total = total,
                Detalles = dto.Detalles.Select(d => new PedidoCreadoDetalleItem
                {
                    idProProducto = d.idProProducto,
                    intCantidad = d.intCantidad,
                    decPrecioUnitario = detalles.First(x => x.IdProducto == d.idProProducto).PrecioUnitario,
                }).ToList(),
                FechaCreacion = DateTime.UtcNow,
            };

            await _eventPublisher.PublishAsync(evento);

            return await GetByIdAsync(pedidoId) ?? throw new InvalidOperationException("Error al crear el pedido.");
        }

        public async Task<PedidoResponseDto> RepublicarPendienteAsync(Guid id)
        {
            var pedido = await _context.Set<VenPedido>()
                .Include(p => p.Detalles)
                .FirstOrDefaultAsync(p => p.id == id)
                ?? throw new KeyNotFoundException("Pedido no encontrado.");

            if (!string.Equals(pedido.strEstadoSaga, "Pendiente", StringComparison.Ordinal))
                throw new InvalidOperationException($"El pedido no está Pendiente (estado actual: {pedido.strEstadoSaga}).");

            if (pedido.Detalles.Count == 0)
                throw new InvalidOperationException("El pedido no tiene detalles; no se puede republicar.");

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

            Exception? ultimoError = null;
            for (var intento = 1; intento <= 3; intento++)
            {
                try
                {
                    await _eventPublisher.PublishAsync(evento);
                    _logger.LogInformation("Republish pedido {PedidoId} OK (intento {Intento})", pedido.id, intento);
                    ultimoError = null;
                    break;
                }
                catch (Exception ex)
                {
                    ultimoError = ex;
                    _logger.LogWarning(ex, "Republish pedido {PedidoId} fallo intento {Intento}/3", pedido.id, intento);
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * intento));
                }
            }

            if (ultimoError != null)
            {
                _logger.LogError(ultimoError, "Republish pedido {PedidoId} NO publicado tras 3 intentos", pedido.id);
                throw ultimoError;
            }

            return await GetByIdAsync(pedido.id) ?? throw new InvalidOperationException("Error al republicar el pedido.");
        }

        public async Task<IReadOnlyList<PedidoResponseDto>> GetPendientesAsync(int max = 50)
        {
            if (max < 1 || max > 200)
                max = 50;

            var ids = await _context.Set<VenPedido>()
                .AsNoTracking()
                .Where(p => p.strEstadoSaga == "Pendiente")
                .OrderBy(p => p.dteFechaPedido)
                .Take(max)
                .Select(p => p.id)
                .ToListAsync();

            var result = new List<PedidoResponseDto>(ids.Count);
            foreach (var id in ids)
            {
                var dto = await GetByIdAsync(id);
                if (dto != null)
                    result.Add(dto);
            }

            return result;
        }

        public async Task<PedidoResponseDto?> GetByIdAsync(Guid id)
        {
            return await _context.Set<VenPedido>()
                .AsNoTracking()
                .Include(p => p.CliCliente)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.ProProducto)
                .Where(p => p.id == id)
                .Select(p => new PedidoResponseDto
                {
                    id = p.id,
                    idCliCliente = p.idCliCliente,
                    strNombreCliente = p.CliCliente != null ? p.CliCliente.strNombreCliente : null,
                    dteFechaPedido = p.dteFechaPedido,
                    decTotal = p.decTotal,
                    strEstadoSaga = p.strEstadoSaga,
                    strMotivoRechazo = p.strMotivoRechazo,
                    RowVersion = p.RowVersion,
                    Detalles = p.Detalles.Select(d => new PedidoDetalleResponseDto
                    {
                        id = d.id,
                        idProProducto = d.idProProducto,
                        strNombreProducto = d.ProProducto != null ? d.ProProducto.strNombreProducto : null,
                        intCantidad = d.intCantidad,
                        decPrecioUnitario = d.decPrecioUnitario,
                    }).ToList(),
                })
                .FirstOrDefaultAsync();
        }

        public async Task<PagedResult<PedidoResponseDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.Set<VenPedido>()
                .AsNoTracking()
                .Include(p => p.CliCliente)
                .Include(p => p.Detalles)
                    .ThenInclude(d => d.ProProducto)
                .AsQueryable();

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(p => p.dteFechaPedido)
                .ApplyPagination(p)
                .Select(p => new PedidoResponseDto
                {
                    id = p.id,
                    idCliCliente = p.idCliCliente,
                    strNombreCliente = p.CliCliente != null ? p.CliCliente.strNombreCliente : null,
                    dteFechaPedido = p.dteFechaPedido,
                    decTotal = p.decTotal,
                    strEstadoSaga = p.strEstadoSaga,
                    strMotivoRechazo = p.strMotivoRechazo,
                    RowVersion = p.RowVersion,
                    Detalles = p.Detalles.Select(d => new PedidoDetalleResponseDto
                    {
                        id = d.id,
                        idProProducto = d.idProProducto,
                        strNombreProducto = d.ProProducto != null ? d.ProProducto.strNombreProducto : null,
                        intCantidad = d.intCantidad,
                        decPrecioUnitario = d.decPrecioUnitario,
                    }).ToList(),
                })
                .ToListAsync();

            return new PagedResult<PedidoResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize,
            };
        }
    }
}