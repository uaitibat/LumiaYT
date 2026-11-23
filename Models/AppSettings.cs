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
    }
}
