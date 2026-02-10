using System.Text.RegularExpressions;
using TorreClou.Core.Enums;
using TorreClou.Core.Shared;

namespace TorreClou.Application.Validators
{
    public static partial class StorageProfileValidator
    {
        public static Result ValidateProfileName(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return Result.Failure(ErrorCode.InvalidProfileName, "Profile name cannot be empty");
            }

            var trimmed = profileName.Trim();

            if (trimmed.Length < 3)
            {
                return Result.Failure(ErrorCode.ProfileNameTooShort, "Profile name must be at least 3 characters");
            }

            if (trimmed.Length > 50)
            {
                return Result.Failure(ErrorCode.ProfileNameTooLong, "Profile name must be at most 50 characters");
            }

            if (!AllowedProfileNamePattern().IsMatch(trimmed))
            {
                return Result.Failure(ErrorCode.InvalidProfileName, "Profile name can only contain letters, numbers, spaces, hyphens, and underscores");
            }

            return Result.Success();
        }

        public static Result ValidateGoogleDriveCredentials(string? clientId, string? clientSecret, string? redirectUri)
        {
            if (string.IsNullOrEmpty(clientId) || !clientId.Contains(".apps.googleusercontent.com"))
            {
                return Result.Failure(ErrorCode.InvalidClientId, "Invalid Google Client ID format. It should end with .apps.googleusercontent.com");
            }

            if (string.IsNullOrEmpty(clientSecret))
            {
                return Result.Failure(ErrorCode.InvalidClientSecret, "Client Secret is required");
            }

            if (string.IsNullOrEmpty(redirectUri))
            {
                return Result.Failure(ErrorCode.InvalidRedirectUri, "Redirect URI is required");
            }

            return Result.Success();
        }

        [GeneratedRegex(@"^[a-zA-Z0-9\s\-_]+$")]
        private static partial Regex AllowedProfileNamePattern();
    }
}
