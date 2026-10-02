namespace Nikse.SubtitleEdit.Logic.Config.Language;

public class LanguageWorkspace
{
    // panels
    public string Subtitles { get; set; }
    public string TextEdit { get; set; }
    public string Video { get; set; }
    public string Waveform { get; set; }
    public string Styles { get; set; }
    public string ScriptInfo { get; set; }
    public string Notes { get; set; }
    public string AutoReplace { get; set; }
    public string Project { get; set; }
    public string GitHub { get; set; }
    public string AssaToolsBar { get; set; }
    public string MainToolbar { get; set; }
    public string HideBar { get; set; }
    public string OnlyForAssa { get; set; }

    // panel contents
    public string SetOnSelectedLines { get; set; }
    public string EditStyles { get; set; }
    public string ChangeResolution { get; set; }
    public string MoreProperties { get; set; }
    public string YCbCrMatrix { get; set; }
    public string ScaledBorderAndShadow { get; set; }
    public string ApplyToSelectedLines { get; set; }
    public string ApplyToAllLines { get; set; }
    public string EditRules { get; set; }
    public string NotesGeneral { get; set; }
    public string NotesProject { get; set; }
    public string NotesEpisode { get; set; }
    public string NoProjectOpen { get; set; }

    // area header / menus
    public string AddPanel { get; set; }
    public string AreaMenu { get; set; }
    public string SplitLeft { get; set; }
    public string SplitRight { get; set; }
    public string SplitUp { get; set; }
    public string SplitDown { get; set; }
    public string MaximizeArea { get; set; }
    public string RestoreArea { get; set; }
    public string OpenInNewWindow { get; set; }
    public string CloseArea { get; set; }
    public string CloseTab { get; set; }
    public string DockBack { get; set; }

    // workspaces
    public string Window { get; set; }
    public string Workspaces { get; set; }
    public string NextWorkspace { get; set; }
    public string PreviousWorkspace { get; set; }
    public string NewWorkspace { get; set; }
    public string DuplicateWorkspace { get; set; }
    public string RenameWorkspace { get; set; }
    public string DeleteWorkspace { get; set; }
    public string ResetWorkspace { get; set; }
    public string LayoutX { get; set; }
    public string ShowAreaHeaders { get; set; }
    public string LoadLayoutPreset { get; set; }
    public string WorkspaceName { get; set; }

    public LanguageWorkspace()
    {
        Subtitles = "Subtitles";
        TextEdit = "Text";
        Video = "Video";
        Waveform = "Waveform";
        Styles = "Styles";
        ScriptInfo = "Script info";
        Notes = "Notes";
        AutoReplace = "Auto-replace";
        Project = "Project";
        GitHub = "GitHub";
        AssaToolsBar = "ASSA tools bar";
        MainToolbar = "Main toolbar";
        HideBar = "Hide";
        OnlyForAssa = "Only available for Advanced Sub Station Alpha (.ass) and Sub Station Alpha (.ssa) files";

        SetOnSelectedLines = "Set on selected lines";
        EditStyles = "Edit styles...";
        ChangeResolution = "Change resolution...";
        MoreProperties = "More...";
        YCbCrMatrix = "YCbCr matrix";
        ScaledBorderAndShadow = "Scale border and shadow";
        ApplyToSelectedLines = "Apply to selected lines";
        ApplyToAllLines = "Apply to all lines";
        EditRules = "Edit rules...";
        NotesGeneral = "General";
        NotesProject = "Project";
        NotesEpisode = "Episode";
        NoProjectOpen = "No project is open";

        AddPanel = "Add panel";
        AreaMenu = "Area";
        SplitLeft = "Split left";
        SplitRight = "Split right";
        SplitUp = "Split up";
        SplitDown = "Split down";
        MaximizeArea = "Maximize area";
        RestoreArea = "Restore area";
        OpenInNewWindow = "Open in new window";
        CloseArea = "Close area";
        CloseTab = "Close tab";
        DockBack = "Dock back into the main window";

        Window = "_Window";
        Workspaces = "Workspaces";
        NextWorkspace = "Next workspace";
        PreviousWorkspace = "Previous workspace";
        NewWorkspace = "New workspace";
        DuplicateWorkspace = "Duplicate";
        RenameWorkspace = "Rename...";
        DeleteWorkspace = "Delete";
        ResetWorkspace = "Reset to preset";
        LayoutX = "Layout {0}";
        ShowAreaHeaders = "Show area headers";
        LoadLayoutPreset = "Load layout preset...";
        WorkspaceName = "Workspace name";
    }
}
