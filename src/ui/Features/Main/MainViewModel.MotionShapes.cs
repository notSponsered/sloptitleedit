using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Assa.AssaMotionShapes;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.MotionShapes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>
/// "Motion shapes": a keyframed shape that follows the video. A selected simple drawing line becomes the first key and is
/// replaced by the result; otherwise the shape lines go just before the first selected line (same layer, so text stays on top).
/// </summary>
public partial class MainViewModel
{
    [RelayCommand]
    private async Task ShowAssaMotionShapes()
    {
        if (Window == null || !IsFormatAssa || !await RequireFfmpegOk())
        {
            return;
        }

        var l = Se.Language.Assa;
        var selectedItems = SubtitleGrid.SelectedItems.Cast<SubtitleLineViewModel>().OrderBy(Subtitles.IndexOf).ToList();
        if (selectedItems.Count == 0 || string.IsNullOrEmpty(_videoFileName))
        {
            await MessageBox.Show(Window, l.MotionShapes, l.MotionSelectLinesFirst, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var spanStart = selectedItems.Min(p => p.StartTime.TotalSeconds);
        var spanEnd = selectedItems.Max(p => p.EndTime.TotalSeconds);
        if (spanEnd - spanStart > 15)
        {
            await MessageBox.Show(Window, l.MotionShapes, string.Format(l.MotionSpanTooLongX, spanEnd - spanStart), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var header = string.IsNullOrEmpty(_subtitle.Header) ? AdvancedSubStationAlpha.DefaultHeader : _subtitle.Header;
        var mediaWidth = _mediaInfo?.Dimension.Width ?? 0;
        var mediaHeight = _mediaInfo?.Dimension.Height ?? 0;
        var styles = AdvancedSubStationAlpha.GetSsaStylesFromHeader(header);

        // a simple drawing line is the first key (and is replaced); anything else is left as it is
        SubtitleLineViewModel? sourceLine = null;
        UiLogic.Assa.PenPath? sourcePath = null;
        foreach (var line in selectedItems)
        {
            if (ShapeSource.GetReplaceablePath(line.Text, line.Style, header, line.MarginL, line.MarginV) is { } path && mediaWidth > 0 && mediaHeight > 0)
            {
                sourceLine = line;
                // same PlayRes fallback (384x288) as the output, so an unchanged shape stays where it is
                var (sx, sy) = ShapeLines.ScriptScale(header, mediaWidth, mediaHeight);
                sourcePath = Scale(path, 1 / sx, 1 / sy);
                break;
            }
        }

        var styleName = sourceLine?.Style ?? NewDrawingStyleName;
        Avalonia.Media.Color? StyleColor(string name) =>
            styles.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Primary.ToAvaloniaColor();

        var referenceTime = Math.Clamp(GetVideoPlayerControl()?.Position ?? spanStart, spanStart, spanEnd);
        var videoFileName = _videoFileName;
        var result = await ShowDialogAsync<AssaMotionShapesWindow, AssaMotionShapesViewModel>(vm =>
            vm.Initialize(videoFileName, mediaWidth, mediaHeight, spanStart, spanEnd, referenceTime, sourcePath, styleName,
                StyleColor(styleName) ?? SKColors.White.ToAvaloniaColor(), StyleColor, header));

        if (!result.OkPressed || result.ResultFrames.Count == 0 || result.VideoWidth <= 0 || result.VideoHeight <= 0)
        {
            return;
        }

        var state = new DrawStyleState
        {
            Name = result.ResultStyleName,
            TemplateStyleName = styleName,
            Color = result.ResultColor,
            NameChanged = result.NameChanged,
            ColorChanged = result.ColorChanged,
        };
        // a fill colour override in the source would hide the colour picked/tracked here
        var userTags = ShapeLines.KeepTags(sourceLine?.Text ?? string.Empty, result.ResultUsesColor || result.ColorChanged || result.NameChanged);
        var template = sourceLine ?? selectedItems[0];

        RunWithoutChangeDetection(() =>
        {
            SaveDrawingStyle(state);
            var pieces = ShapeLines.Write(result.ResultFrames, result.ResultTimesMs, result.ResultSegments, spanStart * 1000, spanEnd * 1000,
                _subtitle.Header ?? header, state.Name, result.VideoWidth, result.VideoHeight, userTags, result.ResultUsesColor, result.ResultSimplify);
            if (pieces.Count == 0)
            {
                return; // nothing to write (span shorter than a centisecond) - keep the source line
            }

            var index = Math.Max(0, Subtitles.IndexOf(template));
            if (sourceLine != null)
            {
                Subtitles.Remove(sourceLine);
            }

            var newLines = new List<SubtitleLineViewModel>();
            foreach (var piece in pieces)
            {
                var line = new SubtitleLineViewModel(template, generateNewId: true)
                {
                    StartTime = TimeSpan.FromMilliseconds(piece.StartMs),
                    EndTime = TimeSpan.FromMilliseconds(piece.EndMs),
                    Text = piece.Text,
                    Style = state.Name,
                };
                if (sourceLine == null)
                {
                    // a copy of a text line (for its layer) must not carry its Comment flag/Effect/margins over to the shape
                    line.Paragraph = null;
                    line.MarginL = line.MarginR = line.MarginV = 0;
                }

                newLines.Add(line);
            }

            for (var i = 0; i < newLines.Count; i++)
            {
                Subtitles.Insert(Math.Min(index + i, Subtitles.Count), newLines[i]);
            }

            Renumber();
        });

        RefreshSubtitlePreview();
        _updateAudioVisualizer = true;
    }
}
