
using TorreClou.Core.Enums;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.Core.Entities.Jobs
{
    public class UserJob : BaseEntity, IRecoverableJob
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;


        public int StorageProfileId { get; set; }
        public UserStorageProfile StorageProfile { get; set; } = null!;

        public JobStatus Status { get; set; } = JobStatus.QUEUED;

        public JobType Type { get; set; } = JobType.Torrent;

        public int RequestFileId { get; set; }

        public RequestedFile RequestFile { get; set; } = null!;

        public string? ErrorMessage { get; set; }
        public string? CurrentState { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        public DateTime? NextRetryAt { get; set; }
      
        public DateTime? LastHeartbeat { get; set; }

        public string? HangfireJobId { get; set; }
        public string? HangfireUploadJobId { get; set; }

        public string? DownloadPath { get; set; }

      
        public long BytesDownloaded { get; set; }

       
        public long TotalBytes { get; set; }



        public string[]? SelectedFilePaths { get; set; }



        /// <summary>
        /// Status change history for this job, providing a complete audit trail.
        /// </summary>
        public ICollection<JobStatusHistory> StatusHistory { get; set; } = new List<JobStatusHistory>();
    }
}