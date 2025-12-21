namespace TorreClou.Core.DTOs.Admin
{
    public class AdminDashboardDto
    {
        // Summary Stats
        public decimal TotalDepositsAmount { get; set; }
        public int TotalDepositsCount { get; set; }
        public int PendingDepositsCount { get; set; }
        public int CompletedDepositsCount { get; set; }
        public int FailedDepositsCount { get; set; }
        
        public decimal TotalWalletBalance { get; set; }
        public int TotalUsersWithBalance { get; set; }
        
        // Chart Data
        public List<ChartDataPoint> DailyDeposits { get; set; } = new();
        public List<ChartDataPoint> WeeklyDeposits { get; set; } = new();
        public List<ChartDataPoint> MonthlyDeposits { get; set; } = new();
    }

    public class ChartDataPoint
    {
        public string Label { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int Count { get; set; }
    }

    public class DateRangeFilter
    {
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
    }

    public class AdminDepositDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string UserFullName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string PaymentProvider { get; set; } = string.Empty;
        public string GatewayTransactionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class AdminWalletDto
    {
        public int UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string UserFullName { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public int TransactionCount { get; set; }
        public DateTime? LastTransactionDate { get; set; }
    }

    public class AdminAdjustBalanceRequest
    {
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}

