using static Nikse.SubtitleEdit.UiLogic.Layout.AreaNode;

namespace Nikse.SubtitleEdit.UiLogic.Layout;

public static class AreaPresets
{
    public const string Edit = "Edit";
    public const string Timing = "Timing";
    public const string Typeset = "Typeset";
    public const string Project = "Project";
    public const int LegacyCount = 12;
    private const double WaveformPx = 150;

    private static AreaNode ListAndEdit() => TopBottom(Leaf(PanelIds.Subtitles), Leaf(PanelIds.TextEdit)).AutoSecond();
    private static AreaNode Video() => Leaf(PanelIds.Video);
    private static AreaNode Waveform() => Leaf(PanelIds.Waveform);

    /// <summary>The 12 layouts of the old "Choose layout" window, as trees.</summary>
    public static AreaNode Legacy(int number) => number switch
    {
        2 => TopBottom(LeftRight(Video(), ListAndEdit()), Waveform()).FixedSecond(WaveformPx),
        3 => LeftRight(TopBottom(ListAndEdit(), Waveform()).FixedSecond(WaveformPx), Video()),
        4 => LeftRight(Video(), TopBottom(ListAndEdit(), Waveform()).FixedSecond(WaveformPx)),
        5 => TopBottom(Video(), TopBottom(ListAndEdit(), Waveform()), 1.0 / 3),
        6 => TopBottom(ListAndEdit(), Waveform()).FixedSecond(WaveformPx),
        7 => LeftRight(ListAndEdit(), Video()),
        8 => TopBottom(ListAndEdit(), Video()),
        9 => TopBottom(Video(), ListAndEdit()),
        10 => TopBottom(LeftRight(Video(), Waveform()), ListAndEdit()),
        11 => TopBottom(Video(), TopBottom(Waveform(), ListAndEdit()), 1.0 / 3),
        12 => ListAndEdit(),
        _ => TopBottom(LeftRight(ListAndEdit(), Video()), Waveform()).FixedSecond(WaveformPx),
    };

    public static AreaNode ForName(string name, int legacyLayoutNumber = 1) => name switch
    {
        Timing => TopBottom(LeftRight(Video(), ListAndEdit(), 0.35), Waveform(), 0.55),
        Typeset => LeftRight(
            TopBottom(Video(), Waveform()).FixedSecond(120),
            TopBottom(ListAndEdit(), Leaf(PanelIds.Styles, PanelIds.ScriptInfo, PanelIds.Notes)).FixedSecond(240),
            0.62),
        // the series desk: episode index (GitHub as a tab behind it) over notes, beside the usual editing set
        Project => LeftRight(
            TopBottom(Leaf(PanelIds.Project, PanelIds.GitHub), Leaf(PanelIds.Notes), 0.62),
            TopBottom(LeftRight(Video(), ListAndEdit()), Waveform()).FixedSecond(WaveformPx),
            0.24),
        _ => Legacy(legacyLayoutNumber),
    };

    /// <summary>First-run workspaces, migrated from the old layout number / undocked-video setting.</summary>
    public static List<Workspace> Defaults(int legacyLayoutNumber, bool videoUndocked)
    {
        var edit = new Workspace { Name = Edit, Root = Legacy(legacyLayoutNumber) };
        if (videoUndocked)
        {
            AreaTree.Detach(edit, PanelIds.Video);
            AreaTree.Detach(edit, PanelIds.Waveform);
        }

        List<Workspace> workspaces =
        [
            edit,
            new Workspace { Name = Timing, Root = ForName(Timing) },
            new Workspace { Name = Typeset, Root = ForName(Typeset) },
            new Workspace { Name = Project, Root = ForName(Project) },
        ];

        foreach (var ws in workspaces)
        {
            AreaTree.AddBar(ws, PanelIds.AssaTools);
        }

        return workspaces;
    }
}
