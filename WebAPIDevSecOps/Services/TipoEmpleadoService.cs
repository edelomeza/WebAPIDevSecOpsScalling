using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class TipoEmpleadoService : ITipoEmpleadoService
    {
        private readonly AppDbContext _context;
        private readonly ICacheService _cache;

        public TipoEmpleadoService(AppDbContext context, ICacheService cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<PagedResult<EmpCatTipoEmpleadoDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var key = "cache:tipo-empleado:list";

            return await _cache.GetOrCreateAsync(key, async () =>
            {
                var p = queryParams ?? new QueryParams();

                var query = _context.EmpCatTipoEmpleado
                    .AsNoTracking()
                    .Select(t => new EmpCatTipoEmpleadoDto
                    {
                        id = t.id,
                        strValor = t.strValor,
                        strDescripcion = t.strDescripcion
                    });

                var totalCount = await query.CountAsync();
                query = query.ApplyPagination(p);
                var items = await query.ToListAsync();

                return new PagedResult<EmpCatTipoEmpleadoDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = p.PageNumber,
                    PageSize = p.PageSize
                };
            }, TimeSpan.FromSeconds(120));
        }

        public async Task<EmpCatTipoEmpleadoDto?> GetByIdAsync(int id)
        {
            var key = $"cache:tipo-empleado:{id}";
            var cached = await _cache.GetAsync<EmpCatTipoEmpleadoDto>(key);
            if (cached is not null) return cached;

            var tipo = await _context.EmpCatTipoEmpleado
                .AsNoTracking()
                .Where(t => t.id == id)
                .Select(t => new EmpCatTipoEmpleadoDto
                {
                    id = t.id,
                    strValor = t.strValor,
                    strDescripcion = t.strDescripcion
                })
                .FirstOrDefaultAsync();

            if (tipo is not null)
                await _cache.SetAsync(key, tipo, TimeSpan.FromSeconds(120));

            return tipo;
        }

    }
}
