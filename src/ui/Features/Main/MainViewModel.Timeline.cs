using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Controls.AudioVisualizerControl;
using Nikse.SubtitleEdit.Features.Shared.ColorPicker;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>
/// Waveform editing like a video editor's timeline. Snap: edges stick to their neighbours and can't
/// overlap them (off = overlap freely; this is the "Allow overlap" setting inverted). Razor: cut a line in
/// two — both halves keep the whole text and meet exactly at the cut.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private bool _waveformSnap = !Se.Settings.Waveform.AllowOverlap;
    [ObservableProperty] private bool _waveformRazor;

    partial void OnWaveformSnapChanged(bool value) => Se.Settings.Waveform.AllowOverlap = !value;

    partial void OnWaveformRazorChanged(bool value)
    {
        if (AudioVisualizer != null)
        {
            AudioVisualizer.RazorMode = value;
        }
    }

    [RelayCommand]
    private void ToggleWaveformSnap() => WaveformSnap = !WaveformSnap;

    [RelayCommand]
    private void ToggleWaveformRazor() => WaveformRazor = !WaveformRazor;

    /// <summary>Cuts at the playhead: the selected lines under it, or every line under it when none of those is selected.</summary>
    [RelayCommand]
    private void RazorAtVideoPosition()
    {
        var vp = GetVideoPlayerControl();
        if (vp == null)
        {
            return;
        }

        var seconds = vp.Position;
        var under = Subtitles.Where(p => p.StartTime.TotalSeconds < seconds && p.EndTime.TotalSeconds > seconds).ToList();
        var selected = under.Where(p => _selectedSubtitles?.Contains(p) == true).ToList();
        RazorCut(selected.Count > 0 ? selected : under, seconds);
    }

    /// <summary>A razor click in the waveform (the event carries a copy of the line; find the real one).</summary>
    internal void AudioVisualizerRazorCut(object sender, ParagraphEventArgs e)
    {
        var line = Subtitles.FirstOrDefault(p => p.Id == e.Paragraph.Id);
        if (line != null)
        {
            RazorCut([line], e.Seconds);
        }
    }

    /// <summary>Splits each line at <paramref name="seconds"/> into two lines with the same text that meet at the cut. One undo step.</summary>
    private void RazorCut(IReadOnlyList<SubtitleLineViewModel> lines, double seconds)
    {
        const double minPartSeconds = 0.01;
        var targets = lines
            .Where(p => seconds - p.StartTime.TotalSeconds >= minPartSeconds && p.EndTime.TotalSeconds - seconds >= minPartSeconds)
            .ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var cut = TimeSpan.FromSeconds(seconds);
        var secondHalves = new List<SubtitleLineViewModel>();
        RunWithoutChangeDetection(() =>
        {
            foreach (var line in targets)
            {
                var second = new SubtitleLineViewModel(line, generateNewId: true);
                second.SetStartTimeOnly(cut); // StartTime itself would move the whole line
                line.EndTime = cut;
                Subtitles.Insert(Subtitles.IndexOf(line) + 1, second); // right after: same place in the render order
                secondHalves.Add(second);
            }

            Renumber();
        });

        SelectAndScrollToSubtitle(secondHalves[^1]);
    }

    /// <summary>The color picker (no alpha), for the waveform settings menu's custom colors. Null when cancelled.</summary>
    public async Task<Color?> PickColorAsync(Color initial)
    {
        var result = await ShowDialogAsync<ColorPickerWindow, ColorPickerViewModel>(picker =>
        {
            picker.ShowAlpha = false;
            picker.Initialize(Color.FromRgb(initial.R, initial.G, initial.B));
        });
        return result.OkPressed ? Color.FromRgb(result.SelectedColor.R, result.SelectedColor.G, result.SelectedColor.B) : null;
    }
}
