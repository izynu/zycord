#!/usr/bin/env python3
"""Build Zycord.

Produces a self-contained folder under dist/ containing the launcher, the
session stub, the interface files and the WebView2 assemblies, then packs it
into a release archive with a SHA-256 checksum.

Only the .NET Framework 4 compiler that ships with Windows is required, plus
the WebView2 SDK assemblies recorded in vendor/. No SDK, no MSBuild, no NuGet
restore at build time.

Usage:
    python build.py              build, run the self test, pack the release
    python build.py --no-test    build and pack without the self test
    python build.py --no-zip     leave the folder unpacked
    python build.py --clean      remove dist/ first
"""

import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.abspath(__file__))
MAIN = os.path.join(ROOT, "app", "main")
RENDERER = os.path.join(ROOT, "app", "renderer")
STUB = os.path.join(ROOT, "stub")
BRAND = os.path.join(ROOT, "brand")
VENDOR = os.path.join(ROOT, "vendor")
DIST = os.path.join(ROOT, "dist")

VERSION = "1.1.3"
ARCHIVE = "Zycord-%s.zip" % VERSION

CSC_CANDIDATES = [
    r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe",
]

SOURCES = [
    "AssemblyInfo.cs",
    "Program.cs",
    "Shell.cs",
    "Store.cs",
    "Sessions.cs",
    "Ui.cs",
    "Bridge.cs",
    "SelfTest.cs",
]

REFERENCES = [
    "System.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Web.Extensions.dll",
]

VENDOR_FILES = [
    ("lib/net462/Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.Core.dll"),
    ("lib/net462/Microsoft.Web.WebView2.WinForms.dll", "Microsoft.Web.WebView2.WinForms.dll"),
    ("runtimes/win-x64/native/WebView2Loader.dll", "WebView2Loader.dll"),
]

UI_FILES = ["index.html", "app.css", "app.js", "mark.svg"]

# Strings that must never appear in a release. The project was rebuilt from
# scratch under a new name; this check keeps a stray copy-paste from undoing it.
FORBIDDEN = ["markterence", "discord-quest-completer", "discordquestcompleter"]


def find_csc():
    for candidate in CSC_CANDIDATES:
        if os.path.isfile(candidate):
            return candidate
    raise SystemExit("The .NET Framework compiler (csc.exe) was not found.")


def run(cmd, **kw):
    return subprocess.run(cmd, check=False, **kw)


def clean():
    if os.path.isdir(DIST):
        shutil.rmtree(DIST)
        print("removed dist/")
    for name in (ARCHIVE, "SHA256.txt"):
        path = os.path.join(ROOT, name)
        if os.path.isfile(path):
            os.remove(path)
            print("removed %s" % name)


def check_sources():
    hits = []
    for base in (MAIN, RENDERER, STUB, os.path.join(ROOT, "scripts")):
        for name in os.listdir(base):
            path = os.path.join(base, name)
            if not os.path.isfile(path):
                continue
            if os.path.splitext(name)[1].lower() not in (".cs", ".js", ".css", ".html", ".py", ".svg"):
                continue
            try:
                with open(path, encoding="utf-8", errors="replace") as handle:
                    text = handle.read().lower()
            except OSError:
                continue
            for needle in FORBIDDEN:
                if needle in text:
                    hits.append("%s contains %r" % (path, needle))
    if hits:
        for hit in hits:
            print("  FORBIDDEN " + hit)
        raise SystemExit("rebrand check failed: remove the listed strings")


def build_stub():
    out = os.path.join(DIST, "stub.exe")
    cmd = [
        find_csc(), "-nologo", "-target:winexe", "-platform:x64", "-optimize+",
        "-win32icon:" + os.path.join(BRAND, "zycord.ico"),
        "-out:" + out,
        "-r:System.dll", "-r:System.Drawing.dll", "-r:System.Windows.Forms.dll",
        os.path.join(STUB, "Stub.cs"),
    ]
    print("compiling stub")
    r = run(cmd, capture_output=True, text=True)
    if r.returncode != 0 or not os.path.isfile(out):
        print(r.stdout, r.stderr)
        raise SystemExit("stub build failed")
    print("  stub.exe  %d bytes" % os.path.getsize(out))


def build_launcher():
    out = os.path.join(DIST, "Zycord.exe")
    cmd = [
        find_csc(), "-nologo", "-target:winexe", "-platform:x64", "-optimize+",
        "-warn:4",
        "-win32icon:" + os.path.join(BRAND, "zycord.ico"),
        "-out:" + out,
    ]
    cmd += ["-r:" + r for r in REFERENCES]
    for rel, _name in VENDOR_FILES:
        if rel.endswith(".dll") and "lib/" in rel:
            cmd.append("-r:" + os.path.join(VENDOR, rel))
    cmd += [os.path.join(MAIN, s) for s in SOURCES]

    print("compiling launcher")
    r = run(cmd, capture_output=True, text=True)
    for line in (r.stdout or "").splitlines():
        if line.strip():
            print("  " + line)
    if r.returncode != 0 or not os.path.isfile(out):
        print(r.stderr)
        raise SystemExit("launcher build failed")
    print("  Zycord.exe  %d bytes" % os.path.getsize(out))


def copy_text(src, dst):
    """Copy a text file with LF line endings.

    A checkout made with core.autocrlf=true on Windows carries CRLF, and
    copying it verbatim makes every text file in the release differ from the
    repository by one byte per line. Writing LF keeps the download checkable
    against the source it was built from.
    """
    with open(src, "rb") as handle:
        data = handle.read()
    with open(dst, "wb") as handle:
        handle.write(data.replace(b"\r\n", b"\n"))
    shutil.copystat(src, dst)


def copy_ui():
    ui = os.path.join(DIST, "ui")
    os.makedirs(ui, exist_ok=True)
    for name in UI_FILES:
        copy_text(os.path.join(RENDERER, name), os.path.join(ui, name))

    # Ship the catalog as plain JSON so the interface can fetch it directly.
    import gzip
    import json
    src = os.path.join(ROOT, "app", "assets", "catalog.json.gz")
    dst = os.path.join(ui, "catalog.json")
    with gzip.open(src, "rb") as f:
        data = json.loads(f.read().decode("utf-8"))
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print("  ui/  %d games" % data["count"])


def copy_vendor():
    for rel, name in VENDOR_FILES:
        src = os.path.join(VENDOR, rel)
        if not os.path.isfile(src):
            raise SystemExit("missing vendor file: " + src)
        shutil.copy2(src, os.path.join(DIST, name))


def copy_docs():
    for name in ("LICENSE", "README.md"):
        src = os.path.join(ROOT, name)
        if os.path.isfile(src):
            copy_text(src, os.path.join(DIST, name))


def check_line_endings():
    """Fail the build if a copied text file still carries CRLF."""
    paths = [os.path.join(DIST, "ui", n) for n in UI_FILES]
    paths += [os.path.join(DIST, n) for n in ("LICENSE", "README.md")]
    hits = []
    for path in paths:
        if not os.path.isfile(path):
            continue
        with open(path, "rb") as handle:
            if b"\r\n" in handle.read():
                hits.append(os.path.relpath(path, ROOT))
    if hits:
        for hit in hits:
            print("  CRLF " + hit)
        raise SystemExit("line ending check failed: text files must use LF")


def self_test():
    exe = os.path.join(DIST, "Zycord.exe")
    print("running self test")
    r = run([exe, "--headless"], capture_output=True, text=True, timeout=180)
    out = (r.stdout or "").strip()
    for line in out.splitlines():
        print("  " + line)
    if r.returncode != 0:
        raise SystemExit("self test failed")
    print("  self test passed")


def pack():
    archive = os.path.join(ROOT, ARCHIVE)
    if os.path.isfile(archive):
        os.remove(archive)

    names = []
    for dirpath, _dirnames, filenames in os.walk(DIST):
        for name in filenames:
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, DIST).replace("\\", "/")
            names.append((full, "Zycord-%s/%s" % (VERSION, rel)))

    names.sort(key=lambda pair: pair[1])
    # Fixed timestamps and attributes keep the archive byte-identical across
    # rebuilds of the same dist/. The files inside still carry their own PE
    # timestamps, so this makes the archive deterministic, not the binaries.
    fixed = (1980, 1, 1, 0, 0, 0)
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for full, rel in names:
            info = zipfile.ZipInfo(rel, date_time=fixed)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(full, "rb") as handle:
                z.writestr(info, handle.read())

    digest = hashlib.sha256()
    with open(archive, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    sha = digest.hexdigest()

    with open(os.path.join(ROOT, "SHA256.txt"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write("%s  %s\n" % (sha, ARCHIVE))

    print()
    print("release  %s  %.2f MB" % (ARCHIVE, os.path.getsize(archive) / (1024 * 1024)))
    print("sha256   %s" % sha)
    return sha


def report():
    total = 0
    count = 0
    for dirpath, _dirnames, filenames in os.walk(DIST):
        for name in filenames:
            total += os.path.getsize(os.path.join(dirpath, name))
            count += 1
    print()
    print("dist/ ready, %.1f MB in %d files" % (total / (1024 * 1024), count))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--clean", action="store_true")
    parser.add_argument("--no-test", action="store_true")
    parser.add_argument("--no-zip", action="store_true")
    args = parser.parse_args()

    if args.clean:
        clean()

    check_sources()
    os.makedirs(DIST, exist_ok=True)
    build_stub()
    build_launcher()
    copy_vendor()
    copy_ui()
    copy_docs()
    check_line_endings()

    if not args.no_test:
        self_test()

    report()
    if not args.no_zip:
        pack()
    return 0


if __name__ == "__main__":
    sys.exit(main())
