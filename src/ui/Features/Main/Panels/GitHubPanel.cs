using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Features.Project;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Optris.Icons.Avalonia;
using MenuItem = Avalonia.Controls.MenuItem;
using System.Collections.Specialized;
using System.Windows.Input;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>
/// Optional source control (git + gh) for whatever is open: the project folder, or the open subtitle's folder.
/// Reads top to bottom in working order: where you are (branch / pull request) → your changes → one main action
/// (create a pull request, or push to the one you're on) → other people's pull requests → command log.
/// Every state that can't do that yet (nothing open, no gh, signed out, not a repository) is a short guided step.
/// </summary>
internal static class GitHubPanel
{
    public static Control Make(MainViewModel vm)
    {
        var gh = vm.CreateGitHubViewModel();
        var p = Se.Language.Project;

        var root = new Grid
        {
            DataContext = gh,
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                new ProgressBar { IsIndeterminate = true, Height = 2, MinHeight = 2, [!Visual.IsVisibleProperty] = new Binding(nameof(gh.IsBusy)) },
            },
        };

        var states = new Grid
        {
            Children =
            {
                Scroll(MakeReady(gh), nameof(gh.IsReady)),
                Scroll(MakeSetup(gh), nameof(gh.NeedsSetup)),
                Scroll(MakeNotRepo(gh), nameof(gh.IsNotRepo)),
                Scroll(Guide(p.NothingOpenTitle, p.NothingOpenBody), nameof(gh.IsNothingOpen)),
            },
        };
        Grid.SetRow(states, 1);
        root.Children.Add(states);

        // start over when the folder/file/episode changes; re-read git each time the panel comes back on screen
        string? shownKey = null;
        root.AttachedToVisualTree += (_, _) => shownKey = null;
        PanelUtil.PollWhileVisible(root, () =>
        {
            if (vm.GitHubContextKey != shownKey)
            {
                shownKey = vm.GitHubContextKey;
                vm.InitializeGitHubViewModel(gh);
            }
        });

        return root;
    }

    // ── ready ────────────────────────────────────────────────────────────────────

    private static Control MakeReady(GitHubViewModel gh)
    {
        var p = Se.Language.Project;

        // where you are
        var branch = new SelectableTextBlock { Classes = { "se-heading" }, FontSize = 15, [!TextBlock.TextProperty] = new Binding(nameof(gh.Branch)) };
        var note = new Button
        {
            Classes = { "se-tool" },
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Classes = { "se-caption" }, TextWrapping = TextWrapping.Wrap, [!TextBlock.TextProperty] = new Binding(nameof(gh.BranchNote)) },
            Command = gh.OpenOnGitHubCommand,
            [!Button.CommandParameterProperty] = new Binding(nameof(gh.CurrentPr)),
            [!InputElement.IsHitTestVisibleProperty] = new Binding(nameof(gh.HasPr)),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var refresh = IconButton("mdi-refresh", p.Refresh, gh.RefreshCommand);
        var switchItem = new MenuItem { [!HeaderedSelectingItemsControl.HeaderProperty] = new Binding(nameof(gh.SwitchToBaseText)), Command = gh.SwitchToBaseCommand };
        var more = IconButton("mdi-dots-horizontal", p.MoreActions, null);
        var moreMenu = new MenuFlyout
        {
            Items =
            {
                new MenuItem { Header = p.PullLatest, Command = gh.PullLatestCommand },
                switchItem,
            },
        };
        moreMenu.Opening += (_, _) => switchItem.IsVisible = !gh.IsOnBase;
        more.Flyout = moreMenu;

        var where = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(12, 10, 8, 10) };
        where.Add(new Icon { Value = "mdi-source-branch", FontSize = 18, Opacity = 0.6, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top }, 0, 0);
        where.Add(new StackPanel { Children = { branch, note } }, 0, 1);
        where.Add(refresh, 0, 2);
        where.Add(more, 0, 3);

        // your changes → the one main action
        var files = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(gh.ChangedFiles)),
            ItemTemplate = new FuncDataTemplate<ChangedFileRow>((row, _) =>
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
                line.Add(new TextBlock { Text = row?.Episode, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
                line.Add(new TextBlock { Text = row?.FileName, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, [ToolTip.TipProperty] = row?.FileName }, 0, 1);
                line.Add(new TextBlock { Text = row?.Change, Classes = { "se-caption" }, VerticalAlignment = VerticalAlignment.Center }, 0, 2);
                return new CheckBox
                {
                    Content = line,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    [!ToggleButton.IsCheckedProperty] = new Binding(nameof(ChangedFileRow.IsChecked)) { Mode = BindingMode.TwoWay },
                };
            }),
        };

        var newPr = Visible(Disclosure(p.NewPullRequest, new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Field(p.PrTitle, nameof(gh.PrTitle)),
                Field(p.PrDescription, nameof(gh.PrBody), multiLine: true),
                Visible(Field(p.NewBranch, nameof(gh.NewBranch)), nameof(gh.ShowNewBranch)),
            },
        }), nameof(gh.ShowNewPrFields));
        newPr.Margin = new Thickness(-2, 6, 0, 0);

        var primary = new Button
        {
            Classes = { "accent" },
            Margin = new Thickness(0, 10, 0, 0),
            [!ContentControl.ContentProperty] = new Binding(nameof(gh.PrimaryText)),
            Command = gh.PrimaryCommand,
        };

        var changes = new StackPanel
        {
            Margin = new Thickness(12, 12, 12, 14),
            Children =
            {
                SectionTitle(p.Changes, nameof(gh.ChangesSummary)),
                Visible(new TextBlock { Text = p.NoChanges, Classes = { "se-caption" }, Margin = new Thickness(0, 2, 0, 6) }, nameof(gh.ChangesSummary), StringConverters.IsNullOrEmpty),
                files,
                Field(p.CommitMessage, nameof(gh.CommitMessage), margin: new Thickness(0, 8, 0, 0)),
                newPr,
                primary,
            },
        };

        // other people's pull requests
        var prs = new ListBox
        {
            Classes = { "se-list" },
            MaxHeight = 280,
            Margin = new Thickness(-12, 4, -12, 0),
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(gh.OpenPrs)),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(gh.SelectedPr)) { Mode = BindingMode.TwoWay },
            ItemTemplate = new FuncDataTemplate<PrRow>((row, _) =>
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("3,*"), Background = Brushes.Transparent };
                g.Add(new Border { Classes = { "se-marker" } }, 0, 0);
                g.Add(new StackPanel
                {
                    Margin = new Thickness(9, 6, 12, 6),
                    Children =
                    {
                        new TextBlock { Text = row?.Heading, Classes = { "se-strong" }, TextTrimming = TextTrimming.CharacterEllipsis, [ToolTip.TipProperty] = row?.Heading },
                        new TextBlock { Text = row?.Byline, Classes = { "se-caption" }, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                }, 0, 1);
                return g;
            }),
        };

        void MarkCheckedOut()
        {
            foreach (var item in prs.GetRealizedContainers())
            {
                item.Classes.Set("current", item.DataContext is PrRow row && row.Number == gh.CurrentPr?.Number);
            }
        }

        prs.ContainerPrepared += (_, _) => MarkCheckedOut();
        ((INotifyCollectionChanged)gh.OpenPrs).CollectionChanged += (_, _) => MarkCheckedOut();
        gh.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(gh.CurrentPr))
            {
                MarkCheckedOut();
            }
        };

        var mergeMenu = new MenuFlyout
        {
            Items =
            {
                new MenuItem { Header = p.SquashAndMerge, Command = gh.MergeCommand, CommandParameter = "squash" },
                new MenuItem { Header = p.CreateMergeCommit, Command = gh.MergeCommand, CommandParameter = "merge" },
                new MenuItem { Header = p.RebaseAndMerge, Command = gh.MergeCommand, CommandParameter = "rebase" },
            },
        };
        var prActions = new WrapPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            [!Visual.IsVisibleProperty] = new Binding(nameof(gh.HasSelectedPr)), // appear once a pull request is picked
            Children =
            {
                Small(p.CheckOut, gh.CheckOutCommand),
                new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children = { new TextBlock { Text = p.Merge }, new Icon { Value = "mdi-chevron-down", FontSize = 14 } },
                    },
                    Flyout = mergeMenu,
                    Margin = new Thickness(0, 0, 6, 6),
                },
                Small(p.OpenOnGitHub, gh.OpenOnGitHubCommand),
            },
        };

        var pullRequests = new StackPanel
        {
            Margin = new Thickness(12, 12, 12, 14),
            Children =
            {
                SectionTitle(p.PullRequests, nameof(gh.PrsSummary)),
                Visible(new TextBlock { Text = p.NoPullRequests, Classes = { "se-caption" }, Margin = new Thickness(0, 2, 0, 0) }, nameof(gh.PrsSummary), StringConverters.IsNullOrEmpty),
                prs,
                prActions,
            },
        };

        var log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            FontSize = 11,
            MaxHeight = 220,
            [!TextBox.TextProperty] = new Binding(nameof(gh.Log)),
        };
        log.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                log.CaretIndex = log.Text?.Length ?? 0;
            }
        };

        var content = new StackPanel
        {
            Children =
            {
                where,
                Rule(),
                changes,
                Rule(),
                pullRequests,
                Rule(),
                WithMargin(Disclosure(p.CommandLog, log), new Thickness(10, 8, 12, 12)),
            },
        };

        // a readable form in a wide area (left aligned, max 760 px); narrow areas still get full width
        var column = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star) { MaxWidth = 760 }, new ColumnDefinition(GridLength.Auto) } };
        column.Children.Add(content);
        return column;
    }

    // ── guided states ────────────────────────────────────────────────────────────

    private static Control MakeSetup(GitHubViewModel gh)
    {
        var commands = new Border
        {
            Classes = { "se-area-header" },
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new SelectableTextBlock
            {
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
                FontSize = 12,
                [!TextBlock.TextProperty] = new Binding(nameof(gh.SetupCommands)),
            },
        };

        return new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = new Binding(nameof(gh.SetupTitle)) },
                new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, [!TextBlock.TextProperty] = new Binding(nameof(gh.SetupBody)) },
                commands,
                new Button { Content = Se.Language.Project.CheckAgain, Command = gh.RefreshCommand, Classes = { "accent" }, Margin = new Thickness(0, 6, 0, 0) },
            },
        };
    }

    private static Control MakeNotRepo(GitHubViewModel gh)
    {
        var p = Se.Language.Project;
        var browse = new Button { Content = "...", Command = gh.BrowseCloneFolderCommand, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);

        return new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new TextBlock { Text = p.NotRepoTitle, FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, [!TextBlock.TextProperty] = new Binding(nameof(gh.NotRepoBody)) },
                Field(p.CloneRepository, nameof(gh.CloneRepo), watermark: "owner/name", margin: new Thickness(0, 6, 0, 0)),
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = p.CloneInto, Classes = { "se-caption" }, Margin = new Thickness(0, 0, 0, 4) },
                        new DockPanel { Children = { browse, new TextBox { [!TextBox.TextProperty] = new Binding(nameof(gh.CloneFolder)) { Mode = BindingMode.TwoWay } } } },
                    },
                },
                new Button { Content = p.Clone, Command = gh.CloneCommand, Classes = { "accent" }, Margin = new Thickness(0, 6, 0, 0) },
            },
        };
    }

    private static Control Guide(string title, string body) => new StackPanel
    {
        Margin = new Thickness(16),
        Spacing = 8,
        MaxWidth = 420,
        HorizontalAlignment = HorizontalAlignment.Left,
        Children =
        {
            new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
        },
    };

    // ── small parts ──────────────────────────────────────────────────────────────

    private static ScrollViewer Scroll(Control content, string isVisiblePath) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        [!Visual.IsVisibleProperty] = new Binding(isVisiblePath),
    };

    private static Border Rule() => new() { Classes = { "se-sep" }, Height = 1 };

    /// <summary>A quiet "› Title" toggle that shows/hides <paramref name="body"/> (lighter than Fluent's Expander box).</summary>
    private static StackPanel Disclosure(string title, Control body)
    {
        var chevron = new Icon { Value = "mdi-chevron-right", FontSize = 16, Opacity = 0.7 };
        var toggle = new Button
        {
            Classes = { "se-tool" },
            Padding = new Thickness(2, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { chevron, new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        body.IsVisible = false;
        body.Margin = new Thickness(22, 6, 0, 0);
        toggle.Click += (_, _) =>
        {
            body.IsVisible = !body.IsVisible;
            chevron.Value = body.IsVisible ? "mdi-chevron-down" : "mdi-chevron-right";
        };
        return new StackPanel { Children = { toggle, body } };
    }

    private static T WithMargin<T>(T control, Thickness margin) where T : Control
    {
        control.Margin = margin;
        return control;
    }

    /// <summary>Section title with a quiet count on the right ("Changes … 2 files").</summary>
    private static Grid SectionTitle(string title, string countPath)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        g.Add(new TextBlock { Text = title, Classes = { "se-heading" } }, 0, 0);
        g.Add(new TextBlock { Classes = { "se-caption" }, VerticalAlignment = VerticalAlignment.Center, [!TextBlock.TextProperty] = new Binding(countPath) }, 0, 1);
        return g;
    }

    private static StackPanel Field(string caption, string path, bool multiLine = false, string? watermark = null, Thickness? margin = null) => new()
    {
        Margin = margin ?? new Thickness(0),
        Children =
        {
            new TextBlock { Text = caption, Classes = { "se-caption" }, Margin = new Thickness(0, 0, 0, 4) },
            new TextBox
            {
                Watermark = watermark,
                AcceptsReturn = multiLine,
                TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multiLine ? 64 : 0,
                [!TextBox.TextProperty] = new Binding(path) { Mode = BindingMode.TwoWay },
            },
        },
    };

    private static T Visible<T>(T control, string path, IValueConverter? converter = null) where T : Control
    {
        control.Bind(Visual.IsVisibleProperty, new Binding(path) { Converter = converter });
        return control;
    }

    private static Button IconButton(string icon, string tip, ICommand? command) => new()
    {
        Classes = { "se-tool" },
        Content = new Icon { Value = icon, FontSize = 18 },
        Padding = new Thickness(6),
        Margin = new Thickness(2, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Top,
        Command = command,
        [ToolTip.TipProperty] = tip,
    };

    private static Button Small(string text, ICommand command) => new() { Content = text, Command = command, Margin = new Thickness(0, 0, 6, 6) };
}
