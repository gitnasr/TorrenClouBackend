namespace TorreClou.Core.DTOs.Torrents
{
    public record TorrentAnalysisDto
    {
        public string InfoHash { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public List<TorrentFileDto> Files { get; init; } = new(); // القائمة اللي فيها Index
    }
}