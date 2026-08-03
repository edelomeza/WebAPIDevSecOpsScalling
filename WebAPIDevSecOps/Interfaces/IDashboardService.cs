using WebAPIDevSecOps.Dto;

namespace WebAPIDevSecOps.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardDto> GetDashboardAsync();
        Task<SagaTimelineDto?> GetTimelineAsync(Guid id);
    }
}
