using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.API.Controllers.Finance
{
    [Authorize]
    [Route("api/finance/wallet")]
    [ApiController]
    public class WalletController(IWalletService walletService) : BaseApiController
    {
        #region Wallet

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            var result = await walletService.GetUserBalanceAsync(UserId);
            var mappedResult = result.IsSuccess
                ? Result.Success(new WalletBalanceDto { Balance = result.Value, Currency = "USD" })
                : Result.Failure<WalletBalanceDto>(result.Error);
            return HandleResult(mappedResult, 200);
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await walletService.GetUserTransactionsAsync(UserId, pageNumber, pageSize);
            return HandleResult(result);
        }

        [HttpGet("transactions/{id}")]
        public async Task<IActionResult> GetTransaction(int id)
        {
            var result = await walletService.GetTransactionByIdAsync(UserId, id);
            return HandleResult(result);
        }

        #endregion
    }
}
