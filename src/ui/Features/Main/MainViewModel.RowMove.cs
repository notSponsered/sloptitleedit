using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.UiLogic.Edit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>
/// Drag-to-reorder in the subtitle list, Explorer style: press on a selected row and drag to move the
/// selected lines (dragging from an unselected row still range-selects). An accent line shows the gap
/// they'll drop into, the list scrolls near its edges, Esc cancels. The move is one undo step.
/// </summary>
public partial class MainViewModel
{
    private const double RowMoveThreshold = 6;
    private const double RowMoveScrollEdge = 20;

    private HashSet<SubtitleLineViewModel>? _rowMoveLines; // set while a press on a selected row may become a move
    private int _rowMovePressedIndex = -1;
    private double _rowMovePressY;
    private bool _rowMoveKeptSelection;
    private bool _rowMoveActive;
    private int _rowMoveInsertIndex = -1;
    private int _rowMoveHoverIndex = -1;
    private Point _rowMovePointer;
    private Border? _rowMoveLine;
    private DataGridRowsPresenter? _rowMoveRows;
    private DispatcherTimer? _rowMoveScrollTimer;
    private TopLevel? _rowMoveTopLevel;

    /// <returns>True when the press is on a selected row, so it may start a move (and drag-select must not start).</returns>
    private bool BeginRowMove(PointerPressedEventArgs e, int rowIndex)
    {
        if (rowIndex >= Subtitles.Count || !SubtitleGrid.SelectedItems.Contains(Subtitles[rowIndex]))
        {
            return false;
        }

        _rowMoveLines = SubtitleGrid.SelectedItems.OfType<SubtitleLineViewModel>().ToHashSet();
        _rowMovePressedIndex = rowIndex;
        _rowMovePressY = e.GetPosition(SubtitleGrid).Y;
        _rowMoveKeptSelection = _rowMoveLines.Count > 1;
        if (_rowMoveKeptSelection)
        {
            // The grid would collapse the selection to this row on press; that now happens on release,
            // unless the press turns into a drag. (Tapped/DoubleTapped still fire for handled presses.)
            e.Handled = true;
            SubtitleGrid.Focus();
        }

        return true;
    }

    /// <returns>True when the row move owns this pointer event.</returns>
    private bool UpdateRowMove(object? sender, PointerEventArgs e)
    {
        if (_rowMoveLines == null)
        {
            return false;
        }

        if (!e.GetCurrentPoint(SubtitleGrid).Properties.IsLeftButtonPressed)
        {
            EndRowMove(e.Pointer); // released outside / capture lost
            return true;
        }

        _rowMovePointer = e.GetPosition(SubtitleGrid);
        if (!_rowMoveActive)
        {
            if (Math.Abs(_rowMovePointer.Y - _rowMovePressY) < RowMoveThreshold)
            {
                return true;
            }

            _rowMoveActive = true;
            if (sender is Control control)
            {
                e.Pointer.Capture(control);
            }

            _rowMoveRows = SubtitleGrid.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
            _rowMoveLine = new Border { Height = 2, Background = new SolidColorBrush(ChromeStyles.Current.Accent), IsHitTestVisible = false };
            AdornerLayer.SetAdorner(SubtitleGrid, new Canvas { IsHitTestVisible = false, Children = { _rowMoveLine } });
            _rowMoveTopLevel = TopLevel.GetTopLevel(SubtitleGrid);
            _rowMoveTopLevel?.AddHandler(InputElement.KeyDownEvent, OnRowMoveKeyDown, RoutingStrategies.Tunnel);
            _rowMoveScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Input, (_, _) => RowMoveAutoScroll());
            _rowMoveScrollTimer.Start();
        }

        UpdateRowMoveTarget();
        e.Handled = true;
        return true;
    }

    /// <returns>True when the release ends a press on a selected row (a move, or a plain click).</returns>
    private bool FinishRowMove(PointerEventArgs e)
    {
        if (_rowMoveLines == null)
        {
            return false;
        }

        var lines = _rowMoveLines;
        var moved = _rowMoveActive;
        var insertIndex = _rowMoveInsertIndex;
        var pressedIndex = _rowMovePressedIndex;
        var keptSelection = _rowMoveKeptSelection;
        EndRowMove(e.Pointer);

        if (moved)
        {
            MoveLines(lines, insertIndex);
        }
        else if (keptSelection && pressedIndex < Subtitles.Count)
        {
            SubtitleGrid.SelectedItem = Subtitles[pressedIndex]; // a plain click: what the grid does on press
        }

        return true;
    }

    private void EndRowMove(IPointer? pointer)
    {
        var wasActive = _rowMoveActive;
        _rowMoveLines = null;
        _rowMoveActive = false;
        _rowMoveInsertIndex = -1;
        _rowMoveHoverIndex = -1;
        _rowMovePressedIndex = -1;
        _rowMoveScrollTimer?.Stop();
        _rowMoveTopLevel?.RemoveHandler(InputElement.KeyDownEvent, OnRowMoveKeyDown);
        _rowMoveTopLevel = null;
        _rowMoveLine = null;
        _rowMoveRows = null;
        if (wasActive)
        {
            AdornerLayer.SetAdorner(SubtitleGrid, null);
            pointer?.Capture(null);
        }
    }

    private void OnRowMoveKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _rowMoveActive)
        {
            e.Handled = true;
            EndRowMove(null);
        }
    }

    /// <summary>The gap under the pointer: above the hovered row's top half, below its bottom half.</summary>
    private void UpdateRowMoveTarget()
    {
        if (_rowMoveRows == null || _rowMoveLine == null)
        {
            return;
        }

        // Above or below the rows (header, outside the list): use the first/last visible row.
        var origin = _rowMoveRows.TranslatePoint(default, SubtitleGrid) ?? default;
        var probe = new Point(
            Math.Clamp(_rowMovePointer.X, origin.X + 1, origin.X + Math.Max(2, _rowMoveRows.Bounds.Width) - 1),
            Math.Clamp(_rowMovePointer.Y, origin.Y + 1, origin.Y + Math.Max(2, _rowMoveRows.Bounds.Height) - 1));
        if (GetDataGridRowFromPoint(probe) is not { } row || row.Index < 0 || row.Index >= Subtitles.Count)
        {
            return; // empty space below the last line: keep the last gap
        }

        var top = row.TranslatePoint(default, SubtitleGrid)?.Y ?? 0;
        var below = probe.Y > top + row.Bounds.Height / 2;
        _rowMoveHoverIndex = row.Index;
        _rowMoveInsertIndex = row.Index + (below ? 1 : 0);
        Canvas.SetTop(_rowMoveLine, (below ? top + row.Bounds.Height : top) - 1);
        _rowMoveLine.Width = SubtitleGrid.Bounds.Width;
    }

    private void RowMoveAutoScroll()
    {
        if (!_rowMoveActive || _rowMoveRows == null || _rowMoveHoverIndex < 0)
        {
            return;
        }

        var top = (_rowMoveRows.TranslatePoint(default, SubtitleGrid) ?? default).Y;
        var next = _rowMovePointer.Y < top + RowMoveScrollEdge ? _rowMoveHoverIndex - 1
            : _rowMovePointer.Y > top + _rowMoveRows.Bounds.Height - RowMoveScrollEdge ? _rowMoveHoverIndex + 1
            : -1;
        if (next < 0 || next >= Subtitles.Count)
        {
            return;
        }

        SubtitleGrid.ScrollIntoView(Subtitles[next], null);
        Dispatcher.UIThread.Post(UpdateRowMoveTarget, DispatcherPriority.Background); // once the rows have moved
    }

    private void MoveLines(IReadOnlySet<SubtitleLineViewModel> lines, int insertIndex)
    {
        var reordered = insertIndex < 0 ? null : LineMover.MoveTo(Subtitles, lines, insertIndex);
        if (reordered == null)
        {
            return;
        }

        RunWithoutChangeDetection(() =>
        {
            ReplaceSubtitles(reordered);
            Renumber();
        });

        var moved = reordered.Where(lines.Contains).ToList();
        _subtitleGridSelectionChangedSkip = true;
        SubtitleGrid.SelectedItems.Clear();
        foreach (var line in moved)
        {
            SubtitleGrid.SelectedItems.Add(line);
        }

        _subtitleGridSelectionChangedSkip = false;
        SubtitleGridSelectionChanged();
        SubtitleGrid.ScrollIntoView(moved[0], null);
    }
}
