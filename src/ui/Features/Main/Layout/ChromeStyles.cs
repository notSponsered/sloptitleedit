using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nikse.SubtitleEdit.Controls;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Every UI color, derived from the theme (<see cref="ThemePreset"/>) and the user's own colors.</summary>
    /// <param name="Ground">What shows between areas: the 1 px dividers (flat) or the gaps (cards).</param>
    /// <param name="Field">Text boxes and other inputs.</param>
    /// <param name="Flat">Panels flush with 1 px dividers; false = rounded cards separated by gaps.</param>
    public sealed record Palette(
        Color Ground, Color Bar, Color Header, Color Body, Color TabActive, Color TabHover,
        Color Text, Color Muted, Color Accent, Color Outline, Color Field,
        Color SyntaxTag, Color SyntaxAttribute, Color SyntaxValue,
        bool Dark, bool Flat, double Gap, double AreaRadius);

    public const double HeaderHeight = 28;

    /// <summary>
    /// Audacity 4's clip/track colors, one per panel, on the panel's icon (tab chips, "+" and split menus),
    /// so areas are told apart at a glance. Everything else stays grey; the accent marks state only.
    /// Settings → Appearance can change each one (<see cref="SeAppearance.PanelTintColors"/>) or turn them off.
    /// </summary>
    public static readonly Dictionary<string, string> DefaultPanelTints = new()
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

    /// <summary>Subtitle clip colors on the waveform (bottom lane first), the same family as the panel tints.</summary>
    private static readonly string[] LaneHex = ["#66a3ff", "#9996fc", "#da8ccc", "#ff9e65", "#34b494", "#e8c050"];

    /// <summary>The waveform's lane colors for the current theme (darker on light themes, like the icons).</summary>
    public static Color[] LaneColors { get; private set; } = LaneHex.Select(Hex).ToArray();

    public static string TintClass(string panelId) => "se-tint-" + panelId;

    /// <summary>A panel's tint as shown: the user's own or the default, darkened on light themes so it reads on them.</summary>
    public static Color PanelTint(string panelId)
    {
        var own = Se.Settings.Appearance.PanelTintColors.GetValueOrDefault(panelId);
        return Readable(TryHex(own) ?? Hex(DefaultPanelTints.GetValueOrDefault(panelId, "#808080")));
    }

    private static Color Readable(Color tint) => Current.Dark ? tint : Mix(tint, Colors.Black, 0.3);

    /// <summary>A panel's icon in its tint (tab chips, the "+" and split menus, the drag ghost).</summary>
    public static Viewbox PanelIcon(string panelId, double size = 16)
    {
        var icon = Glyphs.Make(PanelRegistry.Get(panelId)?.Icon ?? Glyphs.DotsVertical, size);
        icon.Classes.Add(TintClass(panelId));
        return icon;
    }

    /// <summary>
    /// An icon button (content = a <see cref="Glyphs"/> icon) in the look of the ASSA tools bar: flat square
    /// (32 px in toolbars, 24 in the status bar), muted icon that lights up on hover, accent tint when a toggle is on.
    /// </summary>
    public static void AsTool(Button button, double size = 32)
    {
        button.Classes.Add("se-tool");
        button.Width = size;
        button.Height = size;
        button.Padding = new Thickness(0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
    }

    /// <summary>Palette colors that code-built UI (and old dialogs) use as brushes.</summary>
    public enum Role { Text, Muted, Accent, Outline, SyntaxTag, SyntaxAttribute, SyntaxValue }

    private static readonly Dictionary<(Role, double), SolidColorBrush> _shared = new();

    /// <summary>
    /// A brush in a palette color that follows the theme: one shared instance per role and opacity, recolored on
    /// every <see cref="Apply"/>, so whatever holds it (grid lines, borders, separators, syntax colors) updates live.
    /// </summary>
    public static SolidColorBrush Shared(Role role, double opacity = 1)
    {
        if (!_shared.TryGetValue((role, opacity), out var brush))
        {
            brush = new SolidColorBrush(Pick(Current, role), opacity);
            _shared[(role, opacity)] = brush;
        }

        return brush;
    }

    public static Color Pick(Palette p, Role role) => role switch
    {
        Role.Text => p.Text,
        Role.Muted => p.Muted,
        Role.Accent => p.Accent,
        Role.Outline => p.Outline,
        Role.SyntaxTag => p.SyntaxTag,
        Role.SyntaxAttribute => p.SyntaxAttribute,
        _ => p.SyntaxValue,
    };

    private static IStyle? _styles;

    public static Palette Current { get; private set; } = MakePalette();

    /// <summary>After every (re)apply, e.g. a theme change; things drawn in code (the waveform) refresh their colors.</summary>
    public static event Action? Applied;

    /// <summary>
    /// Runs <paramref name="refresh"/> on every theme change while <paramref name="control"/> is on screen (and when it
    /// comes back), for colors drawn in code; no subscription outlives the control.
    /// </summary>
    public static void WhileAttached(Control control, Action refresh)
    {
        control.AttachedToVisualTree += (_, _) =>
        {
            Applied += refresh;
            refresh();
        };
        control.DetachedFromVisualTree += (_, _) => Applied -= refresh;
    }

    public static void Apply()
    {
        if (Application.Current == null)
        {
            return;
        }

        Current = MakePalette();
        LaneColors = LaneHex.Select(h => Readable(Hex(h))).ToArray();
        foreach (var ((role, _), brush) in _shared)
        {
            brush.Color = Pick(Current, role);
        }

        if (_styles != null)
        {
            Application.Current.Styles.Remove(_styles);
        }

        _styles = Build(Current);
        Application.Current.Styles.Add(_styles);
        ApplyCaptions();
        SoftenText();
        SlimSliders();
        Applied?.Invoke();
    }

    /// <summary>
    /// The theme's base colors, with the user's own laid over them, spread into every UI color: surfaces step
    /// lighter (dark themes) or darker (light themes) from the background, the way Audacity 4 / Blender do.
    /// </summary>
    internal static Palette MakePalette()
    {
        var t = ThemePreset.Current;
        var a = Se.Settings.Appearance;
        Color Own(string? own, string? theme, Func<Color> derived) => TryHex(own) ?? TryHex(theme) ?? derived();

        var body = Own(a.BackgroundColor, t.Background, () => Colors.White);
        var text = Own(a.TextColor, t.Text, () => Colors.Black);
        var dark = Luma(body) < 128;
        Color Step(int dark_, int light) => Shade(body, dark ? dark_ : light);

        return new Palette(
            Ground: Own(a.DividerColor, t.Divider, () => Step(-15, -36)),
            Bar: Own(a.BarColor, t.Bar, () => Step(-5, -12)),
            Header: Step(8, -16),
            Body: body,
            TabActive: Step(20, 8),
            TabHover: Step(14, -24),
            Text: text,
            Muted: Mix(text, body, 0.42),
            Accent: Own(a.AccentColor, t.Accent, AccentColor),
            Outline: Step(22, -50),
            Field: Step(-6, 8),
            SyntaxTag: Own(a.SyntaxTagColor, null, () => Hex(dark ? "#d27fbe" : "#9b3f80")),
            SyntaxAttribute: Own(a.SyntaxAttributeColor, null, () => Hex(dark ? "#6fb0e6" : "#2f6fb0")),
            SyntaxValue: Own(a.SyntaxValueColor, null, () => Hex(dark ? "#e0a07e" : "#a85a32")),
            Dark: dark,
            Flat: !t.Cards,
            Gap: t.Cards ? 4 : 1,
            AreaRadius: t.Cards ? 6 : 0);
    }

    private static double Luma(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114;

    /// <summary>Lighter (+) or darker (-) by <paramref name="amount"/> on every channel.</summary>
    private static Color Shade(Color c, int amount)
    {
        byte F(byte v) => (byte)Math.Clamp(v + amount, 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
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
                Setters = { new Setter(Decorator.PaddingProperty, new Thickness(Math.Max(p.Gap, 4))) }, // a frame for the window's resize grips
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
            Fg<Viewbox>("se-tab", B(p.Muted)), Fg<Viewbox>("se-tab", B(p.Text), "active"), Fg<Viewbox>("se-tab", B(p.Text), ":pointerover"),

            // small icon buttons (+, area menu, close) and toolbar grips
            Plain("se-hbtn", radius), Bg<Border>("se-hbtn", B(p.TabHover), ":pointerover"),
            Fg<Viewbox>("se-hbtn", B(p.Muted)), Fg<Viewbox>("se-hbtn", B(p.Text), ":pointerover"),
            Plain("se-grip", new CornerRadius(3)), Bg<Border>("se-grip", B(p.TabHover), ":pointerover"),
            Fg<Viewbox>("se-grip", B(p.Muted)), Fg<Viewbox>("se-grip", B(p.Text), ":pointerover"),

            // workspace tabs in the top bar
            Plain("se-ws-tab", radius),
            Bg<Border>("se-ws-tab", B(p.TabHover), ":pointerover"),
            Bg<Border>("se-ws-tab", p.Flat ? new SolidColorBrush(p.Accent, 0.28) : B(p.Header), "active"),
            Fg<TextBlock>("se-ws-tab", B(p.Muted)), Fg<TextBlock>("se-ws-tab", B(p.Text), "active"), Fg<TextBlock>("se-ws-tab", B(p.Text), ":pointerover"),

            // icon buttons (AsTool): flat square, muted icon that lights up on hover, accent tint while a toggle is on.
            // Icons follow the text color (Glyphs), so every state sets Foreground (and the template's presenter,
            // which Fluent's own states color).
            new Style(x => x.Is<Button>().Class("se-tool"))
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, transparent),
                    new Setter(TemplatedControl.ForegroundProperty, B(p.Muted)),
                    new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                    new Setter(TemplatedControl.CornerRadiusProperty, radius),
                    new Setter(Layoutable.MinHeightProperty, 0.0),
                    new Setter(Layoutable.MinWidthProperty, 0.0),
                },
            },
            ToolState("", transparent, p.Muted),
            ToolState(":pointerover", B(p.TabHover), p.Text),
            ToolState(":pressed", B(p.TabActive), p.Text),
            ToolState(":checked", new SolidColorBrush(p.Accent, 0.28), p.Text),
            ToolState(":checked:pointerover", new SolidColorBrush(p.Accent, 0.36), p.Text),
            new Style(x => x.Is<Button>().Class("se-tool").Class(":disabled"))
            {
                Setters = { new Setter(Visual.OpacityProperty, 0.35) },
            },

            // standalone icons (Glyphs.Make(..., plain: true)), e.g. beside the zoom and volume sliders
            Fg<Viewbox>("se-glyph", B(p.Muted), self: true),

            // the busy spinner
            new Style(x => x.Is<Viewbox>().Class("se-spin"))
            {
                Animations =
                {
                    new Animation
                    {
                        Duration = TimeSpan.FromSeconds(1),
                        IterationCount = IterationCount.Infinite,
                        Children =
                        {
                            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, 0.0) } },
                            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 360.0) } },
                        },
                    },
                },
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
            new Style(x => x.Is<TextBlock>().Class("se-title")) // a panel's title (series, branch, "No project open")
            {
                Setters =
                {
                    new Setter(TextBlock.FontSizeProperty, 15.0),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                    new Setter(TextBlock.ForegroundProperty, B(p.Text)),
                },
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

        // panel icons in their clip color (last, so they win over the muted tab icons)
        if (Se.Settings.Appearance.PanelTints)
        {
            foreach (var id in DefaultPanelTints.Keys)
            {
                styles.Add(new Style(x => x.Is<Viewbox>().Class(TintClass(id))) { Setters = { new Setter(TextElement.ForegroundProperty, B(PanelTint(id))) } });
            }
        }

        // Selection is a tint of the accent, not a solid bar (Muse/Audacity 4 use ~30% accent).
        var accent = B(p.Accent);
        var r = styles.Resources;
        r["DataGridRowSelectedBackgroundBrush"] = accent;
        r["DataGridRowSelectedHoveredBackgroundBrush"] = accent;
        r["DataGridRowSelectedUnfocusedBackgroundBrush"] = accent;
        r["DataGridRowSelectedHoveredUnfocusedBackgroundBrush"] = accent;
        r["DataGridRowSelectedBackgroundOpacity"] = p.Flat ? 0.34 : 0.55;
        r["DataGridRowSelectedHoveredBackgroundOpacity"] = p.Flat ? 0.42 : 0.62;
        r["DataGridRowSelectedUnfocusedBackgroundOpacity"] = p.Flat ? 0.24 : 0.4;
        r["DataGridRowSelectedHoveredUnfocusedBackgroundOpacity"] = p.Flat ? 0.3 : 0.45;

        // Inputs: the field color with quiet outlines instead of Fluent's 60% white/black ones; focus keeps the accent.
        var field = B(p.Field);
        var fieldBorder = B(Mix(p.Outline, p.Muted, 0.25));
        var fieldBorderHover = B(Mix(p.Outline, p.Muted, 0.55));
        foreach (var control in new[] { "TextControl", "ComboBox" })
        {
            r[control + "Background"] = field;
            r[control + "BackgroundPointerOver"] = field;
            r[control + "BorderBrush"] = fieldBorder;
            r[control + "BorderBrushPointerOver"] = fieldBorderHover;
        }

        r["TextControlBackgroundFocused"] = field;
        r["ComboBoxBackgroundPressed"] = field;
        r["ComboBoxBorderBrushPressed"] = fieldBorderHover;

        // Buttons with words: flat, the drawn-icon bars' look in a quiet fill; primary actions (class "accent")
        // in the accent. One set of resources, so every button in every window follows the theme.
        var text = B(p.Text);
        foreach (var kind in new[] { "Button", "ToggleButton", "RepeatButton" })
        {
            r[kind + "Background"] = B(p.Header);
            r[kind + "BackgroundPointerOver"] = B(p.TabHover);
            r[kind + "BackgroundPressed"] = B(p.TabActive);
            r[kind + "BackgroundDisabled"] = B(Mix(p.Header, p.Body, 0.5));
            r[kind + "Foreground"] = text;
            r[kind + "ForegroundPointerOver"] = text;
            r[kind + "ForegroundPressed"] = text;
            r[kind + "ForegroundDisabled"] = B(p.Muted);
            r[kind + "BorderBrush"] = B(p.Outline);
            r[kind + "BorderBrushPointerOver"] = B(Mix(p.Outline, p.Muted, 0.4));
            r[kind + "BorderBrushPressed"] = B(Mix(p.Outline, p.Muted, 0.4));
            r[kind + "BorderBrushDisabled"] = B(p.Outline);
        }

        var accentText = B(Luma(p.Accent) > 150 ? Colors.Black : Colors.White);
        foreach (var kind in new[] { "AccentButton", "ToggleButton" })
        {
            var checkedSuffix = kind == "ToggleButton" ? "Checked" : string.Empty;
            r[kind + "Background" + checkedSuffix] = accent;
            r[kind + "Background" + checkedSuffix + "PointerOver"] = B(Mix(p.Accent, p.Text, 0.15));
            r[kind + "Background" + checkedSuffix + "Pressed"] = B(Mix(p.Accent, p.Body, 0.2));
            r[kind + "Foreground" + checkedSuffix] = accentText;
            r[kind + "Foreground" + checkedSuffix + "PointerOver"] = accentText;
            r[kind + "Foreground" + checkedSuffix + "Pressed"] = accentText;
            r[kind + "BorderBrush" + checkedSuffix] = accent;
            r[kind + "BorderBrush" + checkedSuffix + "PointerOver"] = accent;
            r[kind + "BorderBrush" + checkedSuffix + "Pressed"] = accent;
        }

        r["ControlCornerRadius"] = new CornerRadius(4);
        r["OverlayCornerRadius"] = new CornerRadius(6);

        // menus and tooltips: on the bar color with the dividers' outline; hover is a quiet accent tint
        r["MenuFlyoutPresenterBackground"] = B(p.Bar);
        r["MenuFlyoutPresenterBorderBrush"] = B(p.Outline);
        r["MenuFlyoutItemBackgroundPointerOver"] = new SolidColorBrush(p.Accent, 0.3);
        r["MenuFlyoutItemBackgroundPressed"] = new SolidColorBrush(p.Accent, 0.4);
        r["MenuFlyoutItemForegroundPointerOver"] = text;
        r["MenuFlyoutItemForegroundPressed"] = text;
        r["MenuFlyoutSubItemBackgroundPointerOver"] = new SolidColorBrush(p.Accent, 0.3);
        r["MenuFlyoutSubItemBackgroundSubMenuOpened"] = new SolidColorBrush(p.Accent, 0.3);
        r["ToolTipBackground"] = B(p.Bar);
        r["ToolTipBorderBrush"] = B(p.Outline);
        r["ToolTipForeground"] = text;

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

    /// <summary>Text and icon color of <typeparamref name="T"/> inside a Border with class <paramref name="cls"/>
    /// (<paramref name="self"/>: of a <typeparamref name="T"/> with that class).</summary>
    private static Style Fg<T>(string cls, IBrush brush, string? extra = null, bool self = false) where T : Control
    {
        var style = self
            ? new Style(x => x.Is<T>().Class(cls))
            : new Style(x => (extra == null ? x.Is<Border>().Class(cls) : x.Is<Border>().Class(cls).Class(extra)).Descendant().OfType<T>());
        style.Setters.Add(new Setter(TextElement.ForegroundProperty, brush));
        return style;
    }

    /// <summary>Background and icon color of an AsTool button in a state like ":checked:pointerover" ("" = normal).</summary>
    private static Style ToolState(string states, IBrush background, Color foreground)
    {
        var style = new Style(x =>
        {
            var button = x.Is<Button>().Class("se-tool");
            foreach (var state in states.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                button = button.Class(":" + state);
            }

            return button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
        });
        style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, background));
        style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new SolidColorBrush(foreground)));
        return style;
    }

    private static bool _slidersHooked;

    /// <summary>
    /// Every slider gets a 14 px thumb instead of Fluent's 20 px one: lighter next to the line icons. Fluent sets
    /// its size inside the template, which outranks styles, so it is set on each slider's thumb as it is built.
    /// </summary>
    private static void SlimSliders()
    {
        if (_slidersHooked)
        {
            return;
        }

        _slidersHooked = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<Slider>((_, e) =>
        {
            if (e.NameScope.Find<Thumb>("thumb") is { } thumb)
            {
                thumb.Width = 14;
                thumb.Height = 14;
            }
        });
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
        // the operating system's accent (not the "SystemAccentColor" resource: UiTheme sets that to the palette's)
        return Application.Current?.PlatformSettings?.GetColorValues().AccentColor1 ?? Hex("#4772b3");
    }

    private static Color Hex(string hex) => Color.Parse(hex);

    /// <summary>A stored color, or null when it's empty or not a valid color (e.g. a hand-edited settings file).</summary>
    private static Color? TryHex(string? hex) => !string.IsNullOrEmpty(hex) && Color.TryParse(hex, out var color) ? color : null;

    private static Color Mix(Color a, Color b, double t)
    {
        byte F(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromRgb(F(a.R, b.R), F(a.G, b.G), F(a.B, b.B));
    }
}
