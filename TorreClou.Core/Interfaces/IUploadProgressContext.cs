using Microsoft.Extensions.Logging;

namespace TorreClou.Core.Interfaces
{
    /// <summary>
    /// Scoped context for tracking upload progress across GoogleDriveService and GoogleDriveUploadJob.
    /// Handles progress throttling (5% DB updates, 30s logging) and Redis-backed resume URI caching.
    /// </summary>
    public interface IUploadProgressContext
    {
        /// <summary>
        /// Configures the context for a specific upload job. Must be called before any other methods.
        /// </summary>
        /// <param name="jobId">The job ID for Redis key prefixing</param>
        /// <param name="totalBytes">Total bytes across all files to upload</param>
        /// <param name="logger">Logger instance for progress logging</param>
        /// <param name="onDbUpdate">Callback invoked when DB should be updated (every 5% progress)</param>
        void Configure(int jobId, long totalBytes, ILogger logger, Func<string, double, Task> onDbUpdate);

        /// <summary>
        /// Reports upload progress for a file. Handles throttling internally.
        /// </summary>
        /// <param name="fileName">Name of the file being uploaded</param>
        /// <param name="bytesUploaded">Bytes uploaded so far for this file</param>
        /// <param name="fileSize">Total size of the file</param>
        Task ReportProgressAsync(string fileName, long bytesUploaded, long fileSize);

        /// <summary>
        /// Marks a file as completed and adds its size to the completed bytes counter.
        /// </summary>
        /// <param name="fileName">Name of the completed file</param>
        /// <param name="fileSize">Size of the completed file</param>
        void MarkFileCompleted(string fileName, long fileSize);

        /// <summary>
        /// Marks bytes as completed (for partial uploads or failed files).
        /// Use this instead of MarkFileCompleted when only partial bytes were uploaded.
        /// </summary>
        /// <param name="fileName">Name of the file</param>
        /// <param name="bytesUploaded">Number of bytes actually uploaded</param>
        void MarkBytesCompleted(string fileName, long bytesUploaded);

        /// <summary>
        /// Gets the cached resume URI for a file path, if available.
        /// </summary>
        /// <param name="relativePath">Relative path of the file within the download folder</param>
        /// <returns>The resume URI or null if not found</returns>
        Task<string?> GetResumeUriAsync(string relativePath);

        /// <summary>
        /// Caches the resume URI for a file path with a 7-day TTL.
        /// </summary>
        /// <param name="relativePath">Relative path of the file within the download folder</param>
        /// <param name="resumeUri">The resumable upload URI from Google Drive</param>
        Task SetResumeUriAsync(string relativePath, string resumeUri);

        /// <summary>
        /// Clears the cached resume URI for a file path after successful upload.
        /// </summary>
        /// <param name="relativePath">Relative path of the file within the download folder</param>
        Task ClearResumeUriAsync(string relativePath);

        /// <summary>
        /// Gets whether the context has been configured.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Stores a completed file mapping (relativePath → driveFileId) in Redis.
        /// Used to track which files have been successfully uploaded to prevent duplicates on restart.
        /// </summary>
        /// <param name="relativePath">Relative path of the file within the download folder</param>
        /// <param name="driveFileId">The Google Drive file ID returned after successful upload</param>
        Task SetCompletedFileAsync(string relativePath, string driveFileId);

        /// <summary>
        /// Gets the Google Drive file ID for a completed file, if it was previously uploaded.
        /// </summary>
        /// <param name="relativePath">Relative path of the file within the download folder</param>
        /// <returns>The Google Drive file ID if the file was already uploaded, null otherwise</returns>
        Task<string?> GetCompletedFileAsync(string relativePath);

        /// <summary>
        /// Stores the root folder ID for a job in Redis.
        /// Used to reuse the same folder when a job restarts.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <param name="folderId">The Google Drive root folder ID</param>
        Task SetRootFolderIdAsync(int jobId, string folderId);

        /// <summary>
        /// Gets the root folder ID for a job, if it was previously created.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <returns>The Google Drive root folder ID if it exists, null otherwise</returns>
        Task<string?> GetRootFolderIdAsync(int jobId);

        /// <summary>
        /// Clears all job state from Redis (completed files, root folder, resume URIs).
        /// Should be called when a job completes successfully or is cancelled.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        Task ClearJobStateAsync(int jobId);
    }
}
