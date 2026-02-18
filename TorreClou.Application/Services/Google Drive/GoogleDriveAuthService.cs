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
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;

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

        public async Task<SavedCredentialsDto> SaveCredentialsAsync(int userId, SaveGoogleDriveCredentialsRequestDto request)
        {
            StorageProfileValidator.ValidateGoogleDriveCredentials(
                request.ClientId, request.ClientSecret, request.RedirectUri);

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
                return new SavedCredentialsDto
                {
                    CredentialId = userCredentials.Id,
                    CredentialName = finalName,
                };
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
            return new SavedCredentialsDto
            {
                CredentialId = userOAuthCredential.Id,
                CredentialName = finalName,
            };
        }

        public async Task<List<OAuthCredentialDto>> GetCredentialsAsync(int userId)
        {
            var credentials = await OAuthService.GetAll(userId);
            return credentials.Select(c => new OAuthCredentialDto
            {
                Id = c.Id,
                Name = c.Name,
                ClientIdMasked = MaskClientId(c.ClientId),
                RedirectUri = c.RedirectUri,
                CreatedAt = c.CreatedAt
            }).ToList();
        }

        public async Task<string> ConnectAsync(int userId, ConnectGoogleDriveRequestDto request)
        {
            var credential = await OAuthService.GetUserOAuthCredentialById(request.CredentialId, userId);

            if (credential == null)
                throw new NotFoundException("CredentialNotFound", "OAuth credential not found");

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
            return authUrl;
        }

        public async Task<string> ReauthenticateAsync(int userId, int profileId)
        {
            var profile = await profilesService.GetProfileBySpecAsync(userId, profileId);
            if (profile == null)
                throw new NotFoundException("ProfileNotFound", "Storage profile not found");

            if (profile.ProviderType != StorageProviderType.GoogleDrive)
                throw new ValidationException("InvalidProfile", "Profile is not a Google Drive profile");

            if (!profile.OAuthCredentialId.HasValue)
                throw new BusinessRuleException("MissingCredentials", "Profile has no linked OAuth credentials. Please connect using a saved credential.");

            var credential = await OAuthService.GetUserOAuthCredentialById(profile.OAuthCredentialId.Value, userId);
            if (credential == null)
                throw new NotFoundException("CredentialNotFound", "Linked OAuth credential not found");

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
            return authUrl;
        }

        public async Task<int> HandleOAuthCallbackAsync(string code, string state)
        {
            var storedState = await oauthStateService.ConsumeStateAsync<OAuthState>(state, RedisKeyPrefixConfigure);
            if (storedState == null)
                throw new ValidationException("InvalidState", "Invalid or expired OAuth state. Please start the authentication flow again.");

            if (storedState.ExpiresAt < DateTime.UtcNow)
                throw new ValidationException("InvalidState", "Expired OAuth state");

            var userId = storedState.UserId;

            var user = await userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                logger.LogWarning("OAuth callback attempted for non-existent user {UserId}", userId);
                throw new NotFoundException("UserNotFound", "User not found. Please log in and try again.");
            }

            var credential = await OAuthService.GetUserOAuthCredentialById(storedState.OAuthCredentialId, userId);
            if (credential == null)
                throw new NotFoundException("CredentialNotFound", "OAuth credential not found. Please save credentials and try again.");

            logger.LogInformation("Processing OAuth callback for user {UserId}, profileId={ProfileId}, credentialId={CredentialId}",
                userId, storedState.ProfileId, storedState.OAuthCredentialId);

            // Exchange code for tokens (throws ExternalServiceException on failure)
            var tokenResponse = await googleApiClient.ExchangeCodeForTokensAsync(
                code, credential.ClientId, credential.ClientSecret, credential.RedirectUri);

            if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                logger.LogCritical("OAuth token exchange succeeded but no refresh token received for user {UserId}.", userId);
            else
                logger.LogInformation("Refresh token received successfully for user {UserId}", userId);

            string? email = null;
            try
            {
                var userInfo = await googleApiClient.GetUserInfoAsync(tokenResponse.AccessToken);
                if (!string.IsNullOrEmpty(userInfo.Email))
                    email = userInfo.Email;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch user info for user {UserId}, continuing without email", userId);
            }

            UserStorageProfile profile;

            if (storedState.ProfileId.HasValue)
            {
                var existingProfile = await profilesService.GetProfileBySpecAsync(userId, storedState.ProfileId.Value, activeOnly: false);
                if (existingProfile == null)
                    throw new NotFoundException("ProfileNotFound", "The profile associated with this authentication flow no longer exists.");

                if (!string.IsNullOrEmpty(email))
                {
                    var hasDuplicate = await profilesService.HasDuplicateEmailAsync(userId, existingProfile.Id, email);
                    if (hasDuplicate)
                        throw new ConflictException("DuplicateEmail", $"This Google account ({email}) is already connected to another profile.");
                }

                profile = existingProfile;
                profile.IsActive = true;
                profile.NeedsReauth = false;
                profile.Email = email ?? profile.Email;
                profile.OAuthCredentialId = credential.Id;

                logger.LogInformation("Updating profile {ProfileId} with new tokens for user {UserId}", profile.Id, userId);
            }
            else
            {
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

            if (!storedState.ProfileId.HasValue)
            {
                var hasDefault = await profilesService.HasDefaultProfile(userId);

                if (!hasDefault || storedState.SetAsDefault)
                {
                    if (storedState.SetAsDefault && hasDefault)
                        await profilesService.SetDefaultProfileAsync(userId, profile.Id);
                    else
                        profile.IsDefault = true;
                }
            }

            await profilesService.Save();

            logger.LogInformation("Google Drive OAuth completed for user {UserId}, profile {ProfileId}", userId, profile.Id);
            return profile.Id;
        }

        private static string MaskClientId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId) || clientId.Length < 10)
                return "****";

            return $"{clientId[..4]}...{clientId[^30..]}";
        }
    }
}
