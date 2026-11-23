using System.Collections.Generic;

namespace LumiaYT.Models
{
    public sealed class VideoFeedResult
    {
        public List<VideoItem> Items { get; set; } = new List<VideoItem>();
        public string NextPageToken { get; set; } = string.Empty;
    }

    public sealed class ChannelItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
    }

    public sealed class ChannelFeedResult
    {
        public List<ChannelItem> Items { get; set; } = new List<ChannelItem>();
        public string NextPageToken { get; set; } = string.Empty;
    }
}
