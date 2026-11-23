using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json;
using Windows.Storage;

namespace LumiaYT.Services
{
    public static class LocalLibraryService
    {
        private const string HistoryFile = "history.json";
        private const string WatchLaterFile = "watchlater.json";
        private static List<VideoItem> _history;
        private static List<VideoItem> _watchLater;

        public static async Task AddToHistoryAsync(VideoItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;
            var list = await GetHistoryAsync();
            list.RemoveAll(v => v.Id == item.Id);
            list.Insert(0, CloneLite(item));
            if (list.Count > 100) list = list.Take(100).ToList();
            _history = list;
            await SaveAsync(HistoryFile, list);
        }

        public static async Task<List<VideoItem>> GetHistoryAsync(int max = 20)
        {
            if (_history == null) _history = await LoadAsync(HistoryFile);
            return _history.Take(max).ToList();
        }

        public static async Task AddToWatchLaterAsync(VideoItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;
            var list = await GetWatchLaterAsync(500);
            if (list.Any(v => v.Id == item.Id)) return;
            list.Insert(0, CloneLite(item));
            _watchLater = list;
            await SaveAsync(WatchLaterFile, list);
        }

        public static async Task<List<VideoItem>> GetWatchLaterAsync(int max = 20)
        {
            if (_watchLater == null) _watchLater = await LoadAsync(WatchLaterFile);
            return _watchLater.Take(max).ToList();
        }

        private static VideoItem CloneLite(VideoItem item)
        {
            return new VideoItem
            {
                Id = item.Id,
                Title = item.Title,
                ChannelTitle = item.ChannelTitle,
                ChannelId = item.ChannelId,
                ThumbnailUrl = item.ThumbnailUrl,
                PublishedAt = item.PublishedAt,
                Kind = "video"
            };
        }

        private static async Task<List<VideoItem>> LoadAsync(string name)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(name) as StorageFile;
                if (file == null) return new List<VideoItem>();
                var json = await FileIO.ReadTextAsync(file);
                return JsonConvert.DeserializeObject<List<VideoItem>>(json) ?? new List<VideoItem>();
            }
            catch { return new List<VideoItem>(); }
        }

        private static async Task SaveAsync(string name, List<VideoItem> list)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, JsonConvert.SerializeObject(list));
            }
            catch { }
        }
    }
}
