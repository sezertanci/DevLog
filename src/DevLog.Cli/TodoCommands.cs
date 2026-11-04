namespace DevLog;

public static class TodoCommands
{
    public static int Run(string cmd, Parsed p) => cmd switch
    {
        "add" => Add(p),
        "list" or "ls" => List(p),
        "show" => Show(p),
        "edit" => Edit(p),
        "done" => Close(p, "done"),
        "drop" => Close(p, "dropped"),
        "rm" or "delete" => Remove(p),
        _ => throw new CliException($"Bilinmeyen todo komutu: '{cmd}'. Kullanilabilir: add, list, show, edit, done, drop, rm")
    };

    static int Add(Parsed p)
    {
        var title = p.Get("title", "t") ?? p.Arg(2)
            ?? throw new CliException("Baslik zorunlu: devlog todo add \"baslik\"");

        var git = p.Has("no-git") ? new GitContext(null, null, null) : Git.Current();
        var repo = Resolve(p.Get("repo"), git.Repo);
        var branch = Resolve(p.Get("branch"), git.Branch);
        var commit = Resolve(p.Get("commit"), git.Commit);

        using var db = Db.Open();
        Db.Exec(db, @"
INSERT INTO todos (title, description, suggestion, repo, branch, commit_sha, work_item,
                   files, priority, status, session_id, created_at)
VALUES ($title, $description, $suggestion, $repo, $branch, $commit, $workItem,
        $files, $priority, 'open', $session, $createdAt);",
            ("$title", title),
            ("$description", p.Get("desc", "description", "d")),
            ("$suggestion", p.Get("suggestion", "sug", "s")),
            ("$repo", repo),
            ("$branch", branch),
            ("$commit", commit),
            ("$workItem", p.Get("wi", "work-item")),
            ("$files", p.Get("files", "file")),
            ("$priority", p.Get("priority", "prio") ?? "normal"),
            ("$session", p.Get("session")),
            ("$createdAt", Clock.NowIso()));

        var id = Convert.ToInt64(Db.Scalar(db, "SELECT last_insert_rowid();"));

        if (p.Has("json")) Out.Json(new { id = Ids.Todo(id), title, repo, branch });
        else Out.Info($"{Ids.Todo(id)} eklendi: {title}" + (repo != null ? $"  [{repo}/{branch}]" : ""));
        return 0;
    }

    static int List(Parsed p)
    {
        using var db = Db.Open();

        var where = new List<string>();
        var ps = new List<(string, object?)>();

        var status = p.Get("status");
        if (status != null)
        {
            where.Add("status = $status");
            ps.Add(("$status", status));
        }
        else if (!p.Has("all"))
        {
            where.Add("status = 'open'");
        }

        var repo = p.Get("repo");
        if (repo == ".") repo = Git.Current().Repo;
        if (repo != null) { where.Add("repo = $repo"); ps.Add(("$repo", repo)); }

        var branch = p.Get("branch");
        if (branch == ".") branch = Git.Current().Branch;
        if (branch != null) { where.Add("branch = $branch"); ps.Add(("$branch", branch)); }

        var wi = p.Get("wi", "work-item");
        if (wi != null) { where.Add("work_item = $wi"); ps.Add(("$wi", wi)); }


        var sql = "SELECT * FROM todos"
                  + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
                  + " ORDER BY CASE priority WHEN 'high' THEN 0 WHEN 'normal' THEN 1 ELSE 2 END, id DESC"
                  + $" LIMIT {p.GetInt("limit") ?? 50} OFFSET {p.GetInt("offset") ?? 0};";

        var rows = Db.Query(db, sql, ps.ToArray());

        if (p.Has("json"))
        {
            Out.Json(rows.Select(ToDto).ToList());
            return 0;
        }

        if (rows.Count == 0)
        {
            Out.Info("Kayit yok.");
            return 0;
        }

        PrintTable(rows);
        return 0;
    }

    public static void PrintTable(IReadOnlyList<Row> rows)
        => Out.Table(
            new[] { "ID", "DURUM", "ONC", "REPO", "BRANCH", "IS", "BASLIK", "TARIH" },
            rows.Select(r => new[]
            {
                Ids.Todo(r.Num("id")),
                r.Str("status"),
                r.Str("priority"),
                Out.Truncate(r.Str("repo"), 18),
                Out.Truncate(r.Str("branch"), 22),
                r.Str("work_item"),
                Out.Truncate(r.Str("title"), 60),
                Fmt.Date(r.Str("created_at"))
            }).ToList());

    static int Edit(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "T");
        var fields = new Dictionary<string, object?>();

        void Set(string column, params string[] names)
        {
            if (!p.Provided(names, out var value)) return;
            if (value == "." && column is "repo" or "branch" or "commit_sha")
            {
                var git = Git.Current();
                value = (column switch { "repo" => git.Repo, "branch" => git.Branch, _ => git.Commit }) ?? "";
            }
            fields[column] = value;
        }

        Set("title", "title", "t");
        Set("description", "desc", "description", "d");
        Set("suggestion", "suggestion", "sug", "s");
        Set("repo", "repo");
        Set("branch", "branch");
        Set("commit_sha", "commit");
        Set("work_item", "wi", "work-item");
        Set("files", "files", "file");
        Set("priority", "priority", "prio");
        Set("status", "status");

        if (fields.Count == 0)
            throw new CliException("Guncellenecek alan verilmedi. Ornek: devlog todo edit T-3 --branch feature/20059");

        fields["updated_at"] = Clock.NowIso();

        using var db = Db.Open();
        if (Db.Update(db, "todos", id, fields) == 0)
            throw new CliException($"T-{id} bulunamadi.");

        var changed = fields.Keys.Where(k => k != "updated_at");
        if (p.Has("json")) Out.Json(new { id = Ids.Todo(id), updated = changed });
        else Out.Info($"{Ids.Todo(id)} guncellendi: {string.Join(", ", changed)}");
        return 0;
    }

    static int Show(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "T");
        using var db = Db.Open();
        var row = Db.QueryOne(db, "SELECT * FROM todos WHERE id = $id;", ("$id", id))
                  ?? throw new CliException($"T-{id} bulunamadi.");

        if (p.Has("json")) { Out.Json(ToDto(row)); return 0; }

        Out.Info($"{Ids.Todo(row.Num("id"))}  [{row.Str("status")}]  oncelik: {row.Str("priority")}");
        Out.Info($"Baslik    : {row.Str("title")}");
        Print("Aciklama  ", row.Str("description"));
        Print("Oneri     ", row.Str("suggestion"));
        Print("Repo      ", $"{row.Str("repo")}/{row.Str("branch")} {row.Str("commit_sha")}".Trim(' ', '/'));
        Print("Is maddesi", row.Str("work_item"));
        Print("Dosyalar  ", row.Str("files"));
        Out.Info($"Olusturma : {row.Str("created_at")}");
        Print("Kapanis   ", row.Str("closed_at"));
        Print("Kapatan   ", row.Str("closed_by"));
        return 0;
    }

    static void Print(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Out.Info($"{label}: {value}");
    }

    static int Close(Parsed p, string status)
    {
        var id = Ids.Parse(p.Arg(2), "T");
        using var db = Db.Open();

        var affected = Db.Exec(db, @"
UPDATE todos
   SET status = $status, closed_at = $closedAt, closed_by = $closedBy, updated_at = $closedAt
 WHERE id = $id;",
            ("$status", status),
            ("$closedAt", Clock.NowIso()),
            ("$closedBy", p.Get("by", "closed-by")),
            ("$id", id));

        if (affected == 0) throw new CliException($"T-{id} bulunamadi.");

        if (p.Has("json")) Out.Json(new { id = Ids.Todo(id), status });
        else Out.Info($"{Ids.Todo(id)} {(status == "done" ? "tamamlandi" : "vazgecildi")}.");
        return 0;
    }

    static int Remove(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "T");
        using var db = Db.Open();
        var row = Db.QueryOne(db, "SELECT * FROM todos WHERE id = $id;", ("$id", id))
                  ?? throw new CliException($"T-{id} bulunamadi.");

        if (!p.Has("y") && !p.Has("yes"))
        {
            Out.Info($"KALICI SILINECEK: {Ids.Todo(id)} [{row.Str("status")}] {row.Str("title")}");
            Out.Info($"Onaylamak icin: devlog todo rm {Ids.Todo(id)} -y");
            return 2;
        }

        var attachmentCount = Attachments.DeleteForOwner(db, OwnerType.Todo, id);
        Db.Exec(db, "DELETE FROM todos WHERE id = $id;", ("$id", id));

        if (p.Has("json")) Out.Json(new { id = Ids.Todo(id), deleted = true, attachments = attachmentCount });
        else Out.Info($"{Ids.Todo(id)} kalici olarak silindi." + (attachmentCount > 0 ? $" ({attachmentCount} ek de silindi)" : ""));
        return 0;
    }

    static object ToDto(Row r) => new
    {
        id = Ids.Todo(r.Num("id")),
        title = r.Str("title"),
        description = r.Str("description"),
        suggestion = r.Str("suggestion"),
        repo = r.Str("repo"),
        branch = r.Str("branch"),
        commit = r.Str("commit_sha"),
        workItem = r.Str("work_item"),
        files = r.Str("files"),
        priority = r.Str("priority"),
        status = r.Str("status"),
        createdAt = r.Str("created_at"),
        closedAt = r.Str("closed_at"),
        closedBy = r.Str("closed_by")
    };

    static string? Resolve(string? explicitValue, string? detected)
        => explicitValue == "." ? detected : explicitValue ?? detected;
}
