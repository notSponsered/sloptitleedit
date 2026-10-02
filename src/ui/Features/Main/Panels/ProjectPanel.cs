using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Project;
using Optris.Icons.Avalonia;
using MenuItem = Avalonia.Controls.MenuItem;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>
/// The open project as an episode index: series title, then each season with its files. The episode numbers sit in
/// a fixed gutter (the thing you scan for); the episode you are on gets the accent bar. Click or Enter switches.
/// Knows nothing about GitHub except the optional Connect/GitHub entries in its menu.
/// </summary>
internal static class ProjectPanel
{
    private sealed record SeasonRow(string Title, string Count);

    public static Control Make(MainViewModel vm)
    {
        var p = Se.Language.Project;

        // ── header: series + summary, Edit project, more ─────────────────────────
        var title = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var summary = new TextBlock { Classes = { "se-caption" } };
        var edit = new Button { Content = Clean(p.EditProject), Command = vm.ProjectEditCommand, VerticalAlignment = VerticalAlignment.Center };
        var more = new Button
        {
            Classes = { "se-tool" },
            Content = new Icon { Value = "mdi-dots-horizontal", FontSize = 18 },
            Padding = new Thickness(6),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = p.MoreActions,
            Flyout = MakeMoreMenu(vm),
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(12, 10, 8, 10) };
        header.Add(new StackPanel { Children = { title, summary } }, 0, 0);
        header.Add(edit, 0, 1);
        header.Add(more, 0, 2);
        var rule = new Border { Classes = { "se-sep" }, Height = 1 };

        // ── episode index ────────────────────────────────────────────────────────
        var list = new ListBox { Classes = { "se-list" }, SelectionMode = SelectionMode.Single };
        list.DataTemplates.Add(new FuncDataTemplate<SeasonRow>((row, _) =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 14, 12, 4) };
            g.Add(new TextBlock { Text = row?.Title, Classes = { "se-caption" }, FontWeight = FontWeight.SemiBold }, 0, 0);
            g.Add(new TextBlock { Text = row?.Count, Classes = { "se-caption" } }, 0, 1);
            return g;
        }));
        list.DataTemplates.Add(new FuncDataTemplate<ProjectEpisode>((ep, _) => MakeEpisodeRow(ep, vm)));

        var noEpisodes = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = p.NoEpisodes, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
                Action(p.EditProject, vm.ProjectEditCommand, accent: true),
            },
        };

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(rule, Dock.Top);
        var open = new DockPanel { Children = { header, rule, new Grid { Children = { list, noEpisodes } } } };

        // ── no project: what a project is, and the ways to get one ───────────────
        var closed = new StackPanel
        {
            MaxWidth = 340,
            Margin = new Thickness(20),
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = Se.Language.Workspace.NoProjectOpen, FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = p.ProjectIntro, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
                new WrapPanel
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    Children =
                    {
                        Action(p.NewProject, vm.ProjectNewCommand, accent: true),
                        Action(p.OpenProject, vm.ProjectOpenCommand),
                        Action(p.ImportFromGitHub, vm.ProjectImportFromGitHubCommand),
                    },
                },
            },
        };

        var root = new Grid { Children = { open, closed } };

        // ── keep in step with the main window ────────────────────────────────────
        string? shownSignature = null;

        void MarkCurrent()
        {
            foreach (var item in list.GetRealizedContainers())
            {
                item.Classes.Set("current", item.DataContext is ProjectEpisode ep && ep == vm.SelectedProjectEpisode);
            }
        }

        void Refresh()
        {
            var project = vm.Project;
            open.IsVisible = project != null;
            closed.IsVisible = project == null;
            if (project == null)
            {
                shownSignature = null;
                return;
            }

            // rebuild only when something visible changed (titles, ranges, files)
            var signature = project.GetHashCode() + "|" + project.Name + "|" + string.Join(";",
                project.Episodes.Select(e => $"{e.Tag}:{e.Title}:{e.Subtitle}:{e.Video}"));
            if (signature == shownSignature)
            {
                return;
            }

            shownSignature = signature;
            title.Text = string.IsNullOrWhiteSpace(project.SeriesTitle) ? project.Name : project.SeriesTitle;
            var seasons = project.Episodes.GroupBy(e => e.Season).OrderBy(g => g.Key).ToList();
            summary.Text = string.Format(p.FilesXInSeasonsY, Files(project.Episodes.Count),
                seasons.Count == 1 ? p.OneSeason : string.Format(p.XSeasons, seasons.Count));

            var items = new List<object>();
            foreach (var season in seasons)
            {
                items.Add(new SeasonRow(season.Key == 0 ? p.Specials : string.Format(p.SeasonX, season.Key), Files(season.Count())));
                items.AddRange(season.OrderBy(e => e.Episode));
            }

            list.ItemsSource = items;
            list.SelectedItem = vm.SelectedProjectEpisode;
            noEpisodes.IsVisible = items.Count == 0;
            MarkCurrent();
        }

        list.ContainerPrepared += (_, e) =>
        {
            if (e.Container is ListBoxItem item)
            {
                item.IsEnabled = item.DataContext is not SeasonRow; // headings: not clickable, skipped by arrow keys
                item.Classes.Set("current", item.DataContext is ProjectEpisode ep && ep == vm.SelectedProjectEpisode);
            }
        };

        // click or Enter switches episode; arrow keys only move the highlight (no save prompt per key press)
        list.Tapped += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(true)?.DataContext is ProjectEpisode ep)
            {
                vm.SelectedProjectEpisode = ep;
            }
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && list.SelectedItem is ProjectEpisode ep)
            {
                vm.SelectedProjectEpisode = ep;
                e.Handled = true;
            }
        };

        void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedProjectEpisode))
            {
                MarkCurrent();
                if (vm.SelectedProjectEpisode != null)
                {
                    list.SelectedItem = vm.SelectedProjectEpisode;
                }
            }
            else if (e.PropertyName is nameof(MainViewModel.Project) or nameof(MainViewModel.IsProjectOpen))
            {
                Refresh();
            }
        }

        root.AttachedToVisualTree += (_, _) => vm.PropertyChanged += OnVmChanged;
        root.DetachedFromVisualTree += (_, _) => vm.PropertyChanged -= OnVmChanged;
        PanelUtil.PollWhileVisible(root, Refresh); // titles/files edited elsewhere (editor, auto-create on switch)

        return root;
    }

    /// <summary>One episode: accent bar · number gutter · title with missing-file notes underneath.</summary>
    private static Control MakeEpisodeRow(ProjectEpisode? ep, MainViewModel vm)
    {
        if (ep == null)
        {
            return new TextBlock();
        }

        var p = Se.Language.Project;
        var number = ep.LastEpisode > ep.Episode ? $"{ep.Episode:00}–{ep.LastEpisode:00}" : $"{ep.Episode:00}";
        var hasSubtitle = vm.Project != null && ep.Subtitle.Length > 0 && File.Exists(vm.Project.ResolvePath(ep.Subtitle));
        var hasVideo = vm.Project != null && ep.Video.Length > 0 && File.Exists(vm.Project.ResolvePath(ep.Video));

        var text = new StackPanel { Margin = new Thickness(10, 7, 12, 7), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(ep.Title) ? ep.Tag : ep.Title,
            Opacity = string.IsNullOrWhiteSpace(ep.Title) ? 0.6 : 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            [ToolTip.TipProperty] = ep.Display,
        });

        var missing = new WrapPanel();
        if (!hasSubtitle)
        {
            missing.Children.Add(new TextBlock { Text = p.NoSubtitleYet, Classes = { "se-caption" }, Margin = new Thickness(0, 0, 10, 0) });
        }

        if (!hasVideo)
        {
            missing.Children.Add(new TextBlock { Text = p.NoVideo, Classes = { "se-caption" } });
        }

        if (missing.Children.Count > 0)
        {
            text.Children.Add(missing);
        }

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("3,64,*"), Background = Brushes.Transparent };
        row.Add(new Border { Classes = { "se-marker" } }, 0, 0);
        row.Add(new TextBlock
        {
            Text = number,
            Classes = { "se-strong" },
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(9, 7, 0, 7),
            VerticalAlignment = VerticalAlignment.Top,
        }, 0, 1);
        row.Add(text, 0, 2);
        return row;
    }

    private static MenuFlyout MakeMoreMenu(MainViewModel vm)
    {
        var p = Se.Language.Project;
        var connect = new MenuItem { Header = p.ConnectToGitHub, Command = vm.ProjectConnectGitHubCommand };
        var gitHub = new MenuItem { Header = p.GitHub, Command = vm.ShowProjectGitHubCommand };
        var flyout = new MenuFlyout
        {
            Items =
            {
                new MenuItem { Header = p.EditProjectStyles, Command = vm.ProjectEditStylesCommand },
                new MenuItem { Header = p.ApplyProjectStyles, Command = vm.ProjectApplyStylesCommand },
                new Separator(),
                connect,
                gitHub,
                new Separator(),
                new MenuItem { Header = p.CloseProject, Command = vm.ProjectCloseCommand },
            },
        };

        // the optional GitHub link: offer Connect until it is on, then GitHub…
        flyout.Opening += (_, _) =>
        {
            connect.IsVisible = vm.CanConnectProjectToGitHub;
            gitHub.IsVisible = vm.IsProjectUsingGitHub;
        };
        return flyout;
    }

    private static Button Action(string text, ICommand command, bool accent = false)
    {
        var button = new Button { Content = Clean(text), Command = command, Margin = new Thickness(0, 0, 8, 8) };
        if (accent)
        {
            button.Classes.Add("accent");
        }

        return button;
    }

    private static string Files(int count) =>
        count == 1 ? Se.Language.Project.OneFile : string.Format(Se.Language.Project.XFiles, count);

    private static string Clean(string menuText) => menuText.Replace("_", string.Empty);
}
