using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Interfaces;
using TorreClou.Infrastructure.Settings; // Namespace of your settings

namespace TorreClou.Infrastructure.Services
{
    public class KashierPaymentService : IPaymentGateway
    {
        private readonly KashierSettings _settings;

        public KashierPaymentService(IOptions<KashierSettings> settings)
        {
            _settings = settings.Value;
        }

        public Task<string> InitiatePaymentAsync(Deposit deposit, User user)
        {
            // 1. تجهيز البيانات الأساسية
            var orderId = deposit.Id.ToString(); // أو ReferenceId لو عندك
            var amount = deposit.Amount.ToString();
            var currency = "EGP"; // كاشير غالبا مصري، او حسب عملة اليوزر

            // 2. حساب الهاش (Hash Calculation)
            // القاعدة الذهبية: الترتيب مهم جداً. كاشير ليه معادلة خاصة بيه.
            // المعادلة الشائعة بتاعتهم: Path /?merchantId=...&orderId=...&amount=...&currency=... 
            // لازم تتأكد من الترتيب في الـ Docs. 
            // هنا هفترض الترتيب الأبجدي للبارامترز اللي بتدخل في الهاش

            var path = "/"; // الـ Root path بتاع الـ Checkout URL

            // البارامترز اللي بتدخل في الهاش
            var hashString = $"{path}?amount={amount}&currency={currency}&merchantId={_settings.MerchantId}&mode={_settings.Mode}&orderId={orderId}&secret={_settings.ApiKey}";

            var hash = CalculateHmacSha256(hashString, _settings.ApiKey);

            // 3. بناء اللينك النهائي (Query Parameters)
            var queryParams = new List<string>
            {
                $"merchantId={_settings.MerchantId}",
                $"orderId={orderId}",
                $"amount={amount}",
                $"currency={currency}",
                $"hash={hash}", // الهاش اللي حسبناه
                $"mode={_settings.Mode}",
                $"merchantRedirect={_settings.CallbackUrl}",
                $"serverWebhook={_settings.WebhookUrl}",
                $"paymentRequestId={Guid.NewGuid()}", // ID فريد للريكويست
                $"allowedMethods=card,wallet", // فيزا ومحافظ
                $"brandColor=#000000", // لون البراند بتاعك
                $"display=en", // اللغة
                $"customer={user.FullName}", // اسم الزبون
                $"metaData={{\"userId\":\"{user.Id}\", \"depositId\":\"{deposit.Id}\"}}" // دي هتنفعنا اوي في الـ Webhook
            };

            var fullUrl = $"{_settings.BaseUrl}/?{string.Join("&", queryParams)}";

            return Task.FromResult(fullUrl);
        }

        // دالة التشفير (Helper)
        private string CalculateHmacSha256(string message, string secret)
        {
            var encoding = new ASCIIEncoding();
            byte[] keyByte = encoding.GetBytes(secret);
            byte[] messageBytes = encoding.GetBytes(message);

            using var hmacsha256 = new HMACSHA256(keyByte);
            byte[] hashmessage = hmacsha256.ComputeHash(messageBytes);
            return Convert.ToHexStringLower(hashmessage);
        }
    }
}