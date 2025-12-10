using TorreClou.Core.Shared;

namespace TorreClou.Core.Interfaces
{
    public interface IBlobStorageService
    {
        Task<Result<string>> UploadAsync(Stream stream, string fileName, string contentType);
    }
}




