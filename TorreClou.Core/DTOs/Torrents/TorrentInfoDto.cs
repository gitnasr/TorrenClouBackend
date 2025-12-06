namespace TorreClou.Core.DTOs.Torrents
{
    public record TorrentInfoDto
    {
        public string Name { get; init; }
        public string InfoHash { get; init; }
        public long TotalSize { get; init; }
        public List<TorrentFileDto> Files { get; init; } = new();
        public bool IsMagnet { get; init; }
        public List<string> Trackers { get; init; } = new();
    }

    public record TorrentFileDto
    {
        public int Index { get; init; } = -1; // For download all
        public string Path { get; init; } = string.Empty;
        public long Size { get; init; }
    }
}