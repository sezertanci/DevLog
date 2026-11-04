using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DevLog;

public enum OwnerType
{
    Todo,
    Work
}

/// Ekran goruntuleri diskte, meta verisi veritabaninda tutulur.
/// Veritabanina TAM YOL yazilmaz; yalnizca dosya adi tutulur, yol buradan uretilir.
/// Boylece klasor tasindiginda veya DEVLOG_DB baska yeri gosterdiginde kayitlar kirilmaz.
public static class Attachments
{
    public const long MaxBytes = 10 * 1024 * 1024;

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp"
    };

    public static string Folder(OwnerType type) => type == OwnerType.Todo ? "todo" : "work";
    public static string Prefix(OwnerType type) => type == OwnerType.Todo ? "T" : "W";

    public static OwnerType ParseType(string value) => value.ToLowerInvariant() switch
    {
        "todo" => OwnerType.Todo,
        "work" => OwnerType.Work,
        _ => throw new CliException($"Gecersiz kayit turu: {value}")
    };

    /// "T-18" veya "W-3" gibi bir referansi turu ve id'sine ayirir.
    public static (OwnerType Tip, long Id) ParseOwner(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new CliException("Kayit belirtilmedi (ornek: T-18 veya W-3).");

        var trimmed = reference.Trim();
        var type = trimmed.StartsWith("W", StringComparison.OrdinalIgnoreCase) ? OwnerType.Work : OwnerType.Todo;
        return (type, Ids.Parse(trimmed, Prefix(type)));
    }

    public static string RootDir
        => Path.Combine(Path.GetDirectoryName(Db.DbPath) ?? ".", "attachments");

    public static string OwnerDir(OwnerType type, long ownerId)
        => Path.Combine(RootDir, Folder(type), $"{Prefix(type)}-{ownerId}");

    public static string FilePath(OwnerType type, long ownerId, string fileName)
        => Path.Combine(OwnerDir(type, ownerId), fileName);

    public static long Add(SqliteConnection db, OwnerType type, long ownerId, byte[] data, string? originalName, string? caption)
    {
        if (data.Length == 0) throw new CliException("Bos dosya.");
        if (data.Length > MaxBytes)
            throw new CliException($"Dosya cok buyuk ({data.Length / 1024 / 1024} MB). Sinir: {MaxBytes / 1024 / 1024} MB.");

        var extension = Path.GetExtension(originalName ?? "").ToLowerInvariant();
        if (!Types.TryGetValue(extension, out var mime))
            throw new CliException($"Desteklenmeyen dosya turu: '{extension}'. Izin verilenler: {string.Join(", ", Types.Keys)}");

        // Diskteki ad her zaman yeniden uretilir: cakismayi ve dizin gecisi riskini keser.
        var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{RandomSuffix()}{extension}";
        var dir = OwnerDir(type, ownerId);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, fileName), data);

        var order = Convert.ToInt64(Db.Scalar(db,
            "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM attachments WHERE owner_type = $type AND owner_id = $id;",
            ("$type", Folder(type)), ("$id", ownerId)) ?? 0L);

        Db.Exec(db, @"
INSERT INTO attachments (owner_type, owner_id, file_name, original_name, mime, size_bytes, caption, sort_order, created_at)
VALUES ($type, $id, $file, $original, $mime, $size, $caption, $sort, $createdAt);",
            ("$type", Folder(type)),
            ("$id", ownerId),
            ("$file", fileName),
            ("$original", originalName),
            ("$mime", mime),
            ("$size", data.LongLength),
            ("$caption", caption),
            ("$sort", order),
            ("$createdAt", Clock.NowIso()));

        return Convert.ToInt64(Db.Scalar(db, "SELECT last_insert_rowid();"));
    }

    static string RandomSuffix()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();

    public static List<Row> List(SqliteConnection db, OwnerType type, long ownerId)
        => Db.Query(db, @"
SELECT * FROM attachments
 WHERE owner_type = $type AND owner_id = $id
 ORDER BY sort_order, id;",
            ("$type", Folder(type)), ("$id", ownerId));

    public static Row? Get(SqliteConnection db, long id)
        => Db.QueryOne(db, "SELECT * FROM attachments WHERE id = $id;", ("$id", id));

    public static void Delete(SqliteConnection db, Row row)
    {
        var type = ParseType(row.Str("owner_type"));
        DeleteFile(FilePath(type, row.Num("owner_id"), row.Str("file_name")));
        Db.Exec(db, "DELETE FROM attachments WHERE id = $id;", ("$id", row.Num("id")));
    }

    /// Bir todo veya worklog kaydi silindiginde eklerini de temizler.
    public static int DeleteForOwner(SqliteConnection db, OwnerType type, long ownerId)
    {
        var rows = List(db, type, ownerId);
        if (rows.Count == 0) return 0;

        foreach (var row in rows)
            DeleteFile(FilePath(type, ownerId, row.Str("file_name")));

        var dir = OwnerDir(type, ownerId);
        if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
            Directory.Delete(dir);

        Db.Exec(db, "DELETE FROM attachments WHERE owner_type = $type AND owner_id = $id;",
            ("$type", Folder(type)), ("$id", ownerId));

        return rows.Count;
    }

    static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Dosya kilitliyse kayit yine de silinir; yetim dosya birakmak,
            // kullaniciyi hata ile durdurmaktan iyidir.
        }
    }

    public static object Dto(Row r) => new
    {
        id = $"A-{r.Num("id")}",
        ownerType = r.Str("owner_type"),
        ownerId = r.Num("owner_id"),
        originalName = r.Str("original_name"),
        mime = r.Str("mime"),
        sizeBytes = r.Num("size_bytes"),
        caption = r.Str("caption"),
        createdAt = r.Str("created_at")
    };
}
