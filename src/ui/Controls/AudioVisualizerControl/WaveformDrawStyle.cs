namespace Nikse.SubtitleEdit.Controls.AudioVisualizerControl;

/// <summary>Stored by name in the settings. Shown as Solid / Dynamic / Lines.</summary>
public enum WaveformDrawStyle
{
    Classic, // Solid: one flat color
    Fancy,   // Dynamic: gradient, loud peaks in the high color
    Lines,   // only the outline of the waveform (top and bottom edge)
}
