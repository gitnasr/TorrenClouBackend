using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Enums;

namespace TorreClou.Core.Specifications
{
    public class ActiveJobsByStorageProfileSpecification : BaseSpecification<UserJob>
    {
        public ActiveJobsByStorageProfileSpecification(int storageProfileId)
            : base(j =>
                j.StorageProfileId == storageProfileId &&
                (j.Status == JobStatus.QUEUED ||
                 j.Status == JobStatus.DOWNLOADING ||
                 j.Status == JobStatus.PENDING_UPLOAD ||
                 j.Status == JobStatus.UPLOADING ||
                 j.Status == JobStatus.TORRENT_DOWNLOAD_RETRY ||
                 j.Status == JobStatus.UPLOAD_RETRY))
        {
        }
    }
}

