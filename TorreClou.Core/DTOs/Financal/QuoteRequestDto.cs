using Microsoft.AspNetCore.Http;

namespace TorreClou.Core.DTOs.Financal
{
    public record QuoteRequestDto
    {

        public List<string>? SelectedFilePaths { get; set; }
        public required IFormFile TorrentFile { get; init; }
        public string? VoucherCode { get; set; }
        public int StorageProfileId { get; set; }
    }
}