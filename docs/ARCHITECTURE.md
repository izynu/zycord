# Architecture

Zycord is two executables and a folder of interface files. This document describes how they fit together, the protocol between them, and the checks that keep a malformed catalog entry from writing a file outside the games folder.

## Process model

`Zycord.exe` is a single .NET Framework 4 WinForms application. Its main window has no caption (`FormBorderStyle.None`) and hosts a WebView2 control that fills the client area. The interface draws its own title bar, navigation and pages in HTML and CSS, so the window chrome is part of the renderer rather than the operating system.

Because the window has no caption, `Shell.cs` implements the parts a caption would normally provide:

- a message filter answers `WM_NCHITTEST` with the eight resize border codes, so the window still resizes from its edges;
- the renderer asks the backend to begin a window drag, which sends `WM_NCLBUTTONDOWN` with `HTCAPTION`;
- DWM is told to use the dark title bar attribute and rounded corners.

Only one launcher runs at a time. A named mutex (`Local\Zycord.SingleInstance.1`) guards startup. A second launch posts a registered window message (`Zycord.ShowWindow.1`) to the running instance, which restores its window, and then exits.

The interface files are served from a virtual host. `SetVirtualHostNameToFolderMapping` maps `zycord.local` to the interface folder with `DenyCors`, and the WebView2 navigates to `https://zycord.local/index.html`. A content security policy in the page allows scripts and styles from the same origin, and images from the same origin, `data:` and `https://cdn.discordapp.com` for game icons.

```text
  +-----------------------------------------------------------+
  |  Zycord.exe (WinForms host)                               |
  |                                                           |
  |   +---------------------------+    +-------------------+  |
  |   | WebView2 (zycord.local)   |    | Shell.cs          |  |
  |   | index.html, app.js, app.css|   | window, tray,     |  |
  |   | catalog.json              |    | 1 second timer    |  |
  |   +-------------+-------------+    +---------+---------+  |
  |                 |  postMessage             |              |
  |                 v                          v              |
  |   +-----------------------------------------------------+  |
  |   | Bridge.cs: {id, cmd, args} -> {id, ok, data, error} |  |
  |   +----+------------------+------------------+----------+  |
  |        |                  |                  |             |
  |        v                  v                  v             |
  |   Sessions.cs        Store.cs            Program.cs        |
  |   paths, pids        settings.json       paths, logs       |
  +--------+------------------+------------------+-------------+
           |                  |                  |
           v                  v                  v
  %LOCALAPPDATA%\Zycord\games   settings.json   logs\zycord-YYYYMMDD.log
           |
           v
     stub.exe (one copy per game, renamed)
           |
           v
     Discord's process scan matches the name
```

## The bridge protocol

The interface never touches the filesystem or the process table. Every operation is a command sent over the WebView2 message channel.

A request is one JSON object:

```json
{ "id": "c17", "cmd": "session.start", "args": { "id": "1091500", "name": "Cyberpunk 2077", "exe": "bin/x64/Cyberpunk2077.exe", "icon": "..." } }
```

A reply carries the same id:

```json
{ "id": "c17", "ok": true, "data": { "session": { }, "already": false } }
```

`ok` is false when the command failed, and `error` then holds a sentence the interface can show. `data` is omitted when there is nothing to return. Every reply is posted exactly once for each id, and the interface times a request out after 20 seconds if no reply arrives.

Push messages are not replies. They carry an `event` name and `data`, and are sent without an id:

| Event | When | Data |
| --- | --- | --- |
| `sessions` | A session starts, stops, or the one second timer notices one ended | The current session list |
| `window` | The window is maximised or restored | `{ "maximised": true or false }` |
| `autostop` | The auto stop timer ended one or more sessions | Array of application ids |
| `navigate` | A tray menu item asks for a page | Page name |

Commands, grouped as `Bridge.cs` dispatches them:

| Command | Purpose |
| --- | --- |
| `boot` | One reply with version, paths, settings, library, favourites, recents and history. Sent once when the interface starts. |
| `settings.save` | Write settings, clamp the auto stop to 0 to 240, apply the theme. |
| `library.add`, `library.remove`, `library.clear` | Change the saved games. |
| `favorite.toggle` | Add or remove a favourite. |
| `session.start`, `session.stop`, `session.stopAll`, `session.list`, `session.extend` | Session lifecycle. |
| `history.list`, `history.clear` | Session history. |
| `storage.stats`, `storage.cleanup` | Disk use and prepared file removal. |
| `log.tail`, `log.export`, `log.clear` | Log file access. |
| `shell.minimise`, `shell.maximise`, `shell.close`, `shell.hide`, `shell.drag`, `shell.state` | Window control. |
| `shell.openFolder`, `shell.openExternal`, `shell.clipboard` | Open a folder, open an http or https link, write the clipboard. |
| `app.reset` | Reset history, library or settings. |

Validation happens in the bridge before a command reaches `Sessions` or `Store`. An application id must be 1 to 25 digits. A game name must be non-empty, at most 200 characters, and free of control characters. A library entry reuses the same executable validation as a session. A malformed message produces an error reply and nothing else.

## The catalog schema

The catalog is built by `scripts/build_catalog.py` from Discord's public detectable applications endpoint and stored as `app/assets/catalog.json.gz`. At build time it is decompressed to `ui/catalog.json` so the interface can fetch it directly.

Schema version 2:

```json
{
  "schema": 2,
  "generated": "2026-11-05",
  "source": "https://discord.com/api/applications/detectable",
  "count": 10447,
  "games": [
    {
      "id": "1091500",
      "name": "Cyberpunk 2077",
      "aliases": [],
      "themes": ["Action", "Role-playing (RPG)"],
      "icon": "abcdef0123456789abcdef0123456789",
      "ex": [
        { "n": "bin/x64/Cyberpunk2077.exe", "l": 0 }
      ]
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `schema` | Catalog format version. The launcher expects 2. |
| `generated` | Date the catalog was built. |
| `source` | The endpoint the raw data came from. |
| `count` | Number of games. Matches the length of `games`. |
| `games[].id` | Discord application id, stored as a string. |
| `games[].name` | Display name. |
| `games[].aliases` | Up to 6 alternate names. |
| `games[].themes` | Up to 4 theme labels. |
| `games[].icon` | The icon hash, or an empty string. The launcher builds `https://cdn.discordapp.com/app-icons/<id>/<hash>.png` from it, so no image data ships. |
| `games[].ex` | Executable entries. |
| `games[].ex[].n` | Executable path relative to the game folder, using forward slashes. |
| `games[].ex[].l` | 1 when Discord marks the entry as a launcher, otherwise 0. |

The current catalog holds 10,465 games, 11,171 executable entries, 20 launcher entries and 10,452 icon hashes. The generator keeps only `win32` entries, rejects names with characters Windows refuses in a filename, rejects absolute paths, `..` segments, entries deeper than 8 segments, and anything whose extension Windows would not start as a program: `.exe`, `.bin`, `.dat`, `.dll`, `.des`, `.jbg`, `.game` and `.pae`. Script types are refused. The gzip stream is written with `mtime=0`, so the same input produces a byte-identical archive.

## Path safety

A catalog entry is untrusted input. `Sessions.SafeRelativeExe` reduces it to a safe relative path or returns null:

- backslashes become forward slashes, and the result is trimmed;
- a leading slash, a NUL, a `..` segment, or a colon is refused;
- empty segments and `.` are dropped;
- any character in `Path.GetInvalidFileNameChars()` is refused;
- the last segment must end in `.exe`, case-insensitively;
- more than 8 segments is refused.

The launcher then builds `Path.Combine(games folder, app id, relative path)` and calls `IsInside` before writing. `IsInside` resolves both paths to full form and requires the candidate to start with the root plus a directory separator. The same check runs again after `Directory.CreateDirectory`, immediately before the stub is copied.

The self test feeds twelve hostile strings through the validator, including `../../windows/system32/cmd.exe`, `C:\Windows\notepad.exe`, `/etc/passwd`, `game//sub/../escape.exe`, `game\..\..\evil.exe` and a UNC path, and requires all ten to be refused. It also requires seven valid paths to be accepted, four of them non-`.exe` entries that Discord publishes.

## The stub lifecycle

`stub.exe` is a separate WinForms executable with no network code and no game code. Its whole behaviour is in `stub/Stub.cs`.

1. The launcher validates the application id and executable entry.
2. It creates `%LOCALAPPDATA%\Zycord\games\<app_id>\<relative path>` and copies `stub.exe` there under the expected filename.
3. It starts that copy with `--title "<game>" --app-id <id> --parent <launcher pid>`, and adds `--hidden` when the session window setting is off.
4. The stub parses its arguments, shows a small window with the game name and a timer, or runs with no window in hidden mode, and waits.
5. Discord's process scan sees the expected name in a plausible location and treats the game as running.
6. On stop, the launcher confirms the process with the recorded id still points at the file it created, then terminates it. If the id has been reused by another program, nothing is killed.

The stub watches its parent. A background thread waits for the launcher process to exit and then closes the session window, or calls `Environment.Exit(0)` in hidden mode. A launcher crash therefore cannot leave a stub running with no way to stop it from the interface. A session with no parent id runs until it is closed.

The stub parses arguments explicitly. It does not search the command line for substrings, so a game name that happens to contain `--hidden` cannot change its behaviour.

## Sessions and history

Running sessions are held in memory, keyed by application id. Each entry records the process id, the file path, the start time, and an optional deadline. A one second timer in the shell calls `Sessions.Tick`, which:

- retires a session whose process has exited on its own;
- terminates a session whose auto stop deadline has passed, then retires it with the reason `auto`.

Retiring a session removes it from the table and writes one history entry, guarded by a flag so a session cannot be recorded twice. History is capped at 250 entries and the recent list at 12.

Starting a game that already has a running session returns the existing session with `already: true` instead of starting a second process. Starting one whose process has died retires the old entry and starts fresh.

At startup, before any session exists, `Sessions.SweepOrphans` deletes every folder under the games directory. That removes copies left by a previous run that ended without cleanup. It runs at a point where it cannot touch a file in use.

`storage.cleanup` deletes the same folders on demand, but refuses while any session is running. Deleting the file a running process is using is the case that check exists to prevent.

## Settings and storage

Settings live in `%LOCALAPPDATA%\Zycord\settings.json` and are serialised with `JavaScriptSerializer`. Saves are atomic: the new contents go to `settings.json.tmp`, the old file is moved to `settings.json.bak`, and the temporary file is moved into place. An interrupted save cannot leave a truncated settings file.

Loading is defensive. A missing, empty or unreadable file falls back to defaults. A file that parses is normalised before use: null lists become empty lists, entries missing an id, name or executable are dropped, the auto stop is clamped to 0 to 240, the theme is forced to `glass` or `light`, the history is capped at 250 and the recents at 12, and the window size is clamped to a usable range.

The data folder layout:

| Path | Contents |
| --- | --- |
| `settings.json` | Preferences, library, favourites, recents, history and window placement. |
| `settings.json.bak` | The previous settings file, kept by the atomic save. |
| `logs\zycord-YYYYMMDD.log` | One file per day, rolled to `.1` when it passes 4 MB. |
| `logs\selftest.txt` | The report from the last `--headless` run. |
| `games\<app_id>\` | Prepared stub copies. |
| `webview\` | The WebView2 profile folder. |

A log line is `YYYY-MM-DD HH:mm:ss.fff LEVEL message`, with the level one of `INFO`, `WARN` or `ERROR`. Session starts and stops log the application id, process id and relative executable. Refused operations log a reason. The log never records a Discord account, token or message.

## The self test

`Zycord.exe --headless` runs the self test and exits with code 0 on success or 1 on failure. It is not a unit test harness. It boots the real application and exercises the layers that touch the disk and the process table.

1. It creates a hidden, transparent form off screen and hosts a real WebView2 control in it.
2. It creates the WebView2 environment, maps the interface folder to `zycord.local`, and navigates to `index.html`.
3. The interface loads, calls `boot`, loads the catalog, and posts a `ready` probe when it has finished. The probe path is a separate message shape (`{ "selftest": "ready" }`) that carries no command.
4. The backend then checks, in order: the stub exists, the four interface files exist, the catalog exists, path validation rejects all hostile inputs and accepts all valid ones, settings round trip and clamp the auto stop, the library round trips and the favourite toggle works, a custom game round trips through storage and its removal clears every reference to it, a real session starts, is tracked, reports `already` on a duplicate start, stops, leaves an empty list and writes one history entry, and the log file is being written.
5. It restores every setting and library entry it touched, deletes the test game folder, writes the report to `%LOCALAPPDATA%\Zycord\logs\selftest.txt`, prints it, and exits.

A 60 second timer fails the run at the stage it reached, so a hang in WebView2 cannot block a build indefinitely.

## The build pipeline

`build.py` needs only the .NET Framework compiler that ships with Windows (`csc.exe` under `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`) and Python 3. The steps run in this order:

1. `check_sources` scans the launcher, renderer, stub and scripts for strings that must not appear in a release, and stops the build if any is found.
2. `build_stub` compiles `stub/Stub.cs` to `dist/stub.exe` with the brand icon, as a 64-bit Windows executable.
3. `build_launcher` compiles the eight files in `app/main` to `dist/Zycord.exe`, referencing `System`, `System.Drawing`, `System.Windows.Forms`, `System.Web.Extensions` and the WebView2 managed assemblies in `vendor/`.
4. `copy_vendor` copies the two WebView2 managed DLLs and `WebView2Loader.dll` into `dist/`.
5. `copy_ui` copies `index.html`, `app.css`, `app.js` and `mark.svg` into `dist/ui`, and decompresses the catalog into `dist/ui/catalog.json`.
6. `copy_docs` copies `LICENSE` and `README.md` into `dist/`.
7. `self_test` runs `dist/Zycord.exe --headless` unless `--no-test` was passed.
8. `report` prints the size and file count of `dist/`.
9. `pack` writes `Zycord-1.1.0.zip` with deflate level 9, then writes `SHA256.txt` with the digest and the archive name.

The release folder is about 3.1 MB. The archive contains one top-level folder named `Zycord-1.1.0`, and every entry carries a fixed timestamp, so rebuilding the same `dist/` produces a byte-identical archive and an identical checksum.

## What the design deliberately leaves out

There is no updater, no network client, no analytics and no crash reporter. The catalog is shipped rather than fetched. Those are choices, not gaps: each one would add a dependency the launcher does not need, and each one would be a way for the application to behave differently from what is in this repository.
