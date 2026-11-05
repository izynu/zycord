# Changelog

All notable changes to Zycord are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-05

First public release.

### Launcher

- Borderless window with a native title bar, minimise, maximise and close.
- Interface rendered in Microsoft Edge WebView2 from files shipped in the same
  folder. No installer, no background service, no account, no server.
- Ten sections: Home, Games, Library, Favourites, Sessions, History, Settings,
  Logs, About and Help.
- Light and dark themes, a reduced motion setting, and tray behaviour that
  keeps sessions running when the window closes.
- Settings, library, favourites, history and logs stored in
  `%LOCALAPPDATA%\Zycord`. Nothing leaves the machine.

### Catalog

- 10,447 games and 11,151 executable entries, built from Discord's public
  detectable list and filtered to Windows executables.
- 20 launcher entries and 10,434 game icons.
- Search by name, alias or theme, with exact matches ranked first, then
  prefixes, then word matches.
- Filters for single executable, multiple executables and games that ship a
  launcher.

### Sessions

- A small session process, about 35 KB, copied into
  `%LOCALAPPDATA%\Zycord\games\<app_id>\` under the executable name Discord
  expects, then started.
- The process holds no game code and makes no network calls. It shows a window
  with the elapsed time and an End session button, or runs without a window.
- It watches the launcher's process id and exits if the launcher disappears, so
  a crash cannot leave a session running with no way to stop it.
- Live timer, optional auto stop in minutes, extension by fifteen minutes, and
  duplicate start protection.
- Every session is written to history with its start, stop, duration and end
  reason.

### Packaging

- `python build.py` compiles the launcher and the stub with the .NET Framework
  compiler that ships with Windows, runs a 25 check self test against a real
  WebView2 host, and writes `Zycord-1.0.0.zip` with `SHA256.txt`.
- Release folder is about 3.1 MB in 12 files, including the WebView2
  assemblies.

### Security

- Every path that comes from the catalog or from user input is validated before
  it is used. The self test feeds ten hostile inputs through the validator and
  requires all ten to be rejected.
- The interface runs under a content security policy that allows only local
  scripts and styles, and images from the Discord icon CDN.
- A build time check fails the build if any identifier from an earlier project
  appears in the source.

[1.0.0]: https://github.com/iZynu/zycord/releases/tag/v1.0.0
