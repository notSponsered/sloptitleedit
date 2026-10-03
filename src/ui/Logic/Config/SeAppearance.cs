using Avalonia.Controls;
using Avalonia.Media;
using Nikse.SubtitleEdit.UiLogic.Layout;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Logic.Config;

public class SeAppearance
{
    public string Theme { get; set; }
    public double LayoutScale { get; set; }
    public string FontName { get; set; }
    public double SubtitleGridFontSize { get; set; }
    public bool SubtitleGridTextSingleLine { get; set; }
    public string SubtitleGridTextSingleLineSeparator { get; set; }
    public double SubtitleTextBoxFontSize { get; set; }
    public string SubtitleTextBoxAndGridFontName { get; set; }
    public bool SubtitleTextBoxFontBold { get; set; }
    public bool SubtitleTextBoxColorTags { get; set; }
    public int SubtitleGridFormattingType { get; set; }
    public bool SubtitleGridLiveSpellCheck { get; set; }

    public bool SubtitleTextBoxCenterText { get; set; }
    public bool SubtitleTextBoxLiveSpellCheck { get; set; }
    public bool ShowHints { get; set; }
    public bool GridCompactMode { get; set; }
    public string BookmarkColor { get; set; }
    // Own colors laid over the theme (ThemePreset); "" = the theme's.
    public string AccentColor { get; set; }
    public string BackgroundColor { get; set; }
    public string BarColor { get; set; }
    public string TextColor { get; set; }
    public string DividerColor { get; set; }
    public string SyntaxTagColor { get; set; }
    public string SyntaxAttributeColor { get; set; }
    public string SyntaxValueColor { get; set; }
    public bool PanelTints { get; set; }                        // each panel's icon (and waveform lane) in its own color
    public Dictionary<string, string> PanelTintColors { get; set; } // panel id → own tint; missing = the default
    public bool UseFocusedButtonBackgroundColor { get; set; }
    public string FocusedButtonBackgroundColor { get; set; }
    public string GridLinesAppearance { get; set; }
    public bool ShowHorizontalLineAboveToolbar { get; set; }

    public bool ToolbarShowFileNew { get; set; }
    public bool ToolbarShowFileOpen { get; set; }
    public bool ToolbarShowVideoFileOpen { get; set; }
    public bool ToolbarShowSave { get; set; }
    public bool ToolbarShowSaveAs { get; set; }
    public bool ToolbarShowFind { get; set; }
    public bool ToolbarShowReplace { get; set; }
    public bool ToolbarShowFixCommonErrors { get; set; }
    public bool ToolbarShowSpellCheck { get; set; }
    public bool ToolbarShowRemoveTextForHi { get; set; }
    public bool ToolbarShowVisualSync { get; set; }
    public bool ToolbarShowBeautifyTimeCodes { get; set; }
    public bool ToolbarShowBurnIn { get; set; }
    public bool ToolbarShowSettings { get; set; }
    public bool ToolbarShowLayout { get; set; }
    public bool ToolbarShowHelp { get; set; }
    public bool ToolbarShowEncoding { get; set; }
    public bool ToolbarShowFrameRate { get; set; }
    public bool ShowPluginsMenu { get; set; }
    public bool RightToLeft { get; set; }
    public bool ShowLayer { get; set; }
    public bool ShowUpDownStartTime { get; set; }
    public bool ShowUpDownEndTime { get; set; }
    public bool ShowUpDownDuration { get; set; }
    public bool ShowUpDownLabels { get; set; }

    public List<Workspace> Workspaces { get; set; }
    public int ActiveWorkspace { get; set; }
    public bool ShowAreaHeaders { get; set; }
    public bool ShowMainToolbar { get; set; }      // the big icon toolbar under the menu (off: its pickers live in the menu row)
    public bool AssaToolbarAdded { get; set; }     // one-time: the ASSA tools bar was added to existing workspaces
    public bool ProjectWorkspaceAdded { get; set; } // one-time: the "Project" workspace was added to existing setups
    public bool TextBoxShowButtonAutoBreak { get; set; }
    public bool TextBoxShowButtonUnbreak { get; set; }
    public bool TextBoxShowButtonItalic { get; set; }
    public bool TextBoxShowButtonColor { get; set; }
    public bool TextBoxShowButtonRemoveFormatting { get; set; }

    public SeAppearance()
    {
        Workspaces = [];
        ShowAreaHeaders = true;
        Theme = "System";
        LayoutScale = 1.0;
        FontName = "Default";
        SubtitleTextBoxAndGridFontName = "Default";
        SubtitleGridFontSize = 13d;
        SubtitleGridTextSingleLineSeparator = " ⏎ "; // "<br />";
        SubtitleTextBoxFontSize = 15d;
        SubtitleTextBoxFontBold = true;
        SubtitleTextBoxColorTags = true;
        ShowHints = true;
        SubtitleTextBoxCenterText = false;
        SubtitleTextBoxLiveSpellCheck = false;
        SubtitleGridFormattingType = (int)SubtitleGridFormattingTypes.ShowFormatting;
        GridLinesAppearance = DataGridGridLinesVisibility.None.ToString();
        AccentColor = string.Empty;
        BackgroundColor = string.Empty;
        BarColor = string.Empty;
        TextColor = string.Empty;
        DividerColor = string.Empty;
        SyntaxTagColor = string.Empty;
        SyntaxAttributeColor = string.Empty;
        SyntaxValueColor = string.Empty;
        PanelTints = true;
        PanelTintColors = [];
        UseFocusedButtonBackgroundColor = true;
        FocusedButtonBackgroundColor = string.Empty; // "" = a tint of the theme's accent
        BookmarkColor = Colors.Gold.FromColorToHex();
        GridCompactMode = true;
        ShowLayer = true;
        ShowUpDownStartTime = true;
        ShowUpDownEndTime = false;
        ShowUpDownDuration = true;
        ShowUpDownLabels = true;

        ToolbarShowFileNew = true;
        ToolbarShowFileOpen = true;
        ToolbarShowVideoFileOpen = false;
        ToolbarShowSave = true;
        ToolbarShowSaveAs = false;
        ToolbarShowFind = true;
        ToolbarShowReplace = true;
        ToolbarShowFixCommonErrors = false;
        ToolbarShowSpellCheck = true;
        ToolbarShowRemoveTextForHi = false;
        ToolbarShowVisualSync = false;
        ToolbarShowBeautifyTimeCodes = false;
        ToolbarShowBurnIn = false;
        ToolbarShowSettings = true;
        ToolbarShowLayout = true;
        ToolbarShowHelp = true;
        ToolbarShowEncoding = false;
        ShowPluginsMenu = false;

        TextBoxShowButtonAutoBreak = true;
        TextBoxShowButtonUnbreak = true;
        TextBoxShowButtonItalic = true;
        TextBoxShowButtonColor = false;
        TextBoxShowButtonRemoveFormatting = false;
    }
}