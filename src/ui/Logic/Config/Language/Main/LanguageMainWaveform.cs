namespace Nikse.SubtitleEdit.Logic.Config.Language;

public class LanguageMainWaveform
{
    public string PlayPauseHint { get; set; }
    public string PlayNextHint { get; set; }
    public string PlaySelectionHint { get; set; }
    public string SetStartAndOffsetTheRestHint { get; set; }
    public string SetStartHint { get; set; }
    public string SetEndHint { get; set; }
    public string NewHint { get; set; }
    public string CenterWaveformHint { get; set; }
    public string ZoomHorizontalHint { get; set; }
    public string ZoomVerticalHint { get; set; }
    public string SelectCurrentLineWhilePlayingHint { get; set; }
    public string VideoPosition { get; set; }
    public string HideWaveformToolbar { get; set; }
    public string ResetZoomAndSpeed { get; set; }
    public string RemoveBlankLines { get; set; }
    public string PlaySelectedRepeatHint { get; set; }
    public string SnapHint { get; set; }
    public string RazorHint { get; set; }
    public string RazorAtVideoPosition { get; set; }
    public string SnapToggle { get; set; }
    public string RazorToggle { get; set; }
    public string WaveformSettings { get; set; }
    public string Style { get; set; }
    public string Colors { get; set; }
    public string View { get; set; }
    public string ViewWaveform { get; set; }
    public string ViewSpectrogram { get; set; }
    public string ViewBoth { get; set; }
    public string SubtitleText { get; set; }
    public string TextSize { get; set; }
    public string TextSizeSmall { get; set; }
    public string TextSizeMedium { get; set; }
    public string TextSizeLarge { get; set; }
    public string LineNumber { get; set; }
    public string Duration { get; set; }
    public string CharsPerSecond { get; set; }
    public string GridLines { get; set; }
    public string AudioTrack { get; set; }
    public string NoAudioTracks { get; set; }
    public string MoreWaveformSettings { get; set; }
    public string ColorGreen { get; set; }
    public string ColorBlue { get; set; }
    public string ColorTeal { get; set; }
    public string ColorViolet { get; set; }
    public string ColorAmber { get; set; }
    public string ColorGrey { get; set; }
    public string Waveform { get; set; }
    public string Background { get; set; }
    public string BackgroundBlack { get; set; }
    public string BackgroundCharcoal { get; set; }
    public string BackgroundSlate { get; set; }
    public string BackgroundNavy { get; set; }
    public string ColorSubtitles { get; set; }
    public string CustomWaveformColor { get; set; }
    public string CustomBackgroundColor { get; set; }

    public LanguageMainWaveform()
    {
        PlayPauseHint = "Play / Pause {0}";
        PlayNextHint = "Play next {0}";
        PlaySelectionHint = "Play selection {0}";
        SetStartAndOffsetTheRestHint = "Set start of current subtitle and offset the rest {0}";
        SetStartHint = "Set start of current subtitle {0}";
        SetEndHint = "Set end of current subtitle {0}";
        NewHint = "Insert new subtitle at video position {0}";
        CenterWaveformHint = "Center waveform on current video position while playing {0}";
        ZoomHorizontalHint = "Zoom horizontal {0}";
        ZoomVerticalHint = "Zoom vertical {0}";
        SelectCurrentLineWhilePlayingHint = "Select current subtitle while playing {0}";
        VideoPosition = "Video position {0}";
        HideWaveformToolbar = "Hide toolbar {0}";
        ResetZoomAndSpeed = "Reset zoom & playback speed {0}";
        RemoveBlankLines = "Remove blank lines {0}";
        PlaySelectedRepeatHint = "Play selected subtitle(s) in repeat mode {0}";
        SnapHint = "Snap: edges stick to neighbors and can't overlap (off: overlap freely; Shift while dragging overrides) {0}";
        RazorHint = "Razor: click a subtitle to cut it in two - both halves keep the text {0}";
        RazorAtVideoPosition = "Cut at video position (both halves keep the text)";
        SnapToggle = "Waveform snap (on/off)";
        RazorToggle = "Waveform razor tool (on/off)";
        WaveformSettings = "Waveform settings";
        Style = "Style";
        Colors = "Colors";
        View = "View";
        ViewWaveform = "Waveform";
        ViewSpectrogram = "Spectrogram";
        ViewBoth = "Waveform and spectrogram";
        SubtitleText = "Subtitle text";
        TextSize = "Text size";
        TextSizeSmall = "Small";
        TextSizeMedium = "Medium";
        TextSizeLarge = "Large";
        LineNumber = "Line number (#)";
        Duration = "Duration";
        CharsPerSecond = "Chars/sec";
        GridLines = "Grid lines";
        AudioTrack = "Audio track";
        NoAudioTracks = "No video loaded";
        MoreWaveformSettings = "More waveform settings...";
        ColorGreen = "Green";
        ColorBlue = "Blue";
        ColorTeal = "Teal";
        ColorViolet = "Violet";
        ColorAmber = "Amber";
        ColorGrey = "Grey";
        Waveform = "Waveform";
        Background = "Background";
        BackgroundBlack = "Black";
        BackgroundCharcoal = "Charcoal";
        BackgroundSlate = "Slate";
        BackgroundNavy = "Navy";
        ColorSubtitles = "Color subtitles";
        CustomWaveformColor = "Custom waveform color...";
        CustomBackgroundColor = "Custom background color...";
    }
}