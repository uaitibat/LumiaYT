namespace LumiaYT.Models
{
    public sealed class VideoItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ChannelTitle { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PublishedAt { get; set; } = string.Empty;
        public bool IsLive { get; set; }
        public string ViewCount { get; set; } = string.Empty;
        public string Kind { get; set; } = "video"; // video | channel
    }
}
