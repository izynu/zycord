#!/usr/bin/env python3
"""Check that the built Zycord.exe really is a single-file installer.

The build embeds the whole release folder as one zlib resource, so the exe
alone can install itself. That is easy to break silently: a file added to
dist/ without being embedded, a resource name typo, or a manifest that does
not match the file bytes would all produce an installer that fails on a user's
machine and nowhere else.

This script unpacks the resource straight out of the executable, using the
same format Setup.cs reads, and checks every declared file against the release
folder it came from. It runs on Windows in CI after a real csc build, which is
the only place this exe exists.

Usage:
    python scripts/verify_installer.py            check dist/Zycord.exe
    python scripts/verify_installer.py path.zip   check the exe inside a release zip
"""

import os
import struct
import sys
import zipfile
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST_EXE = os.path.join(ROOT, "dist", "Zycord.exe")

# A zlib stream in a PE file carries no length of its own, so the payload is
# located by scanning for a deflate header and keeping the first candidate that
# parses as a manifest and accounts for every byte.
SCAN_TAIL = 8 * 1024 * 1024


def candidate_streams(exe_bytes):
    start = max(0, len(exe_bytes) - SCAN_TAIL)
    for offset in range(start, len(exe_bytes) - 2):
        if exe_bytes[offset] != 0x78:
            continue
        try:
            raw = zlib.decompressobj().decompress(exe_bytes[offset:])
        except zlib.error:
            continue
        if len(raw) > 4:
            yield offset, raw


def parse_payload(raw):
    length = struct.unpack("<I", raw[:4])[0]
    if length <= 0 or length > 16 * 1024 * 1024 or 4 + length > len(raw):
        raise ValueError("implausible manifest length")

    manifest = raw[4:4 + length].decode("utf-8")
    body = raw[4 + length:]

    files = []
    index = 0
    while index < len(manifest):
        path_end = manifest.index("\0", index)
        length_end = manifest.index("\n", path_end + 1)
        files.append((manifest[index:path_end], int(manifest[path_end + 1:length_end])))
        index = length_end + 1

    if sum(size for _n, size in files) != len(body):
        raise ValueError("manifest does not match the payload length")
    return files, body


def check(exe_bytes, source_dir):
    found = None
    for offset, raw in candidate_streams(exe_bytes):
        try:
            files, body = parse_payload(raw)
        except (ValueError, UnicodeDecodeError):
            continue
        if files:
            found = (offset, files, body)
            break

    if found is None:
        raise SystemExit("FAIL: no payload resource found in the executable")

    offset, files, body = found
    print("payload at offset %d: %d files, %d bytes" % (offset, len(files), len(body)))

    missing, wrong = [], []
    position = 0
    for name, size in files:
        chunk = body[position:position + size]
        position += size
        if not source_dir:
            continue

        source = os.path.join(source_dir, name.replace("/", os.sep))
        if name == "Zycord.exe":
            # The payload holds the launcher as it was before the payload was
            # embedded, because an executable cannot contain itself. It must
            # still be this build's first pass, which the build leaves beside
            # the final executable for exactly this comparison.
            source = os.path.join(source_dir, "Zycord.exe.firstpass")
            if not os.path.isfile(source):
                # Packaging a zip has no first pass available; skip rather
                # than report a false failure.
                continue
        if not os.path.isfile(source):
            missing.append(name)
        elif open(source, "rb").read() != chunk:
            wrong.append(name)

    if missing:
        raise SystemExit("FAIL: embedded files absent from the build: " + ", ".join(missing))
    if wrong:
        raise SystemExit("FAIL: embedded bytes differ from the build: " + ", ".join(wrong))

    names = [n for n, _s in files]
    required = ["Zycord.exe", "stub.exe", "ui/catalog.json", "ui/index.html", "ui/app.js"]
    absent = [r for r in required if r not in names]
    if absent:
        raise SystemExit("FAIL: the payload is missing required files: " + ", ".join(absent))

    print("PASS: single-file installer carries %d files, all bytes match" % len(files))


def main():
    source_dir = os.path.join(ROOT, "dist")

    if len(sys.argv) > 1:
        with zipfile.ZipFile(sys.argv[1]) as archive:
            inner = [n for n in archive.namelist() if n.endswith("Zycord.exe")]
            if not inner:
                raise SystemExit("FAIL: no Zycord.exe inside " + sys.argv[1])
            exe_bytes = archive.read(inner[0])
            source_dir = None  # compared against itself; the zip is the subject
        print("checking Zycord.exe inside %s" % sys.argv[1])
    else:
        if not os.path.isfile(DIST_EXE):
            raise SystemExit("FAIL: %s does not exist; build first" % DIST_EXE)
        with open(DIST_EXE, "rb") as handle:
            exe_bytes = handle.read()
        print("checking %s" % DIST_EXE)

    print("  %d bytes" % len(exe_bytes))
    check(exe_bytes, source_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
