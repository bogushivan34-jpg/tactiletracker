// --selftest: drives the real mutation handlers end-to-end against whatever backend config.json
// points at (use the local mock server, never production), then verifies the server state.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace Tactile
{
    public static class SelfTest
    {
        static readonly StringBuilder report = new StringBuilder();
        static int failures;

        static void Check(bool ok, string what)
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + what);
            if (!ok) failures++;
        }

        static async Task WaitFor(Func<bool> cond, int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (!cond() && DateTime.UtcNow < until) await Task.Delay(50);
        }

        public static async void Run(MainWindow w)
        {
            string outPath = Path.Combine(Path.GetDirectoryName(Log.PathName), "selftest.txt");
            try
            {
                var st = w.State;
                var syncField = typeof(MainWindow).GetField("sync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var sync = (SyncEngine)syncField.GetValue(w);
                var cfgField = typeof(MainWindow).GetField("cfg", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var cfg = (Config)cfgField.GetValue(w);

                await WaitFor(() => sync.LastPullUtc != DateTime.MinValue, 10000);
                Check(sync.LastPullUtc != DateTime.MinValue, "initial pull completed");
                int remoteBefore = st.Issues.Count;
                report.AppendLine("issues after initial pull: " + remoteBefore + ", projects: " + st.Projects.Count);

                // Projects
                w.CreateProject("Selftest");
                var proj = st.Projects.Last();
                Check(st.ActiveProjectId == proj.Id, "new project becomes active");
                w.RenameProject(proj.Id, "Selftest Renamed");
                w.CycleProjectColor(proj.Id);

                // Issues
                w.AddIssue("first task", Status.Task, null);
                var first = st.Issues[0];
                string longPrompt = "So I want to build a really long prompt that goes past one hundred characters so that prompt mode kicks in and the fallback title is used.";
                w.AddIssue(longPrompt, Status.Task, null);
                var prompt = st.Issues[0];
                Check(prompt.Comments.Count == 1 && prompt.Comments[0].Text == longPrompt, "prompt mode stores prompt as first comment");
                Check(prompt.Title.EndsWith("…") || prompt.Title.Length > 0, "prompt mode has a title: " + prompt.Title);
                w.AddIssue("an idea", Status.Backlog, null);
                var idea = st.Issues[0];
                Check(Logic.GetStatus(idea) == Status.Backlog, "idea added as backlog");

                w.CycleStatus(first.Id);
                Check(Logic.GetStatus(first) == Status.InWork && st.Issues[0] == first, "task -> running pins to top");
                w.SetStatus(first.Id, Status.Completed);
                Check(first.Completed, "Done completes");
                w.CycleStatus(first.Id);
                Check(Logic.GetStatus(first) == Status.Task, "restore -> task");

                w.AddComment(first.Id, "hello comment", null);
                Check(first.Comments.Count == 1 && first.Expanded, "comment added and drawer expanded");
                w.UpdateComment(first.Id, first.Comments[0].Id, "edited comment");
                Check(first.Comments[0].Text == "edited comment", "comment edited");

                // Image attachment through the real prepare pipeline (upload attempt -> data URL fallback)
                var bmp = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)200, 64 * 48 * 4).ToArray(), 64 * 4);
                bmp.Freeze();
                string url = null;
                w.PrepareImage(bmp, u => url = u);
                await WaitFor(() => url != null, 10000);
                // A background pull may have replaced the objects while we awaited; re-resolve by id.
                first = st.Find(first.Id); prompt = st.Find(prompt.Id); idea = st.Find(idea.Id);
                Check(url != null && (url.StartsWith("data:image/") || url.StartsWith("http")), "image prepared: " + (url == null ? "null" : url.Substring(0, Math.Min(40, url.Length))));
                w.AddComment(first.Id, "", new List<string> { url });
                Check(Logic.AttachmentCount(first) == 1, "image-only comment added");
                w.DeleteCommentImage(first.Id, first.Comments[1].Id, 0);
                Check(first.Comments.Count == 1, "removing the only image removes the empty comment");

                w.UpdateTitle(prompt.Id, "Renamed prompt");
                Check(prompt.Title == "Renamed prompt", "title updated");
                Logic.Reorder(st.Issues, idea.Id, first.Id);
                Check(Logic.GetStatus(idea) == Status.Task, "drag onto a task adopts its status");
                w.DeleteIssue(idea.Id);
                Check(st.Find(idea.Id) == null, "issue deleted");
                w.ToggleExpand(first.Id);
                w.ShowCompletedForTest();
                w.Render(false);

                await WaitFor(() => w.OutboxCount == 0, 15000);
                Check(w.OutboxCount == 0, "outbox flushed");

                // Verify server state matches
                var sb = new Supabase(cfg);
                List<Dictionary<string, object>> remote = null, remoteProjects = null;
                await Task.Run(() => { remote = sb.Select("issues", "select=*"); remoteProjects = sb.Select("projects", "select=*"); });
                var mine = remote.Where(r => Json.Str(r, "project_id") == proj.Id).ToList();
                Check(mine.Count == 2, "server has 2 issues in test project (got " + mine.Count + ")");
                var rFirst = mine.FirstOrDefault(r => Json.Str(r, "id") == first.Id);
                Check(rFirst != null && Json.Str(rFirst, "title") == "first task" && !Json.Bool(rFirst, "completed"), "server copy of first task matches");
                var rComments = rFirst == null ? null : Json.Arr(Json.Get(rFirst, "comments"));
                Check(rComments != null && rComments.Count == 1 && Json.Str(Json.Obj(rComments[0]), "text") == "edited comment", "server comment matches");
                var rProj = remoteProjects.FirstOrDefault(r => Json.Str(r, "id") == proj.Id);
                Check(rProj != null && Json.Str(rProj, "name") == "Selftest Renamed" && Json.Str(rProj, "color_key") != "coral", "server project matches");
                Check(!remote.Any(r => Json.Str(r, "id") == idea.Id), "deleted issue gone from server");

                // Pull round-trip keeps local state intact
                sync.LastPullUtc = DateTime.MinValue;
                sync.RequestPull();
                await WaitFor(() => sync.LastPullUtc != DateTime.MinValue, 10000);
                Check(st.Find(first.Id) != null && st.Find(prompt.Id).Title == "Renamed prompt", "pull round-trip preserves data");

                App.SaveSnapshot(w, Path.Combine(Path.GetDirectoryName(Log.PathName), "selftest.png"));

                // Delete project cascades
                w.DeleteProject(proj.Id);
                await WaitFor(() => w.OutboxCount == 0, 15000);
                await Task.Run(() => { remote = sb.Select("issues", "select=*"); remoteProjects = sb.Select("projects", "select=*"); });
                Check(!remote.Any(r => Json.Str(r, "project_id") == proj.Id) && !remoteProjects.Any(r => Json.Str(r, "id") == proj.Id), "project delete cascades to server");
                Check(remote.Count == remoteBefore, "server back to original issue count (" + remote.Count + " vs " + remoteBefore + ")");

                // Local persistence round-trip
                st.SaveNow(true);
                var reloaded = new AppState(Path.GetDirectoryName(Log.PathName), w.Dispatcher);
                reloaded.Load();
                Check(reloaded.Issues.Count == st.Issues.Count && reloaded.Projects.Count == st.Projects.Count, "local file round-trip");
            }
            catch (Exception ex)
            {
                Check(false, "exception: " + ex);
            }
            report.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            File.WriteAllText(outPath, report.ToString());
            App.ExitCode = failures == 0 ? 0 : 1;
            w.Close();
        }
    }
}
