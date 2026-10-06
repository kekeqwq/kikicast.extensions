# Kikicast.Extensions

Windows/.NET-native extensions for Kikicast 0.2, AGPL-3.0-only. Not a Raycast runtime or security sandbox. SDK/protocol 1, self-contained .NET 10 x64/ARM64 `.kikicast` ZIP-container packages. No PowerShell/runtime/helper/autostart dependency for extension commands.

- `sdk/Kikicast.ExtensionSdk/`: bounded JSON request/reply runner and local atomic state helpers.
- `plugins/personalization/RandomWallpaper/`: first extension; named UUID folders, persistent nonrepeat bags/current-source identity, desktop/lock-screen results and default-No gated DeleteNow.
- `tests/RandomWallpaper.Tests/`: generated directories/fake Windows adapter only; **never changes real wallpaper or recycles images**.
- `build/package.ps1`: one dual-architecture packaging entry, exact runtime/vendor licenses, complete payload hashes, side-effect-free `--describe`, catalog/checksums and truthful validation receipts.

```powershell
dotnet test Kikicast.Extensions.slnx -c Release -warnaserror
pwsh -NoProfile -File build/package.ps1 -OutputDirectory C:\absolute\fresh-output
```

Build packaging uses PowerShell as development tooling; installed extensions are self-contained .NET executables and do not require it. Packages are unsigned previews; integrity hashes do not establish trust. Host install/update/settings/catalog never run feature code. Human-confirmed commands run with the user's permissions.

First commands: `RandomWallpaper → <folder name>` and `RandomWallpaper → DeleteNow`. On first install configuration is disabled/empty. The authorized local manual-test setup uses `~/Downloads/SafeWallpaper` and `~/Downloads/DeadWallpaper`; these are not distributed personal defaults or scanned during installation.

Supported top-level extensions: JPG/JPEG/PNG/BMP and WebP only if the Windows decoder supports it. Invalid/linked/animated/oversized files refuse. Ordinary folder commands never fall back. DeleteNow tries the current folder first; if no different valid image remains, it tries other enabled configured folders in saved order. Missing/unsafe/empty/invalid/current-alias fallback pools are not used. If no replacement exists anywhere eligible, it retains the source. Shared inspection/candidate/decoder budgets can also refuse safely. It verifies the last selected source and live Windows image, replaces first, then explicitly recycles the verified prior source; fallback updates the current folder UUID and its persistent shuffle bag. Public readback/partial failures retain the source and report results. External changes are not monitored. No resident process; startup is Kikicast's separate default-off sign-in option.

The user confirmed wallpaper setting works on the current manual-test host. DeleteNow cross-folder fallback (`0.1.0-preview.2`) awaits manual re-test; broader/native-x64 compatibility and complete deletion acceptance remain pending. ARM64/native and x64-on-ARM64 `--describe` checks do not establish native-x64 wallpaper behavior. Old WallChanger/WallpaperTools/profile remain unchanged. See [protocol.md](docs/protocol.md).
