using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    [Route("api/torrents")]
    public class TorrentsController(
        ITorrentQuoteService quoteService,
        ITorrentService torrentService
    ) : BaseApiController
    {


        [HttpPost("analyze/file")]
        public async Task<IActionResult> AnalyzeFile(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");
            using var stream = file.OpenReadStream();
            var result = await  torrentService.GetTorrentInfoFromTorrentFileAsync(stream);
            return HandleResult(result);
        }

        [HttpPost("quote")]
        public async Task<IActionResult> GetQuote([FromForm] QuoteRequestDto request)
        {
            var userId = 2;
            using var stream = request.TorrentFile.OpenReadStream();

            var result = await quoteService.GenerateQuoteAsync(request, userId, stream);

            return HandleResult(result);
        }
    }

}