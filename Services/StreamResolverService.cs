using System;
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
        public string MimeType { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
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

        public static async Task<ResolvedStream> ResolveAsync(string videoId)
        {
            LastDebug = "";
            if (string.IsNullOrWhiteSpace(videoId)) return null;
            var dbg = new List<string>();

            // 1) InnerTube - try clients that often return direct URLs on legacy devices
            var clients = new[]
            {
                new InnerTubeClient("ANDROID_VR", "1.62.27", "com.google.android.apps.youtube.vr.oculus/1.62.27 (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip", 32, "AIzaSyDSXy9qVx1CzG2S7hYy7G-F6-HQ8_kB4vI"),
                new InnerTubeClient("ANDROID", "19.17.34", null, 30, null),
                new InnerTubeClient("IOS", "19.16.3", "com.google.ios.youtube/19.16.3 (iPhone14,3; U; CPU iOS 15_6 like Mac OS X)", null, null),
                new InnerTubeClient("TVHTML5_SIMPLY_EMBEDDED_PLAYER", "2.0", null, null, null),
            };

            foreach (var client in clients)
            {
                try
                {
                    var result = await ResolveViaInnerTubeAsync(videoId, client, dbg);
                    if (result != null)
                    {
                        LastDebug = string.Join(" | ", dbg);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    dbg.Add(client.Name + ":err=" + ex.Message);
                }
            }

            // 2) Piped fallback
            bool pipedTried = false;
            foreach (var inst in PipedInstances)
            {
                try
                {
                    var json = await HttpService.GetStringAsync(inst + "/streams/" + Uri.EscapeDataString(videoId));
                    var stream = PickFromPiped(json);
                    if (stream != null)
                    {
                        stream.Source = "Piped";
                        dbg.Add("Piped:ok");
                        LastDebug = string.Join("; ", dbg);
                        return stream;
                    }
                    pipedTried = true;
                }
                catch { pipedTried = true; }
            }
            if (pipedTried) dbg.Add("Piped:falhou");

            LastDebug = string.Join("; ", dbg);
            return null;
        }

        private sealed class InnerTubeClient
        {
            public string Name; public string Version; public string UserAgent; public int? AndroidSdk; public string ApiKey;
            public InnerTubeClient(string name, string version, string ua, int? sdk, string apiKey)
            { Name = name; Version = version; UserAgent = ua; AndroidSdk = sdk; ApiKey = apiKey; }
        }

        private static async Task<ResolvedStream> ResolveViaInnerTubeAsync(string videoId, InnerTubeClient client, List<string> dbg)
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
            if (string.IsNullOrWhiteSpace(resp)) { dbg.Add(client.Name + ":empty"); return null; }

            JObject obj;
            try { obj = JObject.Parse(resp); } catch { dbg.Add(client.Name + ":badjson"); return null; }

            var status = obj.SelectToken("playabilityStatus.status")?.ToString() ?? "";
            if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
            {
                var reason = obj.SelectToken("playabilityStatus.reason")?.ToString() ?? status;
                dbg.Add(client.Name + ":" + Trim(reason, 40));
                return null;
            }

            var formats = new List<JToken>();
            var f1 = obj.SelectToken("streamingData.formats") as JArray;
            var f2 = obj.SelectToken("streamingData.adaptiveFormats") as JArray;
            if (f1 != null) formats.AddRange(f1);
            if (f2 != null) formats.AddRange(f2);
            if (formats.Count == 0) { dbg.Add(client.Name + ":noformats"); return null; }

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
                                if (!string.IsNullOrWhiteSpace(c.N))
                                {
                                    // n-param transform not implemented; keep url as-is
                                }
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
                candidates.Add(new ResolvedStream
                {
                    Url = url,
                    QualityLabel = quality,
                    IsAudioOnly = audioOnly,
                    MimeType = mime,
                    Source = client.Name
                });
            }

            var best = candidates.Where(c => !c.IsAudioOnly).OrderByDescending(c => Score(c)).FirstOrDefault()
                ?? candidates.OrderByDescending(c => Score(c)).FirstOrDefault();
            if (best != null)
            {
                dbg.Add(client.Name + ":ok" + (needsDecipher ? "+dec" : ""));
                return best;
            }
            dbg.Add(client.Name + ":nosel");
            return null;
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

        private static async Task<Decipherer> GetDeciphererAsync(List<string> dbg)
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
            // Try embed page first (smaller)
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
                // Find decipher function name: ...a=a.split("");XX.YY(a,NN);...;return a.join("")
                var funcBody = FindDecipherFunctionBody(js);
                if (string.IsNullOrWhiteSpace(funcBody)) return null;

                // Find helper object name used in body, e.g. AB.CD(a,1)
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
                // reverse call: a.reverse()
                if (Regex.IsMatch(funcBody, @"\.reverse\(\)")) { /* reverse may be direct; handled above? rare in modern */ }

                // Also handle bare reverse in function body order? Common pattern has only helper calls.
                if (ops.Count == 0) return null;
                return new Decipherer(ops);
            }

            private static string FindDecipherFunctionBody(string js)
            {
                // Pattern 1: function(a){a=a.split("");...;return a.join("")}
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
                // method:def pairs
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

        private static ResolvedStream PickFromPiped(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var obj = JObject.Parse(json);
            var candidates = new List<ResolvedStream>();

            Action<JToken, bool> addFromArray = (arr, audioOnly) =>
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
            };

            addFromArray(obj["videoStreams"], false);
            addFromArray(obj["audioStreams"], true);

            var best = candidates.Where(c => !c.IsAudioOnly).OrderByDescending(c => Score(c)).FirstOrDefault();
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
            if (q.Contains("1080") || q.Contains("1440") || q.Contains("2160")) score -= 20;
            if (c.Url.IndexOf("googlevideo", StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
            return score;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
