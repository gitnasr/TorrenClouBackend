using System.ComponentModel.DataAnnotations;

namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    /// <summary>
    /// Save reusable Google OAuth app credentials (ClientId, ClientSecret, RedirectUri).
    /// These can be linked to multiple storage profiles.
    /// </summary>
    public record SaveGoogleDriveCredentialsRequestDto
    {
        /// <summary>
        /// Friendly label for these credentials (e.g. "My GCP Project").
        /// </summary>
        [StringLength(255)]
        public string Name { get; init; } = string.Empty;

        [Required(ErrorMessage = "Client ID is required")]
        [StringLength(500)]
        public string ClientId { get; init; } = string.Empty;

        [Required(ErrorMessage = "Client Secret is required")]
        [StringLength(500)]
        public string ClientSecret { get; init; } = string.Empty;

        [Required(ErrorMessage = "Redirect URI is required")]
        [Url(ErrorMessage = "Redirect URI must be a valid URL")]
        public string RedirectUri { get; init; } = string.Empty;
    }
}
