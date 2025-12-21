using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services.Torrent
{

    public class TorrentHealthService : ITorrentHealthService
    {
        public TorrentHealthMeasurements Compute(ScrapeAggregationResult scrape)
        {
            var seeders = scrape.Seeders;
            var leechers = scrape.Leechers;
            var completed = scrape.Completed;

            decimal ratio = leechers == 0 ? seeders : (decimal)seeders / leechers;

            var isComplete = seeders > 0;                    
            var isDead = seeders == 0 && leechers == 0;        
            var isWeak = seeders <= 2;                         
            var isHealthy = seeders >= 10;                   

            // --- Health Score ---
            // 50% seeders count
            // 30% seeder/leecher ratio
            // 20% completeness flag

            decimal h_seeders = Math.Clamp(seeders / 50m, 0, 1);
            decimal h_ratio =
                ratio < 0.1m ? 0.1m :
                ratio < 0.3m ? 0.3m :
                ratio < 1.0m ? 0.7m :
                1.0m;

            decimal h_complete = isComplete ? 1m : 0m;

            var score =
                0.5m * h_seeders +
                0.3m * h_ratio +
                0.2m * h_complete;

            return new TorrentHealthMeasurements
            {
                Seeders = seeders,
                Leechers = leechers,
                Completed = completed,
                SeederRatio = ratio,
                IsComplete = isComplete,
                IsDead = isDead,
                IsWeak = isWeak,
                IsHealthy = isHealthy,
                HealthScore = (double)score
            };
        }
    }

}
