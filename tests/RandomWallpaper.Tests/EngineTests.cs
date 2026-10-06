using Kikicast.ExtensionSdk;
using Xunit;

namespace RandomWallpaper.Tests;
public partial class EngineTests
{
    [Fact] public void FullRoundAndBoundaryNeverRepeat()
    {
        ShuffleState? bag = null; var previous = ""; var round = new HashSet<string>();
        for (var i = 0; i < 12; i++) { var picked = ShuffleBag.Select(["a", "b", "c"], bag, new HashSet<string> { previous }, _ => { }); Assert.NotEqual(previous, picked.Path); if (i % 3 == 0) round.Clear(); Assert.True(round.Add(picked.Path!)); previous = picked.Path!; bag = picked.State; }
    }
    [Fact] public void SingleCurrentAndDeletedCandidatesDoNotReappear()
    {
        Assert.Null(ShuffleBag.Select(["a"], new(["a"], []), new HashSet<string> { "a" }, _ => { }).Path);
        Assert.Equal("b", ShuffleBag.Select(["b", "c"], new(["a", "b"], ["a", "b"]), new HashSet<string>(), _ => { }).Path);
    }
    private sealed class Fake : IWallpaper
    {
        public readonly List<string> Calls = [];
        public readonly List<string> Inspections = [];
        public readonly HashSet<string> Rejected = [];
        public readonly Dictionary<string, ImageIdentity> Identities = [];
        public bool Desktop = true, Lock = true, Recycle = true, Current = true, ReplacementVerified = true;
        private int verifications;
        public ImageIdentity Inspect(string path)
        { Inspections.Add(path); if (Rejected.Contains(path)) throw new NotSupportedException("Generated decoder refusal"); return Identities.GetValueOrDefault(path) ?? new(path, 1, 2, path, path, path); }
        public bool VerifySystemImage(string pixels, bool desktop, bool lockScreen) => ++verifications % 2 == 1 ? Current : ReplacementVerified;
        public Task<bool> ApplyDesktopAsync(string path) { Calls.Add("desktop:" + path); return Task.FromResult(Desktop); }
        public Task<bool> ApplyLockScreenAsync(string path) { Calls.Add("lock:" + path); return Task.FromResult(Lock); }
        public Task<bool> RecycleAsync(string path) { Calls.Add("recycle:" + path); return Task.FromResult(Recycle); }
    }
    private async Task WithFixture(Func<Request, string, Fake, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastWallpaperPure-" + Guid.NewGuid().ToString("N")); var data = Path.Combine(root, "data"); var images = Path.Combine(root, "images"); Directory.CreateDirectory(data); Directory.CreateDirectory(images);
        try
        { File.WriteAllText(Path.Combine(images, "a.jpg"), "generated fixture, never decoded or applied"); File.WriteAllText(Path.Combine(images, "b.jpg"), "generated fixture, never decoded or applied"); var id = Guid.NewGuid(); var request = new Request(1, "random-wallpaper", "next", id, new(true, new() { ["desktop"] = true, ["lock-screen"] = true }, [new(id, "Owned", images)]), data, false); await test(request, Path.Combine(data, "state.json"), new()); }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task ConfirmationAndOutsideChangeBlockBeforeAnyNativeWrite() => await WithFixture(async (r, state, fake) =>
    { var engine = new WallpaperEngine(fake); Assert.False((await engine.ExecuteAsync(r with { CommandId = "delete-now" })).Success); Assert.Empty(fake.Calls); Assert.True((await engine.ExecuteAsync(r)).Success); fake.Calls.Clear(); fake.Current = false; Assert.False((await engine.ExecuteAsync(r with { CommandId = "delete-now", Confirmed = true })).Success); Assert.Empty(fake.Calls); });
    [Fact] public async Task DeleteReplacesBothThenRecyclesAndPersistsNoRepeat() => await WithFixture(async (r, state, fake) =>
    { var engine = new WallpaperEngine(fake); await engine.ExecuteAsync(r); var before = System.Text.Json.JsonSerializer.Deserialize<WallpaperState>(File.ReadAllText(state), Protocol.Json)!; fake.Calls.Clear(); Assert.True((await engine.ExecuteAsync(r with { CommandId = "delete-now", Confirmed = true })).Success); Assert.Equal(3, fake.Calls.Count); Assert.StartsWith("desktop:", fake.Calls[0]); Assert.StartsWith("lock:", fake.Calls[1]); Assert.Equal("recycle:" + before.Current!.Path, fake.Calls[2]); });
    [Fact] public async Task PartialReplacementNeverRecyclesPriorSource() => await WithFixture(async (r, state, fake) =>
    { var engine = new WallpaperEngine(fake); await engine.ExecuteAsync(r); fake.Calls.Clear(); fake.Lock = false; Assert.False((await engine.ExecuteAsync(r with { CommandId = "delete-now", Confirmed = true })).Success); Assert.DoesNotContain(fake.Calls, x => x.StartsWith("recycle:")); Assert.True(File.Exists(state)); });
    [Fact] public async Task UnverifiedReplacementAndRecycleFailureAreReportedWithoutRollbackOrPermanentDelete() => await WithFixture(async (r, state, fake) =>
    { var engine = new WallpaperEngine(fake); await engine.ExecuteAsync(r); fake.Calls.Clear(); fake.ReplacementVerified = false; Assert.False((await engine.ExecuteAsync(r with { CommandId = "delete-now", Confirmed = true })).Success); Assert.DoesNotContain(fake.Calls, x => x.StartsWith("recycle:")); Assert.Equal(2, fake.Calls.Count); });
    [Fact] public async Task LockedReservationAndPendingStateNeverStartNativeWork() => await WithFixture(async (r, state, fake) =>
    { LocalFiles.Save(state, new WallpaperState()); using (var locked = new FileStream(state, FileMode.Open, FileAccess.Read, FileShare.Read)) { var error = await Record.ExceptionAsync(() => new WallpaperEngine(fake).ExecuteAsync(r)); Assert.True(error is IOException or UnauthorizedAccessException); } Assert.Empty(fake.Calls); LocalFiles.Save(state, new WallpaperState { Pending = fake.Inspect(Path.Combine(r.Configuration.Folders[0].Path, "a.jpg")) }); Assert.False((await new WallpaperEngine(fake).ExecuteAsync(r)).Success); Assert.Empty(fake.Calls); });
}
