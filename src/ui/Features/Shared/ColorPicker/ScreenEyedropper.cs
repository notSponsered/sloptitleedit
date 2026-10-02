using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Features.Main.Panels;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Shared.ColorPicker;

/// <summary>
/// Pick a color from anywhere on screen (the playing video included). A see-through overlay on every screen
/// turns the pointer into an eyedropper; hold the left button to see a loupe of the live pixels, release to take
/// the one under the pointer. Right click or Esc cancels. The overlays and the loupe are excluded from screen
/// capture, so the pixels read are exactly what's underneath. Windows only (GDI + capture exclusion).
/// </summary>
internal static class ScreenEyedropper
{
    private const int Radius = 7;              // loupe shows (2 * radius + 1)² screen pixels
    private const int Size = Radius * 2 + 1;
    private const double LoupeZoom = 8;

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static Task<Color?> PickAsync(TopLevel owner)
    {
        var result = new TaskCompletionSource<Color?>();
        var overlays = new List<Window>();
        var loupe = new LoupeWindow();
        var holding = false;
        DispatcherTimer? timer = null;

        void Finish(Color? color)
        {
            if (result.TrySetResult(color))
            {
                timer?.Stop();
                loupe.Close();
                foreach (var overlay in overlays)
                {
                    overlay.Close();
                }
            }
        }

        var cursor = MakeEyedropperCursor();
        var screens = owner.Screens?.All ?? [];
        foreach (var screen in screens)
        {
            var overlay = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = screen.Bounds.Position,
                Width = screen.Bounds.Width / screen.Scaling,
                Height = screen.Bounds.Height / screen.Scaling,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // catches the mouse, invisible
                Cursor = cursor,
            };
            overlay.Opened += (_, _) => ExcludeFromCapture(overlay);
            overlay.PointerPressed += (_, e) =>
            {
                var props = e.GetCurrentPoint(overlay).Properties;
                if (props.IsRightButtonPressed)
                {
                    Finish(null);
                }
                else if (props.IsLeftButtonPressed)
                {
                    holding = true;
                    e.Pointer.Capture(overlay);
                    UpdateLoupe();
                }
            };
            overlay.PointerReleased += (_, e) =>
            {
                if (holding && e.InitialPressMouseButton == MouseButton.Left)
                {
                    GetCursorPos(out var p);
                    var pixel = Capture(p.X, p.Y, 1, 1);
                    Finish(Color.FromRgb(pixel[2], pixel[1], pixel[0]));
                }
            };
            overlay.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Finish(null);
                }
            };
            overlays.Add(overlay);
            overlay.Show();
        }

        if (overlays.Count == 0)
        {
            return Task.FromResult<Color?>(null);
        }

        loupe.Opened += (_, _) => ExcludeFromCapture(loupe);

        void UpdateLoupe()
        {
            GetCursorPos(out var p);
            loupe.Show(p.X, p.Y, Capture(p.X - Radius, p.Y - Radius, Size, Size));
        }

        // Follow the pointer while held (also between pointer events), and Esc even if an overlay lost focus
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) =>
        {
            if ((GetAsyncKeyState(VkEscape) & 0x8000) != 0)
            {
                Finish(null);
            }
            else if (holding)
            {
                UpdateLoupe();
            }
        });
        timer.Start();
        overlays[0].Activate();
        return result.Task;
    }

    /// <summary>The toolbar's eyedropper glyph, white with a dark edge, hot spot on its tip.</summary>
    private static Cursor MakeEyedropperCursor()
    {
        const int size = 32;
        var scale = size / 24.0;
        var geometry = Geometry.Parse(AssaToolbar.Glyphs.ColorPicker);
        Path Stroke(IBrush brush, double thickness) => new()
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            RenderTransform = new ScaleTransform(scale, scale),
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
        };

        var canvas = new Canvas { Width = size, Height = size, Children = { Stroke(Brushes.Black, 3.2), Stroke(Brushes.White, 1.6) } };
        canvas.Measure(new Size(size, size));
        canvas.Arrange(new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(canvas);
        return new Cursor(bitmap, new PixelPoint((int)(5.5 * scale), (int)(18.5 * scale))); // the tip
    }

    private static void ExcludeFromCapture(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is { } handle)
        {
            SetWindowDisplayAffinity(handle, WdaExcludeFromCapture); // Windows 10 2004+; older: harmless no-op
        }
    }

    /// <summary>BGRA pixels of a screen rectangle (physical pixels, top-down rows).</summary>
    private static byte[] Capture(int x, int y, int width, int height)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        BitBlt(memory, 0, 0, width, height, screen, x, y, SrcCopy | CaptureBlt);
        SelectObject(memory, old);

        var info = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0);

        DeleteObject(bitmap);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
        return pixels;
    }

    /// <summary>Topmost, non-activating window next to the pointer: zoomed pixels, center marked, hex below.</summary>
    private sealed class LoupeWindow : Window
    {
        private readonly Image _image = new() { Width = Size * LoupeZoom, Height = Size * LoupeZoom, Stretch = Stretch.Fill };
        private readonly TextBlock _hex = new() { FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Border _frame;
        private readonly WriteableBitmap _bitmap = new(new PixelSize(Size, Size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        public LoupeWindow()
        {
            WindowDecorations = WindowDecorations.None;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            CanResize = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
            _image.Source = _bitmap;

            _frame = new Border
            {
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = new Grid
                {
                    Children =
                    {
                        _image,
                        new Border // the pixel that releasing takes
                        {
                            Width = LoupeZoom + 2,
                            Height = LoupeZoom + 2,
                            BorderThickness = new Thickness(1),
                            BorderBrush = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
            };

            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _frame,
                    new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
                        Padding = new Thickness(6, 2),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = _hex,
                    },
                },
            };
        }

        public void Show(int cursorX, int cursorY, byte[] pixels)
        {
            using (var buffer = _bitmap.Lock())
            {
                for (var row = 0; row < Size; row++)
                {
                    Marshal.Copy(pixels, row * Size * 4, buffer.Address + row * buffer.RowBytes, Size * 4);
                }
            }

            var center = (Radius * Size + Radius) * 4;
            var color = Color.FromRgb(pixels[center + 2], pixels[center + 1], pixels[center]);
            _frame.BorderBrush = new SolidColorBrush(color);
            _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            _image.InvalidateVisual();

            // Below-right of the pointer; flipped near the screen's right/bottom edges
            var scale = RenderScaling;
            var width = (int)(Bounds.Width * scale);
            var height = (int)(Bounds.Height * scale);
            var offset = (int)(24 * scale);
            var screen = Screens.ScreenFromPoint(new PixelPoint(cursorX, cursorY))?.Bounds ?? new PixelRect(0, 0, int.MaxValue, int.MaxValue);
            var x = cursorX + offset + width > screen.Right ? cursorX - offset - width : cursorX + offset;
            var y = cursorY + offset + height > screen.Bottom ? cursorY - offset - height : cursorY + offset;
            Position = new PixelPoint(x, y);
            if (!IsVisible)
            {
                base.Show();
            }
        }
    }

    // ---- Win32 ----
    private const int VkEscape = 0x1B;
    private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;
    private const uint WdaExcludeFromCapture = 0x11;

    [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height; public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point32 point);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
