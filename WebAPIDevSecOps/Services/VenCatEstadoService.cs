using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class VenCatEstadoService : IVenCatEstadoService
    {
        private readonly AppDbContext _context;
        private readonly ICacheService _cache;

        public VenCatEstadoService(AppDbContext context, ICacheService cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<PagedResult<VenCatEstadoDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var key = "cache:ven-cat-estado:list";

            return await _cache.GetOrCreateAsync(key, async () =>
            {
                var p = queryParams ?? new QueryParams();

                var query = _context.VenCatEstado
                    .AsNoTracking()
                    .Select(t => new VenCatEstadoDto
                    {
                        id = t.id,
                        strValor = t.strValor,
                        strDescripcion = t.strDescripcion
                    });

                var totalCount = await query.CountAsync();
                query = query.ApplyPagination(p);
                var items = await query.ToListAsync();

                return new PagedResult<VenCatEstadoDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = p.PageNumber,
                    PageSize = p.PageSize
                };
            }, TimeSpan.FromSeconds(120));
        }

        public async Task<VenCatEstadoDto?> GetByIdAsync(int id)
        {
            var key = $"cache:ven-cat-estado:{id}";
            var cached = await _cache.GetAsync<VenCatEstadoDto>(key);
            if (cached is not null) return cached;

            var dto = await _context.VenCatEstado
                .AsNoTracking()
                .Where(t => t.id == id)
                .Select(t => new VenCatEstadoDto
                {
                    id = t.id,
                    strValor = t.strValor,
                    strDescripcion = t.strDescripcion
                })
                .FirstOrDefaultAsync();

            if (dto is not null)
                await _cache.SetAsync(key, dto, TimeSpan.FromSeconds(120));

            return dto;
        }
    }
}
