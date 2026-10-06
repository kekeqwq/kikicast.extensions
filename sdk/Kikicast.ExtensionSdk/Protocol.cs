using System.Text.Json;

namespace Kikicast.ExtensionSdk;

public sealed record Folder(Guid Id, string Name, string Path, bool Enabled = true);
public sealed record Configuration(bool Enabled, Dictionary<string, bool> Options, List<Folder> Folders);
public sealed record Request(int Protocol, string PluginId, string CommandId, Guid? FolderId, Configuration Configuration, string DataDirectory, bool Confirmed);
public sealed record Reply(bool Success, string Summary);
public static class Protocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static async Task<int> RunAsync(string[] args, string pluginId, Func<Request, Task<Reply>> execute)
    {
        if (args.SequenceEqual(["--describe"])) { Console.WriteLine(JsonSerializer.Serialize(new { protocol = 1, pluginId, onDemand = true, powershell = false }, Json)); return 0; }
        if (!args.SequenceEqual(["--kikicast-protocol-1"])) return 2;
        try
        {
            var line = await ReadRequestAsync(); var request = JsonSerializer.Deserialize<Request>(line, Json) ?? throw new InvalidDataException();
            if (request.Protocol != 1 || request.PluginId != pluginId || !request.Configuration.Enabled || !LocalFiles.SafeDirectory(request.DataDirectory)) throw new InvalidDataException();
            Console.WriteLine(JsonSerializer.Serialize(await execute(request), Json)); return 0;
        }
        catch { Console.WriteLine(JsonSerializer.Serialize(new Reply(false, "Extension request failed. Native operations are not atomic; inspect extension state before retrying."), Json)); return 0; }
    }
    private static async Task<string> ReadRequestAsync()
    {
        var text = new System.Text.StringBuilder(); var buffer = new char[1];
        while (await Console.In.ReadAsync(buffer) != 0) { if (buffer[0] == '\n') break; if (text.Length >= 65536) throw new InvalidDataException(); text.Append(buffer[0]); }
        return text.ToString();
    }
}
public static class LocalFiles
{
    public static string Expand(string path) => path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path;
    public static bool SafeDirectory(string path) => Inspect(path, out var attributes) && attributes.HasFlag(FileAttributes.Directory);
    public static bool SafeFile(string path) => Inspect(path, out var attributes) && !attributes.HasFlag(FileAttributes.Directory);
    public static bool Inspect(string path, out FileAttributes attributes)
    {
        attributes = default;
        try
        {
            if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\") || path.StartsWith("//")) return false;
            var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full)!;
            if (new DriveInfo(root).DriveType != DriveType.Fixed) return false;
            var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 128 || parts.Any(x => x is "." or ".." || x.Contains(':'))) return false;
            var current = root; attributes = File.GetAttributes(current); if (attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            foreach (var part in parts) { current = Path.Combine(current, part); attributes = File.GetAttributes(current); if (attributes.HasFlag(FileAttributes.ReparsePoint)) return false; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    public static void Save<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Protocol.Json); if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Extension state byte budget exceeded."); stream.Write(bytes); stream.Flush(true); } File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
