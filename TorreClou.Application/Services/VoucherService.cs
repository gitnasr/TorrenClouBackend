using TorreClou.Core.Entities.Marketing;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{

    public interface IVoucherService
    {
        Task<Result<Voucher>> ValidateVoucherAsync(string voucherCode, int userId);
    }
    public class VoucherService(IUnitOfWork unitOfWork) : IVoucherService
    {
        public async Task<Result<Voucher>> ValidateVoucherAsync(string voucherCode, int userId)
        {
            //1. Get the Voicher by code from the database
            var searchSpec = new BaseSpecification<Voucher>(v => v.Code == voucherCode && v.IsActive && v.MaxUsesTotal < v.Usages.Count);
            var voucher =  await unitOfWork.Repository<Voucher>().GetEntityWithSpec(searchSpec);
            if (voucher == null)
                return  Result<Voucher>.Failure("INVALID_VOUCHER", "The voucher code is invalid or inactive.");

            //2. Check if user has exceeded max uses per user
            var userUsageCount = voucher.Usages.Count(uv => uv.UserId == userId);
            if (userUsageCount >= voucher.MaxUsesPerUser)
                return Result<Voucher>.Failure("VOUCHER_USAGE_EXCEEDED", "You have exceeded the maximum uses for this voucher.");
            //3. Check if voucher is expired
            if (voucher.ExpiresAt.HasValue && voucher.ExpiresAt.Value < DateTime.UtcNow)
                return Result<Voucher>.Failure("VOUCHER_EXPIRED", "The voucher has expired.");
            return Result<Voucher>.Success(voucher);
        }
    }
}
