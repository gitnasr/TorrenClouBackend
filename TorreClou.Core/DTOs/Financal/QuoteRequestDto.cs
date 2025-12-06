using Microsoft.AspNetCore.Http;

namespace TorreClou.Core.DTOs.Financal
{
    public record QuoteRequestDto
    {

        public List<int> SelectedFileIndices { get; init; } = new();
        public IFormFile TorrentFile { get; init; }
    }
}