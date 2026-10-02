using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Assa;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Assa.AssaDraw;

/// <summary>A shape drawn on the video: pen path (video pixels), display colour and the subtitle line it belongs to.</summary>
public sealed class PenShape
{
    public PenPath Path { get; init; } = new();
    public Color Color { get; set; } = Colors.White;

    /// <summary>The subtitle line the shape is written to (null = a new line).</summary>
    public object? Tag { get; init; }
}

/// <summary>
/// Drawing directly on the paused video frame with a pen tool (the convention of Figma / Inkscape / Illustrator):
/// click = corner point, click + drag = curved point, click the first point = close the shape, Backspace = undo a point.
/// Mouse wheel zooms at the cursor, middle mouse pans. Coordinates of shapes are video pixels.
/// </summary>
public class VideoDrawCanvas : Control
{
    private const double HitRadius = 7; // screen pixels
    private const double DragThreshold = 3;

    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(79, 195, 247));
    private static readonly IPen ActivePen = new Pen(Accent, 2);
    private static readonly IPen RubberBandPen = new Pen(Accent, 1.5) { DashStyle = new DashStyle([4, 3], 0) };
    private static readonly IPen ClosePreviewPen = new Pen(new SolidColorBrush(Colors.White, 0.5), 1) { DashStyle = new DashStyle([2, 4], 0) };
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Colors.White, 0.7), 1);
    private static readonly IPen PointOutline = new Pen(Brushes.Black, 1);
    private static readonly IBrush HintBackground = new SolidColorBrush(Colors.Black, 0.55);

    private enum DragMode { None, NewPoint, Anchor, HandleIn, HandleOut, Pan }

    private double _zoom = 1;
    private double _panX;
    private double _panY;
    private bool _userView; // the user zoomed/panned - don't refit on resize
    private DragMode _drag;
    private PenAnchor? _dragAnchor;
    private Point _dragStartScreen;
    private Point _lastScreen;
    private Point _grabOffset;
    private Point? _mouse;
    private PenAnchor? _selected;
    private bool _escArmed;

    public Bitmap? Frame { get; set; }
    public int VideoWidth { get; set; } = 1920;
    public int VideoHeight { get; set; } = 1080;

    /// <summary>Finished (closed) shapes.</summary>
    public List<PenShape> Shapes { get; } = [];

    /// <summary>The shape being drawn (open), or null.</summary>
    public PenShape? Active { get; private set; }

    public bool IsModified { get; private set; }

    /// <summary>New shapes get this colour and line (Tag).</summary>
    public Color NewShapeColor { get; set; } = Colors.White;
    public object? NewShapeTag { get; set; }

    /// <summary>Shows every shape (and new ones) in this colour - they all get the same style.</summary>
    public void SetColor(Color color)
    {
        NewShapeColor = color;
        foreach (var shape in AllShapes())
        {
            shape.Color = color;
        }

        InvalidateVisual();
    }

    /// <summary>Grid over the frame; points snap to it while it's shown.</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            InvalidateVisual();
        }
    }

    public int GridSize { get; set; } = DrawSettings.GridSize;

    private bool _showGrid;

    private Point Snap(Point video) => ShowGrid && GridSize > 0
        ? new Point(Math.Round(video.X / GridSize) * GridSize, Math.Round(video.Y / GridSize) * GridSize)
        : video;

    public event EventHandler? DoneRequested;
    public event EventHandler? CancelRequested;

    public void RaiseDone()
    {
        CloseActive(); // a shape still being drawn counts if it can be closed
        DoneRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseCancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    public VideoDrawCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    private Point ToScreen(double x, double y) => new(x * _zoom + _panX, y * _zoom + _panY);

    private Point ToVideo(Point p) => new((p.X - _panX) / _zoom, (p.Y - _panY) / _zoom);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Whole frame visible and centred.</summary>
    public void FitView()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || VideoWidth <= 0 || VideoHeight <= 0)
        {
            return;
        }

        _zoom = Math.Min(Bounds.Width / VideoWidth, Bounds.Height / VideoHeight);
        _panX = (Bounds.Width - VideoWidth * _zoom) / 2;
        _panY = (Bounds.Height - VideoHeight * _zoom) / 2;
        _userView = false;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && !_userView)
        {
            FitView();
        }
    }

    // ----------------------------------------------------------------- hit testing

    private IEnumerable<PenShape> AllShapes() => Active == null ? Shapes : Shapes.Append(Active);

    private bool IsOverFirstPoint(Point screen) =>
        Active is { Path.Anchors.Count: >= 3 } a &&
        Distance(screen, ToScreen(a.Path.Anchors[0].X, a.Path.Anchors[0].Y)) <= HitRadius * 1.6;

    private (PenShape Shape, PenAnchor Anchor)? AnchorAt(Point screen)
    {
        foreach (var shape in AllShapes().Reverse())
        {
            foreach (var anchor in shape.Path.Anchors)
            {
                if (Distance(screen, ToScreen(anchor.X, anchor.Y)) <= HitRadius)
                {
                    return (shape, anchor);
                }
            }
        }

        return null;
    }

    /// <summary>Handles are shown (and grabbable) for the shape being drawn and for the selected point.</summary>
    private IEnumerable<PenAnchor> AnchorsWithVisibleHandles()
    {
        var anchors = Active?.Path.Anchors.Where(a => a.IsSmooth) ?? [];
        return _selected is { IsSmooth: true } s ? anchors.Append(s) : anchors;
    }

    private (PenAnchor Anchor, bool IsOut)? HandleAt(Point screen)
    {
        foreach (var anchor in AnchorsWithVisibleHandles())
        {
            if (Distance(screen, ToScreen(anchor.OutX, anchor.OutY)) <= HitRadius)
            {
                return (anchor, true);
            }

            if (Distance(screen, ToScreen(anchor.InX, anchor.InY)) <= HitRadius)
            {
                return (anchor, false);
            }
        }

        return null;
    }

    // ----------------------------------------------------------------- editing

    private void CloseActive()
    {
        if (Active is { Path.Anchors.Count: >= 3 })
        {
            Shapes.Add(Active);
            Active = null;
            _selected = null;
            IsModified = true;
        }
    }

    /// <summary>Backspace: removes the last point; with no shape in progress the last shape is reopened (undoing its close).</summary>
    public void UndoPoint()
    {
        if (Active != null)
        {
            Active.Path.Anchors.RemoveAt(Active.Path.Anchors.Count - 1);
            if (Active.Path.Anchors.Count == 0)
            {
                Active = null;
            }
        }
        else if (Shapes.Count > 0)
        {
            Active = Shapes[^1];
            Shapes.RemoveAt(Shapes.Count - 1);
        }

        _selected = null;
        IsModified = true;
        InvalidateVisual();
    }

    private void DeleteAnchor(PenShape shape, PenAnchor anchor)
    {
        shape.Path.Anchors.Remove(anchor);
        if (shape == Active)
        {
            if (shape.Path.Anchors.Count == 0)
            {
                Active = null;
            }
        }
        else if (shape.Path.Anchors.Count < 3)
        {
            Shapes.Remove(shape); // a closed shape needs 3 points to have an area
        }

        if (_selected == anchor)
        {
            _selected = null;
        }

        IsModified = true;
        InvalidateVisual();
    }

    /// <summary>Double-click a point: curved -> corner, corner -> curved (handles along the line between its neighbours).</summary>
    private void ToggleCurve(PenShape shape, PenAnchor anchor)
    {
        if (anchor.IsSmooth)
        {
            anchor.MakeCorner();
        }
        else
        {
            var anchors = shape.Path.Anchors;
            var i = anchors.IndexOf(anchor);
            var closed = shape != Active;
            var previous = i > 0 ? anchors[i - 1] : closed ? anchors[^1] : anchor;
            var next = i < anchors.Count - 1 ? anchors[i + 1] : closed ? anchors[0] : anchor;
            var dx = next.X - previous.X;
            var dy = next.Y - previous.Y;
            if (dx == 0 && dy == 0)
            {
                return; // nothing to align the handles with
            }

            anchor.SetOutMirrored(anchor.X + dx / 6, anchor.Y + dy / 6);
        }

        _selected = anchor;
        IsModified = true;
        InvalidateVisual();
    }

    private PenShape? ShapeOf(PenAnchor anchor) => AllShapes().FirstOrDefault(s => s.Path.Anchors.Contains(anchor));

    /// <summary>Keys from the host (main window shortcuts are off while drawing). Sets Handled for keys it uses.</summary>
    public void HandleKey(KeyEventArgs e)
    {
        var escArmed = _escArmed;
        _escArmed = false;
        switch (e.Key)
        {
            case Key.Back:
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                UndoPoint();
                break;
            case Key.Enter:
                if (Active != null)
                {
                    CloseActive();
                }
                else
                {
                    RaiseDone();
                }

                break;
            case Key.Escape:
                if (Active != null)
                {
                    Active = null;
                    _selected = null;
                }
                else if (IsModified && !escArmed)
                {
                    _escArmed = true; // second Esc discards
                }
                else
                {
                    CancelRequested?.Invoke(this, EventArgs.Empty);
                }

                break;
            case Key.Delete:
                if (_selected != null && ShapeOf(_selected) is { } shape)
                {
                    DeleteAnchor(shape, _selected);
                }

                break;
            case Key.Home:
                FitView();
                break;
            default:
                return;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    // ----------------------------------------------------------------- mouse

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _escArmed = false;
        var p = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        e.Handled = true;

        if (properties.IsMiddleButtonPressed)
        {
            _drag = DragMode.Pan;
            _lastScreen = p;
            e.Pointer.Capture(this);
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            if (AnchorAt(p) is { } hit)
            {
                DeleteAnchor(hit.Shape, hit.Anchor);
            }

            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        if (IsOverFirstPoint(p))
        {
            CloseActive();
            InvalidateVisual();
            return;
        }

        if (HandleAt(p) is { } handle)
        {
            _drag = handle.IsOut ? DragMode.HandleOut : DragMode.HandleIn;
            _dragAnchor = handle.Anchor;
            e.Pointer.Capture(this);
            return;
        }

        if (AnchorAt(p) is { } anchorHit)
        {
            if (e.ClickCount == 2)
            {
                ToggleCurve(anchorHit.Shape, anchorHit.Anchor);
                return;
            }

            _selected = anchorHit.Anchor;
            _drag = DragMode.Anchor;
            _dragAnchor = anchorHit.Anchor;
            var v = ToVideo(p);
            _grabOffset = new Point(v.X - anchorHit.Anchor.X, v.Y - anchorHit.Anchor.Y);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        // add a point (a new shape starts with its first point)
        var position = Snap(ToVideo(p));
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && Active is { Path.Anchors.Count: > 0 } a)
        {
            position = Constrain45(a.Path.Anchors[^1], position);
        }

        Active ??= new PenShape { Color = NewShapeColor, Tag = NewShapeTag };
        var anchor = new PenAnchor(position.X, position.Y);
        Active.Path.Anchors.Add(anchor);
        _selected = anchor;
        _drag = DragMode.NewPoint;
        _dragAnchor = anchor;
        _dragStartScreen = p;
        IsModified = true;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    private static Point Constrain45(PenAnchor from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return new Point(from.X + Math.Cos(angle) * length, from.Y + Math.Sin(angle) * length);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        _mouse = p;
        var v = ToVideo(p);
        var symmetric = !e.KeyModifiers.HasFlag(KeyModifiers.Alt); // Alt breaks the handle symmetry (sharp curve)

        switch (_drag)
        {
            case DragMode.Pan:
                _panX += p.X - _lastScreen.X;
                _panY += p.Y - _lastScreen.Y;
                _lastScreen = p;
                _userView = true;
                break;
            case DragMode.NewPoint when _dragAnchor != null:
                if (Distance(p, _dragStartScreen) > DragThreshold)
                {
                    _dragAnchor.SetOutMirrored(v.X, v.Y); // dragging while placing makes a curve
                }

                break;
            case DragMode.Anchor when _dragAnchor != null:
                var moved = Snap(new Point(v.X - _grabOffset.X, v.Y - _grabOffset.Y));
                _dragAnchor.MoveTo(moved.X, moved.Y);
                IsModified = true;
                break;
            case DragMode.HandleOut when _dragAnchor != null:
                _dragAnchor.OutX = v.X;
                _dragAnchor.OutY = v.Y;
                if (symmetric)
                {
                    _dragAnchor.InX = 2 * _dragAnchor.X - v.X;
                    _dragAnchor.InY = 2 * _dragAnchor.Y - v.Y;
                }

                IsModified = true;
                break;
            case DragMode.HandleIn when _dragAnchor != null:
                _dragAnchor.InX = v.X;
                _dragAnchor.InY = v.Y;
                if (symmetric)
                {
                    _dragAnchor.OutX = 2 * _dragAnchor.X - v.X;
                    _dragAnchor.OutY = 2 * _dragAnchor.Y - v.Y;
                }

                IsModified = true;
                break;
            default:
                Cursor = new Cursor(IsOverFirstPoint(p) ? StandardCursorType.Hand
                    : HandleAt(p) != null || AnchorAt(p) != null ? StandardCursorType.SizeAll
                    : StandardCursorType.Cross);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = DragMode.None;
        _dragAnchor = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouse = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
        var zoom = Math.Clamp(_zoom * factor, 0.05, 40);
        // keep the video point under the cursor where it is
        _panX = p.X - (p.X - _panX) * zoom / _zoom;
        _panY = p.Y - (p.Y - _panY) * zoom / _zoom;
        _zoom = zoom;
        _userView = true;
        e.Handled = true;
        InvalidateVisual();
    }

    // ----------------------------------------------------------------- rendering

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var videoRect = new Rect(ToScreen(0, 0), new Size(VideoWidth * _zoom, VideoHeight * _zoom));
        if (Frame != null)
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _zoom > 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
            {
                context.DrawImage(Frame, new Rect(Frame.Size), videoRect);
            }
        }

        if (ShowGrid && GridSize > 0 && GridSize * _zoom >= 4)
        {
            var gridPen = new Pen(new SolidColorBrush(Colors.White, 0.18), 1);
            for (double x = 0; x <= VideoWidth; x += GridSize)
            {
                context.DrawLine(gridPen, ToScreen(x, 0), ToScreen(x, VideoHeight));
            }

            for (double y = 0; y <= VideoHeight; y += GridSize)
            {
                context.DrawLine(gridPen, ToScreen(0, y), ToScreen(VideoWidth, y));
            }
        }

        foreach (var shape in Shapes)
        {
            var geometry = MakeGeometry(shape.Path, closed: true);
            context.DrawGeometry(new SolidColorBrush(shape.Color, 0.45), new Pen(new SolidColorBrush(shape.Color), 1.5), geometry);
        }

        if (Active is { } active && active.Path.Anchors.Count > 0)
        {
            context.DrawGeometry(null, ActivePen, MakeGeometry(active.Path, closed: false));
            if (_mouse is { } m && _drag == DragMode.None)
            {
                var last = active.Path.Anchors[^1];
                var first = active.Path.Anchors[0];
                context.DrawLine(RubberBandPen, ToScreen(last.X, last.Y), m);
                if (active.Path.Anchors.Count >= 2 && !IsOverFirstPoint(m))
                {
                    context.DrawLine(ClosePreviewPen, m, ToScreen(first.X, first.Y));
                }
            }
        }

        foreach (var anchor in AnchorsWithVisibleHandles())
        {
            var a = ToScreen(anchor.X, anchor.Y);
            foreach (var h in new[] { ToScreen(anchor.InX, anchor.InY), ToScreen(anchor.OutX, anchor.OutY) })
            {
                context.DrawLine(HandlePen, a, h);
                context.DrawEllipse(Brushes.White, PointOutline, h, 3.5, 3.5);
            }
        }

        foreach (var shape in AllShapes())
        {
            foreach (var anchor in shape.Path.Anchors)
            {
                var s = ToScreen(anchor.X, anchor.Y);
                var fill = anchor == _selected ? Accent : Brushes.White;
                context.FillRectangle(fill, new Rect(s.X - 3.5, s.Y - 3.5, 7, 7));
                context.DrawRectangle(PointOutline, new Rect(s.X - 3.5, s.Y - 3.5, 7, 7));
            }
        }

        if (Active is { Path.Anchors.Count: > 0 } open)
        {
            // the first point: a ring that grows when a click would close the shape
            var first = ToScreen(open.Path.Anchors[0].X, open.Path.Anchors[0].Y);
            var closing = _mouse is { } m && IsOverFirstPoint(m);
            context.DrawEllipse(closing ? Accent : null, new Pen(Accent, 2), first, closing ? 9 : 6, closing ? 9 : 6);
        }

        DrawHint(context);
    }

    private StreamGeometry MakeGeometry(PenPath path, bool closed)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var anchors = path.Anchors;
        if (anchors.Count == 0)
        {
            return geometry;
        }

        ctx.BeginFigure(ToScreen(anchors[0].X, anchors[0].Y), closed);
        var count = closed ? anchors.Count : anchors.Count - 1;
        for (var i = 1; i <= count; i++)
        {
            var from = anchors[i - 1];
            var to = anchors[i % anchors.Count];
            if (from.OutX == from.X && from.OutY == from.Y && to.InX == to.X && to.InY == to.Y)
            {
                ctx.LineTo(ToScreen(to.X, to.Y));
            }
            else
            {
                ctx.CubicBezierTo(ToScreen(from.OutX, from.OutY), ToScreen(to.InX, to.InY), ToScreen(to.X, to.Y));
            }
        }

        ctx.EndFigure(closed);
        return geometry;
    }

    private void DrawHint(DrawingContext context)
    {
        // only when it helps - the picture should look like the video
        var l = Se.Language.Assa;
        string hint;
        if (_escArmed)
        {
            hint = l.DrawOnVideoHintEscAgain;
        }
        else if (_mouse is { } m && IsOverFirstPoint(m))
        {
            hint = l.DrawOnVideoHintClickToClose;
        }
        else if (Active == null && Shapes.Count == 0 && !IsModified)
        {
            hint = l.DrawOnVideoHintStart;
        }
        else
        {
            return;
        }

        var text = new FormattedText(hint, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);
        var box = new Rect(8, Bounds.Height - text.Height - 14, text.Width + 16, text.Height + 8);
        context.DrawRectangle(HintBackground, null, box, 4, 4);
        context.DrawText(text, new Point(box.X + 8, box.Y + 4));
    }
}
