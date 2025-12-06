
using TorreClou.Core.Enums;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Entities.Financals;

namespace TorreClou.Core.Entities.Jobs
{
    public class UserJob : BaseEntity
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int CachedTorrentId { get; set; }
        public CachedTorrent CachedTorrent { get; set; } = null!;

        public int StorageProfileId { get; set; }
        public UserStorageProfile StorageProfile { get; set; } = null!;

        public JobStatus Status { get; set; } = JobStatus.QUEUED;

        public string? RemoteFileId { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public Invoice? Invoice { get; set; }

        public int[] SelectedFileIndices { get; set; } = [];
    }
}