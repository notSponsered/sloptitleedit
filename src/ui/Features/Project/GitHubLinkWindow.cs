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

namespace Nikse.SubtitleEdit.Features.Project;

// Import / connect, top to bottom in the order you do it:
//   heading + what will happen
//   Repository  [owner/name]
//   Local copy  [folder][…]   note: cloned here / existing copy of x
//               [Get repository]
//   Series folder  (list: name + "13 files in 2 seasons")
//   [ ] copy my files over (connect only)              [Create project | Connect] [Cancel]
public class GitHubLinkWindow : Window
{
    private readonly GitHubLinkViewModel _vm;

    public GitHubLinkWindow(GitHubLinkViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = Se.Language.Workspace.GitHub;
        Width = 640;
        Height = 600;
        MinWidth = 480;
        MinHeight = 440;
        CanResize = true;

        _vm = vm;
        vm.Window = this;
        DataContext = vm;
        var l = Se.Language.Project;

        TextBlock Caption(string text) => new() { Text = text, Classes = { "se-caption" }, Margin = new Thickness(0, 12, 0, 4) };

        var heading = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, [!TextBlock.TextProperty] = new Binding(nameof(vm.Heading)) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, [!TextBlock.TextProperty] = new Binding(nameof(vm.Intro)) };

        var repo = new TextBox { Watermark = "owner/name", [!TextBox.TextProperty] = new Binding(nameof(vm.Repository)) { Mode = BindingMode.TwoWay } };

        var browse = UiUtil.MakeBrowseButton(vm.BrowseLocalCopyCommand);
        browse.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(browse, Dock.Right);
        var localCopy = new DockPanel
        {
            Children = { browse, new TextBox { [!TextBox.TextProperty] = new Binding(nameof(vm.LocalCopy)) { Mode = BindingMode.TwoWay } } },
        };
        var note = new TextBlock { Classes = { "se-caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), [!TextBlock.TextProperty] = new Binding(nameof(vm.LocalNote)) };
        var get = new Button { Content = l.GetRepository, Command = vm.GetRepositoryCommand, Margin = new Thickness(0, 8, 0, 0) };

        var series = new ListBox
        {
            Classes = { "se-list" },
            MinHeight = 120,
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(vm.SeriesFolders)),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding(nameof(vm.SelectedSeries)) { Mode = BindingMode.TwoWay },
            ItemTemplate = new FuncDataTemplate<SeriesFolderRow>((row, _) => new StackPanel
            {
                Margin = new Thickness(8, 5),
                Children =
                {
                    new TextBlock { Text = row?.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = row?.Summary, Classes = { "se-caption" } },
                },
            }),
        };
        var seriesBlock = new DockPanel { [!Visual.IsVisibleProperty] = new Binding(nameof(vm.HasRepository)) };
        var seriesCaption = Caption(l.SeriesFolder);
        DockPanel.SetDock(seriesCaption, Dock.Top);
        seriesBlock.Children.Add(seriesCaption);
        seriesBlock.Children.Add(UiUtil.MakeBorderForControlNoPadding(series));

        var useLocal = new CheckBox
        {
            Content = new TextBlock { Text = l.UseLocalFiles, TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 10, 0, 0),
            [!ToggleButton.IsCheckedProperty] = new Binding(nameof(vm.UseLocalFiles)) { Mode = BindingMode.TwoWay },
            [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsConnect)),
        };

        var finish = new Button
        {
            Classes = { "accent" },
            [!ContentControl.ContentProperty] = new Binding(nameof(vm.FinishText)),
            Command = vm.FinishCommand,
            [!InputElement.IsEnabledProperty] = new Binding(nameof(vm.CanFinish)),
        };
        var buttons = UiUtil.MakeButtonBar(finish, UiUtil.MakeButtonCancel(vm.CancelCommand));
        var busy = new ProgressBar { IsIndeterminate = true, Height = 2, MinHeight = 2, [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsBusy)) };

        var top = new StackPanel
        {
            Children = { heading, intro, Caption(l.CloneRepository), repo, Caption(l.LocalCopy), localCopy, note, get },
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"), Margin = UiUtil.MakeWindowMargin() };
        root.Add(busy, 0, 0);
        root.Add(top, 1, 0);
        root.Add(seriesBlock, 2, 0);
        root.Add(useLocal, 3, 0);
        root.Add(buttons, 4, 0);
        Content = root;

        Loaded += (_, _) =>
        {
            UiUtil.RestoreWindowPosition(this);
            repo.Focus();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            _vm.OnKeyDown(e);
        }
    }
}
