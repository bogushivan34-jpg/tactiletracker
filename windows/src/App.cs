// Entry point. Startup is kept minimal: read local JSON, build UI, render; sync starts after first frame.
using System;
using System.IO;
using System.Runtime;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("Tactile Issues")]
[assembly: System.Reflection.AssemblyProduct("Tactile Issues")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace Tactile
{
    public static class App
    {
        [STAThread]
        public static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            try { return Run(args); }
            catch (Exception ex) { Crash(ex); return 2; }
        }

        static void Crash(Exception ex)
        {
            try
            {
                string dir = Log.PathName != null ? Path.GetDirectoryName(Log.PathName)
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TactileTracker");
                var argv = Environment.GetCommandLineArgs();
                int di = Array.IndexOf(argv, "--data-dir");
                if (di >= 0 && di + 1 < argv.Length) dir = argv[di + 1];
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "crash.txt");
                File.AppendAllText(p, DateTime.Now + " " + ex + Environment.NewLine);
                Log.Write("CRASH " + ex);
            }
            catch { }
        }

        static int Run(string[] args)
        {
            var opts = new Options();
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            opts.DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TactileTracker");
            opts.ConfigPath = Path.Combine(exeDir, "config.json");
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> next = () => i + 1 < args.Length ? args[++i] : "";
                if (a == "--data-dir") opts.DataDir = Path.GetFullPath(next());
                else if (a == "--config") opts.ConfigPath = Path.GetFullPath(next());
                else if (a == "--offline") opts.Offline = true;
                else if (a == "--snapshot") opts.SnapshotPath = Path.GetFullPath(next());
                else if (a == "--snapshot-delay") int.TryParse(next(), out opts.SnapshotDelayMs);
                else if (a == "--show-completed") opts.ShowCompleted = true;
                else if (a == "--selftest") opts.SelfTest = true;
                else if (a == "--size")
                {
                    var parts = next().Split('x');
                    opts.Width = int.Parse(parts[0]);
                    opts.Height = int.Parse(parts[1]);
                }
            }
            Directory.CreateDirectory(opts.DataDir);
            Log.PathName = Path.Combine(opts.DataDir, "app.log");

            // Multicore JIT profile: later launches JIT the startup path in parallel.
            try
            {
                ProfileOptimization.SetProfileRoot(opts.DataDir);
                ProfileOptimization.StartProfile("startup.jitprofile");
            }
            catch { }

            Http.Init();
            Fonts.Init(exeDir);
            var cfg = Config.Load(opts.ConfigPath);
            if (opts.Offline) cfg.SyncEnabled = false;

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log.Write("Unhandled: " + e.Exception);
                e.Handled = true;
            };
            Trace("main entered");
            var w = new MainWindow(opts, cfg);
            int code = app.Run(w);
            return ExitCode != 0 ? ExitCode : code;
        }

        public static int ExitCode;
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        /// Milliseconds since the OS started this process (includes CLR startup).
        public static double SinceStartMs()
        {
            try { return (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
            catch { return clock.Elapsed.TotalMilliseconds; }
        }

        public static void Trace(string what)
        {
            if (Environment.GetEnvironmentVariable("TACTILE_TRACE") == "1" || Array.IndexOf(Environment.GetCommandLineArgs(), "--trace") >= 0)
                Log.Write("[trace +" + Math.Round(SinceStartMs()) + "ms] " + what);
        }

        /// --snapshot: render the window to PNG once images settle, then exit (used for visual checks).
        public static void ScheduleSnapshot(MainWindow w, string path, double startupMs)
        {
            int delay = w.Opts.SnapshotDelayMs > 0 ? w.Opts.SnapshotDelayMs : (w.State.HasEverSynced ? 1800 : 3500);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                SaveSnapshot(w, path);
                File.WriteAllText(path + ".txt", "startup_ms=" + Math.Round(startupMs) + Environment.NewLine);
                w.Close();
            };
            t.Start();
        }

        public static void SaveSnapshot(MainWindow w, string path)
        {
            var el = w.RootElement;
            var src = PresentationSource.FromVisual(w);
            double dpi = src != null ? 96 * src.CompositionTarget.TransformToDevice.M11 : 96;
            var rtb = new RenderTargetBitmap((int)(el.ActualWidth * dpi / 96), (int)(el.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            }
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
