using TorreClou.Core.DTOs.Torrents;

public record QuoteResponseDto
{
    public string FileName { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }       
    public string InfoHash { get; init; } = string.Empty;
    public TorrentHealthMeasurements TorrentHealth { get; init; } = null!;
    public int TorrentFileId { get; init; }
    public string[] SelectedFiles { get; init; } = [];
    public string? Message { get; init; }
}
