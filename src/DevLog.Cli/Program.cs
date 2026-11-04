using System.Text;
using DevLog;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0) return Help.Print();

var parsed = Args.Parse(args);
var group = (parsed.Arg(0) ?? "").ToLowerInvariant();
var command = (parsed.Arg(1) ?? "").ToLowerInvariant();

if (parsed.Has("help") || parsed.Has("h") || group is "help" or "-h" or "--help")
    return Help.Print();

try
{
    switch (group)
    {
        case "todo":
            return TodoCommands.Run(command, parsed);
        case "work":
        case "log":
            return WorkCommands.Run(command, parsed);
        case "attach":
            return AttachCommands.Run(command, parsed);
        case "session":
            return SessionCommands.Run(command, parsed);
        case "ui":
            return UiCommand.Run(parsed);
        case "hook":
            return HookCommands.Run(command, parsed);
        case "setup":
            return SetupCommands.Run(command, parsed);
        case "init":
            using (Db.Open()) { }
            Console.WriteLine($"Veritabani hazir: {Db.DbPath}");
            return 0;
        default:
            Console.Error.WriteLine($"Bilinmeyen komut grubu: '{group}'. Yardim icin: devlog help");
            return 1;
    }
}
catch (CliException ex)
{
    Console.Error.WriteLine($"HATA: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"BEKLENMEYEN HATA: {ex.Message}");
    return 1;
}
