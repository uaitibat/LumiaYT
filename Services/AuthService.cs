using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json.Linq;
using Windows.Security.Authentication.Web;

namespace LumiaYT.Services
{
    public sealed class DeviceCodeInfo
    {
        public string DeviceCode { get; set; } = string.Empty;
        public string UserCode { get; set; } = string.Empty;
        public string VerificationUrl { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public int Interval { get; set; } = 5;
        public string Message { get; set; } = string.Empty;
    }

    public static class AuthService
    {
        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string DeviceCodeEndpoint = "https://oauth2.googleapis.com/device/code";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";

        // Fallback embutido para permitir login sem configurar Cloud Console.
        // Pode parar de funcionar se revogado pelo provedor.
        private const string DefaultClientId = "170729868717-epodfoimm3bte91rpg06fbf79t92l5hj.apps.googleusercontent.com";
        private const string DefaultClientSecret = "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";
        public const string DefaultWebRedirectUri = "https://example.com/oauth2callback";

        public static string GetRedirectUri()
        {
            return WebAuthenticationBroker.GetCurrentApplicationCallbackUri().AbsoluteUri;
        }

        public static string GetEffectiveClientId(AppSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings?.GoogleClientId) ? settings.GoogleClientId.Trim() : DefaultClientId;
        }

        public static string GetEffectiveClientSecret(AppSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings?.GoogleClientSecret) ? settings.GoogleClientSecret.Trim() : DefaultClientSecret;
        }

        public static string GetEffectiveWebRedirectUri(AppSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings?.GoogleClientId) ? "http://localhost" : DefaultWebRedirectUri;
        }

        public static string BuildWebAuthUrl(string clientId, string redirectUri)
        {
            return AuthEndpoint
                + "?client_id=" + Uri.EscapeDataString(clientId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(Scope)
                + "&access_type=offline"
                + "&prompt=select_account"
                + "&include_granted_scopes=true";
        }

        public static string GetQueryParam(string query, string key)
        {
            var dict = ParseQuery(query);
            string val;
            return dict.TryGetValue(key, out val) ? val : string.Empty;
        }

        public static async Task<bool> ExchangeCodeForRedirectAsync(string code, string redirectUri)
        {
            var settings = await SettingsService.GetAsync();
            return await ExchangeCodeAsync(settings, code, redirectUri);
        }

        public static async Task<bool> LoginAsync()
        {
            var settings = await SettingsService.GetAsync();
            var clientId = GetEffectiveClientId(settings);
            if (string.IsNullOrWhiteSpace(clientId))
                return false;

            var redirectUri = GetRedirectUri();
            var authUrl = BuildWebAuthUrl(clientId, redirectUri);

            try
            {
                var start = new Uri(authUrl);
                var end = new Uri(redirectUri);
                var result = await WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None, start, end);
                if (result.ResponseStatus != WebAuthenticationStatus.Success)
                    return false;

                var responseUri = new Uri(result.ResponseData);
                var code = GetQueryParam(responseUri.Query, "code");
                if (string.IsNullOrWhiteSpace(code))
                    return false;

                return await ExchangeCodeAsync(settings, code, redirectUri);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<DeviceCodeInfo> StartDeviceCodeFlowAsync()
        {
            var settings = await SettingsService.GetAsync();
            var clientId = GetEffectiveClientId(settings);
            if (string.IsNullOrWhiteSpace(clientId))
                return null;

            var form = new Dictionary<string, string>
            {
                { "client_id", clientId },
                { "scope", Scope }
            };

            var resp = await HttpService.PostFormAsync(DeviceCodeEndpoint, form);
            if (resp == null || !resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            try
            {
                var obj = JObject.Parse(json);
                var deviceCode = obj.Value<string>("device_code");
                var userCode = obj.Value<string>("user_code");
                if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(userCode)) return null;

                return new DeviceCodeInfo
                {
                    DeviceCode = deviceCode,
                    UserCode = userCode,
                    VerificationUrl = obj.Value<string>("verification_url") ?? obj.Value<string>("verification_uri") ?? "https://www.google.com/device",
                    ExpiresIn = obj.Value<int?>("expires_in") ?? 1800,
                    Interval = obj.Value<int?>("interval") ?? 5,
                    Message = obj.Value<string>("message") ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        public static async Task<bool> PollDeviceCodeAsync(DeviceCodeInfo info, CancellationToken ct, Action<string> statusCallback)
        {
            if (info == null) return false;
            var settings = await SettingsService.GetAsync();
            var clientId = GetEffectiveClientId(settings);
            if (string.IsNullOrWhiteSpace(clientId)) return false;

            var deadline = DateTimeOffset.UtcNow.AddSeconds(info.ExpiresIn);
            var interval = Math.Max(3, info.Interval);

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (ct.IsCancellationRequested) return false;
                try { await Task.Delay(TimeSpan.FromSeconds(interval), ct); }
                catch (TaskCanceledException) { return false; }

                if (ct.IsCancellationRequested) return false;

                var form = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "client_secret", GetEffectiveClientSecret(settings) ?? string.Empty },
                    { "device_code", info.DeviceCode },
                    { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" }
                };

                var resp = await HttpService.PostFormAsync(TokenEndpoint, form);
                if (resp == null) continue;
                var json = await resp.Content.ReadAsStringAsync();

                try
                {
                    var obj = JObject.Parse(json);
                    var error = obj.Value<string>("error");
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        if (ApplyTokenResponse(settings, json, save: true))
                        {
                            statusCallback?.Invoke("Login concluído.");
                            return true;
                        }
                        statusCallback?.Invoke("Resposta de token inválida.");
                        return false;
                    }

                    if (error == "authorization_pending")
                    {
                        statusCallback?.Invoke("Aguardando você autorizar no navegador...");
                        continue;
                    }
                    if (error == "slow_down")
                    {
                        interval += 5;
                        statusCallback?.Invoke("Aguardando autorização (reduzindo polling)...");
                        continue;
                    }
                    if (error == "access_denied")
                    {
                        statusCallback?.Invoke("Acesso negado no Google.");
                        return false;
                    }
                    if (error == "expired_token")
                    {
                        statusCallback?.Invoke("Código expirado. Gere um novo código.");
                        return false;
                    }

                    statusCallback?.Invoke("Erro: " + error);
                    return false;
                }
                catch
                {
                    statusCallback?.Invoke("Erro ao ler resposta do Google.");
                }
            }

            statusCallback?.Invoke("Código expirado. Gere um novo código.");
            return false;
        }

        private static async Task<bool> ExchangeCodeAsync(AppSettings settings, string code, string redirectUri)
        {
            var form = new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", GetEffectiveClientId(settings) },
                { "client_secret", GetEffectiveClientSecret(settings) },
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
                { "client_id", GetEffectiveClientId(settings) },
                { "client_secret", GetEffectiveClientSecret(settings) ?? string.Empty },
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
