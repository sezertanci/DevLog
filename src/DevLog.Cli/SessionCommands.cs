using System.Text.RegularExpressions;

namespace DevLog;

public static class SessionCommands
{
    public static int Run(string cmd, Parsed p) => cmd switch
    {
        "start" => Start(p),
        "end" => End(p),
        "show" => Show(p),
        "time" => Time(p),
        _ => throw new CliException($"Bilinmeyen session komutu: '{cmd}'. Kullanilabilir: start, end, show, time")
    };

    static string RequireId(Parsed p)
        => p.Get("session", "id") ?? p.Arg(2)
           ?? throw new CliException("Oturum kimligi zorunlu: --session <id>");

    static int Start(Parsed p)
    {
        var sessionId = RequireId(p);
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
            ("$repo", p.Get("repo") ?? git.Repo),
            ("$branch", p.Get("branch") ?? git.Branch),
            ("$transcript", p.Get("transcript")),
            ("$startedAt", Clock.NowIso()));

        if (!p.Has("quiet")) Out.Info($"Oturum kaydedildi: {sessionId}");
        return 0;
    }

    static int End(Parsed p)
    {
        var sessionId = RequireId(p);
        using var db = Db.Open();
        Db.Exec(db, "UPDATE sessions SET ended_at = $endedAt WHERE session_id = $session;",
            ("$endedAt", Clock.NowIso()), ("$session", sessionId));

        if (!p.Has("quiet")) Out.Info($"Oturum kapatildi: {sessionId}");
        return 0;
    }

    /// Oturum transcript'inden aktif calisma suresini hesaplar.
    /// Ardisik mesajlar arasindaki fark esigi asarsa mola sayilir ve toplama katilmaz.
    static int Time(Parsed p)
    {
        var gap = p.GetInt("gap") ?? 15;
        var path = p.Get("transcript") ?? LatestTranscript(p.Get("project"));

        if (!File.Exists(path)) throw new CliException($"Transcript bulunamadi: {path}");

        var pattern = new Regex("\"timestamp\":\"([^\"]+)\"", RegexOptions.Compiled);
        var times = new List<DateTime>();

        foreach (var line in File.ReadLines(path))
            foreach (Match match in pattern.Matches(line))
                if (DateTimeOffset.TryParse(match.Groups[1].Value, out var moment))
                    times.Add(moment.LocalDateTime);

        if (times.Count < 2) throw new CliException("Transcript'te yeterli zaman damgasi yok.");

        times.Sort();

        var active = 0.0;
        var blocks = new List<object>();
        var blockStart = times[0];
        var previous = times[0];

        foreach (var moment in times.Skip(1))
        {
            var diff = (moment - previous).TotalMinutes;
            if (diff >= gap)
            {
                blocks.Add(Block(blockStart, previous));
                blockStart = moment;
            }
            else
            {
                active += diff;
            }
            previous = moment;
        }

        blocks.Add(Block(blockStart, previous));

        Out.Json(new
        {
            transcript = path,
            gapMinutes = gap,
            firstMessage = times[0].ToString("yyyy-MM-ddTHH:mm:ss"),
            lastMessage = previous.ToString("yyyy-MM-ddTHH:mm:ss"),
            totalMinutes = (int)Math.Round((previous - times[0]).TotalMinutes),
            activeMinutes = (int)Math.Round(active),
            messageCount = times.Count,
            blocks
        });

        return 0;
    }

    static object Block(DateTime start, DateTime end) => new
    {
        start = start.ToString("HH:mm"),
        end = end.ToString("HH:mm"),
        minutes = (int)Math.Round((end - start).TotalMinutes)
    };

    static string LatestTranscript(string? project)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(home, ".claude", "projects");
        if (!string.IsNullOrWhiteSpace(project)) root = Path.Combine(root, project);

        if (!Directory.Exists(root)) throw new CliException($"Transcript klasoru yok: {root}");

        var file = new DirectoryInfo(root)
            .GetFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new CliException($"Transcript bulunamadi: {root}");

        return file.FullName;
    }

    static int Show(Parsed p)
    {
        var sessionId = RequireId(p);
        using var db = Db.Open();
        var row = Db.QueryOne(db, "SELECT * FROM sessions WHERE session_id = $session;", ("$session", sessionId))
                  ?? throw new CliException($"Oturum bulunamadi: {sessionId}");

        var dto = new
        {
            sessionId = row.Str("session_id"),
            repo = row.Str("repo"),
            branch = row.Str("branch"),
            transcript = row.Str("transcript"),
            startedAt = row.Str("started_at"),
            endedAt = row.Str("ended_at"),
            logged = row.Num("logged") == 1
        };

        if (p.Has("json")) { Out.Json(dto); return 0; }

        Out.Info($"Oturum   : {dto.sessionId}");
        Out.Info($"Repo     : {dto.repo}/{dto.branch}");
        Out.Info($"Baslangic: {dto.startedAt}");
        if (!string.IsNullOrWhiteSpace(dto.endedAt)) Out.Info($"Bitis    : {dto.endedAt}");
        Out.Info($"Kaydedildi: {(dto.logged ? "evet" : "hayir")}");
        return 0;
    }
}
