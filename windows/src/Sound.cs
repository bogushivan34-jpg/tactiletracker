// Warm felt-mallet / marimba UI sounds, synthesized once into in-memory WAVs
// (same notes and envelopes as src/lib/tactileSound.ts) and played via winmm.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tactile
{
    public static class Sound
    {
        public static bool Enabled = true;

        struct Note
        {
            public double Freq, Delay, Duration, Volume, Cutoff;
            public Note(double f, double d, double dur, double v, double c) { Freq = f; Delay = d; Duration = dur; Volume = v; Cutoff = c; }
        }

        static readonly Dictionary<string, Note[]> defs = new Dictionary<string, Note[]>
        {
            { "completed", new[] { new Note(587.33, 0, 0.22, 0.25, 1500), new Note(880.0, 0.055, 0.28, 0.28, 1650) } },
            { "in_work", new[] { new Note(587.33, 0, 0.16, 0.21, 1350), new Note(739.99, 0.045, 0.18, 0.22, 1400), new Note(880.0, 0.09, 0.22, 0.24, 1450) } },
            { "backlog", new[] { new Note(440.0, 0, 0.15, 0.17, 950), new Note(329.63, 0.04, 0.18, 0.16, 820) } },
            { "task", new[] { new Note(329.63, 0, 0.14, 0.19, 1100), new Note(392.0, 0.035, 0.16, 0.20, 1150) } },
            { "expand", new[] { new Note(369.99, 0, 0.16, 0.20, 1100), new Note(440.0, 0.035, 0.20, 0.22, 1200) } },
            { "collapse", new[] { new Note(440.0, 0, 0.14, 0.18, 1050), new Note(369.99, 0.03, 0.16, 0.17, 950) } },
            { "add", new[] { new Note(523.25, 0, 0.18, 0.22, 1350), new Note(659.25, 0.045, 0.24, 0.24, 1450) } },
            { "drag_start", new[] { new Note(220.0, 0, 0.12, 0.18, 750) } },
            { "drop", new[] { new Note(293.66, 0, 0.15, 0.22, 950) } },
        };

        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        const int Rate = 44100;

        [DllImport("winmm.dll", SetLastError = true)]
        static extern bool PlaySound(byte[] data, IntPtr hmod, uint flags);
        const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

        /// Pre-render all sounds off the UI thread so the first click has no latency.
        public static void Warm()
        {
            ThreadPool.QueueUserWorkItem(_ => { foreach (var k in defs.Keys) Get(k); });
        }

        static byte[] Get(string name)
        {
            lock (cache)
            {
                byte[] b;
                if (cache.TryGetValue(name, out b)) return b;
                b = Render(defs[name]);
                cache[name] = b;
                return b;
            }
        }

        public static void Play(string name)
        {
            if (!Enabled || !defs.ContainsKey(name)) return;
            try
            {
                byte[] wav = Get(name);
                // The buffer must stay alive while playing; cached arrays are never freed.
                PlaySound(wav, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            catch { }
        }

        public static void PlayStatus(string status) { Play(status); }
        public static void PlayExpand(bool expanded) { Play(expanded ? "expand" : "collapse"); }

        static byte[] Render(Note[] notes)
        {
            double total = 0;
            foreach (var n in notes) total = Math.Max(total, n.Delay + n.Duration + 0.03);
            int count = (int)(total * Rate);
            var mix = new double[count];
            foreach (var n in notes) AddNote(mix, n);

            // Gentle master compression (soft knee) like the Web Audio DynamicsCompressor.
            var pcm = new short[count];
            for (int i = 0; i < count; i++)
            {
                double x = mix[i] * 0.9;
                double y = Math.Tanh(x * 1.4) / 1.4;
                pcm[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, y * 32767));
            }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = count * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataLen);
                w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataLen);
                foreach (var s in pcm) w.Write(s);
                return ms.ToArray();
            }
        }

        static void AddNote(double[] mix, Note n)
        {
            int start = (int)(n.Delay * Rate);
            int len = (int)((n.Duration + 0.02) * Rate);
            // Biquad low-pass (Q 0.7) to round off digital edges.
            double w0 = 2 * Math.PI * n.Cutoff / Rate, cosw = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * 0.7);
            double a0 = 1 + alpha;
            double b0 = (1 - cosw) / 2 / a0, b1 = (1 - cosw) / a0, b2 = b0, a1 = -2 * cosw / a0, a2 = (1 - alpha) / a0;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            double attack = 0.01, decayEnd = n.Duration;
            double k = Math.Log(0.0008 / n.Volume) / (decayEnd - attack);
            double kOver = Math.Log(0.001 / 0.14) / (n.Duration * 0.55);
            for (int i = 0; i < len && start + i < mix.Length; i++)
            {
                double t = (double)i / Rate;
                double env = t < attack ? 0.0001 + (n.Volume - 0.0001) * (t / attack) : n.Volume * Math.Exp(k * (t - attack));
                if (t > n.Duration) env = 0.0008 * Math.Max(0, 1 - (t - n.Duration) / 0.02);
                double over = 0.14 * Math.Exp(kOver * t);
                double x = Math.Sin(2 * Math.PI * n.Freq * t) + over * Math.Sin(2 * Math.PI * n.Freq * 2 * t);
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                mix[start + i] += y * env;
            }
        }
    }
}
