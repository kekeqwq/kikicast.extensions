using System.Text.Json;
using Kikicast.ExtensionSdk;
using Xunit;

namespace RandomWallpaper.Tests;
public partial class EngineTests
{
    private static WallpaperState ReadState(string path) => JsonSerializer.Deserialize<WallpaperState>(File.ReadAllText(path), Protocol.Json)!;
    private static Folder AddFolder(Request request, string name, params string[] images)
    {
        var directory = Path.Combine(Path.GetDirectoryName(request.DataDirectory)!, name); Directory.CreateDirectory(directory);
        foreach (var file in images) File.WriteAllText(Path.Combine(directory, file), "Generated fake image; never decoded/applied/recycled by Windows.");
        return new(Guid.NewGuid(), name, directory);
    }
    private static Request WithFolders(Request request, params Folder[] folders) => request with { Configuration = request.Configuration with { Folders = request.Configuration.Folders.Concat(folders).ToList() } };
    private static async Task SeedSingle(Request request, Fake fake)
    {
        File.Delete(Path.Combine(request.Configuration.Folders[0].Path, "b.jpg")); // generated marker only
        Assert.True((await new WallpaperEngine(fake).ExecuteAsync(request)).Success); fake.Calls.Clear(); fake.Inspections.Clear();
    }
    [Fact] public async Task DeleteSingleCurrentFolderFallsBackAndPersistsDestinationUuidAndBag() => await WithFixture(async (r, state, fake) =>
    {
        await SeedSingle(r, fake); var old = ReadState(state).Current!; var fallback = AddFolder(r, "other", "c.jpg"); var request = WithFolders(r, fallback);
        Assert.True((await new WallpaperEngine(fake).ExecuteAsync(request with { CommandId = "delete-now", Confirmed = true })).Success);
        var selected = Path.Combine(fallback.Path, "c.jpg"); Assert.Equal(["desktop:" + selected, "lock:" + selected, "recycle:" + old.Path], fake.Calls);
        var saved = ReadState(state); Assert.Equal(fallback.Id, saved.CurrentFolderId); Assert.Equal(selected, saved.Current!.Path); Assert.Equal([selected], saved.Bags[fallback.Id].Known); Assert.Empty(saved.Bags[fallback.Id].Remaining); Assert.Null(saved.Pending);
    });
    [Fact] public async Task AllConfiguredFoldersWithoutAlternativeRetainSourceAndExactState() => await WithFixture(async (r, state, fake) =>
    {
        await SeedSingle(r, fake); var empty = AddFolder(r, "empty"); var before = File.ReadAllBytes(state); var old = ReadState(state).Current!;
        var result = await new WallpaperEngine(fake).ExecuteAsync(WithFolders(r, empty) with { CommandId = "delete-now", Confirmed = true });
        Assert.False(result.Success); Assert.Contains("any enabled configured folder", result.Summary); Assert.Empty(fake.Calls); Assert.Equal(before, File.ReadAllBytes(state)); Assert.True(File.Exists(old.Path));
    });
    [Fact] public async Task DisabledFoldersAreNotInspectedAndOrdinaryNextNeverFallsBack() => await WithFixture(async (r, state, fake) =>
    {
        await SeedSingle(r, fake); var other = AddFolder(r, "other", "c.jpg"); var engine = new WallpaperEngine(fake);
        Assert.False((await engine.ExecuteAsync(WithFolders(r, other with { Enabled = false }) with { CommandId = "delete-now", Confirmed = true })).Success);
        Assert.DoesNotContain(Path.Combine(other.Path, "c.jpg"), fake.Inspections); Assert.Empty(fake.Calls); fake.Inspections.Clear();
        Assert.False((await engine.ExecuteAsync(WithFolders(r, other))).Success); Assert.DoesNotContain(Path.Combine(other.Path, "c.jpg"), fake.Inspections); Assert.Empty(fake.Calls);
    });
    [Fact] public async Task CurrentFolderAlternativeWinsWithoutInspectingFallback() => await WithFixture(async (r, state, fake) =>
    {
        var engine = new WallpaperEngine(fake); await engine.ExecuteAsync(r); var other = AddFolder(r, "other", "c.jpg"); fake.Calls.Clear(); fake.Inspections.Clear();
        Assert.True((await engine.ExecuteAsync(WithFolders(r, other) with { CommandId = "delete-now", Confirmed = true })).Success);
        Assert.Equal(r.FolderId, ReadState(state).CurrentFolderId); Assert.DoesNotContain(Path.Combine(other.Path, "c.jpg"), fake.Inspections);
    });
    [Fact] public async Task EmptyMissingInvalidAndCurrentAliasFallbacksYieldToNextValidFolder() => await WithFixture(async (r, state, fake) =>
    {
        await SeedSingle(r, fake); var old = ReadState(state).Current!;
        var empty = AddFolder(r, "empty"); var missing = new Folder(Guid.NewGuid(), "Missing", Path.Combine(Path.GetDirectoryName(r.DataDirectory)!, "missing"));
        var invalid = AddFolder(r, "invalid", "bad.jpg"); fake.Rejected.Add(Path.Combine(invalid.Path, "bad.jpg"));
        var alias = AddFolder(r, "alias", "same.jpg"); var aliasPath = Path.Combine(alias.Path, "same.jpg"); fake.Identities[aliasPath] = old with { Path = aliasPath };
        var valid = AddFolder(r, "valid", "different.jpg");
        Assert.True((await new WallpaperEngine(fake).ExecuteAsync(WithFolders(r, empty, missing, invalid, alias, valid) with { CommandId = "delete-now", Confirmed = true })).Success);
        Assert.Equal(valid.Id, ReadState(state).CurrentFolderId); Assert.Equal("recycle:" + old.Path, fake.Calls.Last()); Assert.DoesNotContain(fake.Calls, x => x.EndsWith(aliasPath));
    });
    [Theory] [InlineData(false, true)] [InlineData(true, false)] public async Task FallbackPartialOrUnverifiedReplacementNeverRecycles(bool lockResult, bool verified) => await WithFixture(async (r, state, fake) =>
    {
        await SeedSingle(r, fake); var other = AddFolder(r, "other", "c.jpg"); var old = ReadState(state).Current!; fake.Lock = lockResult; fake.ReplacementVerified = verified;
        Assert.False((await new WallpaperEngine(fake).ExecuteAsync(WithFolders(r, other) with { CommandId = "delete-now", Confirmed = true })).Success);
        Assert.Equal(2, fake.Calls.Count); Assert.DoesNotContain(fake.Calls, x => x.StartsWith("recycle:")); Assert.True(File.Exists(old.Path)); Assert.Equal(other.Id, ReadState(state).CurrentFolderId);
    });
    [Fact] public async Task FallbackDecoderBudgetIsSharedAndNeverStartsWritesOnExhaustion() => await WithFixture(async (r, state, fake) =>
    {
        await SeedSingle(r, fake); var a = AddFolder(r, "bad-a", Enumerable.Range(0, 32).Select(i => i + ".jpg").ToArray()); var b = AddFolder(r, "bad-b", Enumerable.Range(0, 32).Select(i => i + ".jpg").ToArray()); var valid = AddFolder(r, "valid", "c.jpg");
        foreach (var folder in new[] { a, b }) foreach (var file in Directory.EnumerateFiles(folder.Path)) fake.Rejected.Add(file);
        var before = File.ReadAllBytes(state); var result = await new WallpaperEngine(fake).ExecuteAsync(WithFolders(r, a, b, valid) with { CommandId = "delete-now", Confirmed = true });
        Assert.False(result.Success); Assert.Contains("Shared image decoder refusal budget", result.Summary); Assert.Empty(fake.Calls); Assert.Equal(before, File.ReadAllBytes(state)); Assert.DoesNotContain(Path.Combine(valid.Path, "c.jpg"), fake.Inspections);
    });
}
