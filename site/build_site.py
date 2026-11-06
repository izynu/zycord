#!/usr/bin/env python3
"""Generate the Zycord website.

Every page shares one head, one header and one footer, so navigation and
metadata cannot drift. The generator is deterministic: the same inputs
produce byte-identical files. Run from anywhere:

    python site/build_site.py

Pages written: index, features, how-it-works, screenshots, download, docs,
faq, safety, changelog, roadmap, privacy, terms and 404.
"""

import gzip
import json
import os
import re

SITE = os.path.dirname(os.path.abspath(__file__))

REPO = "https://github.com/iZynu/zycord"
SITE_URL = "https://izynu.github.io/zycord"
VERSION = "1.1.2"


def catalog_figures():
    """Read the counts the site quotes straight from the shipped catalog.

    The pages say the figures come from the catalog in the release, so they are
    read from it rather than typed. A stale catalog fails the Pages workflow,
    which regenerates the site and requires the result to match what is
    committed.
    """
    path = os.path.join(os.path.dirname(SITE), "app", "assets", "catalog.json.gz")
    with gzip.open(path, "rt", encoding="utf-8") as handle:
        data = json.load(handle)
    games = data.get("games") or []
    executables = sum(len(g.get("ex") or []) for g in games)
    launchers = sum(1 for g in games for e in (g.get("ex") or []) if e.get("l"))
    icons = sum(1 for g in games if g.get("icon"))
    return len(games), executables, launchers, icons


_GAMES, _EXECUTABLES, _LAUNCHERS, _ICONS = catalog_figures()
GAMES = format(_GAMES, ",")
EXECUTABLES = format(_EXECUTABLES, ",")
LAUNCHERS = format(_LAUNCHERS, ",")
ICONS = format(_ICONS, ",")
STUB_SIZE = "about 35 KB"
RELEASE_SIZE = "about 3.1 MB"
DOWNLOAD_SIZE = "0.95 MB"

NAV = [
    ("index.html", "Overview"),
    ("features.html", "Features"),
    ("how-it-works.html", "How it works"),
    ("screenshots.html", "Screenshots"),
    ("download.html", "Download"),
    ("docs.html", "Docs"),
    ("faq.html", "FAQ"),
    ("safety.html", "Safety"),
    ("changelog.html", "Changelog"),
    ("roadmap.html", "Roadmap"),
]

# Footer columns. Every page appears at least once.
FOOT_PRODUCT = [
    ("features.html", "Features"),
    ("how-it-works.html", "How it works"),
    ("screenshots.html", "Screenshots"),
    ("roadmap.html", "Roadmap"),
]
FOOT_SUPPORT = [
    ("download.html", "Download"),
    ("docs.html", "Documentation"),
    ("faq.html", "FAQ"),
    ("safety.html", "Safety"),
    ("changelog.html", "Changelog"),
]
FOOT_LEGAL = [
    ("privacy.html", "Privacy policy"),
    ("terms.html", "Terms and conditions"),
    (REPO + "/blob/HEAD/LICENSE", "MIT licence"),
]

PAGES = [
    "index.html",
    "features.html",
    "how-it-works.html",
    "screenshots.html",
    "download.html",
    "docs.html",
    "faq.html",
    "safety.html",
    "changelog.html",
    "roadmap.html",
    "privacy.html",
    "terms.html",
    "404.html",
]


def esc(text):
    """Escape text for HTML. Only &, <, > and quotes need it."""
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


# GitHub Pages serves 404.html for any unmatched path, at any depth, so a
# document-relative link on that page resolves against the path that was
# mistyped. Every other page is only ever served at its own name.
RELATIVE_ATTR = re.compile(r'(?<= )(?P<attr>(?:href|src))="(?P<url>(?!https?:|mailto:|#|//)[^"]*)"')


def absolutise(html):
    """Rewrite document-relative href and src values to absolute site URLs."""
    return RELATIVE_ATTR.sub(
        lambda m: '%s="%s/%s"' % (m.group("attr"), SITE_URL, m.group("url")), html
    )


def head(title, desc, active, canonical, robots=None):
    links = "\n".join(
        '          <a href="%s"%s>%s</a>'
        % (href, ' class="is-active" aria-current="page"' if href == active else "", label)
        for href, label in NAV
    )
    robots_tag = '\n<meta name="robots" content="%s">' % robots if robots else ""
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title}</title>
<meta name="description" content="{desc}">
<link rel="canonical" href="{canonical}">
<meta name="theme-color" content="#0c0c0e">
<meta name="color-scheme" content="dark">{robots_tag}
<link rel="icon" href="favicon.ico" sizes="any">
<link rel="icon" href="assets/zycord.svg" type="image/svg+xml">
<link rel="apple-touch-icon" href="assets/mark-256.png">
<link rel="stylesheet" href="assets/site.css">
<meta property="og:type" content="website">
<meta property="og:site_name" content="Zycord">
<meta property="og:title" content="{title}">
<meta property="og:description" content="{desc}">
<meta property="og:url" content="{canonical}">
<meta property="og:image" content="{SITE_URL}/assets/mark-512.png">
<meta name="twitter:card" content="summary">
<meta name="twitter:title" content="{title}">
<meta name="twitter:description" content="{desc}">
</head>
<body>
<a class="skip" href="#main">Skip to content</a>

<header class="site-head">
  <div class="wrap">
    <a class="logo" href="index.html">
      <img src="assets/zycord.svg" alt="" width="30" height="30">
      <span class="logo-text">
        <span class="logo-name">Zycord</span>
      </span>
    </a>
    <button class="nav-toggle" id="nav-toggle" aria-expanded="false" aria-controls="site-nav">Menu</button>
    <nav class="site-nav" id="site-nav" aria-label="Main">
{links}
    </nav>
  </div>
</header>
"""


def foot():
    def column(title, items):
        rows = "\n".join(
            '        <a href="%s"%s>%s</a>'
            % (href, ' rel="noopener"' if href.startswith("http") else "", label)
            for href, label in items
        )
        return f'      <div class="foot-col">\n        <h4>{title}</h4>\n{rows}\n      </div>'

    return f"""
<footer class="site-foot">
  <div class="wrap">
    <div class="foot-grid">
      <div class="foot-col foot-brand">
        <a class="logo" href="index.html">
          <img src="assets/zycord.svg" alt="" width="28" height="28">
          <span class="logo-text">
            <span class="logo-name">Zycord</span>
          </span>
        </a>
        <p>A Windows launcher that starts a small session process for a game Discord already detects. Local only, no account, no telemetry.</p>
      </div>
{column("Product", FOOT_PRODUCT)}
{column("Support", FOOT_SUPPORT)}
{column("Legal", FOOT_LEGAL)}
      <div class="foot-col">
        <h4>Source</h4>
        <a href="{REPO}" rel="noopener">GitHub repository</a>
        <a href="{REPO}/releases" rel="noopener">Releases</a>
        <a href="{REPO}/issues" rel="noopener">Report an issue</a>
      </div>
    </div>
    <div class="foot-legal">
      <span>Zycord is an independent project. It is not affiliated with, endorsed by, or sponsored by Discord Inc.</span>
      <span>MIT licensed.</span>
    </div>
  </div>
</footer>

<script src="assets/site.js"></script>
</body>
</html>
"""


def write(filename, title, desc, body, active=None, robots=None, absolute=False):
    active = active or filename
    canonical = SITE_URL + "/" + ("" if filename == "index.html" else filename)
    html = (
        head(title, desc, active, canonical, robots)
        + '\n<main id="main">\n'
        + body
        + "\n</main>\n"
        + foot()
    )
    if absolute:
        html = absolutise(html)
    path = os.path.join(SITE, filename)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(html)
    print("wrote %-20s %6d bytes" % (filename, len(html.encode("utf-8"))))


def doc(filename, title, desc, updated, body):
    """A text page inside the standard document panel."""
    wrapped = (
        '<div class="wrap">\n  <article class="doc">\n'
        f"    <h1>{title}</h1>\n"
        f'    <p class="updated">{updated}</p>\n'
        + body
        + "\n  </article>\n</div>\n"
    )
    write(filename, title + " | Zycord", desc, wrapped)


def page(filename, title, desc, body, active=None):
    """A marketing page: page head, then free sections."""
    write(filename, title + " | Zycord", desc, body, active)


def section(title, intro, inner, cls="section"):
    sub = f"\n    <p>{intro}</p>" if intro else ""
    return f"""
<section class="{cls}">
  <div class="wrap">
    <div class="section-head">
      <h2>{title}</h2>{sub}
    </div>
{inner}
  </div>
</section>"""


def card(kicker, title, text):
    k = f'\n      <span class="card-kicker">{kicker}</span>' if kicker else ""
    return f"""    <div class="card">{k}
      <h3>{title}</h3>
      <p>{text}</p>
    </div>"""


def cards(items, cls="grid"):
    return f'  <div class="{cls}">\n' + "\n".join(card(*i) for i in items) + "\n  </div>"


def step(n, title, text):
    return f"""    <div class="step">
      <div class="step-n">{n}</div>
      <div>
        <h3>{title}</h3>
        <p>{text}</p>
      </div>
    </div>"""


def steps(items):
    return '  <div class="steps">\n' + "\n".join(step(*i) for i in items) + "\n  </div>"


def spec(rows):
    body = "\n".join(
        "      <tr><th>%s</th><td>%s</td></tr>" % (k, v) for k, v in rows
    )
    return f'  <table class="spec">\n{body}\n  </table>'


def notice(title, text, warn=False):
    cls = "notice notice-warn" if warn else "notice"
    return (
        f'  <div class="{cls}">\n'
        f'    <span class="notice-title">{title}</span>\n'
        f"    {text}\n"
        f"  </div>"
    )


def cta(title, text, primary, secondary=None):
    second = (
        f'\n      <a class="btn btn-secondary" href="{secondary[0]}">{secondary[1]}</a>'
        if secondary
        else ""
    )
    return f"""
<section class="section">
  <div class="wrap">
    <div class="cta">
      <h2>{title}</h2>
      <p>{text}</p>
      <div class="hero-actions">
        <a class="btn btn-primary" href="{primary[0]}">{primary[1]}</a>{second}
      </div>
    </div>
  </div>
</section>"""


def page_head(eyebrow, title, lead):
    return f"""
<section class="page-head">
  <div class="wrap">
    <p class="eyebrow">{eyebrow}</p>
    <h1>{title}</h1>
    <p class="lead">{lead}</p>
  </div>
</section>"""


STATS = f"""
  <div class="stat-row">
    <div class="stat"><span class="stat-n">{GAMES}</span><span class="stat-l">games in the catalog</span></div>
    <div class="stat"><span class="stat-n">{EXECUTABLES}</span><span class="stat-l">executable entries</span></div>
    <div class="stat"><span class="stat-n">{LAUNCHERS}</span><span class="stat-l">launcher processes marked</span></div>
    <div class="stat"><span class="stat-n">{ICONS}</span><span class="stat-l">games with an icon</span></div>
  </div>"""

# --------------------------------------------------------------------- index

INDEX = (
    """
<section class="hero">
  <div class="wrap">
    <div>
      <p class="eyebrow">Windows launcher for Discord quests</p>
      <h1>A game process Discord already recognises, started in one click.</h1>
      <p class="hero-lede">
        Zycord copies a small stub executable into
        <code class="inline">%LOCALAPPDATA%\\Zycord\\games\\&lt;app_id&gt;\\</code>,
        renames it to the executable Discord watches for a game, and starts it.
        Discord credits PLAY_ON_DESKTOP quest progress while the stub runs. The
        stub has no game code, no network code and never contacts Discord.
      </p>
      <div class="hero-actions">
        <a class="btn btn-primary" href="download.html">Download for Windows</a>
        <a class="btn btn-secondary" href="how-it-works.html">How the stub works</a>
      </div>
      <p class="hero-note">Windows 10 and 11, 64-bit. No installer, no account, no telemetry. Version """
    + VERSION
    + """.</p>
    </div>
    <div class="hero-visual">
      <span class="hero-orbit" aria-hidden="true"></span>
      <img class="hero-mark" src="assets/mark-512.png" alt="The Zycord mark: a light Z on a dark rounded square" width="512" height="512">
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
"""
    + STATS
    + """
    <p class="muted" style="margin-top:14px;font-size:12.5px">
      Counts are read from the catalog shipped with version """
    + VERSION
    + """. """
    + LAUNCHERS
    + """ of the executable entries are marked as launcher processes.
    </p>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>What Zycord does</h2>
      <p>Five things, each with a clear boundary.</p>
    </div>
"""
    + cards(
        [
            ("01", "Searches the catalog", "10,465 games with 11,171 executable entries, searchable by name, alias and theme. Filters narrow to single-executable, multi-executable, launcher or custom games, and any title you add yourself is searchable beside them."),
            ("02", "Prepares a session file", "Copies the " + STUB_SIZE + " stub into a per-game folder and gives it the filename Discord expects, including any subfolder the catalog records."),
            ("03", "Starts and tracks it", "Starts the process and records its process id. The Sessions tab shows a live timer, and an optional auto stop ends it for you."),
            ("04", "Keeps a local history", "Every session is recorded with its start, stop, duration and reason. Favourites and a saved library sit beside it in one settings file."),
            ("05", "Cleans up", "One action removes every prepared game folder. It refuses while a session is running, so nothing is deleted while in use."),
        ]
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Start a session in four steps</h2>
      <p>From opening the launcher to a running session takes a few seconds.</p>
    </div>
"""
    + steps(
        [
            ("1", "Open Games", "The catalog loads with the application, so search works immediately. Type a name, pick a theme, or filter by executable count."),
            ("2", "Choose the executable", "Most games register one executable. Where a game registers several, each is listed and launcher processes are marked."),
            ("3", "Start", "Zycord prepares the file, starts the process and records its process id. A small window shows elapsed time unless you turn it off."),
            ("4", "Stop when done", "Stop from the Sessions tab, the session window or the tray menu. A typical PLAY_ON_DESKTOP quest needs about 15 minutes."),
        ]
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>What the launcher includes</h2>
      <p>Ten sections, all local, all reachable from the left rail.</p>
    </div>
"""
    + cards(
        [
            ("", "Home", "Running sessions, library count, catalog count and prepared file size at a glance, plus recent games and quick actions."),
            ("", "Games", "The full catalog with search, theme filter, executable filter and three sort orders."),
            ("", "Library", "Saved games with the executable you chose, ready to start again."),
            ("", "Favourites", "Starred games kept separately from the library, with their own count in the rail."),
            ("", "Sessions", "Every running session with its name, executable, elapsed time, auto stop remaining and an extend action."),
            ("", "History", "Up to 250 recorded sessions with start, stop, duration and whether they were stopped, exited or auto stopped."),
            ("", "Settings", "Session, window, storage and data controls, including theme, tray behaviour and a 0 to 240 minute auto stop."),
            ("", "Logs", "A per-day plain text log you can refresh, copy, export or clear. Nothing leaves the computer."),
            ("", "About", "Version, build facts and buttons for the data folder, the website and the repository."),
            ("", "Help", "A short guide to starting a session and answers to the questions that come up most."),
        ]
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>What it does not do</h2>
          <p>Deliberate omissions, not features waiting to be added.</p>
        </div>
        <ul class="muted">
          <li>No sign-in, token or stored credential.</li>
          <li>No patch, injection or read of the Discord client.</li>
          <li>No network request from the launcher except Discord CDN icons.</li>
          <li>No telemetry, analytics, crash reporting or update check.</li>
          <li>No installer, background service or scheduled task.</li>
        </ul>
        <p style="margin-top:14px"><a href="safety.html">Read the safety page</a> for the full boundary.</p>
      </div>
      <div>
        <div class="section-head">
          <h2>Requirements</h2>
        </div>
"""
    + spec(
        [
            ("Operating system", "Windows 10 or Windows 11, 64-bit."),
            ("Runtime", "Microsoft Edge WebView2 Runtime, included with Windows 11 and current Windows 10 updates."),
            ("Discord", "The desktop application running on the same computer."),
            ("Disk", RELEASE_SIZE + " unpacked, plus " + STUB_SIZE + " per prepared game."),
            ("Privileges", "None. Zycord runs as your user and never asks for elevation."),
        ]
    )
    + """
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Before you use it</h2>
    </div>
"""
    + notice(
        "Discord has not documented or endorsed this use.",
        "Progress gained this way can be removed and accounts can be actioned. Zycord is built carefully, but it does not make the practice safe. Read the <a href=\"terms.html\">terms</a> before you start a session.",
        warn=True,
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>Common questions</h2>
        </div>
        <div class="faq">
          <details open>
            <summary>Do I need the game installed?</summary>
            <div class="answer"><p>No. Zycord starts a copy of its own stub with the game's executable name. Your real game folder is never touched.</p></div>
          </details>
          <details>
            <summary>Is anything sent to Discord?</summary>
            <div class="answer"><p>No. The launcher starts a local process. Discord's own client does the detection, and the stub makes no network call.</p></div>
          </details>
          <details>
            <summary>How long does a quest take?</summary>
            <div class="answer"><p>A typical PLAY_ON_DESKTOP quest needs about 15 minutes of the game appearing to run. Leave the session running and Discord credits it.</p></div>
          </details>
          <details>
            <summary>Why does SmartScreen warn me?</summary>
            <div class="answer"><p>The executable is not signed with a code-signing certificate. Choose More info and then Run anyway, or build from source.</p></div>
          </details>
        </div>
        <p style="margin-top:14px"><a href="faq.html">All questions</a></p>
      </div>
      <div>
        <div class="section-head">
          <h2>Latest release</h2>
        </div>
        <div class="road">
          <div class="road-item">
            <span class="road-dot is-shipped" aria-hidden="true"></span>
            <div>
              <h3>"""
    + VERSION
    + """ <span class="badge badge-live">Current</span></h3>
              <p>WebView2 interface, process-id session tracking, a validated path pipeline, auto stop, history, favourites and a rewritten stub.</p>
            </div>
            <span class="road-when">released</span>
          </div>
          <div class="road-item">
            <span class="road-dot is-next" aria-hidden="true"></span>
            <div>
              <h3>Catalog refresh</h3>
              <p>Regenerate the catalog from Discord's public detectable list and ship it in a new build.</p>
            </div>
            <span class="road-when">next</span>
          </div>
          <div class="road-item">
            <span class="road-dot is-later" aria-hidden="true"></span>
            <div>
              <h3>Portable settings export</h3>
              <p>Move your library and favourites between machines without copying the whole data folder.</p>
            </div>
            <span class="road-when">later</span>
          </div>
        </div>
        <p style="margin-top:14px"><a href="changelog.html">Changelog</a> and <a href="roadmap.html">roadmap</a></p>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Every page</h2>
    </div>
    <div class="grid-3">
      <a class="card" href="features.html"><h3>Features</h3><p>Every control in the launcher, described.</p></a>
      <a class="card" href="how-it-works.html"><h3>How it works</h3><p>Detection, path safety, process safety.</p></a>
      <a class="card" href="screenshots.html"><h3>Screenshots</h3><p>The Games, Sessions and Settings sections.</p></a>
      <a class="card" href="download.html"><h3>Download</h3><p>Version """
    + VERSION
    + """, checksum and requirements.</p></a>
      <a class="card" href="docs.html"><h3>Documentation</h3><p>Using, building and troubleshooting.</p></a>
      <a class="card" href="faq.html"><h3>FAQ</h3><p>Straight answers, including the uncomfortable ones.</p></a>
      <a class="card" href="safety.html"><h3>Safety</h3><p>What the stub does, and what it never does.</p></a>
      <a class="card" href="changelog.html"><h3>Changelog</h3><p>What changed in """
    + VERSION
    + """.</p></a>
      <a class="card" href="roadmap.html"><h3>Roadmap</h3><p>Shipped, next and later.</p></a>
      <a class="card" href="privacy.html"><h3>Privacy</h3><p>Local-only data and no telemetry.</p></a>
      <a class="card" href="terms.html"><h3>Terms</h3><p>Acceptable use and the warranty position.</p></a>
    </div>
  </div>
</section>
"""
)

write(
    "index.html",
    "Zycord | A Windows launcher for Discord quest progress",
    "Zycord starts a small local stub under a game's executable name so Discord credits PLAY_ON_DESKTOP quests. "
    + GAMES
    + " games, local only, no account, no telemetry.",
    INDEX,
)

# ------------------------------------------------------------------ features

FEATURES = (
    page_head(
        "Features",
        "Everything in the launcher, and what each control actually does",
        "Zycord "
        + VERSION
        + " is one window with ten sections. Every feature below exists in this release; nothing here is planned or partial.",
    )
    + section(
        "Catalog and search",
        "A fixed catalog shipped with the application, searched locally.",
        cards(
            [
                ("Catalog", GAMES + " games", "Generated from Discord's public detectable list at build time. " + EXECUTABLES + " executable entries, " + ICONS + " games with an icon."),
                ("Search", "Name and alias", "Type any part of a game name or one of its registered aliases. Matching is a case-insensitive substring check, so partial words work."),
                ("Filters", "Theme and executable count", "Filter by any theme in the catalog, then to games with a single executable, multiple executables, or a marked launcher process."),
                ("Sort", "Three orders", "Name A to Z, name Z to A, or most executables first. The catalog opens sorted by name."),
                ("Icons", "Discord CDN, with fallback", "Icons load from Discord's icon CDN. If one fails, a lettered tile is shown instead, so the grid never breaks."),
                ("Paging", "120 at a time", "Results render in pages of 120 with a load more action, which keeps a large result set responsive."),
            ]
        ),
    )
    + section(
        "Sessions",
        "One process per game, tracked by id from start to stop.",
        cards(
            [
                ("Start", "One click", "Pick a game and an executable, then start. Zycord prepares the file, starts the process and records its process id."),
                ("Timer", "Live elapsed time", "The Sessions tab and the session window both show elapsed time, updated while the session runs."),
                ("Auto stop", "1 to 240 minutes", "Set in steps of 15 minutes. Zero disables it. A running session can be extended by 15 minutes from the session list."),
                ("Stop", "One or all", "Stop a single session, or stop all with a confirmation step if you enabled it in Settings."),
                ("Multiple", "Independent sessions", "Run several games at once. Each session has its own file, process id and timer."),
                ("Lifetime", "Tied to the launcher", "Each stub watches the launcher that started it and exits if that launcher disappears, so a crash cannot leave an unstoppable session."),
            ]
        ),
    )
    + section(
        "Library, favourites and history",
        "Local records that survive restarts.",
        cards(
            [
                ("Library", "Saved games", "Saving a game records the game and the executable you chose. Start or remove it from the Library tab."),
                ("Favourites", "A separate shortlist", "Star any game to keep it in Favourites with its own count in the rail, independent of the library."),
                ("Recent", "The last 12", "Recently started games appear on Home for a quick return, capped at 12 entries."),
                ("History", "Up to 250 sessions", "Each entry records start, stop, duration and reason: stopped, exited on its own, or auto stopped."),
                ("Clearing", "Separate controls", "History and recents, the library and favourites, and settings each have their own clear action."),
                ("Persistence", "One settings file", "Library, favourites, recents, history, preferences and window bounds live in one JSON document."),
            ]
        ),
    )
    + section(
        "Window, tray and appearance",
        "A normal desktop application, with the options you would expect.",
        cards(
            [
                ("Tray", "Minimise to tray", "Closing the window can keep Zycord and its sessions running. Turn it off and closing the window stops every session."),
                ("Startup", "Start minimised", "Open straight into the tray when you want the launcher out of the way."),
                ("Always on top", "Keep on top", "Leave the launcher above other windows, useful while watching a timer."),
                ("Themes", "Glass and light", "The default graphite glass finish, or a light theme. The choice is stored with your settings."),
                ("Motion", "Reduce motion", "Turns off the background animation in the launcher."),
                ("Single instance", "One window", "Launching Zycord again brings the existing window forward instead of opening a second copy."),
            ]
        ),
    )
    + section(
        "Storage and diagnostics",
        "Everything the application writes, visible and removable.",
        cards(
            [
                ("Storage", "Size readout", "Settings shows prepared game file size and log size, with a button to open the data folder."),
                ("Cleanup", "Clear game files", "Removes every prepared folder. It refuses while a session is running, because a running process is using one of those files."),
                ("Logs", "One file per day", "Plain text with session starts and stops, process ids, refused operations and errors. A file rolls over at 4 MB."),
                ("Log tools", "Refresh, copy, export, clear", "Read today's log in the interface, copy it, export it to a file, or clear it."),
                ("Paths", "Local app data", "Settings, logs, prepared games and the WebView2 profile all sit under one folder you can delete at any time."),
                ("Settings safety", "Atomic writes", "A temporary file is written and moved over the original, and unreadable settings fall back to defaults."),
            ]
        ),
    )
    + cta(
        "See the mechanism behind it",
        "The safety page explains exactly what the stub is, what it writes, and what it never contacts.",
        ("safety.html", "Read the safety page"),
        ("how-it-works.html", "How it works"),
    )
)

page(
    "features.html",
    "Features",
    "Every Zycord "
    + VERSION
    + " feature: catalog search, one-click sessions, auto stop, history, favourites, tray behaviour, themes and logs.",
    FEATURES,
)

# -------------------------------------------------------------- how it works

HOW = (
    page_head(
        "How it works",
        "The detection mechanism, the safety boundaries, and what each part of the download does",
        "Discord decides a game is running by looking for a process with a known executable name. Zycord supplies exactly that process, and nothing else.",
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>The detection mechanism</h2>
        </div>
        <p>
          Discord maintains a list of detectable applications. Each entry carries an
          application id, a display name, one or more executable names, and often a
          folder fragment. When a game is running, Discord compares the processes on
          the computer against that list. A match turns the game into a running
          activity, which is what PLAY_ON_DESKTOP quest progress depends on.
        </p>
        <p>
          The comparison is on the process name and its location, not on the game's
          content. Zycord therefore does not need the game, only a process that
          satisfies the same check.
        </p>
        <p>
          A file name that is not on the list cannot match, whatever else is true of
          it. That is why a custom game appears as activity only when its executable
          name is one Discord already lists.
        </p>
      </div>
      <div>
        <div class="section-head">
          <h2>What Zycord does with that</h2>
        </div>
        <p>
          For the game you choose, the launcher reads the executable entry from the
          catalog, builds a path under your local application data, copies the stub
          to that path with the expected filename, and starts it.
        </p>
        <p>
          Discord sees a process with the name it expects, in a folder shaped like
          the real game's, and treats the game as running. The stub holds its process
          name and does nothing else.
        </p>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>The path Zycord takes</h2>
      <p>Every start follows the same sequence, with a validation step before each write.</p>
    </div>
"""
    + steps(
        [
            ("1", "Validate the application id", "It must be one to 25 digits. Anything else is refused before a path is built."),
            ("2", "Reduce the catalog entry to a relative path", "Backslashes are normalised, empty and dot segments dropped, and the result rejected if it is absolute, contains a drive letter, contains a parent segment, has an invalid character, is longer than eight segments, or does not end in one of the eight extensions Windows starts as a program."),
            ("3", "Build the target inside the games folder", "The path is %LOCALAPPDATA%\\Zycord\\games\\&lt;app_id&gt;\\ plus the validated relative path."),
            ("4", "Re-check containment", "The resolved target must still sit strictly inside the games folder, or the start is refused."),
            ("5", "Copy and start", "The stub is copied over any previous copy for that game, then started with the game name and application id as arguments."),
            ("6", "Record the process id", "The session table stores the id, the exact file path, the start time and any auto stop deadline."),
        ]
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Why the location matters</h2>
    </div>
    <p>
      Some detectable entries include a path fragment, not just a filename. Discord
      is more likely to match a process that sits in a folder shaped like the one
      the real game uses. The catalog keeps those fragments, and the launcher
      recreates the folder structure beneath
      <code class="inline">%LOCALAPPDATA%\\Zycord\\games</code>. Entries that could
      escape that folder are rejected when the catalog is built and again when a
      session starts.
    </p>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="grid-2">
      <div class="card">
        <span class="card-kicker">Process safety</span>
        <h3>Stopped by id, never by name</h3>
        <p>
          Some tools stop a session by image name, which terminates every process
          with that name, including a real game you have open. Zycord records the
          process id it started. Before terminating anything it confirms that the
          process with that id still points at the file Zycord created. If the id
          has been reused, nothing is stopped.
        </p>
      </div>
      <div class="card">
        <span class="card-kicker">Lifetime safety</span>
        <h3>The stub follows the launcher</h3>
        <p>
          Each stub receives the launcher's process id and waits on it. If the
          launcher exits or crashes, the stub closes itself. A session cannot outlive
          the interface that can stop it.
        </p>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>The parts of the download</h2>
        </div>
"""
    + spec(
        [
            ("Zycord.exe", "A .NET Framework 4 application that hosts the interface in WebView2 and performs every file and process operation."),
            ("stub.exe", "The session process, " + STUB_SIZE + ". Copied and renamed once per game, then started."),
            ("ui/index.html", "The interface. It has no privileged access; it sends commands to the launcher and renders the replies."),
            ("ui/catalog.json", GAMES + " games with their application ids, aliases, themes and executable entries."),
            ("WebView2 DLLs", "Three assemblies that let the launcher render the interface with the Edge WebView2 runtime."),
        ]
    )
    + """
      </div>
      <div>
        <div class="section-head">
          <h2>Where data is stored</h2>
        </div>
"""
    + spec(
        [
            ("Settings", "<code class=\"inline\">%LOCALAPPDATA%\\Zycord\\settings.json</code>"),
            ("Prepared games", "<code class=\"inline\">%LOCALAPPDATA%\\Zycord\\games\\</code>"),
            ("Logs", "<code class=\"inline\">%LOCALAPPDATA%\\Zycord\\logs\\</code>"),
            ("WebView2 profile", "<code class=\"inline\">%LOCALAPPDATA%\\Zycord\\webview\\</code>"),
        ]
    )
    + """
        <p style="margin-top:12px">
          Deleting <code class="inline">%LOCALAPPDATA%\\Zycord</code> removes
          everything the application has stored.
        </p>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>The catalog is fixed, not fetched</h2>
    </div>
    <p>
      The catalog was built once from Discord's public detectable application
      endpoint and is stored with the application. The launcher does not refresh it,
      so it never depends on anyone's server staying online. The only remote request
      the interface makes is for game icons. The trade-off is real: games added to
      Discord's list after a release will not appear until a new release ships with
      an updated catalog.
    </p>
  </div>
</section>
"""
)

page(
    "how-it-works.html",
    "How it works",
    "How Discord detects a running game, how Zycord builds and validates the session path, and how it stops only the process it started.",
    HOW,
)

# -------------------------------------------------------------- screenshots

SHOTS = (
    page_head(
        "Screenshots",
        "The launcher as it looks in version " + VERSION,
        "Captures of the three sections you will use most: browsing the catalog, watching sessions, and changing settings. The window is shown at 1280 by 820.",
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="shots">
      <figure class="shot">
        <div class="shot-bar" aria-hidden="true"><i></i><i></i><i></i><span>Zycord, Games</span></div>
        <img src="assets/shot-games.png" alt="The Games section: a search field, theme filter, sort menu and executable filters above a grid of game cards, each with an icon, name and executable count." width="1280" height="820" loading="lazy">
        <figcaption>Games. Search, filter and sort """ + GAMES + """ games, then start one from its card.</figcaption>
      </figure>

      <figure class="shot">
        <div class="shot-bar" aria-hidden="true"><i></i><i></i><i></i><span>Zycord, Sessions</span></div>
        <img src="assets/shot-sessions.png" alt="The Sessions section: a list of running sessions, each with the game name, executable, elapsed timer, auto stop state and buttons to extend or stop." width="1280" height="820" loading="lazy">
        <figcaption>Sessions. Every running stub with its executable, elapsed time and auto stop state.</figcaption>
      </figure>

      <figure class="shot">
        <div class="shot-bar" aria-hidden="true"><i></i><i></i><i></i><span>Zycord, Settings</span></div>
        <img src="assets/shot-settings.png" alt="The Settings section: panels for sessions, window behaviour, storage size and data clearing, with switches, an auto stop stepper and a theme choice." width="1280" height="820" loading="lazy">
        <figcaption>Settings. Session, window, storage and data controls in one place.</figcaption>
      </figure>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>What the captures show</h2>
    </div>
"""
    + cards(
        [
            ("Games", "A local catalog", "The grid is rendered from the catalog file shipped with the application. No server is queried to fill it."),
            ("Sessions", "One stub per game", "Each row is a session Zycord started. Before stopping anything it confirms the process still points at the file it created."),
            ("Settings", "Stored on this PC", "Every switch writes to the settings file in your local app data folder. Nothing is synced anywhere."),
        ]
    )
    + """
  </div>
</section>
"""
)

page(
    "screenshots.html",
    "Screenshots",
    "Screenshots of Zycord "
    + VERSION
    + ": the Games catalog, the Sessions list and the Settings panels.",
    SHOTS,
)

# ------------------------------------------------------------------ download

DOWNLOAD = (
    page_head(
        "Download",
        "Zycord " + VERSION + " for Windows 10 and Windows 11, 64-bit",
        "One zip archive, no installer, no background service and no registry entry. Extract it, run Zycord.exe, and the launcher is ready.",
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>Get the release</h2>
        </div>
        <p>
          The archive contains the launcher, the session stub, the interface, the
          catalog and the WebView2 assemblies. There is nothing else to install.
        </p>
        <div class="actions" style="margin:18px 0">
          <a class="btn btn-primary" href=\""""
    + REPO
    + """/releases/download/v"""
    + VERSION
    + """/Zycord-"""
    + VERSION
    + """.zip\">Download Zycord-"""
    + VERSION
    + """.zip</a>
          <a class="btn btn-secondary" href=\""""
    + REPO
    + """/releases\">All releases</a>
        </div>
        <p class="muted" style="font-size:12.5px">
          The download is """
    + DOWNLOAD_SIZE
    + """. Unpacked, it is """
    + RELEASE_SIZE
    + """. Each prepared game adds """
    + STUB_SIZE
    + """.
        </p>
      </div>
      <div>
        <div class="section-head">
          <h2>Verify the download</h2>
        </div>
        <p>
          Every release publishes the SHA-256 of the archive on its GitHub release
          page. Compare it before you run anything. On Windows:
        </p>
        <pre class="code">certutil -hashfile Zycord-"""
    + VERSION
    + """.zip SHA256</pre>
        <p style="margin-top:12px">On Linux or macOS, with the archive and the published hash side by side:</p>
        <pre class="code">sha256sum Zycord-"""
    + VERSION
    + """.zip</pre>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>What is in the archive</h2>
    </div>
    <pre class="code">Zycord-"""
    + VERSION
    + """/
  Zycord.exe                     the launcher
  stub.exe                       the session process, """
    + STUB_SIZE
    + """
  ui/
    index.html                   the interface
    app.css
    app.js
    catalog.json                 """
    + GAMES
    + """ games
    mark.svg
  Microsoft.Web.WebView2.Core.dll
  Microsoft.Web.WebView2.WinForms.dll
  WebView2Loader.dll
  README.md
  LICENSE</pre>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>Installing</h2>
        </div>
        <ol>
          <li>Extract the archive to a folder you can write to, for example <code class="inline">C:\\Users\\you\\Apps\\Zycord</code>.</li>
          <li>Run <code class="inline">Zycord.exe</code>.</li>
          <li>If Windows shows a SmartScreen warning, choose More info and then Run anyway.</li>
        </ol>
        <p>
          There is no installer and no elevation prompt. The launcher runs as your
          user and writes only to <code class="inline">%LOCALAPPDATA%\\Zycord</code>.
        </p>
      </div>
      <div>
        <div class="section-head">
          <h2>Upgrading and removing</h2>
        </div>
        <p>
          To upgrade, extract the new version over the old folder. Settings, logs and
          prepared game files live in <code class="inline">%LOCALAPPDATA%\\Zycord</code>
          and are not touched.
        </p>
        <p>
          To remove it, stop any running sessions, delete the folder you extracted,
          then delete <code class="inline">%LOCALAPPDATA%\\Zycord</code> to remove
          settings, logs and prepared files.
        </p>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
"""
    + notice(
        "Why SmartScreen warns.",
        "The executable is not signed with a code-signing certificate. Signing certificates cost money and identify a legal entity, and this project does not have one, so Windows cannot verify the publisher. If that matters to you, build from source instead. The <a href=\"docs.html\">documentation</a> explains how, and the build uses only the compiler that ships with Windows.",
        warn=True,
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Requirements</h2>
    </div>
"""
    + spec(
        [
            ("Operating system", "Windows 10 or Windows 11, 64-bit."),
            ("WebView2 Runtime", "Included with Windows 11 and current Windows 10 updates. If it is missing, install the Evergreen Runtime from Microsoft, then run Zycord again."),
            ("Discord", "The desktop application, running on the same computer."),
            ("Disk", RELEASE_SIZE + " unpacked, plus " + STUB_SIZE + " for each prepared game."),
            ("Privileges", "None."),
        ]
    )
    + """
  </div>
</section>
"""
)

page(
    "download.html",
    "Download",
    "Download Zycord "
    + VERSION
    + " for Windows. One zip archive, no installer, with a published SHA-256 checksum.",
    DOWNLOAD,
)

# ---------------------------------------------------------------------- docs

DOCS = (
    page_head(
        "Documentation",
        "Using Zycord, building it, and fixing the things that usually go wrong",
        "The launcher has ten sections. This page covers each one, then the build, the catalog generator, the self test and the common failures.",
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Using the launcher</h2>
    </div>

    <h3>Home</h3>
    <p>
      Running sessions, library count, catalog count and the size of the prepared
      files, followed by recently started games and four quick actions. The
      Start a session button opens Games.
    </p>

    <h3>Games</h3>
    <p>
      Search matches any part of a game name or one of its aliases, ignoring case.
      The theme menu lists every theme in the catalog with its count. The filter
      chips narrow results to all games, a single executable, multiple executables,
      or games with a marked launcher process. Sort offers name A to Z, name Z to A,
      or most executables first.
    </p>
    <p>
      Selecting a game shows its application id and every executable Discord watches
      for it. Where more than one is listed, choose the one that matches how you
      would normally launch the game. Entries marked as a launcher are separate
      processes some games run alongside the main one.
    </p>

    <h3>Library and Favourites</h3>
    <p>
      Saving a game records the game and the executable you chose, and it survives
      restarts. Favourites are a separate starred list with their own count in the
      rail. Neither copies the game; both are shortcuts to a session.
    </p>

    <h3>Sessions and History</h3>
    <p>
      Every running session is listed with its executable, elapsed time and any auto
      stop remaining. A session can be extended by 15 minutes. Stop ends one session;
      Stop all ends every session, with a confirmation step if you enabled it.
      History keeps the last 250 sessions with start, stop, duration and reason.
    </p>

    <h3>Settings</h3>
"""
    + spec(
        [
            ("Show the session window", "A small window with a timer while a game runs. Turn it off and the stub runs with no window at all."),
            ("Confirm before starting", "Ask first, so a stray click cannot start a game."),
            ("Confirm before stopping", "Ask before a session is ended early."),
            ("Auto stop", "End every session after this long, from 0 to 240 minutes in steps of 15. Zero means off."),
            ("Minimise to the tray", "Closing the window keeps Zycord and its sessions running. Turn it off and closing the window stops every session."),
            ("Start minimised", "Open straight into the tray."),
            ("Keep on top", "Leave the window above other windows."),
            ("Reduce motion", "Turn off the background animation in the launcher."),
            ("Theme", "Glass, the default graphite finish, or Light."),
            ("Storage", "Shows game file and log size, with buttons to open the data folder or clear game files."),
            ("Data", "Clear history and recents, clear the library and favourites, or reset settings. Each is separate."),
        ]
    )
    + """
    <h3>Logs</h3>
    <p>
      Shows the tail of today's log. Each day gets its own file in
      <code class="inline">%LOCALAPPDATA%\\Zycord\\logs</code>, and a file rolls
      over at 4 MB. The log records session starts and stops with process ids,
      refused operations and errors. It never records anything about your Discord
      account. You can refresh, copy, export or clear it.
    </p>

    <h3>About and Help</h3>
    <p>
      About shows the version, build facts and buttons for the data folder, the
      website and the repository. Help is a short guide to starting a session and
      the answers to the questions that come up most.
    </p>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>Building from source</h2>
        </div>
        <p>
          The build needs Windows, the .NET Framework 4 compiler that ships with it,
          and Python 3 for the build script. No Visual Studio, no .NET SDK, no
          MSBuild and no NuGet restore.
        </p>
        <pre class="code">git clone """
    + REPO
    + """.git
cd zycord
python build.py</pre>
        <p style="margin-top:12px">
          The script compiles the stub and the launcher, copies the interface and the
          WebView2 assemblies into <code class="inline">dist/</code>, then runs the
          self test. A passing build ends with <code class="inline">RESULT: PASS</code>
          and exits with code 0.
        </p>
        <h3>Build options</h3>
        <pre class="code">python build.py             build, test and pack the release
python build.py --no-test   skip the self test
python build.py --no-zip    leave dist/ unpacked
python build.py --clean     remove dist/ first</pre>
      </div>
      <div>
        <div class="section-head">
          <h2>The self test</h2>
        </div>
        <p>
          <code class="inline">Zycord.exe --headless</code> boots the real interface
          in a hidden window, waits for it to report ready, then exercises the
          backend directly. It checks path validation against hostile inputs, settings
          round trips, and a full session start and stop.
        </p>
        <p>
          The report is written to
          <code class="inline">%LOCALAPPDATA%\\Zycord\\logs\\selftest.txt</code> and
          printed to the console.
        </p>
        <h3>Rebuilding the catalog</h3>
        <p>
          The catalog is generated from Discord's public detectable list by
          <code class="inline">scripts/build_catalog.py</code>. It keeps only entries
          a Windows launcher can use, rejects entries that could escape a folder, and
          writes a compressed catalog to <code class="inline">app/assets</code>.
        </p>
        <pre class="code">python scripts/build_catalog.py
python scripts/build_catalog.py saved.json
python scripts/build_catalog.py --out other.json.gz</pre>
        <h3>Regenerating the brand assets</h3>
        <p>
          The mark, the favicon and every icon size come from one script, so they
          cannot drift apart. It needs Pillow.
        </p>
        <pre class="code">python brand/make_icon.py</pre>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Troubleshooting</h2>
    </div>
    <h3>The window is blank or the launcher closes immediately</h3>
    <p>
      This almost always means the WebView2 Runtime is missing or damaged. Install
      the Evergreen Runtime from Microsoft, then run Zycord again. The launcher
      writes the reason to today's log before it closes.
    </p>
    <h3>Discord does not show the game as running</h3>
    <ul>
      <li>Confirm Discord's desktop application is running on the same computer.</li>
      <li>Try a different executable from the list for that game.</li>
      <li>Restart Discord after starting the session. Detection is not instant.</li>
      <li>Some games are detected by launcher process rather than the main one. Try the entry marked as a launcher.</li>
      <li>For a custom game, check that the executable name is one Discord lists. The custom dialog checks it as you type, and a name it marks as not detected cannot appear as activity.</li>
    </ul>
    <h3>A session will not stop</h3>
    <p>
      Stop it from the Sessions tab, then from the tray menu. If the process has
      already exited, Zycord removes it from the list on the next refresh. A session
      whose file was moved or deleted cannot be verified, so Zycord deliberately
      refuses to terminate a process it cannot confirm.
    </p>
    <h3>Clear refuses to run</h3>
    <p>
      At least one session is still running. Stop it first. This is deliberate:
      clearing would delete the file a running process is using.
    </p>
    <h3>Where do I report a problem</h3>
    <p>
      Open an issue at <a href=\""""
    + REPO
    + """/issues\">github.com/iZynu/zycord/issues</a>. Include the log from the Logs
      tab; it is the fastest way to see what happened.
    </p>
  </div>
</section>
"""
)

page(
    "docs.html",
    "Documentation",
    "How to use every section of Zycord, how to build it from source, how to rebuild the catalog, and fixes for the common failures.",
    DOCS,
)

# ----------------------------------------------------------------------- faq


def qa(question, *paragraphs, open_first=False):
    body = "\n".join("            <p>%s</p>" % p for p in paragraphs)
    opened = " open" if open_first else ""
    return f"""      <details{opened}>
        <summary>{question}</summary>
        <div class="answer">
{body}
        </div>
      </details>"""


FAQ = (
    page_head(
        "FAQ",
        "Frequently asked questions",
        "Straight answers, including the uncomfortable ones. If something here is not covered, the issue tracker is open.",
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="faq">
"""
    + qa(
        "Is this safe for my Discord account?",
        "No tool can promise that. Discord has not documented or endorsed this use, and progress gained this way can be removed. Accounts have been actioned for automating quests.",
        "What Zycord does avoid is the riskier behaviour of older tools: it does not run a self-bot, does not connect to Discord's gateway, does not read or store your token, and does not modify the Discord client. It only creates a process name Discord already recognises.",
        open_first=True,
    )
    + qa(
        "Does it need my Discord login?",
        "No. There is no sign-in, no token, and no stored credential. Zycord never asks for one.",
    )
    + qa(
        "Do I need the game installed?",
        "No. That is the point. Zycord starts a copy of its own stub with the executable name Discord watches, not the game itself.",
    )
    + qa(
        "Will a custom game show as activity?",
        "Only when the executable name is one Discord already lists. Discord compares the running process name against its detectable list, so a name that is not on that list cannot match. The custom game dialog checks the name as you type and says whether it will be detected before you add it.",
    )
    + qa(
        "Does it work if Discord is closed?",
        "No. Discord must be running on the same computer to detect the process and credit quest progress.",
    )
    + qa(
        "How long does a quest take?",
        "A typical PLAY_ON_DESKTOP quest needs about 15 minutes of the game appearing to run. Leave the session running until Discord shows the quest complete. Some quests ask for longer; the auto stop can be set from 1 to 240 minutes.",
    )
    + qa(
        "Can I run several games at once?",
        "Yes. Each session is independent, with its own prepared file, process id, timer and auto stop. The Sessions tab lists them all.",
    )
    + qa(
        "Why is the catalog fixed instead of downloaded?",
        "A launcher that fetches its data at startup has to trust whoever serves it. That is a dependency on a server staying online, and a way for the data to change under you. The catalog is generated at build time from Discord's public endpoint and shipped with the application, so the launcher never contacts Discord's API or gateway. The only remote request the interface makes is for game icons.",
        "The trade-off is real: games added to Discord's list after a release will not appear until the next release ships with an updated catalog.",
    )
    + qa(
        "Why does Windows SmartScreen warn me?",
        "The executable is not signed. Code-signing certificates cost money and identify a legal entity, which this project does not have. Choose More info and then Run anyway, or build from source and inspect it yourself.",
    )
    + qa(
        "Will it stop my real game?",
        "It should not. Zycord records the process id it started and, before stopping anything, confirms the process still points at the file Zycord created. It never stops by image name, which is the mistake that terminates real games in other tools.",
    )
    + qa(
        "How much disk does it use?",
        "About 3.1 MB unpacked for the application, and about 35 KB for each prepared game. The Settings tab shows the exact figure and clears it on demand.",
    )
    + qa(
        "Does it support Rich Presence?",
        "The current release does not set a custom Rich Presence message. It makes Discord treat the game as running, which is what quest progress needs. Presence text from the real game is not reproduced.",
    )
    + qa(
        "Can I use it on more than one account?",
        "Zycord has no account concept, so nothing stops it. Running it to farm quests across many accounts is exactly the pattern that gets accounts actioned, and the terms of use here do not permit it.",
    )
    + qa(
        "Is it open source?",
        "Yes, under the MIT licence. The launcher source, the stub source, the interface, the catalog generator and the build script are all in the repository.",
    )
    + qa(
        "Does it collect anything?",
        "No. There is no analytics, no crash reporting and no update check. The only things written are a settings file, a local log and the prepared session files, all in %LOCALAPPDATA%\\Zycord.",
    )
    + qa(
        "Why is it called Zycord?",
        "It is a launcher for Discord. The name is a portmanteau, and the mark is a Z.",
    )
    + qa(
        "Is it affiliated with Discord?",
        "No. Zycord is an independent project. It is not affiliated with, endorsed by, or sponsored by Discord Inc.",
    )
    + """
    </div>
    <p style="margin-top:18px">Something else? Open an issue at <a href=\""""
    + REPO
    + """/issues\">github.com/iZynu/zycord/issues</a>.</p>
  </div>
</section>
"""
)

page(
    "faq.html",
    "FAQ",
    "Straight answers about Zycord: account risk, detection, the fixed catalog, SmartScreen, disk use, and what it does not do.",
    FAQ,
)

# -------------------------------------------------------------------- safety

SAFETY = (
    page_head(
        "Safety",
        "What the stub does, what it never does, and where every file goes",
        "The stub is a small C# process with no game code, no network code and no access to Discord. This page is the full boundary, written so you can check it against the source.",
    )
    + section(
        "What the stub does",
        "Everything the session process is responsible for.",
        cards(
            [
                ("Process", "Holds its name", "It runs under the executable filename Discord watches for the chosen game, inside a per-game folder under your local app data."),
                ("Window", "A timer and an end button", "Unless you disable it, it shows a small window with the game name, elapsed time and an End session button."),
                ("Lifetime", "Follows the launcher", "It receives the launcher's process id and exits when that process exits, so a crash cannot leave it running."),
                ("Arguments", "Parsed explicitly", "--title, --app-id, --parent and --hidden are matched as whole arguments. Nothing is matched as a substring of the command line."),
                ("Files", "None", "The stub creates no files, reads no files and writes no files. It only holds its process open."),
                ("Network", "None", "It opens no socket and makes no request. It has no code to contact Discord."),
            ]
        ),
    )
    + section(
        "What it never does",
        "The deliberate omissions, which are as much a part of the design as the features.",
        cards(
            [
                ("Discord client", "No patch or injection", "Discord's installation, memory and files are not touched. Zycord does not read the client at all."),
                ("Credentials", "No token, no account", "There is no sign-in, no token capture and no stored credential of any kind."),
                ("Detection", "No spoofing of Discord", "The launcher does not talk to Discord or pretend to be it. It creates a local process and lets Discord's own client detect it."),
                ("Telemetry", "No collection", "No analytics, no crash reporting, no update check and no phone home. Nothing is measured."),
                ("Elevation", "No admin rights", "The launcher runs as your user and never asks to elevate. It installs no service and schedules no task."),
                ("Persistence", "No autostart", "Nothing is added to startup. Zycord runs only when you start it."),
            ]
        ),
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="split">
      <div>
        <div class="section-head">
          <h2>Every path it writes</h2>
          <p>All of it sits under one folder you can delete.</p>
        </div>
"""
    + spec(
        [
            ("Root", "<code class=\"inline\">%LOCALAPPDATA%\\Zycord\\</code>"),
            ("Settings", "<code class=\"inline\">settings.json</code> holds preferences, library, favourites, recents, history and window bounds."),
            ("Prepared games", "<code class=\"inline\">games\\&lt;app_id&gt;\\</code> holds one renamed copy of the stub per game started."),
            ("Logs", "<code class=\"inline\">logs\\zycord-YYYYMMDD.log</code> holds the day's plain text log."),
            ("WebView2", "<code class=\"inline\">webview\\</code> is the profile folder the interface renders in."),
        ]
    )
    + """
      </div>
      <div>
        <div class="section-head">
          <h2>Path rules</h2>
          <p>Enforced before any file is written.</p>
        </div>
        <ul>
          <li>The application id must be one to 25 digits.</li>
          <li>The executable entry must be relative, with no drive letter and no parent segment.</li>
          <li>Invalid filename characters are refused, and the extension must be one of the eight the validator accepts: .exe, .bin, .dat, .dll, .des, .jbg, .game or .pae.</li>
          <li>The entry is limited to eight path segments.</li>
          <li>After the path is built, it is resolved again and must still sit inside the games folder.</li>
          <li>A catalog entry cannot cause a file to be written outside that folder.</li>
        </ul>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Process rules</h2>
    </div>
    <p>
      A session is stopped only after the launcher confirms that the process with the
      recorded id still points at the exact file Zycord created for it. If the id has
      been reused by another program, or the file has moved, nothing is terminated.
      This is why Zycord never stops a process by image name.
    </p>
    <p>
      Prepared files are removed only when no session is running. At startup, folders
      left behind by a previous run are swept before any session exists, so the sweep
      can never touch a file in use.
    </p>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Network rules</h2>
    </div>
    <p>
      The launcher does not contact Discord's API or gateway. The catalog is stored
      with the application rather than downloaded. The only remote resource is the
      game icon image, which the interface loads from Discord's icon CDN when the
      Games grid is shown. The interface's content security policy allows images from that host and
      nothing else, and blocks all other connections.
    </p>
    <p>
      If you would rather not load icons at all, the interface falls back to a
      lettered tile when an image fails, so the grid still works.
    </p>
  </div>
</section>

<section class="section">
  <div class="wrap">
"""
    + notice(
        "Account risk is not a safety feature.",
        "The safety rules above describe what Zycord does to your computer. They do not make the practice of simulating a running game acceptable to Discord. Read the <a href=\"terms.html\">terms</a>.",
        warn=True,
    )
    + """
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Check it yourself</h2>
    </div>
    <p>
      The stub source, the launcher source and the build script are in the
      repository. The stub is a single C# file; the launcher is a small set of files
      in the same repository. Build it with <code class="inline">python build.py</code>
      and compare the result, or read the source before you run anything.
    </p>
    <p><a href=\""""
    + REPO
    + """\">github.com/iZynu/zycord</a></p>
  </div>
</section>
"""
)

page(
    "safety.html",
    "Safety",
    "What the Zycord stub does and does not do, every path it writes under %LOCALAPPDATA%\\Zycord, and the path and process rules that protect your files.",
    SAFETY,
)

# ----------------------------------------------------------------- changelog

CHANGELOG = f"""
<h2>1.1.2 <span class="badge badge-live">Current</span></h2>
<p class="updated">Released 6 November 2026</p>

<h3>Fixed</h3>
<ul>
  <li>Stop all on the Sessions page now ends every session. The button was present and did nothing.</li>
  <li>History shows how each session ended. The Reason column was in the table but never filled.</li>
  <li>Auto stop rounds a typed value to the nearest fifteen minutes, so the documented step is the step that applies.</li>
  <li>Game names containing control characters are refused in the custom game dialog and in the library, not only when a session starts.</li>
  <li>Links on the not-found page resolve from any mistyped path, instead of only from the site root.</li>
</ul>

<h3>Documentation</h3>
<ul>
  <li>Statements that the launcher makes no network request were corrected. The interface does load game icons from Discord's CDN, and several pages said otherwise.</li>
  <li>The claim that nothing is written outside the data folder now notes the log export you choose a location for.</li>
  <li>The path rules name all eight extensions the validator accepts, instead of describing the rule as .exe only.</li>
  <li>The self test figure for 1.0.0 now matches the checks that release ran.</li>
</ul>

<h2>1.1.1</h2>
<p class="updated">Released 6 November 2026</p>

<h3>Custom games</h3>
<ul>
  <li>The custom game dialog checks the executable name against the same detectable list the catalog is built from, and says what it finds before you add the game.</li>
  <li>A custom game whose file name is not on that list is still accepted, with a warning that the session will run but will not appear as activity. It carries a Not detected tag wherever it is shown.</li>
  <li>The session window reports whether the file name is one Discord lists, instead of claiming detection for every session.</li>
</ul>

<h3>Documentation</h3>
<ul>
  <li>The custom game list was described as behaving like any other game. Discord credits a session only when the running process name is on its detectable list, and this page now says so.</li>
  <li>The path rules list all eight extensions the validator accepts, and the self test figures match the checks it runs.</li>
</ul>

<h2>1.1.0</h2>
<p class="updated">Released 6 November 2026</p>

<h3>Custom games</h3>
<ul>
  <li>A Custom game button on the Games page adds a title Discord does not list. You give it a name and the executable name, and it is searchable, savable and startable like any other game.</li>
  <li>Custom entries appear in search, in the library, in favourites and in history, and they carry a Custom tag wherever they are shown.</li>
  <li>A Custom games filter isolates them, and Remove takes the entry out of the catalog, the library, the favourites and the recents together.</li>
  <li>Up to 200 custom entries. Names are unique, case-insensitively, and the executable goes through the same path validation as a catalog entry.</li>
</ul>

<h3>Catalog</h3>
<ul>
  <li>18 more games: 10,465 in total, with 11,171 executable entries and 10,452 icons.</li>
  <li>The catalog now keeps the non-.exe entries Discord publishes, such as Aion's <code class="inline">aion.bin</code>, the Command &amp; Conquer <code class="inline">.dat</code> and <code class="inline">.game</code> files, and the YOU DON'T KNOW JACK <code class="inline">.jbg</code> builds. Windows starts a program under any extension, so these run exactly like the .exe entries.</li>
  <li>Script types are still refused. A session can never be a <code class="inline">.bat</code>, <code class="inline">.cmd</code> or <code class="inline">.ps1</code>.</li>
</ul>

<h3>Packaging</h3>
<ul>
  <li>Release archive entries now carry a fixed timestamp, so rebuilding the same folder produces a byte-identical zip and an identical checksum. The compiled binaries still carry their own build timestamps, so two separate builds do not match.</li>
</ul>

<h2>1.0.0</h2>
<p class="updated">Released 5 November 2026</p>
<p>The first public release. Everything below shipped in it.</p>

<h3>Launcher</h3>
<ul>
  <li>A borderless window with a native title bar, minimise, maximise and close.</li>
  <li>The interface is rendered in Microsoft Edge WebView2 from files in the same folder. No installer, no background service, no account and no server.</li>
  <li>Ten sections: Home, Games, Library, Favourites, Sessions, History, Settings, Logs, About and Help.</li>
  <li>Light and dark themes, a reduced motion setting, and tray behaviour that keeps sessions running when the window closes.</li>
  <li>Settings, library, favourites, history and logs are stored in <code class="inline">%LOCALAPPDATA%\\Zycord</code>. Nothing leaves the machine.</li>
</ul>

<h3>Catalog</h3>
<ul>
  <li>10,447 games and 11,151 executable entries, generated from Discord's public detectable list and filtered to Windows executables.</li>
  <li>20 launcher entries and 10,434 game icons.</li>
  <li>The catalog is stored as about 583 KB compressed, against about 2.0 MB of raw JSON.</li>
  <li>Search by name, alias or theme, with exact matches ranked first, then prefixes, then word matches.</li>
  <li>Filters for a single executable, multiple executables, and games that ship a launcher process.</li>
</ul>

<h3>Sessions</h3>
<ul>
  <li>A session stub of about 35 KB, copied into <code class="inline">%LOCALAPPDATA%\\Zycord\\games\\&lt;app_id&gt;\\</code> under the executable name Discord expects, then started.</li>
  <li>The stub holds no game code and makes no network calls. It shows a window with the elapsed time and an End session button, or runs with no window at all.</li>
  <li>It watches the launcher's process id and exits if the launcher disappears, so a crash cannot leave a session running with no way to stop it.</li>
  <li>A live timer, an optional auto stop in minutes, extension by fifteen minutes, and protection against starting the same game twice.</li>
  <li>Every session is written to history with its start, stop, duration and end reason.</li>
</ul>

<h3>Packaging</h3>
<ul>
  <li><code class="inline">python build.py</code> compiles the launcher and the stub with the .NET Framework compiler that ships with Windows, runs a 23 check self test against a real WebView2 host, and writes <code class="inline">Zycord-1.0.0.zip</code> with <code class="inline">SHA256.txt</code>.</li>
  <li>The zip you download is 0.93 MB. Unpacked, the release folder is about 3.1 MB across 12 files, including the WebView2 assemblies.</li>
</ul>

<h3>Security</h3>
<ul>
  <li>Every path that comes from the catalog or from user input is validated before use. The self test feeds ten hostile inputs through the validator and requires all ten to be rejected.</li>
  <li>The interface runs under a content security policy that allows only local scripts and styles, plus images from the Discord icon CDN.</li>
  <li>A build time check fails the build if an identifier from an earlier project appears in the source.</li>
</ul>

<h2>Earlier versions</h2>
<p>Version 1.0.0 was the first release.</p>
<p>The full history is kept in <a href="{REPO}/blob/HEAD/CHANGELOG.md">CHANGELOG.md</a>, and each release is tagged at <a href="{REPO}/releases">github.com/iZynu/zycord/releases</a>.</p>
"""

doc(
    "changelog.html",
    "Changelog",
    "Every notable change in Zycord, from the first public release to the corrections shipped in 1.1.2.",
    "Version " + VERSION,
    CHANGELOG,
)

# ------------------------------------------------------------------- roadmap

ROADMAP = (
    page_head(
        "Roadmap",
        "What is shipped, what comes next, and what is deliberately not planned",
        "This is a short list on purpose. It describes intent, not a schedule, and nothing here is a promise of a date.",
    )
    + f"""
<section class="section">
  <div class="wrap">
    <div class="road">
      <div class="road-item">
        <span class="road-dot is-shipped"></span>
        <div>
          <h3>Version 1.1.2</h3>
          <p>Stop all wired up, the end reason shown in history, the auto stop step enforced, one name rule across the app, and the network and write claims corrected.</p>
        </div>
        <span class="road-when">Shipped</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-shipped"></span>
        <div>
          <h3>Version 1.1.1</h3>
          <p>Detection checking in the custom game dialog, a session window that reports detection honestly, and documentation corrected to match.</p>
        </div>
        <span class="road-when">Shipped</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-shipped"></span>
        <div>
          <h3>Version 1.1.0</h3>
          <p>Custom games, 18 more games in the catalog, and a release archive that is byte-identical across rebuilds.</p>
        </div>
        <span class="road-when">Shipped</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-shipped"></span>
        <div>
          <h3>Version 1.0.0</h3>
          <p>The launcher, the ten sections, the fixed catalog, session tracking and the build script, all released together.</p>
        </div>
        <span class="road-when">Shipped</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-next"></span>
        <div>
          <h3>A refreshed catalog</h3>
          <p>Discord adds games to its detectable list over time. Each release rebuilds the catalog from that list, so new games appear with the next version rather than through a download.</p>
        </div>
        <span class="road-when">Next</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-next"></span>
        <div>
          <h3>Compatibility checks on current Windows builds</h3>
          <p>Verifying the launcher and the WebView2 host on the latest Windows 10 and Windows 11 servicing updates before each release.</p>
        </div>
        <span class="road-when">Next</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-next"></span>
        <div>
          <h3>Clearer failure messages</h3>
          <p>When the WebView2 Runtime is missing or a prepared file cannot be written, the message should name the cause and the fix directly, rather than a generic error.</p>
        </div>
        <span class="road-when">Next</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-later"></span>
        <div>
          <h3>Requests from the issue tracker</h3>
          <p>Ideas are considered one at a time against the rule that the launcher stays local and contacts no Discord API or gateway. Open an issue to propose one.</p>
        </div>
        <span class="road-when">Later</span>
      </div>
      <div class="road-item">
        <span class="road-dot is-later"></span>
        <div>
          <h3>Not planned: accounts, telemetry and auto-update</h3>
          <p>These are excluded by design, not deferred. Zycord has no sign-in, collects nothing and does not update itself.</p>
        </div>
        <span class="road-when">Never</span>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>How to read this</h2>
    </div>
    <p>
      Shipped means the work is in a release you can download. Next means it is the
      current focus. Later means it is wanted but not scheduled. Never means the
      decision is settled and will not change.
    </p>
    <p>
      There are no dates on this page because there is no team with a release
      calendar. The project is maintained in the open, and the issue tracker is
      where priorities are discussed.
    </p>
    <p><a href="{REPO}/issues">Open or read an issue</a></p>
  </div>
</section>
"""
)

page(
    "roadmap.html",
    "Roadmap",
    "What has shipped in Zycord, what is next, what is later, and what is deliberately not planned.",
    ROADMAP,
)

# ------------------------------------------------------------------- privacy

PRIVACY = f"""
<p>
  This policy covers two things: the Zycord desktop application and the Zycord
  website. They are treated separately because they behave differently.
</p>
<p>
  The short version: the application collects nothing and sends nothing, and the
  website sets no cookies and runs no analytics. The rest of this page explains
  what that means in detail.
</p>

<h2>The Zycord application</h2>

<h3>What it collects</h3>
<p>
  Nothing. Zycord has no analytics, no crash reporting, no update check and no
  server. It does not transmit data anywhere.
</p>

<h3>What it stores on your computer</h3>
<p>All of it lives in <code class="inline">%LOCALAPPDATA%\\Zycord</code>:</p>
<ul>
  <li><code class="inline">settings.json</code> holds your preferences and the games you saved to the library. It contains game names, application ids and executable paths, and no personal information.</li>
  <li><code class="inline">logs</code> holds one text file per day recording session starts and stops, process ids, refused operations and errors. It does not record your Discord account, token or messages.</li>
  <li><code class="inline">games</code> holds copies of the session stub, renamed for each game you started.</li>
  <li><code class="inline">webview</code> is a standard Microsoft Edge WebView2 profile folder, used to render the interface. Zycord does not read it.</li>
</ul>
<p>
  Deleting <code class="inline">%LOCALAPPDATA%\\Zycord</code> removes everything
  the application has stored. Settings are written atomically, so an interrupted
  save cannot leave a truncated file, and the previous copy is kept as
  <code class="inline">settings.json.bak</code>.
</p>

<h3>What it does not access</h3>
<ul>
  <li>Discord's installation, files, memory or credentials.</li>
  <li>Your browser history, documents, or any file outside its own data folder, apart from a log you export yourself.</li>
  <li>Your network, microphone, camera or location.</li>
</ul>

<h3>Network access</h3>
<p>
  The only network request the application makes is for game icons. The game
  catalog is stored with the application rather than downloaded. Game icons shown in the interface are loaded
  from <code class="inline">cdn.discordapp.com</code> by the WebView2 renderer,
  using only the icon hash stored in the catalog. That request fetches a public
  image and sends no information about you or your sessions.
</p>

<h3>Discord</h3>
<p>
  Zycord has no account, no sign-in and no token. It does not connect to Discord's
  gateway, does not read or modify the Discord client, and does not send anything
  to Discord. It starts a local process with a particular file name, and Discord's
  own client does the detecting.
</p>

<h2>This website</h2>

<h3>Hosting</h3>
<p>
  The site is served by GitHub Pages. GitHub may record standard web server
  information such as the requesting IP address, the page requested and the time,
  as described in GitHub's own privacy statement. That logging is done by GitHub,
  not by this project, and this project has no access to it.
</p>

<h3>Cookies and analytics</h3>
<p>
  The site sets no cookies. It uses no analytics, no advertising, no tracking
  pixels and no third-party embeds. There is no consent banner because there is
  nothing to consent to.
</p>

<h3>External links</h3>
<p>
  Links to GitHub leave this site and are governed by GitHub's policies. The
  project does not control what those pages collect.
</p>

<h2>Children</h2>
<p>
  Zycord is not directed at children. Discord requires users to be at least 13
  years old, and Zycord is only useful alongside a Discord account.
</p>

<h2>Changes to this policy</h2>
<p>
  If this policy changes, the updated version appears on this page with a new
  date. Because the application collects nothing, changes are unlikely to affect
  you in practice.
</p>

<h2>Contact</h2>
<p>
  Questions about privacy can be raised at
  <a href="{REPO}/issues">github.com/iZynu/zycord/issues</a>.
</p>
"""

doc(
    "privacy.html",
    "Privacy policy",
    "What Zycord stores on your computer, what it never collects, and how this website handles data. Local only, no telemetry, no account.",
    "Last updated 5 November 2026",
    PRIVACY,
)

# --------------------------------------------------------------------- terms

TERMS = f"""
<p>
  By downloading or using Zycord, you agree to these terms. If you do not agree,
  do not use it.
</p>

<h2>1. What Zycord is</h2>
<p>
  Zycord is a desktop application that starts a small process named after a game's
  executable so that Discord treats the game as running. It is provided free of
  charge, as open source software under the MIT License.
</p>

<h2>2. Discord is a separate service</h2>
<p>
  Zycord is not affiliated with, endorsed by, or sponsored by Discord Inc. Your
  use of Discord remains governed by Discord's own terms of service and community
  guidelines. Nothing in these terms modifies or overrides Discord's terms.
</p>

<h2>3. Account risk is yours</h2>
<p>
  Using Zycord may breach Discord's terms of service. Discord may remove quest
  progress, restrict, suspend or terminate accounts it considers to be automating
  quests. You accept that risk entirely. The authors and maintainers of Zycord
  accept no responsibility for any action Discord takes against your account.
</p>

<h2>4. Acceptable use</h2>
<p>You may:</p>
<ul>
  <li>Use Zycord on accounts you own.</li>
  <li>Read, modify and redistribute the source under the MIT License.</li>
  <li>Build it from source and inspect what it does.</li>
</ul>
<p>You may not:</p>
<ul>
  <li>Use Zycord to farm quests or rewards across multiple accounts.</li>
  <li>Use Zycord to sell, resell or otherwise monetise quest completion or rewards.</li>
  <li>Present Zycord as affiliated with, endorsed by, or approved by Discord.</li>
  <li>Use Zycord to break any law that applies to you.</li>
</ul>

<h2>5. No warranty</h2>
<p>
  Zycord is provided "as is", without warranty of any kind, express or implied,
  including but not limited to warranties of merchantability, fitness for a
  particular purpose and non-infringement. The entire risk as to the quality and
  performance of the software is with you.
</p>

<h2>6. Limitation of liability</h2>
<p>
  In no event shall the authors or copyright holders be liable for any claim,
  damages or other liability, whether in an action of contract, tort or otherwise,
  arising from, out of or in connection with the software or the use or other
  dealings in the software. This includes, but is not limited to, loss of account
  access, loss of virtual items or progress, loss of data and loss of profits.
</p>

<h2>7. What Zycord does to your computer</h2>
<p>
  Zycord writes files to <code class="inline">%LOCALAPPDATA%\\Zycord</code>:
  settings, logs, the prepared session stubs and a WebView2 profile. It starts and
  stops the stub processes it created. It does not install a service, modify the
  registry beyond standard application behaviour, or change Discord's
  installation. You are responsible for reviewing what the software does before
  running it.
</p>

<h2>8. Third-party components</h2>
<p>
  Zycord uses the Microsoft Edge WebView2 Runtime and its managed wrapper, both
  provided by Microsoft under their own licence terms. It is built with the .NET
  Framework that ships with Windows. It links to no Discord library and
  communicates with no Discord service.
</p>

<h2>9. Updates</h2>
<p>
  Zycord does not update itself and does not check for updates. New versions are
  published on the project's releases page. Continuing to use an old version is
  your choice.
</p>

<h2>10. Changes to these terms</h2>
<p>
  These terms may change between releases. The current version is always on this
  page. Continued use after a change means you accept the new terms.
</p>

<h2>11. Governing law</h2>
<p>
  These terms are governed by the laws of the country in which the maintainer
  resides, without regard to conflict of law provisions. If any provision is found
  unenforceable, the remaining provisions stay in effect.
</p>

<h2>12. Contact</h2>
<p>
  Questions about these terms can be raised at
  <a href="{REPO}/issues">github.com/iZynu/zycord/issues</a>.
</p>
"""

doc(
    "terms.html",
    "Terms and conditions",
    "The terms for using Zycord: Discord's terms apply to quests, account risk is yours, acceptable use, no warranty and limitation of liability.",
    "Last updated 5 November 2026",
    TERMS,
)

# ---------------------------------------------------------------------- 404

NOT_FOUND = (
    page_head(
        "Error 404",
        "This page does not exist",
        "The address you followed is not on this site. It may have moved, or the link may have a typo.",
    )
    + """
<section class="section">
  <div class="wrap">
    <div class="error-page">
      <span class="error-code">404</span>
      <div class="hero-actions">
        <a class="btn btn-primary" href="index.html">Go to the overview</a>
        <a class="btn btn-secondary" href="docs.html">Read the documentation</a>
      </div>
    </div>
  </div>
</section>

<section class="section">
  <div class="wrap">
    <div class="section-head">
      <h2>Every page on this site</h2>
    </div>
    <div class="grid grid-3">
      <a class="card" href="index.html"><h3>Overview</h3><p>What Zycord is and what it does.</p></a>
      <a class="card" href="features.html"><h3>Features</h3><p>Catalog, sessions, library and settings.</p></a>
      <a class="card" href="how-it-works.html"><h3>How it works</h3><p>The four steps from a game to a quest.</p></a>
      <a class="card" href="screenshots.html"><h3>Screenshots</h3><p>The Games, Sessions and Settings sections.</p></a>
      <a class="card" href="download.html"><h3>Download</h3><p>Version """ + VERSION + """ for Windows 10 and 11.</p></a>
      <a class="card" href="docs.html"><h3>Documentation</h3><p>Every section, the build and the common failures.</p></a>
      <a class="card" href="faq.html"><h3>FAQ</h3><p>Straight answers, including the uncomfortable ones.</p></a>
      <a class="card" href="safety.html"><h3>Safety</h3><p>What the stub does and never does.</p></a>
      <a class="card" href="changelog.html"><h3>Changelog</h3><p>What shipped in each version.</p></a>
      <a class="card" href="roadmap.html"><h3>Roadmap</h3><p>Shipped, next, later and never.</p></a>
      <a class="card" href="privacy.html"><h3>Privacy</h3><p>Local-only data and no telemetry.</p></a>
      <a class="card" href="terms.html"><h3>Terms</h3><p>Acceptable use and the warranty position.</p></a>
    </div>
  </div>
</section>
"""
)

write(
    "404.html",
    "Page not found | Zycord",
    "That page does not exist on the Zycord site. Links to every page, plus the overview and the documentation.",
    NOT_FOUND,
    None,
    "noindex",
    absolute=True,
)

# ------------------------------------------------------------------ sitemap

# Every indexable page, in navigation order. The 404 is deliberately absent.
INDEXABLE = [name for name in PAGES if name != "404.html"]

urls = []
for name in INDEXABLE:
    loc = SITE_URL + "/" + ("" if name == "index.html" else name)
    urls.append("  <url><loc>%s</loc></url>" % loc)

sitemap = (
    '<?xml version="1.0" encoding="UTF-8"?>\n'
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n'
    + "\n".join(urls)
    + "\n</urlset>\n"
)
with open(os.path.join(SITE, "sitemap.xml"), "w", encoding="utf-8", newline="\n") as handle:
    handle.write(sitemap)
print("wrote %-20s %6d bytes" % ("sitemap.xml", len(sitemap.encode("utf-8"))))

robots = "User-agent: *\nAllow: /\n\nSitemap: %s/sitemap.xml\n" % SITE_URL
with open(os.path.join(SITE, "robots.txt"), "w", encoding="utf-8", newline="\n") as handle:
    handle.write(robots)
print("wrote %-20s %6d bytes" % ("robots.txt", len(robots.encode("utf-8"))))

missing = [name for name in PAGES if not os.path.exists(os.path.join(SITE, name))]
if missing:
    raise SystemExit("missing pages: " + ", ".join(missing))
print("site generated with %d pages" % len(PAGES))
