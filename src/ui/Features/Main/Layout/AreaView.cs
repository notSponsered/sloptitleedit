using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using Optris.Icons.Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using MenuItem = Avalonia.Controls.MenuItem;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>
/// One Blender-style area: a rounded panel with a header strip (tab chips, "+", area menu) and a body
/// holding every tab's content (inactive tabs are hidden, not destroyed). Tabs and the header's empty
/// space are drag handles (see <see cref="DockDrag"/>); in a floating window the header moves the window.
/// </summary>
internal sealed class AreaView : Border
{
    public AreaNode Leaf { get; }
    public AreaWindow? Window { get; }
    public FloatingArea? Floating => Window?.Area;
    public Border? Header { get; }

    private readonly MainViewModel _vm;
    private readonly Grid _body = new();
    private readonly Dictionary<string, Control> _contents = new();
    private readonly Dictionary<string, Border> _chips = new();

    /// <summary>A toolbar area (one compact panel): a grip instead of a header, sized to its content.</summary>
    public bool IsCompact { get; }

    /// <param name="windowButtons">This area sits in the floating window's top-right corner and carries its close button.</param>
    /// <param name="parentSplit">How the parent split lays this area out; a toolbar in a left|right split stacks vertically.</param>
    public AreaView(AreaNode leaf, AreaWindow? window, bool windowButtons, SplitDirection parentSplit, MainView view, MainViewModel vm, HashSet<string> built)
    {
        Leaf = leaf;
        Window = window;
        _vm = vm;
        IsCompact = leaf.Panels.Count == 1 && PanelIds.IsCompact(leaf.Panels[0]);
        Classes.Add("se-area");
        ClipToBounds = true;

        foreach (var id in leaf.Panels)
        {
            var info = PanelRegistry.Get(id);
            if (info == null || !built.Add(id))
            {
                continue;
            }

            var content = info.Make(view, vm);
            if (info.MinWidth > 0)
            {
                // Narrow areas scroll sideways instead of overlapping the panel's own layout. The content is
                // given the viewport's width (at least MinWidth): a sideways ScrollViewer measures with infinite
                // width, so text wouldn't wrap and star columns wouldn't follow the area's size.
                var inner = content;
                var minWidth = info.MinWidth;
                inner.Width = minWidth;
                var scroller = new ScrollViewer
                {
                    Content = inner,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                };
                scroller.SizeChanged += (_, e) => inner.Width = Math.Max(minWidth, e.NewSize.Width - inner.Margin.Left - inner.Margin.Right);
                content = scroller;
            }

            _contents[id] = content;
            _body.Children.Add(content);
        }

        var dock = new DockPanel();
        if (IsCompact)
        {
            var vertical = parentSplit == SplitDirection.LeftRight;
            foreach (var content in _contents.Values)
            {
                if (content is Panels.AssaToolbar toolbar)
                {
                    toolbar.SetOrientation(vertical ? Orientation.Vertical : Orientation.Horizontal);
                }
            }

            var grip = MakeGrip(vertical);
            DockPanel.SetDock(grip, vertical ? Dock.Top : Dock.Left);
            dock.Children.Add(grip);
            Padding = new Thickness(3);
        }
        else if (Se.Settings.Appearance.ShowAreaHeaders || window != null)
        {
            Header = MakeHeader(windowButtons);
            DockPanel.SetDock(Header, Dock.Top);
            dock.Children.Add(Header);
        }

        dock.Children.Add(_body);
        Child = dock;
        UpdateActiveTab();

        AddHandler(PointerPressedEvent, (_, _) => AreaHost.ActiveLeaf = Leaf, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>A toolbar's handle: drag to move it anywhere (or out into a window), right-click to hide it.</summary>
    private Border MakeGrip(bool vertical)
    {
        var id = Leaf.Panels[0];
        var grip = new Border
        {
            Classes = { "se-grip" },
            Width = vertical ? double.NaN : 14,
            Height = vertical ? 14 : double.NaN,
            Margin = vertical ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 2, 0),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new Icon
            {
                Value = vertical ? "mdi-drag-horizontal" : "mdi-drag-vertical",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTip.SetTip(grip, PanelRegistry.Title(id));

        grip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                DockDrag.Begin(grip, e, Leaf.Panels.ToList(), id, null, Window?.WindowState == WindowState.Normal ? Window : null);
            }
        };

        var l = Se.Language.Workspace;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem { Header = l.HideBar, Command = new RelayCommand(() => AreaHost.ClosePanel(id)) });
        menu.Items.Add(Window == null
            ? new MenuItem { Header = l.OpenInNewWindow, Command = new RelayCommand(() => AreaHost.DetachPanel(id)) }
            : new MenuItem { Header = l.DockBack, Command = new RelayCommand(() => AreaHost.Dock(Window.Area)) });
        grip.ContextFlyout = menu;
        return grip;
    }

    public bool Hosts(string id) => _contents.ContainsKey(id);

    public void UpdateActiveTab()
    {
        var active = Leaf.ActivePanel;
        foreach (var (id, content) in _contents)
        {
            content.IsVisible = id == active;
        }

        foreach (var (id, chip) in _chips)
        {
            chip.Classes.Set("active", id == active);
        }
    }

    private void Activate(string id)
    {
        Leaf.Active = Math.Max(0, Leaf.Panels.IndexOf(id));
        UpdateActiveTab();
    }

    private Border MakeHeader(bool windowButtons)
    {
        var l = Se.Language.Workspace;
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 }; // stretches: underline tabs sit on the header edge
        foreach (var id in Leaf.Panels.Where(_contents.ContainsKey))
        {
            tabs.Children.Add(MakeChip(id));
        }

        // Filled just before showing: a MenuFlyout with no items never opens (so its Opening event never fires).
        var addMenu = new MenuFlyout();
        var add = MakeIconButton(IconNames.Plus, l.AddPanel, addMenu, () => FillAddMenu(addMenu));

        var areaMenu = new MenuFlyout();
        var more = MakeIconButton(IconNames.DotsVertical, l.AreaMenu, areaMenu, () => FillAreaMenu(areaMenu));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(4, 0, 4, 0) };
        grid.Children.Add(tabs);
        Grid.SetColumn(add, 1);
        grid.Children.Add(add);
        Grid.SetColumn(more, 3);
        grid.Children.Add(more);
        if (windowButtons && Window != null)
        {
            var close = MakeIconButton(IconNames.Close, Se.Language.Workspace.DockBack, null,
                () => Dispatcher.UIThread.Post(() => AreaHost.Dock(Window.Area)));
            close.Margin = new Thickness(2, 0, 0, 0);
            Grid.SetColumn(close, 4);
            grid.Children.Add(close);
        }

        var header = new Border
        {
            Classes = { "se-area-header" },
            Height = ChromeStyles.HeaderHeight,
            Child = grid,
        };

        // Empty header space: drag = move the area (main window) or the whole window (floating);
        // double-click = maximize/restore.
        header.PointerPressed += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                e.Handled = true;
                if (Window != null)
                {
                    Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
                else
                {
                    Dispatcher.UIThread.Post(() => AreaHost.ToggleMaximize(Leaf));
                }

                return;
            }

            DockDrag.Begin(header, e, Leaf.Panels.ToList(), Leaf.ActivePanel, null, Window?.WindowState == WindowState.Normal ? Window : null);
        };

        return header;
    }

    private Border MakeChip(string id)
    {
        var info = PanelRegistry.Get(id)!;
        var chip = new Border
        {
            Classes = { "se-tab" },
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 5,
                Children =
                {
                    new Icon { Value = info.Icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Classes = { ChromeStyles.TintClass(id) } },
                    new TextBlock { Text = info.Title(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

        chip.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(chip).Properties;
            if (props.IsMiddleButtonPressed)
            {
                e.Handled = true;
                Dispatcher.UIThread.Post(() => AreaHost.ClosePanel(id));
            }
            else if (props.IsLeftButtonPressed)
            {
                DockDrag.Begin(chip, e, [id], id, () => Activate(id), null);
            }
        };

        var l = Se.Language.Workspace;
        var chipMenu = new MenuFlyout();
        chipMenu.Items.Add(new MenuItem { Header = l.OpenInNewWindow, Command = new RelayCommand(() => AreaHost.DetachPanel(id)) });
        chipMenu.Items.Add(new MenuItem { Header = l.CloseTab, Command = new RelayCommand(() => AreaHost.ClosePanel(id)) });
        chip.ContextFlyout = chipMenu;

        _chips[id] = chip;
        return chip;
    }

    /// <summary>Flat icon button (a Border, so the chrome style sheet owns its look).</summary>
    private static Border MakeIconButton(string icon, string tip, FlyoutBase? flyout, Action? click)
    {
        var button = new Border
        {
            Classes = { "se-hbtn" },
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Icon { Value = icon, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(button, tip);
        if (flyout != null)
        {
            FlyoutBase.SetAttachedFlyout(button, flyout);
        }

        button.PointerPressed += (_, e) => e.Handled = true; // a button, not a header drag
        button.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            click?.Invoke(); // runs first: for a menu button it fills the menu
            if (flyout != null)
            {
                FlyoutBase.ShowAttachedFlyout(button);
            }
        };
        return button;
    }

    private void FillAddMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        foreach (var info in PanelRegistry.All.Where(p => !Leaf.Panels.Contains(p.Id) && !PanelIds.IsCompact(p.Id)))
        {
            menu.Items.Add(new MenuItem
            {
                Header = info.Title(),
                Icon = new Icon { Value = info.Icon, Classes = { ChromeStyles.TintClass(info.Id) } },
                IsEnabled = !info.AssOnly || _vm.IsFormatAssa || _vm.IsFormatSsa,
                Command = new RelayCommand(() => AreaHost.AddTab(Leaf, info.Id)),
            });
        }
    }

    private void FillAreaMenu(MenuFlyout menu)
    {
        var l = Se.Language.Workspace;
        menu.Items.Clear();
        menu.Items.Add(MakeSplitMenu(l.SplitLeft, DockSide.Left));
        menu.Items.Add(MakeSplitMenu(l.SplitRight, DockSide.Right));
        menu.Items.Add(MakeSplitMenu(l.SplitUp, DockSide.Top));
        menu.Items.Add(MakeSplitMenu(l.SplitDown, DockSide.Bottom));
        menu.Items.Add(new Separator());
        if (Window == null)
        {
            menu.Items.Add(new MenuItem
            {
                Header = AreaHost.IsMaximized ? l.RestoreArea : l.MaximizeArea,
                Command = new RelayCommand(() => AreaHost.ToggleMaximize(Leaf)),
            });
        }
        else
        {
            menu.Items.Add(new MenuItem
            {
                Header = Se.Language.Workspace.DockBack,
                Command = new RelayCommand(() => AreaHost.Dock(Window.Area)),
            });
        }

        menu.Items.Add(new MenuItem
        {
            Header = l.OpenInNewWindow,
            IsEnabled = !AreaHost.IsWindowRoot(Leaf),
            Command = new RelayCommand(() => AreaHost.DetachLeaf(Leaf)),
        });
        menu.Items.Add(new MenuItem
        {
            Header = l.CloseArea,
            IsEnabled = Window != null || !AreaHost.IsWindowRoot(Leaf),
            Command = new RelayCommand(() => AreaHost.CloseLeaf(Leaf)),
        });
    }

    private MenuItem MakeSplitMenu(string header, DockSide side)
    {
        var item = new MenuItem { Header = header };
        foreach (var info in PanelRegistry.All)
        {
            item.Items.Add(new MenuItem
            {
                Header = info.Title(),
                Icon = new Icon { Value = info.Icon, Classes = { ChromeStyles.TintClass(info.Id) } },
                IsEnabled = (!info.AssOnly || _vm.IsFormatAssa || _vm.IsFormatSsa) &&
                            !(Leaf.Panels.Count == 1 && Leaf.Panels[0] == info.Id),
                Command = new RelayCommand(() => AreaHost.Split(Leaf, side, info.Id)),
            });
        }

        return item;
    }
}
