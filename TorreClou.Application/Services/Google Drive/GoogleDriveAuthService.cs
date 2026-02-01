using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Core.Options;
using TorreClou.Core.DTOs.Storage.Google_Drive;
using TorreClou.Core.DTOs.Storage.GoogleDrive;

namespace TorreClou.Application.Services
{
    public class GoogleDriveAuthService(
        IUnitOfWork unitOfWork,
        IOptions<GoogleDriveSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDriveAuthService> logger,
        IRedisCacheService redisCache) : IGoogleDriveAuthService
    {
        private readonly GoogleDriveSettings _settings = settings.Value;
        private const string RedisKeyPrefix = "oauth:state:";
        private const string RedisKeyPrefixConfigure = "oauth:gdrive:state:";

        public async Task<Result<string>> GetAuthorizationUrlAsync(int userId, string? profileName = null)
        {
            try
            {
                // Validate profile name if provided
                if (!string.IsNullOrWhiteSpace(profileName))
                {
                    var validationResult = ValidateProfileName(profileName);
                    if (validationResult.IsFailure)
                    {
                        return Result<string>.Failure(validationResult.Error.Code, validationResult.Error.Message);
                    }
                    profileName = profileName.Trim();
                }

                // Generate state parameter (userId + nonce for security)
                var nonce = Guid.NewGuid().ToString("N");
                var state = $"{userId}:{nonce}";
                var stateHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

                // Store state in Redis with expiration (5 minutes)
                var oauthState = new OAuthState
                {
                    UserId = userId,
                    Nonce = nonce,
                    ProfileName = profileName,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                };

                var redisKey = $"{RedisKeyPrefix}{stateHash}";
                var jsonValue = JsonSerializer.Serialize(oauthState);
                
                await redisCache.SetAsync(redisKey, jsonValue, TimeSpan.FromMinutes(5));
              
           

                // Build OAuth URL
                var scopes = string.Join(" ", _settings.Scopes);
                var redirectUri = HttpUtility.UrlEncode(_settings.RedirectUri);
                var encodedState = HttpUtility.UrlEncode(stateHash);

                var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                    $"client_id={_settings.ClientId}&" +
                    $"redirect_uri={redirectUri}&" +
                    $"response_type=code&" +
                    $"scope={HttpUtility.UrlEncode(scopes)}&" +
                    $"access_type=offline&" +
                    $"prompt=consent&" +
                    $"state={encodedState}";

                return Result.Success(authUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating authorization URL for user {UserId}", userId);
                return Result<string>.Failure("AUTH_URL_ERROR", "Failed to generate authorization URL");
            }
        }

        public async Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request)
        {
            try
            {
                // Validate credentials format
                if (string.IsNullOrEmpty(request.ClientId) || !request.ClientId.Contains(".apps.googleusercontent.com"))
                {
                    return Result<string>.Failure("INVALID_CLIENT_ID", "Invalid Google Client ID format. It should end with .apps.googleusercontent.com");
                }

                if (string.IsNullOrEmpty(request.ClientSecret))
                {
                    return Result<string>.Failure("INVALID_CLIENT_SECRET", "Client Secret is required");
                }

                if (string.IsNullOrEmpty(request.RedirectUri))
                {
                    return Result<string>.Failure("INVALID_REDIRECT_URI", "Redirect URI is required");
                }

                // Validate profile name if provided
                if (!string.IsNullOrWhiteSpace(request.ProfileName))
                {
                    var validationResult = ValidateProfileName(request.ProfileName);
                    if (validationResult.IsFailure)
                    {
                        return Result<string>.Failure(validationResult.Error.Code, validationResult.Error.Message);
                    }
                }

                // Generate state with user's credentials
                var nonce = Guid.NewGuid().ToString("N");
                var state = $"{userId}:{nonce}";
                var stateHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

                var oauthState = new OAuthState
                {
                    UserId = userId,
                    Nonce = nonce,
                    ProfileName = request.ProfileName?.Trim(),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10), // Longer expiry for user to complete OAuth
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    RedirectUri = request.RedirectUri,
                    SetAsDefault = request.SetAsDefault
                };

                var redisKey = $"{RedisKeyPrefixConfigure}{stateHash}";
                await redisCache.SetAsync(redisKey, JsonSerializer.Serialize(oauthState), TimeSpan.FromMinutes(10));

                // Build authorization URL using USER'S credentials
                var scopes = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/userinfo.email";
                var encodedState = HttpUtility.UrlEncode(stateHash);

                var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                    $"client_id={HttpUtility.UrlEncode(request.ClientId)}&" +
                    $"redirect_uri={HttpUtility.UrlEncode(request.RedirectUri)}&" +
                    $"response_type=code&" +
                    $"scope={HttpUtility.UrlEncode(scopes)}&" +
                    $"access_type=offline&" +
                    $"prompt=consent&" +
                    $"state={encodedState}";

                logger.LogInformation("Generated OAuth URL for user {UserId} with user-provided credentials", userId);
                return Result.Success(authUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating authorization URL with user credentials for user {UserId}", userId);
                return Result<string>.Failure("AUTH_URL_ERROR", "Failed to generate authorization URL");
            }
        }

        public async Task<Result<int>> HandleOAuthCallbackAsync(string code, string state)
        {
            try
            {
                // Try new configure flow first, then legacy flow
                var configureKey = $"{RedisKeyPrefixConfigure}{state}";
                var legacyKey = $"{RedisKeyPrefix}{state}";

                OAuthState? storedState = null;
                bool isConfigureFlow = false;

                // Try configure flow first (user-provided credentials)
                var stateJson = await redisCache.GetAndDeleteAsync(configureKey);
                if (!string.IsNullOrEmpty(stateJson))
                {
                    isConfigureFlow = true;
                }
                else
                {
                    // Try legacy flow (environment credentials)
                    stateJson = await redisCache.GetAndDeleteAsync(legacyKey);
                }

                if (string.IsNullOrEmpty(stateJson))
                {
                    return Result<int>.Failure("INVALID_STATE", "Invalid or expired OAuth state");
                }

                storedState = JsonSerializer.Deserialize<OAuthState>(stateJson);

                if (storedState == null)
                {
                    return Result<int>.Failure("INVALID_STATE", "Invalid OAuth state format");
                }

                // Validate expiration
                if (storedState.ExpiresAt < DateTime.UtcNow)
                {
                    return Result<int>.Failure("INVALID_STATE", "Expired OAuth state");
                }

                // Extract userId and profileName from validated state
                var userId = storedState.UserId;
                var profileName = storedState.ProfileName;

                // Determine which credentials to use for token exchange
                string clientId, clientSecret, redirectUri;
                if (isConfigureFlow && !string.IsNullOrEmpty(storedState.ClientId))
                {
                    // Use user-provided credentials from configure flow
                    clientId = storedState.ClientId;
                    clientSecret = storedState.ClientSecret;
                    redirectUri = storedState.RedirectUri;
                    logger.LogInformation("Using user-provided credentials for token exchange (user {UserId})", userId);
                }
                else
                {
                    // Use environment credentials (legacy flow)
                    clientId = _settings.ClientId;
                    clientSecret = _settings.ClientSecret;
                    redirectUri = _settings.RedirectUri;
                    logger.LogInformation("Using environment credentials for token exchange (user {UserId})", userId);
                }

                // Exchange authorization code for tokens
                var tokenResponse = await ExchangeCodeForTokensAsync(code, clientId, clientSecret, redirectUri);
                if (tokenResponse == null)
                {
                    return Result<int>.Failure("TOKEN_EXCHANGE_FAILED", "Failed to exchange authorization code for tokens");
                }

                // Validate that refresh token is present (critical for long-running jobs)
                if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    logger.LogCritical("OAuth token exchange succeeded but no refresh token received for user {UserId}. This may cause authentication issues for long-running jobs.", userId);
                    // Note: Google only returns refresh token on first authorization or when prompt=consent is used
                    // We already have prompt=consent in the auth URL, so this should not happen
                }
                else
                {
                    logger.LogInformation("Refresh token received successfully for user {UserId}", userId);
                }

                // Fetch user info (email) from Google
                var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken);
                string? email = null;
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.Email))
                {
                    email = userInfo.Email;
                }

                UserStorageProfile profile;
                bool isReactivation = false;

                // First, check for inactive profile with the same email (reactivation case)
                if (!string.IsNullOrEmpty(email))
                {
                    var inactiveProfileSpec = new BaseSpecification<UserStorageProfile>(
                        p => p.UserId == userId 
                            && p.ProviderType == StorageProviderType.GoogleDrive 
                            && !p.IsActive 
                            && p.Email != null 
                            && p.Email.ToLower() == email.ToLower()
                    );
                    var inactiveProfile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(inactiveProfileSpec);
                    
                    if (inactiveProfile != null)
                    {
                        // Reactivate existing profile
                        logger.LogInformation("Reactivating inactive profile {ProfileId} for user {UserId} with email {Email}", inactiveProfile.Id, userId, email);
                        profile = inactiveProfile;
                        profile.IsActive = true;
                        
                        // Update profile name if provided
                        if (!string.IsNullOrWhiteSpace(profileName))
                        {
                            profile.ProfileName = profileName.Trim();
                        }
                        
                        isReactivation = true;
                    }
                    else
                    {
                        // Check for active duplicate (prevent duplicates)
                        var duplicateSpec = new BaseSpecification<UserStorageProfile>(
                            p => p.UserId == userId 
                                && p.ProviderType == StorageProviderType.GoogleDrive 
                                && p.IsActive 
                                && p.Email != null 
                                && p.Email.ToLower() == email.ToLower()
                        );
                        var duplicateProfile = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(duplicateSpec);
                        
                        if (duplicateProfile != null)
                        {
                            logger.LogWarning("Attempt to connect duplicate email {Email} for user {UserId}", email, userId);
                            return Result<int>.Failure("DUPLICATE_EMAIL", $"This Google account ({email}) is already connected. Please use a different account.");
                        }

                        // Create new profile
                        var finalProfileName = !string.IsNullOrWhiteSpace(profileName) 
                            ? profileName.Trim() 
                            : (!string.IsNullOrEmpty(email) 
                                ? $"Google Drive ({email})" 
                                : "My Google Drive");

                        profile = new UserStorageProfile
                        {
                            UserId = userId,
                            ProfileName = finalProfileName,
                            ProviderType = StorageProviderType.GoogleDrive,
                            Email = email,
                            IsDefault = false,
                            IsActive = true
                        };
                        unitOfWork.Repository<UserStorageProfile>().Add(profile);
                    }
                }
                else
                {
                    // No email available - create new profile
                    var finalProfileName = !string.IsNullOrWhiteSpace(profileName) 
                        ? profileName.Trim() 
                        : "My Google Drive";

                    profile = new UserStorageProfile
                    {
                        UserId = userId,
                        ProfileName = finalProfileName,
                        ProviderType = StorageProviderType.GoogleDrive,
                        Email = email,
                        IsDefault = false,
                        IsActive = true
                    };
                    unitOfWork.Repository<UserStorageProfile>().Add(profile);
                }

                // Store/update tokens in CredentialsJson (include OAuth app credentials for configure flow)
                var credentials = new GoogleDriveCredentials
                {
                    // OAuth app credentials (only for configure flow)
                    ClientId = isConfigureFlow ? clientId : string.Empty,
                    ClientSecret = isConfigureFlow ? clientSecret : string.Empty,
                    RedirectUri = isConfigureFlow ? redirectUri : string.Empty,
                    // OAuth tokens from Google
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn).ToString("O")
                };

                profile.CredentialsJson = JsonSerializer.Serialize(credentials);

                // Validate refresh token was saved
                if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    logger.LogError("CRITICAL: Refresh token is missing from token response. Profile {ProfileId} will not be able to refresh tokens automatically.", profile.Id);
                }
                else
                {
                    logger.LogInformation("Credentials saved successfully for profile {ProfileId} with refresh token present. Configure flow: {IsConfigureFlow}", profile.Id, isConfigureFlow);
                }

                // If this is the first profile or user explicitly wants it as default, set it
                var defaultProfileSpec = new BaseSpecification<UserStorageProfile>(
                    p => p.UserId == userId && p.IsDefault && p.IsActive
                );
                var hasDefault = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(defaultProfileSpec) != null;

                if (!hasDefault || (isConfigureFlow && storedState.SetAsDefault))
                {
                    // Unset other defaults if setting this one as default
                    if (storedState.SetAsDefault && hasDefault)
                    {
                        var allProfilesSpec = new BaseSpecification<UserStorageProfile>(p => p.UserId == userId && p.IsActive);
                        var allProfiles = await unitOfWork.Repository<UserStorageProfile>().ListAsync(allProfilesSpec);
                        foreach (var p in allProfiles)
                            p.IsDefault = false;
                    }
                    profile.IsDefault = true;
                }

                await unitOfWork.Complete();

                if (isReactivation)
                {
                    logger.LogInformation("Google Drive reactivated successfully for user {UserId}, profile {ProfileId}", userId, profile.Id);
                }
                else
                {
                    logger.LogInformation("Google Drive connected successfully for user {UserId}, profile {ProfileId}", userId, profile.Id);
                }

                return Result.Success(profile.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling OAuth callback");
                return Result<int>.Failure("OAUTH_CALLBACK_ERROR", "Failed to complete OAuth flow");
            }
        }

        private async Task<TokenResponse?> ExchangeCodeForTokensAsync(
            string code,
            string clientId,
            string clientSecret,
            string redirectUri)
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                var requestBody = new Dictionary<string, string>
                {
                    { "code", code },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "redirect_uri", redirectUri },
                    { "grant_type", "authorization_code" }
                };

                var content = new FormUrlEncodedContent(requestBody);
                var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogError("Token exchange failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);
                
                // Log token exchange result
                if (tokenResponse != null)
                {
                    if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    {
                        logger.LogWarning("Token exchange response missing refresh_token. Access token expires in {ExpiresIn} seconds.", tokenResponse.ExpiresIn);
                    }
                    else
                    {
                        logger.LogInformation("Token exchange successful. Access token expires in {ExpiresIn} seconds. Refresh token present.", tokenResponse.ExpiresIn);
                    }
                }
                
                return tokenResponse;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception during token exchange");
                return null;
            }
        }

        private async Task<UserInfoResponse?> GetUserInfoAsync(string accessToken)
        {
            // Validate access token
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                logger.LogWarning("Access token is empty, skipping user info fetch");
                return null;
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient();
                
                // Clear any existing headers to avoid conflicts
                httpClient.DefaultRequestHeaders.Clear();
                
                // Set Authorization header with Bearer token
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Trim());

                // Use the correct Google UserInfo API endpoint
                var response = await httpClient.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogCritical(
                        "Failed to fetch user info: {StatusCode} - {Error}. Access token length: {TokenLength}", 
                        response.StatusCode, 
                        errorContent,
                        accessToken.Length);
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<UserInfoResponse>(jsonResponse);
                
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.Email))
                {
                    logger.LogInformation("Successfully fetched user email: {Email}", userInfo.Email);
                }
                
                return userInfo;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exception while fetching user info (non-critical). Access token present: {HasToken}", !string.IsNullOrWhiteSpace(accessToken));
                return null; // Non-critical, continue without email
            }
        }

        private Result ValidateProfileName(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return Result.Failure("INVALID_PROFILE_NAME", "Profile name cannot be empty");
            }

            var trimmed = profileName.Trim();

            if (trimmed.Length < 3)
            {
                return Result.Failure("PROFILE_NAME_TOO_SHORT", "Profile name must be at least 3 characters");
            }

            if (trimmed.Length > 50)
            {
                return Result.Failure("PROFILE_NAME_TOO_LONG", "Profile name must be at most 50 characters");
            }

            // Allow alphanumeric, spaces, hyphens, underscores
            var allowedPattern = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9\s\-_]+$");
            if (!allowedPattern.IsMatch(trimmed))
            {
                return Result.Failure("INVALID_PROFILE_NAME", "Profile name can only contain letters, numbers, spaces, hyphens, and underscores");
            }

            return Result.Success();
        }

    }
}

