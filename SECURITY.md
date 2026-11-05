# Security policy

## Supported versions

| Version | Supported |
| --- | --- |
| 1.0.x | Yes |
| Anything earlier | No |

Only the current release receives fixes. Zycord does not update itself, so if you are on an older version, the fix is to download the current release or build from source.

## Reporting a vulnerability

Report it privately through GitHub's private vulnerability reporting:

1. Open [github.com/iZynu/zycord/security/advisories/new](https://github.com/iZynu/zycord/security/advisories/new).
2. Describe the problem, the version, and the steps to reproduce it.
3. Include a proof of concept if you have one, and say whether you want credit.

**Do not open a public issue for a security problem.** A public report tells everyone about the flaw before a fix exists.

You can expect an acknowledgement within a few days. This is a small project maintained by one person, so a fix may take longer than that. Please give a reasonable window before disclosing publicly, and coordinate the timing with the maintainer.

## What is in scope

The code in this repository, as shipped in a release or built from source:

- **Path handling.** A catalog entry, a game name or an executable path that causes a file to be written or read outside `%LOCALAPPDATA%\Zycord\games`, or outside the intended file.
- **Process handling.** A way to make Zycord terminate a process it did not start, or to stop a process without the check that it still points at the file Zycord created.
- **The bridge.** A message from the interface that reaches a privileged operation without validation, or a command that can be made to do something the interface cannot normally do.
- **The stub.** Anything in `stub/Stub.cs` that goes beyond holding a process name, showing a timer, and watching its parent.
- **Settings and logs.** A crafted `settings.json` or log file that causes code execution, a path traversal, or a crash on startup.
- **The build.** A way to make `build.py` produce a release that differs from the source, or to make the rebrand check pass while a forbidden string is present.
- **The catalog generator.** A way to make `scripts/build_catalog.py` emit an entry that the launcher's validator would accept but that escapes the games folder.

## What is out of scope

These are not vulnerabilities in Zycord, and reports about them will be closed:

- **Discord account actions.** Whether Discord removes quest progress, restricts an account or terminates it. That is Discord's decision, it is described in the terms, and no code change can prevent it.
- **Quest credit decisions.** Whether a particular quest advances, how long it takes, or whether a game is detected at all. That is Discord's detection behaviour, not Zycord's.
- **Discord's own software.** Vulnerabilities in the Discord client, its installer, its updater or its servers belong to Discord.
- **Microsoft's software.** WebView2, the .NET Framework and Windows itself are outside this project.
- **SmartScreen and antivirus warnings.** The executable is unsigned, so Windows cannot verify a publisher. That is a known and documented consequence, not a vulnerability. Building from source is the answer.
- **A missing WebView2 Runtime.** The launcher reports it and stops. That is the intended behaviour.
- **The catalog being out of date.** It is shipped with each release by design. Games added after a release appear in the next one.
- **The absence of code signing, an auto-updater or telemetry.** These are deliberate design choices, described in the architecture document.

## The stub's actual behaviour

The stub is the part most likely to attract suspicion, so here is exactly what it does. The full source is `stub/Stub.cs` and it is short enough to read in one sitting.

- It parses four arguments: `--title`, `--app-id`, `--parent` and `--hidden` (or `--tray`). It does not scan the command line for substrings.
- It shows a small window with the game name, a timer and an End session button, or runs with no window in hidden mode.
- It waits for its parent process to exit and then closes, so a launcher crash cannot leave it running.
- It contains no game code, no network code, no Discord SDK, and no code that reads another process.
- It never contacts Discord. Discord's own client notices the process name; the stub does not talk to anything.

It does not inject into a process, patch a file, install a service, write to the registry, or read credentials. If you find code in the stub that does any of those, that is a security report and it is in scope.

## A note on the mechanism

Zycord starts a process named after a game's executable so that Discord's process scan treats the game as running. That is the whole mechanism, and it is not a security boundary. It is a use Discord has not documented or endorsed, and the account risk is set out in [docs/TERMS.md](docs/TERMS.md). A report that the mechanism works as described is not a vulnerability.
