namespace TorreClou.Core.DTOs.Storage.GoogleDrive
{
    public class FileListResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("files")]
        public FileItem[] Files { get; set; } = Array.Empty<FileItem>();
    }

    public class FileItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}

