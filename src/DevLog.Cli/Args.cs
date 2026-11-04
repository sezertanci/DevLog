namespace DevLog;

public sealed class Parsed
{
    public List<string> Positional { get; } = new();
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string name) => Flags.Contains(name);
    public string? Get(params string[] names)
    {
        foreach (var n in names)
            if (Options.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return null;
    }
    public int? GetInt(params string[] names)
        => int.TryParse(Get(names), out var i) ? i : null;
    public string? Arg(int index) => Positional.Count > index ? Positional[index] : null;

    /// Verilen adlardan biri komut satirinda gecmisse true doner.
    /// Bos deger de gecerlidir: --desc "" alani temizlemek icin kullanilir.
    public bool Provided(string[] names, out string value)
    {
        foreach (var n in names)
            if (Options.TryGetValue(n, out var v)) { value = v; return true; }
        value = "";
        return false;
    }
}

public static class Args
{
    static readonly HashSet<string> BoolFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "y", "yes", "all", "quiet", "no-git", "help", "h", "open", "version"
    };

    public static Parsed Parse(IEnumerable<string> input)
    {
        var p = new Parsed();
        var list = input.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var isOption = a.StartsWith("--", StringComparison.Ordinal)
                           || (a.Length == 2 && a[0] == '-' && char.IsLetter(a[1]));
            if (!isOption) { p.Positional.Add(a); continue; }

            var key = a.TrimStart('-');
            if (BoolFlags.Contains(key)) { p.Flags.Add(key); continue; }

            var next = i + 1 < list.Count ? list[i + 1] : null;
            if (next != null && !next.StartsWith("--", StringComparison.Ordinal))
                p.Options[key] = list[++i];
            else
                p.Flags.Add(key);
        }
        return p;
    }
}
