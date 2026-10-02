using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Shared.ColorPicker;

/// <summary>
/// The one color picker: spectrum + RGBA, hex and ASS codes, recent colors, and an eyedropper that picks from
/// anywhere on screen, the video included (ASSA tools → Image color picker opens it with the eyedropper running).
/// </summary>
public partial class ColorPickerViewModel : ObservableObject
{
    [ObservableProperty] private Color _selectedColor = Colors.White;
    [ObservableProperty] private Color _originalColor = Colors.White;

    [ObservableProperty] private byte _red = 255;
    [ObservableProperty] private byte _green = 255;
    [ObservableProperty] private byte _blue = 255;
    [ObservableProperty] private byte _alpha = 255;

    [ObservableProperty] private string _hexColor = "FFFFFF";
    [ObservableProperty] private string _assColor = "&HFFFFFF&";

    [ObservableProperty] private Color _redGradientStart = Colors.Black;
    [ObservableProperty] private Color _redGradientEnd = Colors.Red;
    [ObservableProperty] private Color _greenGradientStart = Colors.Black;
    [ObservableProperty] private Color _greenGradientEnd = Colors.Lime;
    [ObservableProperty] private Color _blueGradientStart = Colors.Black;
    [ObservableProperty] private Color _blueGradientEnd = Colors.Blue;
    [ObservableProperty] private Color _alphaGradientStart = Colors.Transparent;
    [ObservableProperty] private Color _alphaGradientEnd = Colors.White;

    [ObservableProperty] private bool _showAlpha = true;

    [ObservableProperty] private bool _isPicking;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }

    /// <summary>Opened on its own (ASSA tools): nothing to apply to, so the buttons are Copy + Close.</summary>
    public bool IsStandalone { get; private set; }

    public List<Color> RecentColors { get; } = [];

    public bool CanPickFromScreen => ScreenEyedropper.IsSupported;

    private bool _isUpdating;

    public ColorPickerViewModel()
    {
        LoadRecentColors();
    }

    public void Initialize(Color initialColor)
    {
        OriginalColor = initialColor;
        SetColor(initialColor);
    }

    /// <summary>Opens as the image color picker: Copy + Close instead of OK, eyedropper started once shown.</summary>
    public void InitializeStandalone()
    {
        IsStandalone = true;
        Initialize(RecentColors.FirstOrDefault(Colors.White));
    }

    partial void OnRedChanged(byte value) => UpdateColorFromRgb();
    partial void OnGreenChanged(byte value) => UpdateColorFromRgb();
    partial void OnBlueChanged(byte value) => UpdateColorFromRgb();
    partial void OnAlphaChanged(byte value) => UpdateColorFromRgb();

    partial void OnHexColorChanged(string value)
    {
        var hex = value.Trim().TrimStart('#');
        if (_isUpdating || hex.Length is not (6 or 8) || !IsHex(hex))
        {
            return;
        }

        var color = ("#" + hex).FromHexToColor();
        SetColor(hex.Length == 6 ? Color.FromArgb(Alpha, color.R, color.G, color.B) : color); // RRGGBB keeps the A slider
    }

    /// <summary>ASS order: &amp;HBBGGRR&amp; or &amp;HAABBGGRR&amp; (alpha 00 = opaque).</summary>
    partial void OnAssColorChanged(string value)
    {
        var hex = value.Trim().Trim('&').TrimStart('H', 'h').TrimEnd('&');
        if (_isUpdating || hex.Length is not (6 or 8) || !IsHex(hex))
        {
            return;
        }

        var n = Convert.ToUInt32(hex, 16);
        var alpha = hex.Length == 8 ? (byte)(255 - (n >> 24)) : Alpha;
        SetColor(Color.FromArgb(alpha, (byte)n, (byte)(n >> 8), (byte)(n >> 16)));
    }

    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);

    public void UpdateFromSpectrum(Color color) => SetColor(Color.FromArgb(Alpha, color.R, color.G, color.B));

    private void UpdateColorFromRgb()
    {
        if (!_isUpdating)
        {
            SetColor(Color.FromArgb(Alpha, Red, Green, Blue));
        }
    }

    /// <summary>The one place the color changes: updates channels, codes and slider gradients together.</summary>
    private void SetColor(Color color)
    {
        _isUpdating = true;
        SelectedColor = color;
        Red = color.R;
        Green = color.G;
        Blue = color.B;
        Alpha = color.A;
        HexColor = $"{color.R:X2}{color.G:X2}{color.B:X2}";
        AssColor = color.A == 255 || !ShowAlpha
            ? $"&H{color.B:X2}{color.G:X2}{color.R:X2}&"
            : $"&H{255 - color.A:X2}{color.B:X2}{color.G:X2}{color.R:X2}&";
        RedGradientStart = Color.FromRgb(0, color.G, color.B);
        RedGradientEnd = Color.FromRgb(255, color.G, color.B);
        GreenGradientStart = Color.FromRgb(color.R, 0, color.B);
        GreenGradientEnd = Color.FromRgb(color.R, 255, color.B);
        BlueGradientStart = Color.FromRgb(color.R, color.G, 0);
        BlueGradientEnd = Color.FromRgb(color.R, color.G, 255);
        AlphaGradientStart = Color.FromArgb(0, color.R, color.G, color.B);
        AlphaGradientEnd = Color.FromRgb(color.R, color.G, color.B);
        _isUpdating = false;
    }

    public void PickRecent(Color color) => SetColor(Color.FromArgb(ShowAlpha ? color.A : (byte)255, color.R, color.G, color.B));

    [RelayCommand]
    private void RestoreOriginal() => SetColor(OriginalColor);

    private void LoadRecentColors()
    {
        var saved = new[]
        {
            Se.Settings.Tools.LastColorPickerColor, Se.Settings.Tools.LastColorPickerColor1,
            Se.Settings.Tools.LastColorPickerColor2, Se.Settings.Tools.LastColorPickerColor3,
            Se.Settings.Tools.LastColorPickerColor4, Se.Settings.Tools.LastColorPickerColor5,
            Se.Settings.Tools.LastColorPickerColor6, Se.Settings.Tools.LastColorPickerColor7,
        };

        foreach (var hex in saved.Where(h => !string.IsNullOrWhiteSpace(h)))
        {
            try
            {
                var color = hex.FromHexToColor();
                if (!RecentColors.Any(c => IsSameSwatch(c, color)))
                {
                    RecentColors.Add(color);
                }
            }
            catch
            {
                // ignore a broken entry
            }
        }
    }

    /// <summary>
    /// Whether two colors would look like the same recent-color swatch (then only the newer one is kept).
    /// Used when loading the saved list and when saving after OK / Copy.
    /// </summary>
    private static bool IsSameSwatch(Color a, Color b)
    {
        // Within 3 per channel: eyedropper picks that differ only by compression noise collapse, while
        // deliberately close shades (a fill and its slightly darker shadow) still differ by more.
        const int tolerance = 3;
        return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
    }

    /// <summary>Newest first, no duplicates, no filler.</summary>
    private void SaveRecentColors()
    {
        var list = RecentColors.Where(c => !IsSameSwatch(c, SelectedColor)).Prepend(SelectedColor).Take(8)
            .Select(c => c.FromColorToHex()).ToList();
        while (list.Count < 8)
        {
            list.Add(string.Empty);
        }

        var t = Se.Settings.Tools;
        (t.LastColorPickerColor, t.LastColorPickerColor1, t.LastColorPickerColor2, t.LastColorPickerColor3) = (list[0], list[1], list[2], list[3]);
        (t.LastColorPickerColor4, t.LastColorPickerColor5, t.LastColorPickerColor6, t.LastColorPickerColor7) = (list[4], list[5], list[6], list[7]);
        Se.SaveSettings();
    }

    [RelayCommand]
    private void Ok()
    {
        SaveRecentColors();
        OkPressed = true;
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Window?.Close();
    }

    [RelayCommand]
    private Task CopyHex() => Copy("#" + HexColor);

    [RelayCommand]
    private Task CopyAss() => Copy(AssColor);

    private async Task Copy(string text)
    {
        if (Window?.Clipboard != null)
        {
            await ClipboardHelper.SetTextAsync(Window, text);
            if (IsStandalone)
            {
                SaveRecentColors(); // a color copied out of the image picker counts as used
            }
        }
    }

    /// <summary>
    /// Eyedropper: the pointer becomes an eyedropper over every screen; hold the left button for a loupe and release
    /// on any pixel (the playing video, this window, anything) to take it. Esc / right click cancels.
    /// </summary>
    [RelayCommand]
    private async Task PickFromScreen()
    {
        if (IsPicking || Window == null || !ScreenEyedropper.IsSupported)
        {
            return;
        }

        IsPicking = true;
        var picked = await ScreenEyedropper.PickAsync(Window);
        Window.Activate();

        IsPicking = false;
        if (picked is { } c)
        {
            SetColor(Color.FromArgb(Alpha, c.R, c.G, c.B));
        }
    }

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Source is TextBox)
        {
            return; // Ctrl+C/V/Enter belong to the hex/ASS fields while typing there
        }

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(async () => await CopyHex());
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (Window == null)
                {
                    return;
                }

                var text = (await ClipboardHelper.GetTextAsync(Window))?.Trim() ?? string.Empty;
                if (text.StartsWith('&') || text.StartsWith("H", StringComparison.OrdinalIgnoreCase))
                {
                    AssColor = text;
                }
                else
                {
                    HexColor = text;
                }
            });
        }
        else if (e.Key == Key.Enter && !IsStandalone)
        {
            e.Handled = true;
            Ok();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Window?.Close();
        }
    }
}
