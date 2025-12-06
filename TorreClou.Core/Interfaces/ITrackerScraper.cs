namespace TorreClou.Core.Interfaces
{
    public interface ITrackerScraper
    {
        Task<int> GetSeedersCountAsync(string infoHash, IEnumerable<string> trackersUrl);
    }
}