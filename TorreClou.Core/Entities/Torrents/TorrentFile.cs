using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TorreClou.Core.Entities.Torrents
{
    public class TorrentFile : BaseEntity
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string[] Files { get; set; } = [];
        public string InfoHash { get; set; } = string.Empty;

        public int UploadedByUserId { get; set; }
        public User UploadedByUser { get; set; } = null!;

    }
}
