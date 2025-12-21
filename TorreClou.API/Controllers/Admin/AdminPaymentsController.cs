using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Admin;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Admin;

[Route("api/admin/payments")]
[Authorize(Roles = "Admin")]
public class AdminPaymentsController(IPaymentBusinessService paymentService, IWalletService walletService) : BaseApiController
{
    #region Deposits

    [HttpGet("deposits")]
    public async Task<IActionResult> GetAllDeposits(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] DepositStatus? status = null)
    {
        var result = await paymentService.AdminGetAllDepositsAsync(pageNumber, pageSize, status);
        return HandleResult(result);
    }

    #endregion

    #region Wallets

    [HttpGet("wallets")]
    public async Task<IActionResult> GetAllWallets([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await walletService.AdminGetAllWalletsAsync(pageNumber, pageSize);
        return HandleResult(result);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetAllTransactions([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var result = await walletService.AdminGetAllTransactionsAsync(pageNumber, pageSize);
        return HandleResult(result);
    }

    [HttpPost("wallets/{userId}/adjust")]
    public async Task<IActionResult> AdjustUserBalance(int userId, [FromBody] AdminAdjustBalanceRequest request)
    {
        if (request.Amount == 0)
            return Error("INVALID_AMOUNT", "Amount cannot be zero.");

        if (string.IsNullOrWhiteSpace(request.Description))
            return Error("MISSING_DESCRIPTION", "Description is required.");

        var result = await walletService.AdminAdjustBalanceAsync(UserId, userId, request.Amount, request.Description);
        return HandleResult(result);
    }

    #endregion

    #region Analytics

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics([FromQuery] DateTime? dateFrom = null, [FromQuery] DateTime? dateTo = null)
    {
        var result = await paymentService.GetAnalyticsAsync(dateFrom, dateTo);
        return HandleResult(result);
    }

    #endregion
}
