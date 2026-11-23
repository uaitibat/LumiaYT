using System;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Storage;

namespace LumiaYT.Services
{
    public static class SettingsService
    {
        private const string SettingsFile = "settings.json";
        private static AppSettings _cached;

        public static async Task<AppSettings> GetAsync()
        {
            if (_cached != null) return _cached;
            try
            {
                var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(SettingsFile) as StorageFile;
                if (item == null) { _cached = new AppSettings(); return _cached; }
                var json = await FileIO.ReadTextAsync(item);
                _cached = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
            catch { _cached = new AppSettings(); }
            return _cached;
        }

        public static async Task SaveAsync(AppSettings settings)
        {
            _cached = settings ?? new AppSettings();
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(SettingsFile, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, JsonConvert.SerializeObject(_cached));
        }

        public static async Task<string> ImportFromFileAsync(StorageFile file)
        {
            if (file == null) return "Arquivo inválido.";
            try
            {
                var content = await FileIO.ReadTextAsync(file);
                var settings = await GetAsync();
                var changed = ApplyImport(settings, content);
                if (changed == 0) return "Nenhuma configuração reconhecida no arquivo.";
                await SaveAsync(settings);
                return "Configuração importada: " + changed + " campo(s).";
            }
            catch (Exception ex)
            {
                return "Falha ao importar: " + ex.Message;
            }
        }

        public static async Task<string> TryAutoImportAsync()
        {
            string[] names = { "lumia-yt-config.txt", "lumia-yt-config.json", "config.txt", "config.json" };
            foreach (var name in names)
            {
                try
                {
                    var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(name) as StorageFile;
                    if (item == null) continue;
                    var content = await FileIO.ReadTextAsync(item);
                    var settings = await GetAsync();
                    var changed = ApplyImport(settings, content);
                    if (changed > 0)
                    {
                        await SaveAsync(settings);
                        // rename imported file to avoid repeated import
                        try { await item.RenameAsync(name + ".imported", NameCollisionOption.GenerateUniqueName); } catch { }
                        return "Config importada automaticamente de " + name + " (" + changed + " campo(s)).";
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        private static int ApplyImport(AppSettings settings, string content)
        {
            if (settings == null || string.IsNullOrWhiteSpace(content)) return 0;
            var trimmed = content.Trim();
            int changed = 0;

            if (trimmed.StartsWith("{"))
            {
                try
                {
                    var obj = JObject.Parse(trimmed);
                    changed += SetIfFound(settings, "YouTubeApiKey", GetJsonValue(obj, "YouTubeApiKey", "youtubeApiKey", "apiKey", "ApiKey", "key"));
                    changed += SetIfFound(settings, "GoogleClientId", GetJsonValue(obj, "GoogleClientId", "googleClientId", "clientId", "client_id"));
                    changed += SetIfFound(settings, "GoogleClientSecret", GetJsonValue(obj, "GoogleClientSecret", "googleClientSecret", "clientSecret", "client_secret"));

                    // Google OAuth client JSON formats: {"installed":{...}} or {"web":{...}}
                    var installed = obj["installed"] as JObject;
                    var web = obj["web"] as JObject;
                    var o = installed ?? web;
                    if (o != null)
                    {
                        changed += SetIfFound(settings, "GoogleClientId", GetJsonValue(o, "client_id", "clientId"));
                        changed += SetIfFound(settings, "GoogleClientSecret", GetJsonValue(o, "client_secret", "clientSecret"));
                    }
                    return changed;
                }
                catch { /* fall through to key=value */ }
            }

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int idx = line.IndexOf('=');
                if (idx < 0) idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var key = line.Substring(0, idx).Trim();
                var val = line.Substring(idx + 1).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(val)) continue;

                if (IsKey(key, "YouTubeApiKey", "youtubeApiKey", "apiKey", "ApiKey", "key")) changed += SetIfFound(settings, "YouTubeApiKey", val);
                else if (IsKey(key, "GoogleClientId", "googleClientId", "clientId", "client_id")) changed += SetIfFound(settings, "GoogleClientId", val);
                else if (IsKey(key, "GoogleClientSecret", "googleClientSecret", "clientSecret", "client_secret")) changed += SetIfFound(settings, "GoogleClientSecret", val);
            }

            return changed;
        }

        private static string GetJsonValue(JObject obj, params string[] keys)
        {
            if (obj == null) return null;
            foreach (var k in keys)
            {
                var t = obj[k];
                if (t != null && t.Type == JTokenType.String)
                {
                    var v = t.Value<string>();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            return null;
        }

        private static bool IsKey(string key, params string[] names)
        {
            foreach (var n in names)
                if (string.Equals(key, n, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int SetIfFound(AppSettings settings, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();
            switch (field)
            {
                case "YouTubeApiKey":
                    if (settings.YouTubeApiKey == value) return 0;
                    settings.YouTubeApiKey = value; return 1;
                case "GoogleClientId":
                    if (settings.GoogleClientId == value) return 0;
                    settings.GoogleClientId = value; return 1;
                case "GoogleClientSecret":
                    if (settings.GoogleClientSecret == value) return 0;
                    settings.GoogleClientSecret = value; return 1;
            }
            return 0;
        }
    }
}
