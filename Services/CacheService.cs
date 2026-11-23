using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace LumiaYT.Services
{
    public static class CacheService
    {
        private static readonly StorageFolder CacheFolder = ApplicationData.Current.LocalCacheFolder;

        public static async Task<string> GetOrDownloadTextAsync(string key, string url, TimeSpan maxAge)
        {
            var fileName = Sanitize(key) + ".txt";
            var file = await CacheFolder.TryGetItemAsync(fileName) as StorageFile;
            if (file != null)
            {
                var props = await file.GetBasicPropertiesAsync();
                if (DateTimeOffset.UtcNow - props.DateModified <= maxAge)
                    return await FileIO.ReadTextAsync(file);
            }

            var text = await HttpService.GetStringAsync(url);
            if (!string.IsNullOrEmpty(text))
            {
                var target = file ?? await CacheFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(target, text);
            }
            else if (file != null)
            {
                return await FileIO.ReadTextAsync(file);
            }
            return text;
        }
        private static string Sanitize(string key)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return key;
        }
    }

    internal static class HttpService
    {
        private static readonly System.Net.Http.HttpClient Client = CreateClient();
        private static System.Net.Http.HttpClient CreateClient()
        {
            var c = new System.Net.Http.HttpClient();
            c.DefaultRequestHeaders.UserAgent.ParseAdd("LumiaYT/0.1");
            c.Timeout = TimeSpan.FromSeconds(20);
            return c;
        }
        public static async Task<string> GetStringAsync(string url)
        {
            try { var r = await Client.GetAsync(url); if (!r.IsSuccessStatusCode) return ""; return await r.Content.ReadAsStringAsync(); }
            catch { return ""; }
        }
        public static async Task<System.Net.Http.HttpResponseMessage> PostFormAsync(string url, System.Collections.Generic.Dictionary<string, string> form)
        {
            try { return await Client.PostAsync(url, new System.Net.Http.FormUrlEncodedContent(form)); }
            catch { return null; }
        }
        public static async Task<string> GetStringWithAuthAsync(string url, string token)
        {
            try
            {
                var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var r = await Client.SendAsync(req);
                if (!r.IsSuccessStatusCode) return "";
                return await r.Content.ReadAsStringAsync();
            }
            catch { return ""; }
        }
    }
}
