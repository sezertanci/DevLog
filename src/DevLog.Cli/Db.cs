using Microsoft.Data.Sqlite;

namespace DevLog;

public sealed class Row : Dictionary<string, object?>
{
    public Row() : base(StringComparer.OrdinalIgnoreCase) { }

    public string Str(string key) => this.TryGetValue(key, out var v) && v != null ? v.ToString()! : "";
    public long Num(string key) => this.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : 0;
}

public static class Db
{
    public static string DbPath
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("DEVLOG_DB");
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".devlog", "devlog.db");
        }
    }

    public static SqliteConnection Open()
    {
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        Exec(conn, Schema);
        DropRemovedColumns(conn);
        return conn;
    }

    /// Kullanilmadigi icin kaldirilan kolonlari mevcut veritabanlarindan da temizler.
    /// Yeni kurulumlarda hicbir sey yapmaz.
    static void DropRemovedColumns(SqliteConnection c)
    {
        foreach (var table in new[] { "todos", "worklogs" })
        {
            var exists = Query(c, $"PRAGMA table_info({table});").Any(r => r.Str("name") == "tags");
            if (exists) Exec(c, $"ALTER TABLE {table} DROP COLUMN tags;");
        }
    }

    const string Schema = @"
PRAGMA journal_mode=WAL;

CREATE TABLE IF NOT EXISTS todos (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    title        TEXT NOT NULL,
    description  TEXT,
    suggestion   TEXT,
    repo         TEXT,
    branch       TEXT,
    commit_sha   TEXT,
    work_item    TEXT,
    files        TEXT,
    priority     TEXT NOT NULL DEFAULT 'normal',
    status       TEXT NOT NULL DEFAULT 'open',
    session_id   TEXT,
    created_at   TEXT NOT NULL,
    updated_at   TEXT,
    closed_at    TEXT,
    closed_by    TEXT
);

CREATE INDEX IF NOT EXISTS ix_todos_status ON todos(status);
CREATE INDEX IF NOT EXISTS ix_todos_repo   ON todos(repo, branch);

CREATE TABLE IF NOT EXISTS worklogs (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    date         TEXT NOT NULL,
    started_at   TEXT,
    ended_at     TEXT,
    minutes      INTEGER NOT NULL DEFAULT 0,
    summary      TEXT NOT NULL,
    detail       TEXT,
    repo         TEXT,
    branch       TEXT,
    work_item    TEXT,
    session_id   TEXT,
    linked_todos TEXT,
    created_at   TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_worklogs_date ON worklogs(date);

CREATE TABLE IF NOT EXISTS attachments (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_type    TEXT NOT NULL CHECK (owner_type IN ('todo', 'work')),
    owner_id      INTEGER NOT NULL,
    file_name     TEXT NOT NULL,
    original_name TEXT,
    mime          TEXT,
    size_bytes    INTEGER,
    caption       TEXT,
    sort_order    INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_attachments_owner ON attachments(owner_type, owner_id);

CREATE TABLE IF NOT EXISTS sessions (
    session_id   TEXT PRIMARY KEY,
    repo         TEXT,
    branch       TEXT,
    transcript   TEXT,
    started_at   TEXT NOT NULL,
    ended_at     TEXT,
    logged       INTEGER NOT NULL DEFAULT 0
);
";

    static SqliteCommand Cmd(SqliteConnection c, string sql, IEnumerable<(string, object?)>? ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (ps != null)
            foreach (var (k, v) in ps)
                cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] ps)
        => Cmd(c, sql, ps).ExecuteNonQuery();

    public static object? Scalar(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        var v = Cmd(c, sql, ps).ExecuteScalar();
        return v == DBNull.Value ? null : v;
    }

    public static List<Row> Query(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        var rows = new List<Row>();
        using var r = Cmd(c, sql, ps).ExecuteReader();
        while (r.Read())
        {
            var row = new Row();
            for (var i = 0; i < r.FieldCount; i++)
                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static Row? QueryOne(SqliteConnection c, string sql, params (string, object?)[] ps)
        => Query(c, sql, ps).FirstOrDefault();

    /// Sadece verilen alanlari gunceller. Kolon adlari cagiran taraftaki beyaz listeden gelir.
    public static int Update(SqliteConnection c, string table, long id, IDictionary<string, object?> fields)
    {
        if (fields.Count == 0) return 0;

        var sets = string.Join(", ", fields.Keys.Select(k => $"{k} = ${k}"));
        var ps = fields.Select(kv => ($"${kv.Key}", kv.Value)).ToList();
        ps.Add(("$id", (object?)id));

        return Exec(c, $"UPDATE {table} SET {sets} WHERE id = $id;", ps.ToArray());
    }
}
