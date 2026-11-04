using System.Text.Json;

namespace DevLog;

public static class Out
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Json(object payload)
        => Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));

    public static void Info(string message) => Console.WriteLine(message);

    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return;

        var widths = new int[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            widths[i] = headers[i].Length;
            foreach (var row in rows)
                widths[i] = Math.Max(widths[i], (row[i] ?? "").Length);
        }

        Console.WriteLine(string.Join("  ", headers.Select((h, i) => h.PadRight(widths[i]))).TrimEnd());
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in rows)
            Console.WriteLine(string.Join("  ", row.Select((c, i) => (c ?? "").PadRight(widths[i]))).TrimEnd());
    }

    public static string Truncate(string? value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}

public static class Fmt
{
    public static string Duration(long minutes)
    {
        if (minutes <= 0) return "0dk";
        var h = minutes / 60;
        var m = minutes % 60;
        if (h == 0) return $"{m}dk";
        return m == 0 ? $"{h}s" : $"{h}s {m}dk";
    }

    public static string Date(string? iso)
        => string.IsNullOrWhiteSpace(iso) ? "" : (iso.Length >= 10 ? iso[..10] : iso);

    public static string Clock(string? iso)
        => DateTime.TryParse(iso, out var dt) ? dt.ToString("HH:mm") : "";
}

public static class Ids
{
    public static long Parse(string? raw, string prefix)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new CliException($"Id belirtilmedi (ornek: {prefix}-12).");

        var cleaned = raw.Trim();
        if (cleaned.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[(prefix.Length + 1)..];

        if (!long.TryParse(cleaned, out var id))
            throw new CliException($"Gecersiz id: {raw}");

        return id;
    }

    public static string Todo(long id) => $"T-{id}";
    public static string Work(long id) => $"W-{id}";
}

public sealed class CliException : Exception
{
    public CliException(string message) : base(message) { }
}

public static class Clock
{
    public static string NowIso() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd");
}
