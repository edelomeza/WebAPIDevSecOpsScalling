using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace WebAPIDevSecOps.Services
{
    public class VentaDetalleService : IVentaDetalleService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly IUserAccessor _userAccessor;
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _productLocks = new();

        public VentaDetalleService(AppDbContext context, DbResilienceService dbResilience, IUserAccessor userAccessor)
        {
            _context = context;
            _dbResilience = dbResilience;
            _userAccessor = userAccessor;
        }

        private async Task AssertOwnershipAsync(VenVentaDetalle detalle)
        {
            await _context.Entry(detalle).Reference(d => d.VenVenta).LoadAsync();
            await _context.Entry(detalle.VenVenta!).Reference(v => v.SegUsuario).LoadAsync();
            var username = _userAccessor.GetCurrentUsername();
            if (!string.Equals(detalle.VenVenta!.SegUsuario?.strNombre, username, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("No tiene permiso para acceder a este detalle de venta.");
        }

        public async Task<PagedResult<VenVentaDetalleDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.Set<VenVentaDetalle>()
                .AsNoTracking()
                .Include(vd => vd.ProProducto)
                .Select(vd => new VenVentaDetalleDto
                {
                    id = vd.id,
                    idVenVenta = vd.idVenVenta,
                    idProProducto = vd.idProProducto,
                    strNombreProducto = vd.ProProducto != null ? vd.ProProducto.strNombreProducto : null,
                    decPrecio = vd.ProProducto != null ? vd.ProProducto.decPrecio : 0,
                    intPiezaVenta = vd.intPiezaVenta,
                    decTotalVenta = vd.decTotalVenta,
                    RowVersion = vd.RowVersion,
                });

            var totalCount = await query.CountAsync();
            query = query.ApplyPagination(p);
            var items = await query.ToListAsync();

            return new PagedResult<VenVentaDetalleDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize,
            };
        }

        public async Task<VenVentaDetalleDto?> GetByIdAsync(int id)
        {
            var detalle = await _context.Set<VenVentaDetalle>()
                .Include(vd => vd.ProProducto)
                .FirstOrDefaultAsync(vd => vd.id == id);

            if (detalle == null) return null;

            await AssertOwnershipAsync(detalle);

            return new VenVentaDetalleDto
            {
                id = detalle.id,
                idVenVenta = detalle.idVenVenta,
                idProProducto = detalle.idProProducto,
                strNombreProducto = detalle.ProProducto?.strNombreProducto,
                decPrecio = detalle.ProProducto?.decPrecio ?? 0,
                intPiezaVenta = detalle.intPiezaVenta,
                decTotalVenta = detalle.decTotalVenta,
                RowVersion = detalle.RowVersion,
            };
        }

        public async Task<IEnumerable<ProProductoAutocompleteDto>> AutocompleteProductoAsync(string texto, int maxResultados = 10)
        {
            return await _context.Set<ProProducto>()
                .AsNoTracking()
                .Where(p => p.strNombreProducto.ToLower().Contains(texto.ToLower()))
                .OrderBy(p => p.strNombreProducto)
                .Take(maxResultados)
                .Select(p => new ProProductoAutocompleteDto
                {
                    id = p.id,
                    strTextoAutocomplete = $"{p.strNombreProducto} | #: {p.intNumeroExistencia} | $: {p.decPrecio}"
                })
                .ToListAsync();
        }

        public async Task<VenVentaDetalleDto> CreateAsync(VenVentaDetalleCreateDto dto)
        {
            var venta = await _context.Set<VenVenta>().Include(v => v.SegUsuario).FirstOrDefaultAsync(v => v.id == dto.idVenVenta);
            if (venta == null)
            {
                throw new ArgumentException("La venta especificada no existe.");
            }

            var usuario = _userAccessor.GetCurrentUsername();
            if (!string.Equals(venta.SegUsuario?.strNombre, usuario, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("No tiene permiso para agregar detalles a esta venta.");

            var semaphore = _productLocks.GetOrAdd(dto.idProProducto, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                var producto = await _context.Set<ProProducto>()
                    .FirstOrDefaultAsync(p => p.id == dto.idProProducto);

                if (producto == null)
                {
                    throw new ArgumentException("El producto especificado no existe.");
                }

                if (dto.intPiezaVenta > producto.intNumeroExistencia)
                {
                    throw new ArgumentException("El producto no tiene las suficientes existencias.");
                }

                producto.intNumeroExistencia -= dto.intPiezaVenta;

                var detalle = new VenVentaDetalle
                {
                    idVenVenta = dto.idVenVenta,
                    idProProducto = dto.idProProducto,
                    intPiezaVenta = dto.intPiezaVenta,
                    decTotalVenta = dto.intPiezaVenta * producto.decPrecio,
                };

                _context.Set<VenVentaDetalle>().Add(detalle);
                await _dbResilience.SaveChangesAsync(_context);

                return (await GetByIdAsync(detalle.id))!;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task UpdateAsync(int id, VenVentaDetalleUpdateDto dto)
        {
            if (id != dto.id)
                throw new ArgumentException("El ID del detalle no coincide.");

            var detalle = await _context.Set<VenVentaDetalle>()
                .Include(vd => vd.ProProducto)
                .FirstOrDefaultAsync(vd => vd.id == id);

            if (detalle == null)
                throw new KeyNotFoundException("Detalle no encontrado.");

            await AssertOwnershipAsync(detalle);

            var ventaExiste = await _context.Set<VenVenta>().AnyAsync(v => v.id == dto.idVenVenta);
            if (!ventaExiste)
                throw new ArgumentException("La venta especificada no existe.");

            var productoNuevo = await _context.Set<ProProducto>()
                .Where(p => p.id == dto.idProProducto)
                .FirstOrDefaultAsync();

            if (productoNuevo == null)
                throw new ArgumentException("El producto especificado no existe.");

            if (dto.RowVersion is { Length: > 0 })
                _context.Entry(detalle).Property("RowVersion").OriginalValue = dto.RowVersion;

            var productoAnterior = detalle.ProProducto;

            if (productoAnterior.id == dto.idProProducto)
            {
                var diff = detalle.intPiezaVenta - dto.intPiezaVenta;
                if (diff < 0 && dto.intPiezaVenta > productoAnterior.intNumeroExistencia + detalle.intPiezaVenta)
                    throw new ArgumentException("El producto no tiene las suficientes existencias.");

                productoAnterior.intNumeroExistencia += diff;
                _context.Entry(productoAnterior).State = EntityState.Modified;
            }
            else
            {
                productoAnterior.intNumeroExistencia += detalle.intPiezaVenta;
                _context.Entry(productoAnterior).State = EntityState.Modified;

                if (dto.intPiezaVenta > productoNuevo.intNumeroExistencia)
                    throw new ArgumentException("El producto no tiene las suficientes existencias.");

                productoNuevo.intNumeroExistencia -= dto.intPiezaVenta;
                _context.Entry(productoNuevo).State = EntityState.Modified;
            }

            detalle.idVenVenta = dto.idVenVenta;
            detalle.idProProducto = dto.idProProducto;
            detalle.intPiezaVenta = dto.intPiezaVenta;
            detalle.decTotalVenta = dto.intPiezaVenta * productoNuevo.decPrecio;

            _context.Entry(detalle).State = EntityState.Modified;
            await _dbResilience.SaveChangesAsync(_context);
        }

        public async Task DeleteAsync(int id, VenVentaDetalleDeleteDto dto)
        {
            var detalle = await _context.Set<VenVentaDetalle>()
                .Include(vd => vd.ProProducto)
                .FirstOrDefaultAsync(vd => vd.id == id);

            if (detalle == null)
                throw new KeyNotFoundException("Detalle no encontrado.");

            await AssertOwnershipAsync(detalle);

            if (dto.RowVersion is { Length: > 0 })
                _context.Entry(detalle).Property("RowVersion").OriginalValue = dto.RowVersion;

            detalle.ProProducto.intNumeroExistencia += detalle.intPiezaVenta;
            _context.Entry(detalle.ProProducto).State = EntityState.Modified;

            _context.Set<VenVentaDetalle>().Remove(detalle);
            await _dbResilience.SaveChangesAsync(_context);
        }
    }
}
