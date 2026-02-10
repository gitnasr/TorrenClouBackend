using System.Text.Json;
using Microsoft.Extensions.Logging;
using TorreClou.Application.Services.OAuth;
using TorreClou.Application.Validators;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;
using TorreClou.Core.DTOs.Storage.Google_Drive;
using TorreClou.Core.DTOs.Storage.GoogleDrive;

namespace TorreClou.Application.Services
{
    public class GoogleDriveAuthService(
        IUnitOfWork unitOfWork,
        IOAuthStateService oauthStateService,
        IGoogleApiClient googleApiClient,
        ILogger<GoogleDriveAuthService> logger) : IGoogleDriveAuthService
    {
        private const string RedisKeyPrefixConfigure = "oauth:gdrive:state:";

        public async Task<Result<string>> ConfigureAndGetAuthUrlAsync(int userId, ConfigureGoogleDriveRequestDto request)
        {
            try
            {
                // Validate credentials format
                var credentialValidation = StorageProfileValidator.ValidateGoogleDriveCredentials(
                    request.ClientId, request.ClientSecret, request.RedirectUri);
                if (credentialValidation.IsFailure)
                    return Result<string>.Failure(credentialValidation.Error);

                // Validate profile name if provided
                if (!string.IsNullOrWhiteSpace(request.ProfileName))
                {
                    var nameValidation = StorageProfileValidator.ValidateProfileName(request.ProfileName);
                    if (nameValidation.IsFailure)
                        return Result<string>.Failure(nameValidation.Error);
                }

                // Build OAuth state
                var oauthState = new OAuthState
                {
                    UserId = userId,
                    ProfileName = request.ProfileName?.Trim(),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    RedirectUri = request.RedirectUri,
                    SetAsDefault = request.SetAsDefault
                };

                // Generate state hash and store in Redis
                var stateHash = await oauthStateService.GenerateStateAsync(
                    oauthState, RedisKeyPrefixConfigure, TimeSpan.FromMinutes(10));

                // Build authorization URL
                var authUrl = GoogleOAuthUrlBuilder.BuildAuthorizationUrl(
                    request.ClientId, request.RedirectUri, stateHash);

                logger.LogInformation("Generated OAuth URL for user {UserId} with user-provided credentials", userId);
                return Result.Success(authUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating authorization URL with user credentials for user {UserId}", userId);
                return Result<string>.Failure(ErrorCode.AuthUrlError, "Failed to generate authorization URL");
            }
        }

        public async Task<Result<int>> HandleOAuthCallbackAsync(string code, string state)
        {
            try
            {
                // Consume OAuth state from Redis (atomically retrieves and deletes)
                var storedState = await oauthStateService.ConsumeStateAsync<OAuthState>(state, RedisKeyPrefixConfigure);

                if (storedState == null)
                {
                    return Result<int>.Failure(ErrorCode.InvalidState, "Invalid or expired OAuth state. Please use the /api/storage/gdrive/configure endpoint to connect Google Drive.");
                }

                // Validate expiration
                if (storedState.ExpiresAt < DateTime.UtcNow)
                {
                    return Result<int>.Failure(ErrorCode.InvalidState, "Expired OAuth state");
                }

                // Validate credentials are present in state (required - no env fallback)
                if (string.IsNullOrEmpty(storedState.ClientId) || string.IsNullOrEmpty(storedState.ClientSecret) || string.IsNullOrEmpty(storedState.RedirectUri))
                {
                    return Result<int>.Failure(ErrorCode.MissingCredentials, "OAuth credentials not found. Please use the /api/storage/gdrive/configure endpoint.");
                }

                // Extract userId and profileName from validated state
                var userId = storedState.UserId;
                var profileName = storedState.ProfileName;

                // Verify the user exists before creating a storage profile
                var user = await unitOfWork.Repository<Core.Entities.User>().GetByIdAsync(userId);
                if (user == null)
                {
                    logger.LogWarning("OAuth callback attempted for non-existent user {UserId}", userId);
                    return Result<int>.Failure(ErrorCode.UserNotFound, "User not found. Please log in and try again.");
                }

                // Use user-provided credentials from configure flow
                var clientId = storedState.ClientId;
                var clientSecret = storedState.ClientSecret;
                var redirectUri = storedState.RedirectUri;
                logger.LogInformation("Using user-provided credentials for token exchange (user {UserId})", userId);

                // Exchange authorization code for tokens via GoogleApiClient
                var tokenResult = await googleApiClient.ExchangeCodeForTokensAsync(code, clientId, clientSecret, redirectUri);
                if (tokenResult.IsFailure)
                {
                    return Result<int>.Failure(tokenResult.Error);
                }

                var tokenResponse = tokenResult.Value;

                // Validate that refresh token is present (critical for long-running jobs)
                if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    logger.LogCritical("OAuth token exchange succeeded but no refresh token received for user {UserId}. This may cause authentication issues for long-running jobs.", userId);
                }
                else
                {
                    logger.LogInformation("Refresh token received successfully for user {UserId}", userId);
                }

                // Fetch user info (email) from Google via GoogleApiClient
                string? email = null;
                var userInfoResult = await googleApiClient.GetUserInfoAsync(tokenResponse.AccessToken);
                if (userInfoResult.IsSuccess && !string.IsNullOrEmpty(userInfoResult.Value.Email))
                {
                    email = userInfoResult.Value.Email;
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
                            return Result<int>.Failure(ErrorCode.DuplicateEmail, $"This Google account ({email}) is already connected. Please use a different account.");
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

                // Store/update tokens in CredentialsJson (includes OAuth app credentials from user)
                var credentials = new GoogleDriveCredentials
                {
                    // OAuth app credentials (user-provided via /configure endpoint)
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    RedirectUri = redirectUri,
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
                    logger.LogInformation("Credentials saved successfully for profile {ProfileId} with refresh token present.", profile.Id);
                }

                // If this is the first profile or user explicitly wants it as default, set it
                var defaultProfileSpec = new BaseSpecification<UserStorageProfile>(
                    p => p.UserId == userId && p.IsDefault && p.IsActive
                );
                var hasDefault = await unitOfWork.Repository<UserStorageProfile>().GetEntityWithSpec(defaultProfileSpec) != null;

                if (!hasDefault || storedState.SetAsDefault)
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
                return Result<int>.Failure(ErrorCode.OAuthCallbackError, "Failed to complete OAuth flow");
            }
        }
    }
}
