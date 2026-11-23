using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json.Linq;

namespace LumiaYT.Services
{
    public static class YouTubeApiService
    {
        private const string BaseUrl = "https://www.googleapis.com/youtube/v3";

        public static async Task<List<VideoItem>> SearchAsync(string query, int maxResults = 20)
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.YouTubeApiKey)) return new List<VideoItem>();
            var url = BaseUrl + "/search?part=snippet&type=video&maxResults=" + maxResults
                + "&q=" + Uri.EscapeDataString(query)
                + "&key=" + Uri.EscapeDataString(settings.YouTubeApiKey);
            var json = await CacheService.GetOrDownloadTextAsync("yt_search_" + query + "_" + maxResults, url, TimeSpan.FromMinutes(15));
            return ParseVideoItems(json);
        }

        public static async Task<List<VideoItem>> GetTrendingAsync(string regionCode = "BR", int maxResults = 20)
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.YouTubeApiKey)) return new List<VideoItem>();
            var url = BaseUrl + "/videos?part=snippet,statistics&chart=mostPopular&maxResults=" + maxResults
                + "&regionCode=" + Uri.EscapeDataString(regionCode)
                + "&key=" + Uri.EscapeDataString(settings.YouTubeApiKey);
            var json = await CacheService.GetOrDownloadTextAsync("yt_trending_" + regionCode + "_" + maxResults, url, TimeSpan.FromMinutes(30));
            return ParseVideoItems(json, isVideoList: true);
        }

        public static async Task<List<VideoItem>> GetRecommendedAsync(int maxResults = 20)
        {
            // YouTube Data API v3 has no personalized home feed. Approximate with popular videos.
            // When logged in, HomePage also shows subscriptions; this keeps a "Recommended" section.
            var list = await GetTrendingAsync("BR", maxResults);
            return list;
        }

        public static async Task<List<VideoItem>> GetSubscriptionsAsync(int maxResults = 20)
        {
            var token = await AuthService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return new List<VideoItem>();

            var url = BaseUrl + "/subscriptions?part=snippet&mine=true&maxResults=" + maxResults;
            var json = await HttpService.GetStringWithAuthAsync(url, token);
            var list = new List<VideoItem>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var obj = JObject.Parse(json);
                var items = obj["items"] as JArray;
                if (items == null) return list;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    var chId = sn?["resourceId"]?.Value<string>("channelId") ?? "";
                    list.Add(new VideoItem
                    {
                        Id = chId,
                        ChannelId = chId,
                        Title = sn?.Value<string>("title") ?? "",
                        ChannelTitle = sn?.Value<string>("title") ?? "",
                        Description = sn?.Value<string>("description") ?? "",
                        ThumbnailUrl = sn?["thumbnails"]?["medium"]?.Value<string>("url") ?? "",
                        PublishedAt = sn?.Value<string>("publishedAt") ?? ""
                    });
                }
            }
            catch { }
            return list;
        }

        private static List<VideoItem> ParseVideoItems(string json, bool isVideoList = false)
        {
            var list = new List<VideoItem>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var obj = JObject.Parse(json);
                var items = obj["items"] as JArray;
                if (items == null) return list;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    string id = "";
                    if (isVideoList) id = item.Value<string>("id") ?? "";
                    else id = item["id"]?.Value<string>("videoId") ?? "";

                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var live = sn?.Value<string>("liveBroadcastContent");
                    list.Add(new VideoItem
                    {
                        Id = id,
                        Title = sn?.Value<string>("title") ?? "",
                        ChannelTitle = sn?.Value<string>("channelTitle") ?? "",
                        ChannelId = sn?.Value<string>("channelId") ?? "",
                        Description = sn?.Value<string>("description") ?? "",
                        PublishedAt = sn?.Value<string>("publishedAt") ?? "",
                        ThumbnailUrl = sn?["thumbnails"]?["medium"]?.Value<string>("url") ?? "",
                        IsLive = string.Equals(live, "live", StringComparison.OrdinalIgnoreCase),
                        ViewCount = item["statistics"]?.Value<string>("viewCount") ?? ""
                    });
                }
            }
            catch { }
            return list;
        }
    }
}
