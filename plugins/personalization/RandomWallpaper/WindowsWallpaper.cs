using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kikicast.ExtensionSdk;
using Microsoft.Win32.SafeHandles;
using Windows.Storage;
using Windows.System.UserProfile;

namespace RandomWallpaper;

public sealed class WindowsWallpaper : IWallpaper
{
    public ImageIdentity Inspect(string path)
    {
        if (!LocalFiles.SafeFile(path)) throw new IOException("Unsafe image.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 32L * 1024 * 1024 || !GetFileInformationByHandle(stream.SafeFileHandle, out var info)) throw new IOException("Invalid image identity.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream)); stream.Position = 0;
        var pixels = PixelHash(stream);
        return new(Path.GetFullPath(path), stream.Length, File.GetLastWriteTimeUtc(path).Ticks, $"{info.VolumeSerialNumber:x8}:{info.FileIndexHigh:x8}{info.FileIndexLow:x8}", hash, pixels);
    }
    private static string PixelHash(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1) throw new NotSupportedException("Animated/multipage image refused.");
        var frame = decoder.Frames[0]; if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 16_000_000) throw new NotSupportedException("Image dimensions exceed budget.");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); var pixels = new byte[checked(frame.PixelWidth * frame.PixelHeight * 4)]; converted.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return frame.PixelWidth + "x" + frame.PixelHeight + ":" + Convert.ToHexStringLower(SHA256.HashData(pixels));
    }
    public bool VerifySystemImage(ImageIdentity image, bool desktop, bool lockScreen) => WallpaperImageVerification.Verify(image, desktop, lockScreen, ReadDesktopPath, () => LockScreen.OriginalImageFile, Inspect);
    private static string? ReadDesktopPath()
    {
        var buffer = new System.Text.StringBuilder(260);
        return GetWallpaper(0x0073, 260, buffer, 0) ? buffer.ToString() : null;
    }
    public Task<bool> ApplyDesktopAsync(string path) => Task.FromResult(LocalFiles.SafeFile(path) && SetWallpaper(20, 0, path, 3));
    public async Task<bool> ApplyLockScreenAsync(string path)
    {
        if (!LocalFiles.SafeFile(path)) return false;
        var file = await StorageFile.GetFileFromPathAsync(path); await LockScreen.SetImageFileAsync(file); return true;
    }
    public Task<bool> RecycleAsync(string path) => RecycleFile.RunAsync(path);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWallpaper(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWallpaper(uint action, uint param, System.Text.StringBuilder value, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write; public uint VolumeSerialNumber, SizeHigh, SizeLow, Links, FileIndexHigh, FileIndexLow; }
}
