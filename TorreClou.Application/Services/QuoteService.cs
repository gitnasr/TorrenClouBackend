using System.Text.Json;
using TorreClou.Core.DTOs.Financal;
using TorreClou.Core.DTOs.Torrents;
using TorreClou.Core.Entities;
using TorreClou.Core.Entities.Financals;
using TorreClou.Core.Entities.Torrents;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;
using TorreClou.Core.Specifications;

namespace TorreClou.Application.Services
{
    public interface IQuoteService
    {
        Task<Result<QuoteResponseDto>> GenerateQuoteAsync(QuoteRequestDto request, int userId, Stream torrentFile);    }

    public class QuoteService(IUnitOfWork unitOfWork, IPricingEngine pricingEngine, ITrackerScraper trackerScraper, ITorrentParser torrentParser) : IQuoteService
    {
        public async Task<Result<QuoteResponseDto>> GenerateQuoteAsync(QuoteRequestDto request, int userId , Stream torrentFile)
        {
            long targetSize = 0;
            string fileName = "Unknown";
            bool isCached = false;


            var torrent = torrentParser.ParseTorrentFile(torrentFile);
            if (!torrent.IsSuccess)
            {
                return Result<QuoteResponseDto>.Failure("Invalid torrent file.");
            }

            var torrentInfo = torrent.Value;


            fileName = torrentInfo.Name;

                if (request.SelectedFileIndices != null && request.SelectedFileIndices.Any())
                {

                    if (torrentInfo.TotalSize == 0)
                    {
                        return Result.Success(new QuoteResponseDto
                        {
                            IsReadyToDownload = false,
                            Message = "We're sorry we can't load torrent data. it most likely dead",
                            InfoHash = torrentInfo.InfoHash
                        });
                    }

                    targetSize = torrentInfo.Files
                        .Where(f => request.SelectedFileIndices.Contains(f.Index))
                        .Sum(f => f.Size);
                }
                else
                {
                    targetSize = torrentInfo.TotalSize;
                }

            if (targetSize == 0 && !isCached)
                return Result<QuoteResponseDto>.Failure("Cannot calculate size.");

            // --- STEP 2: Pricing Calculation ---
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);

            int seeders = await GetRealTimeSeeders(torrentInfo.InfoHash, null);

            var snapshot = pricingEngine.CalculatePrice(
                targetSize,
                user.Region,
                seeders,
                isCached // ده اللي بيعمل الخصم جوه الـ Engine
            );

            return Result.Success(new QuoteResponseDto
            {
                IsReadyToDownload = true,
                EstimatedPrice = snapshot.FinalPrice,
                FileName = fileName,
                SizeInGb = Math.Round(targetSize / (1024.0 * 1024.0 * 1024.0), 2),
                IsCached = isCached,
                InfoHash = torrentInfo.InfoHash,
                PricingDetails = snapshot
            });
        }

        // public async Task<Result<int>> CreateInvoiceAsync(string infoHash, int userId)
        // {
        //     // لازم نعيد الحسابات تاني عشان محدش يلعب في السعر من الفرونت
        //     // 1. Get User & Torrent Info logic (Similar to above)...
        //     // ... (Assume we got size, region, seeders again) ...



        //     var snapshot = pricingEngine.CalculatePrice(size, region, seeders, isCached);

        //     // 2. Create Invoice Entity
        //     var invoice = new Invoice
        //     {
        //         UserId = userId,
        //         Amount = snapshot.FinalPrice,
        //         Currency = "USD",
        //         PricingSnapshotJson = JsonSerializer.Serialize(snapshot), // هنا بنحفظ الـ Snapshot
        //         IsPaid = false,
        //         CreatedAt = DateTime.UtcNow
        //     };

        //     unitOfWork.Repository<Invoice>().Add(invoice);
        //     await unitOfWork.Complete();

        //     return Result<int>.Success(invoice.Id);
        // }
        private async Task<int> GetRealTimeSeeders(string infoHash, List<string>? magnetTrackers)
        {
            List<string> defaultTrackers = [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://9.rarbg.com:2810/announce",
        "udp://tracker.openbittorrent.com:80/announce",
        "udp://tracker.tiny-vps.com:6969/announce"
    ];

            // 2. Merge with magnet trackers (if any)
            var allTrackers = defaultTrackers.Concat(magnetTrackers ?? []).Distinct();


            return await trackerScraper.GetSeedersCountAsync(infoHash, allTrackers);
        }
    }
}