// Networking: Supabase REST (PostgREST + Storage) and Gemini title extraction.
// Everything here is called from background threads only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Tactile
{
    public sealed class Config
    {
        public string SupabaseUrl = "";
        public string SupabaseKey = "";
        public string SupabaseBucket = "attachments";
        public string GeminiKey = "";
        public string[] GeminiModels = { "gemini-3.5-flash-lite", "gemini-3.1-flash-lite-preview" };
        public bool SyncEnabled = true;

        public static Config Load(string path)
        {
            var c = new Config();
            try
            {
                if (!File.Exists(path)) return c;
                var d = Json.Obj(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (d == null) return c;
                c.SupabaseUrl = (Json.Str(d, "supabaseUrl") ?? c.SupabaseUrl).TrimEnd('/');
                c.SupabaseKey = Json.Str(d, "supabaseKey") ?? c.SupabaseKey;
                c.SupabaseBucket = Json.Str(d, "supabaseBucket") ?? c.SupabaseBucket;
                c.GeminiKey = Json.Str(d, "geminiKey") ?? c.GeminiKey;
                var models = Json.Arr(Json.Get(d, "geminiModels"));
                if (models != null && models.Count > 0) c.GeminiModels = models.OfType<string>().ToArray();
                if (Json.Get(d, "syncEnabled") is bool) c.SyncEnabled = (bool)Json.Get(d, "syncEnabled");
            }
            catch { }
            if (string.IsNullOrWhiteSpace(c.SupabaseUrl) || string.IsNullOrWhiteSpace(c.SupabaseKey)) c.SyncEnabled = false;
            var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(c.GeminiKey) && !string.IsNullOrWhiteSpace(envKey)) c.GeminiKey = envKey.Trim();
            return c;
        }
    }

    /// Thrown for errors worth retrying (offline, timeouts, 5xx, 429).
    public sealed class TransientNetException : Exception
    {
        public readonly int Code;
        public TransientNetException(string msg, Exception inner, int code = 0) : base(msg, inner) { Code = code; }
    }

    /// Thrown for permanent HTTP errors (4xx other than 408/429).
    public sealed class RejectedNetException : InvalidOperationException
    {
        public readonly int Code;
        public RejectedNetException(string msg, Exception inner, int code) : base(msg, inner) { Code = code; }
    }

    public static class Http
    {
        static Http()
        {
            // .NET Framework defaults can be conservative; allow modern TLS explicitly.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288 /* Tls13 */;
            ServicePointManager.DefaultConnectionLimit = 8;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
        }

        public static void Init() { }

        public static string Send(string method, string url, IDictionary<string, string> headers, byte[] body, string contentType, int timeoutMs)
        {
            int status;
            byte[] bytes = SendRaw(method, url, headers, body, contentType, timeoutMs, out status);
            return Encoding.UTF8.GetString(bytes);
        }

        public static byte[] SendRaw(string method, string url, IDictionary<string, string> headers, byte[] body, string contentType, int timeoutMs, out int status)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.UserAgent = "TactileTracker-Windows/1.0";
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    if (kv.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = kv.Value;
                    else req.Headers[kv.Key] = kv.Value;
                }
            }
            try
            {
                if (body != null)
                {
                    req.ContentType = contentType ?? "application/json";
                    req.ContentLength = body.Length;
                    using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)resp.StatusCode;
                    return ReadAll(resp.GetResponseStream());
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null) throw new TransientNetException("Network error: " + ex.Status, ex);
                int code = (int)resp.StatusCode;
                string text = "";
                try { text = Encoding.UTF8.GetString(ReadAll(resp.GetResponseStream())); } catch { }
                resp.Close();
                if (code >= 500 || code == 429 || code == 408) throw new TransientNetException("HTTP " + code + ": " + text, ex, code);
                throw new RejectedNetException("HTTP " + code + ": " + text, ex, code);
            }
        }

        static byte[] ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }

    public sealed class Supabase
    {
        readonly Config cfg;
        public Supabase(Config cfg) { this.cfg = cfg; }

        Dictionary<string, string> Headers(string prefer)
        {
            var h = new Dictionary<string, string>();
            h["apikey"] = cfg.SupabaseKey;
            h["Authorization"] = "Bearer " + cfg.SupabaseKey;
            h["Accept"] = "application/json";
            if (prefer != null) h["Prefer"] = prefer;
            return h;
        }

        public List<Dictionary<string, object>> Select(string table, string query)
        {
            string text = Http.Send("GET", cfg.SupabaseUrl + "/rest/v1/" + table + "?" + query, Headers(null), null, null, 15000);
            var arr = Json.Arr(Json.Parse(text)) ?? new List<object>();
            return arr.Select(Json.Obj).Where(o => o != null).ToList();
        }

        public void Upsert(string table, Dictionary<string, object> row)
        {
            byte[] body = Encoding.UTF8.GetBytes(Json.Write(row));
            Http.Send("POST", cfg.SupabaseUrl + "/rest/v1/" + table + "?on_conflict=id",
                Headers("resolution=merge-duplicates,return=minimal"), body, "application/json", 15000);
        }

        public void Delete(string table, string id)
        {
            Http.Send("DELETE", cfg.SupabaseUrl + "/rest/v1/" + table + "?id=eq." + Uri.EscapeDataString(id),
                Headers("return=minimal"), null, null, 20000);
        }

        /// Uploads to the storage bucket; returns the public URL, or null if the bucket is unavailable.
        public string TryUploadImage(byte[] data, string mime)
        {
            try
            {
                string ext = mime.Contains("png") ? "png" : mime.Contains("webp") ? "webp" : "jpg";
                string path = "pasted/" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + "." + ext;
                var h = Headers(null);
                h["x-upsert"] = "true";
                Http.Send("POST", cfg.SupabaseUrl + "/storage/v1/object/" + cfg.SupabaseBucket + "/" + path, h, data, mime, 30000);
                return cfg.SupabaseUrl + "/storage/v1/object/public/" + cfg.SupabaseBucket + "/" + path;
            }
            catch { return null; }
        }
    }

    public static class Gemini
    {
        const string Instruction =
            "You extract a short, clear, actionable task title (3 to 8 words max) from a developer prompt intended for a coding session. " +
            "Preserve the language of the prompt. Strip conversational filler like \"So I want to\", \"By the way\", or \"Can you make\". " +
            "Return only the concise title in the JSON field `title`, with no trailing period and no quotes.";

        static string workingModel;

        public static string ExtractTitle(Config cfg, string prompt)
        {
            if (string.IsNullOrWhiteSpace(cfg.GeminiKey) || string.IsNullOrWhiteSpace(prompt)) return null;
            var models = new List<string>();
            if (workingModel != null) models.Add(workingModel);
            models.AddRange(cfg.GeminiModels.Where(m => m != workingModel));
            foreach (var model in models)
            {
                try
                {
                    string t = Call(cfg, model, prompt.Trim());
                    if (!string.IsNullOrWhiteSpace(t)) { workingModel = model; return t.Trim(); }
                }
                catch { }
            }
            return null;
        }

        static Dictionary<string, object> D(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        static List<object> L(params object[] items) { return items.ToList(); }

        static string Call(Config cfg, string model, string prompt)
        {
            var body = D(
                "systemInstruction", D("parts", L(D("text", Instruction))),
                "contents", L(D("role", "user", "parts", L(D("text", prompt)))),
                "generationConfig", D(
                    "responseMimeType", "application/json",
                    "responseSchema", D("type", "OBJECT",
                        "properties", D("title", D("type", "STRING", "description", "Concise 3-8 word session title.")),
                        "required", L("title"))));
            var h = new Dictionary<string, string>();
            h["x-goog-api-key"] = cfg.GeminiKey;
            string text = Http.Send("POST", "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent",
                h, Encoding.UTF8.GetBytes(Json.Write(body)), "application/json", 20000);
            var root = Json.Obj(Json.Parse(text));
            var cands = Json.Arr(Json.Get(root, "candidates"));
            if (cands == null || cands.Count == 0) return null;
            var parts = Json.Arr(Json.Get(Json.Obj(Json.Get(Json.Obj(cands[0]), "content")), "parts"));
            if (parts == null || parts.Count == 0) return null;
            string inner = Json.Str(Json.Obj(parts[0]), "text");
            if (inner == null) return null;
            return Json.Str(Json.Obj(Json.Parse(inner.Trim())), "title");
        }
    }
}
