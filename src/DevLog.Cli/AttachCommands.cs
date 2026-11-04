namespace DevLog;

public static class AttachCommands
{
    public static int Run(string cmd, Parsed p) => cmd switch
    {
        "add" => Add(p),
        "list" or "ls" => List(p),
        "rm" or "delete" => Remove(p),
        _ => throw new CliException($"Bilinmeyen attach komutu: '{cmd}'. Kullanilabilir: add, list, rm")
    };

    static int Add(Parsed p)
    {
        var (type, ownerId) = Attachments.ParseOwner(p.Arg(2));
        var path = p.Get("file") ?? p.Arg(3)
            ?? throw new CliException("Dosya belirtilmedi: devlog attach add T-18 ekran.png");

        if (!File.Exists(path)) throw new CliException($"Dosya bulunamadi: {path}");

        using var db = Db.Open();
        EnsureOwnerExists(db, type, ownerId);

        var id = Attachments.Add(db, type, ownerId,
            File.ReadAllBytes(path), Path.GetFileName(path), p.Get("caption", "desc"));

        if (p.Has("json")) Out.Json(new { id = $"A-{id}", owner = $"{Attachments.Prefix(type)}-{ownerId}" });
        else Out.Info($"A-{id} eklendi: {Path.GetFileName(path)} -> {Attachments.Prefix(type)}-{ownerId}");
        return 0;
    }

    static int List(Parsed p)
    {
        var (type, ownerId) = Attachments.ParseOwner(p.Arg(2));

        using var db = Db.Open();
        var rows = Attachments.List(db, type, ownerId);

        if (p.Has("json")) { Out.Json(rows.Select(Attachments.Dto).ToList()); return 0; }

        if (rows.Count == 0) { Out.Info("Ek yok."); return 0; }

        Out.Table(
            new[] { "ID", "DOSYA", "BOYUT", "ACIKLAMA", "TARIH" },
            rows.Select(r => new[]
            {
                $"A-{r.Num("id")}",
                Out.Truncate(r.Str("original_name"), 32),
                $"{r.Num("size_bytes") / 1024} KB",
                Out.Truncate(r.Str("caption"), 40),
                Fmt.Date(r.Str("created_at"))
            }).ToList());

        Out.Info("");
        Out.Info($"Klasor: {Attachments.OwnerDir(type, ownerId)}");
        return 0;
    }

    static int Remove(Parsed p)
    {
        var id = Ids.Parse(p.Arg(2), "A");

        using var db = Db.Open();
        var row = Attachments.Get(db, id) ?? throw new CliException($"A-{id} bulunamadi.");

        if (!p.Has("y") && !p.Has("yes"))
        {
            Out.Info($"KALICI SILINECEK: A-{id} {row.Str("original_name")} ({row.Num("size_bytes") / 1024} KB)");
            Out.Info($"Onaylamak icin: devlog attach rm A-{id} -y");
            return 2;
        }

        Attachments.Delete(db, row);
        if (p.Has("json")) Out.Json(new { id = $"A-{id}", deleted = true });
        else Out.Info($"A-{id} silindi.");
        return 0;
    }

    static void EnsureOwnerExists(Microsoft.Data.Sqlite.SqliteConnection db, OwnerType type, long ownerId)
    {
        var table = type == OwnerType.Todo ? "todos" : "worklogs";
        if (Db.Scalar(db, $"SELECT id FROM {table} WHERE id = $id;", ("$id", ownerId)) == null)
            throw new CliException($"{Attachments.Prefix(type)}-{ownerId} bulunamadi.");
    }
}
