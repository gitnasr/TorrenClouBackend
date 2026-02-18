namespace TorreClou.Core.DTOs.Torrents
{
    public record ScrapeResult(int Seeders, int Leechers, int Completed, bool Sucess);
}
