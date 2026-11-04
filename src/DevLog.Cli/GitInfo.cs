using System.Diagnostics;

namespace DevLog;

public sealed record GitContext(string? Repo, string? Branch, string? Commit);

public static class Git
{
    static GitContext? _cached;

    public static GitContext Current()
    {
        if (_cached != null) return _cached;

        var top = Run("rev-parse --show-toplevel");
        if (string.IsNullOrWhiteSpace(top)) return _cached = new GitContext(null, null, null);

        var repo = Path.GetFileName(top.TrimEnd('/', Path.DirectorySeparatorChar));
        var branch = Run("rev-parse --abbrev-ref HEAD");
        var sha = Run("rev-parse --short HEAD");
        return _cached = new GitContext(repo, Empty(branch), Empty(sha));
    }

    /// Calisma agacinda commit edilmemis degisiklik var mi.
    public static bool HasChanges()
        => !string.IsNullOrWhiteSpace(Run("status --porcelain"));

    static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    static string? Run(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
