using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using Optris.Icons.Avalonia;
using MenuItem = Avalonia.Controls.MenuItem;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>Blender-style workspace tabs shown at the right of the menu bar.</summary>
internal static class WorkspaceBar
{
    private static StackPanel? _panel;

    public static Control Make(MainViewModel vm)
    {
        _panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        Refresh(vm);
        return _panel;
    }

    public static void Refresh(MainViewModel vm)
    {
        if (_panel == null)
        {
            return;
        }

        AreaHost.EnsureWorkspaces();
        _panel.Children.Clear();
        var l = Se.Language.Workspace;
        var list = Se.Settings.Appearance.Workspaces;
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            var tab = new Border
            {
                Classes = { "se-ws-tab" },
                Padding = new Thickness(12, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = list[i].Name, FontSize = 12.5 },
            };
            tab.Classes.Set("active", i == Se.Settings.Appearance.ActiveWorkspace);
            tab.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    vm.SwitchWorkspace(index);
                }
            };
            tab.ContextFlyout = new MenuFlyout
            {
                Items =
                {
                    new MenuItem { Header = l.RenameWorkspace, Command = new AsyncRelayCommand(() => vm.RenameWorkspace(index)) },
                    new MenuItem { Header = l.DuplicateWorkspace, Command = new RelayCommand(() => vm.DuplicateWorkspace(index)) },
                    new MenuItem { Header = l.ResetWorkspace, Command = new RelayCommand(() => vm.ResetWorkspace(index)) },
                    new Separator(),
                    new MenuItem { Header = l.DeleteWorkspace, IsEnabled = list.Count > 1, Command = new RelayCommand(() => vm.DeleteWorkspace(index)) },
                },
            };
            _panel.Children.Add(tab);
        }

        var add = new Border
        {
            Classes = { "se-hbtn" },
            Width = 22,
            Height = 22,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Icon { Value = IconNames.Plus, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        FlyoutBase.SetAttachedFlyout(add, MakeNewMenu(vm));
        add.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                FlyoutBase.ShowAttachedFlyout(add);
            }
        };
        ToolTip.SetTip(add, l.NewWorkspace);
        _panel.Children.Add(add);
    }

    /// <summary>"New workspace" choices; also used by the Window menu.</summary>
    public static MenuFlyout MakeNewMenu(MainViewModel vm)
    {
        var menu = new MenuFlyout();
        foreach (var item in MakeNewMenuItems(vm))
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    public static Control[] MakeNewMenuItems(MainViewModel vm)
    {
        var l = Se.Language.Workspace;
        var legacy = new MenuItem { Header = Se.Language.Workspace.LoadLayoutPreset.Replace("...", string.Empty) };
        for (var n = 1; n <= AreaPresets.LegacyCount; n++)
        {
            var number = n;
            legacy.Items.Add(new MenuItem { Header = string.Format(l.LayoutX, n), Command = new RelayCommand(() => vm.AddPresetWorkspace(string.Empty, number)) });
        }

        return
        [
            new MenuItem { Header = l.DuplicateWorkspace, Command = new RelayCommand(() => vm.DuplicateWorkspace(Se.Settings.Appearance.ActiveWorkspace)) },
            new Separator(),
            new MenuItem { Header = AreaPresets.Edit, Command = new RelayCommand(() => vm.AddPresetWorkspace(AreaPresets.Edit, 0)) },
            new MenuItem { Header = AreaPresets.Timing, Command = new RelayCommand(() => vm.AddPresetWorkspace(AreaPresets.Timing, 0)) },
            new MenuItem { Header = AreaPresets.Typeset, Command = new RelayCommand(() => vm.AddPresetWorkspace(AreaPresets.Typeset, 0)) },
            new MenuItem { Header = AreaPresets.Project, Command = new RelayCommand(() => vm.AddPresetWorkspace(AreaPresets.Project, 0)) },
            legacy,
        ];
    }
}
