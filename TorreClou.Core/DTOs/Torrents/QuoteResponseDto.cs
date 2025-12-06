using TorreClou.Core.Models.Pricing;

namespace TorreClou.Core.DTOs.Torrents
{
    public record QuoteResponseDto
    {
        // هل نقدر نبدأ تحميل علطول؟ (True للملفات، False للماجنت اللي لسه بيجمع داتا)
        public bool IsReadyToDownload { get; init; }

        // السعر المتوقع (بعد الخصم لو فيه كاش)
        public decimal EstimatedPrice { get; init; }

        public string FileName { get; init; } = string.Empty;

        public double SizeInGb { get; init; }

        // عشان نحط بادج "خصم 50%" في الفرونت
        public bool IsCached { get; init; }

        public string InfoHash { get; init; } = string.Empty;

        // رسالة توضيحية (مثلا: "جاري جلب المعلومات..." في حالة الماجنت)
        public string? Message { get; init; }

        public PricingSnapshot PricingDetails { get; init; } // التفاصيل الكاملة
    }
}