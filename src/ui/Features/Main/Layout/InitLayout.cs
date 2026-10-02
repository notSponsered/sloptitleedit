using Avalonia.Controls;
using Nikse.SubtitleEdit.Controls.AudioVisualizerControl;
using System;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>Disposal helpers for main window content (the layout itself is built by <see cref="AreaHost"/>).</summary>
public static class InitLayout
{
    internal static void CleanupOldContent(Grid contentGrid)
    {
        // Recursively dispose controls and unhook events
        foreach (var child in contentGrid.Children)
        {
            CleanupControl(child);
        }

        contentGrid.Children.Clear();
    }

    private static void CleanupControl(Control? control)
    {
        if (control == null)
        {
            return;
        }

        // Handle AudioVisualizer - clear specific references
        if (control is AudioVisualizer audioVisualizer)
        {
            audioVisualizer.MenuFlyout = new MenuFlyout();
        }
        // Handle DataGrid - clear data bindings and sources
        else if (control is DataGrid dataGrid)
        {
            // Clear data bindings to prevent event handlers from being retained
            dataGrid.ItemsSource = null;
            dataGrid.SelectedItem = null;
            dataGrid.SelectedItems?.Clear();
            dataGrid.Columns.Clear();
            dataGrid.ContextFlyout = null;
        }
        // Handle TextBox - clear event handlers
        else if (control is TextBox textBox)
        {
            textBox.TextChanged -= null;
            textBox.GotFocus -= null;
            textBox.PointerReleased -= null;
        }
        // Handle Panels (Grid, StackPanel, etc.)
        else if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                CleanupControl(child);
            }
            panel.Children.Clear();
        }
        // Handle Borders
        else if (control is Border border)
        {
            CleanupControl(border.Child);
            border.Child = null;
        }
        // Handle ContentControl (Button, etc.)
        else if (control is ContentControl contentControl)
        {
            if (contentControl.Content is Control childControl)
            {
                CleanupControl(childControl);
            }
            contentControl.Content = null;
        }

        // Dispose if the control implements IDisposable
        if (control is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}