namespace TorreClou.Core.Enums
{
    public enum ErrorCode
    {
        // General
        None,
        NullValue,
        ServerConfigError,
        UnexpectedError,

        // Not Found
        NotFound,
        ProfileNotFound,
        TorrentNotFound,
        JobNotFound,
        UserNotFound,
        FileNotFound,
        BucketNotFound,

        // Authentication / Authorization
        Unauthorized,
        InvalidCredentials,
        AccessDenied,

        // Validation
        Invalid,
        InvalidTorrent,
        InvalidInfoHash,
        InvalidFileName,
        InvalidFileSize,
        InvalidProfileName,
        InvalidS3Config,
        InvalidClientId,
        InvalidClientSecret,
        InvalidRedirectUri,
        InvalidState,
        InvalidResponse,
        InvalidProfile,
        InvalidCredentialsJson,
        InvalidStorageProfile,
        V2OnlyNotSupported,
        ProfileNameTooShort,
        ProfileNameTooLong,

        // Conflict / Duplication
        DuplicateEmail,
        AlreadyDisconnected,
        JobAlreadyExists,
        JobAlreadyCancelled,

        // Business Logic
        ProfileInUse,
        NoStorage,
        StorageInactive,
        InactiveProfile,
        NoCredentials,
        MissingRequiredFields,
        JobCompleted,
        JobCancelled,
        JobActive,
        JobRetrying,
        JobInUploadPhase,
        JobNotCancellable,

        // OAuth / External Service
        AuthUrlError,
        MissingCredentials,
        TokenExchangeFailed,
        OAuthCallbackError,
        NoRefreshToken,
        TokenError,
        RefreshFailed,
        RefreshError,
        S3Error,
        BucketAccessDenied,

        // Upload / Storage Operations
        FolderCreateFailed,
        FolderCreateError,
        UploadError,
        UploadIncomplete,
        InitFailed,
        InitUploadFailed,
        InitUploadError,
        ChunkFailed,
        UploadPartFailed,
        UploadPartError,
        CompleteUploadFailed,
        CompleteUploadError,
        ReadError,
        StatusQueryFailed,
        StatusQueryError,
        CheckFailed,
        CheckError,
        ListPartsFailed,
        ListPartsError
    }

    public enum UserRole { User, Admin, Support , Suspended, Banned}

    public enum StorageProviderType { GoogleDrive, OneDrive, AwsS3, Dropbox }

    public enum FileStatus { PENDING, DOWNLOADING, READY, CORRUPTED, DELETED }

    public enum S3UploadProgressStatus
    {
        InProgress,
        Completed,
        Failed
    }
    public enum JobStatus 
    { 
        QUEUED, 
        DOWNLOADING, 
        PENDING_UPLOAD, 
        UPLOADING, 
        TORRENT_DOWNLOAD_RETRY, 
        UPLOAD_RETRY, 
        COMPLETED, 
        FAILED, 
        CANCELLED, 
        TORRENT_FAILED, 
        UPLOAD_FAILED, 
        GOOGLE_DRIVE_FAILED 
    }

    public enum JobType { Torrent }

    public enum ViolationType
    {
        Spam,
        Abuse,
        TermsViolation,
        CopyrightInfringement,
        Other
    }

    /// <summary>
    /// Identifies the source that triggered a job/sync status change.
    /// </summary>
    public enum StatusChangeSource
    {
        /// <summary>Worker process changed the status during job execution.</summary>
        Worker,
        /// <summary>User action triggered the status change (e.g., cancellation).</summary>
        User,
        /// <summary>System/API triggered the status change (e.g., job creation).</summary>
        System,
        /// <summary>Recovery process changed the status (e.g., recovering stuck jobs).</summary>
        Recovery
    }
}