using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Entities.Torrents;

namespace TorreClou.Core.Entities
{
    public class User : BaseEntity
    {
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;



        // Navigation properties
        public ICollection<UserStorageProfile> StorageProfiles { get; set; } = [];
        public ICollection<RequestedFile> UploadedTorrentFiles { get; set; } = [];
    }
}