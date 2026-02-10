using TorreClou.Core.DTOs.Common;

namespace TorreClou.Core.Interfaces
{
    public interface IHealthCheckService
    {
        Task<HealthStatus> GetCachedHealthStatusAsync();
        Task<DetailedHealthStatus> GetDetailedHealthStatusAsync(CancellationToken ct = default);
    }
}
