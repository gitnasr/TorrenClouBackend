using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Options;
using TorreClou.Core.Shared;

namespace TorreClou.Infrastructure.Services
{
    public class GoogleDriveService : IGoogleDriveService
    {
        private readonly GoogleDriveSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GoogleDriveService> _logger;
        private readonly IUploadProgressContext _progressContext;

        // Upload chunk size: 10 MB (must be multiple of 256 KB per Google's requirements)
        private const int ChunkSize = 10 * 1024 * 1024;

        public GoogleDriveService(
            IOptions<GoogleDriveSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<GoogleDriveService> logger,
            IUploadProgressContext progressContext)
        {
            _settings = settings.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _progressContext = progressContext;
        }

        public async Task<Result<string>> GetAccessTokenAsync(string credentialsJson, CancellationToken cancellationToken = default)
        {
            try
            {
                var credentials = JsonSerializer.Deserialize<GoogleDriveCredentials>(credentialsJson);
                if (credentials == null || string.IsNullOrEmpty(credentials.AccessToken))
                {
                    return Result<string>.Failure("INVALID_CREDENTIALS", "Invalid credentials JSON");
                }

                // Check if token is expired
                if (!string.IsNullOrEmpty(credentials.ExpiresAt))
                {
                    if (DateTime.TryParse(credentials.ExpiresAt, out var expiresAt) && expiresAt <= DateTime.UtcNow.AddMinutes(5))
                    {
                        // Token expired or about to expire, refresh it
                        if (string.IsNullOrEmpty(credentials.RefreshToken))
                        {
                            return Result<string>.Failure("NO_REFRESH_TOKEN", "Access token expired and no refresh token available");
                        }

                        var refreshResult = await RefreshAccessTokenAsync(credentials.RefreshToken, cancellationToken);
                        if (refreshResult.IsFailure)
                        {
                            return refreshResult;
                        }

                        return Result.Success(refreshResult.Value);
                    }
                }

                return Result.Success(credentials.AccessToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting access token from credentials");
                return Result<string>.Failure("TOKEN_ERROR", "Failed to get access token");
            }
        }

        public async Task<Result<string>> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var requestBody = new Dictionary<string, string>
                {
                    { "client_id", _settings.ClientId },
                    { "client_secret", _settings.ClientSecret },
                    { "refresh_token", refreshToken },
                    { "grant_type", "refresh_token" }
                };

                var content = new FormUrlEncodedContent(requestBody);
                var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Token refresh failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<string>.Failure("REFRESH_FAILED", "Failed to refresh access token");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var tokenResponse = JsonSerializer.Deserialize<TokenRefreshResponse>(jsonResponse);

                if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                {
                    return Result<string>.Failure("INVALID_RESPONSE", "Invalid token refresh response");
                }

                return Result.Success(tokenResponse.AccessToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during token refresh");
                return Result<string>.Failure("REFRESH_ERROR", "Error refreshing access token");
            }
        }

        public async Task<Result<string>> CreateFolderAsync(string folderName, string? parentFolderId, string accessToken, CancellationToken cancellationToken = default)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var folderMetadata = new
                {
                    name = folderName,
                    mimeType = "application/vnd.google-apps.folder",
                    parents = !string.IsNullOrEmpty(parentFolderId) ? new[] { parentFolderId } : Array.Empty<string>()
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
                    _logger.LogError("Folder creation failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
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
                _logger.LogError(ex, "Exception creating folder: {FolderName}", folderName);
                return Result<string>.Failure("FOLDER_CREATE_ERROR", "Error creating folder");
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

                if (_progressContext.IsConfigured)
                {
                    resumeUri = await _progressContext.GetResumeUriAsync(relativePath);
                }

                if (!string.IsNullOrEmpty(resumeUri))
                {
                    // Query Google for current upload status
                    var statusResult = await QueryUploadStatusAsync(resumeUri, fileSize, accessToken, cancellationToken);
                    if (statusResult.IsSuccess)
                    {
                        startByte = statusResult.Value;
                        _logger.LogInformation("Resuming upload from byte {StartByte} | File: {FileName}", startByte, fileName);
                    }
                    else
                    {
                        // Resume URI invalid, start fresh
                        _logger.LogWarning("Resume URI invalid, starting fresh upload | File: {FileName}", fileName);
                        resumeUri = null;
                        if (_progressContext.IsConfigured)
                        {
                            await _progressContext.ClearResumeUriAsync(relativePath);
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
                    if (_progressContext.IsConfigured)
                    {
                        await _progressContext.SetResumeUriAsync(relativePath, resumeUri);
                    }
                }

                // Upload file in chunks with progress reporting
                var uploadResult = await UploadFileChunkedAsync(
                    filePath, fileName, fileSize, resumeUri, startByte, contentType, accessToken, cancellationToken);

                if (uploadResult.IsSuccess && _progressContext.IsConfigured)
                {
                    // Clear resume URI on success
                    await _progressContext.ClearResumeUriAsync(relativePath);
                }

                return uploadResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception uploading file: {FileName}", fileName);
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
            var httpClient = _httpClientFactory.CreateClient();
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
                _logger.LogError("Failed to initiate resumable upload: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return Result<string>.Failure("INIT_FAILED", $"Failed to initiate upload: {errorContent}");
            }

            var uploadUri = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(uploadUri))
            {
                return Result<string>.Failure("INIT_FAILED", "No upload URI returned");
            }

            _logger.LogDebug("Initiated resumable upload | URI: {Uri}", uploadUri);
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
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(1);

                var request = new HttpRequestMessage(HttpMethod.Put, resumeUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new ByteArrayContent(Array.Empty<byte>());
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
                _logger.LogWarning(ex, "Failed to query upload status");
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
            var httpClient = _httpClientFactory.CreateClient();
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
                    if (_progressContext.IsConfigured)
                    {
                        await _progressContext.ReportProgressAsync(fileName, fileSize, fileSize);
                    }

                    _logger.LogInformation("Upload complete | File: {FileName} | DriveFileId: {FileId}", fileName, driveFile?.Id);
                    return Result.Success(driveFile?.Id ?? "");
                }

                if ((int)response.StatusCode == 308) // Resume Incomplete - chunk accepted
                {
                    currentPosition += bytesRead;

                    // Report progress
                    if (_progressContext.IsConfigured)
                    {
                        await _progressContext.ReportProgressAsync(fileName, currentPosition, fileSize);
                    }

                    // If all bytes have been sent but we still got 308, finalize the upload
                    if (currentPosition >= fileSize)
                    {
                        _logger.LogInformation("All bytes sent, finalizing upload | File: {FileName}", fileName);
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
                            if (_progressContext.IsConfigured)
                            {
                                await _progressContext.ReportProgressAsync(fileName, fileSize, fileSize);
                            }

                            _logger.LogInformation("Upload complete (finalized after 308) | File: {FileName} | DriveFileId: {FileId}", fileName, driveFile?.Id);
                            return Result.Success(driveFile?.Id ?? "");
                        }
                        else
                        {
                            _logger.LogWarning("Finalization request failed: {StatusCode} | File: {FileName}", finalResponse.StatusCode, fileName);
                            // Fall through to post-loop check
                        }
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Chunk upload failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<string>.Failure("CHUNK_FAILED", $"Chunk upload failed: {response.StatusCode}");
                }
            }

            // If we exit the loop and all bytes were sent, finalize the upload as fallback
            if (currentPosition >= fileSize)
            {
                _logger.LogInformation("Loop exited with all bytes sent, finalizing upload | File: {FileName}", fileName);
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
                    if (_progressContext.IsConfigured)
                    {
                        await _progressContext.ReportProgressAsync(fileName, fileSize, fileSize);
                    }

                    _logger.LogInformation("Upload complete (finalized after loop exit) | File: {FileName} | DriveFileId: {FileId}", fileName, driveFile?.Id);
                    return Result.Success(driveFile?.Id ?? "");
                }
                else
                {
                    _logger.LogWarning("Finalization request failed after loop exit: {StatusCode} | File: {FileName}", finalResponse.StatusCode, fileName);
                }
            }

            return Result<string>.Failure("UPLOAD_INCOMPLETE", "Upload did not complete successfully");
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

        private class GoogleDriveCredentials
        {
            [System.Text.Json.Serialization.JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("expires_at")]
            public string? ExpiresAt { get; set; }
        }

        private class TokenRefreshResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private class FolderResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        private class FileUploadResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }
    }
}
