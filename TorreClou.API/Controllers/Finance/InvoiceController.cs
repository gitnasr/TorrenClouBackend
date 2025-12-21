using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TorreClou.Core.Interfaces;

namespace TorreClou.API.Controllers.Finance
{
    [Route("api/invoices")]
    [ApiController]
    [Authorize]
    public class InvoiceController(
        IQuotePricingService pricingService,
        IInvoiceService invoiceService) : BaseApiController
    {
        [HttpPost("pay")]
        public async Task<IActionResult> PayInvoice([FromQuery] int invoiceId)
        {
           var paymentResult = await pricingService.Pay(invoiceId);
            return HandleResult(paymentResult);
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoices(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] DateTime? dateFrom = null,
            [FromQuery] DateTime? dateTo = null)
        {
            var result = await invoiceService.GetUserInvoicesAsync(UserId, pageNumber, pageSize, dateFrom, dateTo);
            return HandleResult(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetInvoice(int id)
        {
            var result = await invoiceService.GetInvoiceByIdAsync(UserId, id);
            return HandleResult(result);
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetInvoiceStatistics()
        {
            var result = await invoiceService.GetUserInvoiceStatisticsAsync(UserId);
            return HandleResult(result);
        }
    }
}
