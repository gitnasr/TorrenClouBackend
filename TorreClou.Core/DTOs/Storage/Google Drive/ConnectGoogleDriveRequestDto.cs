using System.ComponentModel.DataAnnotations;

namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    /// <summary>
    /// Request to connect a new Google Drive account using an existing OAuthCredential.
    /// Creates a new storage profile and starts the OAuth flow in one step.
    /// </summary>
    public record ConnectGoogleDriveRequestDto
    {
        /// <summary>
        /// The ID of the saved OAuthCredential to use for this connection.
        /// </summary>
        [Required(ErrorMessage = "CredentialId is required")]
        public int CredentialId { get; init; }

        /// <summary>
        /// Optional friendly name for the new storage profile (e.g. "Work Drive").
        /// Defaults to "My Google Drive" if omitted.
        /// </summary>
        [StringLength(255)]
        public string? ProfileName { get; init; }

        public bool SetAsDefault { get; init; } = false;
    }
}
