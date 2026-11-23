using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LumiaYT.Services
{
    public sealed class ResolvedStream
    {
        public string Url { get; set; } = string.Empty;
        public string QualityLabel { get; set; } = string.Empty;
        public bool IsAudioOnly { get; set; }
        public string MimeType { get; set; } = string.Empty;
    }

    public static class StreamResolverService
    {
        private static readonly string[] PipedInstances = new[]
        {
            "https://pipedapi.kavin.rocks",
            "https://pipedapi.adminforge.de",
            "https://api.piped.private.coffee",
            "https://pipedapi.in.projectsegfau.lt"
        };

        public static async Task<ResolvedStream> ResolveAsync(string videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId)) return null;

            // 1) Try Piped API (often returns direct/proxy URLs usable on clients)
            foreach (var inst in PipedInstances)
            {
                try
                {
                    var json = await HttpService.GetStringAsync(inst + "/streams/" + Uri.EscapeDataString(videoId));
                    var stream = PickFromPiped(json);
                    if (stream != null) return stream;
                }
                catch { }
            }

            // 2) Try InnerTube android player endpoint
            try
            {
                var it = await ResolveViaInnerTubeAsync(videoId);
                if (it != null) return it;
            }
            catch { }

            return null;
        }

        private static ResolvedStream PickFromPiped(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var obj = JObject.Parse(json);
            var candidates = new List<ResolvedStream>();

            void AddFromArray(JToken arr, bool audioOnly)
            {
                var a = arr as JArray;
                if (a == null) return;
                foreach (var s in a)
                {
                    var url = s.Value<string>("url") ?? "";
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    var mime = s.Value<string>("mimeType") ?? s.Value<string>("format") ?? "";
                    candidates.Add(new ResolvedStream
                    {
                        Url = url,
                        QualityLabel = s.Value<string>("quality") ?? "",
                        IsAudioOnly = audioOnly,
                        MimeType = mime
                    });
                }
            }

            AddFromArray(obj["videoStreams"], false);
            AddFromArray(obj["audioStreams"], true);

            // Prefer non-audio, mp4/h264, <=720p
            var best = candidates
                .Where(c => !c.IsAudioOnly)
                .OrderByDescending(c => Score(c))
                .FirstOrDefault();

            return best ?? candidates.OrderByDescending(c => Score(c)).FirstOrDefault();
        }

        private static int Score(ResolvedStream c)
        {
            int score = 0;
            var mime = (c.MimeType ?? "").ToLowerInvariant();
            var q = (c.QualityLabel ?? "").ToLowerInvariant();
            if (mime.Contains("mp4")) score += 100;
            if (mime.Contains("h264") || mime.Contains("avc")) score += 30;
            if (q.Contains("360")) score += 50;
            else if (q.Contains("480")) score += 45;
            else if (q.Contains("720")) score += 40;
            else if (q.Contains("240")) score += 30;
            else if (q.Contains("144")) score += 20;
            if (q.Contains("1080") || q.Contains("1440") || q.Contains("2160")) score -= 20; // too heavy for SD400
            if (c.Url.Contains("googlevideo")) score += 10;
            return score;
        }

        private static async Task<ResolvedStream> ResolveViaInnerTubeAsync(string videoId)
        {
            var body = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "ANDROID",
                        clientVersion = "19.17.34",
                        androidSdkVersion = 30,
                        hl = "pt",
                        gl = "BR"
                    }
                },
                videoId = videoId,
                contentCheckOk = true,
                racyCheckOk = true
            };

            var jsonBody = JsonConvert.SerializeObject(body);
            var resp = await HttpService.PostJsonAsync("https://www.youtube.com/youtubei/v1/player?prettyPrint=false", jsonBody);
            if (string.IsNullOrWhiteSpace(resp)) return null;

            var obj = JObject.Parse(resp);
            var status = obj.SelectToken("playabilityStatus.status")?.ToString();
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                return null;

            var formats = new List<JToken>();
            var f1 = obj.SelectToken("streamingData.formats") as JArray;
            var f2 = obj.SelectToken("streamingData.adaptiveFormats") as JArray;
            if (f1 != null) formats.AddRange(f1);
            if (f2 != null) formats.AddRange(f2);

            var candidates = new List<ResolvedStream>();
            foreach (var f in formats)
            {
                var url = f.Value<string>("url");
                var cipher = f.Value<string>("signatureCipher") ?? f.Value<string>("cipher");
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(cipher))
                {
                    url = TryExtractUrlFromCipher(cipher);
                    // signature decryption not implemented; only works when url present or no sig needed
                    if (!string.IsNullOrWhiteSpace(url) && (cipher.Contains("&sig=") || cipher.Contains("&lsig=")))
                    {
                        // url already has signature param
                    }
                    else if (!string.IsNullOrWhiteSpace(url) && cipher.Contains("s="))
                    {
                        // needs decipher; skip
                        url = null;
                    }
                }

                if (string.IsNullOrWhiteSpace(url)) continue;
                var mime = f.Value<string>("mimeType") ?? "";
                var quality = f.Value<string>("qualityLabel") ?? f.Value<string>("quality") ?? "";
                var audioQuality = f.Value<string>("audioQuality");
                bool audioOnly = string.IsNullOrWhiteSpace(quality) && !string.IsNullOrWhiteSpace(audioQuality);
                candidates.Add(new ResolvedStream
                {
                    Url = url,
                    QualityLabel = quality,
                    IsAudioOnly = audioOnly,
                    MimeType = mime
                });
            }

            return candidates
                .Where(c => !c.IsAudioOnly)
                .OrderByDescending(c => Score(c))
                .FirstOrDefault();
        }

        private static string TryExtractUrlFromCipher(string cipher)
        {
            try
            {
                var parsed = cipher.Split('&');
                string url = null;
                foreach (var p in parsed)
                {
                    var idx = p.IndexOf('=');
                    if (idx <= 0) continue;
                    var k = p.Substring(0, idx);
                    var v = Uri.UnescapeDataString(p.Substring(idx + 1));
                    if (k == "url") url = v;
                }
                return url;
            }
            catch { return null; }
        }
    }
}
