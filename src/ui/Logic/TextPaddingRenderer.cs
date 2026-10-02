using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Edit;

namespace Nikse.SubtitleEdit.Logic;

/// <summary>
/// Shows the padding in the subtitle text box: a tinted box behind spaces / \h before the first and after the
/// last visible character of each line, and a full-width band on blank lines (the vertical padding made with
/// empty lines). Text box right-click → Show padding turns it off.
/// </summary>
public sealed class TextPaddingRenderer : IBackgroundRenderer
{
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(95, 255, 158, 101)); // Audacity 4 orange
    private static readonly IBrush BlankFill = new SolidColorBrush(Color.FromArgb(45, 255, 158, 101));

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Se.Settings.Appearance.SubtitleTextBoxShowPadding || textView.Document == null || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (var visualLine in textView.VisualLines)
        {
            var line = visualLine.FirstDocumentLine;
            var text = textView.Document.GetText(line);
            if (TextPadding.IsBlank(text) && textView.Document.LineCount > 1)
            {
                // a line with nothing visible: the whole row is padding
                var y = visualLine.VisualTop - textView.ScrollOffset.Y;
                drawingContext.FillRectangle(BlankFill, new Rect(0, y, textView.Bounds.Width, visualLine.Height));
            }

            foreach (var (start, length) in TextPadding.Find(text))
            {
                var segment = new TextSegment { StartOffset = line.Offset + start, Length = length };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    drawingContext.FillRectangle(Fill, rect, 2);
                }
            }
        }
    }
}
