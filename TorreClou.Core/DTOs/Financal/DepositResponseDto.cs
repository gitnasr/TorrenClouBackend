namespace TorreClou.Core.DTOs.Financal
{
    public record DepositResponseDto
    {
        public string PaymentUrl { get; init; } // اللينك اللي هيروحله
        public string DepositId { get; init; }  // رقم العملية عندنا (Reference)
        public string Status { get; init; }     // Pending
    }
}