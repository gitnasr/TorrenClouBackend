namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    /// <summary>
    /// Response returned after saving reusable OAuth app credentials.
    /// </summary>
    public class SaveGoogleDriveCredentialsResponseDto
    {
        public int CredentialId { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
