namespace TorreClou.Core.DTOs.Jobs
{
    public class JobStatisticsDto
    {
        public int TotalJobs { get; set; }
        public int ActiveJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int FailedJobs { get; set; }
        public int QueuedJobs { get; set; }
        public int ProcessingJobs { get; set; }
        public int PendingUploadJobs { get; set; }
        public int UploadingJobs { get; set; }
        public int RetryingJobs { get; set; }
        public int CancelledJobs { get; set; }
    }
}
