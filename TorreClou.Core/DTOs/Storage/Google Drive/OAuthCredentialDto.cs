namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    /// <summary>
    /// Response DTO for listing saved OAuth app credentials.
    /// ClientId is masked for security; ClientSecret is never returned.
    /// </summary>
    public class OAuthCredentialDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Masked ClientId (e.g. "1234...wxyz.apps.googleusercontent.com")
        /// </summary>
        public string ClientIdMasked { get; set; } = string.Empty;

        public string RedirectUri { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
