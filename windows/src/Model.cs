// Data model + pure logic, ported 1:1 from src/lib/issuesLogic.ts, projectsLogic.ts, projectThemes.ts
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace Tactile
{
    public sealed class ThemeTokens
    {
        public string Key, Label;
        public Color Accent, AccentStrong, AccentDark, TintBg, TintBgSubtle, TintBorder,
            TintDrawerBorder, TintHover, TintMuted, Selection;
    }

    public static class Themes
    {
        public static readonly string[] Order = { "coral", "blue", "emerald", "violet", "amber", "rose", "teal" };
        static readonly Dictionary<string, ThemeTokens> map = new Dictionary<string, ThemeTokens>();

        static Color H(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromRgb(
                byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
        }

        static void Add(string key, string label, string accent, string strong, string dark, string tintBg,
            string subtle, string border, string drawer, string hover, byte mr, byte mg, byte mb, double ma, string sel)
        {
            map[key] = new ThemeTokens
            {
                Key = key, Label = label, Accent = H(accent), AccentStrong = H(strong), AccentDark = H(dark),
                TintBg = H(tintBg), TintBgSubtle = H(subtle), TintBorder = H(border), TintDrawerBorder = H(drawer),
                TintHover = H(hover), TintMuted = Color.FromArgb((byte)Math.Round(ma * 255), mr, mg, mb), Selection = H(sel)
            };
        }

        static Themes()
        {
            Add("coral", "Red-Orange", "#FF4F28", "#E03E1A", "#C23210", "#FFF5F0", "#FFF9F6", "#FFC4B0", "#FFD8CC", "#FFF0EC", 154, 52, 18, 0.72, "#FFE4DE");
            Add("blue", "Ocean Blue", "#2563EB", "#1D4ED8", "#1E40AF", "#EFF6FF", "#F5F9FF", "#BFDBFE", "#DBEAFE", "#E0EEFF", 30, 64, 175, 0.72, "#DBEAFE");
            Add("emerald", "Mint Emerald", "#059669", "#047857", "#065F46", "#ECFDF5", "#F4FCF8", "#A7F3D0", "#D1FAE5", "#DFF9EE", 6, 95, 70, 0.72, "#D1FAE5");
            Add("violet", "Iris Violet", "#7C3AED", "#6D28D9", "#5B21B6", "#F5F3FF", "#F9F8FF", "#DDD6FE", "#EDE9FE", "#EDE9FE", 91, 33, 182, 0.72, "#EDE9FE");
            Add("amber", "Warm Amber", "#D97706", "#B45309", "#92400E", "#FFFBEB", "#FFFCF2", "#FDE68A", "#FEF3C7", "#FEF3C7", 146, 64, 14, 0.75, "#FEF3C7");
            Add("rose", "Berry Rose", "#E11D48", "#BE123C", "#9F1239", "#FFF1F2", "#FFF6F7", "#FECDD3", "#FFE4E6", "#FFE4E6", 159, 18, 57, 0.72, "#FFE4E6");
            Add("teal", "Lagoon Teal", "#0D9488", "#0F766E", "#115E59", "#F0FDFA", "#F5FEFC", "#99F6E4", "#CCFBF1", "#CCFBF1", 17, 94, 89, 0.72, "#CCFBF1");
        }

        public static string Normalize(object raw)
        {
            var s = raw as string;
            return s != null && map.ContainsKey(s) ? s : "coral";
        }

        public static ThemeTokens Get(string key) { return map[Normalize(key)]; }
    }

    public sealed class Comment
    {
        public string Id, Text, CreatedAt;
        public List<string> Images; // null when there are none

        public bool HasImages { get { return Images != null && Images.Count > 0; } }
    }

    public sealed class Issue
    {
        public string Id, ProjectId, Section, Title, CreatedAt;
        public bool Completed, Active, Expanded;
        public List<Comment> Comments = new List<Comment>();
    }

    public sealed class Project
    {
        public string Id, Name, ColorKey, CreatedAt;
        public double SortOrder;
    }

    public static class Status
    {
        public const string InWork = "in_work", Task = "task", Backlog = "backlog", Completed = "completed";
    }

    public static class Logic
    {
        public const string DefaultProjectId = "proj-default";
        public const int PromptModeMinChars = 100;
        static readonly Random rng = new Random();

        public static string NowIso() { return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture); }

        static string Base36(long v)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (v == 0) return "0";
            var chars = new List<char>();
            while (v > 0) { chars.Insert(0, digits[(int)(v % 36)]); v /= 36; }
            return new string(chars.ToArray());
        }

        public static string NewId(string prefix, int randLen)
        {
            long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var r = new char[randLen];
            lock (rng) { for (int i = 0; i < randLen; i++) r[i] = digits[rng.Next(36)]; }
            return prefix + "_" + Base36(ms) + "_" + new string(r);
        }

        // ---------- Normalization (DB/local row <-> model) ----------

        static List<string> NormalizeImages(object raw)
        {
            var arr = Json.Arr(raw);
            if (arr == null) return null;
            var list = new List<string>();
            foreach (var o in arr)
            {
                var s = o as string;
                if (s != null && s.Trim().Length > 0) list.Add(s.Trim());
            }
            return list.Count > 0 ? list : null;
        }

        public static List<Comment> NormalizeComments(object raw)
        {
            var result = new List<Comment>();
            var arr = Json.Arr(raw);
            if (arr == null) return result;
            int index = 0;
            foreach (var entry in arr)
            {
                int idx = index++;
                var str = entry as string;
                if (str != null)
                {
                    var t = str.Trim();
                    if (t.Length == 0) continue;
                    result.Add(new Comment
                    {
                        Id = "comment-legacy-" + idx + "-" + (t.Length > 8 ? t.Substring(0, 8) : t),
                        Text = t,
                        CreatedAt = "1970-01-01T00:00:00.000Z"
                    });
                    continue;
                }
                var obj = Json.Obj(entry);
                if (obj == null) continue;
                if (Json.Str(obj, "id") == "__entry_theme_meta__" || Json.Str(obj, "text") == "__theme_metadata__" ||
                    obj.ContainsKey("theme") || obj.ContainsKey("themeCandidates")) continue;
                string text = Json.Str(obj, "text") ?? Json.Str(obj, "body") ?? Json.Str(obj, "content") ?? "";
                text = text.Trim();
                var images = NormalizeImages(Json.Get(obj, "images"));
                if (text.Length == 0 && images == null) continue;
                string id = Json.Str(obj, "id");
                id = id != null && id.Trim().Length > 0 ? id.Trim() : "comment-" + idx + "-" + DateTime.UtcNow.Ticks;
                string createdAt = Json.Str(obj, "createdAt") ?? Json.Str(obj, "created_at") ?? NowIso();
                result.Add(new Comment { Id = id, Text = text, CreatedAt = createdAt, Images = images });
            }
            return result;
        }

        public static string NormalizeSection(object raw)
        {
            var s = raw as string;
            return s == "drafts" || s == "draft" || s == "backlog" ? "drafts" : "prompted";
        }

        public static Issue NormalizeIssue(Dictionary<string, object> row)
        {
            if (row == null) return null;
            object rawId = Json.Get(row, "id");
            string id = rawId == null ? "" : Convert.ToString(rawId, CultureInfo.InvariantCulture).Trim();
            if (id.Length == 0) return null;
            string pid = Json.Str(row, "project_id") ?? Json.Str(row, "projectId");
            string created = Json.Str(row, "created_at");
            return new Issue
            {
                Id = id,
                ProjectId = pid != null && pid.Trim().Length > 0 ? pid.Trim() : DefaultProjectId,
                Section = NormalizeSection(Json.Get(row, "section")),
                Title = Json.Str(row, "title") ?? "",
                Completed = Json.Bool(row, "completed"),
                Active = Json.Bool(row, "active"),
                Expanded = Json.Bool(row, "expanded"),
                Comments = NormalizeComments(Json.Get(row, "comments")),
                CreatedAt = string.IsNullOrEmpty(created) ? NowIso() : created
            };
        }

        public static Dictionary<string, object> CommentToDict(Comment c)
        {
            var d = new Dictionary<string, object>();
            d["id"] = c.Id;
            d["text"] = c.Text;
            d["createdAt"] = c.CreatedAt;
            if (c.HasImages) d["images"] = c.Images.Cast<object>().ToList();
            return d;
        }

        public static Dictionary<string, object> IssueToDict(Issue i)
        {
            var d = new Dictionary<string, object>();
            d["id"] = i.Id;
            d["section"] = i.Section;
            d["title"] = i.Title;
            d["completed"] = i.Completed;
            d["active"] = i.Active;
            d["expanded"] = i.Expanded;
            d["comments"] = i.Comments.Select(c => (object)CommentToDict(c)).ToList();
            d["created_at"] = i.CreatedAt;
            d["project_id"] = i.ProjectId;
            return d;
        }

        public static Project NormalizeProject(Dictionary<string, object> row, int fallbackIndex)
        {
            if (row == null) return null;
            object rawId = Json.Get(row, "id");
            string id = rawId == null ? "" : Convert.ToString(rawId, CultureInfo.InvariantCulture).Trim();
            if (id.Length == 0) return null;
            string name = (Json.Str(row, "name") ?? "").Trim();
            double? order = Json.Num(row, "sort_order") ?? Json.Num(row, "sortOrder");
            string created = Json.Str(row, "created_at") ?? Json.Str(row, "createdAt");
            return new Project
            {
                Id = id,
                Name = name.Length > 0 ? name : "Untitled Project",
                ColorKey = Themes.Normalize(Json.Get(row, "color_key") ?? Json.Get(row, "colorKey")),
                SortOrder = order.HasValue ? order.Value : fallbackIndex,
                CreatedAt = string.IsNullOrEmpty(created) ? NowIso() : created
            };
        }

        public static Dictionary<string, object> ProjectToDict(Project p)
        {
            var d = new Dictionary<string, object>();
            d["id"] = p.Id;
            d["name"] = p.Name;
            d["color_key"] = p.ColorKey;
            d["sort_order"] = p.SortOrder;
            d["created_at"] = p.CreatedAt;
            return d;
        }

        public static List<Project> InitialProjects()
        {
            return new List<Project>
            {
                new Project { Id = DefaultProjectId, Name = "Main", ColorKey = "coral", SortOrder = 0, CreatedAt = "2026-11-01T00:00:00.000Z" },
                new Project { Id = "proj-blue", Name = "Studio", ColorKey = "blue", SortOrder = 1, CreatedAt = "2026-11-01T00:01:00.000Z" },
                new Project { Id = "proj-emerald", Name = "Lab", ColorKey = "emerald", SortOrder = 2, CreatedAt = "2026-11-01T00:02:00.000Z" },
            };
        }

        public static List<Project> SortProjects(IEnumerable<Project> projects)
        {
            return projects.OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt, StringComparer.Ordinal).ToList();
        }

        // ---------- Issue status ----------

        public static bool IsPromptMode(string text) { return (text ?? "").Trim().Length >= PromptModeMinChars; }

        public static string FallbackTitle(string prompt)
        {
            string cleaned = System.Text.RegularExpressions.Regex.Replace(prompt ?? "", "\\s+", " ").Trim();
            if (cleaned.Length == 0) return "";
            if (cleaned.Length <= 52) return cleaned.TrimEnd('.', '!', '?');
            var words = cleaned.Split(' ');
            string slice = string.Join(" ", words.Take(7)).TrimEnd(',', '.', ';', ':', '!', '?');
            return slice + "…";
        }

        public static int AttachmentCount(Issue i)
        {
            return i.Comments.Sum(c => c.HasImages ? c.Images.Count : 0);
        }

        public static string GetStatus(Issue i)
        {
            if (i.Completed) return Status.Completed;
            if (i.Active) return Status.InWork;
            if (i.Section == "drafts") return Status.Backlog;
            return Status.Task;
        }

        public static string NextStatus(string s)
        {
            if (s == Status.Task) return Status.InWork;
            if (s == Status.InWork) return Status.Backlog;
            if (s == Status.Backlog) return Status.Completed;
            return Status.Task;
        }

        public static void ApplyStatus(Issue i, string status)
        {
            if (status == Status.InWork) { i.Completed = false; i.Active = true; i.Section = "prompted"; }
            else if (status == Status.Backlog) { i.Completed = false; i.Active = false; i.Section = "drafts"; }
            else if (status == Status.Completed) { i.Completed = true; i.Active = false; }
            else { i.Completed = false; i.Active = false; i.Section = "prompted"; }
        }

        public static int Rank(Issue i)
        {
            string s = GetStatus(i);
            if (s == Status.InWork) return 0;
            if (s == Status.Task) return 1;
            if (s == Status.Backlog) return 2;
            return 3;
        }

        public static void SetStatus(List<Issue> issues, string id, string status)
        {
            int idx = issues.FindIndex(x => x.Id == id);
            if (idx < 0) return;
            var target = issues[idx];
            ApplyStatus(target, status);
            if (status == Status.InWork)
            {
                issues.RemoveAt(idx);
                issues.Insert(0, target);
            }
        }

        public static void Reorder(List<Issue> issues, string draggedId, string targetId)
        {
            if (string.IsNullOrEmpty(draggedId) || string.IsNullOrEmpty(targetId) || draggedId == targetId) return;
            int di = issues.FindIndex(x => x.Id == draggedId);
            int ti = issues.FindIndex(x => x.Id == targetId);
            if (di < 0 || ti < 0) return;
            var dragged = issues[di];
            ApplyStatus(dragged, GetStatus(issues[ti]));
            issues.RemoveAt(di);
            int nti = issues.FindIndex(x => x.Id == targetId);
            int insertAt = di < ti ? nti + 1 : nti;
            issues.Insert(insertAt, dragged);
        }

        public static Issue CreateIssue(string title, string projectId, string status, List<string> images)
        {
            var clean = images != null && images.Count > 0 ? images.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : null;
            if (clean != null && clean.Count == 0) clean = null;
            string raw = (title ?? "").Trim();
            if (raw.Length == 0 && clean == null) return null;
            string now = NowIso();
            var issue = new Issue
            {
                Id = NewId("iss", 5),
                ProjectId = string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim(),
                Section = status == Status.Backlog ? "drafts" : "prompted",
                Completed = status == Status.Completed,
                Active = status == Status.InWork,
                Expanded = false,
                CreatedAt = now
            };
            if (IsPromptMode(raw))
            {
                issue.Title = FallbackTitle(raw);
                issue.Comments.Add(new Comment { Id = NewId("cmt", 5), Text = raw, CreatedAt = now, Images = clean });
                return issue;
            }
            issue.Title = raw.Length > 0 ? raw : "Pasted image attachment";
            if (clean != null) issue.Comments.Add(new Comment { Id = NewId("cmt", 5), Text = "", CreatedAt = now, Images = clean });
            return issue;
        }

        public static bool AddComment(Issue issue, string text, List<string> images)
        {
            string t = (text ?? "").Trim();
            var clean = images != null && images.Count > 0 ? new List<string>(images) : null;
            if (t.Length == 0 && clean == null) return false;
            issue.Expanded = true;
            issue.Comments.Add(new Comment { Id = NewId("cmt", 5), Text = t, CreatedAt = NowIso(), Images = clean });
            return true;
        }

        public static void UpdateComment(Issue issue, string commentId, string newText)
        {
            string t = (newText ?? "").Trim();
            int idx = issue.Comments.FindIndex(c => c.Id == commentId);
            if (idx < 0) return;
            var c0 = issue.Comments[idx];
            if (t.Length == 0 && !c0.HasImages) issue.Comments.RemoveAt(idx);
            else c0.Text = t;
        }

        public static void DeleteCommentImage(Issue issue, string commentId, int imageIndex)
        {
            int idx = issue.Comments.FindIndex(c => c.Id == commentId);
            if (idx < 0) return;
            var c0 = issue.Comments[idx];
            if (c0.Images != null && imageIndex >= 0 && imageIndex < c0.Images.Count) c0.Images.RemoveAt(imageIndex);
            if (c0.Images != null && c0.Images.Count == 0) c0.Images = null;
            if (c0.Text.Trim().Length == 0 && !c0.HasImages) issue.Comments.RemoveAt(idx);
        }

        /// Sort by status rank, then saved order; items missing from the saved order
        /// (created elsewhere) go first, newest first, mirroring how new items are prepended.
        public static List<Issue> SortInitial(IEnumerable<Issue> issues, IList<string> savedOrder)
        {
            var pos = new Dictionary<string, int>();
            if (savedOrder != null) for (int k = 0; k < savedOrder.Count; k++) pos[savedOrder[k]] = k;
            return issues
                .OrderBy(i => Rank(i))
                .ThenBy(i => { int p; return pos.TryGetValue(i.Id, out p) ? p : -1; })
                .ThenByDescending(i => i.CreatedAt, StringComparer.Ordinal)
                .ToList();
        }

        public static List<Issue> VisibleMain(IEnumerable<Issue> projectIssues)
        {
            return projectIssues.Where(i => !i.Completed).OrderBy(i => Rank(i)).ToList();
        }

        // ---------- Projects ----------

        public static string NextProjectColor(List<Project> projects)
        {
            var used = new HashSet<string>(projects.Select(p => p.ColorKey));
            foreach (var c in Themes.Order) if (!used.Contains(c)) return c;
            return Themes.Order[projects.Count % Themes.Order.Length];
        }

        public static string NextCycledColor(string current)
        {
            int idx = Array.IndexOf(Themes.Order, current);
            return Themes.Order[idx < 0 ? 0 : (idx + 1) % Themes.Order.Length];
        }

        public static Project CreateProject(string name, List<Project> existing)
        {
            string t = (name ?? "").Trim();
            if (t.Length == 0) return null;
            double maxOrder = existing.Count == 0 ? -1 : existing.Max(p => p.SortOrder);
            return new Project
            {
                Id = NewId("proj", 4),
                Name = t,
                ColorKey = NextProjectColor(existing),
                SortOrder = maxOrder + 1,
                CreatedAt = NowIso()
            };
        }
    }
}
