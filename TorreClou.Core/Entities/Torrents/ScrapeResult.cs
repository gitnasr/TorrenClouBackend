namespace TorreClou.Core.Entities.Torrents
{
    public record ScrapeResult(int Seeders, int Leechers, int Completed, bool Sucess);
}
