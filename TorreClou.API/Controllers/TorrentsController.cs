using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    [Route("api/torrents")]
    [Authorize]
    public class TorrentsController(
        ITorrentAnalysisService analysisService
    ) : BaseApiController
    {
        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeTorrent([FromForm] AnalyzeTorrentRequestDto request)
        {
            using var stream = request.TorrentFile.OpenReadStream();

            var result = await analysisService.AnalyzeTorrentAsync(request, UserId, stream);

            return HandleResult(result);
        }
    }

}