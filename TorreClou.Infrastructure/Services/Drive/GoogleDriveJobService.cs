using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Options;
using TorreClou.Core.Shared;
using TorreClou.Core.Entities.Jobs;

namespace TorreClou.Infrastructure.Services.Drive
{
    public  class GoogleDriveJobService(
        IOptions<GoogleDriveSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDriveJobService> logger,
        IUploadProgressContext progressContext,
        IUnitOfWork unitOfWork,
        IRedisLockService redisLockService) : IGoogleDriveJobService
    {
        private readonly GoogleDriveSettings _settings = settings.Value;

        // Upload chunk size: 10 MB (must be multiple of 256 KB per Google's requirements)
        private const int ChunkSize = 10 * 1024 * 1024;

        public async Task<Result<string>> GetAccessTokenAsync(UserStorageProfile profile, CancellationToken cancellationToken = default)
        {
            try
            {
                var credentials = JsonSerializer.Deserialize<GoogleDriveCredentials>(profile.CredentialsJson);
                if (credentials == null || string.IsNullOrEmpty(credentials.AccessToken))
                {
                    return Result<string>.Failure("INVALID_CREDENTIALS", "Invalid credentials JSON");
                }

                // Always refresh token at job start to prevent mid-job expiration
                if (string.IsNullOrEmpty(credentials.RefreshToken))
                {
                    return Result<string>.Failure("NO_REFRESH_TOKEN", "No refresh token available");
                }

                // Use profile credentials if available (configure flow), otherwise use environment settings
                var refreshResult = await RefreshAccessTokenAsync(credentials, cancellationToken);
                if (refreshResult.IsFailure)
                {
                    return Result<string>.Failure(refreshResult.Error.Code, refreshResult.Error.Message);
                }

                // Update credentials with new token and expiration
                credentials.AccessToken = refreshResult.Value.AccessToken;
                credentials.ExpiresAt = DateTime.UtcNow.AddSeconds(refreshResult.Value.ExpiresIn).ToString("O");

                // Save back to profile
                profile.CredentialsJson = JsonSerializer.Serialize(credentials);

                // Persist changes to database
                await unitOfWork.Complete();

                logger.LogInformation("Token refreshed and persisted for profile {ProfileId}", profile.Id);
                return Result.Success(refreshResult.Value.AccessToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting access token from credentials");
                return Result<string>.Failure("TOKEN_ERROR", "Failed to get access token");
            }
        }

        public async Task<Result<(string AccessToken, int ExpiresIn)>> RefreshAccessTokenAsync(GoogleDriveCredentials credentials, CancellationToken cancellationToken = default)
        {
            try
            {
                // Use profile credentials if available (from configure flow), otherwise fall back to settings
                var clientId = !string.IsNullOrEmpty(credentials.ClientId) ? credentials.ClientId : _settings.ClientId;
                var clientSecret = !string.IsNullOrEmpty(credentials.ClientSecret) ? credentials.ClientSecret : _settings.ClientSecret;

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    logger.LogError("No OAuth credentials available for token refresh. Profile credentials empty and no environment settings configured.");
                    return Result<(string, int)>.Failure("NO_CREDENTIALS", "No OAuth credentials available for token refresh");
                }

                var httpClient = httpClientFactory.CreateClient();
                var requestBody = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "refresh_token", credentials.RefreshToken! },
                    { "grant_type", "refresh_token" }
                };

                var content = new FormUrlEncodedContent(requestBody);
                var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Token refresh failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<(string, int)>.Failure("REFRESH_FAILED", "Failed to refresh access token");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenResponse = JsonSerializer.Deserialize<GoogleDriveTokenRefreshResponse>(jsonResponse);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                {
                    return Result<(string, int)>.Failure("INVALID_RESPONSE", "Invalid token refresh response");
                }

                return Result.Success((tokenResponse.AccessToken, tokenResponse.ExpiresIn));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception during token refresh");
                return Result<(string, int)>.Failure("REFRESH_ERROR", "Error refreshing access token");
            }
        }

        public async Task<Result<string>> CreateFolderAsync(string folderName, string? parentFolderId, string accessToken, CancellationToken cancellationToken = default)
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var folderMetadata = new
                {
                    name = folderName,
                    mimeType = "application/vnd.google-apps.folder",
                    parents = !string.IsNullOrEmpty(parentFolderId) ? [parentFolderId] : Array.Empty<string>()
                };

                var json = JsonSerializer.Serialize(folderMetadata);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(
                    "https://www.googleapis.com/drive/v3/files?fields=id,name",
                    content,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Folder creation failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<string>.Failure("FOLDER_CREATE_FAILED", $"Failed to create folder: {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var folderResponse = JsonSerializer.Deserialize<FolderResponse>(responseJson);

                if (folderResponse == null || string.IsNullOrEmpty(folderResponse.Id))
                {
                    return Result<string>.Failure("INVALID_RESPONSE", "Invalid folder creation response");
                }

                return Result.Success(folderResponse.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception creating folder: {FolderName}", folderName);
                return Result<string>.Failure("FOLDER_CREATE_ERROR", "Error creating folder");
            }
        }

        public async Task<Result<string>> FindOrCreateFolderAsync(
            string folderName, 
            string? parentFolderId, 
            string accessToken, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. Check if folder already exists in parent
                var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(1);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Build query: name='folderName' and mimeType='folder' and 'parentId' in parents and trashed=false
                var escapedFolderName = folderName.Replace("'", "\\'");
                var query = $"name='{escapedFolderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
                
                if (!string.IsNullOrEmpty(parentFolderId))
                {
                    query += $" and '{parentFolderId}' in parents";
                }

                var url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id,name)&pageSize=1";

                var response = await httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    var fileListResponse = JsonSerializer.Deserialize<FileListResponse>(responseJson);

                    if (fileListResponse?.Files != null && fileListResponse.Files.Length > 0)
                    {
                        var existingFolderId = fileListResponse.Files[0].Id;
                        logger.LogDebug("Folder already exists | FolderName: {FolderName} | ParentId: {ParentId} | FolderId: {FolderId}",
                            folderName, parentFolderId ?? "root", existingFolderId);
                        return Result.Success(existingFolderId!);
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogWarning("Failed to check folder existence: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    // Continue to create folder even if check fails
                }

                // 2. Folder doesn't exist, create it
                logger.LogDebug("Folder does not exist, creating | FolderName: {FolderName} | ParentId: {ParentId}",
                    folderName, parentFolderId ?? "root");
                
                return await CreateFolderAsync(folderName, parentFolderId, accessToken, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in FindOrCreateFolderAsync: {FolderName}", folderName);
                // Fallback to creating the folder
                return await CreateFolderAsync(folderName, parentFolderId, accessToken, cancellationToken);
            }
        }

        public async Task<Result<string>> UploadFileAsync(
            string filePath,
            string fileName,
            string folderId,
            string accessToken,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(filePath))
                    return Result<string>.Failure("FILE_NOT_FOUND", $"File not found: {filePath}");

                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;
                var contentType = GetMimeType(fileName);

                // Check for existing resume URI
                string? resumeUri = null;
                long startByte = 0;

                if (progressContext.IsConfigured)
                {
                    resumeUri = await progressContext.GetResumeUriAsync(relativePath);
                }

                if (!string.IsNullOrEmpty(resumeUri))
                {
                    // Query Google for current upload status
                    var statusResult = await QueryUploadStatusAsync(resumeUri, fileSize, accessToken, cancellationToken);
                    if (statusResult.IsSuccess)
                    {
                        startByte = statusResult.Value;
                    }
                    else
                    {
                        // Resume URI invalid, start fresh
                        logger.LogCritical("Resume URI invalid, starting fresh upload | File: {FileName}", fileName);
                        resumeUri = null;
                        if (progressContext.IsConfigured)
                        {
                            await progressContext.ClearResumeUriAsync(relativePath);
                        }
                    }
                }

                // Initiate new resumable upload if needed
                if (string.IsNullOrEmpty(resumeUri))
                {
                    var initResult = await InitiateResumableUploadAsync(fileName, folderId, fileSize, contentType, accessToken, cancellationToken);
                    if (initResult.IsFailure)
                    {
                        return Result<string>.Failure(initResult.Error.Code, initResult.Error.Message);
                    }
                    resumeUri = initResult.Value;

                    // Cache the resume URI
                    if (progressContext.IsConfigured)
                    {
                        await progressContext.SetResumeUriAsync(relativePath, resumeUri);
                    }
                }

                // Upload file in chunks with progress reporting
                var uploadResult = await UploadFileChunkedAsync(
                    filePath, fileName, fileSize, resumeUri, startByte, contentType, accessToken, cancellationToken);

                if (uploadResult.IsSuccess && progressContext.IsConfigured)
                {
                    // Clear resume URI on success
                    await progressContext.ClearResumeUriAsync(relativePath);
                }

                return uploadResult;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception uploading file: {FileName}", fileName);
                return Result<string>.Failure("UPLOAD_ERROR", ex.Message);
            }
        }

        private async Task<Result<string>> InitiateResumableUploadAsync(
            string fileName,
            string folderId,
            long fileSize,
            string contentType,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            var initiateUrl = "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,name";
            var request = new HttpRequestMessage(HttpMethod.Post, initiateUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("X-Upload-Content-Type", contentType);
            request.Headers.Add("X-Upload-Content-Length", fileSize.ToString());

            var metadata = new { name = fileName, parents = new[] { folderId } };
            var jsonMetadata = JsonSerializer.Serialize(metadata);
            request.Content = new StringContent(jsonMetadata, Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Failed to initiate resumable upload: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return Result<string>.Failure("INIT_FAILED", $"Failed to initiate upload: {errorContent}");
            }

            var uploadUri = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(uploadUri))
            {
                return Result<string>.Failure("INIT_FAILED", "No upload URI returned");
            }

            return Result.Success(uploadUri);
        }

        public async Task<Result<long>> QueryUploadStatusAsync(
            string resumeUri,
            long fileSize,
            string accessToken,
            CancellationToken cancellationToken)
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(1);

                var request = new HttpRequestMessage(HttpMethod.Put, resumeUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new ByteArrayContent([]);
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(fileSize) { Unit = "bytes" };
                // Format: "bytes */total"

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                {
                    // Upload already complete
                    return Result.Success(fileSize);
                }

                if ((int)response.StatusCode == 308) // Resume Incomplete
                {
                    if (response.Headers.TryGetValues("Range", out var rangeValues))
                    {
                        var rangeHeader = rangeValues.FirstOrDefault();
                        if (!string.IsNullOrEmpty(rangeHeader))
                        {
                            // Format: "bytes=0-12345"
                            var parts = rangeHeader.Replace("bytes=", "").Split('-');
                            if (parts.Length == 2 && long.TryParse(parts[1], out var uploadedBytes))
                            {
                                return Result.Success(uploadedBytes + 1); // Resume from next byte
                            }
                        }
                    }
                    // No Range header means no bytes uploaded yet
                    return Result.Success(0L);
                }

                return Result<long>.Failure("STATUS_QUERY_FAILED", $"Unexpected status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query upload status");
                return Result<long>.Failure("STATUS_QUERY_ERROR", ex.Message);
            }
        }

        private async Task<Result<string>> UploadFileChunkedAsync(
            string filePath,
            string fileName,
            long fileSize,
            string resumeUri,
            long startByte,
            string contentType,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromHours(2);

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            // Seek to start position if resuming
            if (startByte > 0)
            {
                fileStream.Seek(startByte, SeekOrigin.Begin);
            }

            var buffer = new byte[ChunkSize];
            var currentPosition = startByte;

            while (currentPosition < fileSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToRead = (int)Math.Min(ChunkSize, fileSize - currentPosition);
                var bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

                if (bytesRead == 0)
                    break;

                var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
                chunkContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                chunkContent.Headers.ContentLength = bytesRead;
                chunkContent.Headers.ContentRange = new ContentRangeHeaderValue(
                    currentPosition, currentPosition + bytesRead - 1, fileSize);

                var request = new HttpRequestMessage(HttpMethod.Put, resumeUri)
                {
                    Content = chunkContent
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Created)
                {
                    // Upload complete
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    var driveFile = JsonSerializer.Deserialize<FileUploadResponse>(responseJson);

                    // Report final progress
                    if (progressContext.IsConfigured)
                    {
                        await progressContext.ReportProgressAsync(fileName, fileSize, fileSize);
                    }

                    return Result.Success(driveFile?.Id ?? "");
                }

                if ((int)response.StatusCode == 308) // Resume Incomplete - chunk accepted
                {
                    currentPosition += bytesRead;

                    // Report progress
                    if (progressContext.IsConfigured)
                    {
                        await progressContext.ReportProgressAsync(fileName, currentPosition, fileSize);
                    }

                    // If all bytes have been sent but we still got 308, finalize the upload
                    if (currentPosition >= fileSize)
                    {
                        // Make a final PUT request with Content-Range query format to finalize and get file ID
                        var finalRequest = new HttpRequestMessage(HttpMethod.Put, resumeUri);
                        finalRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                        finalRequest.Content = new ByteArrayContent(Array.Empty<byte>());
                        // Use query format: "bytes */fileSize" to finalize upload
                        finalRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(fileSize) { Unit = "bytes" };

                        var finalResponse = await httpClient.SendAsync(finalRequest, cancellationToken);
                        
                        if (finalResponse.StatusCode == HttpStatusCode.OK || finalResponse.StatusCode == HttpStatusCode.Created)
                        {
                            var responseJson = await finalResponse.Content.ReadAsStringAsync(cancellationToken);
                            var driveFile = JsonSerializer.Deserialize<FileUploadResponse>(responseJson);

                            // Report final progress
                            if (progressContext.IsConfigured)
                            {
                                await progressContext.ReportProgressAsync(fileName, fileSize, fileSize);
                            }

                            return Result.Success(driveFile?.Id ?? "");
                        }
                        else
                        {
                            logger.LogWarning("Finalization request failed: {StatusCode} | File: {FileName}", finalResponse.StatusCode, fileName);
                            // Fall through to post-loop check
                        }
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogError("Chunk upload failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<string>.Failure("CHUNK_FAILED", $"Chunk upload failed: {response.StatusCode}");
                }
            }

            // If we exit the loop and all bytes were sent, finalize the upload as fallback
            if (currentPosition >= fileSize)
            {
                // Make a final PUT request with Content-Range query format to finalize and get file ID
                var finalRequest = new HttpRequestMessage(HttpMethod.Put, resumeUri);
                finalRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                finalRequest.Content = new ByteArrayContent(Array.Empty<byte>());
                // Use query format: "bytes */fileSize" to finalize upload
                finalRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(fileSize) { Unit = "bytes" };

                var finalResponse = await httpClient.SendAsync(finalRequest, cancellationToken);
                
                if (finalResponse.StatusCode == HttpStatusCode.OK || finalResponse.StatusCode == HttpStatusCode.Created)
                {
                    var responseJson = await finalResponse.Content.ReadAsStringAsync(cancellationToken);
                    var driveFile = JsonSerializer.Deserialize<FileUploadResponse>(responseJson);

                    // Report final progress
                    if (progressContext.IsConfigured)
                    {
                        await progressContext.ReportProgressAsync(fileName, fileSize, fileSize);
                    }

                    return Result.Success(driveFile?.Id ?? "");
                }
                else
                {
                    logger.LogWarning("Finalization request failed after loop exit: {StatusCode} | File: {FileName}", finalResponse.StatusCode, fileName);
                }
            }

            return Result<string>.Failure("UPLOAD_INCOMPLETE", "Upload did not complete successfully");
        }

        public async Task<Result<string?>> CheckFileExistsAsync(string folderId, string fileName, string accessToken, CancellationToken cancellationToken = default)
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(1);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Escape single quotes in fileName for the query
                var escapedFileName = fileName.Replace("'", "\\'");
                var query = $"name='{escapedFileName}' and '{folderId}' in parents and trashed=false";
                var url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(query)}&fields=files(id,name)&pageSize=1";

                var response = await httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogWarning("Failed to check file existence: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<string?>.Failure("CHECK_FAILED", $"Failed to check file existence: {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var fileListResponse = JsonSerializer.Deserialize<FileListResponse>(responseJson);

                if (fileListResponse?.Files != null && fileListResponse.Files.Length > 0)
                {
                    var fileId = fileListResponse.Files[0].Id;
                    logger.LogDebug("File exists in Google Drive | FileName: {FileName} | FolderId: {FolderId} | FileId: {FileId}",
                        fileName, folderId, fileId);
                    return Result.Success<string?>(fileId);
                }

                logger.LogDebug("File does not exist in Google Drive | FileName: {FileName} | FolderId: {FolderId}", fileName, folderId);
                return Result.Success<string?>(null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception checking file existence: {FileName}", fileName);
                return Result<string?>.Failure("CHECK_ERROR", $"Error checking file existence: {ex.Message}");
            }
        }

        public async Task<bool> DeleteUploadLockAsync(int jobId)
        {
            try
            {
                var lockKey = $"gdrive:lock:{jobId}";
                return await redisLockService.DeleteLockAsync(lockKey);
            }
            catch (Exception ex)
            {
                // Log but don't fail - lock might not exist or already expired
                logger.LogWarning(ex, "Failed to delete upload lock for job {JobId}", jobId);
                return false;
            }
        }

        private static string GetMimeType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                ".wmv" => "video/x-ms-wmv",
                ".flv" => "video/x-flv",
                ".webm" => "video/webm",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                ".tar" => "application/x-tar",
                ".gz" => "application/gzip",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",
                ".ogg" => "audio/ogg",
                ".bin" => "application/octet-stream",
                ".iso" => "application/x-iso9660-image",
                ".exe" => "application/x-msdownload",
                _ => "application/octet-stream"
            };
        }
    }
}
