using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    [Route("api/torrents")]
    public class TorrentsController(
        ITorrentAnalysisService analysisService,
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

        [HttpPost("analyze")]
        [Authorize]
        public async Task<IActionResult> AnalyzeTorrent([FromForm] AnalyzeTorrentRequestDto request)
        {
            using var stream = request.TorrentFile.OpenReadStream();

            var result = await analysisService.AnalyzeTorrentAsync(request, UserId, stream);

            return HandleResult(result);
        }
    }

}