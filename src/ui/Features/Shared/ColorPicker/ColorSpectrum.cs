using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace Nikse.SubtitleEdit.Features.Shared.ColorPicker;

/// <summary>
/// Saturation/brightness square plus a hue strip (the usual editor picker). Keeps its own hue so dragging
/// to grey or black doesn't lose it. <see cref="SetColor"/> to update from outside; <see cref="ColorChanged"/>
/// fires for user drags only.
/// </summary>
public sealed class ColorSpectrum : Control
{
    private const double StripWidth = 18;
    private const double Gap = 10;
    private const double Radius = 4;

    private static readonly Color[] HueStops =
        [Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red];

    private double _hue;        // 0..360
    private double _saturation; // 0..1
    private double _value = 1;  // 0..1
    private bool _draggingStrip;

    public event EventHandler<Color>? ColorChanged;

    public ColorSpectrum()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        ClipToBounds = false;
    }

    public void SetColor(Color color)
    {
        if (Current().R == color.R && Current().G == color.G && Current().B == color.B)
        {
            return; // same colour: keep the hue/saturation the user is working with
        }

        var hsv = color.ToHsv();
        if (hsv.V > 0)
        {
            if (hsv.S > 0)
            {
                _hue = hsv.H;
            }

            _saturation = hsv.S;
        }

        _value = hsv.V;
        InvalidateVisual();
    }

    private Color Current() => HsvColor.ToRgb(_hue, _saturation, _value, 1);

    private Rect Square => new(0, 0, Math.Max(1, Bounds.Width - StripWidth - Gap), Bounds.Height);

    private Rect Strip => new(Bounds.Width - StripWidth, 0, StripWidth, Bounds.Height);

    public override void Render(DrawingContext context)
    {
        var square = Square;
        var strip = Strip;

        // Square: pure hue, whitened to the left, darkened to the bottom
        context.DrawRectangle(new SolidColorBrush(HsvColor.ToRgb(_hue, 1, 1, 1)), null, square, Radius, Radius);
        context.DrawRectangle(Gradient(Colors.White, Color.FromArgb(0, 255, 255, 255), horizontal: true), null, square, Radius, Radius);
        context.DrawRectangle(Gradient(Color.FromArgb(0, 0, 0, 0), Colors.Black, horizontal: false), null, square, Radius, Radius);

        var stops = new GradientStops();
        for (var i = 0; i < HueStops.Length; i++)
        {
            stops.Add(new GradientStop(HueStops[i], i / (double)(HueStops.Length - 1)));
        }

        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = stops,
        }, null, strip, Radius, Radius);

        // Markers: a ring on the square, a bar on the strip (white with a dark edge: visible on any colour)
        var dark = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1);
        var white = new Pen(Brushes.White, 2);
        var marker = new Point(square.X + _saturation * square.Width, square.Y + (1 - _value) * square.Height);
        context.DrawEllipse(null, dark, marker, 7.5, 7.5);
        context.DrawEllipse(null, white, marker, 6, 6);
        context.DrawEllipse(null, dark, marker, 4.5, 4.5);

        var y = strip.Y + _hue / 360 * strip.Height;
        var bar = new Rect(strip.X - 2, y - 3, strip.Width + 4, 6);
        context.DrawRectangle(null, dark, bar.Inflate(1), 3, 3);
        context.DrawRectangle(null, white, bar, 3, 3);
    }

    private static LinearGradientBrush Gradient(Color from, Color to, bool horizontal) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = horizontal ? new RelativePoint(1, 0, RelativeUnit.Relative) : new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var p = e.GetPosition(this);
        _draggingStrip = p.X >= Strip.X - Gap / 2;
        e.Pointer.Capture(this);
        e.Handled = true;
        Update(p);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this)
        {
            Update(e.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Update(Point p)
    {
        if (_draggingStrip)
        {
            _hue = Math.Clamp(p.Y / Strip.Height, 0, 1) * 360; // 360 = red again, marker stays at the bottom
        }
        else
        {
            _saturation = Math.Clamp(p.X / Square.Width, 0, 1);
            _value = 1 - Math.Clamp(p.Y / Square.Height, 0, 1);
        }

        InvalidateVisual();
        ColorChanged?.Invoke(this, Current());
    }
}
