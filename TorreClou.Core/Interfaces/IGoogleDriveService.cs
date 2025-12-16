namespace TorreClou.Application.Services.Google_Drive
{
    public interface IGoogleDriveService
    {
        Task<string> GetAuthorizationUrlAsync(int userId, string? profileName = null);
        Task<string> GetGoogleCallback(string code, string state);
    }
}
