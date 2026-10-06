using Xunit;

namespace RandomWallpaper.Tests;
public class VerificationTests
{
    private static readonly ImageIdentity Source = new(@"C:\GeneratedWallpaper\image.png", 128, 1000, "volume:file", "source-sha", "source-pixels");
    private static bool Verify(Func<string?>? desktop = null, Func<Uri?>? original = null, Func<string, ImageIdentity>? inspect = null, bool useDesktop = true, bool useLock = true) =>
        WallpaperImageVerification.Verify(Source, useDesktop, useLock, desktop ?? (() => Source.Path), original ?? (() => new Uri(Source.Path)), inspect ?? (path => Source with { Path = path }));
    [Fact] public void MatchingOriginalReferencesDoNotRequireWindowsRenderedStreamPixels() => Assert.True(Verify());
    [Fact] public void FileUriEscapingAndPathCasePreserveExactFileIdentity()
    {
        var source = Source with { Path = @"C:\GeneratedWallpaper\中文 a#b%.png" };
        var reference = source.Path.ToUpperInvariant();
        Assert.True(WallpaperImageVerification.Verify(source, true, true, () => reference, () => new Uri(reference), path => source with { Path = path }));
    }
    [Theory] [InlineData(false)] [InlineData(true)] public void OtherPathEvenWithIdenticalPixelsNeverAuthorizesDeletion(bool desktop)
    {
        Assert.False(desktop ? Verify(desktop: () => @"C:\GeneratedWallpaper\other.png") : Verify(original: () => new Uri(@"C:\GeneratedWallpaper\other.png")));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] public void ChangedSourceIdentityOrContentRefuses(int changed)
    {
        var actual = changed switch { 0 => Source with { Length = 129 }, 1 => Source with { LastWriteTicks = 2000 }, 2 => Source with { FileId = "different-file" }, 3 => Source with { Sha256 = "different-sha" }, _ => Source with { Pixels = "different-pixels" } };
        Assert.False(Verify(inspect: _ => actual));
    }
    [Theory] [InlineData("https://example.invalid/image.png")] [InlineData("file://server/share/image.png")] [InlineData("ms-appdata:///local/image.png")] [InlineData("file:///C:/GeneratedWallpaper/image.png?query")] [InlineData("file:///C:/GeneratedWallpaper/image.png#fragment")] public void NonLocalOrAmbiguousLockReferencesRefuse(string uri) => Assert.False(Verify(original: () => new Uri(uri)));
    [Fact] public void MissingUnsupportedOrUnsafeReadbackRefusesWithoutFallback()
    {
        Assert.False(Verify(desktop: () => null)); Assert.False(Verify(original: () => null));
        Assert.False(Verify(original: () => throw new System.Runtime.InteropServices.COMException("Generated stream-set/no original file")));
        Assert.False(Verify(inspect: _ => throw new System.IO.IOException("Generated unsafe/linked/missing source")));
        Assert.False(Verify(useDesktop: false, useLock: false));
    }
    [Fact] public void OnlyTrackedTargetsAreRead()
    {
        Assert.True(Verify(original: () => throw new InvalidOperationException(), useLock: false));
        Assert.True(Verify(desktop: () => throw new InvalidOperationException(), useDesktop: false));
    }
}
