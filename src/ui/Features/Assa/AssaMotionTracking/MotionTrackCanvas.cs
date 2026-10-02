using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nikse.SubtitleEdit.Features.Assa.AssaMotionTracking;

/// <summary>
/// Shows a video frame (uniform fit), the tracking points (boxes), the tracked path and a preview of the subtitle.
/// Points are added from the points list; here they are selected, moved (drag inside) and resized (drag an edge/corner).
/// Coordinates are frame-image pixels.
/// </summary>
public class MotionTrackCanvas : Control
{
    private const double HandleSize = 7; // screen pixels around an edge that grab it for resizing

    private static readonly IPen BoxPen = new Pen(Brushes.LimeGreen, 1.5);
    private static readonly IPen SelectedPen = new Pen(Brushes.LimeGreen, 3);
    private static readonly IPen LostPen = new Pen(Brushes.Red, 2);
    private static readonly IPen PathPen = new Pen(Brushes.Yellow, 1.5);
    private static readonly IPen ReferencePen = new Pen(Brushes.DeepSkyBlue, 2);
    private static readonly Typeface LabelFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    [Flags]
    private enum Edge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        Move = 16,
    }

    private Point? _dragStart;
    private Edge _dragEdge;
    private TrackBox _dragStartBox;

    public Bitmap? Frame { get; set; }
    public List<TrackBox> Boxes { get; set; } = [];
    public int SelectedIndex { get; set; } = -1;
    public bool IsLost { get; set; }
    public IReadOnlyList<Point> TrackPath { get; set; } = [];
    public Point? ReferencePoint { get; set; }

    /// <summary>Rendered subtitle(s) at video resolution, and where it goes: preview pixels -> frame-image pixels.</summary>
    public Bitmap? SubtitlePreview { get; set; }
    public Matrix SubtitlePreviewTransform { get; set; } = Matrix.Identity;
    public bool ShowSubtitlePreview { get; set; } = true;

    /// <summary>A point was moved or resized.</summary>
    public event EventHandler? BoxesChanged;

    /// <summary>A point was clicked (SelectedIndex changed).</summary>
    public event EventHandler? SelectionChanged;

    public MotionTrackCanvas()
    {
        ClipToBounds = true;
    }

    private Rect ImageRect
    {
        get
        {
            if (Frame == null || Frame.PixelSize.Width == 0 || Frame.PixelSize.Height == 0)
            {
                return default;
            }

            var scale = Math.Min(Bounds.Width / Frame.PixelSize.Width, Bounds.Height / Frame.PixelSize.Height);
            var w = Frame.PixelSize.Width * scale;
            var h = Frame.PixelSize.Height * scale;
            return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        }
    }

    private double Scale => Frame == null || Frame.PixelSize.Width == 0 ? 1 : ImageRect.Width / Frame.PixelSize.Width;

    private Point ToScreen(double x, double y) => new(ImageRect.X + x * Scale, ImageRect.Y + y * Scale);

    private Point ToImage(Point p) => new((p.X - ImageRect.X) / Scale, (p.Y - ImageRect.Y) / Scale);

    private Rect ScreenRect(TrackBox b) => new(ToScreen(b.Left, b.Top), new Size(b.Width * Scale, b.Height * Scale));

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (Frame == null)
        {
            return;
        }

        context.DrawImage(Frame, new Rect(Frame.Size), ImageRect);

        if (ShowSubtitlePreview && SubtitlePreview != null)
        {
            var toScreen = Matrix.CreateScale(Scale, Scale) * Matrix.CreateTranslation(ImageRect.X, ImageRect.Y);
            using (context.PushTransform(SubtitlePreviewTransform * toScreen))
            {
                context.DrawImage(SubtitlePreview, new Rect(SubtitlePreview.Size));
            }
        }

        for (var i = 1; i < TrackPath.Count; i++)
        {
            context.DrawLine(PathPen, ToScreen(TrackPath[i - 1].X, TrackPath[i - 1].Y), ToScreen(TrackPath[i].X, TrackPath[i].Y));
        }

        if (ReferencePoint is { } r)
        {
            context.DrawEllipse(null, ReferencePen, ToScreen(r.X, r.Y), 6, 6);
        }

        for (var i = 0; i < Boxes.Count; i++)
        {
            var rect = ScreenRect(Boxes[i]);
            var selected = i == SelectedIndex;
            var pen = IsLost ? LostPen : selected ? SelectedPen : BoxPen;
            context.DrawRectangle(null, pen, rect);
            var center = rect.Center;
            context.DrawLine(pen, center + new Point(-6, 0), center + new Point(6, 0));
            context.DrawLine(pen, center + new Point(0, -6), center + new Point(0, 6));

            var label = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelFont, 13, pen.Brush);
            context.FillRectangle(new SolidColorBrush(Colors.Black, 0.6), new Rect(rect.X, rect.Y - label.Height - 2, label.Width + 6, label.Height + 2));
            context.DrawText(label, new Point(rect.X + 3, rect.Y - label.Height - 1));

            if (selected)
            {
                foreach (var corner in new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight })
                {
                    context.FillRectangle(Brushes.LimeGreen, new Rect(corner.X - 4, corner.Y - 4, 8, 8));
                }
            }
        }
    }

    /// <summary>Which part of which box is under the mouse; the selected box wins.</summary>
    private (int Index, Edge Edge) HitTest(Point screen)
    {
        var order = new List<int>();
        if (SelectedIndex >= 0 && SelectedIndex < Boxes.Count)
        {
            order.Add(SelectedIndex);
        }

        for (var i = Boxes.Count - 1; i >= 0; i--)
        {
            if (i != SelectedIndex)
            {
                order.Add(i);
            }
        }

        foreach (var i in order)
        {
            var rect = ScreenRect(Boxes[i]);
            if (!rect.Inflate(HandleSize).Contains(screen))
            {
                continue;
            }

            var edge = Edge.None;
            if (Math.Abs(screen.X - rect.Left) <= HandleSize) edge |= Edge.Left;
            else if (Math.Abs(screen.X - rect.Right) <= HandleSize) edge |= Edge.Right;
            if (Math.Abs(screen.Y - rect.Top) <= HandleSize) edge |= Edge.Top;
            else if (Math.Abs(screen.Y - rect.Bottom) <= HandleSize) edge |= Edge.Bottom;

            if (edge != Edge.None)
            {
                return (i, edge);
            }

            if (rect.Contains(screen))
            {
                return (i, Edge.Move);
            }
        }

        return (-1, Edge.None);
    }

    private static StandardCursorType CursorFor(Edge edge) => edge switch
    {
        Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => StandardCursorType.TopLeftCorner,
        Edge.Right | Edge.Top or Edge.Left | Edge.Bottom => StandardCursorType.TopRightCorner,
        Edge.Left or Edge.Right => StandardCursorType.SizeWestEast,
        Edge.Top or Edge.Bottom => StandardCursorType.SizeNorthSouth,
        Edge.Move => StandardCursorType.SizeAll,
        _ => StandardCursorType.Arrow,
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Frame == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var (index, edge) = HitTest(e.GetPosition(this));
        if (index < 0)
        {
            return;
        }

        if (index != SelectedIndex)
        {
            SelectedIndex = index;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        _dragStart = ToImage(e.GetPosition(this));
        _dragEdge = edge;
        _dragStartBox = Boxes[index];
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Frame == null)
        {
            return;
        }

        if (_dragStart is not { } start)
        {
            Cursor = new Cursor(CursorFor(HitTest(e.GetPosition(this)).Edge));
            return;
        }

        var p = ToImage(e.GetPosition(this));
        var dx = p.X - start.X;
        var dy = p.Y - start.Y;
        var maxX = (double)Frame.PixelSize.Width;
        var maxY = (double)Frame.PixelSize.Height;
        var b = _dragStartBox;
        double left = b.Left, top = b.Top, right = b.Left + b.Width, bottom = b.Top + b.Height;
        const double minSize = 8;

        if (_dragEdge == Edge.Move)
        {
            left = Math.Clamp(b.Left + dx, 0, maxX - b.Width);
            top = Math.Clamp(b.Top + dy, 0, maxY - b.Height);
            right = left + b.Width;
            bottom = top + b.Height;
        }
        else
        {
            if (_dragEdge.HasFlag(Edge.Left)) left = Math.Clamp(b.Left + dx, 0, right - minSize);
            if (_dragEdge.HasFlag(Edge.Right)) right = Math.Clamp(right + dx, left + minSize, maxX);
            if (_dragEdge.HasFlag(Edge.Top)) top = Math.Clamp(b.Top + dy, 0, bottom - minSize);
            if (_dragEdge.HasFlag(Edge.Bottom)) bottom = Math.Clamp(bottom + dy, top + minSize, maxY);
        }

        Boxes[SelectedIndex] = new TrackBox(left, top, right - left, bottom - top);
        IsLost = false;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart == null)
        {
            return;
        }

        _dragStart = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
        BoxesChanged?.Invoke(this, EventArgs.Empty);
    }
}
