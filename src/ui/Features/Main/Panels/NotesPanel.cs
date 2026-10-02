using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Project;
using System;
using System.IO;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>
/// Free-text notes. General notes live in the settings folder; project/episode notes in the project
/// sidecar (.seproject.json). Nothing is ever written into the subtitle file.
/// </summary>
internal static class NotesPanel
{
    private static readonly object GeneralNotes = new();
    private static string GeneralNotesPath => Path.Combine(Se.DataFolder, "notes.txt");

    public static Control Make(MainViewModel vm)
    {
        var l = Se.Language.Workspace;
        var scope = new ComboBox
        {
            ItemsSource = new[] { l.NotesGeneral, l.NotesProject, l.NotesEpisode },
            SelectedIndex = 0,
            Margin = new Thickness(6),
            MinWidth = 120,
        };
        var text = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 0, 6, 6),
        };
        var noProject = PanelUtil.MakePlaceholder(l.NoProjectOpen);

        object? loadedFor = null;
        var loadedText = string.Empty; // what is on disk/in the project; saving only when the text differs
        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        object? Target() => scope.SelectedIndex switch
        {
            0 => GeneralNotes,
            1 => vm.Project,
            _ => vm.Project == null ? null : vm.SelectedProjectEpisode,
        };

        void Save()
        {
            saveTimer.Stop();
            var value = text.Text ?? string.Empty;
            if (value == loadedText)
            {
                return;
            }

            loadedText = value;
            try
            {
                switch (loadedFor)
                {
                    case SeProject project:
                        project.Notes = value;
                        project.Save();
                        break;
                    case ProjectEpisode episode:
                        episode.Notes = value;
                        vm.Project?.Save();
                        break;
                    default:
                        if (loadedFor == GeneralNotes)
                        {
                            File.WriteAllText(GeneralNotesPath, value);
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                Se.LogError(e, "Saving notes");
            }
        }

        void Load()
        {
            Save();
            var target = Target();
            loadedFor = target;
            text.IsVisible = target != null;
            noProject.IsVisible = target == null;
            loadedText = target switch
            {
                SeProject project => project.Notes,
                ProjectEpisode episode => episode.Notes,
                _ when target == GeneralNotes && File.Exists(GeneralNotesPath) => File.ReadAllText(GeneralNotesPath),
                _ => string.Empty,
            };
            text.Text = loadedText;
        }

        saveTimer.Tick += (_, _) => Save();
        text.TextChanged += (_, _) =>
        {
            saveTimer.Stop();
            saveTimer.Start();
        };
        text.LostFocus += (_, _) => Save();
        scope.SelectionChanged += (_, _) => Load();

        DockPanel.SetDock(scope, Dock.Top);
        var body = new Grid { Children = { text, noProject } };
        var root = new DockPanel { Children = { scope, body } };
        root.DetachedFromVisualTree += (_, _) => Save();

        // Follow project open/close and episode switches.
        PanelUtil.PollWhileVisible(root, () =>
        {
            if (!ReferenceEquals(Target(), loadedFor))
            {
                Load();
            }
        });

        return root;
    }
}
