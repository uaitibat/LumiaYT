using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json.Linq;
using Windows.Security.Authentication.Web;

namespace LumiaYT.Services
{
    public static class AuthService
    {
        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";

        public static string GetRedirectUri()
        {
            // For desktop/UWP WAB, use the WebAuthenticationBroker redirect URI
            return WebAuthenticationBroker.GetCurrentApplicationCallbackUri().AbsoluteUri;
        }

        public static async Task<bool> LoginAsync()
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.GoogleClientId))
                return false;

            var redirectUri = GetRedirectUri();
            var authUrl = AuthEndpoint
                + "?client_id=" + Uri.EscapeDataString(settings.GoogleClientId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(Scope)
                + "&access_type=offline"
                + "&prompt=consent";

            try
            {
                var start = new Uri(authUrl);
                var end = new Uri(redirectUri);
                var result = await WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None, start, end);
                if (result.ResponseStatus != WebAuthenticationStatus.Success)
                    return false;

                var responseUri = new Uri(result.ResponseData);
                var query = ParseQuery(responseUri.Query);
                string code;
                if (!query.TryGetValue("code", out code) || string.IsNullOrWhiteSpace(code))
                    return false;

                return await ExchangeCodeAsync(settings, code, redirectUri);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> ExchangeCodeAsync(AppSettings settings, string code, string redirectUri)
        {
            var form = new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", settings.GoogleClientId },
                { "client_secret", settings.GoogleClientSecret },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" }
            };

            var resp = await HttpService.PostFormAsync(TokenEndpoint, form);
            if (resp == null || !resp.IsSuccessStatusCode) return false;
            var json = await resp.Content.ReadAsStringAsync();
            return ApplyTokenResponse(settings, json, save: true);
        }

        public static async Task<string> GetValidAccessTokenAsync()
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.AccessToken) && string.IsNullOrWhiteSpace(settings.RefreshToken))
                return string.Empty;

            DateTimeOffset expiry;
            if (!string.IsNullOrWhiteSpace(settings.TokenExpiryUtc) &&
                DateTimeOffset.TryParse(settings.TokenExpiryUtc, out expiry) &&
                expiry > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return settings.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(settings.RefreshToken)) return string.Empty;

            var form = new Dictionary<string, string>
            {
                { "refresh_token", settings.RefreshToken },
                { "client_id", settings.GoogleClientId },
                { "client_secret", settings.GoogleClientSecret },
                { "grant_type", "refresh_token" }
            };
            var resp = await HttpService.PostFormAsync(TokenEndpoint, form);
            if (resp == null || !resp.IsSuccessStatusCode) return string.Empty;
            var json = await resp.Content.ReadAsStringAsync();
            if (!ApplyTokenResponse(settings, json, save: true)) return string.Empty;
            return settings.AccessToken;
        }

        private static bool ApplyTokenResponse(AppSettings settings, string json, bool save)
        {
            try
            {
                var obj = JObject.Parse(json);
                var access = obj.Value<string>("access_token");
                if (string.IsNullOrWhiteSpace(access)) return false;
                settings.AccessToken = access;
                var refresh = obj.Value<string>("refresh_token");
                if (!string.IsNullOrWhiteSpace(refresh)) settings.RefreshToken = refresh;
                var expires = obj.Value<int?>("expires_in") ?? 3600;
                settings.TokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(expires).ToString("o");
                if (save) { var t = SettingsService.SaveAsync(settings); t.GetAwaiter().GetResult(); }
                return true;
            }
            catch { return false; }
        }

        public static async Task LogoutAsync()
        {
            var settings = await SettingsService.GetAsync();
            settings.AccessToken = string.Empty;
            settings.RefreshToken = string.Empty;
            settings.TokenExpiryUtc = string.Empty;
            await SettingsService.SaveAsync(settings);
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query)) return dict;
            var q = query.TrimStart('?');
            foreach (var part in q.Split('&'))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length == 0) continue;
                var key = Uri.UnescapeDataString(kv[0]);
                var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                dict[key] = val;
            }
            return dict;
        }
    }
}
