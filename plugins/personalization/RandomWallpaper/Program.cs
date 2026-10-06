using Kikicast.ExtensionSdk;
using RandomWallpaper;

return await Protocol.RunAsync(args, "random-wallpaper", request => new WallpaperEngine(new WindowsWallpaper()).ExecuteAsync(request));
