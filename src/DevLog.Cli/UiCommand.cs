using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace DevLog;

public static class UiCommand
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Run(Parsed p)
    {
        var port = p.GetInt("port") ?? 5177;
        var url = $"http://localhost:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(url);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new CliException($"{port} portu dinlenemedi ({ex.Message}). Baska bir port deneyin: devlog ui --port 5178");
        }

        Out.Info($"devlog arayuzu: {url}");
        Out.Info("Kapatmak icin Ctrl+C");

        if (!p.Has("no-browser"))
            TryOpenBrowser(url);

        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = listener.GetContext(); }
            catch { break; }

            try { Handle(ctx); }
            catch (Exception ex) { WriteJson(ctx, new { error = ex.Message }, 500); }
        }

        return 0;
    }

    static void TryOpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* tarayici acilamazsa adres yine de yazdirildi */ }
    }

    static void Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;
        var query = ctx.Request.QueryString;

        if (path is "/" or "/index.html")
        {
            WriteHtml(ctx, Page());
            return;
        }

        using var db = Db.Open();

        if (path == "/api/meta")
        {
            var repos = Db.Query(db, @"
SELECT DISTINCT repo FROM (
    SELECT repo FROM todos WHERE repo IS NOT NULL AND repo <> ''
    UNION SELECT repo FROM worklogs WHERE repo IS NOT NULL AND repo <> ''
) ORDER BY repo;").Select(r => r.Str("repo")).ToList();

            WriteJson(ctx, new { dbPath = Db.DbPath, repos });
            return;
        }

        if (path == "/api/todos" && method == "GET")
        {
            var where = new List<string>();
            var ps = new List<(string, object?)>();
            if (!string.IsNullOrEmpty(query["status"])) { where.Add("status = $status"); ps.Add(("$status", query["status"])); }
            if (!string.IsNullOrEmpty(query["repo"])) { where.Add("repo = $repo"); ps.Add(("$repo", query["repo"])); }
            if (!string.IsNullOrEmpty(query["q"]))
            {
                where.Add("(title LIKE $q OR description LIKE $q OR suggestion LIKE $q OR files LIKE $q)");
                ps.Add(("$q", "%" + query["q"] + "%"));
            }

            var whereSql = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
            var (limit, offset) = Paging(query);
            var total = Convert.ToInt64(Db.Scalar(db, "SELECT COUNT(*) FROM todos" + whereSql, ps.ToArray()) ?? 0L);

            var orderSql = OrderBy(query, TodoSortColumns,
                "CASE priority WHEN 'high' THEN 0 WHEN 'normal' THEN 1 ELSE 2 END, id DESC");

            var rows = Db.Query(db, "SELECT * FROM todos" + whereSql
                + $" ORDER BY {orderSql}"
                + $" LIMIT {limit} OFFSET {offset};",
                ps.ToArray());

            var attachments = AttachmentMap(db, OwnerType.Todo, rows.Select(r => r.Num("id")));

            WriteJson(ctx, new { total, limit, offset, rows = rows.Select(r => new
            {
                attachments = attachments.GetValueOrDefault(r.Num("id")) ?? new List<string>(),
                id = Ids.Todo(r.Num("id")),
                title = r.Str("title"),
                description = r.Str("description"),
                suggestion = r.Str("suggestion"),
                repo = r.Str("repo"),
                branch = r.Str("branch"),
                workItem = r.Str("work_item"),
                files = r.Str("files"),
                priority = r.Str("priority"),
                status = r.Str("status"),
                createdAt = r.Str("created_at"),
                closedBy = r.Str("closed_by")
            }) });
            return;
        }

        if (path == "/api/todos" && method == "POST")
        {
            var fields = ReadFields(ctx, TodoFields);
            if (Blank(fields, "title")) { WriteJson(ctx, new { error = "Başlık zorunlu." }, 400); return; }

            if (Blank(fields, "status")) fields["status"] = "open";
            if (Blank(fields, "priority")) fields["priority"] = "normal";
            fields["created_at"] = Clock.NowIso();

            WriteJson(ctx, new { id = Ids.Todo(Insert(db, "todos", fields)) });
            return;
        }

        if (path == "/api/work" && method == "POST")
        {
            var fields = ReadFields(ctx, WorkFields);
            if (Blank(fields, "summary")) { WriteJson(ctx, new { error = "Özet zorunlu." }, 400); return; }
            if (!fields.ContainsKey("minutes")) { WriteJson(ctx, new { error = "Süre zorunlu." }, 400); return; }

            if (Blank(fields, "date")) fields["date"] = Clock.Today();
            fields["created_at"] = Clock.NowIso();

            WriteJson(ctx, new { id = Ids.Work(Insert(db, "worklogs", fields)) });
            return;
        }

        if (path == "/api/attachments" && method == "GET")
        {
            var (type, ownerId) = Attachments.ParseOwner(query["owner"]);
            WriteJson(ctx, Attachments.List(db, type, ownerId).Select(Attachments.Dto));
            return;
        }

        if (path == "/api/attachments" && method == "POST")
        {
            var (type, ownerId) = Attachments.ParseOwner(query["owner"]);

            if (ctx.Request.ContentLength64 > Attachments.MaxBytes)
            {
                WriteJson(ctx, new { error = "Dosya cok buyuk (sinir 10 MB)." }, 413);
                return;
            }

            using var buffer = new MemoryStream();
            ctx.Request.InputStream.CopyTo(buffer);

            var id = Attachments.Add(db, type, ownerId, buffer.ToArray(),
                query["name"] ?? "ekran.png", query["caption"]);

            WriteJson(ctx, Attachments.Dto(Attachments.Get(db, id)!));
            return;
        }

        if (path.StartsWith("/api/attachments/") && path.EndsWith("/file") && method == "GET")
        {
            var id = Ids.Parse(path.Split('/')[3], "A");
            var row = Attachments.Get(db, id);
            if (row == null) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }

            var filePath = Attachments.FilePath(
                Attachments.ParseType(row.Str("owner_type")), row.Num("owner_id"), row.Str("file_name"));

            if (!File.Exists(filePath)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }

            var bytes = File.ReadAllBytes(filePath);
            ctx.Response.ContentType = row.Str("mime");
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
            return;
        }

        if (path.StartsWith("/api/attachments/") && method == "DELETE")
        {
            var id = Ids.Parse(path.Split('/').Last(), "A");
            var row = Attachments.Get(db, id);
            if (row != null) Attachments.Delete(db, row);
            WriteJson(ctx, new { id = $"A-{id}", deleted = row != null });
            return;
        }

        if (path.StartsWith("/api/todos/") && method == "PATCH")
        {
            var id = Ids.Parse(path.Split('/').Last(), "T");
            var fields = ReadFields(ctx, TodoFields);
            if (fields.Count == 0) { WriteJson(ctx, new { error = "guncellenecek field yok" }, 400); return; }

            fields["updated_at"] = Clock.NowIso();
            Db.Update(db, "todos", id, fields);
            WriteJson(ctx, new { id = Ids.Todo(id), updated = fields.Keys });
            return;
        }

        if (path.StartsWith("/api/todos/") && method == "POST")
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var id = Ids.Parse(parts[2], "T");
            var status = parts.Length > 3 && parts[3] == "drop" ? "dropped" : "done";

            Db.Exec(db, "UPDATE todos SET status = $status, closed_at = $now, updated_at = $now WHERE id = $id;",
                ("$status", status), ("$now", Clock.NowIso()), ("$id", id));

            WriteJson(ctx, new { id = Ids.Todo(id), status });
            return;
        }

        if (path.StartsWith("/api/todos/") && method == "DELETE")
        {
            var id = Ids.Parse(path.Split('/').Last(), "T");
            Db.Exec(db, "DELETE FROM todos WHERE id = $id;", ("$id", id));
            WriteJson(ctx, new { id = Ids.Todo(id), deleted = true });
            return;
        }

        if (path == "/api/work" && method == "GET")
        {
            var where = new List<string>();
            var ps = new List<(string, object?)>();
            if (!string.IsNullOrEmpty(query["from"])) { where.Add("date >= $from"); ps.Add(("$from", query["from"])); }
            if (!string.IsNullOrEmpty(query["to"])) { where.Add("date <= $to"); ps.Add(("$to", query["to"])); }
            if (!string.IsNullOrEmpty(query["repo"])) { where.Add("repo = $repo"); ps.Add(("$repo", query["repo"])); }
            if (!string.IsNullOrEmpty(query["q"]))
            {
                where.Add("(summary LIKE $q OR detail LIKE $q)");
                ps.Add(("$q", "%" + query["q"] + "%"));
            }

            var whereSql = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
            var (limit, offset) = Paging(query);
            var totals = Db.QueryOne(db, "SELECT COUNT(*) AS adet, COALESCE(SUM(minutes), 0) AS minutes FROM worklogs" + whereSql, ps.ToArray())!;

            var orderSql = OrderBy(query, WorkSortColumns, "date DESC, id DESC");

            var rows = Db.Query(db, "SELECT * FROM worklogs" + whereSql
                + $" ORDER BY {orderSql}"
                + $" LIMIT {limit} OFFSET {offset};", ps.ToArray());

            var attachments = AttachmentMap(db, OwnerType.Work, rows.Select(r => r.Num("id")));

            WriteJson(ctx, new
            {
                total = totals.Num("adet"),
                totalMinutes = totals.Num("minutes"),
                limit,
                offset,
                rows = rows.Select(r => new
            {
                attachments = attachments.GetValueOrDefault(r.Num("id")) ?? new List<string>(),
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
                linkedTodos = r.Str("linked_todos")
            }) });
            return;
        }

        if (path.StartsWith("/api/work/") && method == "PATCH")
        {
            var id = Ids.Parse(path.Split('/').Last(), "W");
            var fields = ReadFields(ctx, WorkFields);
            if (fields.Count == 0) { WriteJson(ctx, new { error = "guncellenecek field yok" }, 400); return; }

            Db.Update(db, "worklogs", id, fields);
            WriteJson(ctx, new { id = Ids.Work(id), updated = fields.Keys });
            return;
        }

        if (path.StartsWith("/api/work/") && method == "DELETE")
        {
            var id = Ids.Parse(path.Split('/').Last(), "W");
            Db.Exec(db, "DELETE FROM worklogs WHERE id = $id;", ("$id", id));
            WriteJson(ctx, new { id = Ids.Work(id), deleted = true });
            return;
        }

        if (path == "/api/report" && method == "GET")
        {
            var column = (query["by"] ?? "day") switch
            {
                "repo" => "repo",
                "workitem" => "work_item",
                _ => "date"
            };

            var from = string.IsNullOrEmpty(query["from"]) ? "0000-01-01" : query["from"];
            var to = string.IsNullOrEmpty(query["to"]) ? "9999-12-31" : query["to"];

            var rows = Db.Query(db, $@"
SELECT COALESCE(NULLIF({column}, ''), '(bos)') AS grp,
       SUM(minutes) AS total,
       COUNT(*)     AS adet
  FROM worklogs
 WHERE date >= $from AND date <= $to
 GROUP BY grp
 ORDER BY grp DESC;", ("$from", from), ("$to", to));

            WriteJson(ctx, new
            {
                totalMinutes = rows.Sum(r => r.Num("total")),
                groups = rows.Select(r => new { key = r.Str("grp"), minutes = r.Num("total"), entries = r.Num("adet") })
            });
            return;
        }

        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    // Istemciden gelen alan adlari ancak bu listede varsa kolona yazilir.
    static readonly Dictionary<string, string> TodoFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["title"] = "title",
        ["description"] = "description",
        ["suggestion"] = "suggestion",
        ["repo"] = "repo",
        ["branch"] = "branch",
        ["workItem"] = "work_item",
        ["files"] = "files",
        ["priority"] = "priority",
        ["status"] = "status"
    };

    static readonly Dictionary<string, string> WorkFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["summary"] = "summary",
        ["detail"] = "detail",
        ["date"] = "date",
        ["minutes"] = "minutes",
        ["repo"] = "repo",
        ["branch"] = "branch",
        ["workItem"] = "work_item",
        ["linkedTodos"] = "linked_todos"
    };

    // Istemciden gelen sira alani ancak bu listede varsa ORDER BY'a girer.
    static readonly Dictionary<string, string> TodoSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "id",
        ["status"] = "status",
        ["title"] = "title",
        ["repo"] = "repo",
        ["workItem"] = "work_item",
        ["createdAt"] = "created_at"
    };

    static readonly Dictionary<string, string> WorkSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "id",
        ["date"] = "date",
        ["minutes"] = "minutes",
        ["summary"] = "summary",
        ["repo"] = "repo",
        ["workItem"] = "work_item"
    };

    static string OrderBy(
        System.Collections.Specialized.NameValueCollection query,
        Dictionary<string, string> allowed,
        string fallback)
    {
        var field = query["sort"];
        if (string.IsNullOrEmpty(field) || !allowed.TryGetValue(field, out var column))
            return fallback;

        var direction = string.Equals(query["dir"], "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        return $"{column} {direction}, id DESC";
    }

    /// Listedeki tum kayitlarin eklerini tek sorguda ceker (satir basina sorgu atmamak icin).
    static Dictionary<long, List<string>> AttachmentMap(Microsoft.Data.Sqlite.SqliteConnection db, OwnerType type, IEnumerable<long> ownerIds)
    {
        var ids = ownerIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, List<string>>();

        var parameters = new List<(string, object?)> { ("$type", Attachments.Folder(type)) };
        var placeholders = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            placeholders.Add($"$id{i}");
            parameters.Add(($"$id{i}", ids[i]));
        }

        var rows = Db.Query(db, $@"
SELECT id, owner_id FROM attachments
 WHERE owner_type = $type AND owner_id IN ({string.Join(",", placeholders)})
 ORDER BY sort_order, id;", parameters.ToArray());

        var map = new Dictionary<long, List<string>>();
        foreach (var r in rows)
        {
            var ownerId = r.Num("owner_id");
            if (!map.TryGetValue(ownerId, out var list))
                map[ownerId] = list = new List<string>();
            list.Add($"A-{r.Num("id")}");
        }
        return map;
    }

    static bool Blank(IDictionary<string, object?> fields, string key)
        => !fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value?.ToString());

    /// Kolon adlari cagiran taraftaki beyaz listeden geldigi icin dogrudan yazilabilir.
    static long Insert(Microsoft.Data.Sqlite.SqliteConnection db, string table, IDictionary<string, object?> fields)
    {
        var columns = string.Join(", ", fields.Keys);
        var values = string.Join(", ", fields.Keys.Select(k => "$" + k));

        Db.Exec(db, $"INSERT INTO {table} ({columns}) VALUES ({values});",
            fields.Select(kv => ("$" + kv.Key, kv.Value)).ToArray());

        return Convert.ToInt64(Db.Scalar(db, "SELECT last_insert_rowid();"));
    }

    static (int limit, int offset) Paging(System.Collections.Specialized.NameValueCollection query)
    {
        var limit = int.TryParse(query["limit"], out var l) ? Math.Clamp(l, 1, 500) : 50;
        var offset = int.TryParse(query["offset"], out var o) ? Math.Max(o, 0) : 0;
        return (limit, offset);
    }

    static Dictionary<string, object?> ReadFields(HttpListenerContext ctx, Dictionary<string, string> allowed)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        var body = reader.ReadToEnd();
        var incoming = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();

        var fields = new Dictionary<string, object?>();
        foreach (var (key, value) in incoming)
        {
            if (!allowed.TryGetValue(key, out var column)) continue;

            if (column == "minutes")
            {
                var minutes = value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32()
                    : int.TryParse(value.GetString(), out var m) ? m : 0;
                if (minutes > 0) fields[column] = minutes;
                continue;
            }

            fields[column] = value.ValueKind == JsonValueKind.Null ? "" : value.GetString() ?? "";
        }
        return fields;
    }

    static string Page()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("Ui.html", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static void WriteHtml(HttpListenerContext ctx, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        // Yeni surum kurulunca tarayici eski sayfayi gostermesin.
        ctx.Response.Headers["Cache-Control"] = "no-store, must-revalidate";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    static void WriteJson(HttpListenerContext ctx, object payload, int statusCode = 200)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }
}
