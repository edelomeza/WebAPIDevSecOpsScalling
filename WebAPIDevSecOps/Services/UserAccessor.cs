using System.Security.Claims;
using WebAPIDevSecOps.Interfaces;

namespace WebAPIDevSecOps.Services
{
    public class UserAccessor : IUserAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UserAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string? GetCurrentUsername()
        {
            return _httpContextAccessor.HttpContext?.User?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        }

        public bool IsAdmin()
        {
            return _httpContextAccessor.HttpContext?.User?
                .IsInRole("Admin") ?? false;
        }

        public bool IsAuthenticated()
        {
            return _httpContextAccessor.HttpContext?.User?
                .Identity?.IsAuthenticated ?? false;
        }
    }
}
