using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;
using WebAPIDevSecOps.Context;


namespace WebAPIDevSecOps.Services
{
    public class UsuarioService: IUsuarioService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly DbResilienceService _dbResilience;
        private readonly ICacheService _cache;

        public UsuarioService(AppDbContext context, IPasswordHasherService passwordHasher, DbResilienceService dbResilience, ICacheService cache)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _dbResilience = dbResilience;
            _cache = cache;
        }

        public async Task<PagedResult<SegUsuarioDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.SegUsuario
                .AsNoTracking()
                .Select(u => new SegUsuarioDto
                {
                    id = u.id,
                    strNombre = u.strNombre,
                    strCorreoElectronico = u.strCorreoElectronico,
                    RowVersion = u.RowVersion
                });

            var totalCount = await query.CountAsync();

            query = query.ApplyPagination(p);

            var items = await query.ToListAsync();

            return new PagedResult<SegUsuarioDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize
            };
        }

        public async Task<PagedResult<SegUsuarioDto>> SearchByNameAsync(string texto, QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = _context.SegUsuario
                .AsNoTracking()
                .Where(u => u.strNombre.ToLower().Contains(texto.ToLower()))
                .Select(u => new SegUsuarioDto
                {
                    id = u.id,
                    strNombre = u.strNombre,
                    strCorreoElectronico = u.strCorreoElectronico,
                    RowVersion = u.RowVersion
                });

            var totalCount = await query.CountAsync();

            query = query.ApplyPagination(p);

            var items = await query.ToListAsync();

            return new PagedResult<SegUsuarioDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize
            };
        }

        public async Task<IEnumerable<SegUsuarioAutocompleteDto>> AutocompleteAsync(string texto, int maxResultados = 10)
        {
            return await _context.SegUsuario
                .AsNoTracking()
                .Where(u => u.strNombre.ToLower().Contains(texto.ToLower()))
                .OrderBy(u => u.strNombre)
                .Take(maxResultados)
                .Select(u => new SegUsuarioAutocompleteDto
                {
                    id = u.id,
                    strNombre = u.strNombre
                })
                .ToListAsync();
        }

        public async Task<SegUsuarioDto?> GetByIdAsync(int id)
        {
            var key = $"cache:usuario:{id}";
            var cached = await _cache.GetAsync<SegUsuarioDto>(key);
            if (cached is not null) return cached;

            var usuario = await _context.SegUsuario
                .AsNoTracking()
                .Where(u => u.id == id)
                .Select(u => new SegUsuarioDto
                {
                    id = u.id,
                    strNombre = u.strNombre,
                    strCorreoElectronico = u.strCorreoElectronico,
                    RowVersion = u.RowVersion
                })
                .FirstOrDefaultAsync();

            if (usuario is not null)
                await _cache.SetAsync(key, usuario, TimeSpan.FromSeconds(60));

            return usuario;
        }

        public async Task<SegUsuarioDto> CreateAsync(UsuarioCreateDto dto)
        {
            bool usuarioExiste = await _context.SegUsuario.AnyAsync(u => u.strNombre == dto.strNombre);
            if (usuarioExiste)
            {
                throw new ArgumentException("El nombre de usuario ya existe.");
            }

            var segUsuario = new SegUsuario
            {
                strNombre = dto.strNombre.Trim(),
                strCorreoElectronico = dto.strCorreoElectronico.Trim(),
                strPWD = _passwordHasher.HashPassword(dto.strPWD.Trim()),
                dteFechaRegistro = DateTime.Now
            };

            _context.SegUsuario.Add(segUsuario);
            await _dbResilience.SaveChangesAsync(_context);

            return new SegUsuarioDto
            {
                id = segUsuario.id,
                strNombre = segUsuario.strNombre,
                strCorreoElectronico = segUsuario.strCorreoElectronico,
                //dteFechaRegistro = segUsuario.dteFechaRegistro,
                //RowVersion = segUsuario.RowVersion
            };
        }

        public async Task UpdateAsync(int id, UsuarioUpdateDto dto)
        {
            if (id != dto.id)
            {
                throw new ArgumentException("El ID del usuario no coincide.");
            }

            var datos = await _context.SegUsuario
                .Where(u => u.id == id || u.strNombre == dto.strNombre)
                .ToListAsync();

            var segUsuario = datos.FirstOrDefault(u => u.id == id);
            bool nombreEnUso = datos.Any(u => u.strNombre == dto.strNombre && u.id != id);

            if (nombreEnUso)
            {
                throw new ArgumentException("El nombre de usuario ya está en uso por otro registro.");
            }

            if (segUsuario == null)
            {
                throw new KeyNotFoundException("Usuario no encontrado.");
            }
            
           

            if (dto.RowVersion is { Length: > 0 })
            {
                // El arreglo NO está vacío y NO es nulo
                _context.Entry(segUsuario).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            segUsuario.strNombre = dto.strNombre.Trim();
            segUsuario.strCorreoElectronico = dto.strCorreoElectronico.Trim();

            if (!string.IsNullOrWhiteSpace(dto.strPWD))
            {
                segUsuario.strPWD = _passwordHasher.HashPassword(dto.strPWD.Trim());
            }

            _context.Entry(segUsuario).State = EntityState.Modified;
            await _dbResilience.SaveChangesAsync(_context);

            await _cache.RemoveAsync($"cache:usuario:{id}");
        }

        public async Task DeleteAsync(int id, UsuarioDeleteDto dto)
        {
            var segUsuario = await _context.SegUsuario
                .FirstOrDefaultAsync(u => u.id == id);

            if (segUsuario == null)
            {
                throw new KeyNotFoundException("Usuario no encontrado.");
            }

            if (dto.RowVersion is { Length: > 0 })
            {
                _context.Entry(segUsuario).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            _context.SegUsuario.Remove(segUsuario);

            await _dbResilience.SaveChangesAsync(_context);

            await _cache.RemoveAsync($"cache:usuario:{id}");
        }
    }
}
