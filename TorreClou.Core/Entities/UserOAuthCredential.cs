namespace TorreClou.Core.Entities
{
    /// <summary>
    /// Reusable OAuth app credentials (ClientId, ClientSecret, RedirectUri).
    /// A user saves these once and can link them to multiple storage profiles.
    /// </summary>
    public class UserOAuthCredential : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        /// <summary>
        /// Friendly label, e.g. "My GCP Project".
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
    }
}
