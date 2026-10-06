param([string]$Version='0.1.0-preview.1', [string]$OutputDirectory, [switch]$RequireCleanSource)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d{1,4}\.\d{1,4}\.\d{1,4}-[a-zA-Z0-9.-]{1,40}$') { throw 'Only explicitly incomplete preview packages are supported.' }
if (-not $OutputDirectory) { $OutputDirectory=Join-Path $repo ('artifacts/releases/'+$Version) }
if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory) -or (Test-Path $OutputDirectory)) { throw 'Provide a fresh absolute output directory.' }
if ($RequireCleanSource -and (@(& git -C $repo status --porcelain).Count -ne 0 -or $LASTEXITCODE -ne 0)) { throw 'Release packaging requires clean committed source.' }
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class KikicastNativeOS { [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool IsWow64Process2(IntPtr p, out ushort process, out ushort native); }'
$processMachine=[ushort]0; $nativeMachine=[ushort]0
if (-not [KikicastNativeOS]::IsWow64Process2([IntPtr]::new(-1),[ref]$processMachine,[ref]$nativeMachine)) { throw 'Native Windows architecture unavailable.' }
$nativeRuntime=if ($nativeMachine -eq 0xaa64) { 'win-arm64' } elseif ($nativeMachine -eq 0x8664) { 'win-x64' } else { throw 'Unsupported native architecture.' }
$sourceCommit=& git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Committed source is required for package receipts.' }
function SHA([string]$file) { $stream=[IO.File]::OpenRead($file); try { [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() } }
Push-Location $repo
try {
 & dotnet test tests/RandomWallpaper.Tests/RandomWallpaper.Tests.csproj -c Release -warnaserror
 if ($LASTEXITCODE -ne 0) { throw 'Generated/fake wallpaper tests failed. No real wallpaper API is tested by this script.' }
 [void][IO.Directory]::CreateDirectory($OutputDirectory)
 $catalog=@(); $receipt=@()
 foreach ($runtime in @('win-arm64','win-x64')) {
  $payload=Join-Path $OutputDirectory ('payload-'+$runtime)
  & dotnet publish plugins/personalization/RandomWallpaper/RandomWallpaper.csproj -c Release -r $runtime --self-contained true -p:Version=$Version -warnaserror -o $payload
  if ($LASTEXITCODE -ne 0) { throw ('Publish failed: '+$runtime) }
  $licenses=Join-Path $payload 'licenses'; [void][IO.Directory]::CreateDirectory($licenses)
  Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $licenses 'Kikicast.Extensions.LICENSE.txt')
  Copy-Item (Join-Path $repo 'NOTICE.md') (Join-Path $licenses 'NOTICE.md')
  $nuget=Join-Path $HOME '.nuget/packages'
  foreach ($pack in @(('microsoft.netcore.app.runtime.'+$runtime),('microsoft.windowsdesktop.app.runtime.'+$runtime))) {
   $source=Join-Path $nuget ($pack+'/10.0.12')
   foreach ($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
    $file=Join-Path $source $name
    if ($name -eq 'LICENSE.TXT' -and -not (Test-Path $file)) { $file=Join-Path $source 'LICENSE' }
    if (Test-Path $file) { Copy-Item $file (Join-Path $licenses ($pack+'-'+$name)) }
    elseif ($name -eq 'LICENSE.TXT') { throw ('Required runtime license missing: '+$pack) }
   }
  }
  Copy-Item (Join-Path $repo 'third-party/*') $licenses
  $manifest=Get-Content (Join-Path $repo 'plugins/personalization/RandomWallpaper/manifest.template.json') -Raw | ConvertFrom-Json -AsHashtable
  $manifest.runtime=$runtime; $manifest.version=$Version; $manifest.files=@{}
  foreach ($file in Get-ChildItem $payload -File -Recurse) { $relative=[IO.Path]::GetRelativePath($payload,$file.FullName).Replace('\','/'); $manifest.files[$relative]=SHA $file.FullName }
  $manifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $payload 'manifest.json') -Encoding utf8NoBOM
  # --describe is deliberately side-effect-free: no configuration, candidate scan, native wallpaper/recycle/startup calls.
  $described=$false
  if ($runtime -eq $nativeRuntime -or ($nativeRuntime -eq 'win-arm64' -and $runtime -eq 'win-x64')) {
   $describe=& (Join-Path $payload 'RandomWallpaper.exe') --describe
   if ($LASTEXITCODE -ne 0 -or ($describe -join "`n" | ConvertFrom-Json).protocol -ne 1) { throw 'Published protocol description failed.' }
   $described=$true
  }
  $asset='RandomWallpaper-'+$Version+'-'+$runtime+'.kikicast'
  [IO.Compression.ZipFile]::CreateFromDirectory($payload,(Join-Path $OutputDirectory $asset),[IO.Compression.CompressionLevel]::Optimal,$false)
  $hash=SHA (Join-Path $OutputDirectory $asset)
  $catalog+=@{id='random-wallpaper';name='RandomWallpaper';version=$Version;runtime=$runtime;minimumHostVersion='0.2.0';protocol=1;asset=$asset;sha256=$hash}
  $validationKind=if ($runtime -eq $nativeRuntime) { 'native-describe-only' } elseif ($described) { 'emulated-x64-on-arm64-describe-only' } else { 'cross-published-no-execution' }
  $receipt+=@{sourceCommit=$sourceCommit;runtime=$runtime;publishedValidation=$validationKind;nativeX64WallpaperAccepted=$false;generatedTests=$true;publishedDescribe=$described;nativeWallpaperAcceptance=$false;wallpaperChanged=$false;sourceRecycled=$false;autostartChanged=$false;signed=$false}
  Remove-Item $payload -Recurse -Force
 }
 $catalog | ConvertTo-Json -Depth 5 -AsArray | Set-Content (Join-Path $OutputDirectory 'catalog.json') -Encoding utf8NoBOM
 $receipt | ConvertTo-Json -Depth 5 -AsArray | Set-Content (Join-Path $OutputDirectory 'validation.json') -Encoding utf8NoBOM
 foreach ($item in $catalog) { ($item.sha256+'  '+$item.asset) | Add-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8NoBOM }
} finally { Pop-Location }
