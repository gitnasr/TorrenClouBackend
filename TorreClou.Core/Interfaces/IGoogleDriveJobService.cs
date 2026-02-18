using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleDriveJobService
    {
        Task<string> UploadFileAsync(
            string filePath,
            string fileName,
            string folderId,
            string accessToken,
            string relativePath,
            CancellationToken cancellationToken = default);

        Task<string> CreateFolderAsync(string folderName, string? parentFolderId, string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh an access token using explicit app credentials and refresh token.
        /// </summary>
        Task<(string AccessToken, int ExpiresIn)> RefreshAccessTokenAsync(
            string clientId, string clientSecret, string refreshToken,
            UserStorageProfile? profile = null, CancellationToken cancellationToken = default);

        Task<string> GetAccessTokenAsync(UserStorageProfile profile, CancellationToken cancellationToken = default);

        Task<long> QueryUploadStatusAsync(string resumeUri, long fileSize, string accessToken, CancellationToken cancellationToken = default);

        Task<string?> CheckFileExistsAsync(string folderId, string fileName, string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the upload lock for a Google Drive job.
        /// </summary>
        Task<bool> DeleteUploadLockAsync(int jobId);

        /// <summary>
        /// Finds a folder by name in the parent, or creates it if it doesn't exist.
        /// This prevents duplicate folders from being created in Google Drive.
        /// </summary>
        Task<string> FindOrCreateFolderAsync(
            string folderName,
            string? parentFolderId,
            string accessToken,
            CancellationToken cancellationToken = default);
    }
}
