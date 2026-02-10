using System.ComponentModel.DataAnnotations;

namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    public record ConfigureGoogleDriveRequestDto
    {
        [StringLength(255)]
        public string ProfileName { get; init; } = string.Empty;

        [Required(ErrorMessage = "Client ID is required")]
        [StringLength(500)]
        public string ClientId { get; init; } = string.Empty;

        [Required(ErrorMessage = "Client Secret is required")]
        [StringLength(500)]
        public string ClientSecret { get; init; } = string.Empty;

        [Required(ErrorMessage = "Redirect URI is required")]
        [Url(ErrorMessage = "Redirect URI must be a valid URL")]
        public string RedirectUri { get; init; } = string.Empty;

        public bool SetAsDefault { get; init; } = false;
    }
}
