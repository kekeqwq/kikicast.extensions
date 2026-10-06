namespace RandomWallpaper;

/// <summary>Verifies public Windows source references, not transformed rendering/cache pixels.</summary>
public static class WallpaperImageVerification
{
    public static bool Verify(ImageIdentity image, bool desktop, bool lockScreen,
        Func<string?> desktopPath, Func<Uri?> lockOriginalFile, Func<string, ImageIdentity> inspect)
    {
        if (!desktop && !lockScreen) return false;
        try
        {
            if (desktop && !MatchesSource(image, desktopPath(), inspect)) return false;
            if (lockScreen)
            {
                // SetImageFileAsync establishes a file reference. GetImageStream may
                // expose Windows-transformed pixels, so it cannot identify that source.
                // Stream-set/missing/remote/opaque references refuse; never guess a file.
                var original = lockOriginalFile();
                if (original is not { IsAbsoluteUri: true, IsFile: true } || original.Host.Length != 0
                    || original.UserInfo.Length != 0 || original.Query.Length != 0 || original.Fragment.Length != 0
                    || !MatchesSource(image, original.LocalPath, inspect)) return false;
            }
            return true;
        }
        catch { return false; } // Unavailable public readback never authorizes recycling.
    }
    private static bool MatchesSource(ImageIdentity expected, string? path, Func<string, ImageIdentity> inspect)
    {
        if (path == null || !path.Equals(expected.Path, StringComparison.OrdinalIgnoreCase)) return false;
        var actual = inspect(path); // Production inspector checks local/no-link ancestry and all byte/pixel budgets.
        return actual.Path.Equals(expected.Path, StringComparison.OrdinalIgnoreCase)
            && actual.Length == expected.Length && actual.LastWriteTicks == expected.LastWriteTicks
            && actual.FileId == expected.FileId && actual.Sha256 == expected.Sha256 && actual.Pixels == expected.Pixels;
    }
}
