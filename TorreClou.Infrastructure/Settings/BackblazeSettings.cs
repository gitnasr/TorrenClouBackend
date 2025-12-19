namespace TorreClou.Infrastructure.Settings
{
    public class BackblazeSettings
    {
        public string KeyId { get; set; } = string.Empty;
        public string ApplicationKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string BlockStoragePath { get; set; } = "/mnt/torrents";
    }
}






