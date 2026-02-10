using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TorreClou.Core.DTOs.Storage.Google_Drive;
using TorreClou.Core.Enums;
using TorreClou.Core.Interfaces;
using TorreClou.Core.Shared;

namespace TorreClou.Infrastructure.Services
{
    public class GoogleApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleApiClient> logger) : IGoogleApiClient
    {
        public async Task<Result<TokenResponse>> ExchangeCodeForTokensAsync(
            string code, string clientId, string clientSecret, string redirectUri)
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                var requestBody = new Dictionary<string, string>
                {
                    { "code", code },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "redirect_uri", redirectUri },
                    { "grant_type", "authorization_code" }
                };

                var content = new FormUrlEncodedContent(requestBody);
                var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogError("Token exchange failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return Result<TokenResponse>.Failure(ErrorCode.TokenExchangeFailed, "Failed to exchange authorization code for tokens");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);

                if (tokenResponse == null)
                {
                    return Result<TokenResponse>.Failure(ErrorCode.InvalidResponse, "Invalid token response");
                }

                if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                {
                    logger.LogWarning("Token exchange response missing refresh_token. Access token expires in {ExpiresIn} seconds.", tokenResponse.ExpiresIn);
                }
                else
                {
                    logger.LogInformation("Token exchange successful. Access token expires in {ExpiresIn} seconds. Refresh token present.", tokenResponse.ExpiresIn);
                }

                return Result.Success(tokenResponse);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception during token exchange");
                return Result<TokenResponse>.Failure(ErrorCode.TokenExchangeFailed, "Error during token exchange");
            }
        }

        public async Task<Result<UserInfoResponse>> GetUserInfoAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                logger.LogWarning("Access token is empty, skipping user info fetch");
                return Result<UserInfoResponse>.Failure(ErrorCode.InvalidCredentials, "Access token is empty");
            }

            try
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken.Trim());

                var response = await httpClient.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger.LogCritical(
                        "Failed to fetch user info: {StatusCode} - {Error}. Access token length: {TokenLength}",
                        response.StatusCode, errorContent, accessToken.Length);
                    return Result<UserInfoResponse>.Failure(ErrorCode.InvalidResponse, "Failed to fetch user info");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var userInfo = JsonSerializer.Deserialize<UserInfoResponse>(jsonResponse);

                if (userInfo == null)
                {
                    logger.LogError("Failed to deserialize user info response. Response length: {Length}", jsonResponse.Length);
                    return Result<UserInfoResponse>.Failure(ErrorCode.InvalidResponse, "Failed to deserialize user info");
                }

                if (!string.IsNullOrEmpty(userInfo.Email))
                {
                    logger.LogInformation("Successfully fetched user email: {Email}", userInfo.Email);
                }

                return Result.Success(userInfo);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exception while fetching user info (non-critical)");
                return Result<UserInfoResponse>.Failure(ErrorCode.InvalidResponse, "Error fetching user info");
            }
        }
    }
}
