using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Controls.VideoPlayer;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.UiLogic.Layout;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>
/// A floating window holding an area tree. No OS title bar: the area headers are the title bar
/// (drag = move with docking, double-click = maximize, × = put back). Keys go to the main window's
/// shortcut handler; closing it (Alt+F4 too) puts the areas back where they came from. Hosts the
/// video's fullscreen toggle when the video is in it.
/// </summary>
internal sealed class AreaWindow : Window
{
    private const int MouseMovementMinPixels = 20;
    private const double GripSize = 6;

    public FloatingArea Area { get; }
    public Border Host { get; }
    public IReadOnlyList<AreaView> Views { get; private set; } = [];
    public string Signature { get; private set; } = string.Empty;
    public bool AllowClose { get; set; }

    private readonly MainViewModel _vm;
    private DispatcherTimer? _activityTimer;
    private (int X, int Y) _lastCursor;

    public AreaWindow(FloatingArea area, MainViewModel vm)
    {
        Area = area;
        _vm = vm;
        UiUtil.InitializeWindow(this, nameof(AreaWindow));
        WindowDecorations = WindowDecorations.BorderOnly;
        CanResize = true;
        var panels = AreaTree.PanelsIn(area.Root);
        var toolbar = panels.Count > 0 && panels.All(PanelIds.IsCompact);
        MinWidth = toolbar ? 48 : 220;
        MinHeight = toolbar ? 40 : 120;

        var size = DockDrag.DefaultFloatingSize(panels);
        Width = area.Width > 0 ? area.Width : size.Width;
        Height = area.Height > 0 ? area.Height : size.Height;
        if (toolbar && area.Width <= 0)
        {
            SizeToContent = SizeToContent.WidthAndHeight; // a new floating toolbar fits its buttons
        }
        if (area.Width > 0 || area.X != 0 || area.Y != 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)area.X, (int)area.Y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        if (area.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        Host = new Border { Classes = { "se-gap", "se-host-float" } };
        var root = new Grid { Children = { Host } };
        AddResizeGrips(root);
        Content = root;

        KeyDown += OnKeyDown;
        KeyUp += (s, e) => _vm.OnKeyUpHandler(s, e);
        Closing += OnClosing;
        Closed += (_, _) => _activityTimer?.Stop();
    }

    public static string TitleOf(FloatingArea area) => string.Join(" | ", AreaTree.PanelsIn(area.Root).Select(PanelRegistry.Title));

    public void SetContent(Control tree, List<AreaView> views, string signature)
    {
        Host.Child = tree;
        Views = views;
        Signature = signature;
        Title = TitleOf(Area);

        var video = VideoHere();
        if (video != null)
        {
            video.FullScreenCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleFullScreen);
            video.FullscreenCollapseRequested += ToggleFullScreen;
            StartActivityTimer(video);
        }
        else
        {
            _activityTimer?.Stop();
        }
    }

    public void SaveBounds()
    {
        Area.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            Area.X = Position.X;
            Area.Y = Position.Y;
            Area.Width = Width;
            Area.Height = Height;
        }
    }

    /// <summary>Invisible edge/corner strips that start an OS resize (the window has no system frame to grab).</summary>
    private void AddResizeGrips(Grid root)
    {
        void Grip(WindowEdge edge, HorizontalAlignment h, VerticalAlignment v, StandardCursorType cursor, double w, double hgt)
        {
            var grip = new Border
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = h,
                VerticalAlignment = v,
                Width = w,
                Height = hgt,
                Cursor = new Cursor(cursor),
                ZIndex = 100,
            };
            grip.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed && WindowState == WindowState.Normal)
                {
                    e.Handled = true;
                    BeginResizeDrag(edge, e);
                }
            };
            root.Children.Add(grip);
        }

        var n = double.NaN;
        var corner = GripSize * 2;
        Grip(WindowEdge.West, HorizontalAlignment.Left, VerticalAlignment.Stretch, StandardCursorType.LeftSide, GripSize, n);
        Grip(WindowEdge.East, HorizontalAlignment.Right, VerticalAlignment.Stretch, StandardCursorType.RightSide, GripSize, n);
        Grip(WindowEdge.North, HorizontalAlignment.Stretch, VerticalAlignment.Top, StandardCursorType.TopSide, n, GripSize / 2);
        Grip(WindowEdge.South, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, StandardCursorType.BottomSide, n, GripSize);
        Grip(WindowEdge.NorthWest, HorizontalAlignment.Left, VerticalAlignment.Top, StandardCursorType.TopLeftCorner, corner, GripSize);
        Grip(WindowEdge.NorthEast, HorizontalAlignment.Right, VerticalAlignment.Top, StandardCursorType.TopRightCorner, corner, GripSize);
        Grip(WindowEdge.SouthWest, HorizontalAlignment.Left, VerticalAlignment.Bottom, StandardCursorType.BottomLeftCorner, corner, corner);
        Grip(WindowEdge.SouthEast, HorizontalAlignment.Right, VerticalAlignment.Bottom, StandardCursorType.BottomRightCorner, corner, corner);
    }

    private VideoPlayerControl? VideoHere() =>
        Views.Any(v => v.Hosts(PanelIds.Video)) ? _vm.VideoPlayerControl : null;

    private void ToggleFullScreen()
    {
        var video = VideoHere();
        var fullScreen = WindowState != WindowState.FullScreen;
        WindowState = fullScreen ? WindowState.FullScreen : WindowState.Normal;
        if (video != null)
        {
            video.IsFullScreen = fullScreen;
            video.NotifyUserActivity();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveBounds();
        if (!AllowClose && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            // Alt+F4 / taskbar close: put the areas back into the main window instead of losing them.
            e.Cancel = true;
            Dispatcher.UIThread.Post(() => AreaHost.Dock(Area));
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var video = VideoHere();
        if (video != null && (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Key == Key.F11 ||
                              e.Key == Key.Escape && WindowState == WindowState.FullScreen))
        {
            e.Handled = true;
            ToggleFullScreen();
            return;
        }

        // The main handler only knows the main window's focus; plain typing in a text box here must stay typing.
        var focused = FocusManager?.GetFocusedElement();
        if ((focused is TextBox || focused?.GetType().Name.Contains("TextEditor") == true || focused?.GetType().Name.Contains("TextArea") == true) &&
            e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            return;
        }

        _vm.OnKeyDownHandler(sender, e);
        if (e.Handled)
        {
            return;
        }

        // Plain player keys, but only where they can't be typing (a video/waveform tab is showing).
        var player = _vm.GetVideoPlayerControl();
        var showsPlayer = Views.Any(v => v.Leaf.ActivePanel is PanelIds.Video or PanelIds.Waveform);
        if (player == null || !showsPlayer || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space: player.TogglePlayPause(); break;
            case Key.Right: player.Position += 2; break;
            case Key.Left: player.Position -= 2; break;
            case Key.Up: player.Volume += 2; break;
            case Key.Down: player.Volume -= 2; break;
            default: return;
        }

        e.Handled = true;
        player.NotifyUserActivity();
    }

    /// <summary>Polls the OS cursor so fullscreen controls auto-hide/show even over the native video surface.</summary>
    private void StartActivityTimer(VideoPlayerControl video)
    {
        _activityTimer?.Stop();
        video.IsFullScreen = WindowState == WindowState.FullScreen;
        _activityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _activityTimer.Tick += (_, _) =>
        {
            try
            {
                var cursor = CursorPositionHelper.GetCursorPosition();
                if (cursor.HasValue &&
                    (Math.Abs(cursor.Value.X - _lastCursor.X) > MouseMovementMinPixels ||
                     Math.Abs(cursor.Value.Y - _lastCursor.Y) > MouseMovementMinPixels))
                {
                    _lastCursor = cursor.Value;
                    video.NotifyUserActivity();
                }
            }
            catch
            {
                // ignore platform cursor errors
            }
        };
        _activityTimer.Start();
    }
}
