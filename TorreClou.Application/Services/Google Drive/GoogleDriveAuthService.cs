using Microsoft.Extensions.Logging;
using System.Text.Json;
using TorreClou.Application.Services.OAuth;
using TorreClou.Application.Validators;
using TorreClou.Core.DTOs.OAuth;
using TorreClou.Core.DTOs.Storage.Google_Drive;
using TorreClou.Core.DTOs.Storage.GoogleDrive;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
namespace TorreClou.Application.Services
{
    public class GoogleDriveAuthService(
        IOAuthStateService oauthStateService,
        IGoogleApiClient googleApiClient,
        IOAuthService OAuthService,
        IStorageProfilesService profilesService,
        IUserService userService,
        ILogger<GoogleDriveAuthService> logger) : IGoogleDriveAuthService
    {
        private const string RedisKeyPrefixConfigure = "oauth:gdrive:state:";


        public async Task<Result<SavedCredentialsDto>> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request)
        {
            try
            {
                var credentialValidation = StorageProfileValidator.ValidateGoogleDriveCredentials(
                    request.ClientId, request.ClientSecret, request.RedirectUri);
                if (credentialValidation.IsFailure)
                    return Result<SavedCredentialsDto>.Failure(credentialValidation.Error);

                var finalName = !string.IsNullOrWhiteSpace(request.Name)
                    ? request.Name.Trim()
                    : "My GCP Credentials";

                var userCredentials = await OAuthService.GetUserOAuthCredentialByClientId(request.ClientId, userId);

                if (userCredentials != null)
                {
                    userCredentials.Name = finalName;
                    userCredentials.ClientSecret = request.ClientSecret;
                    userCredentials.RedirectUri = request.RedirectUri;
                    await OAuthService.Update(userCredentials);

                    logger.LogInformation("Updated OAuth credential {CredentialId} for user {UserId}", userCredentials.Id, userId);
                    return Result.Success(new SavedCredentialsDto()
                    {
                        CredentialId = userCredentials.Id,
                        CredentialName = finalName,

                    });
                }

                var credential = new UserOAuthCredential
                {
                    UserId = userId,
                    Name = finalName,
                    ClientId = request.ClientId,
                    ClientSecret = request.ClientSecret,
                    RedirectUri = request.RedirectUri
                };

                var userOAuthCredential = await OAuthService.Add(credential);

                logger.LogInformation("Saved new OAuth credential {CredentialId} for user {UserId}", userOAuthCredential.Id, userId);
                return Result.Success(new SavedCredentialsDto()
                {
                    CredentialId = userOAuthCredential.Id,
                    CredentialName = finalName,
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving OAuth credentials for user {UserId}", userId);
                return Result<SavedCredentialsDto>.Failure(ErrorCode.UnexpectedError, "Failed to save credentials");
            }
        }


        public async Task<Result<List<OAuthCredentialDto>>> GetCredentialsAsync(int userId)
        {
            try
            {
                var credentials = await OAuthService.GetAll(userId);
                var dtos = credentials.Select(c => new OAuthCredentialDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ClientIdMasked = MaskClientId(c.ClientId),
                    RedirectUri = c.RedirectUri,
                    CreatedAt = c.CreatedAt
                }).ToList();

                return Result.Success(dtos);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing OAuth credentials for user {UserId}", userId);
                return Result<List<OAuthCredentialDto>>.Failure(ErrorCode.UnexpectedError, "Failed to list credentials");
            }
        }

        public async Task<Result<string>> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request)
        {
            try
            {
                // Load the credential
                var credential = await OAuthService.GetUserOAuthCredentialById(request.CredentialId, userId);

                if (credential == null)
                    return Result<string>.Failure(ErrorCode.CredentialNotFound, "OAuth credential not found");

                var profileName = !string.IsNullOrWhiteSpace(request.ProfileName)
                    ? request.ProfileName.Trim()
                    : "My Google Drive";

                var profile = new UserStorageProfile
                {
                    UserId = userId,
                    ProfileName = profileName,
                    ProviderType = StorageProviderType.GoogleDrive,
                    Email = null,
                    CredentialsJson = JsonSerializer.Serialize(new GoogleDriveCredentials()),
                    IsDefault = false,
                    IsActive = true,
                    NeedsReauth = false,
                    OAuthCredentialId = credential.Id
                };
                await profilesService.Add(profile);

                // Handle SetAsDefault
                if (request.SetAsDefault)
                {
                    await profilesService.SetDefaultProfileAsync(userId, profile.Id);
                }
                else
                {

                    var hasDefault = await profilesService.HasDefaultProfile(userId);
                    if (!hasDefault)
                        profile.IsDefault = true;

                }
                await profilesService.Save();



                // Build OAuth state referencing credentialId
                var oauthState = new OAuthState
                {
                    UserId = userId,
                    ProfileId = profile.Id,
                    ProfileName = profileName,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    OAuthCredentialId = credential.Id,
                    SetAsDefault = profile.IsDefault
                };

                var stateHash = await oauthStateService.GenerateStateAsync(
                    oauthState, RedisKeyPrefixConfigure, TimeSpan.FromMinutes(10));

                var authUrl = GoogleOAuthUrlBuilder.BuildAuthorizationUrl(
                    credential.ClientId, credential.RedirectUri, stateHash);

                logger.LogInformation("Created profile {ProfileId} and started OAuth for user {UserId} with credential {CredentialId}",
                    profile.Id, userId, credential.Id);
                return Result.Success(authUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error connecting Google Drive for user {UserId}", userId);
                return Result<string>.Failure(ErrorCode.AuthUrlError, "Failed to start connection flow");
            }
        }

        public async Task<Result<string>> ReauthenticateAsync(int userId, int profileId)
        {
            try
            {
                var profile = await profilesService.GetProfileBySpecAsync(userId, profileId);
                if (profile == null)
                    return Result<string>.Failure(ErrorCode.ProfileNotFound, "Storage profile not found");

                if (profile.ProviderType != StorageProviderType.GoogleDrive)
                    return Result<string>.Failure(ErrorCode.InvalidProfile, "Profile is not a Google Drive profile");

                if (!profile.OAuthCredentialId.HasValue)
                    return Result<string>.Failure(ErrorCode.MissingCredentials, "Profile has no linked OAuth credentials. Please connect using a saved credential.");

                // Load the linked credential
                var credential = await OAuthService.GetUserOAuthCredentialById(profile.OAuthCredentialId.Value, userId);
                if (credential == null)
                    return Result<string>.Failure(ErrorCode.CredentialNotFound, "Linked OAuth credential not found");

                var oauthState = new OAuthState
                {
                    UserId = userId,
                    ProfileId = profileId,
                    ProfileName = profile.ProfileName,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    OAuthCredentialId = credential.Id,
                    SetAsDefault = profile.IsDefault
                };

                var stateHash = await oauthStateService.GenerateStateAsync(
                    oauthState, RedisKeyPrefixConfigure, TimeSpan.FromMinutes(10));

                var authUrl = GoogleOAuthUrlBuilder.BuildAuthorizationUrl(
                    credential.ClientId, credential.RedirectUri, stateHash);

                logger.LogInformation("Generated re-auth OAuth URL for profile {ProfileId} for user {UserId}", profileId, userId);
                return Result.Success(authUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initiating re-auth for profile {ProfileId}, user {UserId}", profileId, userId);
                return Result<string>.Failure(ErrorCode.AuthUrlError, "Failed to generate authorization URL");
            }
        }

        public async Task<Result<int>> HandleOAuthCallbackAsync(string code, string state)
        {
            try
            {
                var storedState = await oauthStateService.ConsumeStateAsync<OAuthState>(state, RedisKeyPrefixConfigure);
                if (storedState == null)
                    return Result<int>.Failure(ErrorCode.InvalidState, "Invalid or expired OAuth state. Please start the authentication flow again.");

                if (storedState.ExpiresAt < DateTime.UtcNow)
                    return Result<int>.Failure(ErrorCode.InvalidState, "Expired OAuth state");

                var userId = storedState.UserId;

                // Verify user exists
                var user = await userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    logger.LogWarning("OAuth callback attempted for non-existent user {UserId}", userId);
                    return Result<int>.Failure(ErrorCode.UserNotFound, "User not found. Please log in and try again.");
                }

                // Load the credential from DB by OAuthCredentialId
                var credential = await OAuthService.GetUserOAuthCredentialById(storedState.OAuthCredentialId, userId);
                if (credential == null)
                    return Result<int>.Failure(ErrorCode.CredentialNotFound, "OAuth credential not found. Please save credentials and try again.");

                var clientId = credential.ClientId;
                var clientSecret = credential.ClientSecret;
                var redirectUri = credential.RedirectUri;

                logger.LogInformation("Processing OAuth callback for user {UserId}, profileId={ProfileId}, credentialId={CredentialId}",
                    userId, storedState.ProfileId, storedState.OAuthCredentialId);

                // Exchange authorization code for tokens
                var tokenResult = await googleApiClient.ExchangeCodeForTokensAsync(code, clientId, clientSecret, redirectUri);
                if (tokenResult.IsFailure)
                    return Result<int>.Failure(tokenResult.Error);

                var tokenResponse = tokenResult.Value;

                if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    logger.LogCritical("OAuth token exchange succeeded but no refresh token received for user {UserId}.", userId);
                else
                    logger.LogInformation("Refresh token received successfully for user {UserId}", userId);

                // Fetch user info (email) from Google
                string? email = null;
                var userInfoResult = await googleApiClient.GetUserInfoAsync(tokenResponse.AccessToken);
                if (userInfoResult.IsSuccess && !string.IsNullOrEmpty(userInfoResult.Value.Email))
                    email = userInfoResult.Value.Email;

                UserStorageProfile profile;

                if (storedState.ProfileId.HasValue)
                {
                    // Load existing profile (created in ConnectAsync or existing for re-auth)
                    var existingProfile = await profilesService.GetProfileBySpecAsync(userId, storedState.ProfileId.Value, activeOnly: false);
                    if (existingProfile == null)
                        return Result<int>.Failure(ErrorCode.ProfileNotFound, "The profile associated with this authentication flow no longer exists.");

                    // Check for duplicate email on OTHER profiles
                    if (!string.IsNullOrEmpty(email))
                    {
                        var hasDuplicate = await profilesService.HasDuplicateEmailAsync(userId, existingProfile.Id, email);
                        if (hasDuplicate)
                            return Result<int>.Failure(ErrorCode.DuplicateEmail, $"This Google account ({email}) is already connected to another profile.");
                    }

                    profile = existingProfile;
                    profile.IsActive = true;
                    profile.NeedsReauth = false;
                    profile.Email = email ?? profile.Email;
                    profile.OAuthCredentialId = credential.Id; // ensure link

                    logger.LogInformation("Updating profile {ProfileId} with new tokens for user {UserId}", profile.Id, userId);
                }
                else
                {
                    // Fallback: no ProfileId in state (should not happen with new flow)
                    var profileName = !string.IsNullOrWhiteSpace(storedState.ProfileName)
                        ? storedState.ProfileName.Trim()
                        : !string.IsNullOrEmpty(email) ? $"Google Drive ({email})" : "My Google Drive";

                    profile = new UserStorageProfile
                    {
                        UserId = userId,
                        ProfileName = profileName,
                        ProviderType = StorageProviderType.GoogleDrive,
                        Email = email,
                        IsDefault = false,
                        IsActive = true,
                        NeedsReauth = false,
                        OAuthCredentialId = credential.Id
                    };
                    await profilesService.AddWithoutSaveAsync(profile);
                }

                // Store only tokens in CredentialsJson (app creds live in UserOAuthCredential)
                var credentials = new GoogleDriveCredentials
                {
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn).ToString("O")
                };

                profile.CredentialsJson = JsonSerializer.Serialize(credentials);

                if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                    logger.LogError("CRITICAL: Refresh token is missing from token response. Profile {ProfileId} will not be able to refresh tokens automatically.", profile.Id);
                else
                    logger.LogInformation("Credentials saved successfully for profile {ProfileId} with refresh token present.", profile.Id);

                // Handle default profile logic (only for new profiles without PreExisting ProfileId)
                if (!storedState.ProfileId.HasValue)
                {
                    var hasDefault = await profilesService.HasDefaultProfile(userId);

                    if (!hasDefault || storedState.SetAsDefault)
                    {
                        if (storedState.SetAsDefault && hasDefault)
                        {
                            await profilesService.SetDefaultProfileAsync(userId, profile.Id);
                        }
                        else
                        {
                            profile.IsDefault = true;
                        }
                    }
                }

                await profilesService.Save();

                logger.LogInformation("Google Drive OAuth completed for user {UserId}, profile {ProfileId}", userId, profile.Id);
                return Result.Success(profile.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling OAuth callback");
                return Result<int>.Failure(ErrorCode.OAuthCallbackError, "Failed to complete OAuth flow");
            }
        }

        private static string MaskClientId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId) || clientId.Length < 10)
                return "****";

            return $"{clientId[..4]}...{clientId[^30..]}";
        }
    }
}
