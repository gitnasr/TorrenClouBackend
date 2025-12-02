namespace TorreClou.Core.Enums
{
    public enum RegionCode { Global, US, EU, EG, SA, IN }

    public enum UserRole { User, Admin, Support }

    public enum StorageProviderType { GoogleDrive, OneDrive, AwsS3, Dropbox }

    public enum TransactionType { DEPOSIT, PAYMENT, REFUND, ADMIN_ADJUSTMENT, BONUS }

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
    public enum JobStatus { QUEUED, PROCESSING, UPLOADING, COMPLETED, FAILED, CANCELLED }

    public enum ViolationType
    {
        Spam,
        Abuse,
        TermsViolation,
        CopyrightInfringement,
        Other
    }
}