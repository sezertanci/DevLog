using System.Text.Json;

namespace DevLog;

/// Claude Code hook'lari. Girdi stdin'den JSON olarak gelir.
/// Hicbir kosulda oturumu bozmamalari icin tum hatalar yutulur ve 0 donulur.
public static class HookCommands
{
    const int StopThresholdMinutes = 20;

    public static int Run(string cmd, Parsed p)
    {
        try
        {
            var input = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(input)) return 0;

            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;

            var sessionId = Text(root, "session_id");
            if (string.IsNullOrEmpty(sessionId)) return 0;

            var cwd = Text(root, "cwd");
            if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
                Directory.SetCurrentDirectory(cwd);

            return cmd switch
            {
                "session-start" => SessionStart(sessionId, Text(root, "transcript_path")),
                "session-end" => SessionEnd(sessionId),
                "stop" => Stop(sessionId, root),
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    static string? Text(JsonElement root, string field)
        => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static int SessionStart(string sessionId, string? transcript)
    {
        var git = Git.Current();

        using var db = Db.Open();
        Db.Exec(db, @"
INSERT INTO sessions (session_id, repo, branch, transcript, started_at)
VALUES ($session, $repo, $branch, $transcript, $startedAt)
ON CONFLICT(session_id) DO UPDATE SET
    repo       = COALESCE(excluded.repo, sessions.repo),
    branch     = COALESCE(excluded.branch, sessions.branch),
    transcript = COALESCE(excluded.transcript, sessions.transcript);",
            ("$session", sessionId),
            ("$repo", git.Repo),
            ("$branch", git.Branch),
            ("$transcript", transcript),
            ("$startedAt", Clock.NowIso()));

        if (string.IsNullOrEmpty(git.Repo)) return 0;

        var rows = Db.Query(db, @"
SELECT * FROM todos
 WHERE status = 'open' AND repo = $repo
 ORDER BY CASE priority WHEN 'high' THEN 0 WHEN 'normal' THEN 1 ELSE 2 END, id DESC
 LIMIT 10;", ("$repo", git.Repo));

        if (rows.Count == 0) return 0;

        Out.Info("Bu repo icin acik devlog maddeleri (yapilacak isler):");
        TodoCommands.PrintTable(rows);
        Out.Info("");
        Out.Info("Bir madde tamamlandiysa: devlog todo done T-<id> --by \"<commit veya PR>\"");
        return 0;
    }

    static int SessionEnd(string sessionId)
    {
        using var db = Db.Open();
        Db.Exec(db, "UPDATE sessions SET ended_at = $endedAt WHERE session_id = $session;",
            ("$endedAt", Clock.NowIso()), ("$session", sessionId));
        return 0;
    }

    /// Oturum basina en fazla bir kez konusur: oturum esigi asmissa ve
    /// calisma agacinda commit edilmemis degisiklik varsa.
    static int Stop(string sessionId, JsonElement root)
    {
        if (root.TryGetProperty("stop_hook_active", out var active) && active.ValueKind == JsonValueKind.True)
            return 0;

        var flagFile = Path.Combine(Path.GetTempPath(), $"devlog-stop-{sessionId}.flag");
        if (File.Exists(flagFile)) return 0;

        if (!Git.HasChanges()) return 0;

        using var db = Db.Open();
        var session = Db.QueryOne(db, "SELECT started_at, logged FROM sessions WHERE session_id = $session;",
            ("$session", sessionId));

        if (session == null || session.Num("logged") == 1) return 0;
        if (!DateTime.TryParse(session.Str("started_at"), out var startedAt)) return 0;
        if ((DateTime.Now - startedAt).TotalMinutes < StopThresholdMinutes) return 0;

        File.WriteAllText(flagFile, string.Empty);

        Console.Error.WriteLine(
            """
            devlog hatirlatmasi (bu oturumda yalnizca bir kez gosterilir):
            1) Bu oturumda sonraya birakilan, kapsam disi birakilan veya elle yapilmasi gereken bir is konusuldu mu?
               Varsa kullaniciya kisaca ozetle ve onay isteyerek 'devlog todo add' ile kaydet. Yoksa hicbir sey kaydetme.
            2) Kullaniciya calisma gunlugunu kaydetmek isteyip istemedigini sor (/worklog).
            Bu iki maddeyi tek ve kisa bir mesajda sor, sonra normal sekilde bitir. Tekrar sorma.
            """);

        return 2;
    }
}
