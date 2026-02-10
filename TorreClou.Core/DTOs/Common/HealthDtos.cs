namespace TorreClou.Core.DTOs.Common
{
    public class HealthStatus
    {
        public DateTime Timestamp { get; set; }
        public bool IsHealthy { get; set; } = true;
        public string Database { get; set; } = "unknown";
        public string Redis { get; set; } = "unknown";
        public string Version { get; set; } = "1.0.0";
        public int? DatabaseResponseTimeMs { get; set; }
        public int? RedisResponseTimeMs { get; set; }
    }

    public class DetailedHealthStatus : HealthStatus
    {
        public int PendingMigrations { get; set; }
        public StorageInfo Storage { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class StorageInfo
    {
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public double UsagePercent { get; set; }
    }
}
