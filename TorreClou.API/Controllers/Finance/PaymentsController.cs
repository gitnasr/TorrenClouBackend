using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Payments;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Finance;

[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentBusinessService paymentService, IWalletService walletService) : BaseApiController
{
    #region Deposits

    [HttpPost("deposit/crypto")]
    public async Task<IActionResult> CryptoDeposit([FromBody] CryptoDepositRequestDto request)
    {
        var result = await paymentService.InitiateDepositAsync(UserId, request.Amount, request.Currency);
        return HandleResult(result, value => new { url = value });
    }

    [HttpGet("deposits")]
    public async Task<IActionResult> GetDeposits([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await paymentService.GetUserDepositsAsync(UserId, pageNumber, pageSize);
        return HandleResult(result);
    }

    [HttpGet("deposits/{id}")]
    public async Task<IActionResult> GetDeposit(int id)
    {
        var result = await paymentService.GetDepositByIdAsync(UserId, id);
        return HandleResult(result);
    }

    #endregion

 

    #region Webhooks

    [HttpPost("webhook/crypto")]
    [AllowAnonymous]
    public async Task<IActionResult> CoinremitterWebhook([FromBody] CoinremitterWebhookDto? dto)
    {
        if (dto == null)
            return Error("MISSING_DATA", "Missing webhook data");

        var invoiceId = dto.InvoiceId;
        var depositId = dto.CustomData1;

        if (string.IsNullOrEmpty(invoiceId) && string.IsNullOrEmpty(depositId))
            return Error("MISSING_ID", "Missing invoice_id or custom_data1");

        var lookupId = !string.IsNullOrEmpty(invoiceId) ? invoiceId : depositId!;
        var coin = dto.CoinSymbol ?? dto.Coin ?? string.Empty;

        var result = await paymentService.ProcessCryptoWebhookAsync(lookupId, coin);
        
        if (!result.IsSuccess)
            return HandleResult(result);
        
        return Success(new { success = true });
    }

    #endregion

    #region Public

    [HttpGet("stablecoins/minimum-amounts")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStablecoinMinimumAmounts([FromServices] IPaymentGateway paymentGateway)
    {
        var minAmounts = await paymentGateway.GetMinimumAmountsForStablecoinsAsync();

        var result = minAmounts.Select(kvp => new StablecoinMinAmountDto
        {
            Currency = kvp.Key,
            MinAmount = kvp.Value,
            FiatEquivalent = $"{kvp.Value:F2} USD"
        }).ToList();

        return Success(new { stablecoins = result });
    }

    #endregion
}
