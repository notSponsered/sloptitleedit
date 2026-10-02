using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Controls.VideoPlayer;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Assa.AssaDraw;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.Logic.VideoPlayers.LibMpvDynamic;
using Nikse.SubtitleEdit.UiLogic.Assa;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>
/// "Draw on video": the draw tool works right on the paused video picture (same place and size - only the picture becomes
/// a drawing surface) with its options as context buttons in the player's button row. Selected drawing lines are edited
/// in place (only their drawing commands change); new shapes go into the selected drawing line, or a new line.
/// </summary>
public partial class MainViewModel
{
    private const string NewDrawingStyleName = "AssaDrawWhite";

    private VideoDrawCanvas? _drawOnVideoCanvas;

    /// <summary>
    /// Options of the tool being used (e.g. drawing on the video), shown at the end of the ASSA tools bar.
    /// Tools fill it when they start and clear it when they end.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<Control> ContextControls { get; } = [];

    /// <summary>The player on screen (docked, undocked or fullscreen), or null.</summary>
    private VideoPlayerControl? DrawOnVideoPlayer() =>
        GetVideoPlayerControl() is { IsEffectivelyVisible: true } player && TopLevel.GetTopLevel(player) != null ? player : null;

    private bool CanDrawOnVideo() => DrawOnVideoPlayer() != null && !string.IsNullOrEmpty(_videoFileName);

    private void StartDrawOnVideo(List<SubtitleLineViewModel> selectedItems, double positionSeconds)
    {
        if (_drawOnVideoCanvas != null || DrawOnVideoPlayer() is not { } player)
        {
            return;
        }

        player.VideoPlayer.Pause();
        var header = string.IsNullOrEmpty(_subtitle.Header) ? AdvancedSubStationAlpha.DefaultHeader : _subtitle.Header;
        var videoWidth = _mediaInfo?.Dimension.Width > 0 ? _mediaInfo.Dimension.Width : 1920;
        var videoHeight = _mediaInfo?.Dimension.Height > 0 ? _mediaInfo.Dimension.Height : 1080;
        var toVideoX = videoWidth / PlayRes(header, "PlayResX", videoWidth);
        var toVideoY = videoHeight / PlayRes(header, "PlayResY", videoHeight);
        var styles = AdvancedSubStationAlpha.GetSsaStylesFromHeader(header);

        var canvas = new VideoDrawCanvas { VideoWidth = videoWidth, VideoHeight = videoHeight };
        var editable = new List<(SubtitleLineViewModel Line, AssaDrawingText Drawing)>();
        var notEditable = 0;
        foreach (var line in selectedItems)
        {
            var drawing = AssaDrawingText.Parse(line.Text);
            if (drawing == null)
            {
                continue; // a text line - new shapes can still be drawn
            }

            var style = styles.FirstOrDefault(s => s.Name.Equals(line.Style, StringComparison.OrdinalIgnoreCase));
            var alignment = AssaTags.FirstAlignment(line.Text) ?? (int.TryParse(style?.Alignment, out var a) ? a : 2);
            var paths = alignment == 7 ? drawing.ToPaths() : null;
            if (paths == null)
            {
                notEditable++;
                continue;
            }

            editable.Add((line, drawing));
            var color = (style?.Primary ?? SKColors.White).ToAvaloniaColor();
            foreach (var path in paths)
            {
                canvas.Shapes.Add(new PenShape { Path = Scale(path, toVideoX, toVideoY), Color = color, Tag = line });
            }
        }

        if (editable.Count > 0)
        {
            canvas.NewShapeTag = editable[0].Line; // new shapes are added to the (first) selected drawing line
        }

        if (notEditable > 0)
        {
            ShowStatus(string.Format(Se.Language.Assa.DrawOnVideoNotEditableX, notEditable), 6000);
        }

        // name + colour = the style of the shape lines
        var styleName = editable.Count > 0 ? editable[0].Line.Style : NewDrawingStyleName;
        var state = new DrawStyleState
        {
            Name = styleName,
            TemplateStyleName = styleName,
            Color = (styles.FirstOrDefault(s => s.Name.Equals(styleName, StringComparison.OrdinalIgnoreCase))?.Primary ?? SKColors.White).ToAvaloniaColor(),
        };
        canvas.NewShapeColor = state.Color;

        var controls = MakeDrawOnVideoControls(canvas, state);

        void End()
        {
            _drawOnVideoCanvas = null;
            player.DetachedFromVisualTree -= OnPlayerDetached;
            ContextControls.Clear();
            player.HideOverlay();
            canvas.Frame?.Dispose();
            canvas.Frame = null;
        }

        // layout rebuilt / player closed while drawing: give the picture back (the drawing is dropped)
        void OnPlayerDetached(object? sender, VisualTreeAttachmentEventArgs e) => End();

        canvas.DoneRequested += (_, _) =>
        {
            End();
            ApplyDrawOnVideo(canvas, editable, selectedItems, positionSeconds, toVideoX, toVideoY, state);
        };
        canvas.CancelRequested += (_, _) => End();
        _drawOnVideoCanvas = canvas;
        player.DetachedFromVisualTree += OnPlayerDetached;

        // switch only when the frame is ready, so the picture doesn't flash or move
        LoadDrawOnVideoFrame(player, positionSeconds, bitmap =>
        {
            if (_drawOnVideoCanvas != canvas)
            {
                bitmap?.Dispose(); // cancelled meanwhile
                return;
            }

            canvas.Frame = bitmap;
            if (ShowAssaToolsBar)
            {
                foreach (var control in controls)
                {
                    ContextControls.Add(control); // shown at the end of the ASSA tools bar
                }

                player.ShowOverlay(canvas, []);
            }
            else
            {
                player.ShowOverlay(canvas, controls); // no tools bar: next to play/stop/fullscreen
            }

            canvas.Focus();
        });
    }

    /// <summary>Name/colour the user picked for the drawing; written to the lines' style on Done.</summary>
    private sealed class DrawStyleState
    {
        public string Name { get; set; } = string.Empty;
        public string TemplateStyleName { get; init; } = string.Empty;
        public Avalonia.Media.Color Color { get; set; }
        public bool NameChanged { get; set; }
        public bool ColorChanged { get; set; }
    }

    private List<Control> MakeDrawOnVideoControls(VideoDrawCanvas canvas, DrawStyleState state)
    {
        var l = Se.Language.Assa;
        Button? colorButton = null;

        void SetColor(Avalonia.Media.Color color)
        {
            state.Color = color;
            Panels.AssaToolbar.SetGlyphFill(colorButton!, color);
            canvas.SetColor(color);
        }

        colorButton = Panels.AssaToolbar.ContextButton(Panels.AssaToolbar.Glyphs.Swatch, l.DrawOnVideoColor, async () =>
        {
            if (Window == null)
            {
                return;
            }

            var picker = new Shared.ColorPicker.ColorPickerViewModel();
            picker.Initialize(state.Color);
            picker.ShowAlpha = false;
            await new Shared.ColorPicker.ColorPickerWindow(picker).ShowDialog(Window);
            if (picker.OkPressed)
            {
                SetColor(Avalonia.Media.Color.FromRgb(picker.SelectedColor.R, picker.SelectedColor.G, picker.SelectedColor.B));
                state.ColorChanged = true;
            }

            canvas.Focus();
        });
        Panels.AssaToolbar.SetGlyphFill(colorButton, state.Color);

        var nameBox = new TextBox
        {
            Text = state.Name,
            Width = 150,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            [ToolTip.TipProperty] = l.DrawOnVideoName,
            [Avalonia.Automation.AutomationProperties.NameProperty] = l.DrawOnVideoName,
        };

        void CommitName()
        {
            var name = nameBox.Text?.Trim().Replace(",", string.Empty) ?? string.Empty; // no commas in a style name
            if (name.Length == 0 || name == state.Name)
            {
                nameBox.Text = state.Name;
                return;
            }

            state.Name = name;
            state.NameChanged = true;
            var header = string.IsNullOrEmpty(_subtitle.Header) ? AdvancedSubStationAlpha.DefaultHeader : _subtitle.Header;
            var existing = AdvancedSubStationAlpha.GetSsaStylesFromHeader(header)
                .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !state.ColorChanged)
            {
                SetColor(existing.Primary.ToAvaloniaColor()); // an existing style brings its colour
            }
        }

        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitName();
                canvas.Focus();
                e.Handled = true; // not "done"
            }
            else if (e.Key == Key.Escape)
            {
                nameBox.Text = state.Name;
                canvas.Focus();
                e.Handled = true;
            }
        };
        nameBox.LostFocus += (_, _) => CommitName();

        return
        [
            Panels.AssaToolbar.ContextButton(Panels.AssaToolbar.Glyphs.Undo, l.DrawOnVideoUndo, canvas.UndoPoint),
            Panels.AssaToolbar.ContextToggle(Panels.AssaToolbar.Glyphs.Grid, l.DrawOnVideoGrid, false, on => canvas.ShowGrid = on),
            Panels.AssaToolbar.ContextButton(Panels.AssaToolbar.Glyphs.Fit, l.DrawOnVideoFit, canvas.FitView),
            colorButton,
            nameBox,
            Panels.AssaToolbar.ContextButton(Panels.AssaToolbar.Glyphs.Done, l.DrawOnVideoDone, canvas.RaiseDone),
            Panels.AssaToolbar.ContextButton(Panels.AssaToolbar.Glyphs.Cancel, l.DrawOnVideoCancel, canvas.RaiseCancel),
        ];
    }

    /// <summary>
    /// While drawing on the video the main window's shortcuts are off (Delete must delete a point, not subtitle lines);
    /// keys no control handled go to the drawing. Returns true when the key was taken.
    /// </summary>
    private bool TryHandleDrawOnVideoKey(KeyEventArgs e)
    {
        if (_drawOnVideoCanvas == null)
        {
            return false;
        }

        if (e.Route == RoutingStrategies.Bubble)
        {
            _drawOnVideoCanvas.HandleKey(e);
        }

        return true;
    }

    /// <summary>The frame being shown: mpv's own screenshot (exactly that frame), else ffmpeg at the position.</summary>
    private void LoadDrawOnVideoFrame(VideoPlayerControl player, double positionSeconds, Action<Bitmap?> done)
    {
        var videoFileName = _videoFileName;
        var mpv = player.VideoPlayer as LibMpvDynamicPlayer;
        var timeCode = Math.Max(0, positionSeconds).ToString("0.000", CultureInfo.InvariantCulture);
        Task.Run(() =>
        {
            Bitmap? bitmap = null;
            try
            {
                var fileName = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
                if (mpv == null || !mpv.ScreenshotToFile(fileName) || !File.Exists(fileName))
                {
                    fileName = !string.IsNullOrEmpty(videoFileName) && File.Exists(videoFileName)
                        ? FfmpegGenerator.GetScreenShot(videoFileName, timeCode)
                        : string.Empty;
                }

                if (File.Exists(fileName))
                {
                    bitmap = new Bitmap(fileName);
                    File.Delete(fileName);
                }
            }
            catch
            {
                // no frame - draw on black
            }

            Dispatcher.UIThread.Post(() => done(bitmap));
        });
    }

    private void ApplyDrawOnVideo(VideoDrawCanvas canvas, List<(SubtitleLineViewModel Line, AssaDrawingText Drawing)> editable,
        List<SubtitleLineViewModel> selectedItems, double positionSeconds, double toVideoX, double toVideoY, DrawStyleState state)
    {
        var styleChanged = state.NameChanged || state.ColorChanged;
        if (!canvas.IsModified && !styleChanged)
        {
            return;
        }

        List<PenPath> PathsOf(object? tag) => canvas.Shapes.Where(s => s.Tag == tag).Select(s => Scale(s.Path, 1 / toVideoX, 1 / toVideoY)).ToList();

        RunWithoutChangeDetection(() =>
        {
            var newPaths = PathsOf(null);
            if (styleChanged || newPaths.Count > 0)
            {
                SaveDrawingStyle(state);
            }

            foreach (var (line, drawing) in editable)
            {
                var paths = PathsOf(line);
                if (paths.Count == 0)
                {
                    Subtitles.Remove(line); // every shape of the line was deleted
                    continue;
                }

                if (canvas.IsModified)
                {
                    line.Text = drawing.WithPaths(paths);
                }

                if (styleChanged)
                {
                    line.Style = state.Name;
                }
            }

            if (newPaths.Count > 0)
            {
                var after = selectedItems.LastOrDefault(Subtitles.Contains);
                var line = after != null
                    ? new SubtitleLineViewModel(after, generateNewId: true)
                    : new SubtitleLineViewModel
                    {
                        StartTime = TimeSpan.FromSeconds(positionSeconds),
                        EndTime = TimeSpan.FromSeconds(positionSeconds + 2),
                    };
                line.Style = state.Name;
                line.Text = AssaDrawingText.NewLine.WithPaths(newPaths);
                var index = after != null ? Subtitles.IndexOf(after) + 1 : Subtitles.Count(p => p.StartTime.TotalSeconds <= positionSeconds);
                Subtitles.Insert(index, line);
            }

            Renumber();
        });

        RefreshSubtitlePreview();
        _updateAudioVisualizer = true;
    }

    /// <summary>
    /// Makes sure the drawing's style exists with the chosen colour: an existing style gets the new colour (if changed);
    /// a new name copies the lines' current style (or a plain top-left aligned style without outline/shadow).
    /// </summary>
    private void SaveDrawingStyle(DrawStyleState state)
    {
        var header = string.IsNullOrEmpty(_subtitle.Header) ? AdvancedSubStationAlpha.DefaultHeader : _subtitle.Header;
        var styles = AdvancedSubStationAlpha.GetSsaStylesFromHeader(header);
        var existing = styles.FirstOrDefault(s => s.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            state.Name = existing.Name; // keep the style's own spelling
            if (!state.ColorChanged)
            {
                return;
            }

            existing.Primary = state.Color.ToSkColor();
            _subtitle.Header = AdvancedSubStationAlpha.AddSsaStyle(existing, header); // replaces the style line
            return;
        }

        var template = styles.FirstOrDefault(s => s.Name.Equals(state.TemplateStyleName, StringComparison.OrdinalIgnoreCase));
        var style = template != null
            ? new SsaStyle(template)
            : new SsaStyle { Alignment = "7", MarginLeft = 0, MarginRight = 0, MarginVertical = 0, OutlineWidth = 0, ShadowWidth = 0 };
        style.Name = state.Name;
        style.Primary = state.Color.ToSkColor();
        _subtitle.Header = AdvancedSubStationAlpha.AddSsaStyle(style, header);
    }

    private static double PlayRes(string header, string tag, int fallback) =>
        double.TryParse(AdvancedSubStationAlpha.GetTagValueFromHeader(tag, "[Script Info]", header), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : fallback;

    /// <summary>Draw window result: written into the selected lines (extra shapes become new lines after them).</summary>
    private void ApplyAssaDrawResult(AssaDrawViewModel result, List<SubtitleLineViewModel> selectedItems)
    {
        _subtitle = result.ResultSubtitle;
        var assa = new SubStationAlpha();
        var firstParagraph = selectedItems.FirstOrDefault();
        var lastParagraph = selectedItems.LastOrDefault() ?? new SubtitleLineViewModel
        {
            StartTime = TimeSpan.FromSeconds(firstParagraph != null ? firstParagraph.StartTime.TotalSeconds : 0),
            EndTime = TimeSpan.FromSeconds(firstParagraph != null ? firstParagraph.EndTime.TotalSeconds : 2),
            Text = string.Empty,
        };

        for (var index = 0; index < result.ResultSubtitle.Paragraphs.Count; index++)
        {
            var p = result.ResultSubtitle.Paragraphs[index];
            if (index < selectedItems.Count)
            {
                selectedItems[index].Text = p.Text;
                selectedItems[index].Style = p.Extra;
                selectedItems[index].Layer = p.Layer;
                lastParagraph = selectedItems[index];
            }
            else
            {
                var newP = new SubtitleLineViewModel(p, assa);
                newP.StartTime = lastParagraph.StartTime;
                newP.EndTime = lastParagraph.EndTime;
                var insertIndex = Subtitles.IndexOf(lastParagraph) + 1;
                if (insertIndex <= 0)
                {
                    insertIndex = Subtitles.Count;
                }

                Subtitles.Insert(insertIndex, newP);
                lastParagraph = newP;
            }
        }

        Renumber();
        RefreshSubtitlePreview();
        _updateAudioVisualizer = true;
    }

    private static PenPath Scale(PenPath path, double sx, double sy)
    {
        var scaled = new PenPath();
        foreach (var a in path.Anchors)
        {
            scaled.Anchors.Add(new PenAnchor(a.X * sx, a.Y * sy) { InX = a.InX * sx, InY = a.InY * sy, OutX = a.OutX * sx, OutY = a.OutY * sy });
        }

        return scaled;
    }
}
