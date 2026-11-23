namespace LumiaYT.Models
{
    public sealed class AppSettings
    {
        public string YouTubeApiKey { get; set; } = string.Empty;
        public string GoogleClientId { get; set; } = string.Empty;
        public string GoogleClientSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string TokenExpiryUtc { get; set; } = string.Empty;
        public string PreferredQuality { get; set; } = "360p";

        public string UserDisplayName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string UserAvatarUrl { get; set; } = string.Empty;
        public string UserChannelId { get; set; } = string.Empty;
        public string UserChannelTitle { get; set; } = string.Empty;
    }
}
