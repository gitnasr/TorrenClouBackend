namespace TorreClou.Application.Services.OAuth
{
    public interface IOAuthStateService
    {
        Task<string> GenerateStateAsync<T>(T data, string keyPrefix, TimeSpan expiry);
        Task<T?> ConsumeStateAsync<T>(string stateHash, string keyPrefix);
    }
}
