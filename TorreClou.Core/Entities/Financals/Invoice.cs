using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Entities.Jobs;
using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Entities.Torrents;

public class Invoice : BaseEntity
{
    public int UserId { get; set; }

    public int? JobId { get; set; }
    public UserJob? Job { get; set; } = null!;


    public decimal OriginalAmountInUSD { get; set; }

    public decimal FinalAmountInUSD { get; set; }

  
    public decimal FinalAmountInNCurrency { get; set; }

    public decimal ExchangeRate { get; set; } = 1.0m;

    public DateTime? CancelledAt { get; set; } = null;

    public string PricingSnapshotJson { get; set; } = "{}";

    public bool IsPaid { get; set; } = false;
    public bool IsRefunded { get; set; } = false;

    public int? WalletTransactionId { get; set; }
    public WalletTransaction? WalletTransaction { get; set; }

    public Voucher? Voucher { get; set; }
    public int? VoucherId { get; set; }

    public int TorrentFileId { get; set; }
    public TorrentFile TorrentFile { get; set; } = new();

    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
