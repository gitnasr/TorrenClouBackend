using TorreClou.Core.Models.Pricing;

public record QuoteResponseDto
{
    public bool IsReadyToDownload { get; init; }

    public decimal OriginalAmountInUSD { get; init; }
    public decimal FinalAmountInUSD { get; init; }
    public decimal FinalAmountInNCurrency { get; init; }

    public string FileName { get; init; } = string.Empty;

    public long SizeInBytes { get; init; }       

    public bool IsCached { get; init; }

    public string InfoHash { get; init; } = string.Empty;

    public string? Message { get; init; }

    public PricingSnapshot PricingDetails { get; init; } = null!;

    public int InvoiceId { get; init; }
}
