using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.Features.Shared.PromptTextBox;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>Workspaces (Blender-style saved area arrangements) and area commands.</summary>
public partial class MainViewModel
{
    [ObservableProperty] private bool _showAreaHeaders = Se.Settings.Appearance.ShowAreaHeaders;
    [ObservableProperty] private bool _showMainToolbar = Se.Settings.Appearance.ShowMainToolbar;
    [ObservableProperty] private bool _showAssaToolsBar; // mirrors "the active workspace has the bar"; set on every rebuild

    [RelayCommand]
    private void ToggleMainToolbar()
    {
        ShowMainToolbar = !ShowMainToolbar;
        Se.Settings.Appearance.ShowMainToolbar = ShowMainToolbar;
        UpdateToolbarSeparator();
    }

    [RelayCommand]
    private void ToggleAssaToolsBar() => AreaHost.ToggleBar(PanelIds.AssaTools);

    internal void UpdateToolbarSeparator()
    {
        if (ToolbarTopSeparator != null)
        {
            ToolbarTopSeparator.IsVisible = ShowMainToolbar && Se.Settings.Appearance.ShowHorizontalLineAboveToolbar;
        }
    }

    [RelayCommand]
    private void NextWorkspace() => SwitchWorkspace(Se.Settings.Appearance.ActiveWorkspace + 1);

    [RelayCommand]
    private void PreviousWorkspace() => SwitchWorkspace(Se.Settings.Appearance.ActiveWorkspace - 1);

    [RelayCommand]
    private void ToggleMaximizeArea() => AreaHost.ToggleMaximize(null);

    [RelayCommand]
    private void ToggleAreaHeaders()
    {
        ShowAreaHeaders = !ShowAreaHeaders;
        Se.Settings.Appearance.ShowAreaHeaders = ShowAreaHeaders;
        RebuildLayout();
    }

    /// <summary>Switches workspace (wraps around); floating windows of the old one close, the new one's open.</summary>
    internal void SwitchWorkspace(int index)
    {
        var list = Se.Settings.Appearance.Workspaces;
        index = (index % list.Count + list.Count) % list.Count;
        if (index != Se.Settings.Appearance.ActiveWorkspace)
        {
            AreaHost.SaveFloatingBounds();
            Se.Settings.Appearance.ActiveWorkspace = index;
            RebuildLayout();
        }

        WorkspaceBar.Refresh(this);
    }

    internal void AddWorkspace(Workspace workspace)
    {
        var list = Se.Settings.Appearance.Workspaces;
        var name = workspace.Name;
        for (var i = 2; list.Any(w => w.Name == workspace.Name); i++)
        {
            workspace.Name = $"{name} {i}";
        }

        list.Add(workspace);
        SwitchWorkspace(list.Count - 1);
    }

    internal void AddPresetWorkspace(string presetName, int legacyLayoutNumber)
    {
        var name = legacyLayoutNumber > 0 ? string.Format(Se.Language.Workspace.LayoutX, legacyLayoutNumber) : presetName;
        var root = legacyLayoutNumber > 0 ? AreaPresets.Legacy(legacyLayoutNumber) : AreaPresets.ForName(presetName, Se.Settings.General.LayoutNumber);
        var workspace = new Workspace { Name = name, Root = root };
        AreaTree.AddBar(workspace, PanelIds.AssaTools);
        AddWorkspace(workspace);
    }

    internal void DuplicateWorkspace(int index)
    {
        AreaHost.SaveFloatingBounds();
        AddWorkspace(Se.Settings.Appearance.Workspaces[index].Clone());
    }

    internal async Task RenameWorkspace(int index)
    {
        var workspace = Se.Settings.Appearance.Workspaces[index];
        var result = await ShowDialogAsync<PromptTextBoxWindow, PromptTextBoxViewModel>(vm =>
        {
            vm.Initialize(Se.Language.Workspace.WorkspaceName, workspace.Name, 250, 20, true);
        });

        if (result.OkPressed && !string.IsNullOrWhiteSpace(result.Text))
        {
            workspace.Name = result.Text.Trim();
            WorkspaceBar.Refresh(this);
        }
    }

    internal void DeleteWorkspace(int index)
    {
        var appearance = Se.Settings.Appearance;
        if (appearance.Workspaces.Count <= 1)
        {
            return;
        }

        var wasActive = index == appearance.ActiveWorkspace;
        appearance.Workspaces.RemoveAt(index);
        if (appearance.ActiveWorkspace > index || appearance.ActiveWorkspace >= appearance.Workspaces.Count)
        {
            appearance.ActiveWorkspace = Math.Max(0, appearance.ActiveWorkspace - 1);
        }

        if (wasActive)
        {
            RebuildLayout();
        }

        WorkspaceBar.Refresh(this);
    }

    internal void ResetWorkspace(int index)
    {
        var workspace = Se.Settings.Appearance.Workspaces[index];
        workspace.Root = AreaPresets.ForName(workspace.Name, Se.Settings.General.LayoutNumber);
        workspace.Floating.Clear();
        AreaTree.AddBar(workspace, PanelIds.AssaTools); // the default workspaces have it
        if (index == Se.Settings.Appearance.ActiveWorkspace)
        {
            RebuildLayout();
        }
    }
}
