using TorreClou.Core.Enums;

namespace TorreClou.Core.DTOs.Jobs
{
    /// <summary>
    /// Represents a single status change entry in a job's timeline.
    /// </summary>
    public class JobTimelineEntryDto
    {
        /// <summary>
        /// The status before this transition. Null for the initial status.
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
        /// Human-readable source name.
        /// </summary>
        public string SourceName => Source.ToString();

        /// <summary>
        /// Error message if this transition was due to a failure.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Additional metadata for this transition.
        /// </summary>
        public string? MetadataJson { get; set; }

        /// <summary>
        /// When this status change occurred.
        /// </summary>
        public DateTime ChangedAt { get; set; }

        /// <summary>
        /// Duration from the previous status change (or job creation) to this one.
        /// </summary>
        public TimeSpan? DurationFromPrevious { get; set; }
    }
}

