namespace DevLog;

public static class WorkCommands
{
    public static int Run(string cmd, Parsed p) => cmd switch
    {
        "add" => Add(p),
        "list" or "ls" => List(p, DefaultFrom(p), p.Get("to")),
        "today" => List(p, Clock.Today(), Clock.Today()),
        "week" => List(p, WeekStart(), Clock.Today()),
        "show" => Show(p),
        "edit" => Edit(p),
        "rm" or "delete" => Remove(p),
        "report" => Report(p),
        _ => throw new CliException($"Bilinmeyen work komutu: '{cmd}'. Kullanilabilir: add, list, today, week, show, edit, rm, report")
    };

    static int Add(Parsed p)
    {
        var summary = p.Get("summary", "s") ?? p.Arg(2)
            ?? throw new CliException("Ozet zorunlu: devlog work add \"ne yapildi\" --minutes 45");

        var date = Dates.Parse(p.Get("date") ?? "today");
        var startedAt = Dates.Combine(date, p.Get("start"));
        var endedAt = Dates.Combine(date, p.Get("end"));
        var minutes = p.GetInt("minutes", "m") ?? Dates.MinutesBetween(startedAt, endedAt);

        if (minutes <= 0)
            throw new CliException("Sure belirtilmeli: --minutes 45 veya --start 09:30 --end 11:00");

        var git = p.Has("no-git") ? new GitContext(null, null, null) : Git.Current();

        using var db = Db.Open();
        Db.Exec(db, @"
INSERT INTO worklogs (date, started_at, ended_at, minutes, summary, detail, repo, branch,
                      work_item, session_id, linked_todos, created_at)
VALUES ($date, $startedAt, $endedAt, $minutes, $summary, $detail, $repo, $branch,
        $workItem, $session, $linkedTodos, $createdAt);",
            ("$date", date),
            ("$startedAt", startedAt),
            ("$endedAt", endedAt),
            ("$minutes", minutes),
            ("$summary", summary),
            ("$detail", p.Get("detail", "d")),
            ("$repo", p.Get("repo") == "." ? git.Repo : p.Get("repo") ?? git.Repo),
            ("$branch", p.Get("branch") == "." ? git.Branch : p.Get("branch") ?? git.Branch),
            ("$workItem", p.Get("wi", "work-item")),
            ("$session", p.Get("session")),
            ("$linkedTodos", p.Get("todos", "todo")),
            ("$createdAt", Clock.NowIso()));

        var id = Convert.ToInt64(Db.Scalar(db, "SELECT last_insert_rowid();"));

        if (p.Get("session") != null)
            Db.Exec(db, "UPDATE sessions SET logged = 1 WHERE session_id = $session;", ("$session", p.Get("session")));

        if (p.Has("json")) Out.Json(new { id = Ids.Work(id), date, minutes, summary });
        else Out.Info($"{Ids.Work(id)} kaydedildi: {date}  {Fmt.Duration(minutes)}  {summary}");
        return 0;
    }

    static string DefaultFrom(Parsed p)
        => p.Get("from") ?? DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd");

    static string WeekStart()
    {
        var today = DateTime.Now.Date;
        var diff = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-diff).ToString("yyyy-MM-dd");
    }

    static int List(Parsed p, string? from, string? to)
    {
        using var db = Db.Open();

        var where = new List<string>();
        var ps = new List<(string, object?)>();

        if (from != null) { where.Add("date >= $from"); ps.Add(("$from", Dates.Parse(from))); }
        if (to != null) { where.Add("date <= $to"); ps.Add(("$to", Dates.Parse(to))); }

        var repo = p.Get("repo");
        if (repo == ".") repo = Git.Current().Repo;
        if (repo != null) { where.Add("repo = $repo"); ps.Add(("$repo", repo)); }

        var wi = p.Get("wi", "work-item");
        if (wi != null) { where.Add("work_item = $wi"); ps.Add(("$wi", wi)); }

        var sql = "SELECT * FROM worklogs"
                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                  + " ORDER BY date DESC, id DESC"
                  + $" LIMIT {p.GetInt("limit") ?? 100} OFFSET {p.GetInt("offset") ?? 0};";

        var rows = Db.Query(db, sql, ps.ToArray());

        if (p.Has("json")) { Out.Json(rows.Select(ToDto).ToList()); return 0; }

        if (rows.Count == 0) { Out.Info("Kayit yok."); return 0; }

        Out.Table(
            new[] { "ID", "TARIH", "SAAT", "SURE", "REPO", "IS", "OZET" },
            rows.Select(r => new[]
            {
                Ids.Work(r.Num("id")),
                r.Str("date"),
                TimeRange(r),
                Fmt.Duration(r.Num("minutes")),
                Out.Truncate(r.Str("repo"), 18),
                r.Str("work_item"),
                Out.Truncate(r.Str("summary"), 70)
            }).ToList());

        var total = rows.Sum(r => r.Num("minutes"));
        Out.Info("");
        Out.Info($"Toplam: {Fmt.Duration(total)}  ({rows.Count} kayit)");
        return 0;
    }

    static string TimeRange(Row r)
    {
        var start = Fmt.Clock(r.Str("started_at"));
        var end = Fmt.Clock(r.Str("ended_at"));
        if (start == "" && end == "") return "";
        return $"{start}-{end}";
    }

    static int Edit(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "W");
        var fields = new Dictionary<string, object?>();

        void Set(string column, params string[] names)
        {
            if (!p.Provided(names, out var value)) return;
            if (value == "." && column is "repo" or "branch")
            {
                var git = Git.Current();
                value = (column == "repo" ? git.Repo : git.Branch) ?? "";
            }
            fields[column] = value;
        }

        Set("summary", "summary", "s");
        Set("detail", "detail", "d");
        Set("repo", "repo");
        Set("branch", "branch");
        Set("work_item", "wi", "work-item");
        Set("linked_todos", "todos", "todo");

        if (p.Provided(new[] { "date" }, out var date))
            fields["date"] = Dates.Parse(date);

        var newMinutes = p.GetInt("minutes", "m");
        if (newMinutes.HasValue) fields["minutes"] = newMinutes.Value;

        var day = fields.TryGetValue("date", out var d) ? d!.ToString()! : null;
        if (p.Get("start") != null || p.Get("end") != null)
        {
            using var read = Db.Open();
            var existing = Db.QueryOne(read, "SELECT date FROM worklogs WHERE id = $id;", ("$id", id))
                         ?? throw new CliException($"W-{id} bulunamadi.");
            day ??= existing.Str("date");

            if (p.Get("start") != null) fields["started_at"] = Dates.Combine(day, p.Get("start"));
            if (p.Get("end") != null) fields["ended_at"] = Dates.Combine(day, p.Get("end"));

            if (!newMinutes.HasValue
                && fields.TryGetValue("started_at", out var b) && fields.TryGetValue("ended_at", out var e))
            {
                var computed = Dates.MinutesBetween(b?.ToString(), e?.ToString());
                if (computed > 0) fields["minutes"] = computed;
            }
        }

        if (fields.Count == 0)
            throw new CliException("Guncellenecek alan verilmedi. Ornek: devlog work edit W-1 --minutes 45");

        using var db = Db.Open();
        if (Db.Update(db, "worklogs", id, fields) == 0)
            throw new CliException($"W-{id} bulunamadi.");

        if (p.Has("json")) Out.Json(new { id = Ids.Work(id), updated = fields.Keys });
        else Out.Info($"{Ids.Work(id)} guncellendi: {string.Join(", ", fields.Keys)}");
        return 0;
    }

    static int Show(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "W");
        using var db = Db.Open();
        var row = Db.QueryOne(db, "SELECT * FROM worklogs WHERE id = $id;", ("$id", id))
                  ?? throw new CliException($"W-{id} bulunamadi.");

        if (p.Has("json")) { Out.Json(ToDto(row)); return 0; }

        Out.Info($"{Ids.Work(row.Num("id"))}  {row.Str("date")}  {Fmt.Duration(row.Num("minutes"))}");
        Out.Info($"Ozet     : {row.Str("summary")}");
        if (!string.IsNullOrWhiteSpace(row.Str("detail"))) Out.Info($"Detay    : {row.Str("detail")}");
        if (!string.IsNullOrWhiteSpace(row.Str("repo"))) Out.Info($"Repo     : {row.Str("repo")}/{row.Str("branch")}");
        if (!string.IsNullOrWhiteSpace(row.Str("work_item"))) Out.Info($"Is maddesi: {row.Str("work_item")}");
        if (!string.IsNullOrWhiteSpace(row.Str("linked_todos"))) Out.Info($"Bagli is : {row.Str("linked_todos")}");
        if (!string.IsNullOrWhiteSpace(row.Str("session_id"))) Out.Info($"Oturum   : {row.Str("session_id")}");
        return 0;
    }

    static int Remove(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "W");
        using var db = Db.Open();
        var row = Db.QueryOne(db, "SELECT * FROM worklogs WHERE id = $id;", ("$id", id))
                  ?? throw new CliException($"W-{id} bulunamadi.");

        if (!p.Has("y") && !p.Has("yes"))
        {
            Out.Info($"KALICI SILINECEK: {Ids.Work(id)} {row.Str("date")} {Fmt.Duration(row.Num("minutes"))} {row.Str("summary")}");
            Out.Info($"Onaylamak icin: devlog work rm {Ids.Work(id)} -y");
            return 2;
        }

        var attachmentCount = Attachments.DeleteForOwner(db, OwnerType.Work, id);
        Db.Exec(db, "DELETE FROM worklogs WHERE id = $id;", ("$id", id));

        if (p.Has("json")) Out.Json(new { id = Ids.Work(id), deleted = true, attachments = attachmentCount });
        else Out.Info($"{Ids.Work(id)} kalici olarak silindi." + (attachmentCount > 0 ? $" ({attachmentCount} ek de silindi)" : ""));
        return 0;
    }

    static int Report(Parsed p)
    {
        var from = Dates.Parse(p.Get("from") ?? WeekStart());
        var to = Dates.Parse(p.Get("to") ?? "today");
        var groupBy = (p.Get("group-by", "by") ?? "day").ToLowerInvariant();

        var column = groupBy switch
        {
            "day" or "date" => "date",
            "repo" => "repo",
            "workitem" or "wi" or "work-item" => "work_item",
            _ => throw new CliException("group-by degeri: day | repo | workitem")
        };

        using var db = Db.Open();
        var rows = Db.Query(db, $@"
SELECT COALESCE(NULLIF({column}, ''), '(bos)') AS grp,
       SUM(minutes) AS total,
       COUNT(*)     AS adet
  FROM worklogs
 WHERE date >= $from AND date <= $to
 GROUP BY grp
 ORDER BY grp DESC;",
            ("$from", from), ("$to", to));

        var total = rows.Sum(r => r.Num("total"));

        if (p.Has("json"))
        {
            Out.Json(new
            {
                from,
                to,
                groupBy,
                totalMinutes = total,
                groups = rows.Select(r => new { key = r.Str("grp"), minutes = r.Num("total"), entries = r.Num("adet") })
            });
            return 0;
        }

        Out.Info($"Rapor: {from} - {to}   (grup: {groupBy})");
        Out.Info("");

        if (rows.Count == 0) { Out.Info("Kayit yok."); return 0; }

        Out.Table(
            new[] { groupBy.ToUpperInvariant(), "SURE", "KAYIT" },
            rows.Select(r => new[] { r.Str("grp"), Fmt.Duration(r.Num("total")), r.Num("adet").ToString() }).ToList());

        Out.Info("");
        Out.Info($"Toplam: {Fmt.Duration(total)}");
        return 0;
    }

    static object ToDto(Row r) => new
    {
        id = Ids.Work(r.Num("id")),
        date = r.Str("date"),
        startedAt = r.Str("started_at"),
        endedAt = r.Str("ended_at"),
        minutes = r.Num("minutes"),
        summary = r.Str("summary"),
        detail = r.Str("detail"),
        repo = r.Str("repo"),
        branch = r.Str("branch"),
        workItem = r.Str("work_item"),
        sessionId = r.Str("session_id"),
        linkedTodos = r.Str("linked_todos")
    };
}

public static class Dates
{
    // Yalnizca acikca tanimli bicimler kabul edilir. Serbest ayristirma (DateTime.Parse)
    // "09.10.2026" gibi girdileri sessizce MM.dd olarak okuyup yanlis tarih yaziyordu.
    static readonly string[] Formats = { "yyyy-MM-dd", "dd.MM.yyyy" };

    public static string Parse(string value)
    {
        var v = value.Trim().ToLowerInvariant();

        if (v is "today" or "bugun") return DateTime.Now.ToString("yyyy-MM-dd");
        if (v is "yesterday" or "dun") return DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");

        if (DateTime.TryParseExact(value.Trim(), Formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd");

        throw new CliException($"Gecersiz date: {value} (yyyy-MM-dd, dd.MM.yyyy, today, yesterday)");
    }

    public static string? Combine(string date, string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        if (time.Contains('T')) return time;
        if (TimeSpan.TryParse(time, out var ts))
            return DateTime.Parse(date).Add(ts).ToString("yyyy-MM-ddTHH:mm:ss");
        throw new CliException($"Gecersiz saat: {time} (HH:mm bekleniyor)");
    }

    public static int MinutesBetween(string? startedAt, string? endedAt)
    {
        if (!DateTime.TryParse(startedAt, out var s) || !DateTime.TryParse(endedAt, out var e)) return 0;
        var minutes = (int)(e - s).TotalMinutes;
        return minutes < 0 ? minutes + 24 * 60 : minutes;
    }
}
