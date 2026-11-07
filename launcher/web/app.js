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
  const cover = id => (Covers[id] || Covers.hiddenobj)();
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
  function renderBanner() {
    const u = state.launcherUpdate, b = $('#lbanner');
    if (!u) { b.classList.add('hidden'); return; }
    b.classList.remove('hidden');
    const btn = u.downloading
      ? `<button class="btn play small" disabled>Downloading <span class="bar"><i style="width:${u.progress}%"></i></span> ${u.progress}%</button>`
      : `<button class="btn play small" data-self>⟳&nbsp; Update now</button>`;
    b.innerHTML = `<div class="lb-txt"><b>Ironite Launcher ${esc(u.version)} is available</b>
      <span>${u.error ? esc(u.error) : u.downloading ? 'Downloading the update. The launcher will close, update and open again by itself (Windows asks for permission first).' : `You have ${esc(state.version)}. The launcher closes, updates and opens again by itself.`}</span></div>
      <div class="actions">${btn}<button class="btn ghost small" data-notes>What's new</button></div>`;
    const s = b.querySelector('[data-self]');
    if (s) s.onclick = async () => {
      try { const r = await api('/api/selfupdate', {}); state.launcherUpdate = r.launcherUpdate; renderBanner(); }
      catch (e) { toast(cap(e.message) + '.'); }
    };
    b.querySelector('[data-notes]').onclick = () => releaseDialog(`Ironite Launcher ${u.version}`, u.notes, u.url);
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
    if (where === 'installed') sub = (isWeb(g) ? 'Web game · ' : s.installedVersion ? `v${esc(s.installedVersion)} · ` : '') + `Played ${dur(played(s))}`;
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

  function section(title, count, blurb, list, where, extra = '') {
    return `<div class="cat"><div class="cat-h"><h2>${esc(title)}</h2><span class="count">${count}</span>${extra}</div>
      ${blurb ? `<p class="cat-b">${esc(blurb)}</p>` : ''}<div class="grid">${list.map(g => tile(g, where)).join('')}</div></div>`;
  }
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
        const count = ready === 0 ? 'Coming soon' : `${ready} available` + (list.length > ready ? ` · ${list.length - ready} coming soon` : '');
        return section(c.name, count, c.blurb, list, 'cat');
      }).join('');
    }
    $('#cats').innerHTML = html;
    document.querySelectorAll('.tile').forEach(t => t.onclick = e => {
      const chip = e.target.closest('.playchip');
      if (chip && chip.dataset.act) { e.stopPropagation(); act(t.dataset.id, chip.dataset.act); return; }
      details(t.dataset.id);
    });
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
      const v = isWeb(g) ? 'Web game' : s.installed
        ? `v${esc(s.installedVersion)} installed` + (s.update ? ` · v${esc(s.latestVersion)} available` : '')
        : `v${esc(s.latestVersion)}` + (s.size ? ` · ${size(s.size)} download` : '');
      $('#mSub').innerHTML = `${esc(g.subtitle)} · ${esc(g.players)} · ${v}`;
      $('#mDesc').textContent = g.description + (isWeb(g) ? ' Needs an internet connection; your progress is saved on this PC.' : '') +
        (demo(g) ? ' New demo versions arrive automatically through the launcher.' : '');
      $('#mMeta').innerHTML = `<span class="mplats">Plays on ${plays(g)}</span><span>Opened <b>${opens(s.playCount)}</b></span><span>Played <b>${dur(played(s))}</b></span><span>Last played <b>${ago(s.lastPlayed)}</b></span>` +
        (s.installed && s.installedAt ? `<span>Installed <b>${ago(s.installedAt)}</b></span>` : '');
      if (s.error) $('#mNotes').innerHTML = `<div class="err">${esc(s.error)}</div>`;
      else if (s.launchError) $('#mNotes').innerHTML = `<div class="err">${esc(s.launchError)}</div>`;
      else if (s.notes) $('#mNotes').innerHTML = `<div class="notes"><div class="nh">${s.update ? 'New in' : 'What’s new in'} version ${esc(s.latestVersion)}${s.publishedAt ? ` <span>· ${ago(s.publishedAt)}</span>` : ''}</div>${notesHtml(s.notes)}</div>`;
      $('#mActions').innerHTML = `<button class="btn ${p.cls} small" data-act="${p.act || ''}" ${p.act ? '' : 'disabled'}>${p.html}</button>
        ${s.error ? '<button class="btn ghost small" data-retry>Try again</button>' : ''}
        ${s.installed && s.update && !s.running && !s.installing ? '<button class="btn ghost small" data-act="play">▶&nbsp; Play ' + esc(s.installedVersion) + '</button>' : ''}
        ${s.installed && !s.running && !s.installing ? '<button class="btn danger" data-act="uninstall">Uninstall</button>' : ''}
        <button class="btn ghost small" data-close>Close</button>`;
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
      const changed = JSON.stringify([s.games, s.launcherUpdate, s.autoUpdate]) !== JSON.stringify([state.games, state.launcherUpdate, state.autoUpdate]);
      const chk = s.checking !== state.checking || s.lastCheck !== state.lastCheck || s.checkError !== state.checkError;
      state = s;
      if (changed) renderAll();
      else { if (chk) renderCheck(); if (view === 'about') renderAbout(); }
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
