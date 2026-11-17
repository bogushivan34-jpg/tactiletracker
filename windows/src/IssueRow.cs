// One issue row: grip, morphing status pill, inline-editable title, Done, comments pill,
// and the expandable comments/attachments drawer (port of components/IssueRow.tsx).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Tactile
{
    public sealed class IssueRowView : Grid
    {
        readonly MainWindow host;
        public Issue Issue;
        public string Id;

        readonly Border shadowLayer = new Border();
        readonly Border card = new Border();
        readonly StackPanel body = new StackPanel();
        readonly Border headerHost = new Border();
        readonly Border drawerHost = new Border();

        string sig;
        bool built, lastExpanded, isRunning;
        bool hovering;
        UIElement grip;

        // Local edit state (survives rebuilds)
        bool editingTitle;
        string editingCommentId;
        public string CommentDraft = "";
        public readonly List<string> Staged = new List<string>();
        public int Uploading;
        bool dirtyWhileEditing;
        TextBox commentInput;
        bool curExtracting, curLast;

        public IssueRowView(MainWindow host)
        {
            this.host = host;
            Children.Add(shadowLayer);
            Children.Add(card);
            card.Child = body;
            body.Children.Add(headerHost);
            body.Children.Add(drawerHost);
            drawerHost.Visibility = Visibility.Collapsed;
            MouseEnter += (s, e) => { hovering = true; ShowHoverBits(); };
            MouseLeave += (s, e) => { hovering = false; ShowHoverBits(); };
        }

        public double TargetOpacity { get; private set; }

        public bool IsEditing
        {
            get { return editingTitle || editingCommentId != null; }
        }

        void ShowHoverBits()
        {
            if (grip != null) grip.Opacity = hovering || host.DraggingId == Id ? 1 : 0;
        }

        string Sig()
        {
            var i = Issue;
            var sb = new System.Text.StringBuilder();
            sb.Append(Logic.GetStatus(i)).Append('|').Append(i.Title).Append('|').Append(i.Expanded).Append('|')
              .Append(curExtracting).Append('|').Append(curLast).Append('|').Append(Staged.Count).Append('|').Append(Uploading);
            foreach (var c in i.Comments)
            {
                sb.Append('|').Append(c.Id).Append('~').Append(c.Text);
                if (c.HasImages) foreach (var im in c.Images) sb.Append('#').Append(im.Length).Append(im.Length > 40 ? im.Substring(im.Length - 24) : im);
            }
            return sb.ToString();
        }

        public void Update(Issue issue, bool extracting, bool isLast, bool dragging)
        {
            Issue = issue;
            Id = issue.Id;
            curExtracting = extracting;
            curLast = isLast;
            if (IsEditing && built)
            {
                // Never rebuild under the user's cursor; apply once the edit ends.
                dirtyWhileEditing = true;
                ApplyDragVisual(dragging);
                return;
            }
            string s = Sig();
            if (s != sig)
            {
                sig = s;
                Build(extracting, isLast);
            }
            ApplyDragVisual(dragging);
        }

        /// Rebuild now with the latest data (used for local view-state changes).
        public void ForceRebuild()
        {
            dirtyWhileEditing = false;
            sig = Sig();
            Build(curExtracting, curLast);
        }

        void FlushDeferred()
        {
            if (dirtyWhileEditing) ForceRebuild();
        }

        void ApplyDragVisual(bool dragging)
        {
            double target = dragging ? 0.55 : (Logic.GetStatus(Issue) == Status.Backlog ? 0.56 : 1);
            if (Math.Abs(target - TargetOpacity) > 0.001 && built)
            {
                var a = new DoubleAnimation(Opacity, target, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop };
                Opacity = target;
                BeginAnimation(OpacityProperty, a);
            }
            else if (!built) Opacity = target;
            TargetOpacity = target;
            ScaleTransform sc; TranslateTransform tr;
            Anim.Transforms(this, out sc, out tr);
            if (dragging) Anim.ScaleTo(this, 1.015, 160, Anim.Out);
            else if (sc.ScaleX != 1 && host.DraggingId == null) Anim.ScaleTo(this, 1, 260, Anim.RowSpring);
            if (!isRunning)
            {
                card.Background = dragging ? (Brush)Pal.B(Pal.S50, 0.9) : Brushes.Transparent;
                card.CornerRadius = new CornerRadius(dragging ? 12 : 0);
            }
            ShowHoverBits();
        }

        // ------------------------------------------------------------------ build

        void Build(bool extracting, bool isLast)
        {
            var issue = Issue;
            bool inputHadFocus = commentInput != null && commentInput.IsKeyboardFocusWithin;
            commentInput = null;
            dirtyWhileEditing = false;
            string status = Logic.GetStatus(issue);
            isRunning = status == Status.InWork;
            bool isBacklog = status == Status.Backlog, isCompleted = status == Status.Completed;

            // Container styling
            if (isRunning)
            {
                Margin = new Thickness(0, 10, 0, 10);
                card.CornerRadius = new CornerRadius(16);
                card.Background = Theme.TintBg;
                card.BorderBrush = Theme.TintBorder;
                card.BorderThickness = new Thickness(1);
                card.Padding = new Thickness(14, 4, 14, 4);
                shadowLayer.Visibility = Visibility.Visible;
                shadowLayer.CornerRadius = new CornerRadius(16);
                shadowLayer.Background = Theme.TintBg;
                shadowLayer.Effect = new DropShadowEffect { Color = Theme.Current.Accent, Opacity = 0.2, BlurRadius = 22, ShadowDepth = 5, Direction = 270, RenderingBias = RenderingBias.Performance };
            }
            else
            {
                Margin = new Thickness(0);
                card.CornerRadius = new CornerRadius(0);
                card.Background = Brushes.Transparent;
                card.BorderBrush = Pal.B(Pal.S100, isBacklog ? 0.8 : 1);
                card.BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1);
                card.Padding = new Thickness(0);
                shadowLayer.Visibility = Visibility.Collapsed;
                shadowLayer.Effect = null;
            }

            headerHost.Child = BuildHeader(issue, status, extracting);

            // Drawer
            bool expanded = issue.Expanded;
            if (expanded)
            {
                drawerHost.Child = BuildDrawer(issue);
                if (!lastExpanded && built) Anim.Reveal(drawerHost, true);
                else drawerHost.Visibility = Visibility.Visible;
            }
            else if (lastExpanded && built)
            {
                Anim.Reveal(drawerHost, false, () => { if (!Issue.Expanded) drawerHost.Child = null; });
            }
            else
            {
                drawerHost.Visibility = Visibility.Collapsed;
                drawerHost.Child = null;
            }
            lastExpanded = expanded;
            built = true;
            if (inputHadFocus && commentInput != null)
            {
                var ci = commentInput;
                ci.Loaded += (s, e) => { ci.Focus(); ci.CaretIndex = ci.Text.Length; };
            }
        }

        UIElement BuildHeader(Issue issue, string status, bool extracting)
        {
            bool isBacklog = status == Status.Backlog, isCompleted = status == Status.Completed;
            var row = new Border
            {
                Margin = new Thickness(-8, 0, -8, 0),
                Padding = new Thickness(8, 12, 8, 12),
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
            };
            if (!isRunning)
            {
                row.MouseEnter += (s, e) => row.Background = Pal.B(Pal.S50, 0.8);
                row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            }
            var g = new Grid();
            for (int c = 0; c < 5; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            row.Child = g;

            // 0. Drag grip (visible on hover)
            var gripBox = new Border
            {
                Width = 18, Height = 22, Margin = new Thickness(-4, 0, 12, 0), CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Opacity = 0,
                Child = Icons.Make("grip", 14, Pal.B(Pal.S400)),
                ToolTip = "Drag to reorder",
            };
            gripBox.MouseEnter += (s, e) => gripBox.Background = Pal.B(Pal.S200, 0.6);
            gripBox.MouseLeave += (s, e) => gripBox.Background = Brushes.Transparent;
            gripBox.MouseLeftButtonDown += (s, e) => { e.Handled = true; host.BeginDrag(this); };
            grip = gripBox;
            g.Children.Add(gripBox);

            // 1. Status pill
            var pill = new Btn(26, 13, new Thickness(10, 0, 10, 0)) { Margin = new Thickness(0, 0, 12, 0) };
            pill.Tactile(true);
            if (isRunning)
            {
                pill.Paint(Theme.Accent, Theme.Accent, Brushes.White, Brushes.White, Theme.AccentStrong, Theme.AccentStrong);
                pill.AddElement(Dots());
                pill.AddText("Running", 11.5, FontWeights.SemiBold, false, 6);
            }
            else if (status == Status.Task)
            {
                pill.Paint(Pal.B(Pal.S100), Theme.TintHover, Pal.B(Pal.S700), Theme.Accent, Pal.B(Pal.S200, 0.9), Theme.TintBorder);
                var play = pill.AddIcon("play", 10);
                pill.AddText("Task", 11.5, FontWeights.SemiBold, false, 5);
            }
            else if (isBacklog)
            {
                pill.Paint(Pal.B(Pal.S100, 0.8), Pal.B(Pal.S100), Pal.B(Pal.S500), Pal.B(Pal.S800));
                pill.Dashed(Pal.B(Pal.S300), Pal.B(Pal.S400));
                pill.AddIcon("lightbulb", 12, 2.1);
                pill.AddText("Idea", 11.5, FontWeights.SemiBold, false, 5);
            }
            else
            {
                pill.Paint(Pal.B(Pal.E50), Pal.B(Pal.S100), Pal.B(Pal.E700), Pal.B(Pal.S700), Pal.B(Pal.E200), Pal.B(Pal.S300));
                pill.AddIcon("rotate-ccw", 12, 2.2);
                pill.AddText("Restore", 11.5, FontWeights.SemiBold, false, 5);
            }
            string next = Logic.NextStatus(status);
            pill.ToolTip = "Click to switch to " + (next == Status.InWork ? "running" : next == Status.Backlog ? "backlog idea" : next == Status.Completed ? "completed" : "task");
            pill.Click = () => host.CycleStatus(issue.Id);
            Grid.SetColumn(pill, 1);
            g.Children.Add(pill);

            // 2. Title (click to edit)
            UIElement titleEl;
            if (editingTitle)
            {
                var tb = T.Input(15, FontWeights.Medium);
                tb.Text = issue.Title;
                tb.BorderThickness = new Thickness(0, 0, 0, 1);
                tb.BorderBrush = Pal.B(Pal.S300);
                tb.Padding = new Thickness(0, 0, 0, 2);
                bool cancelled = false;
                Action commit = () =>
                {
                    if (!editingTitle) return;
                    editingTitle = false;
                    if (cancelled) { sig = null; host.RefreshRow(this); return; }
                    string v = tb.Text.Trim();
                    if (v.Length == 0) host.DeleteIssue(issue.Id);
                    else if (v != issue.Title) host.UpdateTitle(issue.Id, v);
                    else { sig = null; host.RefreshRow(this); }
                };
                tb.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter) { e.Handled = true; commit(); }
                    else if (e.Key == Key.Escape) { e.Handled = true; cancelled = true; commit(); }
                };
                tb.LostKeyboardFocus += (s, e) => commit();
                tb.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
                titleEl = tb;
            }
            else
            {
                var t = new TextBlock
                {
                    Text = issue.Title,
                    FontFamily = Fonts.Sans,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.IBeam,
                    Background = Brushes.Transparent,
                };
                Brush fg, hoverFg;
                if (isCompleted)
                {
                    t.FontSize = 14.5; t.FontWeight = FontWeights.Medium; fg = hoverFg = Pal.B(Pal.S400);
                    var deco = new TextDecoration(TextDecorationLocation.Strikethrough, new Pen(Pal.B(Pal.S300), 1.5), 0, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel);
                    t.TextDecorations = new TextDecorationCollection { deco };
                }
                else if (extracting)
                {
                    t.FontSize = 15; t.FontWeight = FontWeights.Medium; fg = hoverFg = Pal.B(Pal.S600);
                    t.Opacity = 0.65;
                    t.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 0.75, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                }
                else if (isRunning) { t.FontSize = 15.5; t.FontWeight = FontWeights.SemiBold; fg = Pal.B(Pal.S950); hoverFg = Theme.AccentDark; }
                else if (isBacklog) { t.FontSize = 14.5; t.FontWeight = FontWeights.Normal; fg = Pal.B(Pal.S600); hoverFg = Pal.B(Pal.S900); }
                else { t.FontSize = 15; t.FontWeight = FontWeights.Medium; fg = Pal.B(Pal.S900); hoverFg = Pal.B(Pal.S700); }
                t.Foreground = fg;
                t.MouseEnter += (s, e) => t.Foreground = hoverFg;
                t.MouseLeave += (s, e) => t.Foreground = fg;
                t.MouseLeftButtonUp += (s, e) => { editingTitle = true; sig = null; host.RefreshRow(this); };
                t.HorizontalAlignment = HorizontalAlignment.Left;
                titleEl = t;
            }
            ((FrameworkElement)titleEl).Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(titleEl, 2);
            g.Children.Add(titleEl);

            // 3. Done (running only)
            if (isRunning)
            {
                var done = new Btn(28, 14, new Thickness(10, 0, 10, 0)) { Margin = new Thickness(0, 0, 12, 0), ToolTip = "Mark problem as completed" };
                done.Paint(Pal.B(Pal.White, 0.9), Pal.B(Pal.E600), Pal.B(Pal.S700), Brushes.White, Theme.TintBorder, Pal.B(Pal.E600));
                done.AddIcon("check", 12, 2.5);
                done.AddText("Done", 11.5, FontWeights.SemiBold, false, 4);
                done.Click = () => host.SetStatus(issue.Id, Status.Completed);
                Grid.SetColumn(done, 3);
                g.Children.Add(done);
            }

            // 4. Comments pill
            int count = issue.Comments.Count, att = Logic.AttachmentCount(issue);
            var cp = new Btn(28, 14, new Thickness(10, 0, 10, 0)) { HoverScale = 1.07, PressScale = 0.88 };
            if (issue.Expanded)
            {
                var bg = isRunning ? (Brush)Theme.Accent : Pal.B(Pal.S900);
                cp.Paint(bg, bg, Brushes.White, Brushes.White);
            }
            else if (count > 0)
            {
                if (isRunning) cp.Paint(Pal.B(Pal.White, 0.85), Brushes.White, Pal.B(Pal.S800), Pal.B(Pal.S800), Theme.TintDrawerBorder, Theme.TintDrawerBorder);
                else cp.Paint(Brushes.Transparent, Pal.B(Pal.S100), Pal.B(Pal.S700), Pal.B(Pal.S700));
            }
            else
            {
                if (isRunning) cp.Paint(Brushes.Transparent, Pal.B(Pal.White, 0.7), Theme.TintMuted, Theme.AccentDark);
                else cp.Paint(Brushes.Transparent, Pal.B(Pal.S100), Pal.B(Pal.S400), Pal.B(Pal.S700));
            }
            if (att > 0) cp.AddIcon("image", 13.5, 2.1);
            cp.AddIcon("message-square", 14, issue.Expanded || count > 0 ? 2.2 : 1.9);
            if (count > 0) cp.AddText(count.ToString(), 11.5, FontWeights.SemiBold, true, 5);
            cp.ToolTip = (issue.Expanded ? "Collapse" : "Expand") + " comments (" + count + ")";
            cp.Click = () => host.ToggleExpand(issue.Id);
            Grid.SetColumn(cp, 4);
            g.Children.Add(cp);

            return row;
        }

        static UIElement Dots()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int k = 0; k < 3; k++)
            {
                var dot = new Ellipse { Width = 3.5, Height = 3.5, Fill = Brushes.White, Margin = new Thickness(k == 0 ? 0 : 2, 0, 0, 0) };
                ScaleTransform sc; TranslateTransform tr;
                Anim.Transforms(dot, out sc, out tr);
                var begin = TimeSpan.FromSeconds(k * 0.16);
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                Func<double, double, DoubleAnimation> mk = (from, to) =>
                {
                    var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(0.425)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = ease };
                    Timeline.SetDesiredFrameRate(a, 30);
                    return a;
                };
                tr.BeginAnimation(TranslateTransform.YProperty, mk(-1.8, 1.8));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, mk(0.88, 1.12));
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, mk(0.88, 1.12));
                dot.BeginAnimation(OpacityProperty, mk(0.45, 1));
                sp.Children.Add(dot);
            }
            return sp;
        }

        // ------------------------------------------------------------------ drawer

        UIElement BuildDrawer(Issue issue)
        {
            var box = new Border
            {
                Margin = new Thickness(30, 0, 0, 12),
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(12),
                Background = isRunning ? (Brush)Theme.DrawerBg : Pal.B(Pal.S50, 0.85),
                BorderBrush = isRunning ? (Brush)Theme.TintDrawerBorder : Pal.B(Pal.S200, 0.75),
                BorderThickness = new Thickness(1),
            };
            var stack = new StackPanel();
            box.Child = stack;

            foreach (var c in issue.Comments) stack.Children.Add(BuildComment(issue, c));

            // Staged images for the new comment
            if (Staged.Count > 0 || Uploading > 0)
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                for (int k = 0; k < Staged.Count; k++)
                {
                    int idx = k;
                    wp.Children.Add(host.StagedThumb(Staged[k], 56, 8, () => { Staged.RemoveAt(idx); sig = null; host.RefreshRow(this); }));
                }
                for (int k = 0; k < Uploading; k++) wp.Children.Add(host.UploadingThumb(56, 8));
                stack.Children.Add(wp);
            }

            // New comment input
            var form = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pick = new Btn(24, 12, new Thickness(0)) { Width = 24, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Attach image (or paste with Ctrl+V)" };
            pick.Paint(Brushes.Transparent, Pal.B(Pal.S200, 0.6), Pal.B(Pal.S400), Pal.B(Pal.S700));
            pick.AddIcon("image", 14, 2);
            pick.Click = () => host.PickImages(bmps => StageImages(bmps));
            form.Children.Add(pick);

            var input = T.Input(13.5, FontWeights.Normal);
            input.Padding = new Thickness(0, 4, 0, 4);
            input.Text = CommentDraft;
            TextBlock ph;
            var inputWrap = T.WithPlaceholder(input, "Write a comment or paste an image...", out ph);
            ph.Visibility = CommentDraft.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(inputWrap, 1);
            form.Children.Add(inputWrap);

            var submit = new Btn(24, 12, new Thickness(10, 0, 10, 0)) { Margin = new Thickness(8, 0, 0, 0), PressScale = 0.9, HoverScale = 1 };
            submit.Paint(Theme.Accent, Theme.AccentStrong, Brushes.White, Brushes.White);
            submit.AddIcon("corner-down-left", 12, 2);
            Action refreshSubmit = () =>
            {
                bool can = (CommentDraft.Trim().Length > 0 || Staged.Count > 0) && Uploading == 0;
                submit.Disabled = !can;
                submit.Tactile(can);
                if (can) submit.Paint(Theme.Accent, Theme.AccentStrong, Brushes.White, Brushes.White);
                else submit.Paint(Brushes.Transparent, Brushes.Transparent, Pal.B(Pal.S900), Pal.B(Pal.S900));
            };
            refreshSubmit();
            Action send = () =>
            {
                string text = CommentDraft.Trim();
                if ((text.Length == 0 && Staged.Count == 0) || Uploading > 0) return;
                var imgs = Staged.Count > 0 ? new List<string>(Staged) : null;
                CommentDraft = "";
                Staged.Clear();
                host.AddComment(issue.Id, text, imgs);
            };
            submit.Click = send;
            Grid.SetColumn(submit, 2);
            form.Children.Add(submit);

            input.TextChanged += (s, e) => { CommentDraft = input.Text; refreshSubmit(); };
            input.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; send(); }
                else if (e.Key == Key.Escape) { e.Handled = true; Keyboard.ClearFocus(); host.FocusList(); }
            };
            host.HookImagePaste(input, bmps => StageImages(bmps));
            commentInput = input;
            stack.Children.Add(form);
            return box;
        }

        public void FocusInput()
        {
            if (commentInput != null)
            {
                commentInput.Focus();
                commentInput.CaretIndex = commentInput.Text.Length;
            }
        }

        void StageImages(List<BitmapSource> bmps)
        {
            if (bmps.Count == 0) return;
            Sound.Play("add");
            if (!Issue.Expanded) host.ToggleExpand(Id);
            Uploading += bmps.Count;
            sig = null;
            host.RefreshRow(this);
            foreach (var b in bmps)
            {
                host.PrepareImage(b, url =>
                {
                    Uploading--;
                    if (url != null) Staged.Add(url);
                    sig = null;
                    host.RefreshRow(this);
                    FocusInput();
                });
            }
        }

        UIElement BuildComment(Issue issue, Comment c)
        {
            var wrap = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wrap.Children.Add(line);

            if (editingCommentId == c.Id)
            {
                var eg = new Grid();
                eg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                eg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tb = T.Input(13.5, FontWeights.Normal);
                tb.Text = c.Text;
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MaxHeight = 220;
                tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                tb.Padding = new Thickness(8, 4, 8, 4);
                var frame = new Border
                {
                    Background = Brushes.White, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                    BorderBrush = Theme.Accent, Child = tb,
                };
                eg.Children.Add(frame);
                bool cancelled = false;
                Action commit = () =>
                {
                    if (editingCommentId != c.Id) return;
                    editingCommentId = null;
                    if (cancelled) { sig = null; host.RefreshRow(this); FlushDeferred(); return; }
                    string v = tb.Text.Trim();
                    if (v.Length == 0 && !c.HasImages) host.DeleteComment(issue.Id, c.Id);
                    else if (v != c.Text) host.UpdateComment(issue.Id, c.Id, v);
                    else { sig = null; host.RefreshRow(this); }
                    FlushDeferred();
                };
                tb.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; commit(); }
                    else if (e.Key == Key.Escape) { e.Handled = true; cancelled = true; commit(); }
                };
                tb.LostKeyboardFocus += (s, e) => commit();
                tb.Loaded += (s, e) => { tb.Focus(); tb.CaretIndex = tb.Text.Length; };
                var ok = new Btn(24, 12, new Thickness(0)) { Width = 24, Margin = new Thickness(6, 2, 0, 0), VerticalAlignment = VerticalAlignment.Top, ToolTip = "Save comment (Enter)" };
                ok.Paint(Theme.Accent, Theme.AccentStrong, Brushes.White, Brushes.White);
                ok.AddIcon("check", 14, 2.5);
                ok.PreviewMouseLeftButtonDown += (s, e) => commit();
                Grid.SetColumn(ok, 1);
                eg.Children.Add(ok);
                Grid.SetColumnSpan(eg, 2);
                line.Children.Add(eg);
            }
            else
            {
                TextBlock text;
                if (c.Text.Length > 0)
                {
                    text = new TextBlock
                    {
                        Text = c.Text, FontFamily = Fonts.Sans, FontSize = 13.5, Foreground = Pal.B(Pal.S600),
                        TextWrapping = TextWrapping.Wrap, LineHeight = 22, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    };
                    text.MouseEnter += (s, e) => text.Foreground = Pal.B(Pal.S900);
                    text.MouseLeave += (s, e) => text.Foreground = Pal.B(Pal.S600);
                }
                else
                {
                    text = T.Text((c.Images != null && c.Images.Count > 1 ? "Attached images" : "Attached image") + " — click to add note", 12, FontWeights.Normal, Pal.B(Pal.S400));
                    text.MouseEnter += (s, e) => text.Foreground = Pal.B(Pal.S600);
                    text.MouseLeave += (s, e) => text.Foreground = Pal.B(Pal.S400);
                }
                text.Cursor = Cursors.IBeam;
                text.Background = Brushes.Transparent;
                text.ContextMenu = CopyMenu(c.Text);
                text.MouseLeftButtonUp += (s, e) => { editingCommentId = c.Id; sig = null; host.RefreshRow(this); };
                line.Children.Add(text);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, Margin = new Thickness(8, 0, 0, 0) };
                var edit = SmallIconBtn("pencil", 12, "Edit comment", () => { editingCommentId = c.Id; sig = null; host.RefreshRow(this); });
                var del = SmallIconBtn("x", 14, "Delete comment", () => host.DeleteComment(issue.Id, c.Id));
                actions.Children.Add(edit);
                actions.Children.Add(del);
                Grid.SetColumn(actions, 1);
                line.Children.Add(actions);
                wrap.MouseEnter += (s, e) => actions.Opacity = 1;
                wrap.MouseLeave += (s, e) => actions.Opacity = 0;
            }

            if (c.HasImages)
            {
                var wp = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                for (int k = 0; k < c.Images.Count; k++)
                {
                    int idx = k;
                    string src = c.Images[k];
                    wp.Children.Add(host.AttachmentThumb(src, () => host.PreviewImage(src), () => host.DeleteCommentImage(issue.Id, c.Id, idx)));
                }
                wrap.Children.Add(wp);
            }
            return wrap;
        }

        static ContextMenu CopyMenu(string text)
        {
            var m = new ContextMenu();
            var item = new MenuItem { Header = "Copy text" };
            item.Click += (s, e) => { try { Clipboard.SetText(text ?? ""); } catch { } };
            m.Items.Add(item);
            return m;
        }

        static Btn SmallIconBtn(string icon, double size, string tip, Action click)
        {
            var b = new Btn(20, 4, new Thickness(4, 0, 4, 0)) { ToolTip = tip, HoverScale = 1, PressScale = 0.88 };
            b.Paint(Brushes.Transparent, Brushes.Transparent, Pal.B(Pal.S400), Pal.B(Pal.S700));
            b.AddIcon(icon, size, 2);
            b.Click = click;
            return b;
        }
    }
}
