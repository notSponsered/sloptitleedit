using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Assa.AssaMotionShapes;

/// <summary>Frame strip: keyframe diamonds, the current frame, and orange ticks where the shape is only guessed. Click/drag seeks.</summary>
public class ShapeTimeline : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
    private static readonly IBrush Range = new SolidColorBrush(Color.FromArgb(70, 79, 195, 247));
    private static readonly IBrush Key = new SolidColorBrush(Color.FromRgb(79, 195, 247));
    private static readonly IBrush Guessed = new SolidColorBrush(Color.FromRgb(255, 152, 0));
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 83, 80)), 2); // playhead red: visible on light and dark
    private static readonly IPen KeyOutline = new Pen(Brushes.Black, 1);

    public int FrameCount { get; set; }
    public int FrameIndex { get; set; }
    public IReadOnlyCollection<int> Keys { get; set; } = [];
    public IReadOnlyList<bool>? Interpolated { get; set; }

    public event Action<int>? Seek;

    public ShapeTimeline()
    {
        Height = 26;
        ClipToBounds = true;
    }

    private double X(int frame) => FrameCount <= 1 ? 8 : 8 + (Bounds.Width - 16) * frame / (FrameCount - 1);

    private int FrameAt(double x) => FrameCount <= 1 ? 0 : Math.Clamp((int)Math.Round((x - 8) / (Bounds.Width - 16) * (FrameCount - 1)), 0, FrameCount - 1);

    public override void Render(DrawingContext context)
    {
        var mid = Bounds.Height / 2;
        context.FillRectangle(Track, new Rect(8, mid - 3, Math.Max(0, Bounds.Width - 16), 6));
        if (FrameCount == 0)
        {
            return;
        }

        if (Keys.Count > 1)
        {
            context.FillRectangle(Range, new Rect(X(Keys.Min()), mid - 3, X(Keys.Max()) - X(Keys.Min()), 6));
        }

        if (Interpolated != null)
        {
            for (var i = 0; i < Interpolated.Count && i < FrameCount; i++)
            {
                if (Interpolated[i])
                {
                    context.FillRectangle(Guessed, new Rect(X(i) - 1, mid - 7, 2, 14));
                }
            }
        }

        var x = X(FrameIndex);
        context.DrawLine(CursorPen, new Point(x, 1), new Point(x, Bounds.Height - 1));

        foreach (var key in Keys)
        {
            var k = X(key);
            var diamond = new StreamGeometry();
            using (var g = diamond.Open())
            {
                g.BeginFigure(new Point(k, mid - 6), true);
                g.LineTo(new Point(k + 6, mid));
                g.LineTo(new Point(k, mid + 6));
                g.LineTo(new Point(k - 6, mid));
                g.EndFigure(true);
            }

            context.DrawGeometry(Key, KeyOutline, diamond);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Seek?.Invoke(FrameAt(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
        {
            Seek?.Invoke(FrameAt(e.GetPosition(this).X));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }
}
