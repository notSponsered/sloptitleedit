using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nikse.SubtitleEdit.Controls;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using Optris.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>
/// One style sheet for the window chrome (bars, dividers, areas, headers, tabs, toolbars). Elements only
/// carry classes ("se-area", "se-tab", ...); colors and shape come from the palette of the current theme:
/// the flat Audacity 4-like look (panels flush, 1 px dividers, underlined active tab) for the regular
/// themes, rounded cards on gaps for the Blender theme. Must be (re)applied after UiTheme's styles —
/// in Avalonia the last matching style wins.
/// </summary>
internal static class ChromeStyles
{
    /// <param name="Ground">What shows between areas: the 1 px dividers (flat) or the gaps (cards).</param>
    /// <param name="Flat">Panels flush with 1 px dividers; false = rounded cards separated by gaps.</param>
    public sealed record Palette(
        Color Ground, Color Bar, Color Header, Color Body, Color TabActive, Color TabHover,
        Color Text, Color Muted, Color Accent, Color Outline,
        bool Flat, double Gap, double AreaRadius);

    public const double HeaderHeight = 28;

    /// <summary>
    /// Audacity 4's clip/track colors, one per panel, on the panel's icon (tab chips, "+" and split menus),
    /// so areas are told apart at a glance. Everything else stays grey; the accent marks state only.
    /// </summary>
    private static readonly Dictionary<string, string> PanelTints = new()
    {
        [PanelIds.Subtitles] = "#66a3ff",   // blue
        [PanelIds.TextEdit] = "#9996fc",    // violet
        [PanelIds.Video] = "#f08080",       // red
        [PanelIds.Waveform] = "#34b494",    // teal
        [PanelIds.Styles] = "#da8ccc",      // magenta
        [PanelIds.ScriptInfo] = "#48becf",  // cyan
        [PanelIds.Notes] = "#e8c050",       // yellow
        [PanelIds.AutoReplace] = "#ff9e65", // orange
        [PanelIds.AssaTools] = "#da8ccc",   // magenta, like Styles: both ASS
        [PanelIds.Project] = "#e8c050",     // yellow, like Notes
        [PanelIds.GitHub] = "#74be59",      // green
    };

    public static string TintClass(string panelId) => "se-tint-" + panelId;

    private static IStyle? _styles;

    public static Palette Current { get; private set; } = MakePalette();

    public static void Apply()
    {
        if (Application.Current == null)
        {
            return;
        }

        Current = MakePalette();
        if (_styles != null)
        {
            Application.Current.Styles.Remove(_styles);
        }

        _styles = Build(Current);
        Application.Current.Styles.Add(_styles);
        ApplyCaptions();
        SoftenText();
    }

    private static Palette MakePalette()
    {
        if (Se.Settings.Appearance.Theme == UiTheme.ThemeNameBlender)
        {
            // Blender 4 default dark theme.
            return new Palette(
                Ground: Hex("#161616"), Bar: Hex("#1d1d1d"), Header: Hex("#303030"), Body: Hex("#282828"),
                TabActive: Hex("#474747"), TabHover: Hex("#3a3a3a"),
                Text: Hex("#e6e6e6"), Muted: Hex("#9a9a9a"), Accent: Hex("#4772b3"), Outline: Hex("#3d3d3d"),
                Flat: false, Gap: 4, AreaRadius: 6);
        }

        var accent = AccentColor();
        if (UiTheme.IsDarkThemeEnabled())
        {
            // Derived from the user's dark background, nudged cool like Audacity 4's greys.
            var bg = UiTheme.GetDarkThemeBackgroundColor();
            var fg = UiTheme.GetDarkThemeForegroundColor();
            var body = Cool(bg, 3);
            return new Palette(
                Ground: Cool(bg, -15), Bar: Cool(bg, -3), Header: Cool(bg, 8), Body: body,
                TabActive: Cool(bg, 20), TabHover: Cool(bg, 14),
                Text: fg, Muted: Mix(fg, body, 0.42), Accent: accent, Outline: Cool(bg, 22),
                Flat: true, Gap: 1, AreaRadius: 0);
        }

        return new Palette(
            Ground: Hex("#d3d6db"), Bar: Hex("#eceef1"), Header: Hex("#e8eaed"), Body: Hex("#f9fafb"),
            TabActive: Hex("#ffffff"), TabHover: Hex("#e1e4e8"),
            Text: Hex("#1d1f23"), Muted: Hex("#5d6168"), Accent: accent, Outline: Hex("#c6cad0"),
            Flat: true, Gap: 1, AreaRadius: 0);
    }

    private static Styles Build(Palette p)
    {
        IBrush B(Color c) => new SolidColorBrush(c);
        var transparent = Brushes.Transparent;
        var radius = new CornerRadius(4);

        var styles = new Styles
        {
            // window backgrounds
            Bg<Border>("se-gap", B(p.Ground)), Bg<Panel>("se-gap", B(p.Ground)),
            Bg<Border>("se-bar", B(p.Bar)), Bg<Panel>("se-bar", B(p.Bar)),
            new Style(x => x.Is<Border>().Class("se-host"))
            {
                Setters =
                {
                    new Setter(Decorator.PaddingProperty, p.Flat ? new Thickness(0) : new Thickness(p.Gap, p.Gap / 2, p.Gap, p.Gap)),
                    new Setter(Border.BorderThicknessProperty, p.Flat ? new Thickness(0, 1, 0, 1) : new Thickness(0)),
                    new Setter(Border.BorderBrushProperty, B(p.Ground)),
                },
            },
            new Style(x => x.Is<Border>().Class("se-host-float"))
            {
                Setters = { new Setter(Decorator.PaddingProperty, new Thickness(p.Flat ? 0 : p.Gap)) },
            },

            // areas
            new Style(x => x.Is<Border>().Class("se-area"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, B(p.Body)),
                    new Setter(Border.CornerRadiusProperty, new CornerRadius(p.AreaRadius)),
                },
            },
            new Style(x => x.Is<Border>().Class("se-area-header"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, B(p.Header)),
                    new Setter(Border.CornerRadiusProperty, new CornerRadius(p.AreaRadius, p.AreaRadius, 0, 0)),
                    new Setter(Border.BorderThicknessProperty, p.Flat ? new Thickness(0, 0, 0, 1) : new Thickness(0)),
                    new Setter(Border.BorderBrushProperty, B(p.Ground)),
                },
            },

            // tabs in area headers: underlined (flat) or filled chips (cards)
            new Style(x => x.Is<Border>().Class("se-tab"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, transparent),
                    new Setter(Border.CornerRadiusProperty, p.Flat ? new CornerRadius(0) : radius),
                    new Setter(Decorator.PaddingProperty, p.Flat ? new Thickness(10, 0) : new Thickness(8, 3)),
                    new Setter(Layoutable.VerticalAlignmentProperty, p.Flat ? VerticalAlignment.Stretch : VerticalAlignment.Center),
                    new Setter(Border.BorderThicknessProperty, p.Flat ? new Thickness(0, 0, 0, 2) : new Thickness(0)),
                    new Setter(Border.BorderBrushProperty, transparent),
                },
            },
            Bg<Border>("se-tab", B(p.TabHover), ":pointerover"),
            p.Flat
                ? new Style(x => x.Is<Border>().Class("se-tab").Class("active"))
                {
                    Setters = { new Setter(Border.BorderBrushProperty, B(p.Accent)), new Setter(Border.BackgroundProperty, transparent) },
                }
                : Bg<Border>("se-tab", B(p.TabActive), "active"),
            Fg<TextBlock>("se-tab", B(p.Muted)), Fg<TextBlock>("se-tab", B(p.Text), "active"), Fg<TextBlock>("se-tab", B(p.Text), ":pointerover"),
            Fg<Icon>("se-tab", B(p.Muted)), Fg<Icon>("se-tab", B(p.Text), "active"), Fg<Icon>("se-tab", B(p.Text), ":pointerover"),

            // small icon buttons (+, area menu, close) and toolbar grips
            Plain("se-hbtn", radius), Bg<Border>("se-hbtn", B(p.TabHover), ":pointerover"),
            Fg<Icon>("se-hbtn", B(p.Muted)), Fg<Icon>("se-hbtn", B(p.Text), ":pointerover"),
            Plain("se-grip", new CornerRadius(3)), Bg<Border>("se-grip", B(p.TabHover), ":pointerover"),
            Fg<Icon>("se-grip", B(p.Muted)), Fg<Icon>("se-grip", B(p.Text), ":pointerover"),

            // workspace tabs in the top bar
            Plain("se-ws-tab", radius),
            Bg<Border>("se-ws-tab", B(p.TabHover), ":pointerover"),
            Bg<Border>("se-ws-tab", p.Flat ? new SolidColorBrush(p.Accent, 0.28) : B(p.Header), "active"),
            Fg<TextBlock>("se-ws-tab", B(p.Muted)), Fg<TextBlock>("se-ws-tab", B(p.Text), "active"), Fg<TextBlock>("se-ws-tab", B(p.Text), ":pointerover"),

            // toolbar buttons with line icons (ASSA tools bar)
            new Style(x => x.OfType<Button>().Class("se-tool"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.CornerRadiusProperty, radius),
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                },
            },
            new Style(x => x.OfType<Button>().Class("se-tool").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, B(p.TabHover)) },
            },
            new Style(x => x.OfType<Button>().Class("se-tool").Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, B(p.TabActive)) },
            },
            new Style(x => x.OfType<Button>().Class("se-tool").Class(":disabled"))
            {
                Setters = { new Setter(Visual.OpacityProperty, 0.35) },
            },
            new Style(x => x.OfType<Button>().Class("se-tool").Descendant().OfType<Path>())
            {
                Setters = { new Setter(Shape.StrokeProperty, B(p.Muted)) },
            },
            new Style(x => x.OfType<Button>().Class("se-tool").Class(":pointerover").Descendant().OfType<Path>())
            {
                Setters = { new Setter(Shape.StrokeProperty, B(p.Text)) },
            },

            // the same for toggles (context controls, e.g. the drawing grid); "on" = pressed look + bright icon
            new Style(x => x.OfType<ToggleButton>().Class("se-tool"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.CornerRadiusProperty, radius),
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                },
            },
            new Style(x => x.OfType<ToggleButton>().Class("se-tool").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, transparent) },
            },
            new Style(x => x.OfType<ToggleButton>().Class("se-tool").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, B(p.TabHover)) },
            },
            new Style(x => x.OfType<ToggleButton>().Class("se-tool").Class(":checked").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(p.Accent, 0.28)) },
            },
            new Style(x => x.OfType<ToggleButton>().Class("se-tool").Descendant().OfType<Path>())
            {
                Setters = { new Setter(Shape.StrokeProperty, B(p.Muted)) },
            },
            new Style(x => x.OfType<ToggleButton>().Class("se-tool").Class(":checked").Descendant().OfType<Path>())
            {
                Setters = { new Setter(Shape.StrokeProperty, B(p.Text)) },
            },
            new Style(x => x.OfType<ToggleButton>().Class("se-tool").Class(":pointerover").Descendant().OfType<Path>())
            {
                Setters = { new Setter(Shape.StrokeProperty, B(p.Text)) },
            },
            Bg<Border>("se-sep", B(p.Outline)),

            // splitters: the dividers between areas
            new Style(x => x.OfType<GridSplitter>().Class("se-splitter"))
            {
                Setters = { new Setter(TemplatedControl.BackgroundProperty, transparent) },
            },
            new Style(x => x.OfType<GridSplitter>().Class("se-splitter").Class(":pointerover"))
            {
                Setters = { new Setter(TemplatedControl.BackgroundProperty, p.Flat ? new SolidColorBrush(p.Accent, 0.7) : B(p.Outline)) },
            },

            // text roles: captions above fields, the status bar
            new Style(x => x.OfType<TextBlock>().Class("se-caption"))
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, B(p.Muted)),
                    new Setter(TextBlock.FontSizeProperty, 12.0),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.Normal),
                },
            },
            new Style(x => x.Is<Panel>().Class("se-status").Descendant().OfType<TextBlock>())
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, B(p.Muted)),
                    new Setter(TextBlock.FontSizeProperty, 12.0),
                },
            },

            // time codes, durations and list columns use tabular figures (Inter's tnum): digits line up and don't jitter
            Tnum(x => x.Is<DataGrid>()), Tnum(x => x.Is<TimeCodeUpDown>()), Tnum(x => x.Is<SecondsUpDown>()),
            Tnum(x => x.Is<Panel>().Class("se-status")),

            // format/episode pickers in the menu row: as compact as the menu items beside them
            new Style(x => x.Is<Panel>().Class("se-pickers").Descendant().OfType<ComboBox>())
            {
                Setters =
                {
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                    new Setter(Layoutable.HeightProperty, 26.0),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 0, 0, 0)),
                    new Setter(TemplatedControl.FontSizeProperty, 12.0),
                    new Setter(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                },
            },

            // panel lists (ListBox.se-list: project episodes, pull requests): flush rows, quiet hover, accent-tint
            // selection; ListBoxItem.current = the item you are on (accent bar Border.se-marker, accent TextBlock.se-strong)
            new Style(x => x.OfType<ListBox>().Class("se-list"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, transparent),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                },
            },
            new Style(x => x.OfType<ListBox>().Class("se-list").Descendant().OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                },
            },
            ListRow(":pointerover", B(p.TabHover), p.Text),
            ListRow(":selected", B(p.TabActive), p.Text), // neutral: where the keyboard is; the accent marks "current"
            ListRow(":selected:pointerover", B(Mix(p.TabActive, p.Text, 0.06)), p.Text),
            new Style(x => x.Is<Border>().Class("se-marker")) { Setters = { new Setter(Border.BackgroundProperty, transparent) } },
            new Style(x => x.OfType<ListBoxItem>().Class("current").Descendant().Is<Border>().Class("se-marker"))
            {
                Setters = { new Setter(Border.BackgroundProperty, B(p.Accent)) },
            },
            new Style(x => x.OfType<ListBoxItem>().Class("current").Descendant().OfType<TextBlock>().Class("se-strong"))
            {
                Setters = { new Setter(TextBlock.ForegroundProperty, B(p.Accent)) },
            },
            new Style(x => x.OfType<TextBlock>().Class("se-heading"))
            {
                Setters =
                {
                    new Setter(TextBlock.FontSizeProperty, 13.0),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                    new Setter(TextBlock.ForegroundProperty, B(p.Text)),
                },
            },
            Tnum(x => x.OfType<ListBox>().Class("se-list")),

            // lists inside areas: column headers blend into the panel instead of stacking a second header strip
            new Style(x => x.Is<Border>().Class("se-area").Descendant().OfType<DataGridColumnHeader>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, B(p.Body)),
                    new Setter(TemplatedControl.ForegroundProperty, B(p.Muted)),
                    new Setter(DataGridColumnHeader.SeparatorBrushProperty, B(p.Outline)),
                },
            },
        };

        // panel icons in their Audacity 4 clip color (last, so they win over the muted tab icons); darker on light themes
        var lightTheme = p.Body.R + p.Body.G + p.Body.B > 3 * 128;
        foreach (var (id, hex) in PanelTints)
        {
            var tint = lightTheme ? Mix(Hex(hex), Colors.Black, 0.3) : Hex(hex);
            styles.Add(new Style(x => x.OfType<Icon>().Class(TintClass(id))) { Setters = { new Setter(Icon.ForegroundProperty, B(tint)) } });
        }

        // Selection is a tint of the accent, not a solid bar (Muse/Audacity 4 use ~30% accent).
        var accent = B(p.Accent);
        styles.Resources["DataGridRowSelectedBackgroundBrush"] = accent;
        styles.Resources["DataGridRowSelectedHoveredBackgroundBrush"] = accent;
        styles.Resources["DataGridRowSelectedUnfocusedBackgroundBrush"] = accent;
        styles.Resources["DataGridRowSelectedHoveredUnfocusedBackgroundBrush"] = accent;
        styles.Resources["DataGridRowSelectedBackgroundOpacity"] = p.Flat ? 0.34 : 0.55;
        styles.Resources["DataGridRowSelectedHoveredBackgroundOpacity"] = p.Flat ? 0.42 : 0.62;
        styles.Resources["DataGridRowSelectedUnfocusedBackgroundOpacity"] = p.Flat ? 0.24 : 0.4;
        styles.Resources["DataGridRowSelectedHoveredUnfocusedBackgroundOpacity"] = p.Flat ? 0.3 : 0.45;

        // Inputs: quiet outlines instead of Fluent's 60% white/black ones (Blender: its #3d3d3d widget
        // outline, #4d4d4d on hover); focus keeps the accent.
        var field = B(p.Flat ? Mix(p.Outline, p.Muted, 0.35) : p.Outline);
        var fieldHover = B(Mix(p.Outline, p.Muted, p.Flat ? 0.6 : 0.17));
        styles.Resources["TextControlBorderBrush"] = field;
        styles.Resources["TextControlBorderBrushPointerOver"] = fieldHover;
        styles.Resources["ComboBoxBorderBrush"] = field;
        styles.Resources["ComboBoxBorderBrushPointerOver"] = fieldHover;
        styles.Resources["ComboBoxBorderBrushPressed"] = fieldHover;

        return styles;
    }

    private static readonly FontFeatureCollection TabularFigures = FontFeatureCollection.Parse("tnum");

    private static Style Tnum(Func<Selector?, Selector> selector) => new(selector)
    {
        Setters = { new Setter(TextElement.FontFeaturesProperty, TabularFigures) },
    };

    /// <summary>Row background/foreground of a ListBox.se-list item in a state like ":selected:pointerover".</summary>
    private static Style ListRow(string states, IBrush background, Color text)
    {
        var style = new Style(x =>
        {
            var item = x.OfType<ListBox>().Class("se-list").Descendant().OfType<ListBoxItem>();
            foreach (var state in states.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                item = item.Class(":" + state);
            }

            return item.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
        });
        style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, background));
        style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new SolidColorBrush(text)));
        return style;
    }

    private static Style Plain(string cls, CornerRadius radius) => new(x => x.Is<Border>().Class(cls))
    {
        Setters =
        {
            new Setter(Border.BackgroundProperty, Brushes.Transparent),
            new Setter(Border.CornerRadiusProperty, radius),
        },
    };

    private static Style Bg<T>(string cls, IBrush brush, string? extra = null) where T : Control
    {
        var property = typeof(T) == typeof(Panel) ? Panel.BackgroundProperty : Border.BackgroundProperty;
        return new Style(x => extra == null ? x.Is<T>().Class(cls) : x.Is<T>().Class(cls).Class(extra))
        {
            Setters = { new Setter(property, brush) },
        };
    }

    /// <summary>Foreground of <typeparamref name="T"/> inside a Border with class <paramref name="cls"/>.</summary>
    private static Style Fg<T>(string cls, IBrush brush, string? extra = null) where T : Control
    {
        var property = typeof(T) == typeof(Icon) ? Icon.ForegroundProperty : TextBlock.ForegroundProperty;
        var style = new Style(x => (extra == null ? x.Is<Border>().Class(cls) : x.Is<Border>().Class(cls).Class(extra)).Descendant().OfType<T>());
        style.Setters.Add(new Setter(property, brush));
        return style;
    }

    private static bool _textHooked;

    /// <summary>
    /// Greyscale antialiasing with light hinting for every window (the macOS / Audacity 4 look) instead of
    /// ClearType-style subpixel text: no colour fringes, glyph shapes closer to the design. Set on the window;
    /// its whole visual tree renders with it.
    /// </summary>
    private static void SoftenText()
    {
        if (_textHooked || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _textHooked = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Soften(window));
        foreach (var window in desktop.Windows)
        {
            Soften(window);
        }
    }

    private static void Soften(Visual visual)
    {
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(visual, TextHintingMode.Light);
    }

    // Windows 11 can tint a window's native title bar; SE uses it so the caption matches its own top bar
    // instead of the OS accent color.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static bool _captionHooked;

    private static void ApplyCaptions()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (!_captionHooked)
        {
            _captionHooked = true;
            Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => ApplyCaption(window));
        }

        foreach (var window in desktop.Windows)
        {
            ApplyCaption(window);
        }
    }

    private static void ApplyCaption(Window window)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int caption = ColorRef(Current.Bar);
        int text = ColorRef(Current.Text);
        int border = ColorRef(Current.Ground);
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));    // DWMWA_TEXT_COLOR
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));  // DWMWA_BORDER_COLOR
    }

    private static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;

    private static Color AccentColor()
    {
        var app = Application.Current;
        if (app != null && app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out var value) && value is Color color)
        {
            return color;
        }

        return Hex("#4772b3");
    }

    private static Color Hex(string hex) => Color.Parse(hex);

    /// <summary>Lighter/darker by <paramref name="amount"/>, with a slight blue lean (Audacity 4's cool greys).</summary>
    private static Color Cool(Color c, int amount)
    {
        byte F(byte v, int extra) => (byte)Math.Clamp(v + amount + extra, 0, 255);
        return Color.FromRgb(F(c.R, 0), F(c.G, 1), F(c.B, 4));
    }

    private static Color Mix(Color a, Color b, double t)
    {
        byte F(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromRgb(F(a.R, b.R), F(a.G, b.G), F(a.B, b.B));
    }
}
