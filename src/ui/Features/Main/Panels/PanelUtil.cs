using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Logic.Config;
using System;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

internal static class PanelUtil
{
    /// <summary>
    /// Runs <paramref name="tick"/> now and every 500 ms while <paramref name="control"/> is on screen.
    /// ponytail: polling; switch to a HeaderChanged event on MainViewModel if more header panels appear.
    /// </summary>
    public static void PollWhileVisible(Control control, Action tick)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (control.IsEffectivelyVisible)
            {
                tick();
            }
        };
        control.AttachedToVisualTree += (_, _) =>
        {
            tick();
            timer.Start();
        };
        control.DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    public static TextBlock MakePlaceholder(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    public static TextBlock AssaOnlyPlaceholder() => MakePlaceholder(Se.Language.Workspace.OnlyForAssa);

    public static bool IsAssOrSsa(MainViewModel vm) => vm.IsFormatAssa || vm.IsFormatSsa;
}
