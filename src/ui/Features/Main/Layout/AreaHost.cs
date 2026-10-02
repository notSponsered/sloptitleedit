using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>
/// Builds the main window content from the active workspace's area tree (replaces the 12 fixed
/// layouts), owns the floating area windows, and answers drag-and-drop hit tests. Structural changes
/// rebuild, like the old layout switch did; tab switches and splitter drags don't.
/// </summary>
public static class AreaHost
{
    private static MainView? _view;
    private static MainViewModel? _vm;
    private static readonly List<AreaView> _mainViews = [];
    private static readonly Dictionary<FloatingArea, AreaWindow> _windows = [];
    private static readonly List<AreaWindow> _windowOrder = []; // most recently activated first
    private static Border? _contentHost;
    private static AreaNode? _maximized;
    private static bool _workspacesChecked;

    public static AreaNode? ActiveLeaf { get; set; }
    public static bool IsMaximized => _maximized != null;

    public static void Init(MainView view, MainViewModel vm)
    {
        _view = view;
        _vm = vm;
        EnsureWorkspaces();
    }

    /// <summary>First run: migrate the old layout number into workspaces. Every start: repair hand-edited trees.</summary>
    public static void EnsureWorkspaces()
    {
        var appearance = Se.Settings.Appearance;
        if (_workspacesChecked && appearance.Workspaces.Count > 0)
        {
            return;
        }

        _workspacesChecked = true;
        appearance.Workspaces ??= [];
        appearance.Workspaces.RemoveAll(w => w == null);
        if (appearance.Workspaces.Count == 0)
        {
            appearance.Workspaces = AreaPresets.Defaults(Se.Settings.General.LayoutNumber, Se.Settings.General.UndockVideoControls);
            appearance.ActiveWorkspace = 0;
        }

        foreach (var ws in appearance.Workspaces)
        {
            AreaTree.Normalize(ws, PanelRegistry.Ids, () => AreaPresets.ForName(ws.Name, Se.Settings.General.LayoutNumber));
            if (!appearance.AssaToolbarAdded)
            {
                AreaTree.AddBar(ws, PanelIds.AssaTools); // once; after that hiding it sticks
            }
        }

        appearance.AssaToolbarAdded = true;

        // once: existing setups get the Project workspace (deleting it afterwards sticks)
        if (!appearance.ProjectWorkspaceAdded)
        {
            appearance.ProjectWorkspaceAdded = true;
            if (!appearance.Workspaces.Exists(w => w.Name == AreaPresets.Project))
            {
                var project = new Workspace { Name = AreaPresets.Project, Root = AreaPresets.ForName(AreaPresets.Project) };
                AreaTree.AddBar(project, PanelIds.AssaTools);
                appearance.Workspaces.Add(project);
            }
        }

        appearance.ActiveWorkspace = Math.Clamp(appearance.ActiveWorkspace, 0, appearance.Workspaces.Count - 1);
    }

    public static Workspace Active => Se.Settings.Appearance.Workspaces[Se.Settings.Appearance.ActiveWorkspace];

    public static bool ActiveHasVideo => AreaTree.Contains(Active, PanelIds.Video);

    public static bool IsVideoOrWaveformFloating =>
        AreaTree.FindFloating(Active, PanelIds.Video) != null || AreaTree.FindFloating(Active, PanelIds.Waveform) != null;

    /// <summary>The leaf is a whole window's only area (main or floating).</summary>
    public static bool IsWindowRoot(AreaNode leaf) => leaf == Active.Root || Active.Floating.Any(f => f.Root == leaf);

    // ---------------------------------------------------------------------
    // Building
    // ---------------------------------------------------------------------

    /// <summary>Rebuilds main window content and floating windows from the active workspace.</summary>
    public static void Rebuild()
    {
        if (_view == null || _vm == null)
        {
            return;
        }

        var ws = Active;
        var built = new HashSet<string>();
        _mainViews.Clear();

        // New content is built before the old is cleaned up: builders steal singletons (waveform,
        // video file/position) from their old parents, and cleanup would otherwise wipe them.
        SyncFloating(ws, built);

        if (_maximized != null && !AreaTree.Leaves(ws.Root).Contains(_maximized))
        {
            _maximized = null;
        }

        var host = new Border
        {
            Classes = { "se-gap", "se-host" }, // padding/edges come from the theme (flat: flush, cards: gaps)
            Child = BuildNode(_maximized ?? ws.Root, built, null, _mainViews, false, SplitDirection.None),
        };

        var hidden = new List<Control>();
        if (!built.Contains(PanelIds.Subtitles))
        {
            hidden.Add(new Border { IsVisible = false, Child = InitListViewAndEditBox.MakeSubtitleList(_view, _vm) });
        }

        if (!built.Contains(PanelIds.TextEdit))
        {
            hidden.Add(new Border { IsVisible = false, Child = InitListViewAndEditBox.MakeEditBox(_vm) });
        }

        if (!built.Contains(PanelIds.Video))
        {
            // Zero-size but "visible" keeps the player running for audio + waveform (old layout 6 trick).
            hidden.Add(new Border { Width = 0, Height = 0, ZIndex = -1000, Child = InitVideoPlayer.MakeLayoutVideoPlayer(_vm) });
        }

        if (!built.Contains(PanelIds.Waveform))
        {
            _vm.AudioVisualizer?.RemoveControlFromParent(); // keep its context menu out of the cleanup
        }

        InitLayout.CleanupOldContent(_vm.ContentGrid);
        _vm.ContentGrid.Children.Add(host);
        foreach (var control in hidden)
        {
            _vm.ContentGrid.Children.Add(control);
        }

        _contentHost = host;
        _vm.AreVideoControlsUndocked = IsVideoOrWaveformFloating;
        _vm.ShowAssaToolsBar = AreaTree.Contains(ws, PanelIds.AssaTools);
    }

    /// <param name="windowButtons">Pass true for a floating window's root: the area in its top-right corner gets the close button.</param>
    /// <param name="parentSplit">The split this node sits in (toolbars stack vertically in a left|right split).</param>
    private static Control BuildNode(AreaNode node, HashSet<string> built, AreaWindow? window, List<AreaView> views, bool windowButtons, SplitDirection parentSplit)
    {
        if (node.IsLeaf)
        {
            var view = new AreaView(node, window, windowButtons, parentSplit, _view!, _vm!, built);
            views.Add(view);
            return view;
        }

        var horizontal = node.Split == SplitDirection.LeftRight;
        var (first, second) = node.SizeMode switch
        {
            SizeMode.FixedSecond => (new GridLength(1, GridUnitType.Star), new GridLength(node.SecondPx, GridUnitType.Pixel)),
            SizeMode.AutoSecond => (new GridLength(1, GridUnitType.Star), GridLength.Auto),
            SizeMode.AutoFirst => (GridLength.Auto, new GridLength(1, GridUnitType.Star)),
            _ => (new GridLength(node.Ratio, GridUnitType.Star), new GridLength(1 - node.Ratio, GridUnitType.Star)),
        };

        // Top-right corner: the right child of a left|right split, the top child of a top/bottom split.
        var a = BuildNode(node.A!, built, window, views, windowButtons && !horizontal, node.Split);
        var b = BuildNode(node.B!, built, window, views, windowButtons && horizontal, node.Split);

        // A toolbar sizes to its content, so there is nothing to drag between it and its neighbour.
        var toolbarA = node.SizeMode == SizeMode.AutoFirst && IsCompactLeaf(node.A);
        var toolbarB = node.SizeMode == SizeMode.AutoSecond && IsCompactLeaf(node.B);
        var gap = ChromeStyles.Current.Gap;
        var min = horizontal ? 80.0 : 40.0;
        var grid = new Grid();
        if (horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(first) { MinWidth = toolbarA ? 0 : min });
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(second) { MinWidth = toolbarB ? 0 : min });
            Grid.SetColumn(b, 2);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(first) { MinHeight = toolbarA ? 0 : min });
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(second) { MinHeight = toolbarB ? 0 : min });
            Grid.SetRow(b, 2);
        }

        Control divider;
        if (toolbarA || toolbarB)
        {
            divider = new Border { Width = horizontal ? gap : double.NaN, Height = horizontal ? double.NaN : gap };
        }
        else
        {
            // The visible line is `gap` wide (1 px in the flat look) but the grab area is at least 5 px,
            // overlapping the neighbours' edges (it sits above them).
            var grab = Math.Max(gap, 5);
            var overhang = (grab - gap) / 2;
            var splitter = new GridSplitter
            {
                Classes = { "se-splitter" },
                ZIndex = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            if (horizontal)
            {
                splitter.Width = grab;
                splitter.Margin = new Thickness(-overhang, 0, -overhang, 0);
            }
            else
            {
                splitter.Height = grab;
                splitter.Margin = new Thickness(0, -overhang, 0, -overhang);
            }

            splitter.DragCompleted += (_, _) =>
            {
                var sizeA = horizontal ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight;
                var sizeB = horizontal ? grid.ColumnDefinitions[2].ActualWidth : grid.RowDefinitions[2].ActualHeight;
                if (sizeA + sizeB <= 0)
                {
                    return;
                }

                if (node.SizeMode == SizeMode.Ratio)
                {
                    node.Ratio = sizeA / (sizeA + sizeB);
                }
                else
                {
                    node.SizeMode = SizeMode.FixedSecond; // a dragged Auto edit box keeps its new height
                    node.SecondPx = sizeB;
                }
            };
            divider = splitter;
        }

        if (horizontal)
        {
            Grid.SetColumn(divider, 1);
        }
        else
        {
            Grid.SetRow(divider, 1);
        }

        grid.Children.Add(a);
        grid.Children.Add(divider);
        grid.Children.Add(b);
        return grid;
    }

    private static bool IsCompactLeaf(AreaNode? node) => node is { IsLeaf: true, Panels.Count: 1 } && PanelIds.IsCompact(node.Panels[0]);

    private static void SyncFloating(Workspace ws, HashSet<string> built)
    {
        foreach (var (area, window) in _windows.Where(p => !ws.Floating.Contains(p.Key)).ToList())
        {
            _windows.Remove(area);
            _windowOrder.Remove(window);
            window.AllowClose = true;
            window.Close();
        }

        foreach (var area in ws.Floating)
        {
            var signature = Signature(area.Root);
            if (!_windows.TryGetValue(area, out var window))
            {
                window = new AreaWindow(area, _vm!);
                var w = window;
                window.Activated += (_, _) =>
                {
                    _windowOrder.Remove(w);
                    _windowOrder.Insert(0, w);
                };
                _windows[area] = window;
                _windowOrder.Insert(0, window);
                Fill(window, area, built, signature);
                window.Show();
                if (_vm!.Window != null)
                {
                    WindowService.KeepTopmostWhileOwnerActive(window, _vm.Window);
                }
            }
            else if (window.Signature != signature)
            {
                Fill(window, area, built, signature);
            }
            else
            {
                foreach (var view in window.Views)
                {
                    view.UpdateActiveTab();
                }

                foreach (var id in AreaTree.PanelsIn(area.Root))
                {
                    built.Add(id);
                }
            }
        }
    }

    private static void Fill(AreaWindow window, FloatingArea area, HashSet<string> built, string signature)
    {
        var views = new List<AreaView>();
        var tree = BuildNode(area.Root, built, window, views, true, SplitDirection.None);
        window.SetContent(tree, views, signature);
    }

    /// <summary>Structure only (splits + tab order), so ratio changes and tab switches don't rebuild a window.</summary>
    private static string Signature(AreaNode node) => node.IsLeaf
        ? "[" + string.Join(",", node.Panels) + "]"
        : "(" + (node.Split == SplitDirection.LeftRight ? "H" : "V") + Signature(node.A!) + Signature(node.B!) + ")";

    // ---------------------------------------------------------------------
    // Drag & drop
    // ---------------------------------------------------------------------

    internal sealed record DropTarget(AreaNode Node, DropZone Zone, PixelRect Preview);

    /// <param name="OverWindow">The pointer is over one of SE's windows (so releasing must not create a new window).</param>
    internal readonly record struct HitResult(DropTarget? Target, bool OverWindow);

    /// <summary>Where would <paramref name="payload"/> land if dropped at <paramref name="screen"/>?</summary>
    internal static HitResult HitTest(PixelPoint screen, IReadOnlyList<string> payload, AreaWindow? exclude)
    {
        // Floating windows float above the main window; the most recently used one is on top.
        foreach (var window in _windowOrder)
        {
            if (window == exclude || !window.IsVisible || window.Host == null || !ScreenRect(window).Contains(screen))
            {
                continue;
            }

            return new HitResult(HitTestIn(window.Host, window.Area.Root, window.Views, screen, payload), true);
        }

        var main = _vm?.Window;
        if (main == null || main == exclude || !ScreenRect(main).Contains(screen))
        {
            return new HitResult(null, false);
        }

        var target = _contentHost != null && ScreenRect(_contentHost).Contains(screen)
            ? HitTestIn(_contentHost, _maximized == null ? Active.Root : null, _mainViews, screen, payload)
            : null;
        return new HitResult(target, true);
    }

    private static DropTarget? HitTestIn(Control host, AreaNode? root, IReadOnlyList<AreaView> views, PixelPoint screen, IReadOnlyList<string> payload)
    {
        var scale = TopLevel.GetTopLevel(host)?.RenderScaling ?? 1;
        var hostRect = ScreenRect(host);

        // Toolbars only ever dock to a side (never as a tab) and take a bar's thickness, not half an area.
        var toolbarPayload = payload.Count > 0 && payload.All(PanelIds.IsCompact);
        var barThickness = (int)(44 * scale);

        // Window edges: a full-height column / full-width row.
        if (root != null && !root.IsLeaf && !AreaTree.PanelsIn(root).All(payload.Contains))
        {
            var band = (int)(14 * scale);
            var stripW = toolbarPayload ? barThickness : hostRect.Width * 3 / 10; // matches the 30% column AreaTree.Move creates
            var stripH = toolbarPayload ? barThickness : hostRect.Height * 3 / 10;
            if (screen.X - hostRect.X < band)
            {
                return new DropTarget(root, DropZone.Left, new PixelRect(hostRect.X, hostRect.Y, stripW, hostRect.Height));
            }

            if (hostRect.Right - screen.X < band)
            {
                return new DropTarget(root, DropZone.Right, new PixelRect(hostRect.Right - stripW, hostRect.Y, stripW, hostRect.Height));
            }

            if (hostRect.Bottom - screen.Y < band)
            {
                return new DropTarget(root, DropZone.Bottom, new PixelRect(hostRect.X, hostRect.Bottom - stripH, hostRect.Width, stripH));
            }
        }

        var slack = (int)Math.Ceiling(Math.Max(ChromeStyles.Current.Gap, 4) * scale); // the gaps between areas count as the nearest area
        var view = views.FirstOrDefault(v => Inflate(ScreenRect(v), slack).Contains(screen));
        if (view == null || view.Leaf.Panels.Count > 0 && view.Leaf.Panels.All(payload.Contains))
        {
            return null; // nothing changes when an area is dropped onto itself
        }

        var r = ScreenRect(view);
        var sidesOnly = toolbarPayload || view.IsCompact;
        DropZone zone;
        if (!sidesOnly && view.Header != null && screen.Y < r.Y + ChromeStyles.HeaderHeight * scale)
        {
            zone = DropZone.Tab;
        }
        else
        {
            var fx = (screen.X - r.X) / (double)Math.Max(1, r.Width);
            var fy = (screen.Y - r.Y) / (double)Math.Max(1, r.Height);
            var edges = new[] { (DropZone.Left, fx), (DropZone.Right, 1 - fx), (DropZone.Top, fy), (DropZone.Bottom, 1 - fy) };
            var (nearest, distance) = edges.MinBy(e => e.Item2);
            zone = sidesOnly || distance < 0.3 ? nearest : DropZone.Tab;
        }

        if (zone == DropZone.Tab && payload.All(view.Leaf.Panels.Contains))
        {
            return null; // already a tab here
        }

        var w = toolbarPayload ? Math.Min(barThickness, r.Width) : r.Width / 2;
        var h = toolbarPayload ? Math.Min(barThickness, r.Height) : r.Height / 2;
        var preview = zone switch
        {
            DropZone.Left => new PixelRect(r.X, r.Y, w, r.Height),
            DropZone.Right => new PixelRect(r.Right - w, r.Y, w, r.Height),
            DropZone.Top => new PixelRect(r.X, r.Y, r.Width, h),
            DropZone.Bottom => new PixelRect(r.X, r.Bottom - h, r.Width, h),
            _ => r,
        };
        return new DropTarget(view.Leaf, zone, preview);
    }

    private static PixelRect ScreenRect(Visual visual)
    {
        if (TopLevel.GetTopLevel(visual) == null)
        {
            return default;
        }

        var topLeft = visual.PointToScreen(new Point(0, 0));
        var bottomRight = visual.PointToScreen(new Point(visual.Bounds.Width, visual.Bounds.Height));
        return new PixelRect(topLeft, new PixelSize(Math.Max(0, bottomRight.X - topLeft.X), Math.Max(0, bottomRight.Y - topLeft.Y)));
    }

    private static PixelRect Inflate(PixelRect r, int d) => new(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);

    internal static void ApplyMove(IReadOnlyList<string> panels, string? active, DropTarget target)
    {
        if (AreaTree.Move(Active, panels, active, target.Node, target.Zone))
        {
            Changed();
        }
    }

    internal static void ApplyWindowDock(FloatingArea area, DropTarget target)
    {
        if (_windows.TryGetValue(area, out var window))
        {
            window.SaveBounds();
        }

        if (AreaTree.DockWindowAt(Active, area, target.Node, target.Zone))
        {
            Changed();
        }
    }

    internal static void ApplyFloat(IReadOnlyList<string> panels, string? active, PixelRect rect, double scaling)
    {
        var floating = AreaTree.Float(Active, panels, active);
        if (floating == null)
        {
            return;
        }

        floating.X = rect.X;
        floating.Y = rect.Y;
        floating.Width = rect.Width / scaling;
        floating.Height = rect.Height / scaling;
        Changed();
    }

    // ---------------------------------------------------------------------
    // Area actions (all rebuild through MainViewModel so selection/preview are restored)
    // ---------------------------------------------------------------------

    private static void Changed() => _vm?.RebuildLayout();

    public static void AddTab(AreaNode leaf, string id)
    {
        AreaTree.AddTab(Active, leaf, id);
        Changed();
    }

    /// <summary>Window → Add panel: a new tab in the last clicked area (or the first area).</summary>
    public static void AddPanelToActiveArea(string id)
    {
        var ws = Active;
        var target = ActiveLeaf != null && AreaTree.RootOf(ws, ActiveLeaf) != null
            ? ActiveLeaf
            : AreaTree.Leaves(ws.Root).First();
        AddTab(target, id);
    }

    public static void Split(AreaNode leaf, DockSide side, string id)
    {
        AreaTree.Split(Active, leaf, side, id);
        Changed();
    }

    public static void ClosePanel(string id)
    {
        if (AreaTree.RemovePanel(Active, id))
        {
            Changed();
        }
    }

    public static void CloseLeaf(AreaNode leaf)
    {
        if (AreaTree.CloseLeaf(Active, leaf))
        {
            Changed();
        }
    }

    public static void DetachPanel(string id) => Place(AreaTree.Detach(Active, id));

    public static void DetachLeaf(AreaNode leaf) => Place(AreaTree.DetachLeaf(Active, leaf));

    private static void Place(FloatingArea? floating)
    {
        if (floating == null)
        {
            return;
        }

        var size = DockDrag.DefaultFloatingSize(AreaTree.PanelsIn(floating.Root));
        floating.Width = size.Width;
        floating.Height = size.Height;
        if (_vm?.Window is { } main)
        {
            floating.X = main.Position.X + 80 + 30 * Active.Floating.Count;
            floating.Y = main.Position.Y + 80 + 30 * Active.Floating.Count;
        }

        Changed();
    }

    public static void Dock(FloatingArea floating)
    {
        if (_windows.TryGetValue(floating, out var window))
        {
            window.SaveBounds();
        }

        AreaTree.Dock(Active, floating);
        Changed();
    }

    public static void ToggleMaximize(AreaNode? leaf)
    {
        _maximized = _maximized == null ? leaf ?? ActiveLeaf : null;
        Changed();
    }

    public static void LoadPreset(int legacyLayoutNumber)
    {
        var hadBar = AreaTree.Contains(Active, PanelIds.AssaTools);
        Active.Root = AreaPresets.Legacy(legacyLayoutNumber);
        Active.Floating.Clear();
        if (hadBar)
        {
            AreaTree.AddBar(Active, PanelIds.AssaTools);
        }

        _maximized = null;
        Changed();
    }

    /// <summary>Window → ASSA tools bar: show it along the bottom, or hide it wherever it is.</summary>
    public static void ToggleBar(string id)
    {
        if (AreaTree.Contains(Active, id))
        {
            AreaTree.RemovePanel(Active, id);
        }
        else
        {
            AreaTree.AddBar(Active, id);
        }

        Changed();
    }

    public static bool ActiveHas(string id) => AreaTree.Contains(Active, id);

    public static void DetachVideoControls()
    {
        foreach (var id in new[] { PanelIds.Video, PanelIds.Waveform })
        {
            if (AreaTree.Contains(Active.Root, id))
            {
                Place(AreaTree.Detach(Active, id));
            }
        }
    }

    public static void DockVideoControls()
    {
        foreach (var floating in Active.Floating.Where(f => AreaTree.Contains(f.Root, PanelIds.Video) || AreaTree.Contains(f.Root, PanelIds.Waveform)).ToList())
        {
            if (_windows.TryGetValue(floating, out var window))
            {
                window.SaveBounds();
            }

            AreaTree.Dock(Active, floating);
        }

        Changed();
    }

    // ---------------------------------------------------------------------
    // Lifetime
    // ---------------------------------------------------------------------

    public static void SaveFloatingBounds()
    {
        foreach (var window in _windows.Values)
        {
            window.SaveBounds();
        }
    }

    public static void CloseAllFloating()
    {
        SaveFloatingBounds();
        foreach (var window in _windows.Values.ToList())
        {
            window.AllowClose = true;
            window.Close();
        }

        _windows.Clear();
        _windowOrder.Clear();
    }
}
