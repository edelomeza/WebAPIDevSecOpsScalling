using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class ProductoService : IProductoService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly ICacheService _cache;

        public ProductoService(AppDbContext context, DbResilienceService dbResilience, ICacheService cache)
        {
            _context = context;
            _dbResilience = dbResilience;
            _cache = cache;
        }

        public async Task<PagedResult<ProProductoDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();
            var key = $"cache:productos:page{p.PageNumber}:size{p.PageSize}";

            return await _cache.GetOrCreateAsync(key, async () =>
            {
                var query = _context.ProProducto
                    .AsNoTracking()
                    .Select(x => new ProProductoDto
                    {
                        id = x.id,
                        strNombreProducto = x.strNombreProducto,
                        strURLImagen = x.strURLImagen,
                        strDescripcion = x.strDescripcion,
                        intNumeroExistencia = x.intNumeroExistencia,
                        decPrecio = x.decPrecio,
                        RowVersion = x.RowVersion,
                    });

                var totalCount = await query.CountAsync();
                query = query.ApplyPagination(p);
                var items = await query.ToListAsync();

                return new PagedResult<ProProductoDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = p.PageNumber,
                    PageSize = p.PageSize,
                };
            }, TimeSpan.FromSeconds(30));
        }

        public async Task<PagedResult<ProProductoDto>> SearchByNameAsync(string texto, QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.ProProducto
                .AsNoTracking()
                .Where(x => x.strNombreProducto.ToLower().Contains(texto.ToLower()))
                .Select(x => new ProProductoDto
                {
                    id = x.id,
                    strNombreProducto = x.strNombreProducto,
                    strURLImagen = x.strURLImagen,
                    strDescripcion = x.strDescripcion,
                    intNumeroExistencia = x.intNumeroExistencia,
                    decPrecio = x.decPrecio,
                    RowVersion = x.RowVersion,
                });

            var totalCount = await query.CountAsync();
            query = query.ApplyPagination(p);
            var items = await query.ToListAsync();

            return new PagedResult<ProProductoDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize,
            };
        }

        public async Task<ProProductoDto?> GetByIdAsync(int id)
        {
            var key = $"cache:producto:{id}";
            var cached = await _cache.GetAsync<ProProductoDto>(key);
            if (cached is not null) return cached;

            var producto = await _context.ProProducto
                .AsNoTracking()
                .Where(x => x.id == id)
                .Select(x => new ProProductoDto
                {
                    id = x.id,
                    strNombreProducto = x.strNombreProducto,
                    strURLImagen = x.strURLImagen,
                    strDescripcion = x.strDescripcion,
                    intNumeroExistencia = x.intNumeroExistencia,
                    decPrecio = x.decPrecio,
                    RowVersion = x.RowVersion,
                })
                .FirstOrDefaultAsync();

            if (producto is not null)
                await _cache.SetAsync(key, producto, TimeSpan.FromSeconds(60));

            return producto;
        }

        public async Task<ProProductoDto> CreateAsync(ProductoCreateDto dto)
        {
            var producto = new ProProducto
            {
                strNombreProducto = dto.strNombreProducto.Trim(),
                strURLImagen = dto.strURLImagen?.Trim(),
                strDescripcion = dto.strDescripcion?.Trim(),
                intNumeroExistencia = dto.intNumeroExistencia,
                decPrecio = dto.decPrecio,
            };

            _context.ProProducto.Add(producto);
            await _dbResilience.SaveChangesAsync(_context);

            return new ProProductoDto
            {
                id = producto.id,
                strNombreProducto = producto.strNombreProducto,
                strURLImagen = producto.strURLImagen,
                strDescripcion = producto.strDescripcion,
                intNumeroExistencia = producto.intNumeroExistencia,
                decPrecio = producto.decPrecio,
                RowVersion = producto.RowVersion,
            };
        }

        public async Task UpdateAsync(int id, ProductoUpdateDto dto)
        {
            if (id != dto.id)
            {
                throw new ArgumentException("El ID del producto no coincide.");
            }

            var producto = await _context.ProProducto
                .FirstOrDefaultAsync(x => x.id == id);

            if (producto == null)
            {
                throw new KeyNotFoundException("Producto no encontrado.");
            }

            if (dto.RowVersion is { Length: > 0 })
            {
                _context.Entry(producto).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            producto.strNombreProducto = dto.strNombreProducto.Trim();
            producto.strURLImagen = dto.strURLImagen?.Trim();
            producto.strDescripcion = dto.strDescripcion?.Trim();
            producto.intNumeroExistencia = dto.intNumeroExistencia;
            producto.decPrecio = dto.decPrecio;

            _context.Entry(producto).State = EntityState.Modified;
            await _dbResilience.SaveChangesAsync(_context);

            await InvalidateProductCacheAsync(id);
        }

        public async Task DeleteAsync(int id, ProductoDeleteDto dto)
        {
            var producto = await _context.ProProducto
                .FirstOrDefaultAsync(x => x.id == id);

            if (producto == null)
            {
                throw new KeyNotFoundException("Producto no encontrado.");
            }

            if (dto.RowVersion is { Length: > 0 })
            {
                _context.Entry(producto).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            _context.ProProducto.Remove(producto);

            await _dbResilience.SaveChangesAsync(_context);

            await InvalidateProductCacheAsync(id);
        }

        private async Task InvalidateProductCacheAsync(int id)
        {
            await _cache.RemoveAsync($"cache:producto:{id}");
        }
    }
}
