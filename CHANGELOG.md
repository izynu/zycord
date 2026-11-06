# Changelog

All notable changes to Zycord are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.4] - 2026-11-06

### Fixed

- The README inside the download links only to files that are in it. It pointed
  at `docs/`, `CONTRIBUTING.md` and `SECURITY.md`, none of which the archive
  carries, so thirteen of its fourteen links went nowhere. Repository files are
  now absolute URLs, which render the same on GitHub, and the build fails if a
  shipped document links to something the release does not contain.

### Documentation

- The custom domain procedure described a `CNAME` file and a `SITE` constant
  that do not exist. GitHub Pages builds this site from a workflow, where the
  domain is set in the repository settings and any `CNAME` in the artifact is
  ignored. The procedure now says so, and names every file that carries the
  address.
- The site generator fails if a file that should name the site address still
  names a different one, so a domain change cannot be half applied.
- The contributing guide said the Pages workflow runs on a push to `main`. The
  default branch is `master`. It also now names the checks the build runs, so a
  contributor who trips one knows what failed.

## [1.1.3] - 2026-11-06

### Fixed

- The published archive now carries the same text files as the repository.
  The release workflow runs on Windows, where Git wrote CRLF into the
  checkout, so every text file in the download differed from its source by
  one byte per line. A `.gitattributes` pins LF on every platform, the build
  copies text with LF, and the build fails if a copied file still contains
  CRLF.

### Documentation

- The size of the session stub is given as about 36 KB. The binary is 36,864
  bytes, and several pages said 35 KB.

## [1.1.2] - 2026-11-06

### Fixed

- Stop all on the Sessions page now ends every session. The button was
  present and did nothing.
- History shows how each session ended. The table had no Reason column, and
  the reason was never rendered.
- Auto stop rounds a typed value to the nearest fifteen minutes, so the
  documented step is the step that applies.
- Game names containing control characters are refused in the custom game
  dialog and in the library, not only when a session starts.
- Links on the not-found page resolve from any mistyped path, instead of
  only from the site root.

### Changed

- Statements that the launcher makes no network request were corrected. The
  interface does load game icons from Discord's CDN, and several pages said
  otherwise.
- The claim that nothing is written outside the data folder now notes the log
  export you choose a location for.
- The path rules name all eight extensions the validator accepts, instead of
  describing the rule as .exe only.
- The self test figure for 1.0.0 now matches the checks that release ran.

## [1.1.1] - 2026-11-06

### Added

- The custom game dialog checks the executable name you type against the same
  detectable list the catalog is built from, and says what it finds before you
  add the game. A name Discord lists is accepted and named. A name it does not
  list is accepted with a warning that the session will run but will not appear
  as activity.
- A Not detected tag on custom game cards and in the game details modal when
  the file name is not one Discord lists.

### Changed

- The session window reports whether the file name is one Discord lists,
  instead of claiming detection for every session. The launcher passes the
  answer to the stub, and the stub prints the matching sentence.
- Starting a game whose file name Discord does not list shows a warning once
  the session starts, and the confirmation prompt states the same beforehand.

### Fixed

- Custom games were described as behaving like any other game. Discord credits
  a session only when the running process name is on its detectable list, so a
  custom game with an invented name runs but cannot appear as activity. The
  interface, the changelog and the website now say so.
- The contributing guide and the architecture notes listed a ten input, three
  valid path test and ten site pages. The test has twelve hostile inputs and
  seven valid ones, and the site has thirteen pages.
- The path rules were documented as accepting only `.exe`. The validator
  accepts eight extensions, including the `.bin`, `.dat` and `.jbg` entries
  Discord publishes.

## [1.1.0] - 2026-11-06

### Added

- A Custom game button on the Games page, for a title Discord does not list.
  You give it a name and the executable name, and it behaves like any other
  game: it is searchable, can be saved to the library, starred, started and
  recorded in history. Custom entries carry a Custom tag, have their own
  filter, and can be removed from the catalog, library, favourites and
  recents in one action. Up to 200 entries, with unique names and the same
  path validation a catalog entry gets.
- 18 more games in the catalog, for 10,465 games, 11,171 executable entries
  and 10,452 icons. The additions are the non-`.exe` entries Discord
  publishes, such as `aion.bin`, the Command & Conquer `.dat` and `.game`
  files and the YOU DON'T KNOW JACK `.jbg` builds. Windows starts a program
  under any extension, so they run exactly like the `.exe` entries. Script
  types stay refused.

### Changed

- The release archive uses fixed entry timestamps, so rebuilding the same
  folder produces a byte-identical zip and an identical checksum.

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
  compiler that ships with Windows, runs a 23 check self test against a real
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

[1.1.4]: https://github.com/iZynu/zycord/releases/tag/v1.1.4
[1.1.3]: https://github.com/iZynu/zycord/releases/tag/v1.1.3
[1.1.2]: https://github.com/iZynu/zycord/releases/tag/v1.1.2
[1.1.1]: https://github.com/iZynu/zycord/releases/tag/v1.1.1
[1.1.0]: https://github.com/iZynu/zycord/releases/tag/v1.1.0
[1.0.0]: https://github.com/iZynu/zycord/releases/tag/v1.0.0
