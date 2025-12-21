using Hangfire;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Interfaces
{
    public interface IJobRecoveryStrategy
    {
        JobType SupportedJobType { get; }
        IReadOnlyList<JobStatus> MonitoredStatuses { get; }
        string? RecoverJob(IRecoverableJob job, IBackgroundJobClient backgroundJobClient);
    }
}
