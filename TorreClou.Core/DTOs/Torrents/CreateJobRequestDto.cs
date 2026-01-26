namespace TorreClou.Core.DTOs.Torrents
{
    public record CreateJobRequestDto
    {
        public int TorrentFileId { get; init; }
        public string[]? SelectedFilePaths { get; init; }
        public int? StorageProfileId { get; init; }
    }
}
