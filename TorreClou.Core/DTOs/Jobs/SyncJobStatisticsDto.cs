namespace TorreClou.Core.DTOs.Jobs
{
    public class SyncJobStatisticsDto
    {
        public int TotalSyncJobs { get; set; }
        public int ActiveSyncJobs { get; set; }
        public int CompletedSyncJobs { get; set; }
        public int FailedSyncJobs { get; set; }
        public int SyncingJobs { get; set; }
        public int NotStartedJobs { get; set; }
        public int InProgressJobs { get; set; }
        public int RetryingJobs { get; set; }
    }
}

