using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.Logic.Config;
using System;

namespace Nikse.SubtitleEdit.Logic;

/// <summary>
/// Applies the color theme (<see cref="ThemePreset"/> + the user's own colors) to the whole app: Fluent's palette
/// for the standard controls, then <see cref="ChromeStyles"/> for everything else. Also the UI scale.
/// </summary>
public static class UiTheme
{
    private static IStyle? _layoutScaleMenuStyle;
    private static bool _followsSystem;

    public const string ThemeNameSystem = ThemePreset.System;

    public static FluentTheme? FluentTheme { get; set; }

    /// <summary>The theme in use ("System" resolved to Dark or Light).</summary>
    public static string ThemeName => ThemePreset.Current.Name;

    public static bool IsDarkThemeEnabled() => ChromeStyles.Current.Dark;

    public static void SetCurrentTheme()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        // "System" follows the OS's dark/light switch while running
        if (_followsSystem)
        {
            app.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

        _followsSystem = Se.Settings.Appearance.Theme == ThemeNameSystem;
        if (_followsSystem)
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
        }

        ApplyColors();
        if (_followsSystem)
        {
            app.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        }

        ApplyMenuScaleStyle(Se.Settings.Appearance.LayoutScale);
        ApplyLayoutScaleToAllWindows();
    }

    private static void OnActualThemeVariantChanged(object? sender, EventArgs e) => ApplyColors();

    /// <summary>
    /// The palette into Fluent (text, fields, lists, menus, the accent) and the chrome styles. Re-run on any color
    /// change; brushes from <see cref="ChromeStyles.Shared"/> and the waveform recolor with it.
    /// </summary>
    public static void ApplyColors()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        ChromeStyles.Apply(); // first: builds the palette
        var p = ChromeStyles.Current;
        var variant = p.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (FluentTheme != null)
        {
            FluentTheme.Palettes[variant] = MakeFluentPalette(p);
        }

        if (!_followsSystem)
        {
            app.RequestedThemeVariant = variant;
        }
    }

    private static ColorPaletteResources MakeFluentPalette(ChromeStyles.Palette p)
    {
        Color Mix(Color a, Color b, double t)
        {
            byte F(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromRgb(F(a.R, b.R), F(a.G, b.G), F(a.B, b.B));
        }

        Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        return new ColorPaletteResources
        {
            Accent = p.Accent,
            RegionColor = p.Body,
            ErrorText = Color.Parse(p.Dark ? "#e06c6c" : "#c42b1c"),
            BaseHigh = p.Text,
            BaseMediumHigh = Mix(p.Text, p.Body, 0.2),
            BaseMedium = Mix(p.Text, p.Body, 0.35),
            BaseMediumLow = Mix(p.Text, p.Body, 0.55),
            BaseLow = Mix(p.Text, p.Body, 0.75),
            AltHigh = p.Field,
            AltMediumHigh = p.Field,
            AltMedium = p.Field,
            AltMediumLow = p.Field,
            AltLow = Mix(p.Field, p.Body, 0.5),
            ChromeAltLow = p.Text,
            ChromeBlackHigh = Colors.Black,
            ChromeBlackLow = Alpha(Colors.Black, 0x33),
            ChromeBlackMedium = Alpha(Colors.Black, 0x99),
            ChromeBlackMediumLow = Alpha(Colors.Black, 0x66),
            ChromeDisabledHigh = Mix(p.Body, p.Text, 0.12),
            ChromeDisabledLow = Mix(p.Text, p.Body, 0.5),
            ChromeGray = Mix(p.Text, p.Body, 0.55),
            ChromeHigh = p.Outline,
            ChromeLow = p.Ground,
            ChromeMedium = p.Bar,
            ChromeMediumLow = Mix(p.Bar, p.Body, 0.5),
            ChromeWhite = Colors.White,
            ListLow = Alpha(p.Text, 0x15),
            ListMedium = Alpha(p.Text, 0x26),
        };
    }

    public const double ScaleStep = 0.1;
    public const double MinScale = 0.5;
    public const double MaxScale = 2.0;

    /// <summary>
    /// Returns the logical content of a window, unwrapping the
    /// <see cref="LayoutTransformControl"/> that <see cref="ApplyScaleToWindow"/>
    /// uses to host scaled content.
    /// </summary>
    public static Control? GetUnscaledContent(Window window)
    {
        return window.Content is LayoutTransformControl ltc
            ? ltc.Child as Control
            : window.Content as Control;
    }

    public static void ApplyScaleToWindow(Window window)
    {
        var factor = Se.Settings.Appearance.LayoutScale;

        if (window.Content is LayoutTransformControl ltc)
        {
            ltc.LayoutTransform = Math.Abs(factor - 1.0) < 0.0001
                ? null
                : new ScaleTransform(factor, factor);
            return;
        }

        if (Math.Abs(factor - 1.0) < 0.0001)
        {
            return;
        }

        if (window.Content is Control content)
        {
            window.Content = null;
            window.Content = new LayoutTransformControl
            {
                Child = content,
                LayoutTransform = new ScaleTransform(factor, factor)
            };
        }
    }

    public static void ApplyLayoutScaleToAllWindows()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (var window in desktop.Windows)
        {
            ApplyScaleToWindow(window);
        }
    }

    public static void SetLayoutScale(double factor)
    {
        Se.Settings.Appearance.LayoutScale = factor;
        ApplyMenuScaleStyle(factor);
        ApplyLayoutScaleToAllWindows();
        ApplyScaleToExistingMenus(factor);
    }

    private static void ApplyMenuScaleStyle(double factor)
    {
        if (Application.Current == null)
        {
            return;
        }

        // Remove previous style to force Avalonia to re-evaluate all styled controls
        if (_layoutScaleMenuStyle != null)
        {
            Application.Current.Styles.Remove(_layoutScaleMenuStyle);
            _layoutScaleMenuStyle = null;
        }

        // Create a new style each time with the scaled values baked in.
        // This forces re-evaluation on all controls including popup menus.
        var styles = new Styles();

        // Scale MenuItems in popups/context menus (outside LayoutTransformControl)
        var menuItemStyle = new Style(x => x.OfType<MenuItem>());
        menuItemStyle.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 14.0 * factor));
        menuItemStyle.Setters.Add(new Setter(Layoutable.MinHeightProperty, 32.0 * factor));
        styles.Add(menuItemStyle);

        // Reset MenuItems inside LayoutTransformControl (already scaled by transform)
        var ltcMenuItemStyle = new Style(x => x.OfType<LayoutTransformControl>().Descendant().OfType<MenuItem>());
        ltcMenuItemStyle.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 14.0));
        ltcMenuItemStyle.Setters.Add(new Setter(Layoutable.MinHeightProperty, 32.0));
        styles.Add(ltcMenuItemStyle);

        _layoutScaleMenuStyle = styles;
        Application.Current.Styles.Add(styles);
    }

    /// <summary>
    /// Walk all open windows, find Menu, ContextMenu, and MenuFlyout instances,
    /// and directly set FontSize/MinHeight on their items. Also register Opened
    /// handlers so dynamic items get scaled when the menu opens.
    /// </summary>
    private static void ApplyScaleToExistingMenus(double factor)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (var window in desktop.Windows)
        {
            foreach (var visual in window.GetVisualDescendants())
            {
                if (visual is Menu menu)
                {
                    // Scale submenu items only (top-level items are inside LTC and already scaled)
                    foreach (var obj in menu.Items)
                    {
                        if (obj is MenuItem topItem)
                        {
                            ScaleChildMenuItems(topItem, factor);
                        }
                    }
                }

                if (visual is not Control control)
                {
                    continue;
                }

                if (control.ContextMenu is { } contextMenu)
                {
                    ScaleMenuItems(contextMenu, factor);
                    contextMenu.Opened -= OnContextMenuOpened;
                    contextMenu.Opened += OnContextMenuOpened;
                }

                if (control.ContextFlyout is MenuFlyout menuFlyout)
                {
                    ScaleMenuFlyoutItems(menuFlyout, factor);
                    menuFlyout.Opened -= OnMenuFlyoutOpened;
                    menuFlyout.Opened += OnMenuFlyoutOpened;
                }
            }
        }
    }

    private static void ScaleChildMenuItems(MenuItem parent, double factor)
    {
        foreach (var obj in parent.Items)
        {
            if (obj is MenuItem item)
            {
                item.FontSize = 14.0 * factor;
                item.MinHeight = 32.0 * factor;
                ScaleChildMenuItems(item, factor);
            }
        }
    }

    private static void ScaleMenuItems(ItemsControl parent, double factor)
    {
        foreach (var obj in parent.Items)
        {
            if (obj is MenuItem item)
            {
                item.FontSize = 14.0 * factor;
                item.MinHeight = 32.0 * factor;
                ScaleMenuItems(item, factor);
            }
        }
    }

    private static void ScaleMenuFlyoutItems(MenuFlyout flyout, double factor)
    {
        foreach (var obj in flyout.Items)
        {
            if (obj is MenuItem item)
            {
                item.FontSize = 14.0 * factor;
                item.MinHeight = 32.0 * factor;
                ScaleMenuItems(item, factor);
            }
        }
    }

    private static void OnContextMenuOpened(object? sender, EventArgs e)
    {
        if (sender is ContextMenu cm)
        {
            var factor = Se.Settings.Appearance.LayoutScale;
            ScaleMenuItems(cm, factor);
        }
    }

    private static void OnMenuFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is MenuFlyout flyout)
        {
            var factor = Se.Settings.Appearance.LayoutScale;
            ScaleMenuFlyoutItems(flyout, factor);
        }
    }
}
