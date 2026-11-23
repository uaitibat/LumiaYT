using System;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json;
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
    }
}
