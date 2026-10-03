using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Nikse.SubtitleEdit.Logic;

/// <summary>
/// The app's icons: line drawings on a 24×24 grid (stroke 1.6, round caps and joins, live area ~3.5-20.5),
/// so every icon has the same weight. The stroke follows the surrounding text color (inherited Foreground):
/// buttons, menus and styles color an icon by setting Foreground, never the stroke.
/// </summary>
internal static class Glyphs
{
    /// <summary>An icon at <paramref name="size"/> px (20 in toolbars and buttons, 16 in menus, tabs and inline).
    /// <paramref name="plain"/> = a standalone icon in the muted text color (class "se-glyph"), e.g. beside a slider.
    /// <paramref name="color"/> = a fixed data color instead.</summary>
    public static Viewbox Make(string data, double size = 20, bool plain = false, IBrush? color = null)
    {
        var canvas = new Canvas { Width = 24, Height = 24, Background = Brushes.Transparent }; // the whole square takes clicks and tooltips
        if (!string.IsNullOrEmpty(data))
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = 1.6,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
            };
            path.Bind(Shape.StrokeProperty, path.GetObservable(TextElement.ForegroundProperty));
            canvas.Children.Add(path);
        }

        var glyph = new Viewbox { Width = size, Height = size, Child = canvas };
        if (plain)
        {
            glyph.Classes.Add("se-glyph");
        }

        if (color != null)
        {
            TextElement.SetForeground(glyph, color); // a data color, e.g. the bookmark color
        }

        return glyph;
    }

    /// <summary>Swaps the icon of a control whose content was made with <see cref="Make"/> (e.g. play ↔ pause, copy ↔ done).</summary>
    public static void Set(ContentControl control, string data) => control.Content = Make(data, (control.Content as Viewbox)?.Width ?? 20);

    /// <summary>Fills an icon's shape with a data color (e.g. the colour swatch).</summary>
    public static void SetFill(ContentControl control, Color color)
    {
        if (control.Content is Viewbox { Child: Canvas canvas } && canvas.Children.Count > 0 && canvas.Children[0] is Path path)
        {
            path.Fill = new SolidColorBrush(color);
        }
    }

    // ---- context controls (drawing on the video) ----

    // Same rules as the tools below: live area ~3.5-20.5, rounded corners r2, a frame + 2-3 strokes at most.

    // An arrow hooking back: undo the last point (tip on the same 4.5 edge as the grid frame).
    public const string Undo = "M9 4.5 L4.5 9 L9 13.5 M4.5 9 H14.5 A5 5 0 0 1 14.5 19 H10";

    // A 2x2 grid in a rounded square (frame + 2 lines, the density of Script info / Background).
    public const string Grid = "M6.5 4.5 H17.5 A2 2 0 0 1 19.5 6.5 V17.5 A2 2 0 0 1 17.5 19.5 H6.5 A2 2 0 0 1 4.5 17.5 V6.5 A2 2 0 0 1 6.5 4.5 Z M12 4.5 V19.5 M4.5 12 H19.5";

    // Corner brackets (arms like Motion's reticle) around a frame (corners like Resolution): fit the frame in view.
    public const string Fit = "M3.5 7.5 V5 H6 M18 5 H20.5 V7.5 M20.5 16.5 V19 H18 M6 19 H3.5 V16.5 M8.5 8.5 H15.5 A1.5 1.5 0 0 1 17 10 V14 A1.5 1.5 0 0 1 15.5 15.5 H8.5 A1.5 1.5 0 0 1 7 14 V10 A1.5 1.5 0 0 1 8.5 8.5 Z";

    // A rounded colour chip the drawing colour fills (Glyphs.SetFill); smaller than the outlines so it weighs the same.
    public const string Swatch = "M8.5 6.5 H15.5 A2 2 0 0 1 17.5 8.5 V15.5 A2 2 0 0 1 15.5 17.5 H8.5 A2 2 0 0 1 6.5 15.5 V8.5 A2 2 0 0 1 8.5 6.5 Z";

    // Check / cross in a ring (like Position's): the same weight as the enclosed tool glyphs, and as each other.
    public const string Done = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M8.5 12 L11 14.5 L15.5 10";

    public const string Cancel = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M8.5 8.5 L15.5 15.5 M15.5 8.5 L8.5 15.5";

    // ---- video player and waveform toolbar (same grid, weight and corner radius as the tools below) ----

    public const string Play = "M8 5.5 V18.5 L18.5 12 Z";
    public const string Pause = "M8.5 5.5 V18.5 M15.5 5.5 V18.5";
    public const string Stop = "M7.5 6 H16.5 A1.5 1.5 0 0 1 18 7.5 V16.5 A1.5 1.5 0 0 1 16.5 18 H7.5 A1.5 1.5 0 0 1 6 16.5 V7.5 A1.5 1.5 0 0 1 7.5 6 Z";

    // Corner brackets pointing out / in.
    public const string Fullscreen = "M4.5 9 V4.5 H9 M15 4.5 H19.5 V9 M19.5 15 V19.5 H15 M9 19.5 H4.5 V15";
    public const string FullscreenExit = "M9 4.5 V9 H4.5 M19.5 9 H15 V4.5 M15 19.5 V15 H19.5 M4.5 15 H9 V19.5";

    // Play between brackets: only the selected lines.
    public const string PlaySelection = "M6.5 5 H4.5 V19 H6.5 M17.5 5 H19.5 V19 H17.5 M9.5 8.5 V15.5 L15 12 Z";

    // Two arrows chasing each other: play the selection in a loop.
    public const string Repeat = "M4.5 12 A6 6 0 0 1 10.5 6 H17.5 M15 3.5 L17.5 6 L15 8.5 M19.5 12 A6 6 0 0 1 13.5 18 H6.5 M9 15.5 L6.5 18 L9 20.5";

    // Play up to a bar: play the next line.
    public const string PlayNext = "M6 5.5 V18.5 L15.5 12 Z M18.5 5.5 V18.5";

    public const string Add = "M12 5 V19 M5 12 H19";

    // An arrow landing on the line's opening / closing bracket: set start / end at the playhead.
    public const string SetStart = "M10 5 H7 V19 H10 M19.5 12 H12.5 M15.5 9 L12.5 12 L15.5 15";
    public const string SetEnd = "M14 5 H17 V19 H14 M4.5 12 H11.5 M8.5 9 L11.5 12 L8.5 15";

    // A start mark with an arrow pushing on: set start and move the following lines with it.
    public const string SetStartAndOffset = "M5.5 5 V19 M9 12 H19.5 M16 8.5 L19.5 12 L16 15.5";

    // A cross in the gap between two lines.
    public const string RemoveBlankLines = "M4.5 6 H19.5 M4.5 18 H19.5 M9.5 9.5 L14.5 14.5 M14.5 9.5 L9.5 14.5";

    // List rows beside play: select the line under the playhead while playing.
    public const string FollowPlayback = "M4.5 7 H11 M4.5 12 H11 M4.5 17 H11 M14.5 7.5 V16.5 L20.5 12 Z";

    // Arrows meeting at the playhead: keep it centered.
    public const string Center = "M12 4 V20 M3.5 12 H8.5 M6 9.5 L8.5 12 L6 14.5 M20.5 12 H15.5 M18 9.5 L15.5 12 L18 14.5";

    // Horseshoe magnet.
    public const string Snap = "M6 4.5 V12 A6 6 0 0 0 18 12 V4.5 H14.5 V12 A2.5 2.5 0 0 1 9.5 12 V4.5 Z M6 8.5 H9.5 M14.5 8.5 H18";

    // A clip cut in two.
    public const string Razor = "M10 7.5 H5.5 A2 2 0 0 0 3.5 9.5 V14.5 A2 2 0 0 0 5.5 16.5 H10 M14 7.5 H18.5 A2 2 0 0 1 20.5 9.5 V14.5 A2 2 0 0 1 18.5 16.5 H14 M12 3.5 V20.5";

    public const string More = "M12 5.5 A1 1 0 1 0 12 7.5 A1 1 0 1 0 12 5.5 M12 11 A1 1 0 1 0 12 13 A1 1 0 1 0 12 11 M12 16.5 A1 1 0 1 0 12 18.5 A1 1 0 1 0 12 16.5";

    // Three sliders: settings.
    public const string Settings = "M4.5 7 H12.5 M16.5 7 H19.5 M14.5 5 V9 M4.5 12 H6.5 M10.5 12 H19.5 M8.5 10 V14 M4.5 17 H14.5 M18.5 17 H19.5 M16.5 15 V19";

    // Beside the zoom sliders and the volume slider (plain glyphs, not buttons).
    public const string ZoomHorizontal = "M4 12 H20 M7 9 L4 12 L7 15 M17 9 L20 12 L17 15";
    public const string ZoomVertical = "M12 4 V20 M9 7 L12 4 L15 7 M9 17 L12 20 L15 17";
    public const string Volume = "M4.5 9.5 H7.5 L12 5.5 V18.5 L7.5 14.5 H4.5 Z M15.5 9 A4 4 0 0 1 15.5 15 M18 6.5 A7.5 7.5 0 0 1 18 17.5";
    public const string Muted = "M4.5 9.5 H7.5 L12 5.5 V18.5 L7.5 14.5 H4.5 Z M15.5 9.5 L20 14 M20 9.5 L15.5 14";

    // ---- tools ----

    // "Aa": a style is a named look for text.
    public const string Styles = "M3.5 18 L8 6 L12.5 18 M5.2 14 H10.8 M20 12.5 V18 M17.2 12.4 A2.8 2.8 0 1 0 17.2 18 A2.8 2.8 0 1 0 17.2 12.4";

    // A sheet of settings: [Script Info].
    public const string ScriptInfo = "M6.5 3.5 H17.5 A2 2 0 0 1 19.5 5.5 V18.5 A2 2 0 0 1 17.5 20.5 H6.5 A2 2 0 0 1 4.5 18.5 V5.5 A2 2 0 0 1 6.5 3.5 Z M8.5 8.5 H15.5 M8.5 12 H15.5 M8.5 15.5 H12.5";

    // Paperclip: embedded fonts and graphics.
    public const string Attachments = "M15 7 L8.6 13.4 A2 2 0 0 0 11.4 16.2 L18.3 9.3 A4 4 0 0 0 12.6 3.6 L5.7 10.5 A6 6 0 0 0 14.2 19 L19.5 13.7";

    // A 16:9 frame with a scale arrow: PlayRes / resampling.
    public const string Resolution = "M4 6.5 H20 A1.5 1.5 0 0 1 21.5 8 V16 A1.5 1.5 0 0 1 20 17.5 H4 A1.5 1.5 0 0 1 2.5 16 V8 A1.5 1.5 0 0 1 4 6.5 Z M8.5 14.5 L15.5 9.5 M12 9.5 H15.5 V13";

    // Crosshair: \pos.
    public const string Position = "M12 6 A6 6 0 1 0 12 18 A6 6 0 1 0 12 6 Z M12 2.5 V6 M12 18 V21.5 M2.5 12 H6 M18 12 H21.5 M12 11 A1 1 0 1 0 12 13 A1 1 0 1 0 12 11";

    // A bezier with anchors and handles: \p drawings.
    public const string Draw = "M5 18.5 C7 11 13 8 19 5.5 M5 18.5 L7 11 M19 5.5 L13 8 M3.5 17 H6.5 V20 H3.5 Z M17.5 4 H20.5 V7 H17.5 Z M7 9.9 A1.1 1.1 0 1 0 7 12.1 A1.1 1.1 0 1 0 7 9.9 M13 6.9 A1.1 1.1 0 1 0 13 9.1 A1.1 1.1 0 1 0 13 6.9";

    // Text lines on a plate: background box behind a line.
    public const string Background = "M5 6 H19 A2 2 0 0 1 21 8 V16 A2 2 0 0 1 19 18 H5 A2 2 0 0 1 3 16 V8 A2 2 0 0 1 5 6 Z M7 10.5 H17 M7 13.5 H13.5";

    // A tracking reticle (corner brackets + centre dot) at the end of a dashed motion path.
    public const string Motion = "M13.5 6 V3.5 H16 M18 3.5 H20.5 V6 M20.5 8 V10.5 H18 M16 10.5 H13.5 V8 M17 6.2 A0.8 0.8 0 1 0 17 7.8 A0.8 0.8 0 1 0 17 6.2 M3.8 20 L5.3 18.7 M7.2 17.2 L8.7 15.9 M10.6 14.4 L12.1 13.1";

    // a circle moving to and turning into a rounded square
    public const string MotionShapes = "M6.5 12 A3.5 3.5 0 1 0 6.5 19 A3.5 3.5 0 1 0 6.5 12 M15 3.5 H19 A1.5 1.5 0 0 1 20.5 5 V9 A1.5 1.5 0 0 1 19 10.5 H15 A1.5 1.5 0 0 1 13.5 9 V5 A1.5 1.5 0 0 1 15 3.5 M9.8 13.2 L11 12.2 M12.2 11.4 L12.9 10.9";

    // An opacity ramp: a wedge with hatching, rising (in) or falling (out).
    public const string FadeIn = "M3.5 19 L20.5 5 V19 Z M9 19 V14.5 M14.5 19 V9.9";
    public const string FadeOut = "M3.5 5 L20.5 19 H3.5 Z M9.5 9.9 V19 M15 14.5 V19";

    // {\}: an override tag block.
    public const string OverrideTags = "M8.5 4 C6.5 4 6.5 5.5 6.5 7.5 C6.5 9.5 5.5 11 4 12 C5.5 13 6.5 14.5 6.5 16.5 C6.5 18.5 6.5 20 8.5 20 M15.5 4 C17.5 4 17.5 5.5 17.5 7.5 C17.5 9.5 18.5 11 20 12 C18.5 13 17.5 14.5 17.5 16.5 C17.5 18.5 17.5 20 15.5 20 M10.2 8 L13.8 16";

    // Sparkles: advanced effects.
    public const string Effects = "M10 4 C10.6 8.4 11.6 9.4 16 10 C11.6 10.6 10.6 11.6 10 16 C9.4 11.6 8.4 10.6 4 10 C8.4 9.4 9.4 8.4 10 4 Z M17.5 14 C17.8 16.2 18.3 16.7 20.5 17 C18.3 17.3 17.8 17.8 17.5 20 C17.2 17.8 16.7 17.3 14.5 17 C16.7 16.7 17.2 16.2 17.5 14 Z";

    // Eyedropper: pick a color from the frame.
    public const string ColorPicker = "M13 7 L17 11 M17.8 3.9 A2.3 2.3 0 1 0 17.8 8.5 A2.3 2.3 0 1 0 17.8 3.9 M14.15 8.15 L6.65 15.65 L5.5 18.5 L8.35 17.35 L15.85 9.85";

    // A partly filled bar.
    public const string ProgressBar = "M6.5 9 H17.5 A3 3 0 0 1 17.5 15 H6.5 A3 3 0 0 1 6.5 9 Z M6.5 12 H12";

    // ---- general (named after the font icons they replaced; reuse one before drawing a new one) ----

    public const string Plus = Add;
    public const string Minus = "M5 12 H19";
    public const string Close = "M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5";
    public const string Check = "M5 12.5 L9.75 17.25 L19 8";
    public const string DotsVertical = More;
    public const string DotsHorizontal = "M6.5 11 A1 1 0 1 0 6.5 13 A1 1 0 1 0 6.5 11 M12 11 A1 1 0 1 0 12 13 A1 1 0 1 0 12 11 M17.5 11 A1 1 0 1 0 17.5 13 A1 1 0 1 0 17.5 11";
    public const string ChevronLeft = "M14.5 6 L8.5 12 L14.5 18";
    public const string ChevronRight = "M9.5 6 L15.5 12 L9.5 18";
    public const string ChevronDown = "M6 9.5 L12 15.5 L18 9.5";
    public const string ArrowLeft = "M19.5 12 H4.5 M10.5 6 L4.5 12 L10.5 18";
    public const string ArrowRight = "M4.5 12 H19.5 M13.5 6 L19.5 12 L13.5 18";
    public const string ArrowUp = "M12 19.5 V4.5 M6 10.5 L12 4.5 L18 10.5";
    public const string ArrowDown = "M12 4.5 V19.5 M6 13.5 L12 19.5 L18 13.5";
    public const string SwapVertical = "M8 19.5 V4.5 M4.5 8 L8 4.5 L11.5 8 M16 4.5 V19.5 M12.5 16 L16 19.5 L19.5 16";

    // Three quarters of a ring, turned by the "se-spin" style: busy.
    public const string Busy = "M12 4 A8 8 0 1 1 4 12";

    // A ring with "?" / "i" (rings like Done / Cancel).
    public const string Help = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M9.6 9.6 A2.4 2.4 0 1 1 13.1 11.7 C12.4 12.1 12 12.7 12 13.5 V14 M12 16.9 V17.1";
    public const string Information = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M12 11 V16.5 M12 7.6 V7.8";

    // ---- files ----

    public const string Folder = "M3.5 7 A1.5 1.5 0 0 1 5 5.5 H9.5 L11.5 7.5 H19 A1.5 1.5 0 0 1 20.5 9 V17 A1.5 1.5 0 0 1 19 18.5 H5 A1.5 1.5 0 0 1 3.5 17 Z";
    public const string FolderOpen = "M3.5 17 V7 A1.5 1.5 0 0 1 5 5.5 H9.5 L11.5 7.5 H17 A1.5 1.5 0 0 1 18.5 9 V10.5 M3.5 17 L5.9 11.5 A1.5 1.5 0 0 1 7.3 10.5 H19.9 A1 1 0 0 1 20.8 11.9 L18.7 17.4 A1.5 1.5 0 0 1 17.3 18.5 H5 A1.5 1.5 0 0 1 3.5 17 Z";
    public const string New = "M13.5 3.5 H7 A1.5 1.5 0 0 0 5.5 5 V19 A1.5 1.5 0 0 0 7 20.5 H17 A1.5 1.5 0 0 0 18.5 19 V8.5 Z M13.5 3.5 V8.5 H18.5 M12 11.5 V17.5 M9 14.5 H15";
    public const string FileMultiple = "M9 3.5 H15.5 L19.5 7.5 V16.5 A1.5 1.5 0 0 1 18 18 H9 A1.5 1.5 0 0 1 7.5 16.5 V5 A1.5 1.5 0 0 1 9 3.5 Z M15.5 3.5 V7.5 H19.5 M4.5 7.5 V19 A1.5 1.5 0 0 0 6 20.5 H15";
    public const string Save = "M6 4.5 H15 L19.5 9 V18 A1.5 1.5 0 0 1 18 19.5 H6 A1.5 1.5 0 0 1 4.5 18 V6 A1.5 1.5 0 0 1 6 4.5 Z M8.5 4.5 V8.5 H14.5 V4.5 M8 19.5 V14 H16 V19.5";
    public const string SaveAs = "M11.5 19.5 H6 A1.5 1.5 0 0 1 4.5 18 V6 A1.5 1.5 0 0 1 6 4.5 H15 L18.5 8 V11 M8.5 4.5 V8.5 H14.5 V4.5 M13.5 20.5 L14 18 L19 13 L21 15 L16 20 Z";
    public const string Download = "M12 4 V15 M7 10.5 L12 15.5 L17 10.5 M4.5 17 V18.5 A1.5 1.5 0 0 0 6 20 H18 A1.5 1.5 0 0 0 19.5 18.5 V17";
    public const string Import = "M3.5 12 H14 M10 8 L14 12 L10 16 M9 6 V5 A1.5 1.5 0 0 1 10.5 3.5 H18.5 A1.5 1.5 0 0 1 20 5 V19 A1.5 1.5 0 0 1 18.5 20.5 H10.5 A1.5 1.5 0 0 1 9 19 V18";
    public const string Export = "M10 12 H20.5 M16.5 8 L20.5 12 L16.5 16 M15 6 V5 A1.5 1.5 0 0 0 13.5 3.5 H5.5 A1.5 1.5 0 0 0 4 5 V19 A1.5 1.5 0 0 0 5.5 20.5 H13.5 A1.5 1.5 0 0 0 15 19 V18";
    public const string Image = "M5.5 5 H18.5 A1.5 1.5 0 0 1 20 6.5 V17.5 A1.5 1.5 0 0 1 18.5 19 H5.5 A1.5 1.5 0 0 1 4 17.5 V6.5 A1.5 1.5 0 0 1 5.5 5 Z M4 16 L9 11 L13.5 15.5 M12.5 14.5 L15 12 L20 17 M15.5 7.8 A1.2 1.2 0 1 0 15.5 10.2 A1.2 1.2 0 1 0 15.5 7.8";
    public const string Database = "M5 6.5 A7 2 0 1 0 19 6.5 A7 2 0 1 0 5 6.5 Z M5 6.5 V17.5 A7 2 0 0 0 11.5 19.5 M19 6.5 V11 M5 12 A7 2 0 0 0 11.5 14 M14.5 17 H21 M18 14 L21 17 L18 20";

    // ---- editing ----

    public const string Trash = "M4.5 7 H19.5 M9.5 7 V5 A1 1 0 0 1 10.5 4 H13.5 A1 1 0 0 1 14.5 5 V7 M6.5 7 L7.4 18.6 A1.5 1.5 0 0 0 8.9 20 H15.1 A1.5 1.5 0 0 0 16.6 18.6 L17.5 7 M10 11 V16 M14 11 V16";
    public const string Copy = "M9 8.5 H17.5 A1.5 1.5 0 0 1 19 10 V18.5 A1.5 1.5 0 0 1 17.5 20 H9 A1.5 1.5 0 0 1 7.5 18.5 V10 A1.5 1.5 0 0 1 9 8.5 Z M15.5 8.5 V5.5 A1.5 1.5 0 0 0 14 4 H6.5 A1.5 1.5 0 0 0 5 5.5 V13 A1.5 1.5 0 0 0 6.5 14.5 H7.5";
    public const string Pencil = "M4.5 19.5 L5.5 15.5 L15.8 5.2 A1.5 1.5 0 0 1 17.9 5.2 L18.8 6.1 A1.5 1.5 0 0 1 18.8 8.2 L8.5 18.5 Z M14 7 L17 10";
    public const string Eraser = "M8.5 19.5 L4.6 15.6 A1.5 1.5 0 0 1 4.6 13.5 L13.5 4.6 A1.5 1.5 0 0 1 15.6 4.6 L19.4 8.4 A1.5 1.5 0 0 1 19.4 10.5 L10.5 19.5 Z M8.5 19.5 H19.5 M8.6 9.5 L14.5 15.4";
    public const string Pointer = "M6.5 3.5 V17.5 L10.2 14 L12.8 20 L15.2 19 L12.6 13 H17.5 Z";
    public const string Square = "M6.5 5 H17.5 A1.5 1.5 0 0 1 19 6.5 V17.5 A1.5 1.5 0 0 1 17.5 19 H6.5 A1.5 1.5 0 0 1 5 17.5 V6.5 A1.5 1.5 0 0 1 6.5 5 Z";
    public const string Circle = "M12 4.5 A7.5 7.5 0 1 0 12 19.5 A7.5 7.5 0 1 0 12 4.5 Z";
    public const string Find = "M10.5 4 A6.5 6.5 0 1 0 10.5 17 A6.5 6.5 0 1 0 10.5 4 Z M15.2 15.2 L20 20";
    public const string ZoomIn = "M10.5 4 A6.5 6.5 0 1 0 10.5 17 A6.5 6.5 0 1 0 10.5 4 Z M15.2 15.2 L20 20 M10.5 8 V13 M8 10.5 H13";
    public const string ZoomOut = "M10.5 4 A6.5 6.5 0 1 0 10.5 17 A6.5 6.5 0 1 0 10.5 4 Z M15.2 15.2 L20 20 M8 10.5 H13";
    // A magnifier with an arrow on: find and replace.
    public const string FindReplace = "M9.5 3.5 A5.5 5.5 0 1 0 9.5 14.5 A5.5 5.5 0 1 0 9.5 3.5 Z M13.5 13 L16 15.5 M12.5 19.5 H20.5 M17.5 16.5 L20.5 19.5 L17.5 22.5";
    // ".*"
    public const string Regex = "M16.5 4 V12 M13 6 L20 10 M20 6 L13 10 M6.5 16.8 A1.2 1.2 0 1 0 6.5 19.2 A1.2 1.2 0 1 0 6.5 16.8";
    public const string Italic = "M10 4.5 H18 M6 19.5 H14 M14 4.5 L10 19.5";
    // A "T" crossed out: remove formatting.
    public const string FormatClear = "M4.5 5 H15.5 M10 5 V19 M14.5 14 L19.5 19 M19.5 14 L14.5 19";
    // Balance: break the lines evenly.
    public const string Balance = "M12 4 V20 M8 20 H16 M5 7 H19 M5 7 L2.5 13 A2.5 2.5 0 0 0 7.5 13 Z M19 7 L16.5 13 A2.5 2.5 0 0 0 21.5 13 Z";
    // Two lines joining into one: unbreak.
    public const string Merge = "M4.5 7 H9 C12.5 7 12.5 12 16 12 H19.5 M4.5 17 H9 C12.5 17 12.5 12 16 12";
    public const string Palette = "M12 3.5 A8.5 8.5 0 0 0 12 20.5 C13.4 20.5 14 19.6 13.6 18.6 C13.1 17.4 13.9 16 15.3 16 H17 A3.5 3.5 0 0 0 20.5 12.5 C20.5 7.6 16.7 3.5 12 3.5 Z M7.9 11.1 V11.3 M10.4 7.6 V7.8 M14.6 7.6 V7.8 M17 11.1 V11.3";
    public const string Bookmark = "M7 4.5 H17 A0.5 0.5 0 0 1 17.5 5 V19.5 L12 15.5 L6.5 19.5 V5 A0.5 0.5 0 0 1 7 4.5 Z";
    public const string Filter = "M4 5 H20 L14 12.5 V18.5 L10 20 V12.5 Z";
    public const string Refresh = "M19.5 12 A7.5 7.5 0 1 1 17.3 6.7 M19.5 4.5 V8.5 H15.5";
    public const string Lock = "M7.5 10.5 V8 A4.5 4.5 0 0 1 16.5 8 V10.5 M6.5 10.5 H17.5 A1.5 1.5 0 0 1 19 12 V18.5 A1.5 1.5 0 0 1 17.5 20 H6.5 A1.5 1.5 0 0 1 5 18.5 V12 A1.5 1.5 0 0 1 6.5 10.5 Z M12 14 V16.5";
    // Stopwatch with a minus (SMPTE timing) / with a hand (video offset).
    public const string TimerMinus = "M12 7 A6.5 6.5 0 1 0 12 20 A6.5 6.5 0 1 0 12 7 Z M10 3.5 H14 M12 3.5 V7 M9.5 13.5 H14.5";
    public const string Timer = "M12 7 A6.5 6.5 0 1 0 12 20 A6.5 6.5 0 1 0 12 7 Z M10 3.5 H14 M12 3.5 V7 M12 13.5 V10.5 M12 13.5 L14.2 15.2";
    public const string Eye = "M2.5 12 C5 7.5 8.5 5.5 12 5.5 C15.5 5.5 19 7.5 21.5 12 C19 16.5 15.5 18.5 12 18.5 C8.5 18.5 5 16.5 2.5 12 Z M12 9 A3 3 0 1 0 12 15 A3 3 0 1 0 12 9 Z";

    // ---- video, subtitles, tools ----

    public const string Video = "M5.5 5 H18.5 A1.5 1.5 0 0 1 20 6.5 V17.5 A1.5 1.5 0 0 1 18.5 19 H5.5 A1.5 1.5 0 0 1 4 17.5 V6.5 A1.5 1.5 0 0 1 5.5 5 Z M10 9 V15 L15 12 Z";
    public const string SkipPrevious = "M18 5.5 V18.5 L8.5 12 Z M5.5 5.5 V18.5";
    public const string Waveform = "M3.5 11 V13 M6.5 8.5 V15.5 M9.5 5.5 V18.5 M12.5 9 V15 M15.5 7 V17 M18.5 10 V14 M21 11.5 V12.5";
    // "CC" in a frame: captions, hearing-impaired text.
    public const string ClosedCaption = "M5.5 5.5 H18.5 A2 2 0 0 1 20.5 7.5 V16.5 A2 2 0 0 1 18.5 18.5 H5.5 A2 2 0 0 1 3.5 16.5 V7.5 A2 2 0 0 1 5.5 5.5 Z M10.5 10 A2.2 2 0 1 0 10.5 14 M16.5 10 A2.2 2 0 1 0 16.5 14";
    // A frame with a caption line under the picture: burn subtitles in.
    public const string BurnIn = "M5.5 5 H18.5 A1.5 1.5 0 0 1 20 6.5 V17.5 A1.5 1.5 0 0 1 18.5 19 H5.5 A1.5 1.5 0 0 1 4 17.5 V6.5 A1.5 1.5 0 0 1 5.5 5 Z M10.5 8 V12.5 L14 10.25 Z M8 15.5 H16";
    // Two arrows passing each other: sync.
    public const string Sync = "M4.5 8 H17.5 M14.5 5 L17.5 8 L14.5 11 M19.5 16 H6.5 M9.5 13 L6.5 16 L9.5 19";
    // A shield with a check: fix common errors.
    public const string Shield = "M12 3.5 L19 6 V11.5 C19 15.8 16.2 19 12 20.5 C7.8 19 5 15.8 5 11.5 V6 Z M9 12 L11 14 L15 10";
    // "A" with a check: spell check.
    public const string Spellcheck = "M3.5 14 L6.75 5 L10 14 M4.7 10.8 H8.8 M11 16.5 L14 19.5 L20.5 13";
    public const string Web = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M3.5 12 H20.5 M12 3.5 C9.5 6 8.5 9 8.5 12 C8.5 15 9.5 18 12 20.5 M12 3.5 C14.5 6 15.5 9 15.5 12 C15.5 15 14.5 18 12 20.5";
    public const string Network = "M12 4 A2 2 0 1 0 12 8 A2 2 0 1 0 12 4 Z M6 16 A2 2 0 1 0 6 20 A2 2 0 1 0 6 16 Z M18 16 A2 2 0 1 0 18 20 A2 2 0 1 0 18 16 Z M12 8 V12 M6 16 V14 A2 2 0 0 1 8 12 H16 A2 2 0 0 1 18 14 V16";
    public const string Netflix = "M7.5 20 V4 L16.5 20 V4";
    public const string DockTop = "M5.5 4.5 H18.5 A1.5 1.5 0 0 1 20 6 V18 A1.5 1.5 0 0 1 18.5 19.5 H5.5 A1.5 1.5 0 0 1 4 18 V6 A1.5 1.5 0 0 1 5.5 4.5 Z M4 9 H20";
    // Areas in a frame: layout.
    public const string Layout = "M5.5 4.5 H18.5 A1.5 1.5 0 0 1 20 6 V18 A1.5 1.5 0 0 1 18.5 19.5 H5.5 A1.5 1.5 0 0 1 4 18 V6 A1.5 1.5 0 0 1 5.5 4.5 Z M12 4.5 V19.5 M12 12 H20";
    // Wrench.
    public const string Tools = "M14.6 3.8 A4.6 4.6 0 0 0 10.4 10.1 L4.4 16.1 A2 2 0 0 0 7.2 18.9 L13.2 12.9 A4.6 4.6 0 0 0 19.5 8.7 L16.6 11.6 L13.5 10.5 L12.4 7.4 L15.3 4.5 A4.6 4.6 0 0 0 14.6 3.8 Z";

    // ---- panels ----

    public const string Subtitles = "M8.5 6.5 H19.5 M8.5 12 H19.5 M8.5 17.5 H19.5 M4.5 6.4 V6.6 M4.5 11.9 V12.1 M4.5 17.4 V17.6";
    // A text field with a caret.
    public const string TextEdit = "M5 6.5 H19 A1.5 1.5 0 0 1 20.5 8 V16 A1.5 1.5 0 0 1 19 17.5 H5 A1.5 1.5 0 0 1 3.5 16 V8 A1.5 1.5 0 0 1 5 6.5 Z M6.5 10 H11.5 M9 10 V14.5 M16.5 9 V15";
    public const string Notes = "M6 4 H18 A1.5 1.5 0 0 1 19.5 5.5 V14.5 L14 20 H6 A1.5 1.5 0 0 1 4.5 18.5 V5.5 A1.5 1.5 0 0 1 6 4 Z M14 20 V15.5 A1 1 0 0 1 15 14.5 H19.5 M8 8.5 H16 M8 12 H13";
    public const string Project = "M7.5 7.5 V6 A1.5 1.5 0 0 1 9 4.5 H12 L13.5 6 H19 A1.5 1.5 0 0 1 20.5 7.5 V14 A1.5 1.5 0 0 1 19 15.5 H17.5 M3.5 10 A1.5 1.5 0 0 1 5 8.5 H8.5 L10 10 H15 A1.5 1.5 0 0 1 16.5 11.5 V18 A1.5 1.5 0 0 1 15 19.5 H5 A1.5 1.5 0 0 1 3.5 18 Z";
    public const string PullRequest = "M6.5 4 A2 2 0 1 0 6.5 8 A2 2 0 1 0 6.5 4 Z M6.5 16 A2 2 0 1 0 6.5 20 A2 2 0 1 0 6.5 16 Z M17.5 16 A2 2 0 1 0 17.5 20 A2 2 0 1 0 17.5 16 Z M6.5 8 V16 M17.5 16 V9.5 A2.5 2.5 0 0 0 15 7 H11 M13 5 L11 7 L13 9";
    public const string Branch = "M7 4 A2 2 0 1 0 7 8 A2 2 0 1 0 7 4 Z M7 16 A2 2 0 1 0 7 20 A2 2 0 1 0 7 16 Z M17 4 A2 2 0 1 0 17 8 A2 2 0 1 0 17 4 Z M7 8 V16 M17 8 V9.5 A3 3 0 0 1 14 12.5 H10 A3 3 0 0 0 7 15.5";

    // Drag handles of a bar: lines across the direction it is dragged in.
    public const string GripVertical = "M9 8 V16 M12 8 V16 M15 8 V16";
    public const string GripHorizontal = "M8 9 H16 M8 12 H16 M8 15 H16";
}
