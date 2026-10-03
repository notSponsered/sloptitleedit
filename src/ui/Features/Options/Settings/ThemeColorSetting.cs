using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Logic;
using System;

namespace Nikse.SubtitleEdit.Features.Options.Settings;

/// <summary>
/// One color of the color theme the user can set (Settings → Appearance). Shows the color in use, the theme's
/// until the user picks one; picking applies at once (live preview), Reset goes back to the theme's.
/// </summary>
public partial class ThemeColorSetting : ObservableObject
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;
    private readonly Func<Color> _inUse;
    private bool _refreshing;

    public string Name { get; }

    [ObservableProperty] private Color _color;
    [ObservableProperty] private bool _isOwn;

    /// <param name="get">The stored own color ("" = the theme's).</param>
    /// <param name="inUse">The color shown while there is no own color.</param>
    public ThemeColorSetting(string name, Func<string> get, Action<string> set, Func<Color> inUse)
    {
        Name = name;
        _get = get;
        _set = set;
        _inUse = inUse;
        Refresh();
    }

    /// <summary>Re-reads the color in use, e.g. after another theme was chosen.</summary>
    public void Refresh()
    {
        _refreshing = true;
        var own = _get();
        Color = string.IsNullOrEmpty(own) ? _inUse() : own.FromHexToColor();
        IsOwn = !string.IsNullOrEmpty(own);
        _refreshing = false;
    }

    partial void OnColorChanged(Color value)
    {
        if (_refreshing)
        {
            return;
        }

        _set(value.FromColorToHex());
        UiTheme.ApplyColors();
    }

    [RelayCommand]
    private void Reset()
    {
        _set(string.Empty);
        UiTheme.ApplyColors();
    }
}
