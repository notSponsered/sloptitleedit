using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Assa.AssaMotionTracking;

public class MotionTrackItem(MotionTrack track)
{
    public MotionTrack Track { get; } = track;

    public override string ToString() => Track.Samples.Count == 0
        ? "-"
        : $"{new TimeCode(Track.Samples[0].Time * 1000).ToShortDisplayString()} - " +
          $"{new TimeCode(Track.Samples[^1].Time * 1000).ToShortDisplayString()}  ({Track.Samples.Count})";
}

public partial class AssaMotionTrackingViewModel : ObservableObject
{
    // "Record motion" is kept for the session so it can be applied to more lines later
    private static readonly List<MotionTrack> RecordedTracks = [];

    public Window? Window { get; set; }
    public MotionTrackCanvas? Canvas { get; set; }
    public bool OkPressed { get; private set; }
    public MotionTrack? ResultTrack { get; private set; }
    public double ResultReferenceTime { get; private set; }

    [ObservableProperty] private int _frameIndex;
    [ObservableProperty] private int _maxFrameIndex;
    [ObservableProperty] private string _frameInfo = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _referenceInfo = string.Empty;
    [ObservableProperty] private bool _isBusy = true;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _trackScaleAndRotation;
    [ObservableProperty] private ObservableCollection<MotionTrackItem> _tracks = [];
    [ObservableProperty] private MotionTrackItem? _selectedTrack;
    [ObservableProperty] private ObservableCollection<string> _points = [];
    [ObservableProperty] private int _selectedPointIndex = -1;
    [ObservableProperty] private bool _showSubtitlePreview = true;

    private string _videoFileName = string.Empty;
    private int _videoWidth;
    private int _videoHeight;
    private double _spanStart;
    private double _spanEnd;
    private double _referenceTime;
    private int _referenceFrameIndex;
    private string _tempFolder = string.Empty;
    private List<double> _frameTimes = [];
    private List<string> _frameFiles = [];
    private double _toVideoX = 1;
    private double _toVideoY = 1;
    private Bitmap? _frameBitmap;
    private CancellationTokenSource? _cancellation; // tracking (Stop button)
    private readonly CancellationTokenSource _closing = new(); // frame extraction, cancelled when the window closes
    private readonly Dictionary<int, (double X, double Y)[]> _boxCenters = []; // box centers per frame from the last tracking run

    /// <summary>Moved/resized points start over.</summary>
    public void OnBoxesChanged()
    {
        _boxCenters.Clear();
        RefreshPoints();
    }

    public void OnCanvasSelectionChanged()
    {
        if (Canvas != null)
        {
            SelectedPointIndex = Canvas.SelectedIndex;
        }
    }

    private bool _untrackedWarned;
    private Subtitle? _previewSubtitle;

    /// <param name="previewSubtitle">Header + the selected lines, rendered as a preview that follows the track.</param>
    public void Initialize(string videoFileName, int videoWidth, int videoHeight, double spanStart, double spanEnd, double referenceTime,
        Subtitle? previewSubtitle = null)
    {
        _previewSubtitle = previewSubtitle;
        _videoFileName = videoFileName;
        _videoWidth = videoWidth;
        _videoHeight = videoHeight;
        _spanStart = spanStart;
        _spanEnd = spanEnd;
        _referenceTime = referenceTime;
        RefreshTracks(RecordedTracks.LastOrDefault(t => t.VideoFileName == videoFileName));
    }

    public void OnLoaded()
    {
        var l = Se.Language.Assa;
        Status = string.Format(l.MotionExtractingFramesX, 0);
        var cancellationToken = _closing.Token;
        _tempFolder = Path.Combine(Path.GetTempPath(), "SE-motion-" + Guid.NewGuid());
        var start = Math.Max(0, _spanStart - 0.001);
        var duration = _spanEnd - start;
        Task.Run(() =>
        {
            var times = FfmpegGenerator.ExtractFrames(_videoFileName, start, duration, _tempFolder,
                count => Dispatcher.UIThread.Post(() => Status = string.Format(l.MotionExtractingFramesX, count)), cancellationToken);
            Dispatcher.UIThread.Post(() => OnFramesExtracted(times));
        });
    }

    private void OnFramesExtracted(List<double> times)
    {
        if (Window == null || _closing.IsCancellationRequested)
        {
            return;
        }

        _frameFiles = Directory.Exists(_tempFolder)
            ? Directory.GetFiles(_tempFolder, "*.jpg").OrderBy(p => p, StringComparer.Ordinal).Take(times.Count).ToList()
            : [];
        _frameTimes = times.Take(_frameFiles.Count).ToList();
        if (_frameFiles.Count == 0)
        {
            Status = "Unable to extract frames with ffmpeg";
            return;
        }

        using (var first = new Bitmap(_frameFiles[0]))
        {
            if (_videoWidth <= 0 || _videoHeight <= 0)
            {
                _videoWidth = first.PixelSize.Width;
                _videoHeight = first.PixelSize.Height;
            }

            _toVideoX = (double)_videoWidth / first.PixelSize.Width;
            _toVideoY = (double)_videoHeight / first.PixelSize.Height;
        }

        MaxFrameIndex = _frameFiles.Count - 1;
        _referenceFrameIndex = NearestFrame(_referenceTime);
        UpdateReferenceInfo();
        IsBusy = false;
        Status = Se.Language.Assa.MotionDrawBoxHint;
        FrameIndex = _referenceFrameIndex;
        ShowFrame();
        RenderSubtitlePreview();
    }

    /// <summary>
    /// Renders the selected lines once (libass via ffmpeg) at video size; ShowFrame then moves the image with the track.
    /// Fades are removed so the preview isn't invisible at its first frame.
    /// </summary>
    private void RenderSubtitlePreview()
    {
        if (_previewSubtitle == null || _previewSubtitle.Paragraphs.Count == 0)
        {
            return;
        }

        var preview = new Subtitle { Header = _previewSubtitle.Header };
        foreach (var p in _previewSubtitle.Paragraphs)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(p.Text, @"\\fade?\s*\([^)]*\)", string.Empty).Replace("{}", string.Empty);
            preview.Paragraphs.Add(new Paragraph(text, 0, 60_000) { Extra = p.Extra, Layer = p.Layer });
        }

        var (width, height) = (_videoWidth, _videoHeight);
        Task.Run(() =>
        {
            Bitmap? bitmap = null;
            try
            {
                var fileName = FfmpegGenerator.GetScreenShotWithSubtitle(preview, width, height);
                if (fileName != null)
                {
                    bitmap = new Bitmap(fileName);
                    File.Delete(fileName);
                }
            }
            catch
            {
                // no preview then
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (Canvas == null || Window == null)
                {
                    bitmap?.Dispose();
                    return;
                }

                Canvas.SubtitlePreview = bitmap;
                ShowFrame();
            });
        });
    }

    partial void OnFrameIndexChanged(int value) => ShowFrame();

    partial void OnSelectedTrackChanged(MotionTrackItem? value) => ShowFrame();

    partial void OnShowSubtitlePreviewChanged(bool value)
    {
        if (Canvas != null)
        {
            Canvas.ShowSubtitlePreview = value;
            Canvas.InvalidateVisual();
        }
    }

    partial void OnSelectedPointIndexChanged(int value)
    {
        if (Canvas != null && Canvas.SelectedIndex != value)
        {
            Canvas.SelectedIndex = value;
            Canvas.InvalidateVisual();
        }
    }

    private void RefreshPoints()
    {
        if (Canvas == null)
        {
            return;
        }

        var selected = Canvas.SelectedIndex;
        Points = new ObservableCollection<string>(Canvas.Boxes.Select((b, i) =>
            string.Format(Se.Language.Assa.MotionPointX, i + 1, Math.Round(b.Width), Math.Round(b.Height))));
        SelectedPointIndex = selected < Points.Count ? selected : Points.Count - 1;
    }

    [RelayCommand]
    private void AddPoint()
    {
        if (Canvas?.Frame == null || IsBusy)
        {
            return;
        }

        // a box in the middle of the frame (shifted a bit per point so they don't stack) - then move/resize it onto a detail
        var frameWidth = Canvas.Frame.PixelSize.Width;
        var frameHeight = Canvas.Frame.PixelSize.Height;
        var width = Math.Round(frameWidth / 10.0);
        var height = Math.Round(frameHeight / 8.0);
        var offset = 40.0 * Canvas.Boxes.Count;
        Canvas.Boxes.Add(new TrackBox(
            Math.Clamp((frameWidth - width) / 2 + offset, 0, frameWidth - width),
            Math.Clamp((frameHeight - height) / 2 + offset, 0, frameHeight - height),
            width, height));
        Canvas.SelectedIndex = Canvas.Boxes.Count - 1;
        Canvas.IsLost = false;
        Canvas.InvalidateVisual();
        OnBoxesChanged();
    }

    [RelayCommand]
    private void RemovePoint()
    {
        if (Canvas == null || IsBusy || Canvas.SelectedIndex < 0 || Canvas.SelectedIndex >= Canvas.Boxes.Count)
        {
            return;
        }

        Canvas.Boxes.RemoveAt(Canvas.SelectedIndex);
        Canvas.SelectedIndex = Math.Min(Canvas.SelectedIndex, Canvas.Boxes.Count - 1);
        Canvas.IsLost = false;
        Canvas.InvalidateVisual();
        OnBoxesChanged();
    }

    /// <summary>
    /// Preview image (video pixels) -> frame pixels: moved like the applied lines will be,
    /// i.e. by the track from the reference frame to this frame (nearest sample outside the track).
    /// </summary>
    private Matrix SubtitlePreviewTransform()
    {
        var toFrame = Matrix.CreateScale(1 / _toVideoX, 1 / _toVideoY);
        var track = SelectedTrack?.Track;
        if (track == null || track.Samples.Count == 0)
        {
            return toFrame;
        }

        var reference = track.Samples.MinBy(s => Math.Abs(s.Time - _frameTimes[_referenceFrameIndex]));
        var current = track.Samples.MinBy(s => Math.Abs(s.Time - _frameTimes[FrameIndex]));
        var scale = reference.Scale > 0 ? current.Scale / reference.Scale : 1;
        var rotation = (current.Rotation - reference.Rotation) * Math.PI / 180.0;
        return toFrame *
               Matrix.CreateTranslation(-reference.X / _toVideoX, -reference.Y / _toVideoY) *
               Matrix.CreateScale(scale, scale) *
               Matrix.CreateRotation(rotation) *
               Matrix.CreateTranslation(current.X / _toVideoX, current.Y / _toVideoY);
    }

    private void ShowFrame()
    {
        if (Canvas == null || FrameIndex < 0 || FrameIndex >= _frameFiles.Count)
        {
            return;
        }

        var old = _frameBitmap;
        _frameBitmap = new Bitmap(_frameFiles[FrameIndex]);
        Canvas.Frame = _frameBitmap;
        old?.Dispose();

        var time = _frameTimes[FrameIndex];
        var track = SelectedTrack?.Track;
        var sample = track == null ? (MotionSample?)null : FindSample(track, time);
        if (_boxCenters.TryGetValue(FrameIndex, out var centers) && centers.Length == Canvas.Boxes.Count)
        {
            // the boxes follow the last tracking run so drift is easy to spot
            MoveBoxes(Canvas, centers);
            Canvas.IsLost = false;
        }

        Canvas.TrackPath = track?.Samples.Select(p => new Point(p.X / _toVideoX, p.Y / _toVideoY)).ToList() ?? [];
        var reference = track == null ? null : FindSample(track, _frameTimes[_referenceFrameIndex]);
        Canvas.ReferencePoint = reference is { } r ? new Point(r.X / _toVideoX, r.Y / _toVideoY) : null;
        Canvas.SubtitlePreviewTransform = SubtitlePreviewTransform();
        Canvas.InvalidateVisual();

        FrameInfo = string.Format(Se.Language.Assa.MotionFrameInfo, FrameIndex + 1, _frameFiles.Count,
            new TimeCode(time * 1000).ToShortDisplayString(), sample is { } x ? x.Score.ToString("0.00") : "-") +
            (FrameIndex == _referenceFrameIndex ? "   " + Se.Language.Assa.MotionIsReferenceFrame : string.Empty);
    }

    private static MotionSample? FindSample(MotionTrack track, double time)
    {
        foreach (var s in track.Samples)
        {
            if (Math.Abs(s.Time - time) < 0.002)
            {
                return s;
            }
        }

        return null;
    }

    private int NearestFrame(double time)
    {
        var best = 0;
        for (var i = 1; i < _frameTimes.Count; i++)
        {
            if (Math.Abs(_frameTimes[i] - time) < Math.Abs(_frameTimes[best] - time))
            {
                best = i;
            }
        }

        return best;
    }

    private void UpdateReferenceInfo()
    {
        ReferenceInfo = string.Format(Se.Language.Assa.MotionReferenceFrameX, _referenceFrameIndex + 1);
    }

    private void RefreshTracks(MotionTrack? select)
    {
        Tracks = new ObservableCollection<MotionTrackItem>(RecordedTracks
            .Where(t => t.VideoFileName == _videoFileName)
            .Select(t => new MotionTrackItem(t)));
        SelectedTrack = Tracks.FirstOrDefault(t => t.Track == select);
    }

    [RelayCommand]
    private Task TrackForward() => Track(1);

    [RelayCommand]
    private Task TrackBackward() => Track(-1);

    private async Task Track(int direction)
    {
        var l = Se.Language.Assa;
        var canvas = Canvas;
        if (IsBusy || canvas == null || canvas.Boxes.Count == 0 || _frameFiles.Count == 0)
        {
            Status = l.MotionDrawBoxHint;
            return;
        }

        var boxes = canvas.Boxes.ToList();

        var from = FrameIndex;
        var to = direction > 0 ? MaxFrameIndex : 0;
        if (from == to)
        {
            return;
        }

        var files = _frameFiles;
        var startFrame = GrayFrame.FromFile(files[from]);
        if (boxes.Any(b => !MotionTracker.HasEnoughDetail(startFrame, b)))
        {
            Status = l.MotionPickMoreDetailedArea;
            return;
        }

        var track = SelectedTrack?.Track;
        if (track == null)
        {
            track = new MotionTrack { VideoFileName = _videoFileName };
            RecordedTracks.Add(track);
        }

        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        var cancellationToken = _cancellation.Token;
        var total = Math.Abs(to - from) + 1;
        var progress = new Progress<int>(n =>
        {
            Progress = n * 100.0 / total;
            Status = string.Format(l.MotionTrackingXOfY, n, total);
        });
        var scaleAndRotation = TrackScaleAndRotation;
        var baseState = GetBaseState(track, from, direction, boxes, scaleAndRotation);

        List<TrackPoint> points;
        try
        {
            points = await Task.Run(() =>
                MotionTracker.Track(k => GrayFrame.FromFile(files[k]), from, to, boxes, scaleAndRotation, cancellationToken, progress));
        }
        catch (Exception exception)
        {
            // e.g. a frame file that can't be read: report it instead of staying busy forever
            Se.LogError(exception);
            IsBusy = false;
            Progress = 0;
            Status = exception.Message;
            return;
        }

        if (Window == null)
        {
            return; // closed while tracking
        }

        Merge(track, points, baseState);

        foreach (var p in points.Where(p => p.Centers != null))
        {
            _boxCenters[p.Index] = p.Centers!;
        }

        var last = points[^1];
        var lost = !cancellationToken.IsCancellationRequested && last.Index != to;
        if (last.Centers?.Length == canvas.Boxes.Count)
        {
            MoveBoxes(canvas, last.Centers);
        }
        IsBusy = false;
        Progress = 0;
        RefreshTracks(track);
        FrameIndex = lost ? last.Index + direction : last.Index;
        canvas.IsLost = lost;
        ShowFrame();
        Status = lost ? string.Format(l.MotionLostAtFrameX, FrameIndex + 1) : l.MotionCheckPreview;
    }

    [RelayCommand]
    private void GoToReferenceFrame() => FrameIndex = _referenceFrameIndex;

    private readonly record struct BaseState(double X, double Y, double Scale, double Rotation);

    /// <summary>
    /// Where the track already is on the start frame (frame pixels), so a re-seeded box continues the recorded motion
    /// instead of jumping to the new box's center.
    /// </summary>
    private BaseState GetBaseState(MotionTrack track, int from, int direction, List<TrackBox> boxes, bool scaleAndRotation)
    {
        var anchorX = boxes.Average(b => b.CenterX); // the tracker follows the center of all boxes
        var anchorY = boxes.Average(b => b.CenterY);
        if (FindSample(track, _frameTimes[from]) is { } s)
        {
            return new BaseState(s.X / _toVideoX, s.Y / _toVideoY, s.Scale, s.Rotation);
        }

        var neighbor = from - direction;
        if (neighbor >= 0 && neighbor < _frameFiles.Count && FindSample(track, _frameTimes[neighbor]) is { } n)
        {
            // bridge: track the new box one frame back to the last recorded frame, then invert that step
            var step = MotionTracker.Track(k => GrayFrame.FromFile(_frameFiles[k]), from, neighbor, boxes, scaleAndRotation, CancellationToken.None);
            if (step.Count == 2)
            {
                var p = step[1];
                var radians = -p.Rotation * Math.PI / 180.0;
                var vx = (n.X / _toVideoX - p.X) / p.Scale;
                var vy = (n.Y / _toVideoY - p.Y) / p.Scale;
                return new BaseState(
                    anchorX + Math.Cos(radians) * vx - Math.Sin(radians) * vy,
                    anchorY + Math.Sin(radians) * vx + Math.Cos(radians) * vy,
                    n.Scale / p.Scale,
                    n.Rotation - p.Rotation);
            }
        }

        return new BaseState(anchorX, anchorY, 1, 0);
    }

    private static void MoveBoxes(MotionTrackCanvas canvas, (double X, double Y)[] centers)
    {
        for (var i = 0; i < centers.Length; i++)
        {
            var b = canvas.Boxes[i];
            canvas.Boxes[i] = b with { Left = centers[i].X - b.Width / 2, Top = centers[i].Y - b.Height / 2 };
        }
    }

    private void Merge(MotionTrack track, List<TrackPoint> points, BaseState baseState)
    {
        var vx = baseState.X - points[0].X;
        var vy = baseState.Y - points[0].Y;
        var samples = points.Select(p =>
        {
            var radians = p.Rotation * Math.PI / 180.0;
            var x = p.X + p.Scale * (Math.Cos(radians) * vx - Math.Sin(radians) * vy);
            var y = p.Y + p.Scale * (Math.Sin(radians) * vx + Math.Cos(radians) * vy);
            return new MotionSample(_frameTimes[p.Index], x * _toVideoX, y * _toVideoY,
                baseState.Scale * p.Scale, baseState.Rotation + p.Rotation, p.Score);
        }).ToList();

        // re-tracking overwrites the frames it covered
        var minTime = samples.Min(s => s.Time) - 0.002;
        var maxTime = samples.Max(s => s.Time) + 0.002;
        track.Samples.RemoveAll(s => s.Time >= minTime && s.Time <= maxTime);
        track.Samples.AddRange(samples);
        track.Samples.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    [RelayCommand]
    private void Stop()
    {
        _cancellation?.Cancel();
    }

    [RelayCommand]
    private void NewTrack()
    {
        SelectedTrack = null;
    }

    [RelayCommand]
    private void DeleteTrack()
    {
        if (SelectedTrack != null)
        {
            RecordedTracks.Remove(SelectedTrack.Track);
            RefreshTracks(null);
        }
    }

    [RelayCommand]
    private void UseThisFrame()
    {
        _referenceFrameIndex = FrameIndex;
        UpdateReferenceInfo();
        ShowFrame();
    }

    [RelayCommand]
    private void FirstFrame() => FrameIndex = 0;

    [RelayCommand]
    private void PreviousFrame() => FrameIndex = Math.Max(0, FrameIndex - 1);

    [RelayCommand]
    private void NextFrame() => FrameIndex = Math.Min(MaxFrameIndex, FrameIndex + 1);

    [RelayCommand]
    private void LastFrame() => FrameIndex = MaxFrameIndex;

    [RelayCommand]
    private void Apply()
    {
        var track = SelectedTrack?.Track;
        if (IsBusy || track == null || track.Samples.Count == 0)
        {
            Status = Se.Language.Assa.MotionNoTrack;
            return;
        }

        // frames without tracking data just hold the nearest tracked position - say so once before applying
        var untracked = _frameTimes.Count(t => FindSample(track, t) == null);
        if (untracked > 0 && !_untrackedWarned)
        {
            _untrackedWarned = true;
            Status = string.Format(Se.Language.Assa.MotionUntrackedFramesX, untracked);
            return;
        }

        ResultTrack = track;
        ResultReferenceTime = _frameTimes[_referenceFrameIndex];
        OkPressed = true;
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Window?.Close();
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Window?.Close();
        }
        else if (e.Key == Key.Left && !IsBusy)
        {
            e.Handled = true;
            PreviousFrame();
        }
        else if (e.Key == Key.Right && !IsBusy)
        {
            e.Handled = true;
            NextFrame();
        }
    }

    public void OnClosing()
    {
        _cancellation?.Cancel();
        _closing.Cancel();
        Window = null;
        if (Canvas != null)
        {
            Canvas.Frame = null;
            Canvas.SubtitlePreview?.Dispose();
            Canvas.SubtitlePreview = null;
        }

        _frameBitmap?.Dispose();
        _frameBitmap = null;

        // ffmpeg may still be finishing - clean up in the background
        var folder = _tempFolder;
        Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 10 && Directory.Exists(folder); attempt++)
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }
}
