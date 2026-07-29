using Microsoft.EntityFrameworkCore;
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

        public VentasPedidoService(AppDbContext context, DbResilienceService dbResilience, IEventPublisher eventPublisher)
        {
            _context = context;
            _dbResilience = dbResilience;
            _eventPublisher = eventPublisher;
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