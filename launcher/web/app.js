'use strict';
(function () {
  const $ = s => document.querySelector(s);
  const token = new URLSearchParams(location.search).get('t') || '';
  let catalog = null, state = { games: {} }, view = 'all', modalId = null;
  const busy = {}; // id -> true while a request is in flight

  const FEATURED = [
    { id: 'klondike', kicker: 'FEATURED · CARD GAMES', title: 'KLONDIKE', small: 'GOLD RUSH SOLITAIRE', art: 'klondikeHero' },
    { id: 'rewindlife', kicker: 'FREE DEMO · SIMULATION', title: 'REWIND LIFE', small: 'MAPLE GROVE, 1986', art: 'rewindlifeHero' },
    { id: 'euchre', kicker: 'FEATURED · CARD GAMES', title: 'EUCHRE', small: 'A KITCHEN-TABLE CLASSIC', art: 'euchreHero' },
    { id: 'oldmaid', kicker: 'FEATURED · CARD GAMES', title: 'OLD MAID', small: 'A VICTORIAN PARLOR GAME', art: 'oldmaidHero' },
    { id: 'spades', kicker: 'NEW · CARD GAMES', title: 'SPADES', small: 'A BROOKLYN ROOFTOP CARD GAME', art: 'spadesHero' },
    { id: 'cribbage', kicker: 'NEW · CARD GAMES', title: 'CRIBBAGE', small: 'A LIGHTHOUSE CARD GAME', art: 'cribbageHero' },
    { id: 'ginrummy', kicker: 'FEATURED · CARD GAMES', title: 'GIN RUMMY', small: 'A CLUB CAR CARD GAME', art: 'ginrummyHero' },
    { id: 'hearts', kicker: 'FEATURED · CARD GAMES', title: 'HEARTS', small: 'A PARIS CAFÉ CARD GAME', art: 'heartsHero' },
    { id: 'mahjong', kicker: 'NEW · BOARD GAMES', title: 'MAHJONG', small: 'JADE MOON GARDEN', art: 'mahjongHero' },
    { id: 'solarsiege', kicker: 'FEATURED · ARCADE', title: 'SOLAR SIEGE', small: 'DEFEND THE SOLAR SYSTEM', art: 'solarsiegeHero' }
  ];
  let slide = 0, slideTimer = null, heroHover = false;

  async function api(path, body) {
    const r = await fetch(path, {
      method: body ? 'POST' : 'GET',
      headers: { 'X-Launcher-Token': token, 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined
    });
    if (!r.ok) throw new Error((await r.text()).trim());
    const ct = r.headers.get('content-type') || '';
    return ct.includes('json') ? r.json() : r.text();
  }
  const esc = s => String(s == null ? '' : s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const isLocal = g => g && g.type === 'local';
  // The player's own games: a color from the name, the program's icon, and initials as a fallback.
  function customCover(g) {
    const q = `id=${encodeURIComponent(g.id)}&t=${encodeURIComponent(token)}`;
    if (g.art) return `<div class="ccover hasart"><img class="art" src="/api/custom/icon?kind=art&${q}&v=${lastCat}" alt=""></div>`;
    let h = 0; for (const ch of g.title) h = (h * 31 + ch.charCodeAt(0)) % 360;
    const initials = esc(g.title.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]).join('').toUpperCase() || '?');
    return `<div class="ccover" style="--h:${h}"><span class="ci">${initials}</span>` +
      `<img src="/api/custom/icon?${q}&v=${lastCat}" alt="" onload="this.previousElementSibling.style.display='none'" onerror="this.remove()">` +
      `<span class="cname">${esc(g.title)}</span></div>`;
  }
  const cover = id => { const g = game(id); return isLocal(g) ? customCover(g) : (Covers[id] || Covers.hiddenobj)(); };
  const game = id => catalog.games.find(g => g.id === id);
  const st = id => state.games[id] || {};
  const isPlaceholder = g => g.subtitle === 'New category';
  const isWeb = g => g && g.type === 'web';
  const avail = g => !!st(g.id).available;
  const demo = g => g && g.demo;
  const featured = () => FEATURED.filter(f => game(f.id) && avail(game(f.id)));
  // Compatibility: only the versions that actually exist are listed.
  const PLAT = { windows: ['Windows', '⊞'], macos: ['macOS', '⌘'], linux: ['Linux', '♦'], web: ['Web', '◎'] };
  const platChips = (list, cls = '') => (list || []).filter(k => PLAT[k]).map(k => `<span class="plat ${cls}" title="Plays on ${PLAT[k][0]}"><i>${PLAT[k][1]}</i>${PLAT[k][0]}</span>`).join('');
  const plays = g => g.platforms && g.platforms.length ? `<span class="plats">${platChips(g.platforms)}</span>` : '';
  const demoTag = g => demo(g) ? '<span class="demotag" title="A free demo of the game">DEMO</span>' : '';

  function toast(msg) {
    const t = $('#toast'); t.textContent = msg; t.classList.remove('hidden');
    clearTimeout(toast.h); toast.h = setTimeout(() => t.classList.add('hidden'), 3800);
  }
  const cap = s => s.charAt(0).toUpperCase() + s.slice(1);
  function ago(ts) {
    if (!ts) return 'Never';
    const d = new Date(ts * 1000), now = new Date();
    const days = Math.floor((new Date(now.toDateString()) - new Date(d.toDateString())) / 864e5);
    if (days === 0) return 'Today'; if (days === 1) return 'Yesterday';
    if (days < 7) return days + ' days ago';
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  }
  function since(ts) {
    const s = Math.max(0, Date.now() / 1000 - ts);
    if (s < 60) return 'just now';
    if (s < 3600) return Math.floor(s / 60) + ' min ago';
    return Math.floor(s / 3600) + ' h ago';
  }
  function dur(sec) {
    sec = Math.max(0, Math.round(sec || 0));
    if (sec < 60) return sec ? '< 1m' : '0m';
    const h = Math.floor(sec / 3600), m = Math.floor(sec % 3600 / 60);
    return h ? `${h}h ${m}m` : `${m}m`;
  }
  function size(b) {
    if (!b) return '';
    if (b < 1048576) return Math.max(1, Math.round(b / 1024)) + ' KB';
    if (b < 1073741824) return (b / 1048576).toFixed(b < 10485760 ? 1 : 0) + ' MB';
    return (b / 1073741824).toFixed(1) + ' GB';
  }
  const played = s => s.playSeconds || 0;
  const opens = n => `${n || 0} ${n === 1 ? 'time' : 'times'}`;
  const ver = s => s.installedVersion || s.latestVersion || '';

  // Release notes (Markdown from GitHub) -> simple, safe HTML.
  function notesHtml(md) {
    const lines = String(md || '').replace(/\r/g, '').split('\n');
    let html = '', inList = false;
    const inline = t => esc(t).replace(/\*\*(.+?)\*\*/g, '<b>$1</b>').replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\[([^\]]+)\]\([^)]+\)/g, '$1');
    for (const l of lines) {
      if (/^---+\s*$/.test(l)) break; // the footer that links back to the launcher
      let m;
      if ((m = l.match(/^\s*[-*]\s+(.*)$/))) { if (!inList) { html += '<ul>'; inList = true; } html += `<li>${inline(m[1])}</li>`; continue; }
      if (inList) { html += '</ul>'; inList = false; }
      if ((m = l.match(/^#{1,6}\s*(.*)$/))) html += `<h4>${inline(m[1])}</h4>`;
      else if (l.trim()) html += `<p>${inline(l)}</p>`;
    }
    return html + (inList ? '</ul>' : '');
  }

  /* ---------- game state -> UI ---------- */
  // What the main button should do and say for a game.
  function primary(id) {
    const s = st(id), g = game(id), d = demo(g) ? ' DEMO' : '';
    if (isLocal(g)) {
      if (busy[id]) return { act: null, cls: 'play', html: 'OPENING…' };
      if (s.running) return { act: null, cls: 'play', html: 'PLAYING…' };
      return { act: 'play', cls: 'play', html: '▶&nbsp; PLAY' };
    }
    if (isWeb(g)) {
      if (busy[id]) return { act: null, cls: 'play', html: 'OPENING…' };
      if (s.running) return { act: null, cls: 'play', html: 'PLAYING…' };
      return { act: 'play', cls: 'play', html: '▶&nbsp; PLAY ONLINE' };
    }
    if (s.installing) {
      const word = s.phase === 'Downloading' ? (s.updating ? 'UPDATING' : 'DOWNLOADING') : 'INSTALLING';
      const pct = s.phase === 'Downloading' || s.progress ? ` <span class="bar"><i style="width:${s.progress || 0}%"></i></span> ${s.progress || 0}%` : '…';
      return { act: null, cls: 'play inst', html: word + pct };
    }
    if (busy[id]) return { act: null, cls: 'play', html: 'WORKING…' };
    if (s.running) return { act: null, cls: 'play', html: 'PLAYING…' };
    if (s.installed && s.update) return { act: 'install', cls: 'play upd', html: `⟳&nbsp; UPDATE TO ${esc(s.latestVersion)}` };
    if (!s.installed) return { act: 'install', cls: 'play inst', html: '⬇&nbsp; INSTALL' + d };
    return { act: 'play', cls: 'play', html: '▶&nbsp; PLAY' + d };
  }
  function pillFor(g, short) {
    if (!avail(g)) return '<span class="pill soon">COMING SOON</span>';
    const s = st(g.id);
    if (isWeb(g)) return s.running ? '<span class="pill ready">PLAYING</span>' : '<span class="pill web">🌐 WEB GAME</span>';
    if (isLocal(g)) return s.running ? '<span class="pill ready">PLAYING</span>' : s.launchError ? '<span class="pill miss">NOT FOUND</span>' : g.store ? '<span class="pill mine">XBOX / STORE</span>' : '<span class="pill mine">YOUR GAME</span>';
    if (s.error) return '<span class="pill miss">INSTALL FAILED</span>';
    if (s.installing) return `<span class="pill inst">${s.phase === 'Downloading' ? (s.updating ? 'UPDATING' : 'DOWNLOADING') : 'INSTALLING'} ${s.progress || 0}%</span>`;
    if (s.running) return '<span class="pill ready">PLAYING</span>';
    if (s.installed && s.update) return '<span class="pill upd">UPDATE' + (short ? '' : ' AVAILABLE') + '</span>';
    if (s.installed) return '<span class="pill inst">✓ INSTALLED</span>';
    return '<span class="pill avail">NOT INSTALLED</span>';
  }
  async function act(id, what) {
    if (!what || busy[id]) return;
    const g = game(id);
    if (what === 'uninstall') return confirmUninstall(g);
    busy[id] = true; renderAll();
    try {
      const wasUpdate = st(id).update;
      const r = await api('/api/' + (what === 'play' ? 'launch' : what), { id });
      Object.assign(state.games, r.games);
      if (what === 'play') toast(`Starting ${g.title}${demo(g) ? ' (demo)' : ''}…`);
      if (what === 'install') toast(`${wasUpdate ? 'Updating' : 'Downloading'} ${g.title} from GitHub…`);
    } catch (e) { toast(cap(e.message) + '.'); }
    busy[id] = false; renderAll();
  }

  /* ---------- launcher update + update status ---------- */
  // Launcher updates work like Bytehawk Launcher's: a banner offers the new
  // version; Update now downloads its setup (with progress, and Cancel), then
  // runs it showing only its progress window. The launcher closes, setup
  // installs the update and opens the launcher again.
  let bannerHidden = '';
  function renderBanner() {
    const u = state.launcherUpdate, b = $('#lbanner');
    if (!u || (bannerHidden === u.version && !u.downloading && !u.error)) { b.classList.add('hidden'); return; }
    b.classList.remove('hidden');
    let title = `Ironite Launcher ${esc(u.version)} is available`, line = `You have ${esc(state.version)}.`, btns;
    if (u.installing) {
      title = `Installing Ironite Launcher ${esc(u.version)}…`;
      line = 'The launcher will close, and the installer will open it again when it’s done. If Windows asks for permission, click Yes.';
      btns = '';
    } else if (u.downloading) {
      title = `Downloading Ironite Launcher ${esc(u.version)}…`;
      line = `<span class="bar lbbar"><i style="width:${u.progress}%"></i></span> ${u.progress}%` + (u.total ? ` · ${size(u.done)} of ${size(u.total)}` : '');
      btns = '<button class="btn ghost small" data-cancel>Cancel</button>';
    } else {
      if (u.error) line = esc(u.error);
      btns = `<button class="btn play small" data-self>⟳&nbsp; Update now</button><button class="btn ghost small" data-notes>Release notes</button><button class="lbx" data-dismiss title="Hide until next time">✕</button>`;
    }
    b.innerHTML = `<div class="lb-txt"><b>${title}</b><span>${line}</span></div><div class="actions">${btns}</div>`;
    const on = (sel, fn) => { const el = b.querySelector(sel); if (el) el.onclick = fn; };
    on('[data-self]', async () => {
      try { if (u.error) await api('/api/dismiss', { id: 'launcher' }); const r = await api('/api/selfupdate', {}); state.launcherUpdate = r.launcherUpdate; renderBanner(); }
      catch (e) { toast(cap(e.message) + '.'); }
    });
    on('[data-cancel]', async () => { try { await api('/api/selfupdate/cancel', {}); } catch (e) { } });
    on('[data-notes]', () => releaseDialog(`Ironite Launcher ${u.version}`, u.notes, u.url));
    on('[data-dismiss]', () => { bannerHidden = u.version; renderBanner(); });
  }
  function renderCheck() {
    const el = $('#chk');
    let txt;
    if (state.checking) txt = '<span class="spin"></span> Checking GitHub…';
    else if (state.checkError && state.checkBusy) txt = `<span class="warn" title="${esc(state.checkError)}">⚠</span> GitHub is busy · will retry`;
    else if (state.checkError) txt = `<span class="warn" title="${esc(state.checkError)}">⚠</span> Offline · <button class="linkbtn" data-chk>Retry</button>`;
    else if (state.lastCheck) txt = `Checked GitHub ${since(state.lastCheck)} · <button class="linkbtn" data-chk>Check</button>`;
    else txt = '<span class="spin"></span> Checking GitHub…';
    el.innerHTML = txt;
    const b = el.querySelector('[data-chk]');
    if (b) b.onclick = async () => { state.checking = true; renderCheck(); try { await api('/api/check', {}); } catch (e) { } };
  }

  /* ---------- featured carousel ---------- */
  function renderHero() {
    const hero = $('#hero'), F = featured();
    if (!F.length) { hero.classList.add('hidden'); return; }
    if (slide >= F.length) slide = 0;
    hero.innerHTML = F.map((f, i) => {
      const g = game(f.id), s = st(f.id), p = primary(f.id);
      const meta = s.installed || s.playCount
        ? `<span>Opened <b>${opens(s.playCount)}</b></span><span>Played <b>${dur(played(s))}</b></span><span>Last played <b>${ago(s.lastPlayed)}</b></span>`
        : `<span>Version <b>${esc(ver(s))}</b></span><span>${esc(g.players)}</span><span>${demo(g) ? 'Free demo' : 'Free'}</span>${s.size ? `<span>${size(s.size)}</span>` : ''}`;
      return `<div class="slide ${i === slide ? 'on' : ''}">
        <div class="art">${Covers[f.art]()}</div><div class="shade"></div>
        <div class="info">
          <div class="kicker">${f.kicker}</div>
          <h1>${f.title}${demo(g) ? '<span class="demotag big">DEMO</span>' : ''}<small>${f.small}</small></h1>
          <p>${esc(g.description)}</p>
          <div class="actions">
            <button class="btn ${p.cls}" data-act="${p.act || ''}" data-id="${f.id}" ${p.act ? '' : 'disabled'}>${p.html}</button>
            <button class="btn ghost small" data-details="${f.id}">Details</button>
            ${s.running ? '<span class="small"><span class="dot"></span>&nbsp; Running in its own window</span>' : ''}
          </div>
          <div class="meta">${meta}${plays(g)}</div>
        </div></div>`;
    }).join('') +
      `<button class="arrow prev" aria-label="Previous">‹</button><button class="arrow next" aria-label="Next">›</button>
       <div class="dots">${F.map((f, i) => `<button class="${i === slide ? 'on' : ''}" data-i="${i}" aria-label="${f.title}"></button>`).join('')}</div>`;
    hero.querySelectorAll('[data-act]').forEach(b => b.onclick = () => act(b.dataset.id, b.dataset.act));
    hero.querySelectorAll('[data-details]').forEach(b => b.onclick = () => details(b.dataset.details));
    hero.querySelectorAll('.dots button').forEach(b => b.onclick = () => show(+b.dataset.i));
    hero.querySelector('.prev').onclick = () => show(slide - 1);
    hero.querySelector('.next').onclick = () => show(slide + 1);
    hero.onmouseenter = () => heroHover = true;
    hero.onmouseleave = () => heroHover = false;
  }
  function show(i) {
    const n = featured().length || 1;
    slide = (i + n) % n;
    document.querySelectorAll('#hero .slide').forEach((el, k) => el.classList.toggle('on', k === slide));
    document.querySelectorAll('#hero .dots button').forEach((el, k) => el.classList.toggle('on', k === slide));
    restartTimer();
  }
  function restartTimer() {
    clearInterval(slideTimer);
    slideTimer = setInterval(() => { if (!heroHover && !document.hidden && view === 'all') show(slide + 1); }, 8000);
  }

  /* ---------- tiles & sections ---------- */
  function tile(g, where) {
    const soon = !avail(g), s = st(g.id), wide = isPlaceholder(g), p = soon ? null : primary(g.id);
    let sub = soon ? 'Coming soon · ' + esc(g.players) : esc(g.subtitle) + ' · ' + esc(g.players);
    if (where === 'installed') sub = (isWeb(g) ? 'Web game · ' : isLocal(g) ? 'Your game · ' : s.installedVersion ? `v${esc(s.installedVersion)} · ` : '') + `Played ${dur(played(s))}`;
    const icon = !p || !p.act ? '' : p.act === 'play' ? '▶' : s.update ? '⟳' : '⬇';
    const chip = icon ? `<span class="playchip ${p.act === 'play' ? 'go' : s.update ? 'up' : ''}" data-act="${p.act}" title="${p.act === 'play' ? 'Play' : s.update ? 'Update to ' + esc(s.latestVersion) : 'Install'}">${icon}</span>` : '';
    return `<button class="tile ${soon ? 'soon' : ''} ${wide ? 'wide' : ''} ${demo(g) ? 'isdemo' : ''}" data-id="${g.id}">
      <span class="cv">${cover(g.id)}${demo(g) ? '<span class="demoband">FREE DEMO</span>' : ''}</span>
      <span class="ribbon">${pillFor(g, true)}</span>${chip}
      <span class="tt"><div class="t1">${esc(g.title)}${demoTag(g)}</div><div class="t2">${sub}</div>${plays(g)}</span>
      ${s.installing ? `<span class="tbar"><i style="width:${s.progress || 0}%"></i></span>` : ''}
    </button>`;
  }
  // Web games need no install, so they're always in Installed.
  const installedGames = () => catalog.games.filter(g => isWeb(g) ? avail(g) : (st(g.id).installed || st(g.id).installing));
  const updatable = () => installedGames().filter(g => !isWeb(g) && st(g.id).update && !st(g.id).installing && !st(g.id).running);

  function section(title, count, blurb, list, where, extra = '', tail = '') {
    return `<div class="cat"><div class="cat-h"><h2>${esc(title)}</h2><span class="count">${count}</span>${extra}</div>
      ${blurb ? `<p class="cat-b">${esc(blurb)}</p>` : ''}<div class="grid">${list.map(g => tile(g, where)).join('')}${tail}</div></div>`;
  }
  // Non-Ironite Games: all of the player's own games, grouped by the category they gave them.
  function customSection(c) {
    const mine = catalog.games.filter(isLocal);
    const n = mine.length, count = n ? `${n} ${n === 1 ? 'game' : 'games'}` : 'Add your first game';
    const groups = [...new Set(mine.map(g => g.group || ''))].sort((a, b) => !a ? 1 : !b ? -1 : a.localeCompare(b));
    if (view !== 'custom' || groups.length < 2) return section(c.name, count, c.blurb, mine, 'cat', '', addTile());
    let html = `<div class="cat"><div class="cat-h"><h2>${esc(c.name)}</h2><span class="count">${count}</span></div><p class="cat-b">${esc(c.blurb)}</p>`;
    groups.forEach((grp, i) => {
      const list = mine.filter(g => (g.group || '') === grp);
      html += `<h3 class="subcat">${esc(grp || 'No category')}<span class="count">${list.length}</span></h3>
        <div class="grid">${list.map(g => tile(g, 'cat')).join('')}${i === groups.length - 1 ? addTile() : ''}</div>`;
    });
    return html + '</div>';
  }
  const addTile = () => `<button class="tile addtile" data-addgame><span class="cv"><span class="plus">+</span></span>
      <span class="tt"><div class="t1">Add a game</div><div class="t2">Any game on this PC</div></span></button>`;
  function renderCats() {
    $('#hero').classList.toggle('hidden', view !== 'all' || !featured().length);
    let html = '';
    const inst = installedGames(), ups = updatable();
    const upBtn = ups.length ? `<button class="btn play upd small upall" data-upall>⟳&nbsp; Update all (${ups.length})</button>` : '';
    if (view === 'installed') {
      const total = inst.reduce((t, g) => t + played(st(g.id)), 0);
      html = inst.length
        ? section('Installed', `${inst.length} ${inst.length === 1 ? 'game' : 'games'} · ${dur(total)} played in total`, state.autoUpdate ? 'Games you have installed. They update automatically from GitHub when a new version comes out.' : 'Games you have installed. Automatic updates are off; update them here.', inst, 'installed', upBtn)
        : `<div class="cat"><div class="cat-h"><h2>Installed</h2></div><div class="empty"><b>No games installed yet</b>Open the Library and click Install on a game. It downloads from GitHub and shows up here, ready to play.</div></div>`;
    } else {
      if (view === 'all' && inst.length)
        html += section('Installed', `${inst.length} ${inst.length === 1 ? 'game' : 'games'}`, '', inst, 'installed', upBtn);
      html += catalog.categories.filter(c => view === 'all' || view === c.id).map(c => {
        const list = catalog.games.filter(g => g.category === c.id);
        list.sort((a, b) => (avail(b) ? 1 : 0) - (avail(a) ? 1 : 0));
        const ready = list.filter(avail).length;
        if (c.id === 'custom') return customSection(c);
        const count = ready === 0 ? 'Coming soon' : `${ready} available` + (list.length > ready ? ` · ${list.length - ready} coming soon` : '');
        return section(c.name, count, c.blurb, list, 'cat');
      }).join('');
    }
    $('#cats').innerHTML = html;
    document.querySelectorAll('.tile[data-id]').forEach(t => t.onclick = e => {
      const chip = e.target.closest('.playchip');
      if (chip && chip.dataset.act) { e.stopPropagation(); act(t.dataset.id, chip.dataset.act); return; }
      details(t.dataset.id);
    });
    document.querySelectorAll('[data-addgame]').forEach(b => b.onclick = addGameDialog);
    document.querySelectorAll('[data-upall]').forEach(b => b.onclick = () => updatable().forEach(g => act(g.id, 'install')));
    const n = installedGames().filter(g => isWeb(g) || st(g.id).installed).length;
    $('#instCount').textContent = n; $('#instCount').title = ups.length ? `${ups.length} update${ups.length > 1 ? 's' : ''} available` : '';
    $('#instCount').classList.toggle('upd', !!ups.length);
    $('#instCount').classList.toggle('hidden', !n);
  }
  function renderLibrary() { if (view === 'all') renderHero(); renderCats(); }

  /* ---------- details / dialogs ---------- */
  function openModal() { $('#modal').classList.remove('hidden'); }
  function closeModal() { $('#modal').classList.add('hidden'); modalId = null; }
  function details(id) {
    const g = game(id); modalId = id;
    $('#mArt').innerHTML = cover(id);
    $('#mTitle').innerHTML = esc(g.title) + demoTag(g);
    $('#mPill').innerHTML = pillFor(g);
    $('#mNotes').innerHTML = '';
    if (!avail(g)) {
      $('#mSub').textContent = isPlaceholder(g) ? 'New category' : `${g.subtitle} · ${g.players}`;
      $('#mDesc').textContent = g.description + (g.repo ? ' It will be ready to install here as soon as its first release is published.' : ' Coming soon to Ironite Launcher.');
      $('#mMeta').innerHTML = '';
      $('#mActions').innerHTML = '<button class="btn ghost small" data-close>Got it</button>';
    } else {
      const s = st(id), p = primary(id);
      const v = isWeb(g) ? 'Web game' : isLocal(g) ? (g.store ? 'Xbox / Microsoft Store' : 'Not an Ironite game') + (g.group ? ` · ${esc(g.group)}` : '') : s.installed
        ? `v${esc(s.installedVersion)} installed` + (s.update ? ` · v${esc(s.latestVersion)} available` : '')
        : `v${esc(s.latestVersion)}` + (s.size ? ` · ${size(s.size)} download` : '');
      $('#mSub').innerHTML = `${esc(g.subtitle)} · ${esc(g.players)} · ${v}`;
      $('#mDesc').innerHTML = isLocal(g) ? (g.description ? `${esc(g.description)}<br><br>` : '') + (g.store
          ? 'Added from the Xbox app / Microsoft Store. Windows starts it, and the launcher tracks your play time while it\u2019s open; updates come from the Xbox app or Store.'
          : `Added from <code class="path">${esc(g.exe)}</code>. The launcher starts it and tracks your play time; updates come from the game itself.`) : esc(g.description + (isWeb(g) ? ' Needs an internet connection; your progress is saved on this PC.' : '') +
        (demo(g) ? ' New demo versions arrive automatically through the launcher.' : ''));
      $('#mMeta').innerHTML = (isLocal(g) ? '' : `<span class="mplats">Plays on ${plays(g)}</span>`) + `<span>Opened <b>${opens(s.playCount)}</b></span><span>Played <b>${dur(played(s))}</b></span><span>Last played <b>${ago(s.lastPlayed)}</b></span>` +
        (s.installed && s.installedAt ? `<span>${isLocal(g) ? 'Added' : 'Installed'} <b>${ago(s.installedAt)}</b></span>` : '');
      if (s.error) $('#mNotes').innerHTML = `<div class="err">${esc(s.error)}</div>`;
      else if (s.launchError) $('#mNotes').innerHTML = `<div class="err">${esc(s.launchError)}</div>`;
      else if (s.notes) $('#mNotes').innerHTML = `<div class="notes"><div class="nh">${s.update ? 'New in' : 'What’s new in'} version ${esc(s.latestVersion)}${s.publishedAt ? ` <span>· ${ago(s.publishedAt)}</span>` : ''}</div>${notesHtml(s.notes)}</div>`;
      $('#mActions').innerHTML = `<button class="btn ${p.cls} small" data-act="${p.act || ''}" ${p.act ? '' : 'disabled'}>${p.html}</button>
        ${s.error ? '<button class="btn ghost small" data-retry>Try again</button>' : ''}
        ${s.installed && s.update && !s.running && !s.installing ? '<button class="btn ghost small" data-act="play">▶&nbsp; Play ' + esc(s.installedVersion) + '</button>' : ''}
        ${isLocal(g) && g.store && s.running ? '<button class="btn ghost small" data-closed>■&nbsp; It\u2019s closed</button>' : ''}
        ${isLocal(g) ? '<button class="btn ghost small" data-edit>✎&nbsp; Edit details</button>' : ''}
        ${s.installed && !s.running && !s.installing ? `<button class="btn danger" data-act="uninstall">${isLocal(g) ? 'Remove from launcher' : 'Uninstall'}</button>` : ''}
        <button class="btn ghost small" data-close>Close</button>`;
      const cl = $('#mActions').querySelector('[data-closed]');
      if (cl) cl.onclick = async () => { try { const r = await api('/api/custom/closed', { id }); Object.assign(state.games, r.games); renderAll(); } catch (e) { toast(cap(e.message) + '.'); } };
      const ed = $('#mActions').querySelector('[data-edit]');
      if (ed) ed.onclick = () => editDialog(g);
      const rt = $('#mActions').querySelector('[data-retry]');
      if (rt) rt.onclick = async () => { await api('/api/dismiss', { id }).catch(() => { }); delete state.games[id].error; act(id, 'install'); };
    }
    $('#mActions').querySelectorAll('[data-act]').forEach(b => b.onclick = () => { const a = b.dataset.act; if (a !== 'uninstall') closeModal(); act(id, a); });
    $('#mActions').querySelectorAll('[data-close]').forEach(b => b.onclick = closeModal);
    openModal();
  }
  function releaseDialog(title, notes, url) {
    modalId = null;
    $('#mArt').innerHTML = `<img src="icon.png" alt="" class="appicon">`;
    $('#mPill').innerHTML = '<span class="pill upd">LAUNCHER UPDATE</span>';
    $('#mTitle').textContent = title;
    $('#mSub').textContent = `You have version ${state.version}`;
    $('#mDesc').textContent = '';
    $('#mMeta').innerHTML = '';
    $('#mNotes').innerHTML = `<div class="notes">${notesHtml(notes) || '<p>Improvements and fixes.</p>'}</div>`;
    $('#mActions').innerHTML = '<button class="btn ghost small" data-close>Close</button>';
    $('#mActions').querySelector('[data-close]').onclick = closeModal;
    openModal();
  }
  function confirmUninstall(g) {
    if (isLocal(g)) return confirmRemove(g);
    modalId = null;
    $('#mPill').innerHTML = '<span class="pill miss">UNINSTALL</span>';
    $('#mTitle').textContent = `Uninstall ${g.title}${demo(g) ? ' Demo' : ''}?`;
    $('#mSub').textContent = '';
    $('#mNotes').innerHTML = '';
    $('#mDesc').textContent = `This removes ${g.title}'s game files from this PC. Your saves, settings and play stats are kept, and you can install it again any time from the Library.`;
    $('#mMeta').innerHTML = '';
    $('#mActions').innerHTML = '<button class="btn danger" data-yes>Uninstall</button><button class="btn ghost small" data-close>Cancel</button>';
    $('#mActions').querySelector('[data-yes]').onclick = async () => {
      closeModal(); busy[g.id] = true; renderAll();
      try { const r = await api('/api/uninstall', { id: g.id }); Object.assign(state.games, r.games); toast(`${g.title} was uninstalled.`); }
      catch (e) { toast(cap(e.message) + '.'); }
      busy[g.id] = false; renderAll();
    };
    $('#mActions').querySelector('[data-close]').onclick = closeModal;
    openModal();
  }
  function confirmRemove(g) {
    modalId = null;
    $('#mPill').innerHTML = '<span class="pill miss">REMOVE</span>';
    $('#mTitle').textContent = `Remove ${g.title} from the launcher?`;
    $('#mSub').textContent = '';
    $('#mNotes').innerHTML = '';
    $('#mDesc').textContent = `${g.title} disappears from the launcher, along with its play stats here. The game itself isn't touched: its files stay on this PC and you can add it again any time.`;
    $('#mMeta').innerHTML = '';
    $('#mActions').innerHTML = '<button class="btn danger" data-yes>Remove</button><button class="btn ghost small" data-close>Cancel</button>';
    $('#mActions').querySelector('[data-yes]').onclick = async () => {
      closeModal();
      try { await api('/api/custom/remove', { id: g.id }); toast(`${g.title} was removed from the launcher.`); await reloadCatalog(); }
      catch (e) { toast(cap(e.message) + '.'); }
    };
    $('#mActions').querySelector('[data-close]').onclick = closeModal;
    openModal();
  }

  /* ---------- achievements from other launchers ---------- */
  let achData = null, achOpen = {};
  async function renderAchievements(refresh) {
    const body = $('#achBody');
    if (!achData || refresh) body.innerHTML = '<div class="muted ach-loading"><span class="spin"></span> Loading achievements from Steam and Xbox…</div>';
    try { achData = await api('/api/ach' + (refresh ? '?refresh=1' : '')); }
    catch (e) { body.innerHTML = `<div class="err">${esc(cap(e.message))}.</div>`; return; }
    if (view !== 'achievements') return;
    drawAchievements();
  }
  function drawAchievements() {
    const d = achData, body = $('#achBody');
    const linked = (d.games || []).filter(Boolean);
    const steamWho = d.steamProfile ? `<span title="${esc(d.steamProfile)}">${esc(d.steamProfile.replace(/^https?:\/\/(www\.)?steamcommunity\.com\//i, ''))}</span>` : d.steamDetectedName ? `${esc(d.steamDetectedName)} <span class="muted">(signed in to Steam on this PC)</span>` : '<span class="muted">Not found. Paste your profile link.</span>';
    let html = `<div class="ach-accounts">
      <div class="acct"><b>Steam</b><span>${steamWho}</span><button class="linkbtn" data-acct="steam">${d.steamProfile ? 'Change' : 'Use another profile'}</button>
        <small>Your Steam profile's <i>Game details</i> must be set to Public.</small></div>
      <div class="acct"><b>Xbox</b><span>${d.xblKeySet ? 'Connected with your OpenXBL key' : '<span class="muted">Not connected</span>'}</span><button class="linkbtn" data-acct="xbox">${d.xblKeySet ? 'Change key' : 'Add OpenXBL key'}</button>
        <small>Xbox achievements need a free key from xbl.io (sign in there with your Xbox account).</small></div>
    </div>`;
    if (!linked.length) {
      html += `<div class="empty"><b>No games linked yet</b>Open one of your non-Ironite games, choose <i>Edit details</i>, and pick where its achievements come from (Steam or Xbox). Then they'll show up here.</div>`;
    } else {
      const tot = linked.reduce((t, r) => t + (r.total || 0), 0), got = linked.reduce((t, r) => t + (r.unlocked || 0), 0);
      html += `<div class="ach-sum"><b>${got}</b> of ${tot} achievements unlocked across ${linked.length} ${linked.length === 1 ? 'game' : 'games'}
        <button class="btn ghost small" data-achrefresh>⟳&nbsp; Refresh</button></div>`;
      html += linked.map(r => {
        const g = game(r.id); if (!g) return '';
        const pct = r.total ? Math.round(r.unlocked * 100 / r.total) : 0, open = achOpen[r.id];
        const src = r.source === 'xbox' ? 'Xbox' : 'Steam';
        const head = `<button class="ach-game" data-achgame="${esc(r.id)}"><span class="ach-cv">${cover(r.id)}</span>
          <span class="ach-info"><b>${esc(g.title)}</b><span class="muted">${src}${r.error ? '' : ` · ${r.unlocked} of ${r.total}` + (r.scoreTotal ? ` · ${r.score} / ${r.scoreTotal} G` : '')}</span>
          ${r.error ? `<span class="ach-err">${esc(cap(r.error))}.</span>` : `<span class="bar big"><i style="width:${pct}%"></i></span>`}</span>
          ${r.error ? '' : `<span class="ach-pct">${pct}%</span><span class="chev">${open ? '▾' : '▸'}</span>`}</button>`;
        const items = open && !r.error ? `<div class="ach-items">${r.items.map(it => `<div class="ach-item ${it.unlocked ? 'got' : ''}">
            ${it.icon ? `<img src="${esc(it.icon)}" alt="" loading="lazy" referrerpolicy="no-referrer" onerror="this.outerHTML='<span class=noicon>★</span>'">` : '<span class="noicon">★</span>'}
            <span><b>${esc(it.name)}</b>${it.description ? `<small>${esc(it.description)}</small>` : ''}</span>
            <span class="when">${it.unlocked ? (it.unlockedAt ? ago(it.unlockedAt) : 'Unlocked') : 'Locked'}${it.score ? ` · ${it.score} G` : ''}</span></div>`).join('')}</div>` : '';
        return `<div class="ach-card">${head}${items}</div>`;
      }).join('');
    }
    body.innerHTML = html;
    body.querySelectorAll('[data-achgame]').forEach(b => b.onclick = () => { achOpen[b.dataset.achgame] = !achOpen[b.dataset.achgame]; drawAchievements(); });
    const rf = body.querySelector('[data-achrefresh]'); if (rf) rf.onclick = () => renderAchievements(true);
    body.querySelectorAll('[data-acct]').forEach(b => b.onclick = () => accountDialog(b.dataset.acct));
  }
  function accountDialog(kind) {
    modalId = null;
    const steam = kind === 'steam';
    $('#mArt').innerHTML = `<div class="ccover add"><span class="plus">${steam ? '♨' : 'X'}</span></div>`;
    $('#mPill').innerHTML = '<span class="pill mine">ACHIEVEMENTS</span>';
    $('#mTitle').textContent = steam ? 'Steam profile' : 'Xbox (OpenXBL key)';
    $('#mSub').textContent = '';
    $('#mDesc').textContent = steam
      ? 'Paste your Steam profile link (for example steamcommunity.com/id/yourname), or leave it empty to use the account signed in to Steam on this PC. In Steam, set Profile > Privacy Settings > Game details to Public.'
      : 'Xbox achievements need a sign-in, so the launcher reads them through OpenXBL. Go to xbl.io, sign in with your Xbox account, copy your API key, and paste it here. The key is stored only on this PC.';
    $('#mMeta').innerHTML = '';
    $('#mNotes').innerHTML = `<div class="addform"><label>${steam ? 'Profile link or ID' : 'OpenXBL API key'}<input id="acVal" type="${steam ? 'text' : 'password'}" spellcheck="false" value="${steam ? esc(achData.steamProfile || '') : ''}" placeholder="${steam ? 'steamcommunity.com/id/yourname' : achData.xblKeySet ? 'Leave empty to remove the key' : 'Paste your key'}"></label><div class="err hidden" id="fErr"></div></div>`;
    $('#mActions').innerHTML = '<button class="btn play small" data-yes>Save</button><button class="btn ghost small" data-close>Cancel</button>';
    $('#mActions').querySelector('[data-yes]').onclick = async () => {
      formErr('');
      try {
        await api('/api/ach/accounts', steam ? { steamProfile: $('#acVal').value } : { xblKey: $('#acVal').value });
        closeModal(); renderAchievements(true);
      } catch (e) { formErr(e.message); }
    };
    $('#mActions').querySelector('[data-close]').onclick = closeModal;
    openModal();
  }

  /* ---------- adding your own games ---------- */
  async function reloadCatalog() {
    const s = await api('/api/state');
    catalog = await api('/api/catalog'); lastCat = s.catalogSize; state = s; renderAll();
  }
  // Category choices: the launcher's categories, plus the player's own.
  function categoryOptions(sel) {
    const own = [...new Set(catalog.games.filter(isLocal).map(g => g.group).filter(Boolean))];
    const launcher = catalog.categories.filter(c => c.id !== 'custom');
    const launcherNames = new Set(launcher.map(c => c.name));
    const opt = (v, label) => `<option value="${esc(v)}" ${v === sel ? 'selected' : ''}>${esc(label)}</option>`;
    return opt('', 'No category') +
      `<optgroup label="Launcher categories (the game shows there too)">${launcher.map(c => opt('cat:' + c.id, c.name)).join('')}</optgroup>` +
      (own.filter(n => !launcherNames.has(n)).length ? `<optgroup label="Your categories">${own.filter(n => !launcherNames.has(n)).map(n => opt('grp:' + n, n)).join('')}</optgroup>` : '') +
      opt('new', 'New category…');
  }
  const catValue = g => !g ? '' : g.category && g.category !== 'custom' ? 'cat:' + g.category : g.group ? 'grp:' + g.group : '';
  function wireCategory() {
    const sel = $('#fCat'), box = $('#fNewCat');
    sel.onchange = () => { box.classList.toggle('hidden', sel.value !== 'new'); if (sel.value === 'new') box.focus(); };
  }
  function readCategory() {
    const v = $('#fCat').value;
    if (v === 'new') return { category: '', group: $('#fNewCat').value.trim() };
    if (v.startsWith('cat:')) return { category: v.slice(4), group: '' };
    if (v.startsWith('grp:')) return { category: '', group: v.slice(4) };
    return { category: '', group: '' };
  }
  const catField = g => `<label>Category<select id="fCat">${categoryOptions(catValue(g))}</select>
      <input id="fNewCat" class="hidden" type="text" maxlength="40" placeholder="New category name (for example Racing)"></label>`;
  const formErr = m => { const el = $('#fErr'); el.textContent = m ? cap(m) + '.' : ''; el.classList.toggle('hidden', !m); };

  function addGameDialog() {
    modalId = null;
    $('#mArt').innerHTML = '<div class="ccover add"><span class="plus">+</span></div>';
    $('#mPill').innerHTML = '<span class="pill mine">NON-IRONITE GAME</span>';
    $('#mTitle').textContent = 'Add a game';
    $('#mSub').textContent = 'Play any game on this PC from Ironite Launcher.';
    $('#mDesc').textContent = '';
    $('#mMeta').innerHTML = '';
    $('#mNotes').innerHTML = `<div class="tabs"><button class="on" data-tab="exe">Program (.exe)</button><button data-tab="store">Xbox / Microsoft Store</button></div>
      <div class="addform">
        <div data-pane="exe"><label>Program<span class="row"><input id="agPath" type="text" placeholder="C:\\Games\\My Game\\MyGame.exe" spellcheck="false"><button class="btn ghost small" id="agBrowse">Browse…</button></span></label></div>
        <div data-pane="store" class="hidden">
          <label>Game<span class="row"><input id="agSearch" type="text" placeholder="Search your Xbox and Store apps" spellcheck="false"></span></label>
          <div class="applist" id="agApps"><div class="muted small">Loading your Xbox and Microsoft Store apps…</div></div>
          <label class="chk"><input type="checkbox" id="agAll"> Show apps that aren\u2019t games too</label>
        </div>
        <label>Name<input id="agTitle" type="text" maxlength="60" placeholder="My Game"></label>
        ${catField(null)}
        <div class="err hidden" id="fErr"></div>
        <div class="muted small">You can add details and your own cover art after adding it, with Edit details.</div></div>`;
    $('#mActions').innerHTML = '<button class="btn play small" data-yes>+&nbsp; Add game</button><button class="btn ghost small" data-close>Cancel</button>';
    wireCategory();
    const path = $('#agPath'), title = $('#agTitle');
    let tab = 'exe', titleTouched = false, picked = null, apps = null;
    title.oninput = () => titleTouched = true;
    const setTitle = t => { if (!titleTouched || !title.value) title.value = t || ''; };
    document.querySelectorAll('.tabs [data-tab]').forEach(b => b.onclick = () => {
      tab = b.dataset.tab;
      document.querySelectorAll('.tabs [data-tab]').forEach(x => x.classList.toggle('on', x === b));
      document.querySelectorAll('.addform [data-pane]').forEach(p => p.classList.toggle('hidden', p.dataset.pane !== tab));
      formErr('');
      if (tab === 'store' && !apps) loadApps();
    });
    $('#agBrowse').onclick = async () => {
      formErr('');
      try { const r = await api('/api/custom/browse', {}); if (r.path) { path.value = r.path; setTitle(r.title); } }
      catch (e) { formErr(e.message); }
    };
    path.onchange = () => {
      const base = path.value.trim().replace(/^"|"$/g, '').split(/[\\/]/).pop().replace(/\.exe$/i, '');
      setTitle(base.replace(/([_\-\s]?(x64|x86|win64|win32|64bit|launcher|shipping))+$/i, '')
        .replace(/[_\-.]+/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').trim().replace(/^./, c => c.toUpperCase()));
    };
    function drawApps() {
      const box = $('#agApps'); if (!box || !apps) return;
      const q = $('#agSearch').value.trim().toLowerCase(), all = $('#agAll').checked;
      const games = apps.filter(a => a.game);
      let list = apps.filter(a => (all || a.game || !games.length) && (!q || a.name.toLowerCase().includes(q)));
      if (!list.length) { box.innerHTML = `<div class="muted small">${q ? 'Nothing matches that search.' : 'No Xbox or Microsoft Store games were found on this PC. Tick the box below to see other apps.'}</div>`; return; }
      box.innerHTML = list.map(a => `<button class="app ${picked && picked.appId === a.appId ? 'on' : ''}" data-app="${esc(a.appId)}" ${a.added ? 'disabled' : ''}>
          <b>${esc(a.name)}</b>${a.game ? '<span class="tag">XBOX GAME</span>' : ''}${a.added ? '<span class="tag dim">ADDED</span>' : ''}</button>`).join('');
      box.querySelectorAll('[data-app]').forEach(b => b.onclick = () => {
        picked = apps.find(a => a.appId === b.dataset.app); setTitle(picked.name); drawApps();
      });
    }
    async function loadApps() {
      try { apps = (await api('/api/custom/storeapps')).apps || []; drawApps(); }
      catch (e) { $('#agApps').innerHTML = `<div class="err">${esc(cap(e.message))}.</div>`; }
    }
    $('#agSearch').oninput = drawApps; $('#agAll').onchange = drawApps;
    $('#mActions').querySelector('[data-yes]').onclick = async () => {
      formErr('');
      const c = readCategory();
      if ($('#fCat').value === 'new' && !c.group) return formErr('type a name for the new category');
      const body = Object.assign({ title: title.value }, c);
      if (tab === 'store') {
        if (!picked) return formErr('choose a game from the list');
        Object.assign(body, { appId: picked.appId, location: picked.location });
      } else body.path = path.value;
      try {
        await api('/api/custom/add', body);
        closeModal(); toast(`${title.value.trim() || 'Your game'} was added to the launcher.`);
        await reloadCatalog();
        if (view !== 'all' && view !== 'installed') go('custom');
      } catch (e) { formErr(e.message); }
    };
    $('#mActions').querySelector('[data-close]').onclick = closeModal;
    openModal();
    setTimeout(() => path.focus(), 50);
  }

  // Shrinks a chosen picture to cover size (keeps uploads small) and returns a data: URL.
  function loadArt(file) {
    return new Promise((resolve, reject) => {
      if (!file || !/^image\//.test(file.type)) return reject(new Error('choose a picture (PNG, JPEG, WebP or GIF)'));
      const url = URL.createObjectURL(file), img = new Image();
      img.onload = () => {
        const max = 1200, k = Math.min(1, max / Math.max(img.width, img.height));
        const cv = document.createElement('canvas');
        cv.width = Math.round(img.width * k); cv.height = Math.round(img.height * k);
        cv.getContext('2d').drawImage(img, 0, 0, cv.width, cv.height);
        URL.revokeObjectURL(url);
        resolve(file.type === 'image/png' ? cv.toDataURL('image/png') : cv.toDataURL('image/jpeg', 0.9));
      };
      img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('that picture couldn\u2019t be opened')); };
      img.src = url;
    });
  }

  function editDialog(g) {
    modalId = null;
    let art = undefined; // undefined = unchanged, '' = remove, data: URL = new picture
    $('#mArt').innerHTML = cover(g.id);
    $('#mPill').innerHTML = '<span class="pill mine">EDIT DETAILS</span>';
    $('#mTitle').textContent = g.title;
    $('#mSub').textContent = g.store ? 'Xbox / Microsoft Store game' : 'Your game';
    $('#mDesc').textContent = '';
    $('#mMeta').innerHTML = '';
    $('#mNotes').innerHTML = `<div class="addform">
        <label>Name<input id="fTitle" type="text" maxlength="60" value="${esc(g.title)}"></label>
        ${catField(g)}
        <div class="two"><label>Developer<input id="fDev" type="text" maxlength="60" placeholder="Who made it" value="${esc(g.subtitle === 'Your game' ? '' : g.subtitle)}"></label>
          <label>Players<input id="fPlayers" type="text" maxlength="40" placeholder="1 player" value="${esc(g.players === 'On this PC' ? '' : g.players)}"></label></div>
        <label>Description<textarea id="fDesc" rows="3" maxlength="1200" placeholder="What the game is about">${esc(g.description || '')}</textarea></label>
        <label>Achievements from<span class="row achrow">
          <select id="fAch"><option value="">None</option><option value="steam" ${g.achSource === 'steam' ? 'selected' : ''}>Steam</option><option value="xbox" ${g.achSource === 'xbox' ? 'selected' : ''}>Xbox</option></select>
          <input id="fAchId" type="text" spellcheck="false" placeholder="" value="${esc(g.achSource === 'xbox' ? (g.xboxTitleId || '') : (g.steamAppId || ''))}">
          <button class="btn ghost small" id="fAchFind">Find</button></span>
          <span class="muted small" id="fAchHelp"></span></label>
        <label>Cover art<span class="row artrow">
          <button class="btn ghost small" id="fArtPick">🖼&nbsp; Choose picture…</button>
          <button class="btn ghost small ${g.art ? '' : 'hidden'}" id="fArtDel">Use the game\u2019s icon</button>
          <input type="file" id="fArtFile" accept="image/png,image/jpeg,image/webp,image/gif" class="hidden"></span>
          <span class="muted small">A tall picture works best (4:5, like 800 × 1000). You can also drop a picture onto the cover.</span></label>
        <div class="err hidden" id="fErr"></div></div>`;
    $('#mActions').innerHTML = '<button class="btn play small" data-yes>Save</button><button class="btn ghost small" data-close>Cancel</button>';
    wireCategory();
    const achSel = $('#fAch'), achId = $('#fAchId'), achHelp = $('#fAchHelp');
    const achUI = () => {
      const v = achSel.value;
      achId.classList.toggle('hidden', !v); $('#fAchFind').classList.toggle('hidden', !v);
      achId.placeholder = v === 'xbox' ? 'Xbox title ID' : 'Steam app ID or store link';
      achHelp.textContent = v === 'steam' ? 'The number in the game\u2019s Steam store link (store.steampowered.com/app/NUMBER). Find fills it in for games installed by Steam.'
        : v === 'xbox' ? 'Find looks it up from the game, or your Xbox game history (needs your OpenXBL key on the Achievements page).' : '';
    };
    achSel.onchange = () => { achId.value = ''; achUI(); };
    achUI();
    $('#fAchFind').onclick = async () => {
      formErr('');
      try {
        const r = await api('/api/ach/detect', { id: g.id });
        if (!r.source) return formErr(achSel.value === 'xbox' ? 'couldn\u2019t find it automatically; type the title ID' : 'couldn\u2019t find it automatically; paste the game\u2019s Steam store link');
        achSel.value = r.source; achUI(); achId.value = r.source === 'xbox' ? r.xboxTitleId : r.steamAppId;
        if (r.found) achHelp.textContent = `Found: ${r.found}`;
      } catch (e) { formErr(e.message); }
    };
    const preview = src => { $('#mArt').innerHTML = src ? `<div class="ccover hasart"><img class="art" src="${src}" alt=""></div>` : customCover(Object.assign({}, g, { art: false })); };
    const useFile = async f => { formErr(''); try { art = await loadArt(f); preview(art); $('#fArtDel').classList.remove('hidden'); } catch (e) { formErr(e.message); } };
    $('#fArtPick').onclick = () => $('#fArtFile').click();
    $('#fArtFile').onchange = e => useFile(e.target.files[0]);
    $('#fArtDel').onclick = () => { art = ''; preview(''); $('#fArtDel').classList.add('hidden'); };
    const drop = $('#mArt');
    drop.ondragover = e => { e.preventDefault(); drop.classList.add('drop'); };
    drop.ondragleave = () => drop.classList.remove('drop');
    drop.ondrop = e => { e.preventDefault(); drop.classList.remove('drop'); if (e.dataTransfer.files[0]) useFile(e.dataTransfer.files[0]); };
    $('#mActions').querySelector('[data-yes]').onclick = async () => {
      formErr('');
      const c = readCategory();
      if ($('#fCat').value === 'new' && !c.group) return formErr('type a name for the new category');
      try {
        const src = achSel.value, aid = achId.value.trim();
        if (src && !/\d/.test(aid)) return formErr(src === 'xbox' ? 'type the game\u2019s Xbox title ID, or click Find' : 'type the game\u2019s Steam app ID, or click Find');
        await api('/api/custom/update', Object.assign({ id: g.id, title: $('#fTitle').value, developer: $('#fDev').value, players: $('#fPlayers').value, description: $('#fDesc').value,
          achSource: src, steamAppId: src === 'steam' ? aid : (g.steamAppId || ''), xboxTitleId: src === 'xbox' ? aid : (g.xboxTitleId || '') }, c));
        if (art !== undefined) await api('/api/custom/setart', { id: g.id, data: art });
        await reloadCatalog();
        details(g.id); toast('Saved.');
      } catch (e) { formErr(e.message); }
    };
    $('#mActions').querySelector('[data-close]').onclick = () => details(g.id);
    openModal();
  }
  $('#modal').onclick = e => { if (e.target.id === 'modal') closeModal(); };
  document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); });

  /* ---------- changelog ---------- */
  function parseChangelog(md) {
    const rels = []; let cur = null, sec = null;
    md.split(/\r?\n/).forEach(line => {
      let m;
      if ((m = line.match(/^##(?!#)\s*\[?([^\]\s]+)\]?\s*-?\s*(.*)$/))) { cur = { v: m[1], date: m[2], secs: [] }; rels.push(cur); sec = null; }
      else if (cur && (m = line.match(/^###\s*(.+)$/))) { sec = { name: m[1], items: [] }; cur.secs.push(sec); }
      else if (cur && (m = line.match(/^\s*[-*]\s+(.+)$/))) { if (!sec) { sec = { name: 'Changes', items: [] }; cur.secs.push(sec); } sec.items.push(m[1]); }
    });
    return rels;
  }
  function fmtDate(d) { const t = Date.parse(d + 'T12:00:00'); return isNaN(t) ? d : new Date(t).toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric' }); }
  async function renderNews() {
    const rels = parseChangelog(await api('/api/changelog'));
    $('#newsSub').textContent = `You're on version ${state.version}. Launcher releases are listed newest first.`;
    let html = rels.map((r, i) => `<div class="rel"><div class="rel-h"><b>Version ${esc(r.v)}</b>${i === 0 ? '<span class="pill ready">CURRENT</span>' : ''}<span class="date">${esc(fmtDate(r.date))}</span></div>
      ${r.secs.map(s => `<h4>${esc(s.name)}</h4><ul>${s.items.map(x => `<li>${esc(x)}</li>`).join('')}</ul>`).join('')}</div>`).join('');
    const games = catalog.games.filter(g => avail(g) && !isWeb(g));
    html = `<div class="rel game"><div class="rel-h"><b>Games on GitHub</b></div><p class="muted">Each game installs and updates from its own GitHub repository. Latest versions:</p><ul>${games.map(g => {
      const s = st(g.id);
      return `<li>${esc(g.title)}${demo(g) ? ' (demo)' : ''} — version ${esc(s.latestVersion || s.installedVersion)}${s.installed ? (s.update ? ` <span class="muted">(you have ${esc(s.installedVersion)})</span>` : ' <span class="muted">(installed)</span>') : ''}</li>`;
    }).join('')}</ul></div>` + html;
    $('#changelog').innerHTML = html;
  }
  function renderAbout() {
    const titles = catalog.games.filter(g => !isPlaceholder(g));
    const av = catalog.games.filter(avail);
    const inst = av.filter(g => st(g.id).installed);
    const web = av.filter(isWeb).length;
    $('#abGames').textContent = `${inst.length} installed · ${av.length - web} to download · ${web} web · ${titles.length - av.length} coming soon`;
    $('#abTime').textContent = dur(av.reduce((t, g) => t + played(st(g.id)), 0));
    $('#abGamesDir').textContent = state.gamesDir || '—';
    const sw = $('#abAuto');
    sw.classList.toggle('on', !!state.autoUpdate);
    sw.setAttribute('aria-checked', state.autoUpdate ? 'true' : 'false');
    $('#abAutoTxt').textContent = state.autoUpdate ? 'On' : 'Off';
    $('#abChecked').textContent = state.checking ? 'Checking now…' : state.lastCheck ? `${since(state.lastCheck)}${state.checkError ? ' (couldn’t reach GitHub)' : ''}` : '—';
  }
  $('#abAuto').onclick = async () => {
    try { const r = await api('/api/autoupdate', { on: !state.autoUpdate }); state.autoUpdate = r.autoUpdate; renderAbout(); toast(`Automatic game updates are ${state.autoUpdate ? 'on' : 'off'}.`); }
    catch (e) { toast(e.message); }
  };

  /* ---------- nav ---------- */
  function renderAll() {
    renderBanner(); renderCheck();
    if (view === 'news' || view === 'friends' || view === 'achievements') return;
    if (view === 'about') return renderAbout();
    renderLibrary();
    if (modalId && !$('#modal').classList.contains('hidden')) details(modalId);
  }
  function go(v) {
    view = v;
    document.querySelectorAll('#nav button').forEach(b => b.classList.toggle('on', b.dataset.view === v));
    $('#vLibrary').classList.toggle('hidden', v === 'news' || v === 'about' || v === 'friends' || v === 'achievements');
    $('#vFriends').classList.toggle('hidden', v !== 'friends');
    $('#vAchievements').classList.toggle('hidden', v !== 'achievements');
    $('#vNews').classList.toggle('hidden', v !== 'news');
    $('#vAbout').classList.toggle('hidden', v !== 'about');
    if (v === 'news') renderNews().catch(e => toast(e.message));
    else if (v === 'achievements') renderAchievements();
    else renderAll();
    $('#main').scrollTop = 0;
  }
  document.querySelectorAll('#nav button').forEach(b => b.onclick = () => go(b.dataset.view));
  $('#verSide').onclick = () => go('news');

  let lastCat = 0;
  async function refresh() {
    try {
      const s = await api('/api/state');
      if (s.catalogSize !== lastCat) { catalog = await api('/api/catalog'); lastCat = s.catalogSize; state = s; return renderAll(); }
      for (const [id, g] of Object.entries(s.games)) {
        if (g.launchError && g.launchError !== (state.games[id] || {}).launchError) toast(g.launchError);
      }
      // Redraw only when something visible changed. A running game's play time
      // ticks every second, so it counts in whole minutes here.
      const sig = st => JSON.stringify([Object.entries(st.games || {}).map(([k, g]) => [k, Object.assign({}, g, { playSeconds: Math.floor((g.playSeconds || 0) / 60) })]), st.autoUpdate]);
      const changed = sig(s) !== sig(state);
      const lu = JSON.stringify(s.launcherUpdate) !== JSON.stringify(state.launcherUpdate);
      const chk = s.checking !== state.checking || s.lastCheck !== state.lastCheck || s.checkError !== state.checkError;
      state = s;
      if (changed) renderAll();
      else { if (lu) renderBanner(); if (chk) renderCheck(); if (view === 'about') renderAbout(); }
    } catch (e) {
      if (state.launcherUpdate && state.launcherUpdate.downloading) { // the launcher is restarting into the new version
        $('#lbanner').innerHTML = '<div class="lb-txt"><b>Updating Ironite Launcher…</b><span>It will open again in a moment.</span></div>';
        setTimeout(() => window.close(), 2500);
      }
    }
  }

  async function init() {
    state = await api('/api/state');
    catalog = await api('/api/catalog'); lastCat = state.catalogSize;
    $('#verSide').textContent = 'v' + state.version;
    $('#abVer').textContent = state.version;
    $('#abBuild').textContent = state.build;
    $('#abDir').textContent = state.installDir;
    document.title = `Ironite Launcher ${state.version}`;
    renderAll(); restartTimer();
    setInterval(refresh, 1500);
    setInterval(renderCheck, 30000);
  }
  init().catch(e => { document.body.innerHTML = '<p style="padding:40px">Launcher failed to start: ' + esc(e.message) + '</p>'; });
  document.addEventListener('contextmenu', e => e.preventDefault());
  // Tell the launcher the window is closing, so it quits right away.
  window.addEventListener('pagehide', () => { try { navigator.sendBeacon('/api/bye?t=' + encodeURIComponent(token)); } catch (e) { } });
})();
