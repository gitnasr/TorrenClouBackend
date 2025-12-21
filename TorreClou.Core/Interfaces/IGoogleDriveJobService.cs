using TorreClou.Core.Shared;

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

        
        Task<Result<string>> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

      
        Task<Result<string>> GetAccessTokenAsync(string credentialsJson, CancellationToken cancellationToken = default);

       
        Task<Result<long>> QueryUploadStatusAsync(string resumeUri, long fileSize, string accessToken, CancellationToken cancellationToken = default);

       
        Task<Result<string?>> CheckFileExistsAsync(string folderId, string fileName, string accessToken, CancellationToken cancellationToken = default);
    }
}

