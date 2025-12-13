using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Options;

namespace TorreClou.Infrastructure.Services
{
    public class GoogleDriveService : IGoogleDriveService
    {
        private readonly GoogleDriveSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<GoogleDriveService> _logger;

        public GoogleDriveService(
            IOptions<GoogleDriveSettings> settings,
            IHttpClientFactory httpClientFactory,
            ILogger<GoogleDriveService> logger)
        {
            _settings = settings.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
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
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

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

        public async Task<Result<string>> UploadFileAsync(string filePath, string fileName, string folderId, string accessToken, CancellationToken cancellationToken = default)
        {
            try
            {
              

                if (!File.Exists(filePath))
                {
                    return Result<string>.Failure("FILE_NOT_FOUND", $"File not found: {filePath}");
                }

                var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                httpClient.Timeout = TimeSpan.FromHours(2); // Large files may take time
                httpClient.DefaultRequestHeaders.Add("Content-Disposition", "form-data; name=\"metadata\"");
                // Step 1: Create file metadata
                var metadata = new
                {
                    name = fileName,
                    parents = new[] { folderId }
                };

                var metadataJson = JsonSerializer.Serialize(metadata);
                var metadataContent = new StringContent(metadataJson, Encoding.UTF8, "application/json");

               

                // Step 2: Upload file using multipart upload
                using var fileStream = File.OpenRead(filePath);
                
               

                using var multipartContent = new MultipartFormDataContent();
                multipartContent.Add(metadataContent, "metadata");
                var streamContent = new StreamContent(fileStream);
                multipartContent.Add(streamContent, "file", fileName);

            

             

                var response = await httpClient.PostAsync(
                    "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name",
                    multipartContent,
                    cancellationToken);

        

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                    _logger.LogError("File upload failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<string>.Failure("UPLOAD_FAILED", $"Failed to upload file: {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var uploadResponse = JsonSerializer.Deserialize<FileUploadResponse>(responseJson);

                if (uploadResponse == null || string.IsNullOrEmpty(uploadResponse.Id))
                {
                    return Result<string>.Failure("INVALID_RESPONSE", "Invalid upload response");
                }

                _logger.LogInformation("File uploaded successfully: {FileName} -> {FileId}", fileName, uploadResponse.Id);
                return Result.Success(uploadResponse.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception uploading file: {FileName}", fileName);
                return Result<string>.Failure("UPLOAD_ERROR", $"Error uploading file: {ex.Message}");
            }
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

