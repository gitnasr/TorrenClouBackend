using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleDriveJob
    {
        /// <summary>
        /// Uploads a file to Google Drive with resumable upload support.
        /// Progress is reported via the injected IUploadProgressContext.
        /// </summary>
        /// <param name="filePath">Full path to the local file</param>
        /// <param name="fileName">Name for the file in Google Drive</param>
        /// <param name="folderId">Google Drive folder ID to upload to</param>
        /// <param name="accessToken">OAuth access token</param>
        /// <param name="relativePath">Relative path for resume key lookup (from download root)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The Google Drive file ID on success</returns>
        Task<Result<string>> UploadFileAsync(
            string filePath, 
            string fileName, 
            string folderId, 
            string accessToken, 
            string relativePath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a folder in Google Drive.
        /// </summary>
        Task<Result<string>> CreateFolderAsync(string folderName, string? parentFolderId, string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes an OAuth access token using a refresh token.
        /// </summary>
        Task<Result<string>> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a valid access token from credentials JSON, refreshing if needed.
        /// </summary>
        Task<Result<string>> GetAccessTokenAsync(string credentialsJson, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the upload status for a resumable upload to determine how many bytes have been uploaded.
        /// </summary>
        /// <param name="resumeUri">The resumable upload URI from Google Drive</param>
        /// <param name="fileSize">Total size of the file being uploaded</param>
        /// <param name="accessToken">OAuth access token</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The number of bytes uploaded (0 if none, fileSize if complete)</returns>
        Task<Result<long>> QueryUploadStatusAsync(string resumeUri, long fileSize, string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a file with the given name already exists in the specified Google Drive folder.
        /// Used as a fallback to verify file existence when Redis state is unavailable.
        /// </summary>
        /// <param name="folderId">Google Drive folder ID to search in</param>
        /// <param name="fileName">Name of the file to search for</param>
        /// <param name="accessToken">OAuth access token</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The Google Drive file ID if the file exists, null if not found</returns>
        Task<Result<string?>> CheckFileExistsAsync(string folderId, string fileName, string accessToken, CancellationToken cancellationToken = default);
    }
}

