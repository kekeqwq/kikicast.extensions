"""Draft-first exact-source preview publisher. Credentials stay in memory; no worker commands run."""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
REPO = "kekeqwq/kikicast.extensions"


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def credential():
    result = subprocess.run(["git", "-C", str(ROOT), "-c", "credential.interactive=never", "credential", "fill"],
                            input="protocol=https\nhost=github.com\n\n", text=True, capture_output=True, timeout=30,
                            env={**os.environ, "GIT_TERMINAL_PROMPT": "0", "GCM_INTERACTIVE": "Never"})
    fields = dict(line.split("=", 1) for line in result.stdout.splitlines() if "=" in line)
    if result.returncode or not fields.get("password"):
        raise RuntimeError("Existing noninteractive GitHub authentication required; no credentials printed.")
    return fields["password"]


def api(token, method, path, data=None):
    request = urllib.request.Request("https://api.github.com" + path, method=method,
        data=None if data is None else json.dumps(data).encode(), headers={"Authorization": "Bearer " + token,
        "Accept": "application/vnd.github+json", "Content-Type": "application/json", "User-Agent": "Kikicast-extensions-release"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def upload(token, url, file):
    endpoint = urllib.parse.urlsplit(url)
    if endpoint.scheme != "https" or endpoint.hostname != "uploads.github.com":
        raise RuntimeError("Unexpected upload endpoint; no credential delegation.")
    connection = http.client.HTTPSConnection(endpoint.hostname, timeout=180)
    try:
        connection.putrequest("POST", endpoint.path + "?name=" + urllib.parse.quote(file.name))
        for key, value in {"Authorization": "Bearer " + token, "Accept": "application/vnd.github+json", "User-Agent": "Kikicast-extensions-release",
                           "Content-Type": "application/octet-stream", "Content-Length": str(file.stat().st_size)}.items():
            connection.putheader(key, value)
        connection.endheaders()
        with file.open("rb") as source:
            while block := source.read(1024 * 1024):
                connection.send(block)
        response = connection.getresponse()
        if response.status != 201:
            raise RuntimeError("Asset upload failed: HTTP " + str(response.status) + "; draft retained.")
        asset = json.load(response)
        if asset.get("size") != file.stat().st_size or asset.get("digest") != "sha256:" + sha(file):
            raise RuntimeError("Uploaded digest/size mismatch; draft retained.")
        return asset
    finally:
        connection.close()


def validate_package(file, item):
    if not file.is_file() or file.is_symlink() or not 1024 * 1024 < file.stat().st_size <= 200 * 1024 * 1024 or sha(file) != item["sha256"]:
        raise RuntimeError("Missing/unsafe package or checksum mismatch.")
    with zipfile.ZipFile(file) as package:
        entries = package.infolist()
        names = [x.filename for x in entries]
        if len(entries) > 1025 or len(set(x.casefold() for x in names)) != len(names) or sum(x.file_size for x in entries) > 512 * 1024 * 1024:
            raise RuntimeError("Archive name/count/size budget mismatch.")
        for entry in entries:
            if entry.is_dir() or entry.file_size > 128 * 1024 * 1024 or (entry.external_attr >> 16) & 0o170000 == 0o120000 or not re.fullmatch(r"[A-Za-z0-9._/-]+", entry.filename) or any(p in ("", ".", "..") for p in entry.filename.split("/")):
                raise RuntimeError("Unsafe archive entry.")
        manifest = json.loads(package.read("manifest.json"))
        if any(manifest.get(k) != item[k] for k in ["id", "version", "runtime", "minimumHostVersion", "protocol"]) or manifest.get("executable") != "RandomWallpaper.exe":
            raise RuntimeError("Manifest/catalog identity mismatch.")
        inventory = manifest["files"]
        if set(names) != set(inventory) | {"manifest.json"} or not any(x.startswith("licenses/") for x in inventory):
            raise RuntimeError("Manifest inventory/license mismatch.")
        for name, expected in inventory.items():
            with package.open(name) as stream:
                if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
                    raise RuntimeError("Payload inventory digest mismatch.")
        exe = package.read("RandomWallpaper.exe")
        offset = struct.unpack_from("<I", exe, 0x3c)[0]
        if exe[:2] != b"MZ" or exe[offset:offset + 4] != b"PE\x00\x00" or struct.unpack_from("<H", exe, offset + 4)[0] != {"win-arm64": 0xaa64, "win-x64": 0x8664}[item["runtime"]]:
            raise RuntimeError("Payload apphost architecture mismatch.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", default="0.1.0-preview.3")
    parser.add_argument("--artifacts", required=True, type=Path)
    parser.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"\d+\.\d+\.\d+-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*", args.version):
        parser.error("Explicit prerelease required, not stable/signing-gate bypass.")
    if git("status", "--porcelain") or git("remote", "get-url", "origin") != "https://github.com/" + REPO + ".git":
        raise RuntimeError("Clean committed source/exact authorized origin required.")
    source = git("rev-parse", "HEAD"); tag = "v" + args.version
    if git("rev-parse", tag + "^{commit}") != source or not git("ls-remote", "origin", "refs/tags/" + tag + "^{}").startswith(source + "\t"):
        raise RuntimeError("Exact source annotated tag must already be pushed; no force push.")
    directory = args.artifacts.resolve(strict=True)
    catalog = json.loads((directory / "catalog.json").read_text(encoding="utf-8-sig"))
    validation = json.loads((directory / "validation.json").read_text(encoding="utf-8-sig"))
    if len(catalog) != 2 or {x["runtime"] for x in catalog} != {"win-arm64", "win-x64"} or len(validation) != 2 or {x["runtime"] for x in validation} != {"win-arm64", "win-x64"}:
        raise RuntimeError("Exact dual-runtime catalog/validation required.")
    packages = []
    for item in catalog:
        if item["id"] != "random-wallpaper" or item["version"] != args.version or item["protocol"] != 1 or item["minimumHostVersion"] != "0.2.0" or item["asset"] != f"RandomWallpaper-{args.version}-{item['runtime']}.kikicast":
            raise RuntimeError("Catalog mismatch.")
        receipt = next(x for x in validation if x["runtime"] == item["runtime"])
        if receipt["sourceCommit"] != source or not receipt["generatedTests"] or not receipt["publishedDescribe"] or receipt["publishedValidation"] not in {"native-describe-only", "emulated-x64-on-arm64-describe-only"} or any(receipt[x] for x in ["wallpaperChanged", "sourceRecycled", "autostartChanged", "signed", "nativeX64WallpaperAccepted", "nativeWallpaperAcceptance"]):
            raise RuntimeError("Incomplete or misleading side-effect-free validation receipt.")
        file = directory / item["asset"]; validate_package(file, item); packages.append(file)
    sums = (directory / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines()
    if set(sums) != {x["sha256"] + "  " + x["asset"] for x in catalog} or len(sums) != 2:
        raise RuntimeError("Checksum list mismatch.")
    if not args.publish:
        print("PASS: exact source/tag/dual packages/catalog/inventory/architecture/checksums; no release created or commands executed.")
        return
    token = credential()
    if api(token, "GET", "/user")["login"] != "kekeqwq" or not api(token, "GET", "/repos/" + REPO).get("permissions", {}).get("push"):
        raise RuntimeError("Unexpected authenticated owner or missing permissions.")
    try:
        api(token, "GET", "/repos/" + REPO + "/releases/tags/" + tag)
        raise RuntimeError("Existing release is immutable; no overwrite.")
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
    body = (ROOT / "docs/releases" / (args.version + ".md")).read_text(encoding="utf-8")
    release = api(token, "POST", "/repos/" + REPO + "/releases", {"tag_name": tag, "target_commitish": source, "name": "RandomWallpaper " + args.version + " — Windows/.NET extensions", "body": body, "draft": True, "prerelease": True})
    files = packages + [directory / x for x in ["catalog.json", "SHA256SUMS.txt", "validation.json"]]
    for file in files:
        upload(token, release["upload_url"].split("{", 1)[0], file)
    assets = api(token, "GET", "/repos/" + REPO + "/releases/" + str(release["id"]) + "/assets")
    if {a["name"] for a in assets} != {f.name for f in files}:
        raise RuntimeError("Unexpected asset set; draft retained.")
    public = api(token, "PATCH", "/repos/" + REPO + "/releases/" + str(release["id"]), {"draft": False, "prerelease": True, "make_latest": "false"})
    print("PASS: published verified unsigned extension prerelease: " + public["html_url"])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print("Release stopped:", type(error).__name__, str(error) if isinstance(error, RuntimeError) else "validation/network failure; no credentials printed")
        raise SystemExit(1)
