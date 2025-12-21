namespace TorreClou.Core.Enums
{
    public enum RegionCode { Global, US, EU, EG, SA, IN }

    public enum UserRole { User, Admin, Support }

    public enum StorageProviderType { GoogleDrive, OneDrive, AwsS3, Dropbox }

    public enum TransactionType { DEPOSIT, PAYMENT, REFUND, ADMIN_ADJUSTMENT, BONUS,
        DEDUCTION
    }

    public enum FileStatus { PENDING, DOWNLOADING, READY, CORRUPTED, DELETED }

    public enum DiscountType
    {
        Percentage,
        FixedAmount
    }
    public enum DepositStatus
    {
        Pending,   // اليوزر لسه فاتح صفحة الدفع
        Completed, // الفلوس وصلت وتأكدت
        Failed,    // الفيزا اترفضا
        Expired    // اللينك مدته انتهت
    }
    public enum JobStatus 
    { 
        QUEUED, 
        DOWNLOADING, 
        SYNCING, 
        PENDING_UPLOAD, 
        UPLOADING, 
        TORRENT_DOWNLOAD_RETRY, 
        UPLOAD_RETRY, 
        SYNC_RETRY, 
        COMPLETED, 
        FAILED, 
        CANCELLED, 
        TORRENT_FAILED, 
        UPLOAD_FAILED, 
        GOOGLE_DRIVE_FAILED 
    }

    public enum JobType { Torrent,  Sync}

    public enum ViolationType
    {
        Spam,
        Abuse,
        TermsViolation,
        CopyrightInfringement,
        Other
    }

    public enum SyncProgressStatus
    {
        NotStarted,
        InProgress,
        Completed,
        Failed
    }

    public enum SyncStatus
    {
        Pending,      // Created but not started
        InProgress,   // Currently syncing
        Completed,   // All files synced successfully
        Failed,       // Sync failed
        Retrying      // Retrying after failure
    }
}