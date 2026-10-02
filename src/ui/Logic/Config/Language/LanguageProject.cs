using System;

namespace Nikse.SubtitleEdit.Logic.Config.Language;

public class LanguageProject
{
    // menu / picker / shortcuts
    public string Project { get; set; }
    public string NewProject { get; set; }
    public string OpenProject { get; set; }
    public string EditProject { get; set; }
    public string CloseProject { get; set; }
    public string NextEpisode { get; set; }
    public string PreviousEpisode { get; set; }
    public string EditProjectStyles { get; set; }
    public string ApplyProjectStyles { get; set; }
    public string Episode { get; set; }
    public string GitHub { get; set; }
    public string UseGitHub { get; set; }
    public string UseGitHubHint { get; set; }
    public string RepositoryX { get; set; }
    public string NotInRepoUseConnect { get; set; }

    // main window messages
    public string PickProjectFolder { get; set; }
    public string NoProjectInFolderX { get; set; }
    public string CreateSubtitleX { get; set; }
    public string ApplyStylesNow { get; set; }
    public string NoProjectStyles { get; set; }
    public string StylesAppliedToX { get; set; }
    public string ProjectStylesTitle { get; set; }

    // Project panel
    public string ProjectIntro { get; set; }
    public string NoEpisodes { get; set; }
    public string SeasonX { get; set; }
    public string Specials { get; set; }
    public string NoVideo { get; set; }
    public string NoSubtitleYet { get; set; }
    public string OneFile { get; set; }
    public string XFiles { get; set; }
    public string OneSeason { get; set; }
    public string XSeasons { get; set; }
    public string FilesXInSeasonsY { get; set; }
    public string MoreActions { get; set; }

    // project editor
    public string Episodes { get; set; }
    public string Series { get; set; }
    public string Naming { get; set; }
    public string Folder { get; set; }
    public string Name { get; set; }
    public string Season { get; set; }
    public string EpisodeTitle { get; set; }
    public string AirDate { get; set; }
    public string Video { get; set; }
    public string Subtitle { get; set; }
    public string AddEpisodes { get; set; }
    public string Count { get; set; }
    public string EpisodesPerFile { get; set; }
    public string Remove { get; set; }
    public string AutoMap { get; set; }
    public string PickVideoFolder { get; set; }
    public string AutoMapResultXY { get; set; }
    public string Tokens { get; set; }
    public string ScopeSelected { get; set; }
    public string ScopeSeason { get; set; }
    public string ScopeProject { get; set; }
    public string Token { get; set; }
    public string Value { get; set; }
    public string AddToken { get; set; }
    public string ApplyTokens { get; set; }
    public string ImportFromVideo { get; set; }
    public string NoVideoForSelection { get; set; }
    public string FfmpegNotFound { get; set; }
    public string SeriesTitle { get; set; }
    public string Year { get; set; }
    public string TvdbId { get; set; }
    public string ImdbId { get; set; }
    public string LookUpTitles { get; set; }
    public string TitleSeparator { get; set; }
    public string TitleSeparatorHint { get; set; }
    public string ApplyToAllTitles { get; set; }
    public string Source { get; set; }
    public string Search { get; set; }
    public string Import { get; set; }
    public string ImportedX { get; set; }
    public string Preset { get; set; }
    public string Template { get; set; }
    public string MultiEpisodeStyle { get; set; }
    public string Preview { get; set; }
    public string RenameFiles { get; set; }
    public string AlsoRenameVideos { get; set; }
    public string RenameConfirmX { get; set; }
    public string NothingToRename { get; set; }
    public string RenamedX { get; set; }
    public string FolderRequired { get; set; }

    // GitHub panel
    public string BaseBranchNote { get; set; }
    public string NoPullRequestYet { get; set; }
    public string PullRequestXY { get; set; }
    public string SwitchToX { get; set; }
    public string Refresh { get; set; }
    public string PullLatest { get; set; }
    public string Changes { get; set; }
    public string NoChanges { get; set; }
    public string ChangeNew { get; set; }
    public string ChangeEdited { get; set; }
    public string ChangeDeleted { get; set; }
    public string ChangeRenamed { get; set; }
    public string CommitMessage { get; set; }
    public string NewPullRequest { get; set; }
    public string PrTitle { get; set; }
    public string PrDescription { get; set; }
    public string NewBranch { get; set; }
    public string CreatePullRequest { get; set; }
    public string PushToPullRequestX { get; set; }
    public string PullRequests { get; set; }
    public string XOpen { get; set; }
    public string NoPullRequests { get; set; }
    public string ByXFromY { get; set; }
    public string CheckedOut { get; set; }
    public string CheckOut { get; set; }
    public string Merge { get; set; }
    public string SquashAndMerge { get; set; }
    public string CreateMergeCommit { get; set; }
    public string RebaseAndMerge { get; set; }
    public string OpenOnGitHub { get; set; }
    public string CommandLog { get; set; }
    public string ConnectTitle { get; set; }
    public string GhMissingBody { get; set; }
    public string SignInTitle { get; set; }
    public string SignInBody { get; set; }
    public string CheckAgain { get; set; }
    public string NotRepoTitle { get; set; }
    public string NotRepoBodyX { get; set; }
    public string CloneRepository { get; set; }
    public string CloneInto { get; set; }
    public string Clone { get; set; }
    public string CloneFolderMustBeEmpty { get; set; }
    public string ClonedX { get; set; }
    public string NothingOpenTitle { get; set; }
    public string NothingOpenBody { get; set; }
    public string ConfirmMergeXYZ { get; set; }
    public string PrCreatedX { get; set; }
    public string PushedToX { get; set; }
    public string GitError { get; set; }

    // optional project ↔ GitHub link (import / connect)
    public string ImportFromGitHub { get; set; }
    public string ConnectToGitHub { get; set; }
    public string ImportTitle { get; set; }
    public string ImportIntro { get; set; }
    public string ConnectTitleX { get; set; }
    public string ConnectIntro { get; set; }
    public string LocalCopy { get; set; }
    public string WillBeClonedHere { get; set; }
    public string ExistingCopyX { get; set; }
    public string FolderNotEmptyNotRepo { get; set; }
    public string GetRepository { get; set; }
    public string SeriesFolder { get; set; }
    public string NoSeriesFound { get; set; }
    public string UseLocalFiles { get; set; }
    public string CreateProject { get; set; }
    public string Connect { get; set; }
    public string ConnectResultXYZ { get; set; }
    public string ConnectedX { get; set; }

    public LanguageProject()
    {
        Project = "_Project";
        NewProject = "_New project...";
        OpenProject = "_Open project...";
        EditProject = "_Edit project...";
        CloseProject = "_Close project";
        NextEpisode = "Next episode";
        PreviousEpisode = "Previous episode";
        EditProjectStyles = "Edit project _styles...";
        ApplyProjectStyles = "_Apply project styles to all episodes";
        Episode = "Episode";
        GitHub = "_GitHub...";
        UseGitHub = "Use GitHub with this project";
        UseGitHubHint = "Adds GitHub to the Project menu and opens the GitHub panel beside the Project panel.";
        RepositoryX = "Repository: {0}";
        NotInRepoUseConnect = "This folder isn't in a git repository. Use Project → Connect to GitHub to link it to one.";

        PickProjectFolder = "Choose project folder";
        NoProjectInFolderX = "No project found in {0}." + Environment.NewLine + Environment.NewLine + "Create a new project there?";
        CreateSubtitleX = "This episode has no subtitle file yet." + Environment.NewLine + Environment.NewLine + "Create {0}?";
        ApplyStylesNow = "Apply the project styles to all episodes now?";
        NoProjectStyles = "The project has no styles yet. Use \"Edit project styles\" first.";
        StylesAppliedToX = "Project styles applied to {0} file(s).";
        ProjectStylesTitle = "Project styles";

        ProjectIntro = "A project keeps a series together: each episode's video and subtitle file, shared styles and file naming.";
        NoEpisodes = "This project has no episodes yet. Add them in the project editor.";
        SeasonX = "Season {0}";
        Specials = "Specials";
        NoVideo = "No video";
        NoSubtitleYet = "No subtitle yet";
        OneFile = "1 file";
        XFiles = "{0} files";
        OneSeason = "1 season";
        XSeasons = "{0} seasons";
        FilesXInSeasonsY = "{0} in {1}";
        MoreActions = "More project actions";

        Episodes = "Episodes";
        Series = "Series";
        Naming = "Naming";
        Folder = "Folder";
        Name = "Name";
        Season = "Season";
        EpisodeTitle = "Title";
        AirDate = "Air date";
        Video = "Video";
        Subtitle = "Subtitle";
        AddEpisodes = "Add";
        Count = "Count";
        EpisodesPerFile = "Episodes per file";
        Remove = "Remove";
        AutoMap = "Auto-map from folders...";
        PickVideoFolder = "Choose video folder (cancel to skip videos)";
        AutoMapResultXY = "Mapped {0} subtitle file(s) and {1} video file(s).";
        Tokens = "Naming tokens";
        ScopeSelected = "Selected episodes";
        ScopeSeason = "Their season(s)";
        ScopeProject = "Whole project";
        Token = "Token";
        Value = "Value";
        AddToken = "Add token";
        ApplyTokens = "Apply tokens";
        ImportFromVideo = "Import from video";
        NoVideoForSelection = "The selected episode has no video file.";
        FfmpegNotFound = "ffmpeg was not found. Set it up via Video → Open video first.";
        SeriesTitle = "Series title";
        Year = "Year";
        TvdbId = "TVDB id";
        ImdbId = "IMDb id";
        LookUpTitles = "Look up episode titles";
        TitleSeparator = "Title word separator";
        TitleSeparatorHint = "Spaces and the + between joined titles become this, e.g. Meow.Escape.Hey! Used when titles are imported; Apply to all titles switches the ones already here. File names still follow the naming template. Empty = plain titles.";
        ApplyToAllTitles = "Apply to all titles";
        Source = "Source";
        Search = "Search";
        Import = "Import";
        ImportedX = "Imported {0} episode(s).";
        Preset = "Preset";
        Template = "Template";
        MultiEpisodeStyle = "Multi-episode style";
        Preview = "Preview";
        RenameFiles = "Rename files...";
        AlsoRenameVideos = "Also rename videos";
        RenameConfirmX = "Rename these file(s)?" + Environment.NewLine + Environment.NewLine + "{0}";
        NothingToRename = "All files already match the naming template.";
        RenamedX = "Renamed {0} file(s).";
        FolderRequired = "Choose a project folder.";

        BaseBranchNote = "Base branch";
        NoPullRequestYet = "No pull request for this branch yet";
        PullRequestXY = "Pull request #{0}: {1}";
        SwitchToX = "Switch to {0}";
        Refresh = "Refresh";
        PullLatest = "Pull latest changes";
        Changes = "Changes";
        NoChanges = "No changes in this folder.";
        ChangeNew = "new";
        ChangeEdited = "edited";
        ChangeDeleted = "deleted";
        ChangeRenamed = "renamed";
        CommitMessage = "Commit message";
        NewPullRequest = "New pull request";
        PrTitle = "Title";
        PrDescription = "Description";
        NewBranch = "Branch name";
        CreatePullRequest = "Create pull request";
        PushToPullRequestX = "Push to pull request #{0}";
        PullRequests = "Pull requests";
        XOpen = "{0} open";
        NoPullRequests = "No open pull requests.";
        ByXFromY = "by {0} from {1}";
        CheckedOut = "checked out";
        CheckOut = "Check out";
        Merge = "Merge";
        SquashAndMerge = "Squash and merge";
        CreateMergeCommit = "Create a merge commit";
        RebaseAndMerge = "Rebase and merge";
        OpenOnGitHub = "Open on GitHub";
        CommandLog = "Command log";
        ConnectTitle = "Connect to GitHub";
        GhMissingBody = "Install the GitHub CLI and sign in. Run these in a terminal, then check again:";
        SignInTitle = "Sign in to GitHub";
        SignInBody = "The GitHub CLI is installed but not signed in. Run this in a terminal, then check again:";
        CheckAgain = "Check again";
        NotRepoTitle = "Not in a git repository";
        NotRepoBodyX = "{0} isn't inside a git repository. Clone one to work with pull requests.";
        CloneRepository = "Repository (owner/name)";
        CloneInto = "Into folder";
        Clone = "Clone";
        CloneFolderMustBeEmpty = "Pick an empty folder to clone into.";
        ClonedX = "Cloned into {0}.";
        NothingOpenTitle = "Nothing open";
        NothingOpenBody = "Open a subtitle file or a project that lives in a git repository.";
        ConfirmMergeXYZ = "Merge pull request #{0} \"{1}\" into {2}?";
        PrCreatedX = "Pull request created:" + Environment.NewLine + "{0}";
        PushedToX = "Pushed to {0}.";
        GitError = "Git error";

        ImportFromGitHub = "_Import from GitHub...";
        ConnectToGitHub = "Co_nnect to GitHub...";
        ImportTitle = "Import a project from GitHub";
        ImportIntro = "Get a copy of a repository and turn one of its series folders into a project. Episodes are mapped from the file names.";
        ConnectTitleX = "Connect \"{0}\" to GitHub";
        ConnectIntro = "Get a copy of the repository and move this project onto it. Each episode is matched to the repository file with the same season and episode. Your current folder is left as it is.";
        LocalCopy = "Local copy";
        WillBeClonedHere = "The repository will be cloned here.";
        ExistingCopyX = "Existing copy of {0}. It will be used as it is.";
        FolderNotEmptyNotRepo = "This folder isn't empty and isn't a copy of a repository. Pick an empty folder or an existing copy.";
        GetRepository = "Get repository";
        SeriesFolder = "Series folder";
        NoSeriesFound = "No numbered episode files were found in this repository.";
        UseLocalFiles = "Copy my subtitle files over the repository versions (they show up as changes)";
        CreateProject = "Create project";
        Connect = "Connect";
        ConnectResultXYZ = "Matched {0} episode(s) to repository files, copied {1} of your files over them and added {2} new file(s).";
        ConnectedX = "This project is now connected to {0}.";
    }
}
