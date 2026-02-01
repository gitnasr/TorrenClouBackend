using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Shared;
using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Core.Interfaces
{
    public interface IGoogleDriveJobService
    {

        Task<Result<string>> UploadFileAsync(
            string filePath,
            string fileName,
            string folderId,
            string accessToken,
            string relativePath,
            CancellationToken cancellationToken = default);


        Task<Result<string>> CreateFolderAsync(string folderName, string? parentFolderId, string accessToken, CancellationToken cancellationToken = default);


        Task<Result<(string AccessToken, int ExpiresIn)>> RefreshAccessTokenAsync(GoogleDriveCredentials credentials, CancellationToken cancellationToken = default);

      
        Task<Result<string>> GetAccessTokenAsync(UserStorageProfile profile, CancellationToken cancellationToken = default);

       
        Task<Result<long>> QueryUploadStatusAsync(string resumeUri, long fileSize, string accessToken, CancellationToken cancellationToken = default);

       
        Task<Result<string?>> CheckFileExistsAsync(string folderId, string fileName, string accessToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the upload lock for a Google Drive job.
        /// </summary>
        /// <param name="jobId">The job ID</param>
        /// <returns>True if the lock was deleted, false if it didn't exist</returns>
        Task<bool> DeleteUploadLockAsync(int jobId);

        /// <summary>
        /// Finds a folder by name in the parent, or creates it if it doesn't exist.
        /// This prevents duplicate folders from being created in Google Drive.
        /// </summary>
        Task<Result<string>> FindOrCreateFolderAsync(
            string folderName, 
            string? parentFolderId, 
            string accessToken, 
            CancellationToken cancellationToken = default);
    }
}

