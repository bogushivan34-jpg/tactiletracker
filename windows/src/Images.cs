// Image attachments: lazy, cached, off-thread decoding (data: URLs and remote URLs),
// clipboard/file import, and resize+compress before upload (mirrors imageAttachments.ts).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tactile
{
    public static class Images
    {
        public static string CacheDir;
        public static Supabase Storage;
        static readonly Dictionary<string, BitmapSource> mem = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, List<Action<BitmapSource>>> inflight = new Dictionary<string, List<Action<BitmapSource>>>();
        static readonly SemaphoreSlim gate = new SemaphoreSlim(3);

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                // Hash a bounded sample for huge data URLs (length + head + tail is unique enough for a cache key).
                string sample = s.Length > 4096 ? s.Length + "|" + s.Substring(0, 2048) + "|" + s.Substring(s.Length - 2048) : s;
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sample))).Replace("-", "").ToLowerInvariant();
            }
        }

        /// Loads src decoded to at most decodeWidth pixels wide (0 = full size). Callback runs on the UI thread.
        public static void Load(string src, int decodeWidth, Dispatcher ui, Action<BitmapSource> done)
        {
            if (string.IsNullOrEmpty(src)) { done(null); return; }
            string key = Hash(src) + "@" + decodeWidth;
            lock (mem)
            {
                BitmapSource cached;
                if (mem.TryGetValue(key, out cached)) { done(cached); return; }
                List<Action<BitmapSource>> waiters;
                if (inflight.TryGetValue(key, out waiters)) { waiters.Add(done); return; }
                inflight[key] = new List<Action<BitmapSource>> { done };
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                BitmapSource bmp = null;
                gate.Wait();
                try
                {
                    byte[] bytes = GetBytes(src);
                    if (bytes != null) bmp = Decode(bytes, decodeWidth);
                }
                catch (Exception ex) { Log.Write("Image load failed: " + ex.Message); }
                finally { gate.Release(); }
                List<Action<BitmapSource>> list;
                lock (mem)
                {
                    if (bmp != null) mem[key] = bmp;
                    list = inflight[key];
                    inflight.Remove(key);
                }
                ui.BeginInvoke(new Action(() => { foreach (var cb in list) cb(bmp); }));
            });
        }

        static byte[] GetBytes(string src)
        {
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = src.IndexOf(',');
                if (comma < 0) return null;
                return Convert.FromBase64String(src.Substring(comma + 1));
            }
            if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                string file = Path.Combine(CacheDir, Hash(src) + ".bin");
                if (File.Exists(file)) return File.ReadAllBytes(file);
                int status;
                byte[] data = Http.SendRaw("GET", src, null, null, null, 30000, out status);
                try { Directory.CreateDirectory(CacheDir); File.WriteAllBytes(file, data); } catch { }
                return data;
            }
            return null;
        }

        static BitmapSource Decode(byte[] bytes, int decodeWidth)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.StreamSource = new MemoryStream(bytes);
            if (decodeWidth > 0)
            {
                // Peek the size to avoid upscaling small images.
                var frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                if (frame.PixelWidth > decodeWidth) bi.DecodePixelWidth = decodeWidth;
            }
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        // ---------- Import (paste / pick) ----------

        public static List<BitmapSource> FromClipboard()
        {
            var list = new List<BitmapSource>();
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return list;
                // Prefer explicit PNG (keeps alpha, what browsers & snipping tools provide).
                if (data.GetDataPresent("PNG"))
                {
                    var ms = data.GetData("PNG") as MemoryStream;
                    if (ms != null)
                    {
                        var bmp = Load(ms.ToArray());
                        if (bmp != null) { list.Add(bmp); return list; }
                    }
                }
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null)
                    {
                        foreach (var f in files.Where(IsImageFile))
                        {
                            var b = LoadFile(f);
                            if (b != null) list.Add(b);
                        }
                        if (list.Count > 0) return list;
                    }
                }
                if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        // Clipboard DIBs can carry a bogus alpha channel; drop it.
                        var conv = new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0);
                        conv.Freeze();
                        list.Add(conv);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Clipboard read failed: " + ex.Message); }
            return list;
        }

        public static bool ClipboardHasImage()
        {
            try
            {
                if (Clipboard.ContainsImage() || Clipboard.ContainsData("PNG")) return true;
                if (Clipboard.ContainsFileDropList())
                    return Clipboard.GetFileDropList().Cast<string>().Any(IsImageFile);
            }
            catch { }
            return false;
        }

        public static bool IsImageFile(string f)
        {
            string ext = Path.GetExtension(f ?? "").ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp" || ext == ".tif" || ext == ".tiff";
        }

        public static BitmapSource LoadFile(string path)
        {
            try { return Load(File.ReadAllBytes(path)); } catch { return null; }
        }

        static BitmapSource Load(byte[] bytes)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(bytes);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        /// Resize to max 1360px, encode (JPEG q84, or PNG for small/transparent images),
        /// try the Supabase bucket, else fall back to a data URL. Runs on a worker thread.
        public static string Prepare(BitmapSource src)
        {
            const int maxDim = 1360;
            int w = src.PixelWidth, h = src.PixelHeight;
            double scale = Math.Min(1.0, (double)maxDim / Math.Max(w, h));
            BitmapSource img = src;
            if (scale < 1)
            {
                var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                tb.Freeze();
                img = tb;
            }
            bool hasAlpha = img.Format == PixelFormats.Bgra32 || img.Format == PixelFormats.Pbgra32 || img.Format == PixelFormats.Rgba64;
            byte[] png = Encode(new PngBitmapEncoder(), img);
            byte[] bytes;
            string mime;
            if (hasAlpha || png.Length <= 60 * 1024) { bytes = png; mime = "image/png"; }
            else
            {
                var enc = new JpegBitmapEncoder { QualityLevel = 84 };
                var rgb = new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0);
                bytes = Encode(enc, rgb);
                mime = "image/jpeg";
            }
            string url = Storage != null ? Storage.TryUploadImage(bytes, mime) : null;
            return url ?? "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
        }

        static byte[] Encode(BitmapEncoder enc, BitmapSource img)
        {
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }
    }
}
