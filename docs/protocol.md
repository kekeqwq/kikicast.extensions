# Protocol 1 / declarative package SDK

The host installs only matching native OS `win-arm64`/`win-x64`, minimum host <=0.2.0 and protocol 1 packages. `manifest.template.json` declares immutable plugin ID, version, executable, capability disclosures, boolean fields and static/folder-template commands. The packager replaces RID/version and hashes every file (except the manifest) including licenses. A manifest is metadata; the host does not load plugin DLLs/XAML to render settings/list commands.

Installed executable invocation is exactly `--kikicast-protocol-1`, stdin one **unindented UTF-8 JSON line**:

```json
{"protocol":1,"pluginId":"random-wallpaper","commandId":"next","folderId":"UUID","configuration":{"enabled":true,"options":{"desktop":true,"lock-screen":true},"folders":[{"id":"UUID","name":"Folder name","path":"C:/local/folder","enabled":true}]},"dataDirectory":"C:/owned/installation/data","confirmed":false}
```

stdout is a bounded JSON reply `{ "success": true, "summary": "Separate target results" }`; do not echo authored configuration/paths/secrets to stdout/stderr. State/configuration are trusted local plaintext; don't store secrets. The host awaits the started worker; current implementation has no forced process termination/detached timeout. An extension may misbehave and has current-user capabilities: capability declarations are not enforced isolation.

`--describe` is side-effect-free (protocol/plugin ID/on-demand/no PowerShell). Any other standalone invocation refuses. Workers never register startup or search/index independently. Saved configuration alone authorizes no desktop side effect.

Protocol SDK 1 deliberately offers booleans and UUID folder collections, not arbitrary forms/in-process controls. Stable command IDs: `extension:<plugin>:<command>[:<folder-UUID>]`. Do not derive identity from display name/path. Commands require host/current master/plugin/folder availability; destructive ones also require fresh host confirmation and worker-side `confirmed` verification. Hiding/display-off does not disable bindings.

RandomWallpaper persists `state.json` (1 MiB bound) containing folder bags, selected source identity/hash/pixels/folder UUID, target source outcomes and pending native reservation. Reservation is saved before native setters. Pending/damaged states refuse operations; explicit host Reset preserves a backup. Uninstall removes installation/configuration/state/backups, never source paths. DeleteNow exhausts the current folder's valid alternatives before trying other enabled configured folders in saved order; ordinary folder commands remain strictly folder-scoped. The fallback destination UUID/bag becomes current only after at least one target accepts replacement. Inspection (8192), candidates (2048) and decoder refusals (64) are shared across the entire request, not multiplied per folder. Current-folder/source-definition gates, confirmation, old/new image verification and replace-before-recycle remain unchanged. No replacement anywhere eligible means no wallpaper write, no recycle and unchanged state.

`0.1.0-preview.3` checks current public original source references, not the pixels returned by LockScreen.GetImageStream: Windows can transform the displayed image while preserving the original file reference. LockScreen.OriginalImageFile must be a local file URI matching the expected source path, and the bounded safe inspector must match file ID/length/mtime/SHA/pixels. Desktop SPI_GETDESKWALLPAPER receives the same strict source check. Missing/stream-set/remote/ambiguous/external references refuse, even if an unrelated image has identical pixels. Both pre-delete and post-replacement checks use this policy. Version-1 state and stable UUIDs are unchanged; no reset or native action occurs on update.

Wallpaper images are never copied into extension data, so Windows does not reference a plugin-owned image cache that uninstall would remove.

API references: [LockScreen.OriginalImageFile](https://learn.microsoft.com/uwp/api/windows.system.userprofile.lockscreen.originalimagefile), [LockScreen.SetImageFileAsync](https://learn.microsoft.com/uwp/api/windows.system.userprofile.lockscreen.setimagefileasync), [SystemParametersInfoW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow), [IFileOperation flags](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags).
