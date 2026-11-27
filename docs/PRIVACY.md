# Privacy policy

Last updated 27 November 2026

This policy covers two things: the Zycord desktop application, and the Zycord website at [izynu.github.io/zycord](https://izynu.github.io/zycord/). They are treated separately because they behave differently.

This page mirrors the website policy at [izynu.github.io/zycord/privacy.html](https://izynu.github.io/zycord/privacy.html). If the two ever differ, the website version is the current one.

## The Zycord application

### What it collects

Nothing. Zycord has no analytics, no crash reporting and no server. It does not transmit any data about you or your sessions anywhere. The one network request it can make is described under [Update check](#update-check).

### What it stores on your computer

| Item | Contents |
| --- | --- |
| `settings.json` | Your preferences, the games you saved to the library, and the version last announced by the update check. Contains game names, application ids and executable paths. No personal information. |
| `logs/` | One text file per day recording session starts and stops, process ids, refused operations and errors. It does not record your Discord account, token or messages. |
| `games/` | Copies of the session stub, renamed for each game you started. |
| `webview/` | A standard Microsoft Edge WebView2 profile folder. It is used to render the interface and is not read by Zycord. |

All of it lives in `%LOCALAPPDATA%\Zycord`. Deleting that folder removes everything the application has stored.

Settings are written atomically, so an interrupted save cannot leave a truncated file. A previous copy is kept as `settings.json.bak`.

### What it does not access

- Discord's installation, files, memory or credentials.
- Your browser history, documents, or any file outside its own data folder, apart from a log you export yourself.
- Your network, microphone, camera or location.

### Network access

The application makes two kinds of network request, and nothing else. The game catalog is stored with the application rather than downloaded, and nothing contacts Discord's API or gateway.

**Game icons.** Icons shown in the interface are loaded from `cdn.discordapp.com` by the WebView2 renderer, using only the icon hash stored in the catalog. That request fetches a public image and sends no information about you or your sessions.

**Update check.** When it is enabled, the launcher sends one HTTPS GET to `api.github.com` for the public release list of `iZynu/zycord`. It reads the newest version number, compares it with the build that is running, and shows a prompt when a newer release exists. The request carries no identifier, no account, no settings and no session data; the reply is discarded once the version number has been read. The check runs at most once per launch and stops entirely when **Check for updates** is turned off in Settings. GitHub necessarily sees the request as it would any web request, under [GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-privacy-statement); this project receives no log of it.

### Update check

The update check exists so a fix can reach you without you watching the repository. It is on by default because it sends nothing about you, and it is one switch away from off.

- **What is sent:** an HTTP GET for a fixed address, with the version of Zycord in the `User-Agent` header. Nothing else. No machine id, no installation id, no Discord account, no game names, no session history.
- **What is kept:** the newest version number, and the version you were last told about, so the same release is not announced on every launch. Both are ordinary settings in `settings.json`.
- **What happens on a new release:** a dialog names the version, links to the archive on the release page, and links to the release notes. Zycord does not download or install anything itself; the link opens in your browser.
- **To turn it off:** Settings, **Check for updates**. No request is made from then on. The About tab keeps a manual check, which you trigger yourself.

### Discord

Zycord has no account, no sign-in and no token. It does not connect to Discord's gateway, does not read or modify the Discord client, and does not send anything to Discord. It starts a local process with a particular file name, and Discord's own client does the detecting.

## This website

### Hosting

The site is served by GitHub Pages. GitHub may record standard web server information such as the requesting IP address, the page requested and the time, as described in GitHub's own privacy statement. That logging is done by GitHub, not by this project, and this project has no access to it.

### Cookies and analytics

The site sets no cookies. It uses no analytics, no advertising, no tracking pixels and no third-party embeds. There is no consent banner because there is nothing to consent to.

### External links

Links to GitHub leave this site and are governed by GitHub's policies. The project does not control what those pages collect.

## Children

Zycord is not directed at children. Discord requires users to be at least 13 years old, and Zycord is only useful alongside a Discord account.

## Changes to this policy

If this policy changes, the updated version appears on this page with a new date. Because the application collects nothing, changes are unlikely to affect you in practice.

## Contact

Questions about privacy can be raised at [github.com/iZynu/zycord/issues](https://github.com/iZynu/zycord/issues).
