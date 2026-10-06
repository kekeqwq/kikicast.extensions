using System.IO;
using System.Text.Json;
using Kikicast.ExtensionSdk;

namespace RandomWallpaper;

public sealed record ImageIdentity(string Path, long Length, long LastWriteTicks, string FileId, string Sha256, string Pixels);
public sealed record WallpaperState
{
    public int Version { get; init; } = 1;
    public Dictionary<Guid, ShuffleState> Bags { get; init; } = [];
    public ImageIdentity? Current { get; init; }
    public Guid? CurrentFolderId { get; init; }
    public string? DesktopSource { get; init; }
    public string? LockSource { get; init; }
    public ImageIdentity? Pending { get; init; }
    public string LastResult { get; init; } = "No wallpaper operation has run.";
}
public interface IWallpaper
{
    ImageIdentity Inspect(string path);
    bool VerifySystemImage(string pixels, bool desktop, bool lockScreen);
    Task<bool> ApplyDesktopAsync(string path);
    Task<bool> ApplyLockScreenAsync(string path);
    Task<bool> RecycleAsync(string path);
}
public sealed class WallpaperEngine(IWallpaper windows)
{
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
    public async Task<Reply> ExecuteAsync(Request request)
    {
        if (request.CommandId is not ("next" or "delete-now") || request.CommandId == "delete-now" && !request.Confirmed) return new(false, "DeleteNow requires fresh default-No confirmation through Kikicast.");
        var config = request.Configuration;
        if (config.Folders.Count is < 1 or > 32 || config.Folders.Any(x => x.Id == Guid.Empty) || config.Folders.Select(x => x.Id).Distinct().Count() != config.Folders.Count
            || !config.Options.TryGetValue("desktop", out var desktop) || !config.Options.TryGetValue("lock-screen", out var lockScreen) || !desktop && !lockScreen) return new(false, "Choose at least one wallpaper target and a configured folder.");
        var path = Path.Combine(request.DataDirectory, "state.json");
        WallpaperState state;
        if (File.Exists(path))
        {
            if (!LocalFiles.SafeFile(path) || new FileInfo(path).Length > 1024 * 1024) return new(false, "Invalid state retained; no operation ran.");
            state = JsonSerializer.Deserialize<WallpaperState>(File.ReadAllText(path), Protocol.Json) ?? throw new InvalidDataException();
            if (state.Version != 1 || state.Bags == null || state.Bags.Count > 32 || state.Bags.Values.Any(x => x.Known.Count > 8192 || x.Remaining.Count > 8192)) return new(false, "Unsupported/oversized state retained.");
        }
        else state = new();
        // A prior interrupted/unsaved native result cannot authorize deleting an image.
        if (state.Pending != null) return new(false, "An interrupted native operation is pending. No source is recycled; inspect/reset extension state manually.");
        var deleting = request.CommandId == "delete-now";
        var old = state.Current;
        if (deleting && (old == null || !SameIdentity(windows.Inspect(old.Path), old) || !windows.VerifySystemImage(old.Pixels, state.DesktopSource == old.Path, state.LockSource == old.Path)))
            return new(false, "Current source/Windows wallpaper no longer matches the recorded image; not changed or recycled.");
        var folder = deleting ? config.Folders.FirstOrDefault(f => f.Enabled && f.Id == state.CurrentFolderId && LocalFiles.SafeDirectory(LocalFiles.Expand(f.Path)) && old != null && Path.GetDirectoryName(old.Path)!.Equals(Path.GetFullPath(LocalFiles.Expand(f.Path)), StringComparison.OrdinalIgnoreCase))
            : config.Folders.SingleOrDefault(f => f.Id == request.FolderId && f.Enabled);
        if (folder == null) return new(false, "Current folder is unavailable/disabled or its definition changed; not changed or recycled.");
        if (!LocalFiles.SafeDirectory(LocalFiles.Expand(folder.Path))) return new(false, "Choose an existing fixed-drive local folder without linked/remote ancestors.");
        // Only DeleteNow may widen its search. Prefer the current UUID, then enabled
        // configured folders in saved order; ordinary folder commands never fall back.
        var searchFolders = deleting ? new[] { folder }.Concat(config.Folders.Where(f => f.Enabled && f.Id != folder.Id)).ToArray() : [folder];
        var excluded = new[] { state.Current?.Path, state.DesktopSource, state.LockSource }.Where(x => x != null).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ImageIdentity? selected = null; Selection selection = new(null, new([], []));
        var inspected = 0; var candidateCount = 0; var invalid = 0;
        foreach (var searchFolder in searchFolders)
        {
            var directory = LocalFiles.Expand(searchFolder.Path);
            if (!LocalFiles.SafeDirectory(directory)) continue; // unavailable/linked/remote fallback folders are never traversed
            var candidates = new List<string>();
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++inspected > 8192) return new(false, "Shared folder inspection budget exceeded; no wallpaper changed or source recycled.");
                if (Formats.Contains(Path.GetExtension(item)) && LocalFiles.SafeFile(item) && new FileInfo(item).Length <= 32L * 1024 * 1024)
                { candidates.Add(Path.GetFullPath(item)); if (++candidateCount > 2048) return new(false, "Shared image candidate budget exceeded; no wallpaper changed or source recycled."); }
            }
            var bag = state.Bags.GetValueOrDefault(searchFolder.Id);
            while (candidates.Count > 0)
            {
                selection = ShuffleBag.Select(candidates, bag, excluded, values => Random.Shared.Shuffle(values));
                if (selection.Path == null) break;
                try
                {
                    selected = windows.Inspect(selection.Path);
                    if (state.Current?.Pixels == selected.Pixels || state.Current?.FileId == selected.FileId)
                    { candidates.RemoveAll(p => p.Equals(selection.Path, StringComparison.OrdinalIgnoreCase)); selected = null; continue; }
                    break;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
                { candidates.RemoveAll(p => p.Equals(selection.Path, StringComparison.OrdinalIgnoreCase)); if (++invalid >= 64) return new(false, "Shared image decoder refusal budget exceeded; no wallpaper changed or source recycled."); }
            }
            if (selected != null) { folder = searchFolder; break; }
        }
        if (selected == null) return new(false, deleting ? "No different valid image in any enabled configured folder; current source retained." : "No different valid image in this folder; folder commands do not fall back.");
        var bags = state.Bags.Where(x => config.Folders.Any(f => f.Id == x.Key)).ToDictionary(x => x.Key, x => x.Value with { Remaining = x.Value.Remaining.Where(p => !p.Equals(selected.Path, StringComparison.OrdinalIgnoreCase)).ToList() }); bags[folder.Id] = selection.State;
        // Persist reservation before any Windows write. If this fails, no wallpaper operation starts.
        LocalFiles.Save(path, state with { Bags = bags, Pending = selected, LastResult = "Native wallpaper operation pending; do not recycle." });
        bool desktopOk = false, lockOk = false;
        if (desktop) { try { desktopOk = await windows.ApplyDesktopAsync(selected.Path); } catch { } }
        if (lockScreen) { try { lockOk = await windows.ApplyLockScreenAsync(selected.Path); } catch { } }
        var changed = desktopOk || lockOk;
        var updated = state with { Bags = changed ? bags : state.Bags, Current = changed ? selected : state.Current, CurrentFolderId = changed ? folder.Id : state.CurrentFolderId, DesktopSource = desktopOk ? selected.Path : state.DesktopSource, LockSource = lockOk ? selected.Path : state.LockSource, Pending = null,
            LastResult = $"Desktop: {(desktop ? desktopOk ? "set" : "failed" : "off")}; Lock screen: {(lockScreen ? lockOk ? "set" : "failed" : "off")}." };
        LocalFiles.Save(path, updated); // Failure leaves the on-disk pending record; never claims atomic success.
        if (!deleting) return new(changed && (!desktop || desktopOk) && (!lockScreen || lockOk), updated.LastResult);
        if (!changed || updated.DesktopSource == old!.Path || updated.LockSource == old.Path || !windows.VerifySystemImage(selected.Pixels, desktop, lockScreen) || !SameIdentity(windows.Inspect(old.Path), old))
            return new(false, updated.LastResult + " Prior source not recycled: replacement was incomplete/unverified, still referenced, or source identity changed.");
        bool recycled = false; try { recycled = await windows.RecycleAsync(old.Path); } catch { }
        updated = updated with { LastResult = updated.LastResult + (recycled ? " Prior source sent to Recycle Bin." : " Recycle failed; prior source retained, replacement remains set.") }; LocalFiles.Save(path, updated);
        return new(recycled, updated.LastResult);
    }
    private static bool SameIdentity(ImageIdentity a, ImageIdentity b) => a == b;
}
