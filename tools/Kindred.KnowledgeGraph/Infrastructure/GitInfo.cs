using System.Diagnostics;

namespace Kindred.KnowledgeGraph.Infrastructure;

/// <summary>Thin git shell-outs: current commit, changed files for resync, tracked files, provenance audit.</summary>
public sealed class GitInfo(string repoRoot)
{
    public string RepoRoot => repoRoot;

    public string GetHeadCommit() => Run("rev-parse HEAD").Trim();

    /// <summary>Files changed between two commits — the resync scope.</summary>
    public IReadOnlyList<string> GetChangedFiles(string fromCommit, string toCommit = "HEAD") =>
        Run($"diff --name-only {fromCommit}..{toCommit}")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Replace('\\', '/'))
            .ToList();

    /// <summary>All tracked files (repo-relative, forward-slash) — respects .gitignore.</summary>
    public IReadOnlyList<string> ListTrackedFiles() =>
        Run("ls-files")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Commits in from..to, or null when the range can't be resolved (freshness advisory).</summary>
    public int? TryCountCommitsBetween(string fromCommit, string toCommit = "HEAD")
    {
        try
        {
            return int.Parse(Run($"rev-list --count {fromCommit}..{toCommit}").Trim());
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Did this repo-relative path exist at the given commit? (provenance audit)</summary>
    public bool FileExistedAt(string repoRelativePath, string commit)
    {
        try
        {
            Run($"cat-file -e {commit}:\"{repoRelativePath}\"");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private string Run(string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repoRoot,
            // stdin MUST be redirected, not inherited: in MCP mode this process's stdin is the
            // JSON-RPC pipe, and on Windows a git child that inherits it blocks at startup.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");
        proc.StandardInput.Close();
        // Drain both pipes concurrently: sequential ReadToEnd deadlocks if git fills stderr first.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEnd();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed ({proc.ExitCode}): {stderr.Trim()}");
        return stdout;
    }
}

public static class RepoRootLocator
{
    /// <summary>The repo root is the first directory above the cwd that holds Kindred.slnx.</summary>
    public static string Locate()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kindred.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Repo root not found (no Kindred.slnx above the current directory). Run from inside the repo.");
    }
}
