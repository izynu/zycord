/* Zycord interface logic.
   All privileged work happens in the launcher. This file renders state and
   posts commands over the WebView2 message channel. */

(function () {
  "use strict";

  // ---------------------------------------------------------------- bridge

  var pending = new Map();
  var seq = 0;
  var host = window.chrome && window.chrome.webview ? window.chrome.webview : null;

  function call(cmd, args, timeout) {
    return new Promise(function (resolve, reject) {
      if (!host) { reject(new Error("The launcher bridge is not available.")); return; }
      var id = "c" + (++seq);
      pending.set(id, { resolve: resolve, reject: reject });
      host.postMessage({ id: id, cmd: cmd, args: args || {} });
      setTimeout(function () {
        if (pending.has(id)) {
          pending.delete(id);
          reject(new Error("The launcher did not answer in time."));
        }
      }, timeout || 20000);
    });
  }

  if (host) {
    host.addEventListener("message", function (e) {
      var msg = e.data;
      if (!msg || typeof msg !== "object") return;

      if (msg.event) { onEvent(msg.event, msg.data); return; }
      if (!msg.id) return;

      var p = pending.get(msg.id);
      if (!p) return;
      pending.delete(msg.id);
      if (msg.ok) p.resolve(msg.data || {});
      else p.reject(new Error(msg.error || "The command failed."));
    });
  }

  // ----------------------------------------------------------------- state

  var state = {
    catalog: null,
    byExe: null,
    games: [],
    library: [],
    favorites: [],
    recent: [],
    history: [],
    sessions: [],
    boot: null,
    settings: {},
    page: "home",
    query: "",
    filter: "all",
    theme: "",
    sort: "name",
    limit: 120,
    custom: [],
    selected: null,
    selectedExe: null,
    started: 0
  };

  var PAGE_SIZE = 120;

  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  function icon(name, cls) {
    var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    if (cls) svg.setAttribute("class", cls);
    var use = document.createElementNS("http://www.w3.org/2000/svg", "use");
    use.setAttribute("href", "#" + name);
    svg.appendChild(use);
    return svg;
  }

  function iconUrl(id, hash, size) {
    if (!hash) return null;
    // Discord serves an icon only at the path that carries the application id.
    return "https://cdn.discordapp.com/app-icons/" + id + "/" + hash + ".png?size=" + (size || 128);
  }

  // Icon element: a CDN image that degrades to a lettered tile.
  function gameIcon(game, size, cls) {
    var url = iconUrl(game.id, game.icon, size);
    var initial = (game.name || "?").trim().charAt(0).toUpperCase() || "?";
    if (!url) {
      var ph = el("div", (cls || "card-icon") + " is-empty", initial);
      return ph;
    }
    var img = document.createElement("img");
    img.className = cls || "card-icon";
    img.alt = "";
    img.loading = "lazy";
    img.decoding = "async";
    img.referrerPolicy = "no-referrer";
    img.src = url;
    img.addEventListener("error", function () {
      var ph = el("div", (cls || "card-icon") + " is-empty", initial);
      if (img.parentNode) img.parentNode.replaceChild(ph, img);
    });
    return img;
  }

  // ---------------------------------------------------------------- toasts

  function toast(message, kind) {
    var box = $("#toasts");
    var t = el("div", "toast" + (kind ? " is-" + kind : ""), message);
    box.appendChild(t);
    setTimeout(function () {
      t.classList.add("is-out");
      setTimeout(function () { t.remove(); }, 220);
    }, kind === "error" ? 5200 : 3000);
  }

  // --------------------------------------------------------------- helpers

  function clock(seconds) {
    seconds = Math.max(0, Math.floor(seconds || 0));
    var h = Math.floor(seconds / 3600);
    var m = Math.floor((seconds % 3600) / 60);
    var s = seconds % 60;
    return (h < 10 ? "0" : "") + h + ":" + (m < 10 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
  }

  function duration(seconds) {
    seconds = Math.max(0, Math.floor(seconds || 0));
    if (seconds < 60) return seconds + "s";
    var m = Math.floor(seconds / 60);
    if (m < 60) return m + "m " + (seconds % 60) + "s";
    return Math.floor(m / 60) + "h " + (m % 60) + "m";
  }

  function bytes(n) {
    n = Number(n) || 0;
    if (n < 1024) return n + " B";
    if (n < 1048576) return (n / 1024).toFixed(n < 10240 ? 1 : 0) + " KB";
    return (n / 1048576).toFixed(1) + " MB";
  }

  function count(n) {
    return Number(n || 0).toLocaleString("en-US");
  }

  function when(iso) {
    if (!iso) return "";
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    var now = Date.now();
    var diff = (now - d.getTime()) / 1000;
    if (diff < 60) return "just now";
    if (diff < 3600) return Math.floor(diff / 60) + "m ago";
    if (diff < 86400) return Math.floor(diff / 3600) + "h ago";
    if (diff < 604800) return Math.floor(diff / 86400) + "d ago";
    return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
  }

  function stamp(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    return d.toLocaleString(undefined, {
      month: "short", day: "numeric", hour: "2-digit", minute: "2-digit"
    });
  }

  function findGame(id) {
    if (!state.catalog) return null;
    return state.catalog.byId[id] || null;
  }

  function libraryEntry(id) {
    for (var i = 0; i < state.library.length; i++) {
      if (state.library[i].id === id) return state.library[i];
    }
    return null;
  }

  function sessionFor(id) {
    for (var i = 0; i < state.sessions.length; i++) {
      if (state.sessions[i].appId === id) return state.sessions[i];
    }
    return null;
  }

  function isFavorite(id) { return state.favorites.indexOf(id) !== -1; }

  // ---------------------------------------------------------------- catalog

  function loadCatalog() {
    return fetch("catalog.json", { cache: "no-cache" })
      .then(function (r) {
        if (!r.ok) throw new Error("The catalog could not be read (" + r.status + ").");
        return r.json();
      })
      .then(function (data) {
        var byId = Object.create(null);
        var themes = Object.create(null);
        // Discord compares the running process against the file name, so the
        // catalog is indexed by file name as well as by application id. One
        // name can belong to many games, so every bucket is a list.
        var byExe = Object.create(null);
        for (var i = 0; i < data.games.length; i++) {
          var g = data.games[i];
          byId[g.id] = g;
          var t = g.themes || [];
          for (var j = 0; j < t.length; j++) themes[t[j]] = (themes[t[j]] || 0) + 1;
          var names = g.ex || [];
          for (var e = 0; e < names.length; e++) {
            var file = baseName(names[e].n);
            if (!file) continue;
            if (!byExe[file]) byExe[file] = [];
            byExe[file].push(g.name);
          }
        }
        // Custom games join the catalog so search, the library and history
        // all resolve them through the same lookups.
        var games = data.games.slice();
        var custom = state.custom || [];
        for (var c = 0; c < custom.length; c++) {
          var mine = customGame(custom[c]);
          byId[mine.id] = mine;
          games.push(mine);
        }

        data.byId = byId;
        state.catalog = data;
        state.byExe = byExe;
        state.games = games;

        var list = Object.keys(themes).sort(function (a, b) { return themes[b] - themes[a]; });
        var select = $("#theme-filter");
        for (var k = 0; k < list.length; k++) {
          var o = el("option", null, list[k] + " (" + count(themes[list[k]]) + ")");
          o.value = list[k];
          select.appendChild(o);
        }

        $("#foot-catalog").textContent = count(data.count) + " games";
        $("#stat-catalog").textContent = count(data.count);
        $("#fact-catalog").textContent = count(data.count) + " games";
        return data;
      });
  }

  function searchGames() {
    var q = state.query.trim().toLowerCase();
    var out = [];

    for (var i = 0; i < state.games.length; i++) {
      var g = state.games[i];

      if (state.theme && (g.themes || []).indexOf(state.theme) === -1) continue;

      var exes = g.ex || [];
      if (state.filter === "single" && exes.length !== 1) continue;
      if (state.filter === "multi" && exes.length < 2) continue;
      if (state.filter === "launcher") {
        var hasLauncher = false;
        for (var j = 0; j < exes.length; j++) { if (exes[j].l) { hasLauncher = true; break; } }
        if (!hasLauncher) continue;
      }
      if (state.filter === "custom" && !g.custom) continue;

      if (q) {
        var name = g.name.toLowerCase();
        if (name.indexOf(q) === -1) {
          var aliases = g.aliases || [];
          var hit = false;
          for (var k = 0; k < aliases.length; k++) {
            if (String(aliases[k]).toLowerCase().indexOf(q) !== -1) { hit = true; break; }
          }
          if (!hit) continue;
        }
      }

      out.push(g);
    }

    if (state.sort === "name-desc") {
      out.sort(function (a, b) { return b.name.localeCompare(a.name); });
    } else if (state.sort === "exes") {
      out.sort(function (a, b) {
        var d = (b.ex || []).length - (a.ex || []).length;
        return d !== 0 ? d : a.name.localeCompare(b.name);
      });
    } else {
      out.sort(function (a, b) { return a.name.localeCompare(b.name); });
    }

    return out;
  }

  // ------------------------------------------------------------------ cards

  function makeCard(game, opts) {
    opts = opts || {};
    var card = el("article", "card");
    var running = sessionFor(game.id);
    if (running) card.classList.add("is-running");
    card.tabIndex = 0;

    var top = el("div", "card-top");
    top.appendChild(gameIcon(game, 128, "card-icon"));

    var text = el("div", "card-name-wrap");
    var name = el("div", "card-name", game.name);
    var exes = game.ex || [];
    var sub = el("div", "card-sub", exes.length === 1 ? shortExe(exes[0].n) : exes.length + " executables");
    text.appendChild(name);
    text.appendChild(sub);
    top.appendChild(text);
    card.appendChild(top);

    var foot = el("div", "card-foot");
    var themes = game.themes || [];
    if (themes.length) foot.appendChild(el("span", "tag", themes[0]));
    if (game.custom) {
      foot.appendChild(el("span", "tag is-custom", "Custom"));
      if (exes.length && !isDetected(exes[0].n)) {
        foot.appendChild(el("span", "tag is-warn", "Not detected"));
      }
    }
    if (running) {
      foot.appendChild(el("span", "tag", "Running"));
    } else if (libraryEntry(game.id)) {
      foot.appendChild(el("span", "tag", "Saved"));
    }

    var play = el("button", "card-play");
    play.type = "button";
    play.title = running ? "Stop session" : "Start session";
    play.setAttribute("aria-label", play.title);
    play.appendChild(icon(running ? "i-stop" : "i-play"));
    play.addEventListener("click", function (e) {
      e.stopPropagation();
      if (running) stopSession(game.id);
      else quickStart(game);
    });
    foot.appendChild(play);
    card.appendChild(foot);

    var star = el("button", "card-star" + (isFavorite(game.id) ? " is-on" : ""));
    star.type = "button";
    star.title = isFavorite(game.id) ? "Remove favourite" : "Add favourite";
    star.setAttribute("aria-label", star.title);
    star.appendChild(icon("i-star"));
    star.addEventListener("click", function (e) {
      e.stopPropagation();
      toggleFavorite(game.id);
    });
    card.appendChild(star);

    card.addEventListener("click", function () { openGame(game); });
    card.addEventListener("keydown", function (e) {
      if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openGame(game); }
    });

    return card;
  }

  function shortExe(path) {
    if (!path) return "";
    var parts = path.split("/");
    return parts[parts.length - 1];
  }

  // Windows accepts both separators and the catalog stores forward slashes,
  // so the file name is taken from the last segment of either form.
  var SEP = String.fromCharCode(92);

  function baseName(path) {
    if (!path) return "";
    var text = String(path);
    var at = Math.max(text.lastIndexOf("/"), text.lastIndexOf(SEP));
    return (at === -1 ? text : text.slice(at + 1)).toLowerCase();
  }

  // Games whose detectable list carries this executable name. Returns null
  // while the catalog is still loading, so nothing is claimed too early.
  function detectLookup(path) {
    if (!state.byExe) return null;
    var file = baseName(path);
    if (!file) return null;
    return state.byExe[file] || [];
  }

  function isDetected(path) {
    var hits = detectLookup(path);
    return hits === null || hits.length > 0;
  }

  function renderGrid(target, games, emptyNode) {
    target.textContent = "";
    var frag = document.createDocumentFragment();
    for (var i = 0; i < games.length; i++) {
      var c = makeCard(games[i]);
      c.style.animationDelay = Math.min(i, 14) * 14 + "ms";
      frag.appendChild(c);
    }
    target.appendChild(frag);
    if (emptyNode) emptyNode.hidden = games.length > 0;
  }
  // ------------------------------------------------------------------ modal

  function openGame(game) {
    state.selected = game;

    var exes = game.ex || [];
    var best = 0;
    for (var i = 0; i < exes.length; i++) {
      if (!exes[i].l) { best = i; break; }
    }
    state.selectedExe = exes.length ? exes[best].n : null;

    $("#modal-title").textContent = game.name;

    var saved = libraryEntry(game.id);
    $("#modal-sub").textContent = saved
      ? "In your library - " + saved.exe
      : game.custom
        ? "Custom game - " + (exes.length ? shortExe(exes[0].n) : "no executable")
        : exes.length + " executable" + (exes.length === 1 ? "" : "s");

    var holder = $("#modal-icon");
    var fresh = gameIcon(game, 128, "modal-icon");
    holder.parentNode.replaceChild(fresh, holder);
    fresh.id = "modal-icon";

    var list = $("#modal-exes");
    list.textContent = "";
    for (var j = 0; j < exes.length; j++) {
      (function (entry) {
        var row = el("button", "exe-opt" + (entry.n === state.selectedExe ? " is-on" : ""));
        row.type = "button";
        row.appendChild(el("i"));
        row.appendChild(el("span", null, entry.n));
        if (entry.l) row.appendChild(el("em", "exe-tag", "launcher"));
        row.addEventListener("click", function () {
          state.selectedExe = entry.n;
          $$(".exe-opt", list).forEach(function (n) { n.classList.remove("is-on"); });
          row.classList.add("is-on");
        });
        list.appendChild(row);
      })(exes[j]);
    }
    if (!exes.length) list.appendChild(el("p", "run-empty", "This game has no usable executable."));

    var tags = $("#modal-tags");
    tags.textContent = "";
    var themes = game.themes || [];
    for (var k = 0; k < themes.length; k++) tags.appendChild(el("span", "tag", themes[k]));
    tags.appendChild(el("span", "tag", game.custom ? "Custom game" : "App " + game.id));
    if (game.custom && exes.length && !isDetected(exes[0].n)) {
      tags.appendChild(el("span", "tag is-warn", "Not detected"));
    }

    var running = sessionFor(game.id);
    var fav = $("#modal-fav");
    fav.innerHTML = "";
    fav.appendChild(icon("i-star"));
    fav.appendChild(document.createTextNode(isFavorite(game.id) ? "Starred" : "Favourite"));
    fav.classList.toggle("is-on", isFavorite(game.id));

    $("#modal-remove").hidden = !game.custom;

    var add = $("#modal-add");
    add.innerHTML = "";
    add.appendChild(icon("i-plus"));
    add.appendChild(document.createTextNode(saved ? "In library" : "Add to library"));
    add.disabled = !!saved;

    var start = $("#modal-start");
    start.innerHTML = "";
    start.appendChild(icon(running ? "i-stop" : "i-play"));
    start.appendChild(document.createTextNode(running ? "Stop session" : "Start session"));
    start.classList.toggle("is-danger", !!running);

    $("#modal").hidden = false;
    start.focus();
  }

  function closeModal() {
    $("#modal").hidden = true;
    state.selected = null;
    state.selectedExe = null;
  }

  function ask(title, body, okLabel) {
    return new Promise(function (resolve) {
      var wrap = el("div", "modal");
      var scrim = el("div", "modal-scrim");
      var card = el("div", "modal-card glass");

      var h = el("h2", null, title);
      h.style.font = "600 16px/1.3 var(--font-display)";
      var p = el("p", null, body);
      p.style.cssText = "margin-top:8px;font-size:12.5px;color:var(--ink-3);line-height:1.55";

      var foot = el("div", "modal-foot");
      var cancel = el("button", "btn is-ghost", "Cancel");
      var ok = el("button", "btn is-primary", okLabel || "Continue");

      function done(v) {
        wrap.remove();
        resolve(v);
      }

      cancel.addEventListener("click", function () { done(false); });
      ok.addEventListener("click", function () { done(true); });
      scrim.addEventListener("click", function () { done(false); });

      foot.appendChild(cancel);
      foot.appendChild(ok);
      card.appendChild(h);
      card.appendChild(p);
      card.appendChild(foot);
      wrap.appendChild(scrim);
      wrap.appendChild(card);
      document.body.appendChild(wrap);
      ok.focus();
    });
  }

  // ---------------------------------------------------------- custom games

  var CUSTOM_EXTENSIONS = [".exe", ".bin", ".dat", ".dll", ".des", ".jbg", ".game", ".pae"];

  function customGame(row) {
    return {
      id: row.id,
      name: row.name,
      aliases: [],
      themes: [],
      icon: row.icon || "",
      ex: [{ n: row.exe, l: 0 }],
      custom: true
    };
  }

  // The custom dialog checks the typed file name against the same list the
  // catalog was built from. Discord compares the running process name, so a
  // name that is not in that list cannot appear as activity.
  function resetDetect() {
    var node = $("#custom-status");
    node.hidden = true;
    node.textContent = "";
    node.className = "field-status";
  }

  function paintDetect() {
    var exe = $("#custom-exe").value.trim();
    if (!exe) { resetDetect(); return; }

    var node = $("#custom-status");
    var parts = exe.split(SEP).join("/").split("/");
    var shown = parts[parts.length - 1];
    var hits = detectLookup(exe);

    node.textContent = "";
    node.hidden = false;

    if (hits === null) {
      node.className = "field-status";
      node.appendChild(icon("i-info"));
      node.appendChild(el("span", null, "The catalog is still loading."));
      return;
    }

    if (hits.length) {
      var list = hits.slice(0, 3).join(", ");
      if (hits.length > 3) list += " and " + (hits.length - 3) + " more";
      node.className = "field-status is-ok";
      node.appendChild(icon("i-check"));
      node.appendChild(el("span", null, "Discord lists " + shown + " for " + list + "."));
      return;
    }

    node.className = "field-status is-warn";
    node.appendChild(icon("i-alert"));
    node.appendChild(el("span", null,
      "Discord does not list " + shown + ". A session will run, but it will not show as activity."));
  }

  function openCustom() {
    $("#custom-name").value = "";
    $("#custom-exe").value = "";
    $("#custom-icon").textContent = "?";
    resetDetect();
    $("#custom-error").hidden = true;
    $("#custom-error").textContent = "";
    $("#custom-modal").hidden = false;
    $("#custom-name").focus();
  }

  function closeCustom() {
    $("#custom-modal").hidden = true;
  }

  function customError(message) {
    var node = $("#custom-error");
    node.textContent = message;
    node.hidden = false;
  }

  function saveCustom() {
    var name = $("#custom-name").value.trim();
    var exe = $("#custom-exe").value.trim().split("\\").join("/");

    if (!name) { customError("Give the game a name."); return; }
    if (!exe) { customError("Enter the executable name."); return; }
    if (exe.charAt(0) === "/" || /^[a-z]:/i.test(exe)) {
      customError("Use a relative path, not a full one.");
      return;
    }
    if (exe.split("/").indexOf("..") !== -1) {
      customError("The path cannot contain two dots as a folder.");
      return;
    }
    var dot = exe.lastIndexOf(".");
    var extension = dot === -1 ? "" : exe.slice(dot).toLowerCase();
    if (CUSTOM_EXTENSIONS.indexOf(extension) === -1) {
      customError("The file must end in " + CUSTOM_EXTENSIONS.join(", ") + ".");
      return;
    }

    var button = $("#custom-save");
    button.disabled = true;
    call("custom.add", { name: name, exe: exe }).then(function (row) {
      var game = customGame(row);
      state.custom.push(row);
      state.catalog.byId[game.id] = game;
      state.games.push(game);
      closeCustom();
      toast("Custom game added: " + game.name, "ok");
      paintGames();
    }).catch(function (e) {
      customError(e.message);
    }).then(function () {
      button.disabled = false;
    });
  }

  function removeCustomGame(game) {
    ask("Remove this custom game?",
      game.name + " will be removed from the catalog, your library and your favourites. A session already running is not affected.",
      "Remove game").then(function (yes) {
        if (!yes) return null;
        return call("custom.remove", { id: game.id }).then(function () {
          state.custom = state.custom.filter(function (c) { return c.id !== game.id; });
          state.games = state.games.filter(function (g) { return g.id !== game.id; });
          delete state.catalog.byId[game.id];
          state.library = state.library.filter(function (g) { return g.id !== game.id; });
          state.favorites = state.favorites.filter(function (id) { return id !== game.id; });
          closeModal();
          toast("Removed " + game.name);
          paintGames();
          paintLibrary();
          paintFavorites();
          paintNavCounts();
        }).catch(function (e) { toast(e.message, "error"); });
      });
  }

  function wireCustom() {
    $("#custom-btn").addEventListener("click", openCustom);

    $("#custom-modal").addEventListener("click", function (e) {
      if (e.target.hasAttribute("data-custom-close") || e.target.closest("[data-custom-close]")) closeCustom();
    });

    $("#custom-save").addEventListener("click", saveCustom);
    $("#custom-exe").addEventListener("input", paintDetect);

    $("#custom-name").addEventListener("input", function () {
      var value = $("#custom-name").value.trim();
      $("#custom-icon").textContent = (value.charAt(0) || "?").toUpperCase();
    });

    $("#custom-name").addEventListener("keydown", function (e) {
      if (e.key === "Enter") { e.preventDefault(); $("#custom-exe").focus(); }
    });

    $("#custom-exe").addEventListener("keydown", function (e) {
      if (e.key === "Enter") { e.preventDefault(); saveCustom(); }
    });

    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && !$("#custom-modal").hidden) closeCustom();
    });
  }

  // --------------------------------------------------------------- sessions

  function startSession(game, exe) {
    if (!exe) { toast("Choose an executable first.", "error"); return Promise.resolve(); }
    var detected = isDetected(exe);

    var go = function () {
      return call("session.start", {
        id: game.id, name: game.name, exe: exe, icon: game.icon || "", detected: detected
      }).then(function (data) {
        if (data.already) {
          toast(game.name + " is already running.");
        } else {
          toast("Session started: " + game.name, "ok");
          if (!detected) {
            toast("Discord does not list " + shortExe(exe) + ", so it will not show as activity.", "error");
          }
        }
        return refreshSessions();
      }).catch(function (e) {
        toast(e.message, "error");
      });
    };

    if (state.settings.confirmOnStart) {
      var body = detected
        ? "Zycord will run a small stub named " + shortExe(exe) +
          " so Discord sees " + game.name + " as open."
        : "Zycord will run a small stub named " + shortExe(exe) +
          ". Discord does not list that file name, so it will not show as activity.";
      return ask("Start a session?", body, "Start session").then(function (yes) {
        return yes ? go() : null;
      });
    }
    return go();
  }

  function quickStart(game) {
    var exes = game.ex || [];
    if (!exes.length) { openGame(game); return; }
    var pick = exes[0].n;
    for (var i = 0; i < exes.length; i++) {
      if (!exes[i].l) { pick = exes[i].n; break; }
    }
    return startSession(game, pick);
  }

  function stopSession(id) {
    var session = sessionFor(id);
    var name = session ? session.name : "this session";

    var go = function () {
      return call("session.stop", { id: id }).then(function () {
        toast("Session stopped: " + name);
        return refreshSessions();
      }).catch(function (e) { toast(e.message, "error"); });
    };

    if (state.settings.confirmBeforeStop) {
      return ask("Stop the session?", name + " will no longer be visible to Discord.", "Stop session")
        .then(function (yes) { return yes ? go() : null; });
    }
    return go();
  }

  function extendSession(id) {
    return call("session.extend", { id: id, minutes: 15 }).then(function () {
      toast("Session extended by 15 minutes", "ok");
      return refreshSessions();
    }).catch(function (e) { toast(e.message, "error"); });
  }

  function refreshSessions() {
    return call("session.list").then(function (data) {
      state.sessions = data || [];
      paintSessions();
      paintHomeRunning();
      paintNavCounts();
      // The stat cards and the home heading read the session list too, so
      // they have to be repainted whenever it changes.
      paintHomeStats();
      return data;
    }).catch(function () { return []; });
  }

  function toggleFavorite(id) {
    return call("favorite.toggle", { id: id }).then(function (data) {
      if (data.favorite) {
        if (state.favorites.indexOf(id) === -1) state.favorites.push(id);
      } else {
        state.favorites = state.favorites.filter(function (x) { return x !== id; });
      }
      paintNavCounts();
      if (state.page === "favorites") paintFavorites();
      if (state.page === "games") paintGames();
      if (state.page === "home") paintHomePopular();
      if (state.selected) {
        var fav = $("#modal-fav");
        fav.innerHTML = "";
        fav.appendChild(icon("i-star"));
        fav.appendChild(document.createTextNode(isFavorite(id) ? "Starred" : "Favourite"));
        fav.classList.toggle("is-on", isFavorite(id));
      }
      return data.favorite;
    }).catch(function (e) { toast(e.message, "error"); });
  }

  function addToLibrary(game, exe) {
    return call("library.add", {
      id: game.id, name: game.name, exe: exe, icon: game.icon || ""
    }).then(function () {
      var entry = { id: game.id, name: game.name, exe: exe, icon: game.icon || "" };
      state.library = state.library.filter(function (g) { return g.id !== game.id; });
      state.library.unshift(entry);
      toast("Added to library: " + game.name, "ok");
      paintNavCounts();
      if (state.page === "library") paintLibrary();
      if (state.page === "games") paintGames();
      $("#modal-add").disabled = true;
      $("#modal-add").innerHTML = "";
      $("#modal-add").appendChild(icon("i-plus"));
      $("#modal-add").appendChild(document.createTextNode("In library"));
      return entry;
    }).catch(function (e) { toast(e.message, "error"); });
  }

  // -------------------------------------------------------------- rendering

  function paintNavCounts() {
    var lib = $("#nav-library");
    lib.hidden = state.library.length === 0;
    lib.textContent = state.library.length;

    var fav = $("#nav-fav");
    fav.hidden = state.favorites.length === 0;
    fav.textContent = state.favorites.length;

    var live = $("#nav-sessions");
    live.hidden = state.sessions.length === 0;
    live.textContent = state.sessions.length;

    var badge = $("#tb-state");
    badge.hidden = state.sessions.length === 0;
    badge.textContent = state.sessions.length === 1 ? "Session running" : state.sessions.length + " sessions running";
  }

  function paintSessions() {
    var list = $("#session-list");
    list.textContent = "";
    $("#sessions-empty").hidden = state.sessions.length > 0;

    state.sessions.forEach(function (s) {
      var row = el("div", "session");

      var game = findGame(s.appId) || { name: s.name, icon: s.icon };
      row.appendChild(gameIcon(game, 128, "session-icon"));

      var main = el("div", "session-main");
      main.appendChild(el("div", "session-name", s.name));

      var exe = el("div", "session-exe", s.exe || "");
      main.appendChild(exe);

      var bar = el("div", "session-bar");
      var fill = el("i");
      var total = s.remaining && s.seconds ? s.seconds + s.remaining : 0;
      fill.style.width = total > 0 ? Math.min(100, (s.seconds / total) * 100) + "%" : "100%";
      bar.appendChild(fill);
      main.appendChild(bar);
      row.appendChild(main);

      var clockBox = el("div", "session-clock");
      var time = el("div", "session-time", clock(s.seconds));
      time.dataset.started = s.startedUtc;
      time.dataset.kind = "elapsed";
      clockBox.appendChild(time);
      if (s.remaining != null) {
        var rem = el("div", "session-remaining", "auto stop in " + duration(s.remaining));
        rem.dataset.started = s.startedUtc;
        rem.dataset.remaining = s.remaining;
        rem.dataset.seconds = s.seconds;
        rem.dataset.kind = "remaining";
        clockBox.appendChild(rem);
      } else {
        clockBox.appendChild(el("div", "session-remaining", "no auto stop"));
      }
      row.appendChild(clockBox);

      var actions = el("div", "session-actions");
      var ext = el("button", "btn is-ghost is-small", "+15m");
      ext.title = "Extend the automatic stop by fifteen minutes";
      ext.addEventListener("click", function () { extendSession(s.appId); });
      actions.appendChild(ext);

      var stop = el("button", "btn is-danger is-small");
      stop.appendChild(icon("i-stop"));
      stop.appendChild(document.createTextNode("Stop"));
      stop.addEventListener("click", function () { stopSession(s.appId); });
      actions.appendChild(stop);

      row.appendChild(actions);
      list.appendChild(row);
    });
  }

  function paintHomeRunning() {
    var box = $("#home-running");
    box.textContent = "";

    if (!state.sessions.length) {
      box.appendChild(el("p", "run-empty", "No sessions are running. Pick a game to get started."));
      return;
    }

    state.sessions.forEach(function (s) {
      var row = el("div", "run-row");
      var game = findGame(s.appId) || { name: s.name, icon: s.icon };
      row.appendChild(gameIcon(game, 64, "run-icon"));

      row.appendChild(el("div", "run-name", s.name));

      var t = el("span", "run-time", clock(s.seconds));
      t.dataset.started = s.startedUtc;
      t.dataset.kind = "elapsed";
      row.appendChild(t);

      var stop = el("button", "btn is-ghost is-small");
      stop.appendChild(icon("i-stop"));
      stop.addEventListener("click", function () { stopSession(s.appId); });
      row.appendChild(stop);

      box.appendChild(row);
    });
  }

  function paintHomeStats() {
    $("#stat-running").textContent = state.sessions.length;
    $("#stat-running-note").textContent = state.sessions.length
      ? "Since " + when(state.sessions[0].startedUtc)
      : "No active sessions";
    $("#stat-library").textContent = state.library.length;
    $("#stat-disk").textContent = bytes(state.boot ? state.boot.diskUse : 0);
    $("#stat-disk-note").textContent = (state.boot ? state.boot.folders : 0) + " game folder" +
      ((state.boot && state.boot.folders === 1) ? "" : "s");

    var title = $("#home-title");
    var eyebrow = $("#home-eyebrow");
    if (state.sessions.length) {
      title.textContent = state.sessions.length === 1
        ? state.sessions[0].name + " is running"
        : state.sessions.length + " sessions running";
      eyebrow.textContent = "In progress";
    } else {
      title.textContent = "Ready when you are";
      eyebrow.textContent = "Welcome back";
    }
  }

  function paintHomeRecent() {
    var box = $("#home-recent");
    box.textContent = "";

    if (!state.recent.length) {
      box.appendChild(el("span", "panel-note", "Games you start will show up here."));
      return;
    }

    state.recent.forEach(function (id) {
      var game = findGame(id);
      if (!game) return;
      var chip = el("button", "chip");
      var img = gameIcon(game, 64, "chip-icon");
      img.style.cssText = "width:18px;height:18px;border-radius:5px;object-fit:cover";
      chip.appendChild(img);
      chip.appendChild(el("span", null, game.name));
      chip.addEventListener("click", function () { openGame(game); });
      box.appendChild(chip);
    });

    if (!box.children.length) box.appendChild(el("span", "panel-note", "Games you start will show up here."));
  }

  function paintHomePopular() {
    var grid = $("#home-popular");
    if (!state.catalog) return;

    var picks = [];
    var wanted = ["Fortnite", "Roblox", "Minecraft", "Grand Theft Auto V", "VALORANT",
                  "Counter-Strike 2", "League of Legends", "Apex Legends"];
    for (var i = 0; i < wanted.length && picks.length < 8; i++) {
      var g = null;
      for (var j = 0; j < state.games.length; j++) {
        if (state.games[j].name.toLowerCase() === wanted[i].toLowerCase()) { g = state.games[j]; break; }
      }
      if (g) picks.push(g);
    }
    for (var k = 0; picks.length < 8 && k < state.games.length; k++) {
      var c = state.games[k];
      if (isFavorite(c.id)) picks.push(c);
    }
    for (var m = 0; picks.length < 8 && m < state.games.length; m++) {
      if (picks.indexOf(state.games[m]) === -1) picks.push(state.games[m]);
    }

    renderGrid(grid, picks.slice(0, 8), null);
  }

  function paintGames() {
    if (!state.catalog) return;

    var results = searchGames();
    var shown = results.slice(0, state.limit);

    var label = count(results.length) + " game" + (results.length === 1 ? "" : "s") +
      (state.query ? " matching \"" + state.query + "\"" : "");
    if (!state.query && state.custom.length) label += " - " + state.custom.length + " custom";
    $("#result-count").textContent = label;
    $("#result-hint").textContent = results.length > shown.length
      ? "Showing " + count(shown.length)
      : "";

    renderGrid($("#game-grid"), shown, $("#games-empty"));
    $("#load-more").hidden = results.length <= shown.length;
  }

  function paintLibrary() {
    var grid = $("#library-grid");
    var games = [];
    state.library.forEach(function (entry) {
      var game = findGame(entry.id);
      if (game) {
        games.push(game);
      } else {
        games.push({ id: entry.id, name: entry.name, icon: entry.icon, ex: [{ n: entry.exe, l: 0 }], themes: [] });
      }
    });
    renderGrid(grid, games, $("#library-empty"));
    $("#library-empty").hidden = games.length > 0;
  }

  function paintFavorites() {
    var games = [];
    state.favorites.forEach(function (id) {
      var game = findGame(id);
      if (game) games.push(game);
    });
    renderGrid($("#favorites-grid"), games, $("#favorites-empty"));
    $("#favorites-empty").hidden = games.length > 0;
  }

  function paintHistory() {
    var body = $("#history-body");
    body.textContent = "";
    $("#history-empty").hidden = state.history.length > 0;

    state.history.forEach(function (h) {
      var tr = document.createElement("tr");

      var tdGame = document.createElement("td");
      var cell = el("div", "cell-game");
      var game = findGame(h.id) || { name: h.name, icon: h.icon };
      cell.appendChild(gameIcon(game, 64, "cell-icon"));
      cell.appendChild(el("span", null, h.name));
      tdGame.appendChild(cell);
      tr.appendChild(tdGame);

      tr.appendChild(el("td", "cell-num", stamp(h.started)));
      tr.appendChild(el("td", "cell-num", duration(h.seconds)));
      tr.appendChild(el("td", "cell-num", h.reason === "autostop" ? "auto stop" : stamp(h.stopped)));

      body.appendChild(tr);
    });
  }
  // --------------------------------------------------------------- settings

  function applySettings(s) {
    state.settings = s || {};
    $$("[data-setting]").forEach(function (input) {
      var key = input.getAttribute("data-setting");
      input.checked = !!state.settings[key];
    });

    var auto = $("#auto-stop");
    auto.value = state.settings.autoStopMinutes || 0;

    var theme = state.settings.theme === "light" ? "light" : "glass";
    document.documentElement.setAttribute("data-theme", theme);
    $$("#theme-seg button").forEach(function (b) {
      b.classList.toggle("is-on", b.getAttribute("data-theme-value") === theme);
    });

    document.body.classList.toggle("reduce-motion", !!state.settings.reduceMotion);
  }

  function saveSettings(patch) {
    var payload = {};
    Object.keys(state.settings).forEach(function (k) {
      if (k === "theme" || typeof state.settings[k] === "boolean" || typeof state.settings[k] === "number") {
        payload[k] = state.settings[k];
      }
    });
    Object.keys(patch || {}).forEach(function (k) { payload[k] = patch[k]; });

    return call("settings.save", payload).then(function (s) {
      applySettings(s);
      return s;
    }).catch(function (e) { toast(e.message, "error"); });
  }

  function wireSettings() {
    $$("[data-setting]").forEach(function (input) {
      input.addEventListener("change", function () {
        var key = input.getAttribute("data-setting");
        var patch = {};
        patch[key] = input.checked;
        state.settings[key] = input.checked;
        saveSettings(patch);
      });
    });

    $$("#theme-seg button").forEach(function (b) {
      b.addEventListener("click", function () {
        var theme = b.getAttribute("data-theme-value");
        state.settings.theme = theme;
        document.documentElement.setAttribute("data-theme", theme);
        $$("#theme-seg button").forEach(function (x) { x.classList.toggle("is-on", x === b); });
        saveSettings({ theme: theme });
      });
    });

    var auto = $("#auto-stop");
    function commitAuto() {
      var v = parseInt(auto.value, 10);
      if (isNaN(v)) v = 0;
      v = Math.max(0, Math.min(240, v));
      auto.value = v;
      state.settings.autoStopMinutes = v;
      saveSettings({ autoStopMinutes: v });
    }
    auto.addEventListener("change", commitAuto);
    $$(".stepper button").forEach(function (b) {
      b.addEventListener("click", function () {
        var step = parseInt(b.getAttribute("data-step"), 10);
        var v = parseInt(auto.value, 10);
        if (isNaN(v)) v = 0;
        auto.value = Math.max(0, Math.min(240, v + step));
        commitAuto();
      });
    });

    $("#settings-reset").addEventListener("click", function () {
      ask("Reset every setting?", "Library, favourites and history are kept.", "Reset settings")
        .then(function (yes) {
          if (!yes) return;
          call("app.reset", { what: "settings" }).then(function (s) {
            applySettings(s);
            toast("Settings reset", "ok");
          }).catch(function (e) { toast(e.message, "error"); });
        });
    });

    $$("[data-reset]").forEach(function (b) {
      b.addEventListener("click", function () {
        var what = b.getAttribute("data-reset");
        ask("Clear " + what + "?", "This cannot be undone.", "Clear").then(function (yes) {
          if (!yes) return;
          call("app.reset", { what: what }).then(function () {
            if (what === "history") { state.history = []; state.recent = []; paintHistory(); paintHomeRecent(); }
            if (what === "library") { state.library = []; state.favorites = []; paintLibrary(); paintFavorites(); paintNavCounts(); }
            toast("Cleared", "ok");
          }).catch(function (e) { toast(e.message, "error"); });
        });
      });
    });

    $("#open-data").addEventListener("click", function () {
      call("shell.openFolder", { which: "data" });
    });

    $("#cleanup").addEventListener("click", function () {
      call("storage.cleanup").then(function (r) {
        toast(r.removed != null ? "Removed " + r.removed + " game folder(s)" : "Game files cleared", "ok");
        refreshStats();
      }).catch(function (e) { toast(e.message, "error"); });
    });

    $("#library-clear").addEventListener("click", function () {
      ask("Clear your library?", "Saved games are removed. Favourites stay.", "Clear library").then(function (yes) {
        if (!yes) return;
        call("library.clear").then(function () {
          state.library = [];
          paintLibrary();
          paintNavCounts();
          toast("Library cleared", "ok");
        }).catch(function (e) { toast(e.message, "error"); });
      });
    });

    $("#history-clear").addEventListener("click", function () {
      ask("Clear the history?", "Every recorded session is removed.", "Clear history").then(function (yes) {
        if (!yes) return;
        call("history.clear").then(function () {
          state.history = [];
          paintHistory();
          toast("History cleared", "ok");
        }).catch(function (e) { toast(e.message, "error"); });
      });
    });
  }

  function refreshStats() {
    return call("storage.stats").then(function (s) {
      $("#storage-size").textContent = bytes(s.diskUse);
      $("#storage-note").textContent = (s.folders || 0) + " game folder" + (s.folders === 1 ? "" : "s");
      $("#log-size").textContent = bytes(s.logSize);
      $("#log-note").textContent = (s.logCount || 0) + " log file" + (s.logCount === 1 ? "" : "s");
      if (state.boot) { state.boot.diskUse = s.diskUse; state.boot.folders = s.folders; }
      $("#stat-disk").textContent = bytes(s.diskUse);
      $("#stat-disk-note").textContent = (s.folders || 0) + " game folder" + (s.folders === 1 ? "" : "s");
      return s;
    }).catch(function () { return null; });
  }

  // ------------------------------------------------------------------- logs

  function loadLogs() {
    return call("log.tail", { lines: 500 }).then(function (d) {
      var box = $("#log-text");
      box.textContent = d.text || "";
      box.scrollTop = box.scrollHeight;
      $("#log-file").textContent = d.file || "Log file";
      $("#log-stats").textContent = bytes(d.size) + " - " + (d.count || 0) + " file" + (d.count === 1 ? "" : "s");
      return d;
    }).catch(function (e) { toast(e.message, "error"); });
  }

  function wireLogs() {
    $("#log-refresh").addEventListener("click", function () { loadLogs().then(function () { toast("Log refreshed"); }); });

    $("#log-copy").addEventListener("click", function () {
      call("shell.clipboard", { text: $("#log-text").textContent || "" })
        .then(function () { toast("Log copied to the clipboard", "ok"); })
        .catch(function (e) { toast(e.message, "error"); });
    });

    $("#log-export").addEventListener("click", function () {
      call("log.export", {}, 120000).then(function (r) {
        if (r && r.saved) toast("Log saved to " + r.path, "ok");
      }).catch(function (e) { toast(e.message, "error"); });
    });

    $("#log-clear").addEventListener("click", function () {
      ask("Clear the log files?", "Old diagnostics are deleted. The current log starts again.", "Clear logs")
        .then(function (yes) {
          if (!yes) return;
          call("log.clear").then(function () {
            loadLogs();
            refreshStats();
            toast("Logs cleared", "ok");
          }).catch(function (e) { toast(e.message, "error"); });
        });
    });
  }

  // --------------------------------------------------------------- navigation

  function showPage(name) {
    if (name === "favorites" && !state.favorites.length && state.catalog) {
      // Still show the empty state; it explains what to do.
    }
    state.page = name;

    $$(".page").forEach(function (p) { p.classList.toggle("is-active", p.id === "page-" + name); });
    $$(".nav-item").forEach(function (n) { n.classList.toggle("is-active", n.getAttribute("data-page") === name); });
    $("#main").scrollTop = 0;

    if (name === "games") paintGames();
    if (name === "library") paintLibrary();
    if (name === "favorites") paintFavorites();
    if (name === "history") paintHistory();
    if (name === "sessions") paintSessions();
    if (name === "home") { paintHomeStats(); paintHomeRunning(); paintHomeRecent(); }
    if (name === "settings") refreshStats();
    if (name === "logs") loadLogs();
  }

  function wireNav() {
    $("#nav").addEventListener("click", function (e) {
      var btn = e.target.closest("[data-page]");
      if (btn) showPage(btn.getAttribute("data-page"));
    });

    document.addEventListener("click", function (e) {
      var go = e.target.closest("[data-go]");
      if (go) showPage(go.getAttribute("data-go"));
    });

    $("#quick-folder").addEventListener("click", function () {
      call("shell.openFolder", { which: "games" });
    });

    $("#about-site").addEventListener("click", function () {
      call("shell.openExternal", { url: "https://izynu.github.io/zycord/" });
    });
    $("#about-repo").addEventListener("click", function () {
      call("shell.openExternal", { url: "https://github.com/iZynu/zycord" });
    });
    $("#about-folder").addEventListener("click", function () {
      call("shell.openFolder", { which: "data" });
    });
  }

  // --------------------------------------------------------------- title bar

  function wireTitlebar() {
    $("#tb-drag").addEventListener("mousedown", function (e) {
      if (e.button !== 0) return;
      call("shell.drag");
    });
    $("#tb-drag").addEventListener("dblclick", function () { call("shell.maximise"); });

    $("#tb-min").addEventListener("click", function () { call("shell.minimise"); });
    $("#tb-max").addEventListener("click", function () { call("shell.maximise"); });
    $("#tb-close").addEventListener("click", function () { call("shell.close"); });

    // A maximised window loses the resize border; the icon reflects it.
    document.addEventListener("zycord:window", function (e) {
      var max = !!(e.detail && e.detail.maximised);
      var btn = $("#tb-max");
      btn.innerHTML = "";
      btn.appendChild(icon(max ? "i-restore" : "i-max"));
      btn.title = max ? "Restore" : "Maximise";
      btn.setAttribute("aria-label", btn.title);
    });
  }

  // ------------------------------------------------------------------ search

  function wireSearch() {
    var input = $("#search");
    var clear = $("#search-clear");
    var timer = null;

    function run() {
      state.query = input.value;
      state.limit = PAGE_SIZE;
      clear.hidden = !input.value;
      paintGames();
    }

    input.addEventListener("input", function () {
      clearTimeout(timer);
      timer = setTimeout(run, 120);
    });

    input.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { input.value = ""; run(); }
    });

    clear.addEventListener("click", function () {
      input.value = "";
      run();
      input.focus();
    });

    $("#theme-filter").addEventListener("change", function (e) {
      state.theme = e.target.value;
      state.limit = PAGE_SIZE;
      paintGames();
    });

    $("#sort").addEventListener("change", function (e) {
      state.sort = e.target.value;
      paintGames();
    });

    $("#filters").addEventListener("click", function (e) {
      var chip = e.target.closest("[data-filter]");
      if (!chip) return;
      state.filter = chip.getAttribute("data-filter");
      state.limit = PAGE_SIZE;
      $$("#filters .chip").forEach(function (c) { c.classList.toggle("is-on", c === chip); });
      paintGames();
    });

    $("#load-more").addEventListener("click", function () {
      state.limit += PAGE_SIZE;
      paintGames();
    });
  }

  function wireModal() {
    $("#modal").addEventListener("click", function (e) {
      if (e.target.hasAttribute("data-close") || e.target.closest("[data-close]")) closeModal();
    });

    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && !$("#modal").hidden) closeModal();
    });

    $("#modal-start").addEventListener("click", function () {
      var game = state.selected;
      if (!game) return;
      var running = sessionFor(game.id);
      if (running) {
        closeModal();
        stopSession(game.id);
        return;
      }
      var exe = state.selectedExe;
      closeModal();
      startSession(game, exe);
    });

    $("#modal-add").addEventListener("click", function () {
      if (!state.selected) return;
      addToLibrary(state.selected, state.selectedExe);
    });

    $("#modal-fav").addEventListener("click", function () {
      if (state.selected) toggleFavorite(state.selected.id);
    });

    $("#modal-remove").addEventListener("click", function () {
      if (state.selected) removeCustomGame(state.selected);
    });
  }

  // ------------------------------------------------------------------ events

  function onEvent(name, data) {
    if (name === "sessions") {
      state.sessions = data || [];
      paintSessions();
      paintHomeRunning();
      paintHomeStats();
      paintNavCounts();
      return;
    }

    if (name === "window") {
      document.dispatchEvent(new CustomEvent("zycord:window", { detail: data || {} }));
      return;
    }

    if (name === "autostop") {
      var names = (data || []).map(function (id) {
        var s = findGame(id);
        return s ? s.name : "a session";
      });
      toast("Auto stop ended " + names.join(", "));
      refreshSessions();
      call("history.list").then(function (h) { state.history = h || []; paintHistory(); });
      return;
    }
  }

  function tickClocks() {
    var nodes = $$("[data-kind='elapsed']");
    for (var i = 0; i < nodes.length; i++) {
      var started = Date.parse(nodes[i].dataset.started);
      if (isNaN(started)) continue;
      nodes[i].textContent = clock((Date.now() - started) / 1000);
    }

    var rems = $$("[data-kind='remaining']");
    for (var j = 0; j < rems.length; j++) {
      var s = Date.parse(rems[j].dataset.started);
      var left = parseInt(rems[j].dataset.remaining, 10) - Math.floor((Date.now() - s) / 1000 - parseInt(rems[j].dataset.seconds, 10));
      if (isNaN(left)) continue;
      rems[j].textContent = left > 0 ? "auto stop in " + duration(left) : "stopping";
    }
  }

  // -------------------------------------------------------------------- boot

  function probe(kind, message) {
    if (!host) return;
    try { host.postMessage({ selftest: kind, message: message || "" }); } catch (e) { }
  }

  function boot() {
    var booted = false;

    call("boot").then(function (data) {
      state.boot = data;
      state.library = data.library || [];
      state.favorites = data.favorites || [];
      state.recent = data.recent || [];
      state.history = data.history || [];
      state.custom = data.custom || [];
      state.settings = data.settings || {};

      $("#foot-version").textContent = "v" + data.version;
      $("#about-version").textContent = "v" + data.version;
      $("#fact-version").textContent = data.version;

      applySettings(data.settings);
      paintNavCounts();
      paintHomeStats();
      paintHomeRunning();
      paintHistory();
      paintLibrary();
      paintFavorites();

      var dot = $("#foot-dot");
      var status = $("#foot-status");
      if (data.stubPresent) {
        dot.classList.add("is-ok");
        status.textContent = "Ready";
      } else {
        dot.classList.add("is-bad");
        status.textContent = "Stub missing";
      }

      return loadCatalog();
    }).then(function () {
      paintGames();
      paintHomePopular();
      paintHomeRecent();
      refreshSessions();
      refreshStats();

      if (!booted) {
        booted = true;
        probe("ready");
      }
    }).catch(function (e) {
      $("#foot-dot").classList.add("is-bad");
      $("#foot-status").textContent = "Not ready";
      $("#result-count").textContent = "The catalog could not be loaded.";
      toast(e.message, "error");
      probe("error", e.message);
    });
  }

  function start() {
    wireNav();
    wireTitlebar();
    wireSearch();
    wireModal();
    wireCustom();
    wireSettings();
    wireLogs();

    setInterval(tickClocks, 1000);
    setInterval(function () {
      if (state.sessions.length) refreshSessions();
    }, 5000);

    boot();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", start);
  } else {
    start();
  }
})();
