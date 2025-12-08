namespace TorreClou.Core.DTOs.Torrents
{
    public record TorrentInfoDto
    {
        public string InfoHash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long TotalSize { get; set; }

        public List<TorrentFileDto> Files { get; init; } = new();
        public List<string> Trackers { get; init; } = new();
        public ScrapeAggregationResult ScrapeResult { get; init; } = new();
    }

    public record TorrentFileDto
    {
        public int Index { get; init; } = -1; // For download all
        public string Path { get; init; } = string.Empty;
        public long Size { get; init; }
    }
}