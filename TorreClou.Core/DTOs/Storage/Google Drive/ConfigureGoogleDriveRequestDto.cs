namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    public record ConfigureGoogleDriveRequestDto
    {
        public string ProfileName { get; init; } = string.Empty;
        public string ClientId { get; init; } = string.Empty;
        public string ClientSecret { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public bool SetAsDefault { get; init; } = false;
    }
}
