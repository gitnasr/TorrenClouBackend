using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TorreClou.Core.DTOs.Storage.Google_Drive;
using TorreClou.Core.Exceptions;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services
{
    public class GoogleApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleApiClient> logger) : IGoogleApiClient
    {
        public async Task<TokenResponse> ExchangeCodeForTokensAsync(
            string code, string clientId, string clientSecret, string redirectUri)
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
                throw new ExternalServiceException("TokenExchangeFailed", "Failed to exchange authorization code for tokens");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);

            if (tokenResponse == null)
                throw new ExternalServiceException("InvalidResponse", "Invalid token response");

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                logger.LogError("Token exchange returned empty access_token. ExpiresIn: {ExpiresIn}", tokenResponse.ExpiresIn);
                throw new ExternalServiceException("InvalidResponse", "Token exchange response is missing access_token");
            }

            if (string.IsNullOrEmpty(tokenResponse.RefreshToken))
                logger.LogWarning("Token exchange response missing refresh_token. Access token expires in {ExpiresIn} seconds.", tokenResponse.ExpiresIn);
            else
                logger.LogInformation("Token exchange successful. Access token expires in {ExpiresIn} seconds. Refresh token present.", tokenResponse.ExpiresIn);

            return tokenResponse;
        }

        public async Task<UserInfoResponse> GetUserInfoAsync(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                logger.LogWarning("Access token is empty, skipping user info fetch");
                throw new UnauthorizedException("InvalidCredentials", "Access token is empty");
            }

            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken.Trim());

            var response = await httpClient.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    "Failed to fetch user info: {StatusCode} - {Error}. Access token length: {TokenLength}",
                    response.StatusCode, errorContent, accessToken.Length);
                throw new ExternalServiceException("InvalidResponse", "Failed to fetch user info");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var userInfo = JsonSerializer.Deserialize<UserInfoResponse>(jsonResponse);

            if (userInfo == null)
            {
                logger.LogError("Failed to deserialize user info response. Response length: {Length}", jsonResponse.Length);
                throw new ExternalServiceException("InvalidResponse", "Failed to deserialize user info");
            }

            logger.LogInformation("User info fetched successfully. Email present: {EmailPresent}", !string.IsNullOrEmpty(userInfo.Email));

            return userInfo;
        }
    }
}
