using TorreClou.Core.Enums;

namespace TorreClou.Core.Entities.Jobs
{
    /// <summary>
    /// Tracks status transitions for UserJob entities, providing a complete audit trail.
    /// </summary>
    public class JobStatusHistory : BaseEntity
    {
        public int JobId { get; set; }
        public UserJob Job { get; set; } = null!;

        /// <summary>
        /// The status before this transition. Null for the initial status when the job is created.
        /// </summary>
        public JobStatus? FromStatus { get; set; }

        /// <summary>
        /// The status after this transition.
        /// </summary>
        public JobStatus ToStatus { get; set; }

        /// <summary>
        /// The source that triggered this status change.
        /// </summary>
        public StatusChangeSource Source { get; set; }

        /// <summary>
        /// Error message if this transition was due to a failure.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// JSON-serialized metadata for this transition (e.g., progress, retry count, Hangfire job ID).
        /// </summary>
        public string? MetadataJson { get; set; }

        /// <summary>
        /// When this status change occurred.
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}

