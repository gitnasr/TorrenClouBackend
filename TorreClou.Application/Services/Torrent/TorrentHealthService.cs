using System;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services.Torrent
{
    public class TorrentHealthService : ITorrentHealthService
    {
        public TorrentHealthMeasurements Compute(ScrapeAggregationResult scrape)
        {
            if (scrape == null) throw new ArgumentNullException(nameof(scrape));

            // Defensive: make sure we don't get negative values from upstream
            var seeders = Math.Max(0, scrape.Seeders);
            var leechers = Math.Max(0, scrape.Leechers);
            var completed = Math.Max(0, scrape.Completed);

            // More semantically correct flags
            var isAvailable = seeders > 0;             // has seeders
            var isComplete = completed > 0;            // at least one completed download reported
            var isDead = seeders == 0 && leechers == 0;
            var isWeak = seeders > 0 && seeders <= 2;  // avoid overlap with dead
            var isHealthy = seeders >= 10;

            // Seeder/Leecher ratio (treat leecher=0 as "max ratio")
            decimal ratio = leechers == 0
                ? decimal.MaxValue
                : (decimal)seeders / leechers;

            // --- Health Score ---
            // 50% seeders strength (non-linear, saturates fast)
            // 30% ratio bucket
            // 20% completeness (based on completed count)

            // Normalize seeders using log10: 0->0, 9->~1, 99->~2 (then clamped)
            // We clamp into [0,1] so anything >= 9-10 seeders already reaches max
            decimal h_seeders = Math.Clamp((decimal)Math.Log10(seeders + 1), 0m, 1m);

            decimal h_ratio =
                ratio < 0.1m ? 0.1m :
                ratio < 0.3m ? 0.3m :
                ratio < 1.0m ? 0.7m :
                1.0m;

            decimal h_complete = isComplete ? 1m : 0m;

            decimal score =
                0.5m * h_seeders +
                0.3m * h_ratio +
                0.2m * h_complete;

            return new TorrentHealthMeasurements
            {
                Seeders = seeders,
                Leechers = leechers,
                Completed = completed,
                SeederRatio = leechers == 0 ? seeders : (decimal)seeders / leechers, 
                IsComplete = isComplete,
                IsDead = isDead,
                IsWeak = isWeak,
                IsHealthy = isHealthy,
                HealthScore = (double)Math.Clamp(score, 0m, 1m)
            };
        }
    }
}
