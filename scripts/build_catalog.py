#!/usr/bin/env python3
"""Build the Zycord game catalog.

Reads Discord's public detectable-applications list, keeps only the entries a
Windows launcher can use, and writes a compressed catalog to app/assets.

Usage:
    python scripts/build_catalog.py                  fetch the endpoint
    python scripts/build_catalog.py path/to/raw.json use a saved copy
    python scripts/build_catalog.py --out other.json.gz

The endpoint is public and needs no authentication. If Discord changes it, the
saved-copy form lets you keep building from a snapshot.

Each game carries the hash of its Discord application icon. The launcher builds
https://cdn.discordapp.com/app-icons/<id>/<hash>.png from it, so the catalog
itself stays small and no image data is shipped in the release.
"""

import argparse
import datetime
import gzip
import json
import os
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ENDPOINT = "https://discord.com/api/applications/detectable"
DEFAULT_OUT = os.path.join(ROOT, "app", "assets", "catalog.json.gz")

SCHEMA = 2

# Characters Windows refuses in a filename, plus the separator.
BAD_CHARS = set('<>:"|?*')
# A hash becomes part of a CDN path, so anything that could reshape the URL is
# refused rather than escaped.
BAD_ICON_CHARS = set(['/', chr(92), '?', '&', '#'])
BACKSLASH = chr(92)

# Extensions Windows will start as a PE image. Discord lists a handful of
# games whose Windows entry is not named .exe; Windows runs the stub under any
# of these names, so they are kept rather than dropped. Script types are
# deliberately absent: a session must never be a .bat or .ps1.
ALLOWED_EXTENSIONS = (
    ".exe", ".bin", ".dat", ".dll", ".des", ".jbg", ".game", ".pae",
)


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Zycord-Catalog/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


def safe_entry(name):
    """Reject anything that could escape the games folder once written."""
    name = (name or "").strip()
    if not name:
        return False
    if any(c in BAD_CHARS for c in name):
        return False
    if name.startswith("/") or name.startswith(BACKSLASH):
        return False
    parts = name.replace(BACKSLASH, "/").split("/")
    if ".." in parts:
        return False
    if len(parts) > 8:
        return False
    last = parts[-1].lower()
    dot = last.rfind(".")
    if dot < 0 or last[dot:] not in ALLOWED_EXTENSIONS:
        return False
    return True


def icon_hash(app):
    """The application icon hash, if Discord published a usable one."""
    value = app.get("icon_hash")
    if not value:
        return ""
    value = str(value).strip()
    if not value or any(c in value for c in BAD_ICON_CHARS):
        return ""
    return value[:128]


def build(raw):
    games = []
    for app in raw:
        executables = []
        seen = set()
        for entry in (app.get("executables") or []):
            if entry.get("os") != "win32":
                continue
            name = (entry.get("name") or "").strip()
            if not safe_entry(name):
                continue
            key = name.lower()
            if key in seen:
                continue
            seen.add(key)
            executables.append({"n": name, "l": 1 if entry.get("is_launcher") else 0})

        if not executables:
            continue

        games.append({
            "id": str(app["id"]),
            "name": (app.get("name") or "").strip(),
            "aliases": [a for a in (app.get("aliases") or []) if a][:6],
            "themes": [t for t in (app.get("themes") or []) if t][:4],
            "icon": icon_hash(app),
            "ex": executables,
        })

    games.sort(key=lambda g: g["name"].lower())
    return {
        "schema": SCHEMA,
        "generated": datetime.date.today().isoformat(),
        "source": ENDPOINT,
        "count": len(games),
        "games": games,
    }


def main():
    parser = argparse.ArgumentParser(description="Build the Zycord game catalog.")
    parser.add_argument("source", nargs="?", help="a saved JSON copy; omit to fetch the endpoint")
    parser.add_argument("--out", default=DEFAULT_OUT, help="output path")
    args = parser.parse_args()

    if args.source:
        with open(args.source, encoding="utf-8") as handle:
            raw = json.load(handle)
    else:
        raw = fetch(ENDPOINT)

    catalog = build(raw)
    blob = json.dumps(catalog, separators=(",", ":"), ensure_ascii=False).encode("utf-8")

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    # mtime=0 keeps the archive byte-identical across runs, so builds are reproducible.
    with gzip.GzipFile(args.out, "wb", compresslevel=9, mtime=0) as handle:
        handle.write(blob)

    entries = sum(len(g["ex"]) for g in catalog["games"])
    launchers = sum(1 for g in catalog["games"] for e in g["ex"] if e["l"])
    icons = sum(1 for g in catalog["games"] if g["icon"])
    print("games        %d" % catalog["count"])
    print("executables  %d" % entries)
    print("launchers    %d" % launchers)
    print("icons        %d (%.1f%%)" % (icons, 100.0 * icons / max(1, catalog["count"])))
    print("raw          %.1f MB" % (len(blob) / (1024 * 1024)))
    print("compressed   %.0f KB" % (os.path.getsize(args.out) / 1024))
    print("written      %s" % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
