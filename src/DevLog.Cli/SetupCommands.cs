using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevLog;

/// devlog'un Claude Code entegrasyonunu kurar, durumunu gosterir ve kaldirir.
/// Mevcut ayarlari ezmez: settings.json'a yalnizca eksik hook'lari ekler,
/// CLAUDE.md'ye kendi bolumunu isaretli sinirlar arasinda yazar.
public static class SetupCommands
{
    const string StartMarker = "<!-- devlog:start -->";
    const string EndMarker = "<!-- devlog:end -->";
    const string CommandPrefix = "devlog hook";

    // PowerShell script'lerine isaret eden eski kurulumun izi; bulunursa temizlenir.
    const string LegacyMarker = "devlog-hooks";

    static readonly (string Olay, string Komut)[] Hooks =
    {
        ("SessionStart", "devlog hook session-start"),
        ("SessionEnd", "devlog hook session-end"),
        ("Stop", "devlog hook stop")
    };

    public static int Run(string cmd, Parsed p) => cmd switch
    {
        "" or "install" => Install(),
        "status" => Status(),
        "remove" => Remove(),
        _ => throw new CliException($"Bilinmeyen setup komutu: '{cmd}'. Kullanilabilir: install, status, remove")
    };

    // --- yollar ---

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string ClaudeDir => Path.Combine(Home, ".claude");
    static string SettingsPath => Path.Combine(ClaudeDir, "settings.json");
    static string RulesPath => Path.Combine(ClaudeDir, "CLAUDE.md");
    static string SkillsDir => Path.Combine(ClaudeDir, "skills");
    static string ToolsDir => Path.Combine(Home, ".dotnet", "tools");
    static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    static string ShimName => Windows ? "dl.cmd" : "dl";
    static string ShimPath => Path.Combine(ToolsDir, ShimName);

    // --- kurulum ---

    static int Install()
    {
        Directory.CreateDirectory(ClaudeDir);

        WriteSkill("todo", "skill-todo.md");
        WriteSkill("worklog", "skill-worklog.md");
        Out.Info("Skill'ler yazildi     : " + Path.Combine(SkillsDir, "todo") + ", " + Path.Combine(SkillsDir, "worklog"));

        WriteRulesSection();
        Out.Info("Kural dosyasi         : " + RulesPath);

        var added = AddHooks();
        Out.Info("Hook'lar              : " + (added == 0 ? "zaten kurulu" : $"{added} hook eklendi") + $"  ({SettingsPath})");

        WriteShim();
        Out.Info("Kisayol               : " + ShimPath);

        Out.Info("");
        WarnIfNotOnPath();
        Out.Info("Kurulum tamam. Ayarlarin gecerli olmasi icin YENI bir Claude Code oturumu acin.");
        return 0;
    }

    static void WriteSkill(string name, string resource)
    {
        var dir = Path.Combine(SkillsDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), Resource(resource), new UTF8Encoding(false));
    }

    static void WriteRulesSection()
    {
        var section = $"{StartMarker}\n{Resource("claude-md.md").TrimEnd()}\n{EndMarker}\n";

        if (!File.Exists(RulesPath))
        {
            File.WriteAllText(RulesPath, section, new UTF8Encoding(false));
            return;
        }

        var existing = File.ReadAllText(RulesPath);
        File.WriteAllText(RulesPath, ReplaceSection(existing, section), new UTF8Encoding(false));
    }

    static string ReplaceSection(string existing, string newSection)
    {
        var start = existing.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = existing.IndexOf(EndMarker, StringComparison.Ordinal);

        if (start < 0 || end < 0)
            return existing.TrimEnd() + "\n\n" + newSection;

        var before = existing[..start];
        var after = existing[(end + EndMarker.Length)..];
        return before + newSection.TrimEnd() + after;
    }

    static int AddHooks()
    {
        var root = ReadSettings();
        Backup();

        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        var added = 0;
        foreach (var (eventName, command) in Hooks)
        {
            if (hooks[eventName] is not JsonArray array)
            {
                array = new JsonArray();
                hooks[eventName] = array;
            }

            RemoveEntries(array, LegacyMarker);
            if (HasCommand(array, command)) continue;

            array.Add(new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = command,
                        ["timeout"] = 15
                    }
                }
            });
            added++;
        }

        WriteSettings(root);
        return added;
    }

    static bool HasCommand(JsonArray array, string command)
        => array.Any(girdi => girdi?.ToJsonString().Contains(command, StringComparison.Ordinal) == true);

    static void RemoveEntries(JsonArray array, string marker)
    {
        for (var i = array.Count - 1; i >= 0; i--)
            if (array[i]?.ToJsonString().Contains(marker, StringComparison.Ordinal) == true)
                array.RemoveAt(i);
    }

    static void WriteShim()
    {
        if (!Directory.Exists(ToolsDir)) return;

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(ShimPath, "@echo off\r\n\"%~dp0devlog.exe\" %*\r\n", new UTF8Encoding(false));
            return;
        }

        File.WriteAllText(ShimPath, "#!/bin/sh\nexec \"$(dirname \"$0\")/devlog\" \"$@\"\n", new UTF8Encoding(false));
        File.SetUnixFileMode(ShimPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    static void WarnIfNotOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path.Split(Path.PathSeparator).Any(p => p.TrimEnd('/', '\\').Equals(ToolsDir.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)))
            return;

        Out.Info($"UYARI: {ToolsDir} PATH'te gorunmuyor.");
        if (!Windows)
        {
            Out.Info("Kabuk profilinize su satiri ekleyin (~/.zshrc veya ~/.bashrc):");
            Out.Info("  export PATH=\"$PATH:$HOME/.dotnet/tools\"");
        }
        Out.Info("");
    }

    // --- durum ---

    static int Status()
    {
        var root = ReadSettings();
        var hooks = root["hooks"] as JsonObject;

        Out.Info($"Isletim sistemi : {(Windows ? "Windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Linux")}");
        Out.Info($"Veritabani      : {Db.DbPath}  {(File.Exists(Db.DbPath) ? "(var)" : "(henuz yok)")}");
        Out.Info("");

        foreach (var (eventName, command) in Hooks)
        {
            var array = hooks?[eventName] as JsonArray;
            StatusLine($"Hook {eventName}", array != null && HasCommand(array, command));
        }

        StatusLine("CLAUDE.md bolumu", File.Exists(RulesPath) && File.ReadAllText(RulesPath).Contains(StartMarker, StringComparison.Ordinal));
        StatusLine("Skill: todo", File.Exists(Path.Combine(SkillsDir, "todo", "SKILL.md")));
        StatusLine("Skill: worklog", File.Exists(Path.Combine(SkillsDir, "worklog", "SKILL.md")));
        StatusLine("dl kisayolu", File.Exists(ShimPath));

        Out.Info("");
        WarnIfNotOnPath();
        return 0;
    }

    static void StatusLine(string label, bool installed)
        => Out.Info($"{label.PadRight(20)} {(installed ? "kurulu" : "eksik")}");

    // --- kaldirma ---

    static int Remove()
    {
        var running = Process.GetProcessesByName("devlog").Length;
        if (running > 1)
            Out.Info($"UYARI: {running} devlog sureci calisiyor (muhtemelen 'devlog ui'). Araci kaldirmadan once kapatin.\n");

        var root = ReadSettings();
        if (root["hooks"] is JsonObject hooks)
        {
            Backup();
            foreach (var (eventName, _) in Hooks)
            {
                if (hooks[eventName] is not JsonArray array) continue;

                RemoveEntries(array, CommandPrefix);
                RemoveEntries(array, LegacyMarker);

                if (array.Count == 0) hooks.Remove(eventName);
            }

            if (hooks.Count == 0) root.Remove("hooks");
            WriteSettings(root);
            Out.Info("Hook'lar settings.json'dan cikarildi (diger ayarlariniza dokunulmadi).");
        }

        if (File.Exists(RulesPath))
        {
            var text = File.ReadAllText(RulesPath);
            var start = text.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = text.IndexOf(EndMarker, StringComparison.Ordinal);

            if (start >= 0 && end >= 0)
            {
                var remaining = (text[..start] + text[(end + EndMarker.Length)..]).Trim();
                if (remaining.Length == 0) File.Delete(RulesPath);
                else File.WriteAllText(RulesPath, remaining + "\n", new UTF8Encoding(false));
                Out.Info("CLAUDE.md devlog bolumu silindi.");
            }
        }

        foreach (var name in new[] { "todo", "worklog" })
        {
            var dir = Path.Combine(SkillsDir, name);
            if (!Directory.Exists(dir)) continue;
            Directory.Delete(dir, true);
            Out.Info($"Skill silindi: {dir}");
        }

        if (File.Exists(ShimPath))
        {
            File.Delete(ShimPath);
            Out.Info($"Kisayol silindi: {ShimPath}");
        }

        Out.Info("");
        Out.Info($"Kayitlariniz KORUNDU: {Db.DbPath}");
        Out.Info("Araci tamamen kaldirmak icin: dotnet tool uninstall --global DevLog.Cli");
        return 0;
    }

    // --- settings.json yardimcilari ---

    static JsonObject ReadSettings()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new CliException($"{SettingsPath} gecerli JSON degil, dokunulmadi: {ex.Message}");
        }
    }

    static void Backup()
    {
        if (File.Exists(SettingsPath))
            File.Copy(SettingsPath, SettingsPath + ".devlog-yedek", true);
    }

    static void WriteSettings(JsonObject root)
    {
        Directory.CreateDirectory(ClaudeDir);
        File.WriteAllText(SettingsPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    static string Resource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var fullName = asm.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
