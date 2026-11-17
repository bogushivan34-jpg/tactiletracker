// Local-first storage + background sync with a persistent outbox.
//  * Startup reads one local JSON file and renders immediately (no network on the critical path).
//  * Every edit is saved locally (debounced) and queued in the outbox.
//  * A single background worker flushes the outbox to Supabase and pulls remote changes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace Tactile
{
    public sealed class OutboxItem
    {
        public string Key; // "issue:<id>", "issue-del:<id>", "project:<id>", "project-del:<id>"
        public long Seq;
    }

    public sealed class AppState
    {
        public List<Project> Projects = Logic.InitialProjects();
        public List<Issue> Issues = new List<Issue>();
        public string ActiveProjectId = Logic.DefaultProjectId;
        public List<OutboxItem> Outbox = new List<OutboxItem>();
        public long ChangeCounter;
        public bool HasEverSynced;
        public DateTime LastSyncUtc = DateTime.MinValue;
        long seq;

        readonly string path;
        readonly Dispatcher ui;
        DispatcherTimer saveTimer;

        public AppState(string dataDir, Dispatcher ui)
        {
            Directory.CreateDirectory(dataDir);
            path = Path.Combine(dataDir, "data.json");
            this.ui = ui;
        }

        public Project ActiveProject
        {
            get
            {
                return Projects.FirstOrDefault(p => p.Id == ActiveProjectId) ?? Projects.FirstOrDefault() ?? Logic.InitialProjects()[0];
            }
        }

        public Issue Find(string id) { return Issues.FirstOrDefault(i => i.Id == id); }

        // ---------- Persistence ----------

        public void Load()
        {
            if (!File.Exists(path)) return;
            try
            {
                var root = Json.Obj(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (root == null) return;
                var projects = (Json.Arr(Json.Get(root, "projects")) ?? new List<object>())
                    .Select((o, idx) => Logic.NormalizeProject(Json.Obj(o), idx)).Where(p => p != null).ToList();
                if (projects.Count > 0) Projects = Logic.SortProjects(projects);
                Issues = (Json.Arr(Json.Get(root, "issues")) ?? new List<object>())
                    .Select(o => Logic.NormalizeIssue(Json.Obj(o))).Where(i => i != null).ToList();
                ActiveProjectId = Json.Str(root, "activeProjectId") ?? ActiveProjectId;
                HasEverSynced = Json.Bool(root, "hasEverSynced");
                DateTime last;
                var lastStr = Json.Str(root, "lastSyncUtc");
                if (lastStr != null && DateTime.TryParse(lastStr, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out last)) LastSyncUtc = last.ToUniversalTime();
                Outbox = (Json.Arr(Json.Get(root, "outbox")) ?? new List<object>())
                    .Select(Json.Obj).Where(o => o != null && Json.Str(o, "key") != null)
                    .Select(o => new OutboxItem { Key = Json.Str(o, "key"), Seq = (long)(Json.Num(o, "seq") ?? 0) }).ToList();
                seq = Outbox.Count == 0 ? 0 : Outbox.Max(o => o.Seq);
            }
            catch (Exception ex)
            {
                Log.Write("Failed to read local data: " + ex.Message);
                try { File.Copy(path, path + ".corrupt-" + DateTime.Now.Ticks, true); } catch { }
            }
            if (!Projects.Any(p => p.Id == ActiveProjectId)) ActiveProjectId = Projects[0].Id;
        }

        string Serialize()
        {
            var root = new Dictionary<string, object>();
            root["version"] = 1;
            root["activeProjectId"] = ActiveProjectId;
            root["hasEverSynced"] = HasEverSynced;
            if (LastSyncUtc != DateTime.MinValue) root["lastSyncUtc"] = LastSyncUtc.ToString("o");
            root["projects"] = Projects.Select(p => (object)Logic.ProjectToDict(p)).ToList();
            root["issues"] = Issues.Select(i => (object)Logic.IssueToDict(i)).ToList();
            root["outbox"] = Outbox.Select(o =>
            {
                var d = new Dictionary<string, object>();
                d["key"] = o.Key;
                d["seq"] = (double)o.Seq;
                return (object)d;
            }).ToList();
            return Json.Write(root);
        }

        public void SaveSoon()
        {
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
                saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(false); };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }

        readonly object writeLock = new object();

        public void SaveNow(bool synchronous)
        {
            if (saveTimer != null) saveTimer.Stop();
            string text = Serialize();
            Action write = () =>
            {
                lock (writeLock)
                {
                    try
                    {
                        string tmp = path + ".tmp";
                        File.WriteAllText(tmp, text, new UTF8Encoding(false));
                        if (File.Exists(path)) File.Replace(tmp, path, null);
                        else File.Move(tmp, path);
                    }
                    catch (Exception ex) { Log.Write("Save failed: " + ex.Message); }
                }
            };
            if (synchronous) write();
            else ThreadPool.QueueUserWorkItem(_ => write());
        }

        // ---------- Outbox ----------

        public void Enqueue(string kind, string id)
        {
            // An upsert after a pending delete (or vice versa) replaces it: only the latest intent matters.
            string baseKind = kind.Replace("-del", "");
            Outbox.RemoveAll(o => o.Key == baseKind + ":" + id || o.Key == baseKind + "-del:" + id);
            Outbox.Add(new OutboxItem { Key = kind + ":" + id, Seq = ++seq });
        }

        /// Call after every user edit.
        public void Touch()
        {
            ChangeCounter++;
            SaveSoon();
        }
    }

    public enum SyncState { Idle, Syncing, Offline, Disabled }

    public enum ProblemKind { NoInternet, Unreachable, Timeout, Secure, Server, Auth, Rejected, Other }

    /// Why the last sync attempt failed, in words a user can act on.
    public sealed class SyncProblem
    {
        public ProblemKind Kind;
        public string Title, Detail;
        public DateTime SinceUtc;

        public bool Serious { get { return Kind == ProblemKind.Auth || Kind == ProblemKind.Rejected; } }

        const string Vpn = " Try turning on your VPN.";

        static SyncProblem Make(ProblemKind k, string title, string detail)
        {
            return new SyncProblem { Kind = k, Title = title, Detail = detail, SinceUtc = DateTime.UtcNow };
        }

        public static SyncProblem From(Exception e, bool networkUp)
        {
            int code = e is TransientNetException ? ((TransientNetException)e).Code
                     : e is RejectedNetException ? ((RejectedNetException)e).Code : 0;
            System.Net.WebException web = null;
            for (var x = e; x != null && web == null; x = x.InnerException) web = x as System.Net.WebException;

            if (!networkUp)
                return Make(ProblemKind.NoInternet, "No internet connection",
                    "This PC is offline. Changes are saved here and will sync when you're back online.");
            if (code == 401 || code == 403)
                return Make(ProblemKind.Auth, "Access denied",
                    "The server rejected the app's key (HTTP " + code + "). Check supabaseKey in config.json.");
            if (code > 0 && e is TransientNetException)
                return Make(ProblemKind.Server, "Server error", "The sync server returned HTTP " + code + ". It will retry automatically.");
            if (e is RejectedNetException)
                return Make(ProblemKind.Rejected, "Server rejected the request", "HTTP " + code + ". Your data is still saved on this PC.");
            if (web != null)
            {
                switch (web.Status)
                {
                    case System.Net.WebExceptionStatus.NameResolutionFailure:
                    case System.Net.WebExceptionStatus.ProxyNameResolutionFailure:
                        return Make(ProblemKind.Unreachable, "Can't reach the server",
                            "The sync server's address couldn't be found. It may be blocked on this network." + Vpn);
                    case System.Net.WebExceptionStatus.Timeout:
                        return Make(ProblemKind.Timeout, "Server isn't responding",
                            "The connection timed out. The sync server may be blocked on this network." + Vpn);
                    case System.Net.WebExceptionStatus.ConnectFailure:
                        return Make(ProblemKind.Unreachable, "Can't connect to the server",
                            "The connection was refused. Check your connection, or try turning on your VPN.");
                    case System.Net.WebExceptionStatus.SecureChannelFailure:
                    case System.Net.WebExceptionStatus.TrustFailure:
                        return Make(ProblemKind.Secure, "Secure connection failed",
                            "The secure connection to the sync server failed. This network may be filtering traffic." + Vpn);
                    default:
                        return Make(ProblemKind.Unreachable, "Connection dropped",
                            "The connection to the sync server was cut off (" + web.Status + "). This network may be blocking it." + Vpn);
                }
            }
            string msg = e == null ? "Unknown error." : e.Message;
            if (msg.Length > 160) msg = msg.Substring(0, 160) + "…";
            return Make(ProblemKind.Other, "Sync failed", msg);
        }
    }

    public sealed class SyncEngine
    {
        readonly AppState state;
        readonly Dispatcher ui;
        readonly Supabase sb;
        readonly Config cfg;
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        volatile bool pullRequested;
        int failures;

        public event Action<SyncState, int> StatusChanged;
        /// Raised on the UI thread when a failure starts, changes kind, or clears (null).
        public event Action<SyncProblem> ProblemChanged;
        /// Raised on the UI thread for one-off notices ("Back online", "A change was rejected").
        public event Action<string> Notice;
        /// Current problem; read on the UI thread only.
        public SyncProblem Problem;
        volatile bool problemActive;
        Exception lastFailure;
        /// Raised on the UI thread after remote data replaced local state.
        public event Action Pulled;

        public SyncEngine(AppState state, Dispatcher ui, Config cfg)
        {
            this.state = state;
            this.ui = ui;
            this.cfg = cfg;
            sb = new Supabase(cfg);
        }

        public DateTime LastPullUtc = DateTime.MinValue;

        public void Start()
        {
            if (!cfg.SyncEnabled) { Report(SyncState.Disabled); return; }
            // Retry right away when a network or VPN comes up instead of waiting out the backoff.
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s, e) => { if (problemActive) RequestPull(); };
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) => { if (problemActive) RequestPull(); };
            var t = new Thread(Loop) { IsBackground = true, Name = "sync", Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        public void Kick() { signal.Set(); }

        public void RequestPull()
        {
            pullRequested = true;
            signal.Set();
        }

        void Report(SyncState s)
        {
            int pending = 0;
            ui.BeginInvoke(new Action(() =>
            {
                pending = state.Outbox.Count;
                var h = StatusChanged;
                if (h != null) h(s, pending);
            }));
        }

        void Loop()
        {
            pullRequested = true;
            while (true)
            {
                try
                {
                    lastFailure = null;
                    bool ok = Flush();
                    if (ok && pullRequested)
                    {
                        pullRequested = false;
                        ok = Pull();
                    }
                    if (ok)
                    {
                        failures = 0;
                        ReportSuccess();
                        Report(SyncState.Idle);
                        signal.WaitOne();
                    }
                    else
                    {
                        failures++;
                        ReportProblem(lastFailure);
                        Report(SyncState.Offline);
                        int[] backoff = { 2000, 4000, 8000, 15000, 30000, 60000 };
                        signal.WaitOne(backoff[Math.Min(failures - 1, backoff.Length - 1)]);
                        pullRequested = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Sync loop error: " + ex);
                    Thread.Sleep(5000);
                }
            }
        }

        /// Returns false when the network is unavailable (retry later).
        bool Flush()
        {
            while (true)
            {
                OutboxItem head = null;
                Dictionary<string, object> payload = null;
                ui.Invoke(new Action(() =>
                {
                    head = state.Outbox.FirstOrDefault();
                    if (head == null) return;
                    int colon = head.Key.IndexOf(':');
                    string kind = head.Key.Substring(0, colon), id = head.Key.Substring(colon + 1);
                    if (kind == "issue")
                    {
                        var i = state.Find(id);
                        if (i != null) payload = Logic.IssueToDict(i);
                    }
                    else if (kind == "project")
                    {
                        var p = state.Projects.FirstOrDefault(x => x.Id == id);
                        if (p != null) payload = Logic.ProjectToDict(p);
                    }
                }));
                if (head == null) return true;
                Report(SyncState.Syncing);

                try
                {
                    int colon = head.Key.IndexOf(':');
                    string kind = head.Key.Substring(0, colon), id = head.Key.Substring(colon + 1);
                    if (kind == "issue" && payload != null) sb.Upsert("issues", payload);
                    else if (kind == "project" && payload != null) sb.Upsert("projects", payload);
                    else if (kind == "issue-del") sb.Delete("issues", id);
                    else if (kind == "project-del") sb.Delete("projects", id);
                }
                catch (TransientNetException ex)
                {
                    Log.Write("Sync deferred (" + head.Key + "): " + ex.Message);
                    lastFailure = ex;
                    return false;
                }
                catch (Exception ex)
                {
                    // Permanent rejection (e.g. 4xx). Drop it so the queue never wedges; local copy stays intact.
                    Log.Write("Sync rejected (" + head.Key + "): " + ex.Message);
                    var rej = ex as RejectedNetException;
                    var note = "The server rejected a change" + (rej != null ? " (HTTP " + rej.Code + ")" : "") + ". It's kept on this PC only.";
                    ui.BeginInvoke(new Action(() => { var h = Notice; if (h != null) h(note); }));
                }

                var done = head;
                ui.Invoke(new Action(() =>
                {
                    // Only remove if it was not re-queued (edited again) while in flight.
                    state.Outbox.RemoveAll(o => o.Key == done.Key && o.Seq == done.Seq);
                    state.SaveSoon();
                }));
            }
        }

        void ReportSuccess()
        {
            problemActive = false;
            ui.BeginInvoke(new Action(() =>
            {
                state.LastSyncUtc = DateTime.UtcNow;
                state.SaveSoon();
                if (Problem == null) return;
                Problem = null;
                var h = ProblemChanged;
                if (h != null) h(null);
                var n = Notice;
                if (n != null) n("Back online · synced");
            }));
        }

        void ReportProblem(Exception ex)
        {
            bool net = true;
            try { net = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); } catch { }
            var p = SyncProblem.From(ex, net);
            problemActive = true;
            ui.BeginInvoke(new Action(() =>
            {
                // Keep the original "since" time while the same kind of failure repeats.
                if (Problem != null && Problem.Kind == p.Kind) p.SinceUtc = Problem.SinceUtc;
                bool changed = Problem == null || Problem.Kind != p.Kind || Problem.Detail != p.Detail;
                Problem = p;
                var h = ProblemChanged;
                if (h != null && changed) h(p);
            }));
        }

        /// Returns false when the fetch failed (the reason is in lastFailure).
        bool Pull()
        {
            long version = 0;
            ui.Invoke(new Action(() => version = state.ChangeCounter));
            Report(SyncState.Syncing);

            List<Dictionary<string, object>> projectRows = null, issueRows;
            try
            {
                try { projectRows = sb.Select("projects", "select=*&order=sort_order.asc"); }
                catch (InvalidOperationException ex) { Log.Write("Projects table unavailable: " + ex.Message); }
                issueRows = sb.Select("issues", "select=*&order=created_at.desc");
            }
            catch (Exception ex)
            {
                Log.Write("Pull failed: " + ex.Message);
                pullRequested = true;
                lastFailure = ex;
                return false;
            }

            var remoteProjects = projectRows == null ? null :
                projectRows.Select((r, idx) => Logic.NormalizeProject(r, idx)).Where(p => p != null).ToList();
            var remoteIssues = issueRows.Select(Logic.NormalizeIssue).Where(i => i != null).ToList();

            ui.Invoke(new Action(() =>
            {
                if (state.ChangeCounter != version || state.Outbox.Count > 0)
                {
                    // The user edited something while we were fetching; never clobber it. Try again shortly.
                    pullRequested = true;
                    signal.Set();
                    return;
                }
                LastPullUtc = DateTime.UtcNow;
                var order = state.Issues.Select(i => i.Id).ToList();
                // Preserve local expand/collapse state for rows we already have (pure view state).
                var localExpanded = state.Issues.ToDictionary(i => i.Id, i => i.Expanded);
                foreach (var ri in remoteIssues)
                {
                    bool exp;
                    if (localExpanded.TryGetValue(ri.Id, out exp)) ri.Expanded = exp;
                }
                state.Issues = Logic.SortInitial(remoteIssues, order);
                if (remoteProjects != null && remoteProjects.Count > 0) state.Projects = Logic.SortProjects(remoteProjects);
                if (!state.Projects.Any(p => p.Id == state.ActiveProjectId)) state.ActiveProjectId = state.Projects[0].Id;
                state.HasEverSynced = true;
                state.SaveSoon();
                var h = Pulled;
                if (h != null) h();
            }));
            return true;
        }
    }

    public static class Log
    {
        public static string PathName;
        static readonly object gate = new object();

        public static void Write(string msg)
        {
            if (PathName == null) return;
            lock (gate)
            {
                try
                {
                    var fi = new FileInfo(PathName);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
