using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LumiaYT.Models;
using Newtonsoft.Json.Linq;

namespace LumiaYT.Services
{
    public static class YouTubeApiService
    {
        private const string BaseUrl = "https://www.googleapis.com/youtube/v3";

        public static async Task<VideoFeedResult> SearchAsync(string query, int maxResults = 20, string pageToken = "")
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.YouTubeApiKey)) return new VideoFeedResult();
            var url = BaseUrl + "/search?part=snippet&type=video&maxResults=" + maxResults
                + "&q=" + Uri.EscapeDataString(query)
                + "&key=" + Uri.EscapeDataString(settings.YouTubeApiKey);
            if (!string.IsNullOrWhiteSpace(pageToken)) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var cacheKey = "yt_search_" + query + "_" + maxResults + "_" + pageToken;
            var json = await CacheService.GetOrDownloadTextAsync(cacheKey, url, TimeSpan.FromMinutes(10));
            return ParseVideoFeed(json, false);
        }

        public static async Task<List<string>> GetSearchSuggestionsAsync(string query)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(query)) return list;
            try
            {
                var url = "https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&q=" + Uri.EscapeDataString(query);
                var json = await HttpService.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(json)) return list;
                var arr = JArray.Parse(json);
                if (arr.Count < 2) return list;
                var suggestions = arr[1] as JArray;
                if (suggestions == null) return list;
                foreach (var s in suggestions)
                {
                    var val = s.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) list.Add(val);
                }
            }
            catch { }
            return list;
        }

        public static async Task<VideoFeedResult> GetTrendingAsync(string regionCode = "BR", int maxResults = 20, string pageToken = "")
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.YouTubeApiKey)) return new VideoFeedResult();
            var url = BaseUrl + "/videos?part=snippet,statistics&chart=mostPopular&maxResults=" + maxResults
                + "&regionCode=" + Uri.EscapeDataString(regionCode)
                + "&key=" + Uri.EscapeDataString(settings.YouTubeApiKey);
            if (!string.IsNullOrWhiteSpace(pageToken)) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var cacheKey = "yt_trending_" + regionCode + "_" + maxResults + "_" + pageToken;
            var json = await CacheService.GetOrDownloadTextAsync(cacheKey, url, TimeSpan.FromMinutes(20));
            return ParseVideoFeed(json, true);
        }

        public static async Task<VideoFeedResult> GetRecommendedAsync(int maxResults = 20, string pageToken = "")
        {
            // Official Data API v3 has no "For You" feed.
            // Approximate recommendations: recent uploads from subscribed channels (personalized)
            // mixed with popular content, avoiding duplicates.
            var token = await AuthService.GetValidAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
            {
                if (string.IsNullOrWhiteSpace(pageToken))
                {
                    var subsFeed = await GetSubscriptionFeedAsync(maxResults, "");
                    if (subsFeed.Items.Count > 0)
                    {
                        subsFeed.NextPageToken = "subs:20";
                        return subsFeed;
                    }
                }
                else if (pageToken.StartsWith("subs:"))
                {
                    int offset = 0; int.TryParse(pageToken.Substring(5), out offset);
                    var subsFeed = await GetSubscriptionFeedAsync(maxResults, offset.ToString());
                    if (subsFeed.Items.Count > 0)
                    {
                        subsFeed.NextPageToken = string.IsNullOrWhiteSpace(subsFeed.NextPageToken) ? "trend:" : "subs:" + (offset + maxResults);
                        return subsFeed;
                    }
                    pageToken = "trend:";
                }

                if (pageToken.StartsWith("trend:"))
                {
                    var realToken = pageToken.Length > 6 ? pageToken.Substring(6) : "";
                    var trend = await GetTrendingAsync("BR", maxResults, realToken);
                    trend.NextPageToken = string.IsNullOrWhiteSpace(trend.NextPageToken) ? "" : "trend:" + trend.NextPageToken;
                    return trend;
                }
            }

            return await GetTrendingAsync("BR", maxResults, pageToken);
        }

        public static async Task<VideoFeedResult> GetMyActivitiesAsync(int maxResults = 20, string pageToken = "")
        {
            var token = await AuthService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return new VideoFeedResult();
            var url = BaseUrl + "/activities?part=snippet,contentDetails&home=true&maxResults=" + maxResults;
            if (!string.IsNullOrWhiteSpace(pageToken)) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var json = await HttpService.GetStringWithAuthAsync(url, token);
            return ParseActivityFeed(json);
        }

        public static async Task<ChannelFeedResult> GetSubscriptionChannelsAsync(int maxResults = 20, string pageToken = "")
        {
            var token = await AuthService.GetValidAccessTokenAsync();
            var result = new ChannelFeedResult();
            if (string.IsNullOrWhiteSpace(token)) return result;
            var url = BaseUrl + "/subscriptions?part=snippet&mine=true&maxResults=" + Math.Min(50, Math.Max(1, maxResults));
            if (!string.IsNullOrWhiteSpace(pageToken)) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var json = await HttpService.GetStringWithAuthAsync(url, token);
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var obj = JObject.Parse(json);
                result.NextPageToken = obj.Value<string>("nextPageToken") ?? "";
                var items = obj["items"] as JArray;
                if (items == null) return result;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    var chId = sn?["resourceId"]?.Value<string>("channelId") ?? "";
                    if (string.IsNullOrWhiteSpace(chId)) continue;
                    result.Items.Add(new ChannelItem
                    {
                        Id = chId,
                        Title = sn?.Value<string>("title") ?? "",
                        ThumbnailUrl = sn?["thumbnails"]?["default"]?.Value<string>("url") ?? ""
                    });
                }
            }
            catch { }
            return result;
        }

        public static async Task<VideoFeedResult> GetSubscriptionFeedAsync(int maxResults = 20, string pageToken = "")
        {
            // Build subscription feed by fetching recent uploads from subscribed channels.
            // pageToken is used as channel-page cursor in format "ch:<channelPage>|idx:<n>"
            var token = await AuthService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return new VideoFeedResult();

            var subs = await GetAllSubscriptionChannelsAsync();
            if (subs.Count == 0) return new VideoFeedResult();

            int start = 0;
            if (!string.IsNullOrWhiteSpace(pageToken) && int.TryParse(pageToken, out var p)) start = Math.Max(0, p);

            var slice = subs.Skip(start).Take(maxResults).ToList();
            var tasks = slice.Select(c => GetChannelRecentUploadsAsync(c.Id, 2)).ToList();
            var results = await Task.WhenAll(tasks);
            var feed = new VideoFeedResult();
            foreach (var r in results)
                feed.Items.AddRange(r);

            feed.Items = feed.Items
                .GroupBy(v => v.Id)
                .Select(g => g.First())
                .OrderByDescending(v => ParseDate(v.PublishedAt))
                .Take(maxResults)
                .ToList();

            var next = start + maxResults;
            feed.NextPageToken = next < subs.Count ? next.ToString() : "";
            return feed;
        }

        private static async Task<List<ChannelItem>> GetAllSubscriptionChannelsAsync()
        {
            var all = new List<ChannelItem>();
            string page = "";
            for (int i = 0; i < 5; i++) // cap to avoid heavy calls on SD400
            {
                var res = await GetSubscriptionChannelsAsync(50, page);
                all.AddRange(res.Items);
                if (string.IsNullOrWhiteSpace(res.NextPageToken)) break;
                page = res.NextPageToken;
            }
            return all.GroupBy(c => c.Id).Select(g => g.First()).ToList();
        }

        public static async Task<List<VideoItem>> GetChannelRecentUploadsAsync(string channelId, int maxResults = 5)
        {
            var settings = await SettingsService.GetAsync();
            if (string.IsNullOrWhiteSpace(settings.YouTubeApiKey) || string.IsNullOrWhiteSpace(channelId)) return new List<VideoItem>();
            var url = BaseUrl + "/search?part=snippet&type=video&order=date&maxResults=" + maxResults
                + "&channelId=" + Uri.EscapeDataString(channelId)
                + "&key=" + Uri.EscapeDataString(settings.YouTubeApiKey);
            var json = await CacheService.GetOrDownloadTextAsync("yt_chup_" + channelId + "_" + maxResults, url, TimeSpan.FromMinutes(15));
            return ParseVideoFeed(json, false).Items;
        }

        public static async Task<VideoFeedResult> GetPlaylistItemsAsync(string playlistId, int maxResults = 20, string pageToken = "")
        {
            var token = await AuthService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(playlistId)) return new VideoFeedResult();
            var url = BaseUrl + "/playlistItems?part=snippet,contentDetails&maxResults=" + maxResults
                + "&playlistId=" + Uri.EscapeDataString(playlistId);
            if (!string.IsNullOrWhiteSpace(pageToken)) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            var json = await HttpService.GetStringWithAuthAsync(url, token);
            return ParsePlaylistItemsFeed(json);
        }

        public static async Task<VideoFeedResult> GetMyChannelSectionAsync(string kind, int maxResults = 20, string pageToken = "")
        {
            var token = await AuthService.GetValidAccessTokenAsync();
            if (string.IsNullOrWhiteSpace(token)) return new VideoFeedResult();

            // history: YouTube Data API v3 does not expose personal watch history.
            if (kind == "history")
            {
                var local = await LocalLibraryService.GetHistoryAsync(maxResults);
                return new VideoFeedResult { Items = local };
            }

            var channelJson = await HttpService.GetStringWithAuthAsync(BaseUrl + "/channels?part=contentDetails&mine=true", token);
            if (string.IsNullOrWhiteSpace(channelJson)) return new VideoFeedResult();
            try
            {
                var obj = JObject.Parse(channelJson);
                var items = obj["items"] as JArray;
                if (items == null || items.Count == 0) return new VideoFeedResult();
                var related = items[0]?["contentDetails"]?["relatedPlaylists"];
                string playlistId = "";
                if (kind == "likes") playlistId = related?.Value<string>("likes") ?? "";
                else if (kind == "watchLater") playlistId = related?.Value<string>("watchLater") ?? "";
                else if (kind == "uploads") playlistId = related?.Value<string>("uploads") ?? "";

                if (!string.IsNullOrWhiteSpace(playlistId))
                {
                    var res = await GetPlaylistItemsAsync(playlistId, maxResults, pageToken);
                    if (res.Items.Count > 0) return res;
                }

                // Fallback for watchLater when API playlist is unavailable/private in this context
                if (kind == "watchLater")
                {
                    var local = await LocalLibraryService.GetWatchLaterAsync(maxResults);
                    return new VideoFeedResult { Items = local };
                }

                return new VideoFeedResult();
            }
            catch { return new VideoFeedResult(); }
        }

        public static async Task<VideoFeedResult> GetLikedVideosAsync(int maxResults = 20, string pageToken = "")
        {
            return await GetMyChannelSectionAsync("likes", maxResults, pageToken);
        }

        public static async Task<List<VideoItem>> GetMyPlaylistsAsync(int maxResults = 20)
        {
            var token = await AuthService.GetValidAccessTokenAsync();
            var list = new List<VideoItem>();
            if (string.IsNullOrWhiteSpace(token)) return list;
            var url = BaseUrl + "/playlists?part=snippet,contentDetails&mine=true&maxResults=" + maxResults;
            var json = await HttpService.GetStringWithAuthAsync(url, token);
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var obj = JObject.Parse(json);
                var items = obj["items"] as JArray;
                if (items == null) return list;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    var id = item.Value<string>("id") ?? "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var rawDate = sn?.Value<string>("publishedAt") ?? "";
                    list.Add(new VideoItem
                    {
                        Id = id,
                        Title = sn?.Value<string>("title") ?? "",
                        ChannelTitle = (item["contentDetails"]?.Value<int?>("itemCount")?.ToString() ?? "0") + " vídeos",
                        ThumbnailUrl = sn?["thumbnails"]?["medium"]?.Value<string>("url") ?? sn?["thumbnails"]?["default"]?.Value<string>("url") ?? "",
                        PublishedAtRaw = rawDate,
                        PublishedAt = FormatDate(rawDate),
                        Kind = "playlist"
                    });
                }
            }
            catch { }
            return list;
        }

        private static VideoFeedResult ParseVideoFeed(string json, bool isVideoList)
        {
            var result = new VideoFeedResult();
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var obj = JObject.Parse(json);
                result.NextPageToken = obj.Value<string>("nextPageToken") ?? "";
                var items = obj["items"] as JArray;
                if (items == null) return result;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    string id = isVideoList ? (item.Value<string>("id") ?? "") : (item["id"]?.Value<string>("videoId") ?? "");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var live = sn?.Value<string>("liveBroadcastContent");
                    var rawDate = sn?.Value<string>("publishedAt") ?? "";
                    result.Items.Add(new VideoItem
                    {
                        Id = id,
                        Title = sn?.Value<string>("title") ?? "",
                        ChannelTitle = sn?.Value<string>("channelTitle") ?? "",
                        ChannelId = sn?.Value<string>("channelId") ?? "",
                        Description = sn?.Value<string>("description") ?? "",
                        PublishedAtRaw = rawDate,
                        PublishedAt = FormatDate(rawDate),
                        ThumbnailUrl = sn?["thumbnails"]?["medium"]?.Value<string>("url") ?? sn?["thumbnails"]?["default"]?.Value<string>("url") ?? "",
                        IsLive = string.Equals(live, "live", StringComparison.OrdinalIgnoreCase),
                        ViewCount = item["statistics"]?.Value<string>("viewCount") ?? "",
                        Kind = "video"
                    });
                }
            }
            catch { }
            return result;
        }

        private static VideoFeedResult ParseActivityFeed(string json)
        {
            var result = new VideoFeedResult();
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var obj = JObject.Parse(json);
                result.NextPageToken = obj.Value<string>("nextPageToken") ?? "";
                var items = obj["items"] as JArray;
                if (items == null) return result;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    var cd = item["contentDetails"];
                    var vid = cd?.SelectToken("upload.videoId")?.ToString()
                              ?? cd?.SelectToken("recommendation.resourceId.videoId")?.ToString()
                              ?? cd?.SelectToken("like.resourceId.videoId")?.ToString()
                              ?? cd?.SelectToken("playlistItem.resourceId.videoId")?.ToString()
                              ?? cd?.SelectToken("social.resourceId.videoId")?.ToString()
                              ?? cd?.SelectToken("bulletin.resourceId.videoId")?.ToString();

                    if (string.IsNullOrWhiteSpace(vid)) continue;
                    var rawDate = sn?.Value<string>("publishedAt") ?? "";
                    result.Items.Add(new VideoItem
                    {
                        Id = vid,
                        Title = sn?.Value<string>("title") ?? "",
                        ChannelTitle = sn?.Value<string>("channelTitle") ?? "",
                        ChannelId = sn?.Value<string>("channelId") ?? "",
                        Description = sn?.Value<string>("description") ?? "",
                        PublishedAtRaw = rawDate,
                        PublishedAt = FormatDate(rawDate),
                        ThumbnailUrl = sn?["thumbnails"]?["medium"]?.Value<string>("url") ?? sn?["thumbnails"]?["default"]?.Value<string>("url") ?? "",
                        Kind = "video"
                    });
                }
            }
            catch { }
            return result;
        }

        private static VideoFeedResult ParsePlaylistItemsFeed(string json)
        {
            var result = new VideoFeedResult();
            if (string.IsNullOrWhiteSpace(json)) return result;
            try
            {
                var obj = JObject.Parse(json);
                result.NextPageToken = obj.Value<string>("nextPageToken") ?? "";
                var items = obj["items"] as JArray;
                if (items == null) return result;
                foreach (var item in items)
                {
                    var sn = item["snippet"];
                    var vid = sn?["resourceId"]?.Value<string>("videoId") ?? item["contentDetails"]?.Value<string>("videoId") ?? "";
                    if (string.IsNullOrWhiteSpace(vid)) continue;
                    var rawDate = sn?.Value<string>("publishedAt") ?? "";
                    result.Items.Add(new VideoItem
                    {
                        Id = vid,
                        Title = sn?.Value<string>("title") ?? "",
                        ChannelTitle = sn?.Value<string>("videoOwnerChannelTitle") ?? sn?.Value<string>("channelTitle") ?? "",
                        ChannelId = sn?.Value<string>("videoOwnerChannelId") ?? "",
                        Description = sn?.Value<string>("description") ?? "",
                        PublishedAtRaw = rawDate,
                        PublishedAt = FormatDate(rawDate),
                        ThumbnailUrl = sn?["thumbnails"]?["medium"]?.Value<string>("url") ?? sn?["thumbnails"]?["default"]?.Value<string>("url") ?? "",
                        Kind = "video"
                    });
                }
            }
            catch { }
            return result;
        }

        private static DateTimeOffset ParseDate(string value)
        {
            if (DateTimeOffset.TryParse(value, out var dt)) return dt;
            return DateTimeOffset.MinValue;
        }

        private static string FormatDate(string value)
        {
            if (!DateTimeOffset.TryParse(value, out var dt)) return "";
            var local = dt.ToLocalTime();
            var now = DateTimeOffset.Now;
            var diff = now - local;
            if (diff.TotalDays < 1) return "Hoje";
            if (diff.TotalDays < 2) return "Ontem";
            if (diff.TotalDays < 7) return ((int)diff.TotalDays) + " dias atrás";
            if (diff.TotalDays < 30) return Math.Max(1, (int)(diff.TotalDays / 7)) + " sem atrás";
            if (diff.TotalDays < 365) return Math.Max(1, (int)(diff.TotalDays / 30)) + " mês(es) atrás";
            return local.ToString("dd/MM/yyyy");
        }
    }
}
