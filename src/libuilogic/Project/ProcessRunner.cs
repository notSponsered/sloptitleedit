using System.Diagnostics;
using System.Text;

namespace Nikse.SubtitleEdit.UiLogic.Project;

public record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    /// <summary>Exit code −1 = the executable could not be started (not installed / not on PATH).</summary>
    public bool NotFound => ExitCode == -1;

    public string Message => (StdErr.Trim().Length > 0 ? StdErr : StdOut).Trim();
}

/// <summary>
/// Runs a command-line tool (git, gh, ffmpeg) without a console window and captures its output.
/// </summary>
public static class ProcessRunner
{
    // a fresh install (e.g. winget) may not be on PATH yet for an already running SE
    public static string GitExe => FindInProgramFiles(Path.Combine("Git", "cmd", "git.exe")) ?? "git";
    public static string GhExe => FindInProgramFiles(Path.Combine("GitHub CLI", "gh.exe")) ?? "gh";

    public static Task<ProcessResult> Git(string workDir, CancellationToken cancellationToken, params string[] args) =>
        Run(GitExe, workDir, cancellationToken, args);

    public static Task<ProcessResult> Gh(string workDir, CancellationToken cancellationToken, params string[] args) =>
        Run(GhExe, workDir, cancellationToken, args);

    /// <summary>Top folder of the git repository containing <paramref name="folder"/>, or null.</summary>
    public static async Task<string?> GetRepoRoot(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var r = await Git(folder, CancellationToken.None, "rev-parse", "--show-toplevel");
        return r.Ok && r.StdOut.Trim().Length > 0 ? Path.GetFullPath(r.StdOut.Trim()) : null;
    }

    /// <summary>"owner/name" of the GitHub remote of the repo containing <paramref name="folder"/>; the repo folder's name
    /// when it has no GitHub remote; null when the folder is not in a repository.</summary>
    public static async Task<string?> GetRepoName(string folder)
    {
        var root = await GetRepoRoot(folder);
        if (root == null)
        {
            return null;
        }

        var remote = await Git(root, CancellationToken.None, "remote", "get-url", "origin");
        return (remote.Ok ? ProjectRepo.ParseRepoName(remote.StdOut) : null) ?? Path.GetFileName(root);
    }

    /// <summary>Adds the project sidecar to the repo's local-only exclude file (never committed).</summary>
    public static async Task EnsureGitExclude(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var r = await Git(folder, CancellationToken.None, "rev-parse", "--git-path", "info/exclude");
        if (r.Ok && r.StdOut.Trim().Length > 0)
        {
            SeProject.EnsureExcludeLine(Path.GetFullPath(Path.Combine(folder, r.StdOut.Trim())));
        }
    }

    private static string? FindInProgramFiles(string relativePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relativePath);
        return File.Exists(path) ? path : null;
    }

    public static async Task<ProcessResult> Run(string exe, string workDir, CancellationToken cancellationToken, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // never block on an interactive prompt (stdin is closed right away)
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        startInfo.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty, $"{exe} could not be started");
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, $"{exe}: {ex.Message}");
        }

        process.StandardInput.Close();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // already exited
            }

            throw;
        }

        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }
}
