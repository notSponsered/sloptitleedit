using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Assa.AssaDraw;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionShapes;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Assa.AssaMotionShapes;

/// <summary>
/// Keyframed shape that follows the video: draw the shape on some frames (keys); Calculate fits it to the object's outline on
/// every frame (<see cref="ShapeFitter"/>); Apply writes it as few lines as possible (holds + straight moves).
/// </summary>
public partial class AssaMotionShapesViewModel : ObservableObject
{
    // the last keys per video + span, so Undo -> reopen -> fix -> Apply doesn't start over
    private static readonly Dictionary<(string Video, double Start, double End), List<(double Time, PenPath Path)>> SavedKeys = [];

    public Window? Window { get; set; }
    public VideoDrawCanvas? Canvas { get; set; }
    public ShapeTimeline? Timeline { get; set; }

    public bool OkPressed { get; private set; }
    public List<ShapeFrame> ResultFrames { get; private set; } = [];
    public List<double> ResultTimesMs { get; private set; } = [];
    public List<ShapeSegment> ResultSegments { get; private set; } = [];
    public bool ResultUsesColor { get; private set; }
    public double ResultSimplify => Simplify;
    public string ResultStyleName => StyleName;
    public Color ResultColor => _color;
    public bool NameChanged { get; private set; }
    public bool ColorChanged { get; private set; }
    public int VideoWidth => _videoWidth;
    public int VideoHeight => _videoHeight;

    [ObservableProperty] private int _frameIndex;
    [ObservableProperty] private int _maxFrameIndex;
    [ObservableProperty] private string _frameInfo = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _stats = string.Empty;
    [ObservableProperty] private bool _isBusy = true;
    [ObservableProperty] private bool _followMotion = true;
    [ObservableProperty] private double _simplify = ShapeLines.DefaultSimplify;
    [ObservableProperty] private string _keyCount = string.Empty;
    [ObservableProperty] private bool _trackColor;
    [ObservableProperty] private string _styleName = string.Empty;
    [ObservableProperty] private IBrush _styleBrush = Brushes.White;

    private readonly SortedDictionary<int, PenShape> _keys = [];
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenSource? _cancellation;
    private List<ShapeFrame>? _result;
    private List<ShapeSegment> _segments = [];
    private DispatcherTimer? _playTimer;
    private int _shownFrame = -1;
    private Color _color = Colors.White;
    private Func<string, Color?> _styleColor = _ => null;
    private string _videoFileName = string.Empty;
    private int _videoWidth;
    private int _videoHeight;
    private double _spanStart;
    private double _spanEnd;
    private double _referenceTime;
    private PenPath? _sourcePath;
    private string _header = string.Empty;
    private string _tempFolder = string.Empty;
    private List<double> _frameTimes = [];
    private List<string> _frameFiles = [];
    private double _toVideoX = 1;
    private double _toVideoY = 1;
    private Bitmap? _frameBitmap;

    /// <param name="sourcePath">A selected drawing line's shape (video px) - the first key, at the reference frame.</param>
    /// <param name="styleColor">Primary colour of an existing style by name (null = no such style).</param>
    public void Initialize(string videoFileName, int videoWidth, int videoHeight, double spanStart, double spanEnd, double referenceTime,
        PenPath? sourcePath, string styleName, Color color, Func<string, Color?> styleColor, string header)
    {
        _videoFileName = videoFileName;
        _videoWidth = videoWidth;
        _videoHeight = videoHeight;
        _spanStart = spanStart;
        _spanEnd = spanEnd;
        _referenceTime = referenceTime;
        _sourcePath = sourcePath;
        _header = header;
        StyleName = styleName;
        _styleColor = styleColor;
        SetColor(color);
    }

    private void SetColor(Color color)
    {
        _color = color;
        StyleBrush = new SolidColorBrush(color);
        Canvas?.SetColor(color);
    }

    public void OnLoaded()
    {
        var l = Se.Language.Assa;
        Status = string.Format(l.MotionExtractingFramesX, 0);
        var cancellationToken = _closing.Token;
        _tempFolder = Path.Combine(Path.GetTempPath(), "SE-shapes-" + Guid.NewGuid());
        var start = Math.Max(0, _spanStart - 0.001);
        var duration = _spanEnd - start;
        Task.Run(() =>
        {
            List<double> times;
            try
            {
                times = FfmpegGenerator.ExtractFrames(_videoFileName, start, duration, _tempFolder,
                    count => Dispatcher.UIThread.Post(() => Status = string.Format(l.MotionExtractingFramesX, count)), cancellationToken);
            }
            catch (Exception exception)
            {
                if (exception is not OperationCanceledException)
                {
                    Se.LogError(exception, "Motion shapes: extracting frames failed");
                }

                times = []; // shown as "Unable to extract frames"
            }

            Dispatcher.UIThread.Post(() => OnFramesExtracted(times));
        });
    }

    private void OnFramesExtracted(List<double> times)
    {
        if (Window == null || Canvas == null || _closing.IsCancellationRequested)
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

        Canvas.VideoWidth = _videoWidth;
        Canvas.VideoHeight = _videoHeight;
        Canvas.MaxShapes = 1;
        Canvas.FitView();

        if (SavedKeys.TryGetValue((_videoFileName, _spanStart, _spanEnd), out var saved))
        {
            foreach (var (time, path) in saved)
            {
                _keys[NearestFrame(time)] = NewShape(path.Clone());
            }
        }
        else if (_sourcePath != null)
        {
            _keys[NearestFrame(_referenceTime)] = NewShape(_sourcePath);
        }

        MaxFrameIndex = _frameFiles.Count - 1;
        IsBusy = false;
        Status = Se.Language.Assa.ShapesHint;
        var startFrame = _keys.Count > 0 ? _keys.Keys.First() : NearestFrame(_referenceTime);
        if (FrameIndex == startFrame)
        {
            ShowFrame();
        }
        else
        {
            FrameIndex = startFrame;
        }
    }

    private PenShape NewShape(PenPath path) => new() { Path = path, Color = _color };

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

    partial void OnFrameIndexChanged(int value) => ShowFrame();

    private void ShowFrame()
    {
        if (Canvas == null || FrameIndex < 0 || FrameIndex >= _frameFiles.Count)
        {
            return;
        }

        Canvas.CloseActive(); // a shape being drawn is kept as the key of the frame it was drawn on
        _shownFrame = FrameIndex;
        var old = _frameBitmap;
        _frameBitmap = new Bitmap(_frameFiles[FrameIndex]);
        Canvas.Frame = _frameBitmap;
        old?.Dispose();

        Canvas.Reset();
        Canvas.NewShapeColor = _color;
        var isKey = _keys.TryGetValue(FrameIndex, out var key);
        if (isKey)
        {
            key!.Color = _color;
            Canvas.Shapes.Add(key);
        }
        else if (_result != null)
        {
            // the computed shape, editable: the first edit makes it a key
            Canvas.Shapes.Add(NewShape(_result[FrameIndex].Path.Clone()));
        }

        // onion skin: the neighbouring keys
        foreach (var neighbour in new[] { _keys.Keys.LastOrDefault(k => k < FrameIndex, -1), _keys.Keys.FirstOrDefault(k => k > FrameIndex, -1) })
        {
            if (neighbour >= 0)
            {
                Canvas.Overlays.Add((_keys[neighbour].Path, _color, true));
            }
        }

        Canvas.InvalidateVisual();
        RefreshTimeline();
        FrameInfo = string.Format(Se.Language.Assa.ShapesFrameInfo, FrameIndex + 1, _frameFiles.Count,
            new TimeCode(_frameTimes[FrameIndex] * 1000).ToShortDisplayString()) + (isKey ? "   " + Se.Language.Assa.ShapesIsKey : string.Empty);
    }

    private void RefreshTimeline()
    {
        if (Timeline == null)
        {
            return;
        }

        Timeline.FrameCount = _frameFiles.Count;
        Timeline.FrameIndex = FrameIndex;
        Timeline.Keys = _keys.Keys.ToList();
        Timeline.Interpolated = _result?.Select(f => f.Interpolated).ToList();
        Timeline.InvalidateVisual();
        KeyCount = string.Format(Se.Language.Assa.ShapesKeyCountX, _keys.Count);
    }

    /// <summary>"24 frames → 9 lines · ~12 points per line" for the current result and Simplify value.</summary>
    private void UpdateStats()
    {
        if (_result == null || _segments.Count == 0)
        {
            Stats = string.Empty;
            return;
        }

        var (sx, sy) = ShapeLines.ScriptScale(_header, _videoWidth, _videoHeight);
        var points = _segments.Average(s => ShapeSimplify.PointCount(ShapeSimplify.Simplify(_result[s.First].Path, Simplify / Math.Max(sx, sy))));
        Stats = string.Format(Se.Language.Assa.ShapesFramesToLinesXY, _result.Count, _segments.Count) + "\n" +
                string.Format(Se.Language.Assa.ShapesPointsPerLineX, Math.Round(points));
    }

    partial void OnSimplifyChanged(double value) => UpdateStats();

    /// <summary>The shape on the canvas is the key of the frame shown; no shape = no key.</summary>
    public void OnCanvasChanged()
    {
        if (Canvas == null || _shownFrame < 0)
        {
            return;
        }

        if (Canvas.Shapes.Count > 0)
        {
            _keys[_shownFrame] = Canvas.Shapes[0];
        }
        else
        {
            _keys.Remove(_shownFrame);
        }

        InvalidateResult();
        RefreshTimeline();
    }

    private void InvalidateResult()
    {
        _result = null;
        _segments = [];
        Stats = string.Empty;
    }

    partial void OnFollowMotionChanged(bool value) => InvalidateResult();


    partial void OnTrackColorChanged(bool value) => InvalidateResult();

    public void Seek(int frame)
    {
        if (!IsBusy)
        {
            FrameIndex = Math.Clamp(frame, 0, MaxFrameIndex);
        }
    }

    /// <summary>K: keeps the shape shown (the computed one) as a key, or copies the nearest key here to adjust.</summary>
    [RelayCommand]
    private void AddKey()
    {
        if (Canvas == null || IsBusy || _shownFrame < 0 || _keys.ContainsKey(_shownFrame))
        {
            return;
        }

        Canvas.CloseActive();
        if (_keys.ContainsKey(_shownFrame))
        {
            return; // the open shape just became the key
        }

        if (Canvas.Shapes.Count > 0)
        {
            _keys[_shownFrame] = Canvas.Shapes[0]; // same shape as computed - the result stays valid
        }
        else if (_keys.Count > 0)
        {
            var nearest = _keys.Keys.MinBy(k => Math.Abs(k - _shownFrame));
            _keys[_shownFrame] = NewShape(_keys[nearest].Path.Clone());
            InvalidateResult();
        }

        ShowFrame();
    }

    [RelayCommand]
    private void DeleteKey()
    {
        if (IsBusy || Canvas == null)
        {
            return;
        }

        // also drops a key reopened with Backspace (open shape, already out of _keys) instead of closing it again
        Canvas.Reset();
        _keys.Remove(_shownFrame);
        InvalidateResult();
        ShowFrame();
    }

    [RelayCommand]
    private async Task Calculate()
    {
        var l = Se.Language.Assa;
        if (Canvas == null || IsBusy)
        {
            return;
        }

        StopPlaying();
        Canvas.CloseActive();
        if (_keys.Count == 0)
        {
            Status = l.ShapesNeedTwoKeys;
            return;
        }

        var keys = _keys.Select(k => new ShapeKey(k.Key, k.Value.Path.Clone())).ToList();
        var timesMs = _frameTimes.Select(t => t * 1000).ToList();
        var files = _frameFiles;
        var useColor = TrackColor;
        IsBusy = true;
        Status = l.ShapesCalculate + "...";
        _cancellation = new CancellationTokenSource();
        var cancellationToken = _cancellation.Token;
        var video = FollowMotion ? new ShapeVideo(k => RgbFrame.FromFile(files[k]), files.Count, _toVideoX, _toVideoY) : null;


        try
        {
            var frames = await Task.Run(() => ShapeTracker.Compute(keys, timesMs, video, false, cancellationToken), cancellationToken);
            if (useColor)
            {
                var bt709 = ShapeColor.NeedsBt709Correction(_header, _videoWidth, _videoHeight);
                var (toVideoX, toVideoY) = (_toVideoX, _toVideoY);
                await Task.Run(() =>
                {
                    var colors = new SKColor?[frames.Count];
                    for (var i = 0; i < frames.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var bitmap = SKBitmap.Decode(files[i]);
                        colors[i] = bitmap == null ? null : ShapeColor.Sample(bitmap, frames[i].Path, toVideoX, toVideoY);
                    }

                    var smooth = ShapeColor.Smooth(colors);
                    for (var i = 0; i < frames.Count; i++)
                    {
                        frames[i].Color = bt709 ? ShapeColor.To709(smooth[i]) : smooth[i];
                    }
                }, cancellationToken);
            }

            if (Window == null)
            {
                return; // closed meanwhile
            }

            _result = frames;
            _segments = ShapeLines.Segment(frames, timesMs, useColor);
            UpdateStats();
            var lost = frames.FindIndex(f => f.Interpolated);
            Status = lost >= 0 ? string.Format(l.ShapesNotTrackedFromX, lost + 1) : string.Empty;
            IsBusy = false;
            if (lost >= 0 && FrameIndex != lost)
            {
                FrameIndex = lost;
            }
            else
            {
                ShowFrame();
            }
        }
        catch (OperationCanceledException)
        {
            Status = string.Empty;
        }
        catch (Exception exception)
        {
            Se.LogError(exception);
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Stop() => _cancellation?.Cancel();

    [RelayCommand]
    private void Play()
    {
        if (_playTimer != null)
        {
            StopPlaying();
            return;
        }

        if (IsBusy || _frameTimes.Count < 2)
        {
            return;
        }

        if (FrameIndex >= MaxFrameIndex)
        {
            FrameIndex = 0;
        }

        var frameMs = (_frameTimes[^1] - _frameTimes[0]) * 1000 / (_frameTimes.Count - 1);
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(frameMs, 10, 200)) };
        _playTimer.Tick += (_, _) =>
        {
            if (FrameIndex >= MaxFrameIndex)
            {
                StopPlaying();
            }
            else
            {
                FrameIndex++;
            }
        };
        _playTimer.Start();
    }

    private void StopPlaying()
    {
        _playTimer?.Stop();
        _playTimer = null;
    }

    [RelayCommand]
    private void FirstFrame() => Seek(0);

    [RelayCommand]
    private void PreviousFrame() => Seek(FrameIndex - 1);

    [RelayCommand]
    private void NextFrame() => Seek(FrameIndex + 1);

    [RelayCommand]
    private void LastFrame() => Seek(MaxFrameIndex);

    [RelayCommand]
    private void PreviousKey()
    {
        var key = _keys.Keys.LastOrDefault(k => k < FrameIndex, -1);
        if (key >= 0)
        {
            Seek(key);
        }
    }

    [RelayCommand]
    private void NextKey()
    {
        var key = _keys.Keys.FirstOrDefault(k => k > FrameIndex, -1);
        if (key >= 0)
        {
            Seek(key);
        }
    }

    [RelayCommand]
    private async Task PickColor()
    {
        if (Window == null)
        {
            return;
        }

        var picker = new Shared.ColorPicker.ColorPickerViewModel();
        picker.Initialize(_color);
        picker.ShowAlpha = false;
        await new Shared.ColorPicker.ColorPickerWindow(picker).ShowDialog(Window);
        if (picker.OkPressed)
        {
            SetColor(Color.FromRgb(picker.SelectedColor.R, picker.SelectedColor.G, picker.SelectedColor.B));
            ColorChanged = true;
        }

        Canvas?.Focus();
    }

    /// <summary>Name box left: no commas in a style name; an existing style brings its colour.</summary>
    public void CommitStyleName(string? text)
    {
        var name = text?.Trim().Replace(",", string.Empty) ?? string.Empty;
        if (name.Length == 0)
        {
            OnPropertyChanged(nameof(StyleName)); // puts the old name back in the box
            return;
        }

        if (name == StyleName && !NameChanged)
        {
            return;
        }

        StyleName = name;
        NameChanged = true;
        if (!ColorChanged && _styleColor(name) is { } color)
        {
            SetColor(color);
        }
    }

    [RelayCommand]
    private async Task Apply()
    {
        if (IsBusy)
        {
            return;
        }

        if (_result == null)
        {
            await Calculate();
            if (_result == null)
            {
                return;
            }
        }

        ResultFrames = _result;
        ResultTimesMs = _frameTimes.Select(t => t * 1000).ToList();
        ResultSegments = _segments;
        ResultUsesColor = TrackColor && _result.Any(f => f.Color != null);
        OkPressed = true;
        Window?.Close();
    }

    [RelayCommand]
    private void Cancel() => Window?.Close();

    /// <summary>Window keys (tunnel, so focused check boxes/buttons don't eat Space). Typing in the name box is left alone.</summary>
    public void OnKeyDown(KeyEventArgs e)
    {
        if (e.Source is TextBox || Canvas == null)
        {
            return;
        }

        if (IsBusy)
        {
            // no edits while calculating (the result would be from the old keys); Esc still closes (cancels)
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Window?.Close();
            }

            return;
        }

        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.Space:
                Play();
                break;
            case Key.K:
                AddKey();
                break;
            case Key.F5:
            case Key.Enter when Canvas.Active == null:
                _ = Calculate();
                break;
            case Key.Left when alt:
                PreviousKey();
                break;
            case Key.Right when alt:
                NextKey();
                break;
            case Key.Left:
                PreviousFrame();
                break;
            case Key.Right:
                NextFrame();
                break;
            case Key.Escape when Canvas.Active == null:
                Window?.Close();
                break;
            case Key.Back or Key.Delete or Key.Enter or Key.Home or Key.Escape:
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                Canvas.HandleKey(e);
                return;
            default:
                return;
        }

        e.Handled = true;
    }

    public void OnClosing()
    {
        StopPlaying();
        _cancellation?.Cancel();
        _closing.Cancel();
        Canvas?.CloseActive(); // an open/reopened shape is saved as its key
        Window = null;
        if (_frameTimes.Count > 0)
        {
            SavedKeys[(_videoFileName, _spanStart, _spanEnd)] = _keys
                .Where(k => k.Key < _frameTimes.Count)
                .Select(k => (_frameTimes[k.Key], k.Value.Path.Clone()))
                .ToList();
        }

        if (Canvas != null)
        {
            Canvas.Frame = null;
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
