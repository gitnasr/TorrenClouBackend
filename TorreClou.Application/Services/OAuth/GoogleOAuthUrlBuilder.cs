using System.Web;

namespace TorreClou.Application.Services.OAuth
{
    public static class GoogleOAuthUrlBuilder
    {
        private const string DefaultScopes = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/userinfo.email";

        public static string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string? scopes = null)
        {
            var scopeValue = scopes ?? DefaultScopes;
            var encodedState = HttpUtility.UrlEncode(state);

            return $"https://accounts.google.com/o/oauth2/v2/auth?" +
                $"client_id={HttpUtility.UrlEncode(clientId)}&" +
                $"redirect_uri={HttpUtility.UrlEncode(redirectUri)}&" +
                $"response_type=code&" +
                $"scope={HttpUtility.UrlEncode(scopeValue)}&" +
                $"access_type=offline&" +
                $"prompt=consent&" +
                $"state={encodedState}";
        }
    }
}
