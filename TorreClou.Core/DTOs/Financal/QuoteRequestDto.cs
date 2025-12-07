using Microsoft.AspNetCore.Http;

namespace TorreClou.Core.DTOs.Financal
{
    public record QuoteRequestDto
    {

        public List<int> SelectedFileIndices { get; set; } = new();
        public IFormFile TorrentFile { get; init; }

        public string? VoucherCode { get; set; }
    }
}