using Microsoft.EntityFrameworkCore;
using WebAPIDevSecOps.Context;
using WebAPIDevSecOps.Dto;
using WebAPIDevSecOps.Interfaces;
using WebAPIDevSecOps.Models;

namespace WebAPIDevSecOps.Services
{
    public class ClienteService : IClienteService
    {
        private readonly AppDbContext _context;
        private readonly DbResilienceService _dbResilience;
        private readonly ICacheService _cache;
        private readonly IUserAccessor _userAccessor;

        public ClienteService(AppDbContext context, DbResilienceService dbResilience, ICacheService cache, IUserAccessor userAccessor)
        {
            _context = context;
            _dbResilience = dbResilience;
            _cache = cache;
            _userAccessor = userAccessor;
        }

        private IQueryable<CliCliente> ApplyOwnershipFilter(IQueryable<CliCliente> query)
        {
            if (_userAccessor.IsAdmin())
                return query;
            var username = _userAccessor.GetCurrentUsername();
            if (username == null)
                return query.Where(c => false);
            return query.Where(c => c.strCreadoPorUsuario == username);
        }

        private void AssertOwnership(CliCliente cliente)
        {
            if (_userAccessor.IsAdmin())
                return;
            var username = _userAccessor.GetCurrentUsername();
            if (cliente.strCreadoPorUsuario != username)
                throw new UnauthorizedAccessException("No tiene permisos para acceder a este recurso.");
        }

        public async Task<PagedResult<CliClienteDto>> GetAllAsync(QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();
            var key = $"cache:clientes:page{p.PageNumber}:size{p.PageSize}";

            return await _cache.GetOrCreateAsync(key, async () =>
            {
                var query = ApplyOwnershipFilter(_context.CliCliente.AsNoTracking())
                    .Select(c => new CliClienteDto
                    {
                        id = c.id,
                        strNombreCliente = c.strNombreCliente,
                        strDireccionCliente = c.strDireccionCliente,
                        strCorreoElectronico = c.strCorreoElectronico,
                        strNumeroTelefono = c.strNumeroTelefono,
                        RowVersion = c.RowVersion,
                    });

                var totalCount = await query.CountAsync();
                query = query.ApplyPagination(p);
                var items = await query.ToListAsync();

                return new PagedResult<CliClienteDto>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = p.PageNumber,
                    PageSize = p.PageSize,
                };
            }, TimeSpan.FromSeconds(30));
        }

        public async Task<PagedResult<CliClienteDto>> SearchByNameAsync(string texto, QueryParams? queryParams = null)
        {
            var p = queryParams ?? new QueryParams();

            var query = ApplyOwnershipFilter(_context.CliCliente.AsNoTracking())
                .Where(c => c.strNombreCliente.ToLower().Contains(texto.ToLower()))
                .Select(c => new CliClienteDto
                {
                    id = c.id,
                    strNombreCliente = c.strNombreCliente,
                    strDireccionCliente = c.strDireccionCliente,
                    strCorreoElectronico = c.strCorreoElectronico,
                    strNumeroTelefono = c.strNumeroTelefono,
                    RowVersion = c.RowVersion,
                });

            var totalCount = await query.CountAsync();
            query = query.ApplyPagination(p);
            var items = await query.ToListAsync();

            return new PagedResult<CliClienteDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = p.PageNumber,
                PageSize = p.PageSize,
            };
        }

        public async Task<IEnumerable<CliClienteAutocompleteDto>> AutocompleteAsync(string texto, int maxResultados = 10)
        {
            return await ApplyOwnershipFilter(_context.CliCliente.AsNoTracking())
                .Where(c => c.strNombreCliente.ToLower().Contains(texto.ToLower()))
                .OrderBy(c => c.strNombreCliente)
                .Take(maxResultados)
                .Select(c => new CliClienteAutocompleteDto
                {
                    id = c.id,
                    strNombreCliente = c.strNombreCliente
                })
                .ToListAsync();
        }

        public async Task<CliClienteDto?> GetByIdAsync(int id)
        {
            var key = $"cache:cliente:{id}";
            var cached = await _cache.GetAsync<CliClienteDto>(key);
            if (cached is not null) return cached;

            var cliente = await _context.CliCliente
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.id == id);

            if (cliente == null) return null;

            AssertOwnership(cliente);

            var dto = new CliClienteDto
            {
                id = cliente.id,
                strNombreCliente = cliente.strNombreCliente,
                strDireccionCliente = cliente.strDireccionCliente,
                strCorreoElectronico = cliente.strCorreoElectronico,
                strNumeroTelefono = cliente.strNumeroTelefono,
                RowVersion = cliente.RowVersion,
            };

            await _cache.SetAsync(key, dto, TimeSpan.FromSeconds(60));

            return dto;
        }

        public async Task<CliClienteDto> CreateAsync(CliClienteCreateDto dto)
        {
            bool correoExiste = await _context.CliCliente.AnyAsync(c => c.strCorreoElectronico == dto.strCorreoElectronico);
            if (correoExiste)
            {
                throw new ArgumentException("El correo electrónico ya está registrado.");
            }

            var cliente = new CliCliente
            {
                strNombreCliente = dto.strNombreCliente.Trim(),
                strDireccionCliente = dto.strDireccionCliente?.Trim(),
                strCorreoElectronico = dto.strCorreoElectronico.Trim(),
                strNumeroTelefono = dto.strNumeroTelefono.Trim(),
                strCreadoPorUsuario = _userAccessor.GetCurrentUsername(),
            };

            _context.CliCliente.Add(cliente);
            await _dbResilience.SaveChangesAsync(_context);

            return new CliClienteDto
            {
                id = cliente.id,
                strNombreCliente = cliente.strNombreCliente,
                strDireccionCliente = cliente.strDireccionCliente,
                strCorreoElectronico = cliente.strCorreoElectronico,
                strNumeroTelefono = cliente.strNumeroTelefono,
                RowVersion = cliente.RowVersion,
            };
        }

        public async Task UpdateAsync(int id, CliClienteUpdateDto dto)
        {
            if (id != dto.id)
            {
                throw new ArgumentException("El ID del cliente no coincide.");
            }

            var clientes = await _context.CliCliente
                .Where(c => c.id == id || c.strCorreoElectronico == dto.strCorreoElectronico)
                .ToListAsync();

            var cliente = clientes.FirstOrDefault(c => c.id == id);

            if (cliente == null)
            {
                throw new KeyNotFoundException("Cliente no encontrado.");
            }

            AssertOwnership(cliente);

            bool correoEnUso = clientes.Any(c => c.strCorreoElectronico == dto.strCorreoElectronico && c.id != id);
            if (correoEnUso)
            {
                throw new ArgumentException("El correo electrónico ya está en uso por otro registro.");
            }

            if (dto.RowVersion is { Length: > 0 })
            {
                _context.Entry(cliente).Property("RowVersion").OriginalValue = dto.RowVersion;
            }

            cliente.strNombreCliente = dto.strNombreCliente.Trim();
            cliente.strDireccionCliente = dto.strDireccionCliente?.Trim();
            cliente.strCorreoElectronico = dto.strCorreoElectronico.Trim();
            cliente.strNumeroTelefono = dto.strNumeroTelefono.Trim();

            _context.Entry(cliente).State = EntityState.Modified;
            await _dbResilience.SaveChangesAsync(_context);

            await _cache.RemoveAsync($"cache:cliente:{id}");
        }

        public async Task DeleteAsync(int id, CliClienteDeleteDto dto)
        {
            var cliente = await _context.CliCliente
                .FirstOrDefaultAsync(c => c.id == id);

            if (cliente == null)
            {
                throw new KeyNotFoundException("Cliente no encontrado.");
            }

            AssertOwnership(cliente);

            _context.Entry(cliente).Property("RowVersion").OriginalValue = dto.RowVersion;
            _context.CliCliente.Remove(cliente);

            await _dbResilience.SaveChangesAsync(_context);

            await _cache.RemoveAsync($"cache:cliente:{id}");
        }
    }
}
