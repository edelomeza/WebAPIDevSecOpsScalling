using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface ILogin2faService
    {
        Task<Login2faResponse> Login2faAsync(Login2faRequest request, CancellationToken ct);
        Task<Login2faVerifyResponse> Verify2faAsync(Login2faVerifyRequest request, CancellationToken ct);
    }
}
