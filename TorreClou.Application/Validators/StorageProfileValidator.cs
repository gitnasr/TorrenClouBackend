using System.Text.RegularExpressions;
using TorreClou.Core.Exceptions;

namespace TorreClou.Application.Validators
{
    public static partial class StorageProfileValidator
    {
        public static void ValidateProfileName(string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ValidationException("InvalidProfileName", "Profile name cannot be empty");

            var trimmed = profileName.Trim();

            if (trimmed.Length < 3)
                throw new ValidationException("ProfileNameTooShort", "Profile name must be at least 3 characters");

            if (trimmed.Length > 50)
                throw new ValidationException("ProfileNameTooLong", "Profile name must be at most 50 characters");

            if (!AllowedProfileNamePattern().IsMatch(trimmed))
                throw new ValidationException("InvalidProfileName", "Profile name can only contain letters, numbers, spaces, hyphens, and underscores");
        }

        public static void ValidateGoogleDriveCredentials(string? clientId, string? clientSecret, string? redirectUri)
        {
            if (string.IsNullOrEmpty(clientId) || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("InvalidClientId", "Invalid Google Client ID format. It should end with .apps.googleusercontent.com");

            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new ValidationException("InvalidClientSecret", "Client Secret is required");

            if (string.IsNullOrWhiteSpace(redirectUri))
                throw new ValidationException("InvalidRedirectUri", "Redirect URI is required");
        }

        [GeneratedRegex(@"^[a-zA-Z0-9\s\-_]+$")]
        private static partial Regex AllowedProfileNamePattern();
    }
}
