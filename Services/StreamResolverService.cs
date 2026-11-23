using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        public bool IsVideoOnly { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public int Itag { get; set; }
        public int Height { get; set; }

        public string DisplayLabel
        {
            get
            {
                var q = string.IsNullOrWhiteSpace(QualityLabel) ? "Auto" : QualityLabel;
                var src = string.IsNullOrWhiteSpace(Source) ? "" : " - " + Source;
                return q + src;
            }
        }
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

        private static string _cachedPlayerJsUrl;
        private static string _cachedPlayerJs;
        private static Decipherer _cachedDecipherer;
        private static readonly object DecipherLock = new object();

        public static string LastDebug { get; private set; } = "";

        public static async Task<List<ResolvedStream>> ResolveAllAsync(string videoId)
        {
            LastDebug = "";
            if (string.IsNullOrWhiteSpace(videoId)) return new List<ResolvedStream>();
            var dbg = new ConcurrentBag<string>();
            var all = new List<ResolvedStream>();

            // Run InnerTube and Piped in parallel for speed
            var innerTask = ResolveInnerTubeAllAsync(videoId, dbg);
            var pipedTask = ResolvePipedAllAsync(videoId, dbg);

            await Task.WhenAll(innerTask, pipedTask);
            try { all.AddRange(innerTask.Result); } catch { }
            try { all.AddRange(pipedTask.Result); } catch { }

            // Deduplicate by URL, prefer better labels
            var dedup = all
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Url))
                .GroupBy(s => s.Url)
                .Select(g => g.First())
                .ToList();

            var ordered = OrderCandidates(dedup);
            LastDebug = string.Join("; ", dbg);
            return ordered;
        }

        public static async Task<ResolvedStream> ResolveAsync(string videoId)
        {
            var all = await ResolveAllAsync(videoId);
            return PickDefault(all);
        }

        public static ResolvedStream PickDefault(List<ResolvedStream> streams)
        {
            if (streams == null || streams.Count == 0) return null;
            var playable = streams.Where(c => !c.IsAudioOnly && !c.IsVideoOnly).ToList();
            if (playable.Count == 0) playable = streams.Where(c => !c.IsAudioOnly).ToList();
            if (playable.Count == 0) playable = streams.ToList();
            return playable.OrderByDescending(c => Score(c, true)).FirstOrDefault();
        }

        private static List<ResolvedStream> OrderCandidates(List<ResolvedStream> candidates)
        {
            return candidates
                .Where(c => c != null)
                .OrderBy(c => c.IsAudioOnly ? 2 : (c.IsVideoOnly ? 1 : 0))
                .ThenByDescending(c => c.Height)
                .ThenByDescending(c => Score(c, false))
                .ToList();
        }

        private static async Task<List<ResolvedStream>> ResolvePipedAllAsync(string videoId, ConcurrentBag<string> dbg)
        {
            var tasks = PipedInstances.Select(async inst =>
            {
                try
                {
                    var json = await HttpService.GetStringAsync(inst + "/streams/" + Uri.EscapeDataString(videoId));
                    var list = PickAllFromPiped(json);
                    foreach (var s in list) s.Source = "Piped";
                    return list;
                }
                catch { return new List<ResolvedStream>(); }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            var merged = new List<ResolvedStream>();
            foreach (var r in results) merged.AddRange(r);
            if (merged.Count > 0) dbg.Add("Piped:ok");
            else dbg.Add("Piped:falhou");
            return merged;
        }

        private sealed class InnerTubeClient
        {
            public string Name; public string Version; public string UserAgent; public int? AndroidSdk; public string ApiKey;
            public InnerTubeClient(string name, string version, string ua, int? sdk, string apiKey)
            { Name = name; Version = version; UserAgent = ua; AndroidSdk = sdk; ApiKey = apiKey; }
        }

        private static async Task<List<ResolvedStream>> ResolveInnerTubeAllAsync(string videoId, ConcurrentBag<string> dbg)
        {
            var clients = new[]
            {
                new InnerTubeClient("ANDROID_VR", "1.62.27", "com.google.android.apps.youtube.vr.oculus/1.62.27 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip", 32, "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI"),
                new InnerTubeClient("ANDROID", "19.17.34", null, 30, null),
                new InnerTubeClient("IOS", "19.16.3", "com.google.ios.youtube/19.16.3 (iPhone14,3; U; CPU iOS 15_6 like Mac OS X)", null, null),
                new InnerTubeClient("TVHTML5_SIMPLY_EMBEDDED_PLAYER", "2.0", null, null, null),
            };

            var tasks = clients.Select(c => ResolveViaInnerTubeAsync(videoId, c, dbg)).ToArray();
            var results = await Task.WhenAll(tasks);
            var all = new List<ResolvedStream>();
            foreach (var r in results) if (r != null) all.AddRange(r);
            return all;
        }

        private static async Task<List<ResolvedStream>> ResolveViaInnerTubeAsync(string videoId, InnerTubeClient client, ConcurrentBag<string> dbg)
        {
            var clientObj = new JObject
            {
                ["clientName"] = client.Name,
                ["clientVersion"] = client.Version,
                ["hl"] = "pt",
                ["gl"] = "BR"
            };
            if (client.AndroidSdk.HasValue) clientObj["androidSdkVersion"] = client.AndroidSdk.Value;
            if (!string.IsNullOrWhiteSpace(client.UserAgent)) clientObj["userAgent"] = client.UserAgent;
            clientObj["platform"] = client.Name.StartsWith("ANDROID") ? "MOBILE" : "DESKTOP";

            var body = new JObject
            {
                ["context"] = new JObject { ["client"] = clientObj },
                ["videoId"] = videoId,
                ["contentCheckOk"] = true,
                ["racyCheckOk"] = true
            };

            var endpoint = "https://www.youtube.com/youtubei/v1/player?prettyPrint=false";
            if (!string.IsNullOrWhiteSpace(client.ApiKey)) endpoint += "&key=" + client.ApiKey;

            var resp = await HttpService.PostJsonAsync(endpoint, body.ToString(Formatting.None));
            if (string.IsNullOrWhiteSpace(resp)) { dbg.Add(client.Name + ":empty"); return new List<ResolvedStream>(); }

            JObject obj;
            try { obj = JObject.Parse(resp); } catch { dbg.Add(client.Name + ":badjson"); return new List<ResolvedStream>(); }

            var status = obj.SelectToken("playabilityStatus.status")?.ToString() ?? "";
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                var reason = obj.SelectToken("playabilityStatus.reason")?.ToString() ?? status;
                dbg.Add(client.Name + ":" + Trim(reason, 30));
                return new List<ResolvedStream>();
            }

            var formats = new List<JToken>();
            var f1 = obj.SelectToken("streamingData.formats") as JArray;
            var f2 = obj.SelectToken("streamingData.adaptiveFormats") as JArray;
            if (f1 != null) formats.AddRange(f1);
            if (f2 != null) formats.AddRange(f2);
            if (formats.Count == 0) { dbg.Add(client.Name + ":noformats"); return new List<ResolvedStream>(); }

            var candidates = new List<ResolvedStream>();
            bool needsDecipher = false;
            foreach (var f in formats)
            {
                var url = f.Value<string>("url");
                var cipher = f.Value<string>("signatureCipher") ?? f.Value<string>("cipher");
                if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(cipher))
                {
                    var c = ParseCipher(cipher);
                    url = c.Url;
                    if (!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(c.S))
                    {
                        needsDecipher = true;
                        var dec = await GetDeciphererAsync(dbg);
                        if (dec != null)
                        {
                            var sig = dec.Decipher(c.S);
                            if (!string.IsNullOrWhiteSpace(sig))
                            {
                                var sp = string.IsNullOrWhiteSpace(c.Sp) ? "signature" : c.Sp;
                                url += "&" + sp + "=" + Uri.EscapeDataString(sig);
                            }
                            else { url = null; }
                        }
                        else { url = null; }
                    }
                }

                if (string.IsNullOrWhiteSpace(url)) continue;
                var mime = f.Value<string>("mimeType") ?? "";
                var quality = f.Value<string>("qualityLabel") ?? f.Value<string>("quality") ?? "";
                var audioQuality = f.Value<string>("audioQuality");
                bool audioOnly = string.IsNullOrWhiteSpace(quality) && !string.IsNullOrWhiteSpace(audioQuality);
                bool hasVideo = mime.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0 || !string.IsNullOrWhiteSpace(quality);
                bool videoOnly = hasVideo && !audioOnly && (mime.IndexOf("audio", StringComparison.OrdinalIgnoreCase) < 0 && !string.IsNullOrWhiteSpace(quality));
                int itag = f.Value<int?>("itag") ?? 0;
                int height = f.Value<int?>("height") ?? ParseHeight(quality);
                candidates.Add(new ResolvedStream
                {
                    Url = url,
                    QualityLabel = quality,
                    IsAudioOnly = audioOnly,
                    IsVideoOnly = videoOnly,
                    MimeType = mime,
                    Source = client.Name,
                    Itag = itag,
                    Height = height
                });
            }

            if (candidates.Count > 0) dbg.Add(client.Name + ":ok" + (needsDecipher ? "+dec" : ""));
            else dbg.Add(client.Name + ":nosel");
            return candidates;
        }

        private static int ParseHeight(string quality)
        {
            if (string.IsNullOrWhiteSpace(quality)) return 0;
            var m = Regex.Match(quality, @"(\d{3,4})");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var h)) return h;
            return 0;
        }

        private sealed class CipherParts { public string Url; public string S; public string Sp; public string N; }
        private static CipherParts ParseCipher(string cipher)
        {
            var parts = new CipherParts();
            if (string.IsNullOrWhiteSpace(cipher)) return parts;
            foreach (var p in cipher.Split('&'))
            {
                var idx = p.IndexOf('=');
                if (idx <= 0) continue;
                var k = p.Substring(0, idx);
                var v = Uri.UnescapeDataString(p.Substring(idx + 1));
                if (k == "url") parts.Url = v;
                else if (k == "s") parts.S = v;
                else if (k == "sp") parts.Sp = v;
                else if (k == "n") parts.N = v;
            }
            return parts;
        }

        private static async Task<Decipherer> GetDeciphererAsync(ConcurrentBag<string> dbg)
        {
            lock (DecipherLock)
            {
                if (_cachedDecipherer != null) return _cachedDecipherer;
            }

            try
            {
                var jsUrl = await FindPlayerJsUrlAsync();
                if (string.IsNullOrWhiteSpace(jsUrl)) { dbg.Add("js:notfound"); return null; }
                if (jsUrl == _cachedPlayerJsUrl && !string.IsNullOrWhiteSpace(_cachedPlayerJs))
                {
                    var d0 = Decipherer.Parse(_cachedPlayerJs);
                    lock (DecipherLock) { _cachedDecipherer = d0; }
                    return d0;
                }

                var js = await HttpService.GetStringAsync(jsUrl);
                if (string.IsNullOrWhiteSpace(js)) { dbg.Add("js:empty"); return null; }
                var dec = Decipherer.Parse(js);
                if (dec == null) { dbg.Add("js:parsefail"); return null; }
                lock (DecipherLock)
                {
                    _cachedPlayerJsUrl = jsUrl;
                    _cachedPlayerJs = js;
                    _cachedDecipherer = dec;
                }
                dbg.Add("js:ok");
                return dec;
            }
            catch (Exception ex)
            {
                dbg.Add("js:err=" + ex.Message);
                return null;
            }
        }

        private static async Task<string> FindPlayerJsUrlAsync()
        {
            var urls = new[]
            {
                "https://www.youtube.com/embed/dQw4w9WgXcQ",
                "https://www.youtube.com/iframe_api",
                "https://www.youtube.com/"
            };
            foreach (var pageUrl in urls)
            {
                try
                {
                    var html = await HttpService.GetStringAsync(pageUrl);
                    if (string.IsNullOrWhiteSpace(html)) continue;
                    var m = Regex.Match(html, @"/s/player/[a-zA-Z0-9_\-]+/[a-zA-Z0-9_\-./]+/base\.js");
                    if (m.Success) return "https://www.youtube.com" + m.Value;
                    m = Regex.Match(html, @"\""jsUrl\""\s*:\s*\""(/s/player/[^\\""]+base\.js)\""");
                    if (m.Success) return "https://www.youtube.com" + m.Groups[1].Value.Replace("\\/", "/");
                }
                catch { }
            }
            return null;
        }

        private sealed class Decipherer
        {
            private readonly List<DecipherOp> _ops;
            private Decipherer(List<DecipherOp> ops) { _ops = ops; }

            public string Decipher(string sig)
            {
                if (string.IsNullOrEmpty(sig) || _ops == null) return sig;
                var chars = sig.ToCharArray().ToList();
                foreach (var op in _ops)
                {
                    try
                    {
                        if (op.Type == DecipherOpType.Reverse) chars.Reverse();
                        else if (op.Type == DecipherOpType.Slice) { int n = Math.Min(op.Arg, chars.Count); chars.RemoveRange(0, n); }
                        else if (op.Type == DecipherOpType.Swap)
                        {
                            int n = op.Arg % chars.Count;
                            if (chars.Count > 0 && n < chars.Count) { var tmp = chars[0]; chars[0] = chars[n]; chars[n] = tmp; }
                        }
                    }
                    catch { }
                }
                return new string(chars.ToArray());
            }

            public static Decipherer Parse(string js)
            {
                if (string.IsNullOrWhiteSpace(js)) return null;
                var funcBody = FindDecipherFunctionBody(js);
                if (string.IsNullOrWhiteSpace(funcBody)) return null;

                var helperMatch = Regex.Match(funcBody, @"([A-Za-z0-9_$]{2,})\.[A-Za-z0-9_$]{2,}\([A-Za-z0-9_$],\s*\d+\)");
                if (!helperMatch.Success) return null;
                var helperName = helperMatch.Groups[1].Value;
                var helperObj = FindHelperObject(js, helperName);
                if (helperObj == null) return null;

                var ops = new List<DecipherOp>();
                var callMatches = Regex.Matches(funcBody, @"([A-Za-z0-9_$]{2,})\.([A-Za-z0-9_$]{2,})\(([A-Za-z0-9_$]),\s*(\d+)\)");
                foreach (Match cm in callMatches)
                {
                    var objName = cm.Groups[1].Value;
                    var meth = cm.Groups[2].Value;
                    var arg = int.Parse(cm.Groups[4].Value);
                    if (objName != helperName) continue;
                    DecipherOpType type;
                    if (!helperObj.TryGetValue(meth, out type)) continue;
                    ops.Add(new DecipherOp { Type = type, Arg = arg });
                }
                if (ops.Count == 0) return null;
                return new Decipherer(ops);
            }

            private static string FindDecipherFunctionBody(string js)
            {
                var patterns = new[]
                {
                    @"function\(([A-Za-z0-9_$])\)\{\1=\1\.split\(""""""\);(.+?);return \1\.join\(""""""\)\}",
                    @"\b([A-Za-z0-9_$]{2,})\s*=\s*function\(([A-Za-z0-9_$])\)\{\2=\2\.split\(""""""\);(.+?);return \2\.join\(""""""\)\}"
                };
                foreach (var pat in patterns)
                {
                    var m = Regex.Match(js, pat);
                    if (m.Success) return m.Groups[m.Groups.Count - 1].Value;
                }
                return null;
            }

            private static Dictionary<string, DecipherOpType> FindHelperObject(string js, string name)
            {
                var escaped = Regex.Escape(name);
                var m = Regex.Match(js, "var " + escaped + "=\\{(.+?)\\}\\};", RegexOptions.Singleline);
                if (!m.Success) m = Regex.Match(js, escaped + "=\\{(.+?)\\}\\};", RegexOptions.Singleline);
                if (!m.Success) return null;
                var objBody = m.Groups[1].Value;
                var dict = new Dictionary<string, DecipherOpType>();
                var methods = Regex.Matches(objBody, @"([A-Za-z0-9_$]{2,})\s*:\s*function\([^\)]*\)\{([^\}]*)\}");
                foreach (Match mm in methods)
                {
                    var meth = mm.Groups[1].Value;
                    var body = mm.Groups[2].Value;
                    if (body.Contains("reverse")) dict[meth] = DecipherOpType.Reverse;
                    else if (body.Contains("splice")) dict[meth] = DecipherOpType.Slice;
                    else if (Regex.IsMatch(body, @"var\s+[A-Za-z0-9_$]+\s*=\s*[A-Za-z0-9_$]+\[0\]")) dict[meth] = DecipherOpType.Swap;
                }
                return dict.Count > 0 ? dict : null;
            }
        }

        private enum DecipherOpType { Reverse, Slice, Swap }
        private sealed class DecipherOp { public DecipherOpType Type; public int Arg; }

        private static List<ResolvedStream> PickAllFromPiped(string json)
        {
            var list = new List<ResolvedStream>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            JObject obj;
            try { obj = JObject.Parse(json); } catch { return list; }

            Action<JToken, bool> addFromArray = (arr, audioOnly) =>
            {
                var a = arr as JArray;
                if (a == null) return;
                foreach (var s in a)
                {
                    var url = s.Value<string>("url") ?? "";
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    var mime = s.Value<string>("mimeType") ?? s.Value<string>("format") ?? "";
                    var quality = s.Value<string>("quality") ?? "";
                    bool videoOnly = s.Value<bool?>("videoOnly") ?? false;
                    list.Add(new ResolvedStream
                    {
                        Url = url,
                        QualityLabel = quality,
                        IsAudioOnly = audioOnly,
                        IsVideoOnly = videoOnly && !audioOnly,
                        MimeType = mime,
                        Height = ParseHeight(quality)
                    });
                }
            };

            addFromArray(obj["videoStreams"], false);
            addFromArray(obj["audioStreams"], true);
            return list;
        }

        private static int Score(ResolvedStream c, bool forDefault)
        {
            int score = 0;
            var mime = (c.MimeType ?? "").ToLowerInvariant();
            var q = (c.QualityLabel ?? "").ToLowerInvariant();
            int h = c.Height > 0 ? c.Height : ParseHeight(q);
            if (mime.Contains("mp4")) score += 100;
            if (mime.Contains("h264") || mime.Contains("avc")) score += 30;
            if (c.Url.IndexOf("googlevideo", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;

            if (forDefault)
            {
                // Prefer fast start on Lumia 640: 360p > 240p > 480p > 720p > 144p
                if (h == 360) score += 120;
                else if (h == 240) score += 110;
                else if (h == 480) score += 90;
                else if (h == 144) score += 70;
                else if (h == 720) score += 60;
                else if (h >= 1080) score -= 20;
            }
            else
            {
                if (h > 0) score += Math.Min(h, 720) / 10;
            }

            if (q.Contains("audio")) score -= 200;
            if (c.IsVideoOnly) score -= 500;
            return score;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
