# Contributing to Zycord

Zycord is a small codebase: one C# launcher, one C# stub, one HTML interface, and Python scripts for the build, the catalog, the icons and the site. This guide covers what you need to build it, test it, and get a change accepted.

## Before you start

- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). It explains the process model, the bridge protocol and the safety checks, which most changes touch.
- For anything larger than a bug fix, open a feature request first so the approach can be agreed before you write code.
- Security problems do not go in a public issue. See [SECURITY.md](SECURITY.md).

## Build

You need Windows 10 or 11, 64-bit, and Python 3. The build uses the .NET Framework 4 compiler that ships with Windows. There is no SDK, MSBuild or NuGet step.

```bash
git clone https://github.com/iZynu/zycord.git
cd zycord
python build.py
```

| Command | Effect |
| --- | --- |
| `python build.py` | Build, run the self test, pack `Zycord-1.1.5.zip` and `SHA256.txt`. |
| `python build.py --no-test` | Build and pack without the self test. |
| `python build.py --no-zip` | Leave `dist/` unpacked. |
| `python build.py --clean` | Remove `dist/` first. |

A full build takes a few seconds on a normal machine. The self test adds a few more because it boots WebView2.

The build also refuses to finish if a check fails:

- `check_sources` fails on an identifier from an earlier project.
- `check_line_endings` fails if a copied text file still contains CRLF.
- `check_shipped_links` fails if `README.md` or `LICENSE` links to a file the release folder does not contain. Both ship on their own, without `docs/`, so a link to a repository file has to be an absolute URL.
- `check_download_size` fails if the size in the README download badge is not the size of the archive the build just wrote.
- `check_version_refs` fails if the README download link or version badge names a release other than the one being built.

## Run the self test

The self test is the gate for every change that touches the backend, the interface or the stub.

```bash
python build.py
```

That runs it as part of the build. To run it on its own:

```bash
dist/Zycord.exe --headless
```

It exits with code 0 on success and 1 on failure, prints the report, and writes the same report to `%LOCALAPPDATA%\Zycord\logs\selftest.txt`. A passing run ends with `RESULT: PASS`.

The test covers:

- WebView2 startup and navigation to the interface;
- the presence of the stub, the four interface files and the catalog;
- path validation against twelve hostile inputs and seven valid ones, four of them non-`.exe` entries;
- settings round trip and auto stop clamping;
- library round trip and the favourite toggle;
- a real session start, duplicate start, stop and history entry;
- a session that records a file name Discord does not list;
- a custom game round trip and its removal from every list;
- the log file being written.

It restores every setting and library entry it touches and deletes the game folder it creates. If you add a backend feature, add a check for it to `app/main/SelfTest.cs` rather than testing by hand.

`Zycord.exe --version` prints the version and exits. `Zycord.exe --devtools` enables the WebView2 developer tools for interface work.

## Code style

### C#

The launcher and the stub target .NET Framework 4 and are compiled with `-warn:4`. The build must stay warning-free.

- Four spaces, no tabs. Braces on their own line for types and methods.
- Private fields are `_camelCase`. Public and internal members are `PascalCase`.
- Use `var` only when the type is obvious from the right-hand side.
- Prefer explicit delegates (`delegate { }`) over lambdas in the existing files. The codebase is consistent about this and a mixed style reads badly.
- No LINQ in the hot paths. The catalog is large and the session table is small; plain loops are easier to audit.
- Every public method that takes a path or an id validates it first. The rules are in `Sessions.SafeRelativeExe` and `Bridge.IsAppId`. Do not add a second, weaker validation elsewhere.
- A user-facing error is one sentence, in plain English, returned as the `error` field of a reply. Do not put stack traces in it. Log the detail instead.
- Do not add a dependency. The build references only what is in `vendor/` and what ships with the .NET Framework.

### Renderer JavaScript

The interface is plain ES5 in one file, `app/renderer/app.js`. It is loaded with `<script src="app.js">`, not a module, and it runs inside WebView2.

- ES5 only: `var`, `function`, no arrow functions, no template literals, no `let` or `const`.
- Everything stays inside the existing `(function () { "use strict"; ... })()` wrapper. Do not add globals.
- DOM access goes through the existing `$` and `$$` helpers.
- All backend work goes through `call(cmd, args)`, which returns a Promise. Do not call `window.chrome.webview.postMessage` directly except in the bridge wrapper.
- Build DOM with `document.createElement` and the `el()` helper. Do not build HTML strings from game names, executable paths or log text. Those values come from an untrusted catalog and a log file.
- Respect the content security policy in `index.html`. No inline scripts, no inline styles added at runtime, no external fonts.
- No emoji in the interface, in strings or in comments. Icons are the SVG symbols defined at the top of `index.html`.

### HTML and CSS

- The interface is a single page. Each section is a `<section class="page">` with an id of the form `page-<name>`, and the navigation button carries the same name in `data-page`.
- Use the existing classes and the CSS variables in `app.css`. Do not introduce a second colour system.
- Keep the palette in `app.css`. Do not add a second colour system, do not add decorative colour effects outside the brand tile, and keep animation functional rather than ornamental. The Settings tab has a reduce motion switch, and it must keep working for anything you add.
- Every control that a keyboard user needs must be reachable and labelled. The existing markup uses `aria-label` and `aria-expanded`; follow that.

### Python

- Python 3, standard library only, except Pillow for `brand/make_icon.py`.
- Scripts must run from any working directory. They resolve paths from `__file__`, not from the current directory.
- Write generated files with `newline="\n"` and UTF-8, so the output is identical on every machine.

## Regenerate the icons

All icon sizes, the favicon and the SVG come from one script, so they cannot drift apart. It needs Pillow.

```bash
python brand/make_icon.py
```

It writes `brand/zycord.svg`, the PNG set from 16 to 1024 pixels, `brand/zycord.ico`, `brand/favicon.ico`, a copy of the SVG to `app/renderer/mark.svg`, and `site/favicon.ico`. Commit the regenerated files with the change that required them. Do not hand-edit a generated PNG.

## Regenerate the site

The site header and footer are defined once in `site/build_site.py` and written into every page, so navigation cannot drift.

```bash
python site/build_site.py
```

It writes all thirteen pages, including `404.html`. The Pages workflow runs the same script on a push to `master` and fails if the committed pages differ from the generated ones. It also fails if a file that names the site address still names a different one, which catches a domain change applied in only some of the places it appears. Run it before you commit any change under `site/`.

## Rebuild the catalog

The catalog is built from Discord's public detectable list.

```bash
python scripts/build_catalog.py
```

To build from a saved copy of the endpoint response instead of fetching it:

```bash
python scripts/build_catalog.py path/to/raw.json
```

The script filters to Windows executables, rejects anything that could escape the games folder, and writes `app/assets/catalog.json.gz` with a fixed mtime so the output is reproducible. If the catalog changes, say so in the pull request: it changes which games the launcher can start.

## Commit messages

Use the imperative mood and a short subject line under 72 characters. Add a body when the reason for the change is not obvious from the subject.

```text
Add auto stop extension to the session list

The session window could extend a running session, but the Sessions tab
could not. Both now call the same session.extend command.
```

Prefixes are not required. If you use one, keep it lowercase and a single word: `fix:`, `docs:`, `build:`.

Keep a commit to one change. A pull request that mixes a refactor with a behaviour change is hard to review and will be asked to split.

## Pull requests

- Fill in the pull request template. It asks what changed, how you tested it and what you checked.
- Run `python build.py` and confirm the self test passes before you open the pull request.
- If you change the interface, include a screenshot in the pull request.
- If you change behaviour, update the README, the architecture document or the site page that describes it, whichever applies.
- Keep the diff focused. Reformatting a file you did not otherwise change makes review harder.

## What will not be accepted

These are deliberate boundaries, not a backlog.

- **Anything that contacts Discord.** No gateway connection, no API calls, no self-bot, no token handling, no Rich Presence SDK link. The launcher does not contact Discord's API or gateway, and that stays true. The interface fetching game icons from Discord's CDN is the one exception, and it is documented as such.
- **Anything that reads or modifies the Discord client.** No injection, no patching, no memory reading, no file scraping.
- **Telemetry, analytics or crash reporting of any kind.** There is no server and there will not be one.
- **An auto-updater or a remote catalog fetch.** The catalog is shipped so the application has no remote dependency.
- **Stopping processes by image name.** Sessions stop by process id after confirming the process still points at the file Zycord created. Do not weaken that check.
- **Writing outside the games folder.** Path validation and the second `IsInside` check exist for this. A change that relaxes either will be rejected.
- **Copy that overstates what the tool does.** No unverifiable claims about detection, no promises that an account is safe, no invented statistics.
- **Emoji in the interface, the documentation or commit messages.**
- **Em dashes in prose or tables.** Use a comma, a colon or a full stop.
- **A previous project name or author.** The product is Zycord and the source strings are checked for both.
- **A new dependency without a discussion first.** The build intentionally needs only the Windows compiler, Python and the vendored WebView2 assemblies.

If you are unsure whether an idea crosses one of these lines, open an issue and ask before writing the code.
