using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Project;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Project;

// Project editor:
//   ┌ General | Episodes | Metadata | Naming ─────────────────────────────┐
//   │ Episodes: [Season][Count][Per file][Add][Remove][Auto-map...]       │
//   │ ┌ S │ E │ E-end │ Abs │ Title │ Air date │ Video [..] │ Subtitle [..] ┐
//   │ Naming tokens: [scope] [Import from video] [Add token] [Apply]      │
//   │ ┌ Token │ Value (editable combo) ┐                                   │
//   └ status                                              [OK] [Cancel] ──┘
public class ProjectWindow : Window
{
    private readonly ProjectViewModel _vm;

    public ProjectWindow(ProjectViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = Se.Language.Project.Project.Replace("_", string.Empty);
        Width = 1150;
        Height = 760;
        MinWidth = 800;
        MinHeight = 500;
        CanResize = true;

        _vm = vm;
        vm.Window = this;
        DataContext = vm;

        var l = Se.Language.Project;
        var tabs = new TabControl
        {
            Resources = { ["TabItemHeaderFontSize"] = 15.0 }, // Fluent's 24 px headers are out of scale with the app chrome
            Items =
            {
                new TabItem { Header = l.Episodes, Content = MakeEpisodesTab(vm) },
                new TabItem { Header = l.Series, Content = MakeSeriesTab(vm) },
                new TabItem { Header = l.Naming, Content = MakeNamingTab(vm) },
            },
        };

        var status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.StatusText)),
        };
        var busy = new ProgressBar { IsIndeterminate = true, Width = 120, [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsBusy)) };
        var buttons = UiUtil.MakeButtonBar(UiUtil.MakeButtonOk(vm.OkCommand), UiUtil.MakeButtonCancel(vm.CancelCommand));

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        bottom.Add(busy, 0, 0);
        bottom.Add(status, 0, 1);
        bottom.Add(buttons, 0, 2);

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10, Margin = UiUtil.MakeWindowMargin() };
        root.Add(tabs, 0, 0);
        root.Add(bottom, 1, 0);
        Content = root;

        Loaded += (_, _) => UiUtil.RestoreWindowPosition(this);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
    };

    private static TextBox Text(string path, double width = double.NaN) => new()
    {
        Width = width,
        HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        [!TextBox.TextProperty] = new Binding(path) { Mode = BindingMode.TwoWay },
    };

    private static ComboBox Combo(string itemsPath, string selectedPath, double width) => new()
    {
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
        [!ItemsControl.ItemsSourceProperty] = new Binding(itemsPath),
        [!SelectingItemsControl.SelectedItemProperty] = new Binding(selectedPath) { Mode = BindingMode.TwoWay },
    };

    private static Grid Form(params (string Label, Control Control)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(Label(rows[i].Label), i, 0);
            grid.Add(rows[i].Control, i, 1);
        }

        return grid;
    }

    // Series = who this project is (folder, names, ids, optional GitHub link) + looking up episode titles.
    private static Control MakeSeriesTab(ProjectViewModel vm)
    {
        var l = Se.Language.Project;
        var folder = new DockPanel { LastChildFill = true };
        var browse = UiUtil.MakeBrowseButton(vm.BrowseFolderCommand);
        DockPanel.SetDock(browse, Dock.Right);
        folder.Children.Add(browse);
        folder.Children.Add(Text(nameof(vm.Folder)));

        var gitHub = new StackPanel { Spacing = 2 };
        var useGitHub = new CheckBox
        {
            Content = l.UseGitHub,
            [!ToggleButton.IsCheckedProperty] = new Binding("Project.UseGitHub") { Mode = BindingMode.TwoWay },
            [!InputElement.IsEnabledProperty] = new Binding(nameof(vm.IsInRepo)),
        };
        gitHub.Children.Add(useGitHub);
        gitHub.Children.Add(new TextBlock { Classes = { "se-caption" }, TextWrapping = TextWrapping.Wrap, [!TextBlock.TextProperty] = new Binding(nameof(vm.GitHubNote)) });

        var search = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        search.Children.Add(Combo(nameof(vm.MetaSources), nameof(vm.SelectedMetaSource), 120));
        var query = Text(nameof(vm.MetaQuery), 320);
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                vm.MetaSearchCommand.Execute(null);
            }
        };
        search.Children.Add(query);
        search.Children.Add(UiUtil.MakeButton(l.Search, vm.MetaSearchCommand));

        var results = new ListBox
        {
            Height = 200,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(vm.MetaResults)),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(vm.SelectedMetaResult)) { Mode = BindingMode.TwoWay },
            DisplayMemberBinding = new Binding(nameof(ShowSearchResult.Display)),
        };

        var lookUp = new TextBlock { Text = l.LookUpTitles, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) };

        // what spaces and "+" in titles become (on import, or for the titles already here)
        var separator = new StackPanel { Spacing = 2 };
        var separatorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        separatorRow.Children.Add(Text("Project.TitleSeparator", 80));
        separatorRow.Children.Add(UiUtil.MakeButton(l.ApplyToAllTitles, vm.ApplyTitleSeparatorCommand));
        separator.Children.Add(separatorRow);
        separator.Children.Add(new TextBlock { Text = l.TitleSeparatorHint, Classes = { "se-caption" }, TextWrapping = TextWrapping.Wrap });

        return new ScrollViewer
        {
            Content = Form(
                (l.Folder, folder),
                (l.Name, Text("Project.Name", 300)),
                (l.SeriesTitle, Text("Project.SeriesTitle", 400)),
                (l.Year, Text(nameof(vm.SeriesYearText), 100)),
                (l.TvdbId, Text("Project.TvdbId", 200)),
                (l.ImdbId, Text("Project.ImdbId", 200)),
                ("GitHub", gitHub),
                (string.Empty, lookUp),
                (l.TitleSeparator, separator),
                (l.Source, search),
                (string.Empty, results),
                (string.Empty, UiUtil.MakeButton(l.Import, vm.MetaImportCommand))),
        };
    }

    private static Control MakeEpisodesTab(ProjectViewModel vm)
    {
        var l = Se.Language.Project;

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 10, 0, 6) };
        toolbar.Children.Add(Label(l.Season));
        toolbar.Children.Add(UiUtil.MakeNumericUpDownInt(0, 99, 1, 110, vm, nameof(vm.AddSeason)));
        toolbar.Children.Add(Label(l.Count));
        toolbar.Children.Add(UiUtil.MakeNumericUpDownInt(1, 999, 12, 110, vm, nameof(vm.AddCount)));
        toolbar.Children.Add(Label(l.EpisodesPerFile));
        toolbar.Children.Add(UiUtil.MakeNumericUpDownInt(1, 99, 1, 110, vm, nameof(vm.AddPerFile)));
        toolbar.Children.Add(UiUtil.MakeButton(l.AddEpisodes, vm.AddEpisodesCommand));
        toolbar.Children.Add(UiUtil.MakeButton(l.Remove, vm.RemoveEpisodesCommand));
        toolbar.Children.Add(UiUtil.MakeButton(l.AutoMap, vm.AutoMapCommand));

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            CanUserSortColumns = false,
            CanUserResizeColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            [!DataGrid.ItemsSourceProperty] = new Binding("Project.Episodes"),
            [!DataGrid.SelectedItemProperty] = new Binding(nameof(vm.SelectedEpisode)) { Mode = BindingMode.TwoWay },
            Columns =
            {
                TextColumn(l.Season, nameof(ProjectEpisode.Season), 70),
                TextColumn(l.Episode, nameof(ProjectEpisode.EpisodeText), 90), // "5" or a range "1-4"
                TextColumn(l.EpisodeTitle, nameof(ProjectEpisode.Title), 200),
                TextColumn(l.AirDate, nameof(ProjectEpisode.AirDate), 100),
                FileColumn(l.Video, nameof(ProjectEpisode.Video), vm.BrowseVideoCommand),
                FileColumn(l.Subtitle, nameof(ProjectEpisode.Subtitle), vm.BrowseSubtitleCommand),
            },
        };
        grid.SelectionChanged += (_, _) => vm.SetSelectedEpisodes(grid.SelectedItems.OfType<ProjectEpisode>());

        var tokenBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 6) };
        tokenBar.Children.Add(new TextBlock { Text = l.Tokens, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        tokenBar.Children.Add(Combo(nameof(vm.TokenScopes), nameof(vm.SelectedTokenScope), 180));
        tokenBar.Children.Add(UiUtil.MakeButton(l.ImportFromVideo, vm.ImportFromVideoCommand));
        tokenBar.Children.Add(UiUtil.MakeButton(l.AddToken, vm.AddTokenCommand));
        tokenBar.Children.Add(UiUtil.MakeButton(l.ApplyTokens, vm.ApplyTokensCommand));

        var tokens = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            CanUserSortColumns = false,
            [!DataGrid.ItemsSourceProperty] = new Binding(nameof(vm.TokenRows)),
            Columns =
            {
                new DataGridTextColumn
                {
                    Header = l.Token,
                    Binding = new Binding(nameof(TokenRow.Name)) { Mode = BindingMode.TwoWay },
                    Width = new DataGridLength(260),
                },
                new DataGridTemplateColumn
                {
                    Header = l.Value,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
                    CellTemplate = new FuncDataTemplate<TokenRow>((row, _) => new ComboBox
                    {
                        IsEditable = true,
                        ItemsSource = row?.Options ?? [],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(2),
                        [!ComboBox.TextProperty] = new Binding(nameof(TokenRow.Value)) { Mode = BindingMode.TwoWay },
                    }),
                },
            },
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,2*,Auto,*") };
        root.Add(toolbar, 0, 0);
        root.Add(UiUtil.MakeBorderForControlNoPadding(grid), 1, 0);
        root.Add(tokenBar, 2, 0);
        root.Add(UiUtil.MakeBorderForControlNoPadding(tokens), 3, 0);
        return root;
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path) { Mode = BindingMode.TwoWay },
        Width = new DataGridLength(width),
    };

    private static DataGridTemplateColumn FileColumn(string header, string path, CommunityToolkit.Mvvm.Input.IRelayCommand browse) => new()
    {
        Header = header,
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        CellTheme = UiUtil.DataGridNoBorderNoPaddingCellTheme,
        CellTemplate = new FuncDataTemplate<ProjectEpisode>((row, _) =>
        {
            var button = UiUtil.MakeBrowseButton(browse);
            button.CommandParameter = row;
            button.Margin = new Thickness(2);
            DockPanel.SetDock(button, Dock.Right);
            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.LeadingCharacterEllipsis,
                Margin = new Thickness(4, 0),
                [!TextBlock.TextProperty] = new Binding(path),
                [!ToolTip.TipProperty] = new Binding(path),
            };
            return new DockPanel { Children = { button, text } };
        }),
    };

    private static Control MakeNamingTab(ProjectViewModel vm)
    {
        var l = Se.Language.Project;

        var preset = Combo(nameof(vm.Presets), nameof(vm.SelectedPreset), 300);
        preset.DisplayMemberBinding = new Binding(nameof(NamingPreset.Name));

        var preview = new SelectableTextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.NamingPreview)),
        };

        var help = new SelectableTextBlock { Text = vm.TokenHelp, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };

        var rename = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        rename.Children.Add(UiUtil.MakeButton(l.RenameFiles, vm.RenameFilesCommand));
        rename.Children.Add(UiUtil.MakeCheckBox(l.AlsoRenameVideos, vm, nameof(vm.AlsoRenameVideos)));

        return Form(
            (l.Preset, preset),
            (l.Template, Text("Project.NamingTemplate")),
            (l.MultiEpisodeStyle, Combo(nameof(vm.MultiEpisodeStyles), "Project.MultiEpisodeStyle", 200)),
            (l.Preview, preview),
            (string.Empty, help),
            (string.Empty, rename));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            _vm.OnKeyDown(e); // Esc while editing a cell only cancels the edit
        }
    }
}
