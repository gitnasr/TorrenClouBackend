using TorreClou.Core.DTOs.Jobs;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Service for managing job and sync status transitions with full audit trail.
    /// </summary>
    public interface IJobStatusService
    {
        /// <summary>
        /// Transitions a UserJob to a new status and records the change in history.
        /// </summary>
        /// <param name="job">The job entity to transition (must be tracked by the DbContext).</param>
        /// <param name="newStatus">The new status to transition to.</param>
        /// <param name="source">The source triggering this status change.</param>
        /// <param name="errorMessage">Optional error message if this is a failure transition.</param>
        /// <param name="metadata">Optional metadata object to serialize as JSON.</param>
        Task TransitionJobStatusAsync(
            UserJob job,
            JobStatus newStatus,
            StatusChangeSource source,
            string? errorMessage = null,
            object? metadata = null);

        /// <summary>
        /// Transitions a Sync entity to a new status and records the change in history.
        /// </summary>
        /// <param name="sync">The sync entity to transition (must be tracked by the DbContext).</param>
        /// <param name="newStatus">The new status to transition to.</param>
        /// <param name="source">The source triggering this status change.</param>
        /// <param name="errorMessage">Optional error message if this is a failure transition.</param>
        /// <param name="metadata">Optional metadata object to serialize as JSON.</param>
        Task TransitionSyncStatusAsync(
            Sync sync,
            SyncStatus newStatus,
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
        /// Records the initial status for a newly created sync.
        /// </summary>
        /// <param name="sync">The newly created sync entity.</param>
        /// <param name="metadata">Optional metadata object to serialize as JSON.</param>
        Task RecordInitialSyncStatusAsync(Sync sync, object? metadata = null);

        /// <summary>
        /// Gets the status timeline for a job.
        /// </summary>
        /// <param name="jobId">The job ID.</param>
        /// <returns>List of timeline entries ordered by change time.</returns>
        Task<IReadOnlyList<JobTimelineEntryDto>> GetJobTimelineAsync(int jobId);

        /// <summary>
        /// Gets the status timeline for a sync.
        /// </summary>
        /// <param name="syncId">The sync ID.</param>
        /// <returns>List of timeline entries ordered by change time.</returns>
        Task<IReadOnlyList<SyncTimelineEntryDto>> GetSyncTimelineAsync(int syncId);
    }
}

