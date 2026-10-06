# Protocol 1 / declarative package SDK

The host installs only matching native OS `win-arm64`/`win-x64`, minimum host <=0.2.0 and protocol 1 packages. `manifest.template.json` declares immutable plugin ID, version, executable, capability disclosures, boolean fields and static/folder-template commands. The packager replaces RID/version and hashes every file (except the manifest) including licenses. A manifest is metadata; the host does not load plugin DLLs/XAML to render settings/list commands.

Installed executable invocation is exactly `--kikicast-protocol-1`, stdin one **unindented UTF-8 JSON line**:

```json
{"protocol":1,"pluginId":"random-wallpaper","commandId":"next","folderId":"UUID","configuration":{"enabled":true,"options":{"desktop":true,"lock-screen":true},"folders":[{"id":"UUID","name":"Folder name","path":"C:/local/folder","enabled":true}]},"dataDirectory":"C:/owned/installation/data","confirmed":false}
```

stdout is a bounded JSON reply `{ "success": true, "summary": "Separate target results" }`; do not echo authored configuration/paths/secrets to stdout/stderr. State/configuration are trusted local plaintext; don't store secrets. The host awaits the started worker; current implementation has no forced process termination/detached timeout. An extension may misbehave and has current-user capabilities: capability declarations are not enforced isolation.

`--describe` is side-effect-free (protocol/plugin ID/on-demand/no PowerShell). Any other standalone invocation refuses. Workers never register startup or search/index independently. Saved configuration alone authorizes no desktop side effect.

Protocol SDK 1 deliberately offers booleans and UUID folder collections, not arbitrary forms/in-process controls. Stable command IDs: `extension:<plugin>:<command>[:<folder-UUID>]`. Do not derive identity from display name/path. Commands require host/current master/plugin/folder availability; destructive ones also require fresh host confirmation and worker-side `confirmed` verification. Hiding/display-off does not disable bindings.

RandomWallpaper persists `state.json` (1 MiB bound) containing folder bags, selected source identity/hash/pixels/folder UUID, target source outcomes and pending native reservation. Reservation is saved before native setters. Pending/damaged states refuse operations; explicit host Reset preserves a backup. Uninstall removes installation/configuration/state/backups, never source paths. Wallpaper images are never copied into extension data, so Windows does not reference a plugin-owned image cache that uninstall would remove.

API references: [LockScreen.SetImageFileAsync](https://learn.microsoft.com/uwp/api/windows.system.userprofile.lockscreen.setimagefileasync), [SystemParametersInfoW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow), [IFileOperation flags](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags).
