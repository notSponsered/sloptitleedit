using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Logic;
using Optris.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>
/// Drag-and-drop docking. Drag a tab (one panel) or an area header (all its tabs); a translucent zone
/// shows where it lands: an area's header/centre = join as tab, its edges = split, a window's outer
/// edge = full-height/width section, outside every window = new floating window. Dragging a floating
/// window's header moves the window live and docks it when released over a zone. The zone and the
/// ghost label are separate topmost windows so they also show over the native (mpv) video surface.
/// Esc cancels.
/// </summary>
internal static class DockDrag
{
    private const double ThresholdDip = 6;

    private sealed class Session
    {
        public required Control Owner;
        public required IPointer Pointer;
        public required List<string> Panels;
        public string? Active;
        public Action? Click;
        public AreaWindow? MovingWindow;
        public PixelPoint StartScreen;
        public PixelPoint StartWindowPosition;
        public bool Dragging;
        public AreaHost.DropTarget? Target;
        public TopLevel? TopLevel;
        public double Scaling = 1;
    }

    private static Session? _session;
    private static OverlayWindow? _zone;
    private static OverlayWindow? _ghost;
    private static TextBlock? _ghostText;
    private static Icon? _ghostIcon;

    /// <param name="moveWindow">Set when the whole floating window should move with the pointer (its header was grabbed).</param>
    public static void Begin(Control owner, PointerPressedEventArgs e, IReadOnlyList<string> panels, string? active, Action? click, AreaWindow? moveWindow)
    {
        if (_session != null || !e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed || panels.Count == 0)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(owner);
        _session = new Session
        {
            Owner = owner,
            Pointer = e.Pointer,
            Panels = [.. panels],
            Active = active,
            Click = click,
            MovingWindow = moveWindow,
            StartScreen = CursorScreen(owner, e),
            StartWindowPosition = moveWindow?.Position ?? default,
            TopLevel = topLevel,
            Scaling = topLevel?.RenderScaling ?? 1,
        };

        e.Pointer.Capture(owner);
        owner.PointerMoved += OnMoved;
        owner.PointerReleased += OnReleased;
        owner.PointerCaptureLost += OnCaptureLost;
        topLevel?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        e.Handled = true;
    }

    private static void OnMoved(object? sender, PointerEventArgs e)
    {
        var s = _session;
        if (s == null)
        {
            return;
        }

        var screen = CursorScreen(s.Owner, e);
        if (!s.Dragging)
        {
            if (Math.Abs(screen.X - s.StartScreen.X) + Math.Abs(screen.Y - s.StartScreen.Y) < ThresholdDip * s.Scaling)
            {
                return;
            }

            s.Dragging = true;
        }

        if (s.MovingWindow != null)
        {
            s.MovingWindow.Position = new PixelPoint(
                s.StartWindowPosition.X + screen.X - s.StartScreen.X,
                s.StartWindowPosition.Y + screen.Y - s.StartScreen.Y);
        }
        else
        {
            ShowGhost(s, screen);
        }

        var hit = AreaHost.HitTest(screen, s.Panels, s.MovingWindow);
        s.Target = hit.Target;
        if (hit.Target != null)
        {
            ShowZone(hit.Target.Preview, s.Scaling);
        }
        else if (!hit.OverWindow && s.MovingWindow == null)
        {
            ShowZone(FloatRect(s, screen), s.Scaling); // where the new window will appear
        }
        else
        {
            _zone?.Hide();
        }
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var s = _session;
        if (s == null)
        {
            return;
        }

        var screen = CursorScreen(s.Owner, e);
        var overWindow = s.Dragging && AreaHost.HitTest(screen, s.Panels, s.MovingWindow).OverWindow;
        End();

        if (!s.Dragging)
        {
            s.Click?.Invoke();
            return;
        }

        // Defer: the drop rebuilds the area that raised this event.
        Dispatcher.UIThread.Post(() =>
        {
            if (s.Target != null && s.MovingWindow != null)
            {
                AreaHost.ApplyWindowDock(s.MovingWindow.Area, s.Target);
            }
            else if (s.Target != null)
            {
                AreaHost.ApplyMove(s.Panels, s.Active, s.Target);
            }
            else if (s.MovingWindow != null)
            {
                s.MovingWindow.SaveBounds();
            }
            else if (!overWindow)
            {
                AreaHost.ApplyFloat(s.Panels, s.Active, FloatRect(s, screen), s.Scaling);
            }
        });
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => Cancel();

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _session != null)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private static void Cancel()
    {
        var s = _session;
        if (s == null)
        {
            return;
        }

        End();
        if (s.MovingWindow != null && s.Dragging)
        {
            s.MovingWindow.Position = s.StartWindowPosition;
        }
    }

    private static void End()
    {
        var s = _session;
        _session = null;
        if (s == null)
        {
            return;
        }

        s.Owner.PointerMoved -= OnMoved;
        s.Owner.PointerReleased -= OnReleased;
        s.Owner.PointerCaptureLost -= OnCaptureLost;
        s.TopLevel?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        if (s.Pointer.Captured == s.Owner)
        {
            s.Pointer.Capture(null);
        }

        // Close (not Hide): the app shuts down on last window close, and a hidden window still counts.
        _zone?.Close();
        _zone = null;
        _ghost?.Close();
        _ghost = null;
        _ghostText = null;
        _ghostIcon = null;
    }

    /// <summary>Default size of a new floating window for these panels (DIPs).</summary>
    public static Size DefaultFloatingSize(IReadOnlyList<string> panels) =>
        panels.Count > 0 && panels.All(UiLogic.Layout.PanelIds.IsCompact) ? new Size(560, 52)
        : panels.Count == 1 && panels[0] == UiLogic.Layout.PanelIds.Waveform ? new Size(800, 220)
        : panels.Contains(UiLogic.Layout.PanelIds.Video) ? new Size(800, 450)
        : new Size(560, 420);

    private static PixelRect FloatRect(Session s, PixelPoint screen)
    {
        var size = DefaultFloatingSize(s.Panels);
        return new PixelRect(
            screen.X - (int)(60 * s.Scaling), screen.Y - (int)(14 * s.Scaling),
            (int)(size.Width * s.Scaling), (int)(size.Height * s.Scaling));
    }

    private static PixelPoint CursorScreen(Control owner, PointerEventArgs e)
    {
        if (CursorPositionHelper.GetCursorPosition() is { } p)
        {
            return new PixelPoint(p.X, p.Y);
        }

        return owner.PointToScreen(e.GetPosition(owner));
    }

    private static void ShowZone(PixelRect rect, double scaling)
    {
        _zone ??= new OverlayWindow(new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(Math.Max(3, ChromeStyles.Current.AreaRadius)),
        });

        if (_zone.Content is Border border)
        {
            var accent = ChromeStyles.Current.Accent;
            border.Background = new SolidColorBrush(accent, 0.28);
            border.BorderBrush = new SolidColorBrush(accent);
        }

        _zone.Place(rect, scaling);
    }

    private static void ShowGhost(Session s, PixelPoint screen)
    {
        if (_ghost == null)
        {
            _ghostIcon = new Icon { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _ghostText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            _ghost = new OverlayWindow(new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _ghostIcon, _ghostText } },
            })
            {
                SizeToContent = SizeToContent.WidthAndHeight,
            };
        }

        var p = ChromeStyles.Current;
        if (_ghost.Content is Border border)
        {
            border.Background = new SolidColorBrush(p.TabActive);
            border.BorderBrush = new SolidColorBrush(p.Outline);
        }

        _ghostText!.Foreground = new SolidColorBrush(p.Text);
        _ghostIcon!.Foreground = new SolidColorBrush(p.Text);
        _ghostText.Text = string.Join(", ", s.Panels.Select(PanelRegistry.Title));
        _ghostIcon.Value = PanelRegistry.Get(s.Active ?? s.Panels[0])?.Icon ?? IconNames.DotsVertical;
        _ghost.Position = new PixelPoint(screen.X + (int)(14 * s.Scaling), screen.Y + (int)(16 * s.Scaling));
        if (!_ghost.IsVisible)
        {
            _ghost.Show();
        }
    }

    /// <summary>A borderless, non-activating, topmost, transparent window used for drag feedback.</summary>
    private sealed class OverlayWindow : Window
    {
        public OverlayWindow(Control content)
        {
            WindowDecorations = WindowDecorations.None;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            Content = content;
        }

        public void Place(PixelRect rect, double scaling)
        {
            Position = rect.Position;
            Width = Math.Max(1, rect.Width / scaling);
            Height = Math.Max(1, rect.Height / scaling);
            if (!IsVisible)
            {
                Show();
            }
        }
    }
}
