using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface ITorrentQuoteService
    {
        Task<Result<QuoteResponseDto>> GenerateQuoteAsync(
            QuoteRequestDto request,
            int userId,
            Stream torrentFile);
    }
}