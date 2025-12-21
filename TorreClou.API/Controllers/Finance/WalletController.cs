using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Application.Services;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Finance
{
    [Route("api/wallet")]
    [ApiController]
    public class WalletController(IWalletService walletService) : BaseApiController
    {
        #region Wallet

        [HttpGet("wallet/balance")]
        public async Task<IActionResult> GetBalance()
        {
            var result = await walletService.GetUserBalanceAsync(UserId);
            return HandleResult(result, balance => new WalletBalanceDto { Balance = balance, Currency = "USD" });
        }

        [HttpGet("wallet/transactions")]
        public async Task<IActionResult> GetTransactions([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
        {
            var result = await walletService.GetUserTransactionsAsync(UserId, pageNumber, pageSize);
            return HandleResult(result);
        }

        [HttpGet("wallet/transactions/{id}")]
        public async Task<IActionResult> GetTransaction(int id)
        {
            var result = await walletService.GetTransactionByIdAsync(UserId, id);
            return HandleResult(result);
        }

        #endregion
    }
}
