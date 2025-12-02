using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers
{
    [ApiController]
    [Route("api/payments")]
    [Authorize]
    public class PaymentsController(IPaymentBusinessService paymentService) : ControllerBase
    {
        [HttpPost("deposit")]
        public async Task<IActionResult> InitiateDeposit([FromBody] DepositRequestDto request)
        {
            // 1. Get UserId from Token (Clean & Simple)
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            // 2. Call Service
            var result = await paymentService.InitiateDepositAsync(userId, request.Amount);

            // 3. Handle Result
            if (!result.IsSuccess)
            {
                return BadRequest(result.Error);
            }

            // 4. Return URL
            return Ok(new { paymentUrl = result.Value });
        }
    }
}