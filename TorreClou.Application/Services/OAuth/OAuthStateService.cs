using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TorreClou.Core.Interfaces;

namespace TorreClou.Application.Services.OAuth
{
    public interface IOAuthStateService
    {
        Task<string> GenerateStateAsync<T>(T data, string keyPrefix, TimeSpan expiry);
        Task<T?> ConsumeStateAsync<T>(string stateHash, string keyPrefix);
    }

    public class OAuthStateService(IRedisCacheService redisCache) : IOAuthStateService
    {
        public async Task<string> GenerateStateAsync<T>(T data, string keyPrefix, TimeSpan expiry)
        {
            var nonce = Guid.NewGuid().ToString("N");
            var stateHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)));

            var redisKey = $"{keyPrefix}{stateHash}";
            await redisCache.SetAsync(redisKey, JsonSerializer.Serialize(data), expiry);

            return stateHash;
        }

        public async Task<T?> ConsumeStateAsync<T>(string stateHash, string keyPrefix)
        {
            var redisKey = $"{keyPrefix}{stateHash}";
            var json = await redisCache.GetAndDeleteAsync(redisKey);

            if (string.IsNullOrEmpty(json))
                return default;

            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
