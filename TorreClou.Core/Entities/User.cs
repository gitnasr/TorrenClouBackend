using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Entities.Compliance;

namespace TorreClou.Core.Entities
{
    public class User : BaseEntity
    {
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        // Google Drive integration (separate from login)
        public string? GoogleDriveEmail { get; set; }
        public string? GoogleDriveRefreshToken { get; set; }
        public DateTime? GoogleDriveTokenCreatedAt { get; set; }
        public bool IsGoogleDriveConnected { get; set; } = false;

        // Navigation properties
        public ICollection<UserStorageProfile> StorageProfiles { get; set; } = [];
        public ICollection<UserStrike> Strikes { get; set; } = [];
        public ICollection<RequestedFile> UploadedTorrentFiles { get; set; } = [];
    }
}