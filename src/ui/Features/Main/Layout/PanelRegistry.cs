using Avalonia.Controls;
using Nikse.SubtitleEdit.Features.Main.Panels;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <param name="AssOnly">Only useful for ASS/SSA; greyed out in the "+" menu otherwise.</param>
/// <param name="MinWidth">Content narrower than this scrolls sideways instead of overlapping (0 = shrink freely).</param>
public record PanelInfo(string Id, Func<string> Title, string Icon, bool AssOnly, Func<MainView, MainViewModel, Control> Make, double MinWidth = 0);

/// <summary>Every panel an area can show. Adding a panel = one line here + a builder.</summary>
public static class PanelRegistry
{
    public static readonly List<PanelInfo> All =
    [
        new(PanelIds.Subtitles, () => Se.Language.Workspace.Subtitles, "mdi-format-list-bulleted", false,
            InitListViewAndEditBox.MakeSubtitleList),
        new(PanelIds.TextEdit, () => Se.Language.Workspace.TextEdit, "mdi-form-textbox", false,
            (_, vm) => InitListViewAndEditBox.MakeEditBox(vm)), // lays itself out for any size (FitEditBox)
        new(PanelIds.Video, () => Se.Language.Workspace.Video, "mdi-video", false,
            (view, vm) => InitVideoPlayer.MakeLayoutVideoPlayer(vm, new Avalonia.Thickness(0), out _)),
        new(PanelIds.Waveform, () => Se.Language.Workspace.Waveform, "mdi-waveform", false,
            (_, vm) => InitWaveform.MakeWaveform(vm)),
        new(PanelIds.Styles, () => Se.Language.Workspace.Styles, "mdi-palette-swatch", true,
            (_, vm) => StylesPanel.Make(vm)),
        new(PanelIds.ScriptInfo, () => Se.Language.Workspace.ScriptInfo, "mdi-information-outline", true,
            (_, vm) => ScriptInfoPanel.Make(vm)),
        new(PanelIds.Notes, () => Se.Language.Workspace.Notes, "mdi-note-text-outline", false,
            (_, vm) => NotesPanel.Make(vm)),
        new(PanelIds.AutoReplace, () => Se.Language.Workspace.AutoReplace, "mdi-find-replace", false,
            (_, vm) => AutoReplacePanel.Make(vm)),
        new(PanelIds.AssaTools, () => Se.Language.Workspace.AssaToolsBar, "mdi-tools", false,
            (_, vm) => new AssaToolbar(vm)),
        new(PanelIds.Project, () => Se.Language.Workspace.Project, "mdi-folder-multiple", false,
            (_, vm) => ProjectPanel.Make(vm)),
        new(PanelIds.GitHub, () => Se.Language.Workspace.GitHub, "mdi-source-pull", false,
            (_, vm) => GitHubPanel.Make(vm)),
    ];

    public static PanelInfo? Get(string id) => All.FirstOrDefault(p => p.Id == id);

    public static string[] Ids => All.Select(p => p.Id).ToArray();

    public static string Title(string id) => Get(id)?.Title() ?? id;
}
