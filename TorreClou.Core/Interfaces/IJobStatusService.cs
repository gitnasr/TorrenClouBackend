using TorreClou.Core.DTOs.Common;
using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Interfaces
{

    public interface IJobStatusService
    {
        Task TransitionJobStatusAsync(
            UserJob job,
            JobStatus newStatus,
            StatusChangeSource source,
            string? errorMessage = null,
            object? metadata = null);


        /// <summary>
        /// Records the initial status for a newly created job.
        /// </summary>
        /// <param name="job">The newly created job entity.</param>
        /// <param name="metadata">Optional metadata object to serialize as JSON.</param>
        Task RecordInitialJobStatusAsync(UserJob job, object? metadata = null);


        /// <summary>
        /// Gets the status timeline for a job.
        /// </summary>
        /// <param name="jobId">The job ID.</param>
        /// <returns>List of timeline entries ordered by change time.</returns>
        Task<IReadOnlyList<JobTimelineEntryDto>> GetJobTimelineAsync(int jobId);

        /// <summary>
        /// Gets the paginated status timeline for a job.
        /// </summary>
        Task<PaginatedResult<JobTimelineEntryDto>> GetJobTimelinePaginatedAsync(int jobId, int pageNumber, int pageSize);

    }
}

