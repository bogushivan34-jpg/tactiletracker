// Shared UI toolkit: palette, fonts, Lucide icons, spring easing, animations, tactile buttons.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Tactile
{
    public static class Pal
    {
        public static Color Hex(string hex, double alpha = 1)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb((byte)Math.Round(alpha * 255),
                byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber));
        }

        static readonly Dictionary<string, SolidColorBrush> cache = new Dictionary<string, SolidColorBrush>();

        /// Frozen, cached brush.
        public static SolidColorBrush B(string hex, double alpha = 1)
        {
            string key = hex + "/" + alpha;
            SolidColorBrush b;
            if (!cache.TryGetValue(key, out b))
            {
                b = new SolidColorBrush(Hex(hex, alpha));
                b.Freeze();
                cache[key] = b;
            }
            return b;
        }

        public static SolidColorBrush B(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public const string S50 = "#F8FAFC", S100 = "#F1F5F9", S200 = "#E2E8F0", S300 = "#CBD5E1", S400 = "#94A3B8",
            S500 = "#64748B", S600 = "#475569", S700 = "#334155", S800 = "#1E293B", S900 = "#0F172A", S950 = "#020617",
            E50 = "#ECFDF5", E200 = "#A7F3D0", E600 = "#059669", E700 = "#047857", White = "#FFFFFF";
    }

    /// Live, mutable brushes for the active project's theme. Everything that is accent-tinted
    /// references these instances, so switching project just animates their colors.
    public static class Theme
    {
        public static readonly SolidColorBrush Accent = new SolidColorBrush(), AccentStrong = new SolidColorBrush(),
            AccentDark = new SolidColorBrush(), TintBg = new SolidColorBrush(), TintBgSubtle = new SolidColorBrush(),
            TintBorder = new SolidColorBrush(), TintDrawerBorder = new SolidColorBrush(), TintHover = new SolidColorBrush(),
            TintMuted = new SolidColorBrush(), Selection = new SolidColorBrush(), DrawerBg = new SolidColorBrush();
        public static ThemeTokens Current;
        public static event Action Changed;

        public static void Apply(ThemeTokens t, bool animate)
        {
            Current = t;
            Set(Accent, t.Accent, animate);
            Set(AccentStrong, t.AccentStrong, animate);
            Set(AccentDark, t.AccentDark, animate);
            Set(TintBg, t.TintBg, animate);
            Set(TintBgSubtle, t.TintBgSubtle, animate);
            Set(TintBorder, t.TintBorder, animate);
            Set(TintDrawerBorder, t.TintDrawerBorder, animate);
            Set(TintHover, t.TintHover, animate);
            Set(TintMuted, t.TintMuted, animate);
            Set(Selection, t.Selection, animate);
            Set(DrawerBg, Color.FromArgb(230, 255, 255, 255), false);
            var h = Changed;
            if (h != null) h();
        }

        static void Set(SolidColorBrush b, Color c, bool animate)
        {
            if (!animate) { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = c; return; }
            var a = new ColorAnimation(c, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            b.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }
    }

    public static class Fonts
    {
        public static FontFamily Sans, Mono;

        public static void Init(string baseDir)
        {
            // Bundled fonts (optional) live in .\fonts next to the exe; otherwise fall back to Windows fonts.
            string dir = System.IO.Path.Combine(baseDir, "fonts");
            bool hasJakarta = Directory.Exists(dir) && Directory.GetFiles(dir, "PlusJakartaSans*.ttf").Length > 0;
            bool hasMono = Directory.Exists(dir) && Directory.GetFiles(dir, "JetBrainsMono*.ttf").Length > 0;
            var uri = new Uri(dir.TrimEnd('\\') + "\\");
            Sans = hasJakarta ? new FontFamily(uri, "./#Plus Jakarta Sans, Segoe UI") : new FontFamily("Segoe UI");
            Mono = hasMono ? new FontFamily(uri, "./#JetBrains Mono, Cascadia Mono, Consolas") : new FontFamily("Cascadia Mono, Consolas");
        }
    }

    public static class Icons
    {
        // Lucide icon paths (24x24 viewBox, stroke-width ~2, round caps/joins).
        static readonly Dictionary<string, string> paths = new Dictionary<string, string>
        {
            { "plus", "M5 12h14 M12 5v14" },
            { "x", "M18 6 6 18 M6 6l12 12" },
            { "check", "M20 6 9 17l-5-5" },
            { "chevron-down", "M6 9l6 6 6-6" },
            { "chevron-left", "M15 18l-6-6 6-6" },
            { "chevron-right", "M9 18l6-6-6-6" },
            { "message-square", "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" },
            { "image", "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M7 9a2 2 0 1 0 4 0a2 2 0 1 0-4 0z M21 15l-3.086-3.086a2 2 0 0 0-2.828 0L6 21" },
            { "lightbulb", "M15 14c0.2-1 0.7-1.7 1.5-2.5 1-0.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 0.2 2.2 1.5 3.5 0.7 0.7 1.3 1.5 1.5 2.5 M9 18h6 M10 22h4" },
            { "circle-check", "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0z M9 12l2 2 4-4" },
            { "terminal", "M4 17l6-6-6-6 M12 19h8" },
            { "file-text", "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7z M14 2v4a2 2 0 0 0 2 2h4 M10 9H8 M16 13H8 M16 17H8" },
            { "loader", "M21 12a9 9 0 1 1-6.219-8.56" },
            { "corner-down-left", "M9 10l-5 5 5 5 M20 4v7a4 4 0 0 1-4 4H4" },
            { "rotate-ccw", "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8 M3 3v5h5" },
            { "pencil", "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-0.5 0.83l-1.321 4.352a0.5 0.5 0 0 0 0.623 0.622l4.353-1.32a2 2 0 0 0 0.83-0.497z M15 5l4 4" },
            { "refresh", "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5" },
            { "cloud-off", "M2 2l20 20 M5.782 5.782A7 7 0 0 0 9 19h8.5a4.5 4.5 0 0 0 1.307-0.193 M21.532 16.5A4.5 4.5 0 0 0 17.5 10h-1.79A7.008 7.008 0 0 0 10 5.07" },
        };

        // Filled shapes
        static readonly Dictionary<string, string> filled = new Dictionary<string, string>
        {
            { "play", "M6 3L20 12 6 21z" },
            { "grip", "M8 12a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M8 5a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M8 19a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M14 12a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M14 5a1 1 0 1 0 2 0a1 1 0 1 0-2 0z M14 19a1 1 0 1 0 2 0a1 1 0 1 0-2 0z" },
        };

        static readonly Dictionary<string, Geometry> geo = new Dictionary<string, Geometry>();

        static Geometry G(string name)
        {
            Geometry g;
            if (geo.TryGetValue(name, out g)) return g;
            string d;
            if (!paths.TryGetValue(name, out d)) d = filled[name];
            g = Geometry.Parse(d);
            g.Freeze();
            geo[name] = g;
            return g;
        }

        public static Icon Make(string name, double size, Brush brush, double stroke = 2)
        {
            return new Icon(G(name), size, brush, stroke, filled.ContainsKey(name));
        }
    }

    public sealed class Icon : Viewbox
    {
        readonly System.Windows.Shapes.Path path;
        readonly bool isFilled;

        public Icon(Geometry g, double size, Brush brush, double stroke, bool isFilled)
        {
            this.isFilled = isFilled;
            Width = size;
            Height = size;
            Stretch = Stretch.Uniform;
            SnapsToDevicePixels = false;
            path = new System.Windows.Shapes.Path
            {
                Data = g,
                StrokeThickness = isFilled ? 0.6 : stroke,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            Child = canvas;
            SetBrush(brush);
            IsHitTestVisible = false;
            VerticalAlignment = VerticalAlignment.Center;
        }

        public void SetBrush(Brush b)
        {
            path.Stroke = b;
            path.Fill = isFilled ? b : null;
        }

        public double Stroke { set { path.StrokeThickness = value; } }
    }

    /// Damped spring easing, so tween-based WPF animations feel like motion's spring physics.
    public sealed class SpringEase : EasingFunctionBase
    {
        public double Stiffness = 500, Damping = 30, Mass = 1, DurationSec = 0.45;

        protected override double EaseInCore(double t)
        {
            double w0 = Math.Sqrt(Stiffness / Mass);
            double zeta = Damping / (2 * Math.Sqrt(Stiffness * Mass));
            double time = t * DurationSec;
            if (t >= 1) return 1;
            if (zeta < 1)
            {
                double wd = w0 * Math.Sqrt(1 - zeta * zeta);
                return 1 - Math.Exp(-zeta * w0 * time) * (Math.Cos(wd * time) + (zeta * w0 / wd) * Math.Sin(wd * time));
            }
            return 1 - Math.Exp(-w0 * time) * (1 + w0 * time);
        }

        protected override Freezable CreateInstanceCore() { return new SpringEase(); }

        public static SpringEase Make(double stiffness, double damping, double mass, double durationSec)
        {
            var e = new SpringEase { Stiffness = stiffness, Damping = damping, Mass = mass, DurationSec = durationSec, EasingMode = EasingMode.EaseIn };
            e.Freeze();
            return e;
        }
    }

    public static class Anim
    {
        public static readonly IEasingFunction PressSpring = SpringEase.Make(550, 24, 1, 0.42);
        public static readonly IEasingFunction RowSpring = SpringEase.Make(520, 34, 0.7, 0.35);
        public static readonly IEasingFunction DrawerSpring = SpringEase.Make(460, 34, 1, 0.38);
        public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };

        static Anim() { ((Freezable)Out).Freeze(); }

        public static void Transforms(UIElement el, out ScaleTransform scale, out TranslateTransform translate)
        {
            var g = el.RenderTransform as TransformGroup;
            if (g == null || g.Children.Count != 2)
            {
                g = new TransformGroup();
                g.Children.Add(new ScaleTransform(1, 1));
                g.Children.Add(new TranslateTransform());
                el.RenderTransform = g;
                el.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            scale = (ScaleTransform)g.Children[0];
            translate = (TranslateTransform)g.Children[1];
        }

        public static void ScaleTo(UIElement el, double to, double ms, IEasingFunction ease)
        {
            ScaleTransform s; TranslateTransform t;
            Transforms(el, out s, out t);
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public static void Fade(UIElement el, double to, double ms, Action done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out };
            if (done != null) a.Completed += (s, e) => done();
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        /// Row entrance: fade + rise + slight scale-up with spring.
        public static void Enter(UIElement el, double targetOpacity)
        {
            ScaleTransform s; TranslateTransform t;
            Transforms(el, out s, out t);
            var dur = TimeSpan.FromMilliseconds(350);
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, targetOpacity, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, dur) { EasingFunction = RowSpring });
            var sa = new DoubleAnimation(0.985, 1, dur) { EasingFunction = RowSpring };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, sa);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, sa);
        }

        /// Height reveal with clipping (like motion's height: 0 -> auto) plus a fade.
        public static void Reveal(FrameworkElement el, bool open, Action done = null)
        {
            el.ClipToBounds = true;
            if (open)
            {
                el.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                el.MaxHeight = double.PositiveInfinity;
                el.Visibility = Visibility.Visible;
                el.Opacity = 0;
                el.UpdateLayout();
                double target = el.ActualHeight;
                if (target <= 0) { el.Opacity = 1; if (done != null) done(); return; }
                var a = new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(380)) { EasingFunction = DrawerSpring };
                a.Completed += (s, e) =>
                {
                    el.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                    el.MaxHeight = double.PositiveInfinity;
                    if (done != null) done();
                };
                el.BeginAnimation(FrameworkElement.MaxHeightProperty, a);
                el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out });
            }
            else
            {
                double from = el.ActualHeight;
                var a = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out };
                a.Completed += (s, e) =>
                {
                    el.Visibility = Visibility.Collapsed;
                    el.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
                    el.MaxHeight = double.PositiveInfinity;
                    el.BeginAnimation(UIElement.OpacityProperty, null);
                    el.Opacity = 1;
                    if (done != null) done();
                };
                el.BeginAnimation(FrameworkElement.MaxHeightProperty, a);
                el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)));
            }
        }

        public static void Spin(UIElement el)
        {
            var r = new RotateTransform();
            el.RenderTransform = r;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            r.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    public static class T
    {
        public static TextBlock Text(string text, double size, FontWeight weight, Brush fg)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = Fonts.Sans,
                FontSize = size,
                FontWeight = weight,
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public static TextBlock Mono(string text, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = Fonts.Mono,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public static TextBox Input(double size, FontWeight weight)
        {
            var tb = new TextBox
            {
                FontFamily = Fonts.Sans,
                FontSize = size,
                FontWeight = weight,
                Foreground = Pal.B(Pal.S900),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                CaretBrush = Pal.B(Pal.S900),
                SelectionBrush = Theme.Accent,
                SelectionOpacity = 0.28,
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null,
            };
            return tb;
        }

        /// TextBox with a placeholder overlay.
        public static Grid WithPlaceholder(TextBox tb, string placeholder, out TextBlock ph)
        {
            var g = new Grid();
            var p = Text(placeholder, tb.FontSize, tb.FontWeight, Pal.B(Pal.S400));
            p.IsHitTestVisible = false;
            p.VerticalAlignment = tb.VerticalAlignment;
            p.Margin = new Thickness(2, tb.Padding.Top, 0, 0);
            p.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(tb);
            g.Children.Add(p);
            tb.TextChanged += (s, e) => p.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ph = p;
            return g;
        }
    }

    /// A tactile, spring-animated clickable surface (replaces <motion.button>).
    public class Btn : Grid
    {
        public readonly Border Face;
        readonly Border shadow;
        Rectangle dashed;
        readonly StackPanel content;
        readonly List<TextBlock> texts = new List<TextBlock>();
        readonly List<Icon> icons = new List<Icon>();
        bool pressed, hovering;

        public Brush Bg, HoverBg, Fg, HoverFg, BorderBrushN, BorderBrushH, DashBrush, DashBrushH;
        public double HoverScale = 1.05, PressScale = 0.9;
        public bool Disabled;
        public Action Click;

        public Btn(double height, double radius, Thickness padding)
        {
            Height = height;
            Cursor = Cursors.Hand;
            shadow = new Border { CornerRadius = new CornerRadius(radius), Background = Pal.B(Pal.S900, 0.12), Margin = new Thickness(0, 2, 0, -2), Visibility = Visibility.Collapsed };
            Face = new Border { CornerRadius = new CornerRadius(radius), Padding = padding };
            content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Face.Child = content;
            Children.Add(shadow);
            Children.Add(Face);
            VerticalAlignment = VerticalAlignment.Center;
            SnapsToDevicePixels = true;

            MouseEnter += (s, e) => { hovering = true; Refresh(); if (!Disabled && HoverScale != 1) Anim.ScaleTo(this, HoverScale, 160, Anim.Out); };
            MouseLeave += (s, e) => { hovering = false; Refresh(); if (!pressed) Anim.ScaleTo(this, 1, 260, Anim.PressSpring); };
            MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (Disabled) return;
                pressed = true;
                CaptureMouse();
                shadow.Opacity = 0;
                Anim.ScaleTo(this, PressScale, 70, Anim.Out);
            };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!pressed) return;
                e.Handled = true;
                pressed = false;
                ReleaseMouseCapture();
                shadow.Opacity = 1;
                bool inside = IsMouseOver;
                Anim.ScaleTo(this, inside ? HoverScale : 1, 420, Anim.PressSpring);
                if (inside && Click != null && !Disabled) Click();
            };
            LostMouseCapture += (s, e) => { if (pressed) { pressed = false; shadow.Opacity = 1; Anim.ScaleTo(this, 1, 300, Anim.PressSpring); } };
        }

        public Btn Tactile(bool on)
        {
            shadow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            return this;
        }

        public Btn Dashed(Brush stroke, Brush hoverStroke)
        {
            if (dashed == null)
            {
                dashed = new Rectangle { StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2.2, 1.6 }, IsHitTestVisible = false, SnapsToDevicePixels = true };
                Children.Add(dashed);
            }
            dashed.RadiusX = dashed.RadiusY = Face.CornerRadius.TopLeft;
            DashBrush = stroke;
            DashBrushH = hoverStroke;
            dashed.Visibility = Visibility.Visible;
            Refresh();
            return this;
        }

        public void NoDash() { if (dashed != null) dashed.Visibility = Visibility.Collapsed; }

        public Btn AddIcon(string name, double size, double stroke = 2)
        {
            var ic = Icons.Make(name, size, Fg ?? Brushes.Black, stroke);
            if (content.Children.Count > 0) ic.Margin = new Thickness(6, 0, 0, 0);
            icons.Add(ic);
            content.Children.Add(ic);
            Refresh();
            return this;
        }

        public Btn AddElement(UIElement el)
        {
            content.Children.Add(el);
            return this;
        }

        public TextBlock AddText(string text, double size, FontWeight weight, bool mono = false, double gap = 6)
        {
            var tb = mono ? T.Mono(text, size, Fg) : T.Text(text, size, weight, Fg);
            if (content.Children.Count > 0) tb.Margin = new Thickness(gap, 0, 0, 0);
            texts.Add(tb);
            content.Children.Add(tb);
            Refresh();
            return tb;
        }

        public void Clear()
        {
            content.Children.Clear();
            texts.Clear();
            icons.Clear();
        }

        public void Paint(Brush bg, Brush hoverBg, Brush fg, Brush hoverFg, Brush border = null, Brush hoverBorder = null)
        {
            Bg = bg; HoverBg = hoverBg ?? bg; Fg = fg; HoverFg = hoverFg ?? fg;
            BorderBrushN = border; BorderBrushH = hoverBorder ?? border;
            Refresh();
        }

        public void Refresh()
        {
            bool h = hovering && !Disabled;
            Face.Background = h ? HoverBg : Bg;
            var fg = h ? HoverFg : Fg;
            foreach (var t in texts) t.Foreground = fg;
            foreach (var i in icons) i.SetBrush(fg);
            var bb = h ? BorderBrushH : BorderBrushN;
            Face.BorderBrush = bb;
            Face.BorderThickness = bb == null ? new Thickness(0) : new Thickness(1);
            if (dashed != null) dashed.Stroke = h ? DashBrushH : DashBrush;
            Face.Opacity = Disabled ? 0.3 : 1;
            Cursor = Disabled ? Cursors.Arrow : Cursors.Hand;
        }
    }
}
