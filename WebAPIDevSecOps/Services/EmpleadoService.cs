using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class EmpleadoService : IEmpleadoService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly ICacheService _cache;

        public EmpleadoService(AppDbContext context, DbResilienceService dbResilience, ICacheService cache)
        {
            _context = context;
            _dbResilience = dbResilience;
            _cache = cache;
        }

        public async Task<PagedResult<EmpEmpleadoDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.EmpEmpleado
                .AsNoTracking()
                .Select(e => new EmpEmpleadoDto
                {
                    id = e.id,
                    strNombre = e.strNombre,
                    strAPaterno = e.strAPaterno,
                    strAMaterno = e.strAMaterno,
                    strCURP = e.strCURP,
                    idEmpCatTipoEmpleado = e.idEmpCatTipoEmpleado,
                    RowVersion = e.RowVersion
                });

            var totalCount = await query.CountAsync();
            var items = await query.ApplyPagination(p).ToListAsync();

            return new PagedResult<EmpEmpleadoDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize
            };
        }

        public async Task<EmpEmpleadoDto?> GetByIdAsync(int id)
        {
            var key = $"cache:empleado:{id}";
            var cached = await _cache.GetAsync<EmpEmpleadoDto>(key);
            if (cached is not null) return cached;

            var empleado = await _context.EmpEmpleado
                .AsNoTracking()
                .Where(e => e.id == id)
                .Select(e => new EmpEmpleadoDto
                {
                    id = e.id,
                    strNombre = e.strNombre,
                    strAPaterno = e.strAPaterno,
                    strAMaterno = e.strAMaterno,
                    strCURP = e.strCURP,
                    idEmpCatTipoEmpleado = e.idEmpCatTipoEmpleado,
                    RowVersion = e.RowVersion
                })
                .FirstOrDefaultAsync();

            if (empleado is not null)
                await _cache.SetAsync(key, empleado, TimeSpan.FromSeconds(60));

            return empleado;
        }

        public async Task<PagedResult<EmpEmpleadoDto>> SearchAsync(string? texto, int? idTipoEmpleado, QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.EmpEmpleado
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(e =>
                    e.strNombre.Contains(texto) ||
                    (e.strAPaterno != null && e.strAPaterno.Contains(texto)) ||
                    (e.strAMaterno != null && e.strAMaterno.Contains(texto)));
            }

            if (idTipoEmpleado.HasValue)
            {
                query = query.Where(e => e.idEmpCatTipoEmpleado == idTipoEmpleado.Value);
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .Select(e => new EmpEmpleadoDto
                {
                    id = e.id,
                    strNombre = e.strNombre,
                    strAPaterno = e.strAPaterno,
                    strAMaterno = e.strAMaterno,
                    strCURP = e.strCURP,
                    idEmpCatTipoEmpleado = e.idEmpCatTipoEmpleado,
                    RowVersion = e.RowVersion
                })
                .ApplyPagination(p)
                .ToListAsync();

            return new PagedResult<EmpEmpleadoDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize
            };
        }

        public async Task<EmpEmpleadoDto> CreateAsync(EmpEmpleadoCreateDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.strCURP))
            {
                bool curpExiste = await _context.EmpEmpleado.AnyAsync(e => e.strCURP == dto.strCURP);
                if (curpExiste)
                {
                    throw new ArgumentException("El CURP ya existe.");
                }
            }

            if (dto.idEmpCatTipoEmpleado.HasValue)
            {
                var tipoExiste = await _context.EmpCatTipoEmpleado
                    .AnyAsync(t => t.id == dto.idEmpCatTipoEmpleado.Value);
                if (!tipoExiste)
                {
                    throw new ArgumentException("El tipo de empleado especificado no existe.");
                }
            }

            var empleado = new EmpEmpleado
            {
                strNombre = dto.strNombre.Trim(),
                strAPaterno = dto.strAPaterno?.Trim(),
                strAMaterno = dto.strAMaterno?.Trim(),
                strCURP = dto.strCURP?.Trim(),
                idEmpCatTipoEmpleado = dto.idEmpCatTipoEmpleado
            };

            _context.EmpEmpleado.Add(empleado);
            await _dbResilience.SaveChangesAsync(_context);

            return new EmpEmpleadoDto
            {
                id = empleado.id,
                strNombre = empleado.strNombre,
                strAPaterno = empleado.strAPaterno,
                strAMaterno = empleado.strAMaterno,
                strCURP = empleado.strCURP,
                idEmpCatTipoEmpleado = empleado.idEmpCatTipoEmpleado,
                RowVersion = empleado.RowVersion
            };
        }

        public async Task UpdateAsync(int id, EmpEmpleadoUpdateDto dto)
        {
            if (id != dto.id)
            {
                throw new ArgumentException("El ID del empleado no coincide.");
            }

            if (!string.IsNullOrWhiteSpace(dto.strCURP))
            {
                bool curpEnUso = await _context.EmpEmpleado.AnyAsync(e => e.strCURP == dto.strCURP && e.id != id);
                if (curpEnUso)
                {
                    throw new ArgumentException("El CURP ya está en uso por otro registro.");
                }
            }

            if (dto.idEmpCatTipoEmpleado.HasValue)
            {
                var tipoExiste = await _context.EmpCatTipoEmpleado
                    .AnyAsync(t => t.id == dto.idEmpCatTipoEmpleado.Value);
                if (!tipoExiste)
                {
                    throw new ArgumentException("El tipo de empleado especificado no existe.");
                }
            }

            var empleado = await _context.EmpEmpleado.FirstOrDefaultAsync(e => e.id == id);
            if (empleado == null)
            {
                throw new KeyNotFoundException("Empleado no encontrado.");
            }

            if (dto.RowVersion is { Length: > 0 })
            {
                _context.Entry(empleado).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            empleado.strNombre = dto.strNombre.Trim();
            empleado.strAPaterno = dto.strAPaterno?.Trim();
            empleado.strAMaterno = dto.strAMaterno?.Trim();
            empleado.strCURP = dto.strCURP?.Trim();
            empleado.idEmpCatTipoEmpleado = dto.idEmpCatTipoEmpleado;

            _context.Entry(empleado).State = EntityState.Modified;
            await _dbResilience.SaveChangesAsync(_context);

            await _cache.RemoveAsync($"cache:empleado:{id}");
        }

        public async Task DeleteAsync(int id, EmpEmpleadoDeleteDto dto)
        {
            if (id != dto.id)
            {
                throw new ArgumentException("El ID del empleado no coincide.");
            }

            var empleado = await _context.EmpEmpleado.FirstOrDefaultAsync(e => e.id == id);
            if (empleado == null)
            {
                throw new KeyNotFoundException("Empleado no encontrado.");
            }

            if (dto.RowVersion is { Length: > 0 })
            {
                _context.Entry(empleado).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            _context.EmpEmpleado.Remove(empleado);
            await _dbResilience.SaveChangesAsync(_context);

            await _cache.RemoveAsync($"cache:empleado:{id}");
        }
    }
}
