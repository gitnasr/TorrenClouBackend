namespace TorreClou.Core.DTOs.Financal
{
    public class InvoiceDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int? JobId { get; set; }
        public decimal OriginalAmountInUSD { get; set; }
        public decimal FinalAmountInUSD { get; set; }
        public decimal FinalAmountInNCurrency { get; set; }
        public decimal ExchangeRate { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public int TorrentFileId { get; set; }
        public string? TorrentFileName { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Computed properties
        public bool IsPaid => PaidAt != null;
        public bool IsCancelled => CancelledAt != null;
        public bool IsRefunded => RefundedAt != null;
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}

