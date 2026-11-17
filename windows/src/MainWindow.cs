// Main window: project carousel, composer, completed drawer, issue list, lightbox, sync status card/chip.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Tactile
{
    public sealed class Options
    {
        public string DataDir, ConfigPath, SnapshotPath;
        public int SnapshotDelayMs;
        public bool Offline, ShowCompleted, SelfTest;
        public int Width = 0, Height = 0;
    }

    public sealed class MainWindow : Window
    {
        readonly Options opts;
        public Options Opts { get { return opts; } }
        readonly Config cfg;
        public readonly AppState State;
        readonly SyncEngine sync;
        readonly HashSet<string> extracting = new HashSet<string>();
        public string DraggingId;

        // Layout
        readonly Grid root = new Grid();
        readonly Rectangle topGradient = new Rectangle { Height = 170, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        readonly GradientStop gradTop = new GradientStop(Colors.White, 0);
        readonly ScrollViewer scroller = new ScrollViewer();
        readonly StackPanel page = new StackPanel();
        readonly StackPanel mainList = new StackPanel();
        readonly StackPanel completedList = new StackPanel();
        readonly Border completedDrawer = new Border();
        readonly TextBlock emptyText = new TextBlock();
        readonly Dictionary<string, IssueRowView> mainRows = new Dictionary<string, IssueRowView>();
        readonly Dictionary<string, IssueRowView> completedRows = new Dictionary<string, IssueRowView>();

        // Carousel
        readonly Grid carouselRow = new Grid();
        readonly ScrollViewer track = new ScrollViewer();
        readonly StackPanel trackPanel = new StackPanel { Orientation = Orientation.Horizontal };
        readonly StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        readonly Border addProjectHost = new Border();
        Btn prevBtn, nextBtn;
        string carouselSig;
        string editingProjectId;
        bool addingProject;

        // Composer
        TextBox composer;
        Btn plusBtn, imgBtn, ideaBtn, completedBtn;
        Border modeIndicator;
        Icon modeIcon;
        TextBlock completedCount;
        Icon chevron;
        readonly WrapPanel stagedPanel = new WrapPanel { Margin = new Thickness(36, 6, 0, 0) };
        readonly List<string> composerStaged = new List<string>();
        int composerUploading;
        bool ideaMode, showCompleted, wasPromptMode;
        string modeShown = "";

        // Overlays
        readonly Grid lightbox = new Grid();
        readonly Border lightboxFrame = new Border();
        BitmapSource lightboxBmp;
        readonly Border syncChip = new Border();
        readonly TextBlock syncText = new TextBlock();
        readonly Border syncIconHost = new Border();

        public MainWindow(Options opts, Config cfg)
        {
            this.opts = opts;
            this.cfg = cfg;
            State = new AppState(opts.DataDir, Dispatcher);
            State.Load();
            App.Trace("state loaded: " + State.Issues.Count + " issues");
            sync = new SyncEngine(State, Dispatcher, cfg);
            Images.CacheDir = System.IO.Path.Combine(opts.DataDir, "imgcache");
            Images.Storage = cfg.SyncEnabled ? new Supabase(cfg) : null;

            Title = "Issues";
            Background = Brushes.White;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            MinWidth = 440;
            MinHeight = 420;
            FontFamily = Fonts.Sans;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            AllowDrop = true;
            LoadBounds();

            BuildLayout();
            App.Trace("layout built");
            Theme.Apply(Themes.Get(State.ActiveProject.ColorKey), false);
            showCompleted = opts.ShowCompleted;
            firstFrameShown = showCompleted;
            Render(false);
            firstFrameShown = false;
            if (showCompleted) completedDrawer.Visibility = Visibility.Visible;

            sync.StatusChanged += OnSyncStatus;
            sync.Pulled += () => Render(false);
            SourceInitialized += (s, e) => ApplyCaptionColor();
            Theme.Changed += ApplyCaptionColor;
            ContentRendered += OnFirstRender;
            Loaded += (s, e) => App.Trace("loaded");
            SourceInitialized += (s, e) => App.Trace("hwnd created");
            Activated += (s, e) =>
            {
                if ((DateTime.UtcNow - sync.LastPullUtc).TotalSeconds > 20) sync.RequestPull();
            };
            Closing += (s, e) => { SaveBounds(); State.SaveNow(true); };
            PreviewKeyDown += OnWindowKey;
            Drop += OnDropFiles;
            DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };

            var periodic = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            periodic.Tick += (s, e) => { if (IsActive) sync.RequestPull(); };
            periodic.Start();
        }

        void OnFirstRender(object sender, EventArgs e)
        {
            ContentRendered -= OnFirstRender;
            double ms = App.SinceStartMs();
            App.Trace("first frame");
            Log.Write("Startup to first frame: " + Math.Round(ms) + " ms");
            Sound.Warm();
            firstFrameShown = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                suppressEnter = true;
                Render(false);
                suppressEnter = false;
            }));
            if (!opts.Offline) sync.Start();
            else OnSyncStatus(SyncState.Disabled, 0);
            if (opts.SnapshotPath != null) App.ScheduleSnapshot(this, opts.SnapshotPath, ms);
            if (opts.SelfTest) SelfTest.Run(this);
            if (!opts.SelfTest && opts.SnapshotPath == null) composer.Focus();
        }

        // ================================================================== layout

        void BuildLayout()
        {
            Content = root;
            var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            grad.GradientStops.Add(gradTop);
            grad.GradientStops.Add(new GradientStop(Colors.White, 1));
            topGradient.Fill = grad;
            root.Children.Add(topGradient);

            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Focusable = false;
            scroller.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), SlimScrollBar.Style());
            scroller.PreviewMouseWheel += SmoothWheel;
            root.Children.Add(scroller);

            page.MaxWidth = 760;
            page.Margin = new Thickness(24, 40, 24, 48);
            page.HorizontalAlignment = HorizontalAlignment.Stretch;
            scroller.Content = page;

            page.Children.Add(BuildCarousel());
            page.Children.Add(BuildComposer());

            completedDrawer.Visibility = Visibility.Collapsed;
            completedDrawer.Child = BuildCompletedPanel();
            page.Children.Add(completedDrawer);

            mainList.Margin = new Thickness(0, 8, 0, 0);
            mainList.Background = Brushes.Transparent;
            page.Children.Add(mainList);

            emptyText.FontFamily = Fonts.Sans;
            emptyText.FontSize = 13.5;
            emptyText.Foreground = Pal.B(Pal.S400);
            emptyText.TextAlignment = TextAlignment.Center;
            emptyText.TextWrapping = TextWrapping.Wrap;
            emptyText.Margin = new Thickness(0, 48, 0, 48);
            page.Children.Add(emptyText);

            BuildSyncChip();
            BuildLightbox();

            mainList.MouseMove += OnDragMove;
            mainList.MouseLeftButtonUp += (s, e) => EndDrag();
            mainList.LostMouseCapture += (s, e) => EndDrag();
        }

        // ================================================================== carousel

        UIElement BuildCarousel()
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            carouselRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            carouselRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            carouselRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            prevBtn = NavBtn("chevron-left", "Previous project (Ctrl+Shift+Tab)", () => StepProject(-1));
            nextBtn = NavBtn("chevron-right", "Next project (Ctrl+Tab)", () => StepProject(1));
            nextBtn.Margin = new Thickness(4, 0, 0, 0);
            nav.Children.Add(prevBtn);
            nav.Children.Add(nextBtn);
            carouselRow.Children.Add(nav);

            track.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            track.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            track.Focusable = false;
            track.Padding = new Thickness(8, 6, 12, 12);
            track.Margin = new Thickness(-8, -6, -12, -12);
            track.Content = trackPanel;
            track.PreviewMouseWheel += (s, e) =>
            {
                track.ScrollToHorizontalOffset(track.HorizontalOffset - e.Delta * 0.6);
                e.Handled = true;
            };
            Grid.SetColumn(track, 1);
            carouselRow.Children.Add(track);

            addProjectHost.Margin = new Thickness(8, 0, 0, 0);
            addProjectHost.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(addProjectHost, 2);
            carouselRow.Children.Add(addProjectHost);

            section.Children.Add(carouselRow);
            section.Children.Add(dots);
            return section;
        }

        static Btn NavBtn(string icon, string tip, Action click)
        {
            var b = new Btn(28, 14, new Thickness(0)) { Width = 28, ToolTip = tip };
            b.Paint(Brushes.White, Pal.B(Pal.S50), Pal.B(Pal.S600), Pal.B(Pal.S900), Pal.B(Pal.S200, 0.9), Pal.B(Pal.S200, 0.9));
            b.AddIcon(icon, 14, 2.2);
            b.Click = click;
            return b;
        }

        void RenderCarousel()
        {
            var counts = State.Issues.Where(i => !i.Completed).GroupBy(i => i.ProjectId).ToDictionary(g => g.Key, g => g.Count());
            string sig = string.Join(";", State.Projects.Select(p =>
            {
                int c; counts.TryGetValue(p.Id, out c);
                return p.Id + "," + p.Name + "," + p.ColorKey + "," + c;
            })) + "|" + State.ActiveProject.Id + "|" + editingProjectId + "|" + addingProject;
            if (sig == carouselSig) return;
            carouselSig = sig;

            prevBtn.Disabled = nextBtn.Disabled = State.Projects.Count <= 1;
            prevBtn.Refresh(); nextBtn.Refresh();

            trackPanel.Children.Clear();
            FrameworkElement activeSlide = null;
            foreach (var p in State.Projects)
            {
                int c; counts.TryGetValue(p.Id, out c);
                var slide = BuildProjectSlide(p, c);
                if (p.Id == State.ActiveProject.Id) activeSlide = slide;
                trackPanel.Children.Add(slide);
            }

            // Add project button / inline form
            if (addingProject) addProjectHost.Child = BuildAddProjectForm();
            else
            {
                var add = new Btn(32, 16, new Thickness(10, 0, 10, 0)) { ToolTip = "Create a new color-tinted project", HoverScale = 1.03 };
                add.Paint(Brushes.Transparent, Brushes.Transparent, Pal.B(Pal.S500), Pal.B(Pal.S900));
                add.Dashed(Pal.B(Pal.S300), Pal.B(Pal.S400));
                add.AddIcon("plus", 14, 2.2);
                add.AddText("Project", 12, FontWeights.Medium, false, 4);
                add.Click = () => { addingProject = true; RenderCarousel(); };
                addProjectHost.Child = add;
            }

            // Marker dots
            dots.Children.Clear();
            dots.Visibility = State.Projects.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var p in State.Projects)
            {
                bool active = p.Id == State.ActiveProject.Id;
                var th = Themes.Get(p.ColorKey);
                var d = new Border
                {
                    Height = 6, Width = active ? 20 : 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0),
                    Background = active ? Pal.B(th.Accent) : Pal.B(Pal.S300), Cursor = Cursors.Hand, ToolTip = p.Name,
                };
                string pid = p.Id;
                d.MouseLeftButtonUp += (s, e) => SelectProject(pid);
                dots.Children.Add(d);
            }

            if (activeSlide != null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => CenterSlide(activeSlide)));
        }

        FrameworkElement BuildProjectSlide(Project p, int openCount)
        {
            bool active = p.Id == State.ActiveProject.Id;
            var th = Themes.Get(p.ColorKey);
            var slide = new Border
            {
                Height = 36, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 8, 0),
                Background = active ? Pal.B(th.TintBg) : Brushes.White,
                BorderBrush = active ? Pal.B(th.TintBorder) : Pal.B(Pal.S200),
                Cursor = Cursors.Hand,
            };
            if (active) slide.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = th.Accent, Opacity = 0.18, BlurRadius = 18, ShadowDepth = 4, Direction = 270 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            slide.Child = sp;

            // Color dot (click on active cycles color)
            var dot = new Btn(16, 8, new Thickness(0)) { Width = 16, HoverScale = 1.1, ToolTip = active ? th.Label + " — click to change project color" : th.Label + " — click to open project" };
            dot.Paint(Pal.B(th.Accent), Pal.B(th.Accent), Brushes.White, Brushes.White);
            string pid = p.Id;
            dot.Click = () => { if (pid != State.ActiveProject.Id) SelectProject(pid); else CycleProjectColor(pid); };
            sp.Children.Add(dot);

            if (editingProjectId == p.Id)
            {
                var tb = T.Input(12.5, FontWeights.Medium);
                tb.Text = p.Name;
                tb.Width = 96;
                tb.Padding = new Thickness(6, 2, 6, 2);
                var frame = new Border { Child = tb, Background = Pal.B(Pal.White, 0.9), CornerRadius = new CornerRadius(4), BorderBrush = Pal.B(Pal.S300), BorderThickness = new Thickness(1), Margin = new Thickness(8, 0, 4, 0) };
                bool cancelled = false;
                Action commit = () =>
                {
                    if (editingProjectId != pid) return;
                    editingProjectId = null;
                    if (!cancelled && tb.Text.Trim().Length > 0 && tb.Text.Trim() != p.Name) RenameProject(pid, tb.Text.Trim());
                    else RenderCarousel();
                };
                tb.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter) { e.Handled = true; commit(); }
                    else if (e.Key == Key.Escape) { e.Handled = true; cancelled = true; commit(); }
                };
                tb.LostKeyboardFocus += (s, e) => commit();
                tb.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
                sp.Children.Add(frame);
                var ok = new Btn(20, 10, new Thickness(0)) { Width = 20 };
                ok.Paint(Pal.B(th.Accent), Pal.B(th.Accent), Brushes.White, Brushes.White);
                ok.AddIcon("check", 12, 2.5);
                ok.PreviewMouseLeftButtonDown += (s, e) => commit();
                sp.Children.Add(ok);
                return slide;
            }

            var name = T.Text(p.Name, 13, active ? FontWeights.SemiBold : FontWeights.Normal, active ? Pal.B(th.AccentDark) : Pal.B(Pal.S600));
            name.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(name);
            var cnt = T.Mono(openCount.ToString(), 11, active ? Pal.B(th.AccentDark) : Pal.B(Pal.S600));
            cnt.Opacity = 0.6;
            cnt.FontWeight = FontWeights.Medium;
            cnt.Margin = new Thickness(6, 1, 0, 0);
            sp.Children.Add(cnt);

            slide.MouseLeftButtonUp += (s, e) => SelectProject(pid);
            slide.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { editingProjectId = pid; RenderCarousel(); e.Handled = true; }
            };
            if (!active)
            {
                slide.MouseEnter += (s, e) => { slide.Background = Pal.B(Pal.S50, 0.8); name.Foreground = Pal.B(Pal.S900); };
                slide.MouseLeave += (s, e) => { slide.Background = Brushes.White; name.Foreground = Pal.B(Pal.S600); };
            }
            else
            {
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 0), Opacity = 0 };
                var ren = new Btn(20, 4, new Thickness(4, 0, 4, 0)) { ToolTip = "Rename project (or double-click)", HoverScale = 1 };
                ren.Paint(Brushes.Transparent, Brushes.Transparent, Pal.B(Pal.S400), Pal.B(Pal.S700));
                ren.AddIcon("pencil", 10, 2);
                ren.Click = () => { editingProjectId = pid; RenderCarousel(); };
                tools.Children.Add(ren);
                if (State.Projects.Count > 1)
                {
                    var del = new Btn(20, 4, new Thickness(4, 0, 4, 0)) { ToolTip = "Delete project", HoverScale = 1 };
                    del.Paint(Brushes.Transparent, Brushes.Transparent, Pal.B(Pal.S400), Pal.B(Pal.S700));
                    del.AddIcon("x", 12, 2);
                    del.Click = () => DeleteProject(pid);
                    tools.Children.Add(del);
                }
                sp.Children.Add(tools);
                slide.MouseEnter += (s, e) => tools.Opacity = 1;
                slide.MouseLeave += (s, e) => tools.Opacity = 0;
            }
            return slide;
        }

        UIElement BuildAddProjectForm()
        {
            var frame = new Border { CornerRadius = new CornerRadius(12), BorderBrush = Pal.B(Pal.S200), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(8, 4, 6, 4) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            frame.Child = sp;
            var tb = T.Input(12.5, FontWeights.Medium);
            tb.Width = 112;
            TextBlock ph;
            sp.Children.Add(T.WithPlaceholder(tb, "Project name...", out ph));
            var ok = new Btn(20, 10, new Thickness(0)) { Width = 20, Margin = new Thickness(4, 0, 0, 0) };
            ok.Paint(Theme.Accent, Theme.Accent, Brushes.White, Brushes.White);
            ok.AddIcon("check", 12, 2.5);
            Action submit = () =>
            {
                if (!addingProject) return;
                addingProject = false;
                string n = tb.Text.Trim();
                if (n.Length > 0) CreateProject(n);
                else RenderCarousel();
            };
            ok.PreviewMouseLeftButtonDown += (s, e) => submit();
            sp.Children.Add(ok);
            tb.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; submit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; addingProject = false; RenderCarousel(); }
            };
            tb.LostKeyboardFocus += (s, e) => { if (addingProject && tb.Text.Trim().Length == 0) { addingProject = false; RenderCarousel(); } };
            tb.Loaded += (s, e) => tb.Focus();
            return frame;
        }

        void CenterSlide(FrameworkElement slide)
        {
            if (!slide.IsLoaded || PresentationSource.FromVisual(slide) == null) return;
            double x = slide.TransformToAncestor(trackPanel).Transform(new Point(0, 0)).X;
            double target = x - track.ViewportWidth / 2 + slide.ActualWidth / 2;
            target = Math.Max(0, Math.Min(target, track.ScrollableWidth));
            AnimateScroll(track, target, true);
        }

        // Smooth scrolling (WPF can't animate offsets directly; tween per frame).
        readonly Dictionary<ScrollViewer, double> scrollTargets = new Dictionary<ScrollViewer, double>();
        bool scrollHooked;

        void AnimateScroll(ScrollViewer sv, double target, bool horizontal)
        {
            scrollTargets[sv] = target;
            sv.Tag = horizontal;
            if (!scrollHooked) { scrollHooked = true; CompositionTarget.Rendering += ScrollTick; }
        }

        void ScrollTick(object sender, EventArgs e)
        {
            foreach (var sv in scrollTargets.Keys.ToList())
            {
                bool h = sv.Tag is bool && (bool)sv.Tag;
                double cur = h ? sv.HorizontalOffset : sv.VerticalOffset;
                double target = scrollTargets[sv];
                double next = cur + (target - cur) * 0.22;
                if (Math.Abs(target - next) < 0.6) { next = target; scrollTargets.Remove(sv); }
                if (h) sv.ScrollToHorizontalOffset(next); else sv.ScrollToVerticalOffset(next);
            }
            if (scrollTargets.Count == 0) { CompositionTarget.Rendering -= ScrollTick; scrollHooked = false; }
        }

        void SmoothWheel(object sender, MouseWheelEventArgs e)
        {
            // Let multi-line text boxes that can scroll handle their own wheel.
            var tb = FindAncestor<TextBox>(e.OriginalSource as DependencyObject);
            if (tb != null && tb.ExtentHeight > tb.ViewportHeight) return;
            e.Handled = true;
            double basis;
            if (!scrollTargets.TryGetValue(scroller, out basis)) basis = scroller.VerticalOffset;
            double target = Math.Max(0, Math.Min(scroller.ScrollableHeight, basis - e.Delta * 0.9));
            scroller.Tag = false;
            AnimateScroll(scroller, target, false);
        }

        static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                var t = d as T;
                if (t != null) return t;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        // ================================================================== composer

        UIElement BuildComposer()
        {
            var bar = new Border { BorderBrush = Pal.B(Pal.S200, 0.8), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8) };
            var stack = new StackPanel();
            bar.Child = stack;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stack.Children.Add(row);
            stack.Children.Add(stagedPanel);

            var form = new Grid { Margin = new Thickness(0, 6, 8, 6) };
            for (int c = 0; c < 5; c++) form.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            row.Children.Add(form);

            plusBtn = new Btn(26, 13, new Thickness(0)) { Width = 26, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0), HoverScale = 1.08, PressScale = 0.86, ToolTip = "Add (Enter)" };
            plusBtn.AddIcon("plus", 14, 2.5);
            plusBtn.Click = SubmitComposer;
            form.Children.Add(plusBtn);

            composer = T.Input(15, FontWeights.Medium);
            composer.AcceptsReturn = true;
            composer.TextWrapping = TextWrapping.Wrap;
            composer.MaxHeight = 180;
            composer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            composer.VerticalAlignment = VerticalAlignment.Top;
            composer.Padding = new Thickness(0, 4, 0, 2);
            TextBlock ph;
            var wrap = T.WithPlaceholder(composer, "Add a task, prompt, or paste an image...", out ph);
            composerPlaceholder = ph;
            Grid.SetColumn(wrap, 1);
            form.Children.Add(wrap);
            composer.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; SubmitComposer(); }
                else if (e.Key == Key.Escape) { e.Handled = true; FocusList(); }
            };
            composer.TextChanged += (s, e) =>
            {
                bool isPrompt = Logic.IsPromptMode(composer.Text);
                if (isPrompt != wasPromptMode && composer.Text.Trim().Length > 0)
                {
                    Sound.Play(isPrompt ? "drop" : "drag_start");
                    wasPromptMode = isPrompt;
                }
                RefreshComposer();
            };
            HookImagePaste(composer, StageComposerImages);

            imgBtn = new Btn(28, 14, new Thickness(0)) { Width = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Attach image (or paste anywhere with Ctrl+V)" };
            imgBtn.AddIcon("image", 14, 2.1);
            imgBtn.Click = () => PickImages(StageComposerImages);
            Grid.SetColumn(imgBtn, 2);
            form.Children.Add(imgBtn);

            ideaBtn = new Btn(28, 14, new Thickness(10, 0, 10, 0)) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
            ideaBtn.AddIcon("lightbulb", 12, 2.1);
            ideaBtn.AddText("Idea", 11.5, FontWeights.Medium, false, 4);
            ideaBtn.Click = () => { ideaMode = !ideaMode; RefreshComposer(); };
            Grid.SetColumn(ideaBtn, 3);
            form.Children.Add(ideaBtn);

            modeIndicator = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            Grid.SetColumn(modeIndicator, 4);
            form.Children.Add(modeIndicator);

            completedBtn = new Btn(32, 16, new Thickness(12, 0, 12, 0)) { HoverScale = 1.03, PressScale = 0.94 };
            completedBtn.AddIcon("circle-check", 14, 2.1);
            completedBtn.AddText("Completed", 12, FontWeights.SemiBold, false, 6);
            completedCount = completedBtn.AddText("0", 11.5, FontWeights.SemiBold, true, 6);
            completedCount.Opacity = 0.8;
            completedBtn.AddIcon("chevron-down", 14, 2);
            chevron = null;
            completedBtn.Click = ToggleCompleted;
            Grid.SetColumn(completedBtn, 1);
            row.Children.Add(completedBtn);

            return bar;
        }

        TextBlock composerPlaceholder;

        void RefreshComposer()
        {
            string text = composer.Text;
            int len = text.Trim().Length;
            bool typing = len > 0 || composerStaged.Count > 0;
            bool prompt = Logic.IsPromptMode(text);

            if (typing)
            {
                plusBtn.NoDash();
                plusBtn.Tactile(true);
                if (prompt) plusBtn.Paint(Theme.Accent, Theme.Accent, Brushes.White, Brushes.White);
                else if (ideaMode) plusBtn.Paint(Pal.B(Pal.S600), Pal.B(Pal.S600), Brushes.White, Brushes.White);
                else plusBtn.Paint(Pal.B(Pal.S900), Pal.B(Pal.S900), Brushes.White, Brushes.White);
            }
            else
            {
                plusBtn.Tactile(false);
                plusBtn.Paint(Brushes.Transparent, Brushes.Transparent, Pal.B(Pal.S400), Pal.B(Pal.S700));
                plusBtn.Dashed(Pal.B(Pal.S300), Pal.B(Pal.S500));
            }
            plusBtn.Disabled = composerUploading > 0;
            plusBtn.Refresh();

            if (composerStaged.Count > 0) imgBtn.Paint(Pal.B(Pal.S200), Pal.B(Pal.S200), Pal.B(Pal.S800), Pal.B(Pal.S800));
            else imgBtn.Paint(Brushes.Transparent, Pal.B(Pal.S100), Pal.B(Pal.S400), Pal.B(Pal.S700));

            if (ideaMode) ideaBtn.Paint(Pal.B(Pal.S200, 0.9), Pal.B(Pal.S200, 0.9), Pal.B(Pal.S800), Pal.B(Pal.S800), Pal.B(Pal.S300), Pal.B(Pal.S300));
            else ideaBtn.Paint(Brushes.Transparent, Pal.B(Pal.S100), Pal.B(Pal.S400), Pal.B(Pal.S700));
            ideaBtn.ToolTip = ideaMode ? "Adding as backlog idea (sits at bottom of list)" : "Click to add as backlog idea";
            composerPlaceholder.Text = ideaMode ? "Add a backlog idea (or paste an image)..." : "Add a task, prompt, or paste an image...";

            // Mode indicator: FileText (<100 chars) vs Terminal (prompt mode, Gemini title)
            string mode = len == 0 ? "" : prompt ? "prompt" : "normal";
            if (mode != modeShown)
            {
                string prev = modeShown;
                modeShown = mode;
                if (mode == "")
                {
                    Anim.ScaleTo(modeIndicator, 0.6, 160, Anim.Out);
                    Anim.Fade(modeIndicator, 0, 160, () => { if (modeShown == "") modeIndicator.Visibility = Visibility.Collapsed; });
                }
                else
                {
                    modeIndicator.Background = prompt ? (Brush)Theme.TintHover : Pal.B(Pal.S100);
                    modeIcon = Icons.Make(prompt ? "terminal" : "file-text", 14, prompt ? (Brush)Theme.Accent : Pal.B(Pal.S400), prompt ? 2.3 : 2.1);
                    modeIcon.HorizontalAlignment = HorizontalAlignment.Center;
                    var rt = new RotateTransform(prompt ? -65 : 65);
                    var st = new ScaleTransform(0.35, 0.35);
                    var tg = new TransformGroup();
                    tg.Children.Add(st);
                    tg.Children.Add(rt);
                    modeIcon.RenderTransform = tg;
                    modeIcon.RenderTransformOrigin = new Point(0.5, 0.5);
                    modeIndicator.Child = modeIcon;
                    var spring = SpringEase.Make(620, 22, 1, 0.35);
                    rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)) { EasingFunction = spring });
                    var sa = new DoubleAnimation(1, TimeSpan.FromMilliseconds(350)) { EasingFunction = spring };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
                    modeIndicator.ToolTip = prompt
                        ? "Prompt mode (" + len + " chars) — Gemini extracts a title & saves the prompt as the first comment"
                        : "Normal entry (" + len + "/" + Logic.PromptModeMinChars + " chars) — added directly";
                    modeIndicator.Visibility = Visibility.Visible;
                    if (prev == "")
                    {
                        modeIndicator.Opacity = 0;
                        Anim.Fade(modeIndicator, 1, 200);
                        ScaleTransform s0; TranslateTransform t0;
                        Anim.Transforms(modeIndicator, out s0, out t0);
                        s0.ScaleX = s0.ScaleY = 0.6;
                        Anim.ScaleTo(modeIndicator, 1, 240, Anim.Out);
                    }
                    else
                    {
                        // Pop when crossing into prompt mode
                        Anim.ScaleTo(modeIndicator, prompt ? 1.16 : 1, 120, Anim.Out);
                        if (prompt) Dispatcher.BeginInvoke(new Action(() => Anim.ScaleTo(modeIndicator, 1, 200, Anim.Out)), DispatcherPriority.Background);
                    }
                }
            }

            // Staged composer images
            stagedPanel.Children.Clear();
            for (int k = 0; k < composerStaged.Count; k++)
            {
                int idx = k;
                stagedPanel.Children.Add(StagedThumb(composerStaged[k], 64, 12, () => { composerStaged.RemoveAt(idx); RefreshComposer(); }));
            }
            for (int k = 0; k < composerUploading; k++) stagedPanel.Children.Add(UploadingThumb(64, 12));
            stagedPanel.Visibility = composerStaged.Count > 0 || composerUploading > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void RefreshCompletedButton(int count)
        {
            completedCount.Text = count.ToString();
            if (showCompleted) completedBtn.Paint(Pal.B(Pal.S900), Pal.B(Pal.S900), Brushes.White, Brushes.White);
            else completedBtn.Paint(Pal.B(Pal.S100), Pal.B(Pal.S200, 0.8), Pal.B(Pal.S600), Pal.B(Pal.S900));
            completedBtn.ToolTip = "Completed problems (" + count + ")";
        }

        void SubmitComposer()
        {
            string text = composer.Text.Trim();
            if ((text.Length == 0 && composerStaged.Count == 0) || composerUploading > 0) return;
            AddIssue(text, ideaMode ? Status.Backlog : Status.Task, composerStaged.Count > 0 ? new List<string>(composerStaged) : null);
            composer.Clear();
            composerStaged.Clear();
            wasPromptMode = false;
            RefreshComposer();
            composer.Focus();
        }

        void StageComposerImages(List<BitmapSource> bmps)
        {
            if (bmps.Count == 0) return;
            Sound.Play("add");
            composerUploading += bmps.Count;
            RefreshComposer();
            foreach (var b in bmps)
            {
                PrepareImage(b, url =>
                {
                    composerUploading--;
                    if (url != null) composerStaged.Add(url);
                    RefreshComposer();
                });
            }
        }

        // ================================================================== completed drawer

        UIElement BuildCompletedPanel()
        {
            var box = new Border
            {
                Margin = new Thickness(0, 8, 0, 8), CornerRadius = new CornerRadius(16), Background = Pal.B(Pal.S50, 0.9),
                BorderBrush = Pal.B(Pal.S200, 0.8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12),
            };
            var stack = new StackPanel();
            box.Child = stack;
            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var headerBorder = new Border { BorderBrush = Pal.B(Pal.S200, 0.7), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8), Child = header };
            completedHeader = T.Text("Completed problems (0)", 12, FontWeights.SemiBold, Pal.B(Pal.S500));
            header.Children.Add(completedHeader);
            var hint = T.Text("Click Restore on any item to return it to the main list", 11.5, FontWeights.Normal, Pal.B(Pal.S400));
            hint.HorizontalAlignment = HorizontalAlignment.Right;
            hint.TextTrimming = TextTrimming.CharacterEllipsis;
            hint.Margin = new Thickness(180, 0, 0, 0);
            header.Children.Add(hint);
            stack.Children.Add(headerBorder);
            completedEmpty = T.Text("No completed problems yet.", 13.5, FontWeights.Normal, Pal.B(Pal.S400));
            completedEmpty.HorizontalAlignment = HorizontalAlignment.Center;
            completedEmpty.Margin = new Thickness(0, 16, 0, 16);
            stack.Children.Add(completedEmpty);
            stack.Children.Add(completedList);
            return box;
        }

        TextBlock completedHeader, completedEmpty;

        void ToggleCompleted()
        {
            showCompleted = !showCompleted;
            Sound.PlayExpand(showCompleted);
            Render(false);
            Anim.Reveal(completedDrawer, showCompleted, () => { if (!showCompleted) { completedList.Children.Clear(); completedRows.Clear(); } });
        }

        // ================================================================== render

        public void Render(bool animateTheme)
        {
            var proj = State.ActiveProject;
            if (Theme.Current == null || Theme.Current.Key != proj.ColorKey) Theme.Apply(Themes.Get(proj.ColorKey), animateTheme);
            AnimateGradient(Themes.Get(proj.ColorKey).TintBgSubtle, animateTheme);
            RenderCarousel();

            var projectIssues = State.Issues.Where(i => i.ProjectId == proj.Id).ToList();
            var main = Logic.VisibleMain(projectIssues);
            var completed = projectIssues.Where(i => i.Completed).ToList();

            RefreshCompletedButton(completed.Count);
            completedHeader.Text = "Completed problems (" + completed.Count + ")";
            completedEmpty.Visibility = completed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (showCompleted) SyncList(completedList, completedRows, completed, true);

            SyncList(mainList, mainRows, main, true);
            emptyText.Visibility = main.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            emptyText.Inlines.Clear();
            if (sync.Problem != null && !State.HasEverSynced)
                emptyText.Inlines.Add(new System.Windows.Documents.Run("Couldn't load your items from the server yet. They'll appear here once sync works."));
            else
            {
            emptyText.Inlines.Add(new System.Windows.Documents.Run("No open items in "));
            emptyText.Inlines.Add(new System.Windows.Documents.Run(proj.Name) { FontWeight = FontWeights.Medium, Foreground = Pal.B(Pal.S600) });
            emptyText.Inlines.Add(new System.Windows.Documents.Run(" yet — add a task or paste an image above."));
            }
            RefreshComposer();
        }

        bool firstFrameShown, suppressEnter;

        void SyncList(StackPanel panel, Dictionary<string, IssueRowView> cache, List<Issue> issues, bool animateNew)
        {
            var ids = new HashSet<string>(issues.Select(i => i.Id));
            foreach (var k in cache.Keys.ToList())
            {
                if (!ids.Contains(k)) { panel.Children.Remove(cache[k]); cache.Remove(k); }
            }
            int enterDelay = 0;
            // Before the first frame only build what fits on screen; the rest follows right after.
            int limit = firstFrameShown ? issues.Count : Math.Min(issues.Count, 14);
            for (int idx = 0; idx < limit; idx++)
            {
                var issue = issues[idx];
                IssueRowView row;
                bool isNew = false;
                if (!cache.TryGetValue(issue.Id, out row))
                {
                    row = new IssueRowView(this);
                    cache[issue.Id] = row;
                    isNew = true;
                }
                row.Update(issue, extracting.Contains(issue.Id), idx == issues.Count - 1, DraggingId == issue.Id);
                if (idx >= panel.Children.Count || panel.Children[idx] != row)
                {
                    if (row.Parent == panel) panel.Children.Remove(row);
                    panel.Children.Insert(idx, row);
                }
                if (isNew && animateNew && firstFrameShown && !suppressEnter && enterDelay < 30)
                {
                    enterDelay++;
                    Anim.Enter(row, row.TargetOpacity);
                }
            }
        }

        public void RefreshRow(IssueRowView row) { row.ForceRebuild(); }

        void AnimateGradient(Color c, bool animate)
        {
            if (!animate) { gradTop.BeginAnimation(GradientStop.ColorProperty, null); gradTop.Color = c; return; }
            gradTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(220)));
        }

        // ================================================================== mutations (mirror App.tsx handlers)

        void Changed(string kind, string id)
        {
            if (kind != null) State.Enqueue(kind, id);
            State.Touch();
            sync.Kick();
        }

        public void SelectProject(string id)
        {
            if (id == State.ActiveProjectId) return;
            Sound.PlayExpand(true);
            State.ActiveProjectId = id;
            State.SaveSoon();
            Render(true);
            scroller.ScrollToTop();
        }

        void StepProject(int dir)
        {
            if (State.Projects.Count <= 1) return;
            int idx = State.Projects.FindIndex(p => p.Id == State.ActiveProject.Id);
            var target = State.Projects[(idx + dir + State.Projects.Count) % State.Projects.Count];
            Sound.PlayExpand(dir > 0);
            State.ActiveProjectId = target.Id;
            State.SaveSoon();
            Render(true);
            scroller.ScrollToTop();
        }

        public void CreateProject(string name)
        {
            var p = Logic.CreateProject(name, State.Projects);
            if (p == null) return;
            Sound.Play("add");
            State.Projects.Add(p);
            State.ActiveProjectId = p.Id;
            Changed("project", p.Id);
            Render(true);
        }

        public void RenameProject(string id, string name)
        {
            var p = State.Projects.FirstOrDefault(x => x.Id == id);
            if (p == null || name.Trim().Length == 0) return;
            p.Name = name.Trim();
            Changed("project", id);
            Render(false);
        }

        public void CycleProjectColor(string id)
        {
            var p = State.Projects.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            Sound.Play("in_work");
            p.ColorKey = Logic.NextCycledColor(p.ColorKey);
            Changed("project", id);
            Render(true);
        }

        public void DeleteProject(string id)
        {
            if (State.Projects.Count <= 1) return;
            var p = State.Projects.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            int n = State.Issues.Count(i => i.ProjectId == id);
            if (n > 0 && !opts.SelfTest)
            {
                var r = MessageBox.Show(this, "Delete \"" + p.Name + "\" and its " + n + " item" + (n == 1 ? "" : "s") + "?\nThis also deletes them from the synced database.",
                    "Delete project", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
                if (r != MessageBoxResult.OK) return;
            }
            Sound.PlayExpand(false);
            State.Projects.Remove(p);
            foreach (var i in State.Issues.Where(i => i.ProjectId == id).ToList())
            {
                State.Issues.Remove(i);
                State.Enqueue("issue-del", i.Id);
            }
            if (State.ActiveProjectId == id) State.ActiveProjectId = State.Projects[0].Id;
            Changed("project-del", id);
            Render(true);
        }

        public void AddIssue(string raw, string status, List<string> images)
        {
            var issue = Logic.CreateIssue(raw, State.ActiveProject.Id, status, images);
            if (issue == null) return;
            Sound.Play("add");
            State.Issues.Insert(0, issue);
            Changed("issue", issue.Id);
            if (Logic.IsPromptMode(raw) && !string.IsNullOrWhiteSpace(cfg.GeminiKey) && !opts.Offline)
            {
                extracting.Add(issue.Id);
                string id = issue.Id;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string title = null;
                    try { title = Gemini.ExtractTitle(cfg, raw); } catch (Exception ex) { Log.Write("Gemini failed: " + ex.Message); }
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        extracting.Remove(id);
                        var cur = State.Find(id);
                        if (cur != null && !string.IsNullOrWhiteSpace(title))
                        {
                            cur.Title = title.Trim();
                            Changed("issue", id);
                        }
                        Render(false);
                    }));
                });
            }
            Render(false);
        }

        public void CycleStatus(string id)
        {
            var i = State.Find(id);
            if (i == null) return;
            string next = Logic.NextStatus(Logic.GetStatus(i));
            Sound.PlayStatus(next);
            Logic.SetStatus(State.Issues, id, next);
            Changed("issue", id);
            Render(false);
        }

        public void SetStatus(string id, string status)
        {
            Sound.PlayStatus(status);
            Logic.SetStatus(State.Issues, id, status);
            Changed("issue", id);
            Render(false);
        }

        public void ToggleExpand(string id)
        {
            var i = State.Find(id);
            if (i == null) return;
            i.Expanded = !i.Expanded;
            Sound.PlayExpand(i.Expanded);
            Changed("issue", id);
            Render(false);
        }

        public void UpdateTitle(string id, string title)
        {
            if (title.Trim().Length == 0) { DeleteIssue(id); return; }
            var i = State.Find(id);
            if (i == null) return;
            i.Title = title.Trim();
            Changed("issue", id);
            Render(false);
        }

        public void DeleteIssue(string id)
        {
            var i = State.Find(id);
            if (i == null) return;
            Sound.PlayExpand(false);
            State.Issues.Remove(i);
            Changed("issue-del", id);
            Render(false);
        }

        public void AddComment(string id, string text, List<string> images)
        {
            var i = State.Find(id);
            if (i == null) return;
            Sound.Play("add");
            if (!Logic.AddComment(i, text, images)) return;
            Changed("issue", id);
            Render(false);
        }

        public void UpdateComment(string id, string commentId, string text)
        {
            var i = State.Find(id);
            if (i == null) return;
            Logic.UpdateComment(i, commentId, text);
            Changed("issue", id);
            Render(false);
        }

        public void DeleteComment(string id, string commentId)
        {
            var i = State.Find(id);
            if (i == null) return;
            Sound.PlayExpand(false);
            i.Comments.RemoveAll(c => c.Id == commentId);
            Changed("issue", id);
            Render(false);
        }

        public void DeleteCommentImage(string id, string commentId, int index)
        {
            var i = State.Find(id);
            if (i == null) return;
            Sound.PlayExpand(false);
            Logic.DeleteCommentImage(i, commentId, index);
            Changed("issue", id);
            Render(false);
        }

        // ================================================================== drag reorder

        public void BeginDrag(IssueRowView row)
        {
            DraggingId = row.Id;
            Sound.Play("drag_start");
            Mouse.Capture(mainList, CaptureMode.SubTree);
            Render(false);
        }

        void OnDragMove(object sender, MouseEventArgs e)
        {
            if (DraggingId == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            foreach (UIElement child in mainList.Children)
            {
                var row = child as IssueRowView;
                if (row == null || row.Id == DraggingId) continue;
                var pos = e.GetPosition(row);
                if (pos.Y >= 0 && pos.Y < row.ActualHeight)
                {
                    Logic.Reorder(State.Issues, DraggingId, row.Id);
                    State.Touch();
                    Render(false);
                    break;
                }
            }
            // Auto-scroll near the window edges while dragging
            var p = e.GetPosition(scroller);
            if (p.Y < 40) scroller.ScrollToVerticalOffset(scroller.VerticalOffset - 12);
            else if (p.Y > scroller.ActualHeight - 40) scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 12);
        }

        void EndDrag()
        {
            if (DraggingId == null) return;
            string id = DraggingId;
            DraggingId = null;
            if (Mouse.Captured == mainList) mainList.ReleaseMouseCapture();
            Sound.Play("drop");
            Changed("issue", id);
            Render(false);
        }

        // ================================================================== images

        public void HookImagePaste(TextBox tb, Action<List<BitmapSource>> stage)
        {
            CommandManager.AddPreviewExecutedHandler(tb, (s, e) =>
            {
                if (e.Command != ApplicationCommands.Paste) return;
                if (!Images.ClipboardHasImage()) return;
                e.Handled = true;
                stage(Images.FromClipboard());
            });
        }

        public void PickImages(Action<List<BitmapSource>> stage)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff|All files|*.*",
                Title = "Attach images",
            };
            if (dlg.ShowDialog(this) != true) return;
            stage(dlg.FileNames.Select(Images.LoadFile).Where(b => b != null).ToList());
        }

        public void PrepareImage(BitmapSource bmp, Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string url = null;
                try { url = Images.Prepare(bmp); } catch (Exception ex) { Log.Write("Image prepare failed: " + ex.Message); }
                Dispatcher.BeginInvoke(new Action(() => done(url)));
            });
        }

        void OnDropFiles(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (e.Data.GetData(DataFormats.FileDrop) as string[] ?? new string[0]).Where(Images.IsImageFile).ToList();
            var bmps = files.Select(Images.LoadFile).Where(b => b != null).ToList();
            StageComposerImages(bmps);
        }

        /// Thumbnail for an already-attached image (max 320x210, rounded, click = lightbox, hover x = remove).
        public UIElement AttachmentThumb(string src, Action open, Action remove)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            var frame = new Border
            {
                Width = 120, Height = 80, CornerRadius = new CornerRadius(12), BorderBrush = Pal.B(Pal.S200, 0.9),
                BorderThickness = new Thickness(1), Background = Pal.B(Pal.S100),
            };
            g.Children.Add(frame);
            var x = new Btn(20, 10, new Thickness(0)) { Width = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 6, 0), Opacity = 0, ToolTip = "Remove image" };
            x.Paint(Pal.B(Pal.S900, 0.75), Pal.B(Pal.S900), Brushes.White, Brushes.White);
            x.AddIcon("x", 12, 2.2);
            x.Click = remove;
            g.Children.Add(x);
            g.MouseEnter += (s, e) => { x.Opacity = 1; Anim.ScaleTo(frame, 1.015, 150, Anim.Out); };
            g.MouseLeave += (s, e) => { x.Opacity = 0; Anim.ScaleTo(frame, 1, 150, Anim.Out); };
            frame.MouseLeftButtonUp += (s, e) => open();
            int px = (int)(320 * DpiScale());
            Images.Load(src, px, Dispatcher, bmp =>
            {
                if (bmp == null)
                {
                    frame.Child = T.Text("image unavailable", 11, FontWeights.Normal, Pal.B(Pal.S400));
                    ((TextBlock)frame.Child).HorizontalAlignment = HorizontalAlignment.Center;
                    return;
                }
                double w = bmp.PixelWidth, h = bmp.PixelHeight;
                double scale = Math.Min(1, Math.Min(320 / (w / DpiScaleFor(bmp)), 210 / (h / DpiScaleFor(bmp))));
                double dw = w / DpiScaleFor(bmp) * scale, dh = h / DpiScaleFor(bmp) * scale;
                frame.Width = Math.Max(24, dw);
                frame.Height = Math.Max(24, dh);
                frame.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(frame, BitmapScalingMode.HighQuality);
            });
            return g;
        }

        double DpiScaleFor(BitmapSource bmp)
        {
            // Thumbnails were decoded at up to 320*dpi px; treat pixels as device pixels.
            return Math.Max(1, DpiScale());
        }

        double DpiScale()
        {
            var src = PresentationSource.FromVisual(this);
            return src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1;
        }

        public UIElement StagedThumb(string src, double size, double radius, Action remove)
        {
            var g = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0) };
            var frame = new Border { CornerRadius = new CornerRadius(radius), BorderBrush = Pal.B(Pal.S200), BorderThickness = new Thickness(1), Background = Pal.B(Pal.S50) };
            g.Children.Add(frame);
            var x = new Btn(16, 8, new Thickness(0)) { Width = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0), HoverScale = 1.1, ToolTip = "Remove" };
            x.Paint(Pal.B(Pal.S900, 0.8), Pal.B(Pal.S900), Brushes.White, Brushes.White);
            x.AddIcon("x", 10, 2.4);
            x.Click = remove;
            g.Children.Add(x);
            Images.Load(src, (int)(size * 2.5), Dispatcher, bmp =>
            {
                if (bmp != null) frame.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            });
            return g;
        }

        public UIElement UploadingThumb(double size, double radius)
        {
            var g = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0) };
            var r = new Rectangle { RadiusX = radius, RadiusY = radius, Stroke = Pal.B(Pal.S300), StrokeDashArray = new DoubleCollection { 3, 2 }, Fill = Pal.B(Pal.S50) };
            g.Children.Add(r);
            var ic = Icons.Make("loader", 16, Pal.B(Pal.S400), 2);
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            Anim.Spin(ic);
            g.Children.Add(ic);
            return g;
        }

        // ================================================================== lightbox

        void BuildLightbox()
        {
            lightbox.Background = Pal.B(Pal.S950, 0.8);
            lightbox.Visibility = Visibility.Collapsed;
            lightbox.MouseLeftButtonUp += (s, e) => CloseLightbox();
            lightboxFrame.CornerRadius = new CornerRadius(16);
            lightboxFrame.Background = Brushes.White;
            lightboxFrame.HorizontalAlignment = HorizontalAlignment.Center;
            lightboxFrame.VerticalAlignment = VerticalAlignment.Center;
            lightboxFrame.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.35, Direction = 270 };
            lightboxFrame.MouseLeftButtonUp += (s, e) => e.Handled = true;
            RenderOptions.SetBitmapScalingMode(lightboxFrame, BitmapScalingMode.HighQuality);
            lightbox.Children.Add(lightboxFrame);
            var close = new Btn(36, 18, new Thickness(0)) { Width = 36, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 20, 20, 0), ToolTip = "Close (Esc)" };
            close.Paint(Pal.B(Pal.White, 0.15), Pal.B(Pal.White, 0.25), Brushes.White, Brushes.White);
            close.AddIcon("x", 20, 2);
            close.Click = CloseLightbox;
            lightbox.Children.Add(close);
            root.Children.Add(lightbox);
            SizeChanged += (s, e) => LayoutLightbox();
        }

        public void PreviewImage(string src)
        {
            lightboxBmp = null;
            lightboxFrame.Background = Pal.B(Pal.S100);
            lightboxFrame.Width = 200;
            lightboxFrame.Height = 140;
            lightbox.Visibility = Visibility.Visible;
            lightbox.Opacity = 0;
            Anim.Fade(lightbox, 1, 150);
            Anim.Transforms(lightboxFrame, out lbScale, out lbTranslate);
            lbScale.ScaleX = lbScale.ScaleY = 0.94;
            Anim.ScaleTo(lightboxFrame, 1, 380, SpringEase.Make(480, 32, 1, 0.38));
            Images.Load(src, 0, Dispatcher, bmp =>
            {
                if (bmp == null || lightbox.Visibility != Visibility.Visible) return;
                lightboxBmp = bmp;
                lightboxFrame.Background = new ImageBrush(bmp) { Stretch = Stretch.Uniform };
                LayoutLightbox();
            });
        }

        ScaleTransform lbScale;
        TranslateTransform lbTranslate;

        void LayoutLightbox()
        {
            if (lightboxBmp == null) return;
            double scale = DpiScale();
            double w = lightboxBmp.PixelWidth / scale, h = lightboxBmp.PixelHeight / scale;
            double maxW = ActualWidth * 0.9, maxH = ActualHeight * 0.86;
            double k = Math.Min(1, Math.Min(maxW / w, maxH / h));
            lightboxFrame.Width = Math.Max(40, w * k);
            lightboxFrame.Height = Math.Max(40, h * k);
        }

        void CloseLightbox()
        {
            if (lightbox.Visibility != Visibility.Visible) return;
            Anim.Fade(lightbox, 0, 140, () => { lightbox.Visibility = Visibility.Collapsed; lightboxFrame.Background = null; lightboxBmp = null; });
        }

        // ================================================================== sync status
        // Three pieces, all bottom-right / bottom-center overlays:
        //  * syncCard  - shown while sync keeps failing: why, last sync, pending count, Retry, close.
        //  * syncChip  - compact pill: "Syncing…" during slow syncs, or "Not synced" after the card is closed.
        //  * noticeToast - dark snackbar for one-off notices ("Back online · synced").

        static readonly string AmberBg = "#FFFBEB", AmberBorder = "#FDE68A", AmberIcon = "#B45309",
            RoseBg = "#FFF1F2", RoseBorder = "#FECDD3", RoseIcon = "#BE123C";

        readonly Border syncCard = new Border();
        readonly Border syncCardIcon = new Border();
        readonly TextBlock syncCardTitle = new TextBlock(), syncCardDetail = new TextBlock(), syncCardMeta = new TextBlock();
        Btn syncRetry;
        readonly Border noticeToast = new Border();
        readonly TextBlock noticeText = new TextBlock();
        DispatcherTimer noticeTimer, metaTimer;
        ProblemKind? dismissedKind;

        void BuildSyncChip()
        {
            syncChip.HorizontalAlignment = HorizontalAlignment.Right;
            syncChip.VerticalAlignment = VerticalAlignment.Bottom;
            syncChip.Margin = new Thickness(0, 0, 20, 18);
            syncChip.Padding = new Thickness(10, 5, 12, 5);
            syncChip.CornerRadius = new CornerRadius(10);
            syncChip.Background = Brushes.White;
            syncChip.BorderBrush = Pal.B(Pal.S200);
            syncChip.BorderThickness = new Thickness(1);
            syncChip.Cursor = Cursors.Hand;
            syncChip.ToolTip = "Click (or press F5) to sync now";
            syncChip.Opacity = 0;
            syncChip.IsHitTestVisible = false;
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(syncIconHost);
            syncText.FontFamily = Fonts.Sans;
            syncText.FontSize = 12;
            syncText.FontWeight = FontWeights.Medium;
            syncText.Foreground = Pal.B(Pal.S600);
            syncText.Margin = new Thickness(6, 0, 0, 0);
            syncText.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(syncText);
            syncChip.Child = sp;
            syncChip.MouseLeftButtonUp += (s, e) =>
            {
                // Reopen the card if it was closed, and retry.
                dismissedKind = null;
                RefreshSyncUi();
                sync.RequestPull();
            };
            root.Children.Add(syncChip);

            BuildSyncCard();
            BuildNoticeToast();
            sync.ProblemChanged += p => { if (p == null) dismissedKind = null; RefreshSyncUi(); };
            sync.Notice += ShowNotice;
            metaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            metaTimer.Tick += (s, e) => UpdateSyncMeta();
        }

        void BuildSyncCard()
        {
            syncCard.HorizontalAlignment = HorizontalAlignment.Right;
            syncCard.VerticalAlignment = VerticalAlignment.Bottom;
            syncCard.Margin = new Thickness(0, 0, 20, 18);
            syncCard.Width = 340;
            syncCard.CornerRadius = new CornerRadius(14);
            syncCard.BorderThickness = new Thickness(1);
            syncCard.Padding = new Thickness(14, 12, 6, 12);
            syncCard.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.12, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
            syncCard.Visibility = Visibility.Collapsed;
            syncCard.Opacity = 0;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            syncCardIcon.VerticalAlignment = VerticalAlignment.Top;
            syncCardIcon.Margin = new Thickness(0, 1, 10, 0);
            g.Children.Add(syncCardIcon);

            var col = new StackPanel();
            Grid.SetColumn(col, 1);
            syncCardTitle.FontFamily = Fonts.Sans;
            syncCardTitle.FontSize = 13.5;
            syncCardTitle.FontWeight = FontWeights.SemiBold;
            syncCardTitle.Foreground = Pal.B(Pal.S900);
            syncCardTitle.TextWrapping = TextWrapping.Wrap;
            col.Children.Add(syncCardTitle);
            syncCardDetail.FontFamily = Fonts.Sans;
            syncCardDetail.FontSize = 12.5;
            syncCardDetail.Foreground = Pal.B(Pal.S600);
            syncCardDetail.TextWrapping = TextWrapping.Wrap;
            syncCardDetail.LineHeight = 17;
            syncCardDetail.Margin = new Thickness(0, 3, 0, 0);
            col.Children.Add(syncCardDetail);
            syncCardMeta.FontFamily = Fonts.Mono;
            syncCardMeta.FontSize = 10.5;
            syncCardMeta.FontWeight = FontWeights.SemiBold;
            syncCardMeta.Foreground = Pal.B(Pal.S500);
            syncCardMeta.TextWrapping = TextWrapping.Wrap;
            syncCardMeta.Margin = new Thickness(0, 5, 0, 0);
            col.Children.Add(syncCardMeta);

            syncRetry = new Btn(28, 14, new Thickness(12, 0, 12, 0)) { HoverScale = 1.03, PressScale = 0.94 };
            syncRetry.HorizontalAlignment = HorizontalAlignment.Left;
            syncRetry.Margin = new Thickness(0, 9, 0, 0);
            syncRetry.Tactile(true);
            syncRetry.Click = () => { sync.RequestPull(); SetRetrying(true); };
            col.Children.Add(syncRetry);
            g.Children.Add(col);

            var close = new Btn(28, 14, new Thickness(7, 0, 7, 0)) { HoverScale = 1.08, PressScale = 0.9 };
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new Thickness(4, -4, 0, 0);
            close.Paint(Brushes.Transparent, Pal.B(Pal.S900, 0.06), Pal.B(Pal.S400), Pal.B(Pal.S700));
            close.AddIcon("x", 14);
            close.ToolTip = "Hide (the \"Not synced\" chip stays)";
            close.Click = () =>
            {
                if (sync.Problem != null) dismissedKind = sync.Problem.Kind;
                RefreshSyncUi();
            };
            Grid.SetColumn(close, 2);
            g.Children.Add(close);

            syncCard.Child = g;
            root.Children.Add(syncCard);
        }

        void BuildNoticeToast()
        {
            noticeToast.HorizontalAlignment = HorizontalAlignment.Center;
            noticeToast.VerticalAlignment = VerticalAlignment.Bottom;
            noticeToast.Margin = new Thickness(20, 0, 20, 18);
            noticeToast.Padding = new Thickness(14, 8, 14, 8);
            noticeToast.CornerRadius = new CornerRadius(12);
            noticeToast.Background = Pal.B(Pal.S900);
            noticeToast.IsHitTestVisible = false;
            noticeToast.Opacity = 0;
            noticeToast.Visibility = Visibility.Collapsed;
            noticeText.FontFamily = Fonts.Sans;
            noticeText.FontSize = 12.5;
            noticeText.FontWeight = FontWeights.Medium;
            noticeText.Foreground = Brushes.White;
            noticeText.TextWrapping = TextWrapping.Wrap;
            noticeToast.Child = noticeText;
            root.Children.Add(noticeToast);
        }

        void ShowNotice(string text)
        {
            Log.Write("Notice: " + text);
            noticeText.Text = text;
            noticeToast.Visibility = Visibility.Visible;
            Anim.Fade(noticeToast, 1, 160);
            if (noticeTimer == null)
            {
                noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
                noticeTimer.Tick += (s, e) =>
                {
                    noticeTimer.Stop();
                    Anim.Fade(noticeToast, 0, 260, () => noticeToast.Visibility = Visibility.Collapsed);
                };
            }
            noticeTimer.Stop();
            noticeTimer.Start();
        }

        static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return null;
            var s = (DateTime.UtcNow - utc).TotalSeconds;
            if (s < 60) return "just now";
            if (s < 3600) return (int)(s / 60) + " min ago";
            if (s < 86400) return (int)(s / 3600) + " h ago";
            return (int)(s / 86400) + " d ago";
        }

        void UpdateSyncMeta()
        {
            var ago = Ago(State.LastSyncUtc);
            string meta = ago == null ? "Not synced yet on this PC" : "Last synced " + ago;
            int pending = State.Outbox.Count;
            if (pending > 0) meta += " · " + pending + " change" + (pending == 1 ? "" : "s") + " waiting";
            syncCardMeta.Text = meta;
        }

        void SetRetrying(bool on)
        {
            syncRetry.Clear();
            var p = sync.Problem;
            string fg = p != null && p.Serious ? RoseIcon : Pal.S700;
            syncRetry.Paint(Brushes.White, Pal.B(Pal.S50), Pal.B(Pal.S700), Pal.B(Pal.S900), Pal.B(p != null && p.Serious ? RoseBorder : AmberBorder));
            if (on)
            {
                var ic = Icons.Make("loader", 13, Pal.B(fg), 2);
                Anim.Spin(ic);
                syncRetry.AddElement(ic);
                syncRetry.AddText("Retrying…", 12, FontWeights.SemiBold);
                syncRetry.Disabled = false;
            }
            else
            {
                syncRetry.AddIcon("rotate-ccw", 13);
                syncRetry.AddText("Retry now", 12, FontWeights.SemiBold);
            }
        }

        /// Shows the card, the chip, or nothing, from the current problem / dismissal / sync state.
        void RefreshSyncUi()
        {
            var p = sync.Problem;
            bool cardOn = p != null && dismissedKind != p.Kind;
            if (p != null)
            {
                syncCard.Background = Pal.B(p.Serious ? RoseBg : AmberBg);
                syncCard.BorderBrush = Pal.B(p.Serious ? RoseBorder : AmberBorder);
                syncCardIcon.Child = Icons.Make("cloud-off", 18, Pal.B(p.Serious ? RoseIcon : AmberIcon), 2);
                syncCardTitle.Text = p.Title;
                syncCardDetail.Text = p.Detail;
                UpdateSyncMeta();
                SetRetrying(lastSync == SyncState.Syncing);
            }
            if (cardOn)
            {
                syncCard.IsHitTestVisible = true;
                if (syncCard.Visibility != Visibility.Visible) { syncCard.Visibility = Visibility.Visible; Anim.Enter(syncCard, 1); }
                metaTimer.Start();
            }
            else if (syncCard.Visibility == Visibility.Visible)
            {
                syncCard.IsHitTestVisible = false;
                metaTimer.Stop();
                Anim.Fade(syncCard, 0, 180, () => { if (!syncCard.IsHitTestVisible) syncCard.Visibility = Visibility.Collapsed; });
            }

            // Chip: "Not synced" when the card is hidden; otherwise only for slow syncs (see OnSyncStatus).
            if (p != null && !cardOn)
            {
                int pending = State.Outbox.Count;
                ShowChip(true, "cloud-off", pending > 0 ? "Not synced · " + pending + " waiting" : "Not synced · click to retry", true);
            }
            else if (p != null || lastSync != SyncState.Syncing)
                ShowChip(false, null, null, false);
            // The "couldn't load yet" hint lives in the empty list text.
            if (emptyText.Visibility == Visibility.Visible) Render(false);
        }

        SyncState lastSync = SyncState.Idle;
        DispatcherTimer chipDelay;

        void OnSyncStatus(SyncState s, int pending)
        {
            lastSync = s;
            if (chipDelay == null)
            {
                chipDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                chipDelay.Tick += (o, e) =>
                {
                    chipDelay.Stop();
                    if (lastSync == SyncState.Syncing && sync.Problem == null) ShowChip(true, "loader", "Syncing…", false);
                };
            }
            switch (s)
            {
                case SyncState.Idle:
                    chipDelay.Stop();
                    if (sync.Problem == null) ShowChip(false, null, null, false);
                    break;
                case SyncState.Syncing:
                    // Only show if syncing takes noticeably long; keeps the UI calm.
                    if (sync.Problem == null) { if (!chipDelay.IsEnabled) chipDelay.Start(); }
                    else SetRetrying(true);
                    break;
                case SyncState.Offline:
                    chipDelay.Stop();
                    RefreshSyncUi();
                    break;
                case SyncState.Disabled:
                    ShowChip(opts.Offline && opts.SnapshotPath == null, "cloud-off", "Sync off", false);
                    break;
            }
        }

        void ShowChip(bool show, string icon, string text, bool warn)
        {
            if (show)
            {
                var fg = Pal.B(warn ? AmberIcon : Pal.S500);
                syncText.Text = text;
                syncText.Foreground = warn ? Pal.B(AmberIcon) : Pal.B(Pal.S600);
                syncChip.Background = Pal.B(warn ? AmberBg : Pal.White);
                syncChip.BorderBrush = Pal.B(warn ? AmberBorder : Pal.S200);
                var ic = Icons.Make(icon, 13, fg, 2);
                if (icon == "loader") Anim.Spin(ic);
                syncIconHost.Child = ic;
                syncChip.IsHitTestVisible = true;
                Anim.Fade(syncChip, 1, 160);
            }
            else
            {
                syncChip.IsHitTestVisible = false;
                Anim.Fade(syncChip, 0, 220);
            }
        }

        // ================================================================== keyboard

        void OnWindowKey(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            if (e.Key == Key.Escape && lightbox.Visibility == Visibility.Visible) { CloseLightbox(); e.Handled = true; return; }
            if (e.Key == Key.F5) { sync.RequestPull(); e.Handled = true; return; }
            if ((mods & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.Tab) { StepProject((mods & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; }
                else if (e.Key == Key.PageDown) { StepProject(1); e.Handled = true; }
                else if (e.Key == Key.PageUp) { StepProject(-1); e.Handled = true; }
                else if (e.Key == Key.N || e.Key == Key.L) { composer.Focus(); e.Handled = true; }
                else if (e.Key == Key.M)
                {
                    Sound.Enabled = !Sound.Enabled;
                    SaveBounds();
                    e.Handled = true;
                }
                else if (e.Key == Key.V && !(Keyboard.FocusedElement is TextBox))
                {
                    // Paste anywhere: route into the composer.
                    composer.Focus();
                    if (Images.ClipboardHasImage()) StageComposerImages(Images.FromClipboard());
                    else composer.Paste();
                    e.Handled = true;
                }
            }
        }

        public void FocusList()
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
        }

        // ================================================================== window chrome & bounds

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void ApplyCaptionColor()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero || Theme.Current == null) return;
                var c = Theme.Current.TintBgSubtle;
                int caption = c.R | (c.G << 8) | (c.B << 16);
                int text = 0x0F | (0x17 << 8) | (0x2A << 16);
                var b = Theme.Current.TintBorder;
                int border = b.R | (b.G << 8) | (b.B << 16);
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
                DwmSetWindowAttribute(hwnd, 36, ref text, 4);
                DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            }
            catch { }
        }

        string SettingsPath { get { return System.IO.Path.Combine(opts.DataDir, "settings.json"); } }

        void LoadBounds()
        {
            Width = opts.Width > 0 ? opts.Width : 960;
            Height = opts.Height > 0 ? opts.Height : 860;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (opts.Width > 0) return;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var d = Json.Obj(Json.Parse(File.ReadAllText(SettingsPath)));
                double? l = Json.Num(d, "left"), t = Json.Num(d, "top"), w = Json.Num(d, "width"), h = Json.Num(d, "height");
                if (Json.Get(d, "sound") is bool) Sound.Enabled = (bool)Json.Get(d, "sound");
                if (l.HasValue && t.HasValue && w.HasValue && h.HasValue)
                {
                    var rect = new Rect(l.Value, t.Value, w.Value, h.Value);
                    var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                    if (vs.IntersectsWith(rect) && w.Value >= MinWidth && h.Value >= MinHeight)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = l.Value; Top = t.Value; Width = w.Value; Height = h.Value;
                    }
                }
                if (Json.Bool(d, "maximized")) WindowState = WindowState.Maximized;
            }
            catch { }
        }

        void SaveBounds()
        {
            if (opts.SnapshotPath != null || opts.SelfTest) return;
            try
            {
                var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                var d = new Dictionary<string, object>();
                d["left"] = r.Left; d["top"] = r.Top; d["width"] = r.Width; d["height"] = r.Height;
                d["maximized"] = WindowState == WindowState.Maximized;
                d["sound"] = Sound.Enabled;
                File.WriteAllText(SettingsPath, Json.Write(d));
            }
            catch { }
        }

        // For the self-test harness
        public int OutboxCount { get { return State.Outbox.Count; } }
        public bool IsExtracting(string id) { return extracting.Contains(id); }
        public void ShowCompletedForTest() { if (!showCompleted) ToggleCompleted(); }
        public FrameworkElement RootElement { get { return root; } }
    }
}
