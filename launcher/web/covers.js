'use strict';
/* Original cover art for every title, drawn as SVG (no external images). */
(function () {
  const RED = '#c8312b', INK = '#1d1d24';
  const isRed = s => s === '♥' || s === '♦';

  function card(x, y, rot, rank, suit, opts = {}) {
    const w = opts.w || 64, h = w * 1.4, col = isRed(suit) ? RED : INK;
    const face = opts.face || '#fbf7ee';
    return `<g transform="translate(${x} ${y}) rotate(${rot})">
      <rect x="${-w / 2 + 2}" y="${-h / 2 + 4}" width="${w}" height="${h}" rx="${w * .09}" fill="rgba(0,0,0,.35)"/>
      <rect x="${-w / 2}" y="${-h / 2}" width="${w}" height="${h}" rx="${w * .09}" fill="${face}" stroke="rgba(0,0,0,.25)"/>
      <text x="${-w / 2 + w * .1}" y="${-h / 2 + w * .3}" font-family="Georgia,serif" font-weight="700" font-size="${w * .26}" fill="${col}">${rank}</text>
      <text x="${-w / 2 + w * .1}" y="${-h / 2 + w * .52}" font-size="${w * .2}" fill="${col}">${suit}</text>
      <text x="0" y="${w * .2}" text-anchor="middle" font-size="${w * .62}" fill="${col}">${opts.center || suit}</text>
    </g>`;
  }
  function back(x, y, rot, w = 64, c1 = '#7b2a22', c2 = '#a83f2c') {
    const h = w * 1.4;
    return `<g transform="translate(${x} ${y}) rotate(${rot})">
      <rect x="${-w / 2 + 2}" y="${-h / 2 + 4}" width="${w}" height="${h}" rx="${w * .09}" fill="rgba(0,0,0,.35)"/>
      <rect x="${-w / 2}" y="${-h / 2}" width="${w}" height="${h}" rx="${w * .09}" fill="${c1}" stroke="#f3e7cf" stroke-width="3"/>
      <rect x="${-w / 2 + 7}" y="${-h / 2 + 7}" width="${w - 14}" height="${h - 14}" rx="4" fill="none" stroke="${c2}" stroke-width="2"/>
      <path d="M0 ${-h * .22} L${w * .22} 0 L0 ${h * .22} L${-w * .22} 0Z" fill="${c2}"/>
    </g>`;
  }
  const svg = (vb, body) => `<svg viewBox="${vb}" preserveAspectRatio="xMidYMid slice" xmlns="http://www.w3.org/2000/svg">${body}</svg>`;
  const felt = (id, a, b) => `<defs><radialGradient id="${id}" cx=".5" cy=".38" r=".85"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${b}"/></radialGradient></defs><rect width="100%" height="100%" fill="url(#${id})"/>`;
  const chips = (x, y, n, c) => Array.from({ length: n }, (_, i) =>
    `<g transform="translate(${x} ${y - i * 7})"><ellipse rx="22" ry="8" cy="4" fill="rgba(0,0,0,.35)"/><ellipse rx="22" ry="8" fill="${c}" stroke="#fff" stroke-width="2" stroke-dasharray="6 5"/></g>`).join('');

  /* --- Klondike: Yukon sunset, peaks, gold, fanned cards --- */
  function yukon(w, h, cardsAt) {
    return `<defs>
      <linearGradient id="ksky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2a1830"/><stop offset=".45" stop-color="#b04a2c"/><stop offset=".75" stop-color="#f0a14a"/><stop offset="1" stop-color="#f7cf7a"/></linearGradient>
      <linearGradient id="kgold" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff0a8"/><stop offset=".5" stop-color="#e7b532"/><stop offset="1" stop-color="#9c6a12"/></linearGradient>
    </defs>
    <rect width="${w}" height="${h}" fill="url(#ksky)"/>
    <circle cx="${w * .7}" cy="${h * .62}" r="${h * .16}" fill="#ffd98a" opacity=".85"/>
    <path d="M0 ${h * .7} L${w * .14} ${h * .44} L${w * .24} ${h * .58} L${w * .4} ${h * .3} L${w * .55} ${h * .56} L${w * .66} ${h * .42} L${w * .82} ${h * .62} L${w * .92} ${h * .48} L${w} ${h * .58} V${h} H0Z" fill="#3b2232"/>
    <path d="M${w * .4} ${h * .3} L${w * .45} ${h * .39} L${w * .42} ${h * .37} L${w * .38} ${h * .4} L${w * .36} ${h * .35}Z" fill="#f4e6e0" opacity=".8"/>
    <path d="M0 ${h * .8} Q${w * .3} ${h * .7} ${w * .55} ${h * .8} T${w} ${h * .76} V${h} H0Z" fill="#1f1420"/>
    ${Array.from({ length: 14 }, (_, i) => { const x = (i * 97) % w, s = 14 + (i * 7) % 16, y = h * .8 + (i % 3) * 6; return `<path d="M${x} ${y - s * 2.2} L${x + s * .6} ${y} H${x - s * .6}Z" fill="#140d16"/>`; }).join('')}
    ${cardsAt}`;
  }

  const C = {
    klondike: () => svg('0 0 400 500', yukon(400, 500,
      card(140, 365, -16, 'K', '♠', { w: 92, center: '♚' }) + card(200, 350, 0, 'Q', '♥', { w: 92, center: '♛' }) + card(260, 365, 16, 'A', '♦', { w: 92 }) +
      `<text x="200" y="150" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="50" letter-spacing="4" fill="#fff2d6" stroke="#3a1a10" stroke-width="1.5">KLONDIKE</text>
       <text x="200" y="180" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-size="15" letter-spacing="6" fill="#ffe6a6">GOLD RUSH SOLITAIRE</text>
       <g transform="translate(330 452)"><path d="M-20 0 Q-24 -16 -8 -20 Q4 -30 16 -18 Q26 -8 18 4 Q4 12 -20 0Z" fill="url(#kgold)" stroke="#6b470c"/></g>`)),

    klondikeHero: () => svg('0 0 1200 420', yukon(1200, 420,
      card(860, 250, -18, 'K', '♠', { w: 120, center: '♚' }) + card(950, 232, -4, 'Q', '♥', { w: 120, center: '♛' }) + card(1040, 240, 10, 'J', '♣', { w: 120, center: '♞' }) + card(1118, 270, 22, 'A', '♦', { w: 120 }) +
      `<g transform="translate(760 360)"><path d="M-26 0 Q-30 -20 -10 -26 Q6 -38 20 -22 Q32 -10 22 6 Q4 14 -26 0Z" fill="url(#kgold)" stroke="#6b470c"/></g>
       <g transform="translate(800 382) scale(.6)"><path d="M-26 0 Q-30 -20 -10 -26 Q6 -38 20 -22 Q32 -10 22 6 Q4 14 -26 0Z" fill="url(#kgold)" stroke="#6b470c"/></g>`)),

    holdem: () => svg('0 0 400 500', felt('fh', '#1f7a4a', '#0b2f1d') +
      `<ellipse cx="200" cy="300" rx="230" ry="170" fill="none" stroke="#5a3a1c" stroke-width="26"/>` +
      card(160, 230, -10, 'A', '♠', { w: 96 }) + card(240, 230, 10, 'A', '♥', { w: 96 }) +
      chips(110, 410, 5, '#c8312b') + chips(200, 420, 7, '#1d1d24') + chips(290, 410, 4, '#2a5bc4')),

    blackjack: () => svg('0 0 400 500', felt('fb', '#1d5d8a', '#0b2236') +
      `<path d="M30 160 A260 260 0 0 0 370 160" fill="none" stroke="#e7c35a" stroke-width="3" stroke-dasharray="2 8"/>
       <text x="200" y="150" text-anchor="middle" font-family="Bahnschrift,sans-serif" font-weight="700" font-size="64" fill="#e7c35a">21</text>` +
      card(165, 300, -8, 'A', '♣', { w: 100 }) + card(240, 310, 9, 'K', '♦', { w: 100, center: '♚' }) + chips(320, 440, 4, '#e7c35a')),

    gofish: () => svg('0 0 400 500', `<defs><linearGradient id="fw" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#4fc3e8"/><stop offset="1" stop-color="#0d3a6b"/></linearGradient></defs>
      <rect width="400" height="500" fill="url(#fw)"/>
      ${[80, 150, 230].map((y, i) => `<path d="M0 ${y} Q100 ${y - 14} 200 ${y} T400 ${y}" fill="none" stroke="rgba(255,255,255,.25)" stroke-width="3"/>`).join('')}
      ${[[90, 420, .6], [320, 380, .45], [300, 460, .35]].map(([x, y, s]) => `<circle cx="${x}" cy="${y}" r="${30 * s}" fill="none" stroke="rgba(255,255,255,.4)" stroke-width="2"/>`).join('')}
      <g transform="translate(200 300)"><path d="M-110 0 Q-40 -70 50 -10 L100 -50 L90 0 L100 50 L50 10 Q-40 70 -110 0Z" fill="#f2903a" stroke="#7a3d10" stroke-width="3"/><circle cx="-70" cy="-8" r="8" fill="#1d1d24"/><path d="M-20 -30 Q-5 0 -20 30" fill="none" stroke="#7a3d10" stroke-width="3"/></g>` +
      card(120, 150, -14, '7', '♥', { w: 70 }) + card(285, 140, 12, '7', '♠', { w: 70 })),

    oldmaid: () => '<img src="covers/oldmaid.jpg" alt="Old Maid">',
    oldmaidHero: () => '<img src="covers/oldmaid-hero.jpg" alt="Old Maid">',
    euchre: () => '<img src="covers/euchre.jpg" alt="Euchre">',
    euchreHero: () => '<img src="covers/euchre-hero.jpg" alt="Euchre">',
    hearts: () => '<img src="covers/hearts.jpg" alt="Hearts">',
    heartsHero: () => '<img src="covers/hearts-hero.jpg" alt="Hearts">',

    spades: () => svg('0 0 400 500', felt('fs', '#323a4f', '#0d1018') +
      `<path d="M200 90 C120 180 70 230 110 300 C140 350 185 330 192 310 L170 410 H230 L208 310 C215 330 260 350 290 300 C330 230 280 180 200 90Z" fill="#e9e3d6"/>` +
      card(300, 400, 14, 'A', '♠', { w: 70 }) + card(100, 410, -12, 'K', '♠', { w: 70, center: '♚' })),

    cribbage: () => svg('0 0 400 500', `<rect width="400" height="500" fill="#3b2516"/>
      <g transform="translate(200 250) rotate(-12)"><rect x="-90" y="-210" width="180" height="420" rx="26" fill="#b07a43" stroke="#6b4422" stroke-width="5"/>
      ${Array.from({ length: 3 }, (_, c) => Array.from({ length: 24 }, (_, r) => `<circle cx="${-40 + c * 40}" cy="${-180 + r * 15.6 + Math.floor(r / 5) * 2}" r="3.6" fill="#4a2c14"/>`).join('')).join('')}
      <rect x="-46" y="-10" width="12" height="34" rx="5" fill="#d9372b"/><rect x="-6" y="40" width="12" height="34" rx="5" fill="#2a5bc4"/><rect x="34" y="-90" width="12" height="34" rx="5" fill="#e7c35a"/></g>` +
      card(320, 420, 18, '5', '♥', { w: 70 }) + card(90, 150, -16, 'J', '♦', { w: 70, center: '♞' })),

    ginrummy: () => '<img src="covers/ginrummy.jpg" alt="Gin Rummy">',
    ginrummyHero: () => '<img src="covers/ginrummy-hero.jpg" alt="Gin Rummy">',

    chess: () => svg('0 0 400 500', `<defs><linearGradient id="chb" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1c1a22"/><stop offset="1" stop-color="#3a2f2a"/></linearGradient></defs>
      <rect width="400" height="500" fill="url(#chb)"/>
      <g transform="translate(200 330)">${Array.from({ length: 6 }, (_, r) => Array.from({ length: 8 }, (_, c) => { const y0 = r * 26, w0 = 300 + r * 30, x0 = -w0 / 2 + c * w0 / 8, w1 = 300 + (r + 1) * 30, x1 = -w1 / 2 + c * w1 / 8; return `<path d="M${x0} ${y0} L${x0 + w0 / 8} ${y0} L${x1 + w1 / 8} ${y0 + 26} L${x1} ${y0 + 26}Z" fill="${(r + c) % 2 ? '#6b4a2a' : '#e8d4ac'}"/>`; }).join('')).join('')}</g>
      <g transform="translate(150 300)" fill="#f2e8d6" stroke="#2b1d14" stroke-width="3"><path d="M-30 30 h60 l-6 -14 h-48z"/><path d="M-22 16 q-6 -40 10 -60 q-10 -10 0 -22 h24 q10 12 0 22 q16 20 10 60z"/><rect x="-4" y="-80" width="8" height="22"/><rect x="-12" y="-72" width="24" height="7"/></g>
      <g transform="translate(250 312)" fill="#2a2430" stroke="#c9b48a" stroke-width="3"><path d="M-26 22 h52 l-6 -12 h-40z"/><path d="M-18 10 q-4 -30 8 -44 q-26 -6 -20 -30 q14 -16 40 -6 q12 24 2 46 q10 14 6 34z"/><circle cx="2" cy="-58" r="3" fill="#c9b48a" stroke="none"/></g>
      <text x="200" y="168" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="58" letter-spacing="6" fill="#f2e8d6">CHESS</text>
      <path d="M120 188 H280" stroke="#c9a24c" stroke-width="2"/>`),

    checkers: () => svg('0 0 400 500', `<rect width="400" height="500" fill="#3a1c14"/>
      ${Array.from({ length: 8 }, (_, r) => Array.from({ length: 6 }, (_, c) => `<rect x="${c * 70 - 10}" y="${r * 70 - 20}" width="70" height="70" fill="${(r + c) % 2 ? '#1d1a1a' : '#b8312a'}"/>`).join('')).join('')}
      ${[[95, 250, '#1d1a1a'], [235, 180, '#b8312a'], [305, 320, '#1d1a1a'], [165, 390, '#b8312a']].map(([x, y, col]) => `<g transform="translate(${x} ${y})"><ellipse cy="10" rx="32" ry="10" fill="rgba(0,0,0,.45)"/><ellipse cy="6" rx="30" ry="9" fill="${col}" stroke="#f2e8d6" stroke-width="2"/><rect x="-30" y="-4" width="60" height="10" fill="${col}"/><ellipse cy="-4" rx="30" ry="9" fill="${col}" stroke="#f2e8d6" stroke-width="2"/><ellipse cy="-4" rx="20" ry="5.5" fill="none" stroke="rgba(255,255,255,.35)" stroke-width="2"/></g>`).join('')}
      <g transform="translate(235 168)"><path d="M-16 -8 l6 -12 l10 8 l10 -8 l6 12z" fill="#e8c35a" stroke="#7a5a1a" stroke-width="1.5"/></g>
      <rect x="40" y="112" width="320" height="76" rx="10" fill="rgba(20,10,8,.8)"/>
      <text x="200" y="170" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="52" letter-spacing="4" fill="#f2e8d6">CHECKERS</text>`),

    mahjong: () => '<img src="covers/mahjong.jpg" alt="Mahjong">',
    mahjongHero: () => '<img src="covers/mahjong-hero.jpg" alt="Mahjong">',
    maplelane: () => '<img src="covers/maplelane.jpg" alt="Maple Lane Finds">',
    maplelaneHero: () => '<img src="covers/maplelane-hero.jpg" alt="Maple Lane Finds">',
    snugfit: () => '<img src="covers/snugfit.jpg" alt="Snugfit">',
    snugfitHero: () => '<img src="covers/snugfit-hero.jpg" alt="Snugfit">',
    hiddenobj: () => svg('0 0 800 500', `<defs><radialGradient id="hl" cx=".56" cy=".5" r=".28"><stop offset="0" stop-color="#f3dcae"/><stop offset="1" stop-color="#f3dcae" stop-opacity="0"/></radialGradient></defs>
      <rect width="800" height="500" fill="#1b1f2c"/>
      <rect x="40" y="60" width="300" height="360" fill="#262b3c"/><rect x="60" y="80" width="260" height="20" fill="#3a3326"/><rect x="60" y="200" width="260" height="14" fill="#3a3326"/><rect x="60" y="320" width="260" height="14" fill="#3a3326"/>
      ${[[80, 150, '#5a4a7a'], [140, 140, '#7a3a3a'], [200, 160, '#3a6a5a'], [100, 270, '#7a6a3a'], [260, 280, '#3a4a7a']].map(([x, y, c]) => `<rect x="${x}" y="${y}" width="34" height="50" fill="${c}"/>`).join('')}
      <circle cx="620" cy="140" r="56" fill="#2d3346" stroke="#454c63" stroke-width="6"/><path d="M620 140 V104 M620 140 L644 152" stroke="#9aa2b8" stroke-width="5" stroke-linecap="round"/>
      <rect x="480" y="300" width="260" height="140" rx="8" fill="#3a2b20"/><rect x="500" y="280" width="70" height="24" fill="#5a4030"/>
      <rect width="800" height="500" fill="url(#hl)"/>
      <g transform="translate(450 250)"><circle r="84" fill="rgba(255,240,210,.12)" stroke="#e7c35a" stroke-width="12"/><circle r="84" fill="none" stroke="#7a5a1a" stroke-width="3"/><path d="M58 58 L130 130" stroke="#5a3a1c" stroke-width="26" stroke-linecap="round"/>
      <g transform="translate(-10 6)"><path d="M-14 -20 h28 l6 12 h-40Z M-18 -8 h36 v26 h-36Z" fill="#e7c35a"/><circle cx="0" cy="6" r="6" fill="#7a5a1a"/></g></g>`),
    solarsiegeHero: () => svg('0 0 1200 420', `<defs>
      <radialGradient id="shs" cx=".8" cy="1.25" r=".75"><stop offset="0" stop-color="#ffd27a"/><stop offset=".22" stop-color="#e8873a" stop-opacity=".75"/><stop offset=".6" stop-color="#3a1850" stop-opacity=".5"/><stop offset="1" stop-color="#05060f" stop-opacity="0"/></radialGradient>
      <radialGradient id="shp" cx=".35" cy=".35" r=".7"><stop offset="0" stop-color="#ffe2a8"/><stop offset=".6" stop-color="#d9a46a"/><stop offset="1" stop-color="#7a4a22"/></radialGradient>
      <linearGradient id="shl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7ef9ff" stop-opacity="0"/><stop offset="1" stop-color="#7ef9ff"/></linearGradient></defs>
      <rect width="1200" height="420" fill="#05060f"/><rect width="1200" height="420" fill="url(#shs)"/>
      ${Array.from({ length: 160 }, (_, i) => `<circle cx="${(i * 173) % 1200}" cy="${(i * 97) % 420}" r="${.7 + (i % 3) * .6}" fill="#fff" opacity="${.2 + (i % 5) * .14}"/>`).join('')}
      <circle cx="1050" cy="330" r="130" fill="url(#shp)" opacity=".95"/>
      <ellipse cx="1050" cy="330" rx="230" ry="44" fill="none" stroke="#e8d29a" stroke-width="7" opacity=".75" transform="rotate(-14 1050 330)"/>
      ${[[620, 60, 9, '#b5a89a'], [690, 110, 13, '#e6c27a'], [600, 300, 15, '#4f8fe0'], [700, 350, 10, '#d0603a'], [1160, 70, 12, '#8fd6e0'], [880, 40, 6, '#c9b9a8']].map(([x, y, r, c]) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${c}"/>`).join('')}
      ${(() => { const cols = ['#ff5f6d', '#5ef0a0', '#5ec8f0', '#ffd23f']; let o = ''; for (let r = 0; r < 3; r++) for (let c = 0; c < 6; c++) { const x = 760 + c * 62 + (r % 2) * 14, y = 70 + r * 46, col = cols[r % 4]; o += `<g transform="translate(${x} ${y}) scale(.85)" fill="${col}"><rect x="-24" y="-8" width="48" height="16"/><rect x="-16" y="-16" width="32" height="8"/><rect x="-32" y="0" width="8" height="16"/><rect x="24" y="0" width="8" height="16"/><rect x="-12" y="-4" width="6" height="6" fill="#05060f"/><rect x="6" y="-4" width="6" height="6" fill="#05060f"/></g>`; } return o; })()}
      <rect x="878" y="210" width="5" height="120" fill="url(#shl)"/>
      <rect x="840" y="250" width="4" height="22" rx="2" fill="#ff5f6d"/>
      <g transform="translate(880 370) scale(1.5)"><path d="M0 -42 L14 -6 L36 10 L36 22 L10 16 L6 28 H-6 L-10 16 L-36 22 L-36 10 L-14 -6Z" fill="#e9eef7" stroke="#7a8aa8" stroke-width="2"/><path d="M0 -30 L6 -10 H-6Z" fill="#5ec8f0"/><path d="M-6 28 L0 46 L6 28Z" fill="#ffb347"/></g>`),
    dookieclicker: () => svg('0 0 400 500', `<defs>
      <radialGradient id="dkb" cx=".5" cy=".45" r=".75"><stop offset="0" stop-color="#ffe9a0"/><stop offset=".55" stop-color="#f3b24a"/><stop offset="1" stop-color="#9a4f12"/></radialGradient>
      <linearGradient id="dkp" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#a8683a"/><stop offset="1" stop-color="#5e3418"/></linearGradient></defs>
      <rect width="400" height="500" fill="url(#dkb)"/>
      ${Array.from({ length: 12 }, (_, i) => `<path d="M200 250 L${200 + 420 * Math.cos(i * Math.PI / 6)} ${250 + 420 * Math.sin(i * Math.PI / 6)} L${200 + 420 * Math.cos(i * Math.PI / 6 + .22)} ${250 + 420 * Math.sin(i * Math.PI / 6 + .22)}Z" fill="#fff" opacity=".12"/>`).join('')}
      <ellipse cx="200" cy="356" rx="118" ry="20" fill="rgba(80,40,10,.3)"/>
      <g stroke="#4a2810" stroke-width="4" stroke-linejoin="round">
        <path d="M86 340 Q80 300 118 296 L282 296 Q320 300 314 340 Q312 356 290 356 H110 Q88 356 86 340Z" fill="url(#dkp)"/>
        <path d="M110 300 Q104 262 140 258 L260 258 Q296 262 290 300Z" fill="url(#dkp)"/>
        <path d="M138 262 Q132 228 164 224 L236 224 Q268 228 262 262Z" fill="url(#dkp)"/>
        <path d="M168 228 Q170 196 200 180 Q214 196 232 204 Q240 214 232 228Z" fill="url(#dkp)"/>
      </g>
      <path d="M120 312 Q200 322 280 312" stroke="rgba(255,255,255,.25)" stroke-width="6" fill="none" stroke-linecap="round"/>
      <ellipse cx="170" cy="286" rx="14" ry="17" fill="#fff"/><ellipse cx="230" cy="286" rx="14" ry="17" fill="#fff"/>
      <circle cx="173" cy="290" r="7" fill="#1d1d24"/><circle cx="233" cy="290" r="7" fill="#1d1d24"/>
      <circle cx="175" cy="287" r="2.5" fill="#fff"/><circle cx="235" cy="287" r="2.5" fill="#fff"/>
      <path d="M180 320 Q200 340 220 320" stroke="#2a1608" stroke-width="5" fill="#c0392b" stroke-linecap="round"/>
      <ellipse cx="148" cy="314" rx="12" ry="6" fill="#ff8f8f" opacity=".55"/><ellipse cx="252" cy="314" rx="12" ry="6" fill="#ff8f8f" opacity=".55"/>
      <g transform="translate(300 360) rotate(-24)"><path d="M0 0 V-46 Q0 -54 8 -54 Q16 -54 16 -46 V-20 L40 -16 Q50 -14 48 -2 L44 22 Q42 32 30 32 H8 Q-2 32 -6 22 L-14 2 Q-18 -8 -8 -10Z" fill="#fff" stroke="#1d1d24" stroke-width="4" stroke-linejoin="round"/></g>
      <text x="306" y="236" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="34" fill="#fff" stroke="#9a4f12" stroke-width="1.5">+1</text>
      <text x="60" y="262" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="24" fill="#fff" opacity=".85">+1</text>
      ${[[70, 420], [330, 440], [110, 460]].map(([x, y]) => `<g transform="translate(${x} ${y})"><circle r="16" fill="#ffd23f" stroke="#a87412" stroke-width="3"/><text y="7" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="18" fill="#a87412">$</text></g>`).join('')}
      <text x="200" y="134" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="42" letter-spacing="2" fill="#fff8e6" stroke="#6b3410" stroke-width="2">DOOKIE</text>
      <text x="200" y="163" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="21" letter-spacing="8" fill="#6b3410">CLICKER</text>`),

    simcat: () => svg('0 0 800 500', `<defs><linearGradient id="smk" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#8fd0f0"/><stop offset="1" stop-color="#d9f0e4"/></linearGradient></defs>
      <rect width="800" height="500" fill="url(#smk)"/>
      <circle cx="660" cy="90" r="40" fill="#ffe08a"/>
      <path d="M0 330 Q200 290 400 320 T800 310 V500 H0Z" fill="#7fc47a"/><path d="M0 380 Q240 350 480 372 T800 360 V500 H0Z" fill="#5aa85a"/>
      <path d="M120 420 Q330 360 470 400 T800 380" stroke="#c9b48a" stroke-width="22" fill="none"/>
      ${[[150, 330, '#e8873a', '#9a3a20'], [260, 312, '#5e9ef0', '#2a4f8a'], [520, 320, '#e7c35a', '#8a6a1a'], [640, 330, '#c86ad8', '#6a2a7a']].map(([x, y, w, r]) => `<g transform="translate(${x} ${y})"><rect x="-40" y="-40" width="80" height="60" fill="${w}"/><path d="M-50 -38 L0 -82 L50 -38Z" fill="${r}"/><rect x="-10" y="-8" width="20" height="28" fill="#5a3a20"/><rect x="-32" y="-30" width="16" height="14" fill="#fff8dc"/><rect x="16" y="-30" width="16" height="14" fill="#fff8dc"/></g>`).join('')}
      ${[[60, 360], [370, 340], [740, 350], [440, 300]].map(([x, y]) => `<g transform="translate(${x} ${y})"><rect x="-4" y="0" width="8" height="26" fill="#6a4a2a"/><circle cy="-8" r="24" fill="#3e8e4a"/></g>`).join('')}
      <g transform="translate(400 200)"><rect x="-28" y="-60" width="56" height="70" fill="#9aa4b8"/><rect x="-18" y="-50" width="12" height="12" fill="#fff8dc"/><rect x="6" y="-50" width="12" height="12" fill="#fff8dc"/><rect x="-18" y="-28" width="12" height="12" fill="#fff8dc"/><rect x="6" y="-28" width="12" height="12" fill="#fff8dc"/></g>`),
    rewindlife: () => svg('0 0 400 500', `<defs><linearGradient id="rws" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2a0f4a"/><stop offset=".55" stop-color="#e8389a"/><stop offset="1" stop-color="#ffb347"/></linearGradient></defs>
      <rect width="400" height="500" fill="url(#rws)"/>
      ${Array.from({ length: 7 }, (_, i) => `<rect x="70" y="${170 + i * 14}" width="260" height="${8 - i}" fill="#2a0f4a" opacity=".55"/>`).join('')}
      <circle cx="200" cy="200" r="90" fill="#ffd23f" opacity=".9"/>
      ${Array.from({ length: 6 }, (_, i) => `<rect x="100" y="${196 + i * 13}" width="200" height="${3 + i}" fill="#e8389a"/>`).join('')}
      <path d="M0 330 L400 330 L400 500 L0 500Z" fill="#14072a"/>
      ${Array.from({ length: 11 }, (_, i) => `<path d="M${200 + (i - 5) * 18} 330 L${200 + (i - 5) * 110} 500" stroke="#5ec8f0" stroke-width="2" opacity=".7"/>`).join('')}
      ${[350, 375, 410, 455].map(y => `<path d="M0 ${y} H400" stroke="#5ec8f0" stroke-width="2" opacity=".7"/>`).join('')}
      <g transform="translate(110 290)"><rect x="-40" y="-36" width="80" height="44" fill="#f3e6c8"/><path d="M-48 -34 L0 -70 L48 -34Z" fill="#c8312b"/><rect x="-8" y="-14" width="16" height="22" fill="#5a3a20"/><rect x="-32" y="-26" width="14" height="12" fill="#5ec8f0"/><rect x="18" y="-26" width="14" height="12" fill="#5ec8f0"/></g>
      <g transform="translate(290 300) rotate(-8)"><rect x="-56" y="-34" width="112" height="70" rx="6" fill="#1d1d24" stroke="#f3e6c8" stroke-width="3"/><rect x="-44" y="-24" width="88" height="26" rx="3" fill="#f3e6c8"/><circle cx="-22" cy="-11" r="8" fill="#1d1d24"/><circle cx="22" cy="-11" r="8" fill="#1d1d24"/><rect x="-26" y="14" width="52" height="14" fill="#333"/></g>
      <text x="200" y="138" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="46" letter-spacing="3" fill="#fff" stroke="#2a0f4a" stroke-width="2" font-style="italic">REWIND</text>
      <text x="200" y="168" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="22" letter-spacing="12" fill="#5ec8f0">LIFE</text>`),

    rewindlifeHero: () => svg('0 0 1200 420', `<defs><linearGradient id="rwh" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1a0833"/><stop offset=".5" stop-color="#7a1f6e"/><stop offset=".74" stop-color="#e8389a"/><stop offset="1" stop-color="#ffb347"/></linearGradient>
      <linearGradient id="rwsun" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffe66b"/><stop offset="1" stop-color="#ff7a3d"/></linearGradient></defs>
      <rect width="1200" height="420" fill="url(#rwh)"/>
      ${Array.from({ length: 40 }, (_, i) => `<circle cx="${(i * 197) % 1200}" cy="${(i * 53) % 150}" r="${i % 3 ? 1.2 : 2}" fill="#fff" opacity="${.3 + (i % 4) * .15}"/>`).join('')}
      <circle cx="860" cy="250" r="130" fill="url(#rwsun)"/>
      ${Array.from({ length: 7 }, (_, i) => `<rect x="720" y="${232 + i * 15}" width="280" height="${3 + i * 1.3}" fill="#9a2a7e"/>`).join('')}
      <path d="M560 300 L640 230 L700 270 L780 205 L860 268 L930 222 L1010 280 L1090 238 L1200 300Z" fill="#3a0f4e" opacity=".85"/>
      <path d="M0 300 H1200 V420 H0Z" fill="#14072a"/>
      ${Array.from({ length: 25 }, (_, i) => `<path d="M${860 + (i - 12) * 34} 300 L${860 + (i - 12) * 190} 420" stroke="#5ec8f0" stroke-width="2" opacity=".55"/>`).join('')}
      ${[312, 330, 356, 392].map(y => `<path d="M0 ${y} H1200" stroke="#5ec8f0" stroke-width="2" opacity=".55"/>`).join('')}
      ${[[640, 296, '#f3e6c8', '#c8312b'], [760, 300, '#9ad0e8', '#2a4f8a'], [1080, 298, '#f7d58a', '#8a3a20']].map(([x, y, w, r]) => `<g transform="translate(${x} ${y})"><rect x="-46" y="-44" width="92" height="44" fill="${w}"/><path d="M-54 -42 L0 -82 L54 -42Z" fill="${r}"/><rect x="-9" y="-24" width="18" height="24" fill="#5a3a20"/><rect x="-36" y="-34" width="16" height="13" fill="#ffd23f"/><rect x="20" y="-34" width="16" height="13" fill="#ffd23f"/></g>`).join('')}
      ${[[700, 300], [1000, 300], [1150, 300]].map(([x, y]) => `<g transform="translate(${x} ${y})"><rect x="-3" y="-26" width="6" height="26" fill="#2a0f3a"/><circle cy="-36" r="18" fill="#2a0f3a"/></g>`).join('')}
      <g transform="translate(960 372) rotate(-7)"><rect x="-74" y="-44" width="148" height="92" rx="8" fill="#1d1d24" stroke="#f3e6c8" stroke-width="4"/><rect x="-58" y="-32" width="116" height="34" rx="4" fill="#f3e6c8"/><circle cx="-28" cy="-15" r="10" fill="#1d1d24"/><circle cx="28" cy="-15" r="10" fill="#1d1d24"/><rect x="-34" y="18" width="68" height="18" fill="#3a3a44"/><text x="0" y="-36" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="10" letter-spacing="2" fill="#e8389a" dy="0"></text></g>`),

    lifeboat: () => svg('0 0 400 500', `<defs><linearGradient id="lbs" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#6a8aa8"/><stop offset=".5" stop-color="#c9d6de"/><stop offset="1" stop-color="#e9d8b8"/></linearGradient><linearGradient id="lbw" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2a6a8a"/><stop offset="1" stop-color="#0b2236"/></linearGradient></defs>
      <rect width="400" height="500" fill="url(#lbs)"/>
      <circle cx="300" cy="220" r="34" fill="#fff4d0" opacity=".85"/>
      <path d="M0 290 Q50 270 100 290 T200 290 T300 290 T400 290 V500 H0Z" fill="url(#lbw)"/>
      <g transform="translate(200 312) rotate(-6)"><path d="M-110 -10 H110 L84 34 H-84Z" fill="#e8622a" stroke="#7a2a10" stroke-width="3"/><rect x="-110" y="-20" width="220" height="12" rx="4" fill="#f3e6c8"/><path d="M-100 0 H100" stroke="#fff" stroke-width="3" stroke-dasharray="10 8"/><rect x="-4" y="-120" width="7" height="102" fill="#6a4a2a"/><path d="M3 -116 L70 -60 L3 -40Z" fill="#f3e6c8" stroke="#a89878" stroke-width="2"/><circle cx="-56" cy="-34" r="11" fill="#e9b28a"/><rect x="-66" y="-24" width="20" height="18" rx="4" fill="#e8873a"/></g>
      ${[[0, 360], [0, 400], [0, 450]].map(([x, y], i) => `<path d="M${x} ${y} Q50 ${y - 14} 100 ${y} T200 ${y} T300 ${y} T400 ${y}" stroke="rgba(255,255,255,.35)" stroke-width="3" fill="none"/>`).join('')}
      <path d="M70 160 q10 -10 20 0 q10 -10 20 0" stroke="#3a4a5a" stroke-width="3" fill="none"/>
      <text x="200" y="120" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="40" letter-spacing="3" fill="#0b2236">LIFEBOAT</text>
      <text x="200" y="150" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="17" letter-spacing="8" fill="#2a4a6a">SIMULATOR</text>`),

    prisoncell: () => svg('0 0 400 500', `<rect width="400" height="500" fill="#3a3d44"/>
      ${Array.from({ length: 12 }, (_, r) => Array.from({ length: 6 }, (_, c) => `<rect x="${c * 70 + (r % 2) * 35 - 20}" y="${r * 42}" width="66" height="38" fill="#464a52" stroke="#33363c" stroke-width="2"/>`).join('')).join('')}
      <rect x="150" y="150" width="100" height="80" fill="#9fc7e8"/><rect x="150" y="150" width="100" height="80" fill="none" stroke="#2a2c31" stroke-width="6"/>
      ${[175, 200, 225].map(x => `<rect x="${x - 3}" y="150" width="6" height="80" fill="#2a2c31"/>`).join('')}
      <path d="M150 230 L250 230 L320 300 L80 300Z" fill="#9fc7e8" opacity=".15"/>
      <rect x="40" y="350" width="200" height="34" rx="4" fill="#6a6e78"/><rect x="40" y="340" width="200" height="16" rx="6" fill="#d9d4c8"/><rect x="46" y="384" width="10" height="40" fill="#2a2c31"/><rect x="224" y="384" width="10" height="40" fill="#2a2c31"/>
      <path d="M290 380 h50 v40 h-50z" fill="#c9cdd4"/><ellipse cx="315" cy="380" rx="28" ry="10" fill="#e9ecf0"/>
      <rect x="0" y="424" width="400" height="76" fill="#2c2f35"/>
      ${Array.from({ length: 7 }, (_, i) => `<rect x="${10 + i * 58}" y="0" width="12" height="500" fill="#1d1f24"/><rect x="${13 + i * 58}" y="0" width="3" height="500" fill="#5a5e68"/>`).join('')}
      <rect x="0" y="70" width="400" height="10" fill="#1d1f24"/>
      <g fill="#e7e2d6" opacity=".55">${[[300, 250], [312, 250], [324, 250], [336, 250], [348, 250]].map(([x, y]) => `<rect x="${x}" y="${y}" width="3" height="22"/>`).join('')}<path d="M296 266 L354 254" stroke="#e7e2d6" stroke-width="3"/></g>
      <text x="200" y="118" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="30" letter-spacing="3" fill="#f3efe6" stroke="#1d1f24" stroke-width="4" paint-order="stroke">PRISON CELL</text>
      <text x="200" y="142" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="15" letter-spacing="8" fill="#e8873a" stroke="#1d1f24" stroke-width="3" paint-order="stroke">SIMULATOR</text>`),

    bigtop: () => svg('0 0 400 500', `<defs><linearGradient id="bts" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1b1446"/><stop offset=".55" stop-color="#5a2a6a"/><stop offset="1" stop-color="#e8873a"/></linearGradient><clipPath id="btc"><path d="M80 330 L200 170 L320 330Z"/></clipPath></defs>
      <rect width="400" height="500" fill="url(#bts)"/>
      ${[[40, 60], [110, 40], [330, 50], [360, 120], [60, 140], [250, 30]].map(([x, y]) => `<circle cx="${x}" cy="${y}" r="2" fill="#fff4d0"/>`).join('')}
      <g transform="translate(320 190)">${Array.from({ length: 12 }, (_, i) => `<path d="M0 0 L${Math.cos(i * Math.PI / 6) * 26} ${Math.sin(i * Math.PI / 6) * 26}" stroke="${['#ffd23f', '#ff5fb4', '#5ec8f0'][i % 3]}" stroke-width="3"/>`).join('')}</g>
      <g transform="translate(70 230)"><circle r="44" fill="none" stroke="#f3e6c8" stroke-width="4"/>${Array.from({ length: 8 }, (_, i) => `<path d="M0 0 L${Math.cos(i * Math.PI / 4) * 44} ${Math.sin(i * Math.PI / 4) * 44}" stroke="#f3e6c8" stroke-width="2"/><rect x="${Math.cos(i * Math.PI / 4) * 44 - 6}" y="${Math.sin(i * Math.PI / 4) * 44 - 4}" width="12" height="10" rx="2" fill="${['#e84a5f', '#ffd23f', '#5ec8f0', '#5fc489'][i % 4]}"/>`).join('')}<path d="M-20 70 L0 0 L20 70" stroke="#f3e6c8" stroke-width="4" fill="none"/></g>
      <rect x="0" y="380" width="400" height="120" fill="#3a7a3a"/><rect x="0" y="380" width="400" height="6" fill="#4f9a4a"/>
      <g clip-path="url(#btc)"><rect x="80" y="170" width="240" height="160" fill="#f3e6c8"/>${Array.from({ length: 7 }, (_, i) => `<path d="M200 170 L${92 + i * 36} 330 L${110 + i * 36} 330Z" fill="#c8312b"/>`).join('')}</g>
      <path d="M80 330 Q100 344 120 330 Q140 344 160 330 Q180 344 200 330 Q220 344 240 330 Q260 344 280 330 Q300 344 320 330" fill="#ffd23f" stroke="#8a3a20" stroke-width="2"/>
      <rect x="92" y="336" width="216" height="50" fill="#f3e6c8"/>${Array.from({ length: 6 }, (_, i) => `<rect x="${92 + i * 36}" y="336" width="18" height="50" fill="#c8312b"/>`).join('')}
      <path d="M176 386 V352 Q200 334 224 352 V386Z" fill="#1d1d24"/><path d="M176 352 Q200 334 224 352" stroke="#ffd23f" stroke-width="4" fill="none"/>
      <rect x="198" y="140" width="4" height="32" fill="#5a3a20"/><path d="M202 142 L232 150 L202 158Z" fill="#ffd23f"/>
      <path d="M80 330 L40 384 M320 330 L360 384" stroke="#5a3a20" stroke-width="2"/>
      ${[[30, 420], [64, 440], [330, 430], [366, 416], [300, 456]].map(([x, y], i) => `<g transform="translate(${x} ${y})"><circle cy="-30" r="8" fill="#e9b28a"/><rect x="-8" y="-22" width="16" height="22" rx="4" fill="${['#5e9ef0', '#e8873a', '#c86ad8', '#5fc489', '#e84a5f'][i]}"/></g>`).join('')}
      <path d="M0 404 Q200 380 400 404" stroke="rgba(255,244,208,.6)" stroke-width="2" fill="none"/>
      ${Array.from({ length: 11 }, (_, i) => `<circle cx="${i * 40 + 4}" cy="${404 - Math.sin(i / 10 * Math.PI) * 18}" r="4" fill="${['#ffd23f', '#ff5fb4', '#5ec8f0'][i % 3]}"/>`).join('')}
      <text x="200" y="86" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="44" letter-spacing="4" fill="#ffd23f" stroke="#1b1446" stroke-width="5" paint-order="stroke">BIG TOP</text>
      <text x="200" y="120" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="22" letter-spacing="10" fill="#f3e6c8" stroke="#1b1446" stroke-width="4" paint-order="stroke">BUILDER</text>`),

    carnival: () => svg('0 0 400 500', `<defs><linearGradient id="ccs" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0b0e27"/><stop offset=".6" stop-color="#2a2160"/><stop offset="1" stop-color="#4a2a6a"/></linearGradient><radialGradient id="ccg" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#f4b942" stop-opacity=".4"/><stop offset="1" stop-color="#f4b942" stop-opacity="0"/></radialGradient></defs>
      <rect width="400" height="500" fill="url(#ccs)"/>
      ${[[30, 40], [150, 30], [370, 30], [340, 140], [20, 190], [260, 130]].map(([x, y]) => `<circle cx="${x}" cy="${y}" r="2" fill="#fff4dc"/>`).join('')}
      <circle cx="300" cy="260" r="130" fill="url(#ccg)"/>
      <g transform="translate(300 260)"><path d="M-40 130 L0 0 L40 130" stroke="#e8d9b5" stroke-width="5" fill="none"/><circle r="86" fill="none" stroke="#f4b942" stroke-width="5"/><circle r="64" fill="none" stroke="#e23b3b" stroke-width="3"/>${Array.from({ length: 12 }, (_, i) => { const a = i * Math.PI / 6, x = Math.cos(a) * 86, y = Math.sin(a) * 86; return `<path d="M0 0 L${x} ${y}" stroke="#e8d9b5" stroke-width="2"/><rect x="${x - 8}" y="${y + 2}" width="16" height="13" rx="3" fill="${['#e23b3b', '#2bb3a3', '#f4b942'][i % 3]}"/>`; }).join('')}<circle r="10" fill="#e23b3b" stroke="#f4b942" stroke-width="3"/></g>
      <g stroke="#f4b942" stroke-width="3" fill="none"><path d="M60 392 V160 M84 392 V160"/>${Array.from({ length: 10 }, (_, i) => `<path d="M60 ${160 + i * 23} L84 ${183 + i * 23}"/>`).join('')}<path d="M30 160 H230 M30 172 H230"/>${Array.from({ length: 8 }, (_, i) => `<path d="M${30 + i * 25} 172 L${55 + i * 25} 160"/>`).join('')}<path d="M72 160 L72 136 L30 160 M72 136 L230 160"/></g>
      <rect x="22" y="174" width="26" height="22" fill="#5a5e68"/>
      <path d="M200 172 V244" stroke="#e8d9b5" stroke-width="2"/>
      <g transform="translate(200 262)"><path d="M-30 -14 H30 L22 16 Q0 26 -22 16Z" fill="#2bb3a3" stroke="#fff4dc" stroke-width="2"/><path d="M30 -6 Q44 -4 40 8 Q36 14 26 10" stroke="#fff4dc" stroke-width="4" fill="none"/><circle cy="-14" r="4" fill="#f4b942"/><path d="M-18 -18 L0 -30 L18 -18" stroke="#e8d9b5" stroke-width="2" fill="none"/></g>
      <rect x="0" y="390" width="400" height="110" fill="#0d1030"/>
      <path d="M150 390 H250 L310 500 H90Z" fill="#3a2f4f"/>
      <g transform="translate(110 320)"><rect width="130" height="70" fill="#5b2b4a"/><rect y="40" width="130" height="30" fill="#3a1b30"/><rect x="-8" y="-24" width="146" height="26" fill="#fff4dc"/>${Array.from({ length: 4 }, (_, i) => `<rect x="${-8 + i * 37}" y="-24" width="18" height="26" fill="#e23b3b"/>`).join('')}${Array.from({ length: 8 }, (_, i) => `<circle cx="${-2 + i * 19}" cy="2" r="9" fill="${i % 2 ? '#fff4dc' : '#e23b3b'}"/>`).join('')}${Array.from({ length: 3 }, (_, i) => `<ellipse cx="${30 + i * 35}" cy="26" rx="9" ry="4" fill="none" stroke="#f4b942" stroke-width="3"/>`).join('')}</g>
      <path d="M0 300 Q130 350 260 300" stroke="rgba(255,244,208,.6)" stroke-width="2" fill="none"/>
      ${Array.from({ length: 9 }, (_, i) => { const t = i / 8; return `<circle cx="${t * 260}" cy="${300 + 2 * t * (1 - t) * 50}" r="4" fill="${['#f4b942', '#e23b3b', '#2bb3a3'][i % 3]}"/>`; }).join('')}
      ${[[40, 440], [74, 456], [330, 440], [366, 452], [300, 470]].map(([x, y], i) => `<g transform="translate(${x} ${y})"><circle cy="-30" r="8" fill="#e9b28a"/><rect x="-8" y="-22" width="16" height="22" rx="4" fill="${['#2bb3a3', '#e23b3b', '#f4b942', '#c86ad8', '#5e9ef0'][i]}"/></g>`).join('')}
      <text x="200" y="76" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="44" letter-spacing="4" fill="#f4b942" stroke="#0b0e27" stroke-width="5" paint-order="stroke">CARNIVAL</text>
      <text x="200" y="108" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="20" letter-spacing="7" fill="#fff4dc" stroke="#0b0e27" stroke-width="4" paint-order="stroke">CONSTRUCTOR</text>`),

    appalachian: () => svg('0 0 400 500', `<defs><linearGradient id="ats" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f6c98a"/><stop offset=".5" stop-color="#e8a06a"/><stop offset="1" stop-color="#b8775a"/></linearGradient></defs>
      <rect width="400" height="500" fill="url(#ats)"/>
      <path d="M0 250 Q60 190 120 220 T240 200 T400 215 V500 H0Z" fill="#7a6a8a" opacity=".7"/>
      <path d="M0 290 Q80 230 160 265 T320 250 T400 260 V500 H0Z" fill="#4f6a5a"/>
      <path d="M0 340 Q100 290 200 320 T400 310 V500 H0Z" fill="#2f4a3a"/>
      <path d="M150 500 Q180 430 230 400 Q270 380 250 340" stroke="#c9a878" stroke-width="18" fill="none" stroke-linecap="round"/>
      ${[[50, 420, 1.2], [100, 390, .9], [320, 410, 1.3], [370, 380, 1], [290, 360, .8]].map(([x, y, k]) => `<g transform="translate(${x} ${y}) scale(${k})"><rect x="-3" y="0" width="6" height="34" fill="#4a3220"/><path d="M0 -50 L22 10 H-22Z" fill="#1f3a2a"/><path d="M0 -70 L16 -20 H-16Z" fill="#24442f"/></g>`).join('')}
      <g transform="translate(320 460)"><rect x="-12" y="-110" width="24" height="120" fill="#5a3a22"/><rect x="-7" y="-80" width="14" height="24" fill="#fff"/></g>
      <g transform="translate(225 372)"><circle cy="-36" r="6" fill="#3a2a1a"/><rect x="-5" y="-30" width="10" height="20" rx="3" fill="#c8312b"/><rect x="-11" y="-30" width="9" height="18" rx="3" fill="#6a4a2a"/><path d="M-3 -10 L-7 4 M3 -10 L6 4" stroke="#3a2a1a" stroke-width="4" stroke-linecap="round"/><path d="M8 -26 L14 4" stroke="#6a4a2a" stroke-width="2"/></g>
      <text x="200" y="104" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-size="15" letter-spacing="8" fill="#3a2a1a">THE</text>
      <text x="200" y="142" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="36" letter-spacing="2" fill="#fff8ec" stroke="#5a3a22" stroke-width="1.5">APPALACHIAN</text>
      <text x="200" y="172" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="20" letter-spacing="10" fill="#2f4a3a">TRAIL</text>`),
    solarsiege: () => svg('0 0 400 500', `<defs><radialGradient id="ssun" cx=".5" cy="1.05" r=".75"><stop offset="0" stop-color="#ffcf6a"/><stop offset=".25" stop-color="#e8873a" stop-opacity=".7"/><stop offset="1" stop-color="#05060f" stop-opacity="0"/></radialGradient></defs>
      <rect width="400" height="500" fill="#05060f"/><rect width="400" height="500" fill="url(#ssun)"/>
      ${Array.from({ length: 70 }, (_, i) => `<circle cx="${(i * 131) % 400}" cy="${(i * 89) % 420}" r="${.8 + (i % 3) * .6}" fill="#fff" opacity="${.25 + (i % 4) * .18}"/>`).join('')}
      ${[[40, 230, 7, '#b5a89a'], [78, 205, 10, '#e6c27a'], [124, 190, 11, '#4f8fe0'], [168, 182, 8, '#d0603a'], [222, 178, 20, '#d9a46a'], [282, 182, 17, '#e8d29a'], [330, 196, 12, '#8fd6e0'], [362, 214, 11, '#4a6ae0'], [386, 236, 5, '#c9b9a8']].map(([x, y, r, c], i) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${c}"/>${i === 5 ? `<ellipse cx="${x}" cy="${y}" rx="${r * 1.8}" ry="${r * .45}" fill="none" stroke="#d9c38a" stroke-width="2.5"/>` : ''}`).join('')}
      ${[[110, 132, '#5ef0a0'], [200, 118, '#ff5f6d'], [290, 132, '#5ec8f0']].map(([x, y, c]) => `<g transform="translate(${x} ${y})" fill="${c}"><rect x="-24" y="-8" width="48" height="16"/><rect x="-16" y="-16" width="32" height="8"/><rect x="-32" y="0" width="8" height="16"/><rect x="24" y="0" width="8" height="16"/><rect x="-12" y="-4" width="6" height="6" fill="#05060f"/><rect x="6" y="-4" width="6" height="6" fill="#05060f"/></g>`).join('')}
      <path d="M200 285 V350" stroke="#7ef9ff" stroke-width="4" stroke-dasharray="12 10"/>
      <g transform="translate(200 400)"><path d="M0 -42 L14 -6 L36 10 L36 22 L10 16 L6 28 H-6 L-10 16 L-36 22 L-36 10 L-14 -6Z" fill="#e9eef7" stroke="#7a8aa8" stroke-width="2"/><path d="M0 -30 L6 -10 H-6Z" fill="#5ec8f0"/><path d="M-6 28 L0 48 L6 28Z" fill="#ffb347"/></g>
      <text x="200" y="478" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="34" letter-spacing="6" fill="#fff2d6">SOLAR SIEGE</text>`),
    keyhole: () => svg('0 0 400 500', `<defs><linearGradient id="khb" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0c1517"/><stop offset=".55" stop-color="#16262a"/><stop offset="1" stop-color="#0a1012"/></linearGradient><linearGradient id="khr" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f6dc8e"/><stop offset=".45" stop-color="#c9922e"/><stop offset="1" stop-color="#6e4a14"/></linearGradient><radialGradient id="khp" cx=".42" cy=".38" r=".75"><stop offset="0" stop-color="#3b4a4c"/><stop offset="1" stop-color="#162124"/></radialGradient><radialGradient id="khg" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#ffe6a3" stop-opacity=".95"/><stop offset=".4" stop-color="#f4b942" stop-opacity=".45"/><stop offset="1" stop-color="#f4b942" stop-opacity="0"/></radialGradient></defs>
      <rect width="400" height="500" fill="url(#khb)"/>
      <g stroke="#ffffff" stroke-opacity=".05" stroke-width="2">${[130, 400].map(y => `<path d="M0 ${y} H400"/>`).join('')}<path d="M40 0 V500 M360 0 V500"/></g>
      ${[[40, 130], [360, 130], [40, 400], [360, 400]].map(([x, y]) => `<circle cx="${x}" cy="${y}" r="4" fill="#2c3b3e" stroke="#4d5f62"/>`).join('')}
      <g transform="translate(200 262)">
        <circle r="138" fill="rgba(0,0,0,.45)" cy="6"/>
        <circle r="134" fill="url(#khr)"/>
        <circle r="118" fill="#2a1e0c" opacity=".55"/>
        <circle r="114" fill="url(#khp)" stroke="#8a6420" stroke-width="3"/>
        ${Array.from({ length: 60 }, (_, i) => { const a = i * Math.PI / 30, r1 = i % 5 ? 124 : 119; return `<path d="M${Math.cos(a) * r1} ${Math.sin(a) * r1} L${Math.cos(a) * 130} ${Math.sin(a) * 130}" stroke="#5a3d10" stroke-width="${i % 5 ? 1.5 : 3}"/>`; }).join('')}
        ${Array.from({ length: 12 }, (_, i) => { const a = i * Math.PI / 6 + Math.PI / 12; return `<circle cx="${Math.cos(a) * 98}" cy="${Math.sin(a) * 98}" r="5" fill="#c9922e" stroke="#5a3d10" stroke-width="1.5"/><circle cx="${Math.cos(a) * 98 - 1.5}" cy="${Math.sin(a) * 98 - 1.5}" r="1.6" fill="#fbe7b0"/>`; }).join('')}
        <circle r="66" fill="none" stroke="#c9922e" stroke-width="4" opacity=".75"/>
        <circle r="120" fill="url(#khg)" opacity=".55"/>
        <g transform="translate(0 -8)"><circle cy="-10" r="24" fill="#120a02"/><path d="M-14 4 L14 4 L24 56 L-24 56Z" fill="#120a02"/><circle cy="-10" r="17" fill="#ffd98a"/><path d="M-8 2 L8 2 L15 48 L-15 48Z" fill="#ffd98a"/><circle cy="-14" r="7" fill="#fff6dc" opacity=".9"/></g>
      </g>
      ${[...'UNLOCK'].map((ch, i) => { const st = ['c', 'c', 'w', 'c', 's', 'c'][i], fill = st === 'c' ? 'url(#khr)' : st === 'w' ? '#b8653a' : '#33403f', ink = st === 's' ? '#8fa0a0' : '#1a1206', x = 41 + i * 54; return `<g transform="translate(${x} 414)"><rect width="48" height="54" rx="6" fill="rgba(0,0,0,.5)" y="4"/><rect width="48" height="54" rx="6" fill="${fill}" stroke="#0a1012" stroke-width="2"/><rect x="5" y="5" width="38" height="8" rx="4" fill="#fff" opacity=".18"/><text x="24" y="40" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="30" fill="${ink}">${ch}</text></g>`; }).join('')}
      <text x="200" y="74" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="52" letter-spacing="7" fill="url(#khr)" stroke="#0a1012" stroke-width="5" paint-order="stroke">KEYHOLE</text>
      <text x="200" y="104" text-anchor="middle" font-family="Bahnschrift,Segoe UI,sans-serif" font-weight="700" font-size="14" letter-spacing="6" fill="#e9dcc0">THE DAILY WORD VAULT</text>`),

    arcadecat: () => svg('0 0 800 500', `<defs><linearGradient id="ag" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#0b0820"/><stop offset="1" stop-color="#2a0f4a"/></linearGradient></defs>
      <rect width="800" height="500" fill="url(#ag)"/>
      ${Array.from({ length: 60 }, (_, i) => `<circle cx="${(i * 137) % 800}" cy="${(i * 71) % 330}" r="${1 + (i % 3) * .7}" fill="#fff" opacity="${.3 + (i % 4) * .15}"/>`).join('')}
      ${Array.from({ length: 11 }, (_, i) => `<path d="M${400 + (i - 5) * 30} 340 L${400 + (i - 5) * 160} 500" stroke="#e8389a" stroke-width="2" opacity=".55"/>`).join('')}
      ${[350, 380, 420, 470].map(y => `<path d="M0 ${y} H800" stroke="#e8389a" stroke-width="2" opacity=".55"/>`).join('')}
      <rect x="0" y="336" width="800" height="4" fill="#ff5fb4"/>
      ${[[250, 120, '#5ef0a0'], [400, 90, '#ffd23f'], [550, 120, '#5ec8f0']].map(([x, y, c]) => `<g transform="translate(${x} ${y})" fill="${c}"><rect x="-30" y="-10" width="60" height="20"/><rect x="-20" y="-20" width="40" height="10"/><rect x="-40" y="0" width="10" height="20"/><rect x="30" y="0" width="10" height="20"/><rect x="-20" y="10" width="10" height="10" fill="#0b0820"/><rect x="10" y="10" width="10" height="10" fill="#0b0820"/><rect x="-16" y="-6" width="8" height="8" fill="#0b0820"/><rect x="8" y="-6" width="8" height="8" fill="#0b0820"/></g>`).join('')}
      <path d="M400 180 V250" stroke="#ffd23f" stroke-width="5" stroke-dasharray="10 8"/>
      <g transform="translate(400 290)" fill="#e8873a"><path d="M0 -26 L20 14 H-20Z"/><rect x="-34" y="10" width="68" height="14" rx="3"/></g>`),

    puzzlecat: () => svg('0 0 800 500', `<rect width="800" height="500" fill="#14303a"/>
      ${(() => { const cols = ['#e8873a', '#5fc489', '#5e9ef0', '#e7c35a', '#c86ad8', '#e84a5f']; let o = ''; for (let r = 0; r < 5; r++) for (let c = 0; c < 9; c++) { const x = 130 + c * 62, y = 95 + r * 62, k = (r * 7 + c * 3 + (r * c)) % 6, gone = (r === 2 && (c === 4 || c === 5)); if (!gone) o += `<rect x="${x}" y="${y}" width="54" height="54" rx="10" fill="${cols[k]}" opacity="${.55 + ((r + c) % 3) * .15}"/><rect x="${x + 6}" y="${y + 6}" width="42" height="12" rx="6" fill="rgba(255,255,255,.22)"/>`; } return o; })()}
      <g transform="translate(450 250) rotate(-14)"><path d="M-40 -40 H-8 A12 12 0 1 1 8 -40 H40 V-8 A12 12 0 1 1 40 8 V40 H8 A12 12 0 1 0 -8 40 H-40 V8 A12 12 0 1 0 -40 -8Z" fill="#fbf7ee" stroke="#1d1d24" stroke-width="3"/></g>`),

    wordcat: () => svg('0 0 800 500', `<rect width="800" height="500" fill="#2b2140"/>
      <g transform="translate(140 120)">${['IRONITE', 'TRIVIA', 'PUZZLE'].map((w, r) => [...w].map((ch, i) => `<g transform="translate(${i * 74 + r * 40} ${r * 92})"><rect width="66" height="66" rx="8" fill="${(r + i) % 4 === 0 ? '#e7c35a' : '#f3e6c8'}" stroke="#8a6d3b" stroke-width="2"/><text x="33" y="46" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="38" fill="#2b2140">${ch}</text><text x="56" y="60" text-anchor="end" font-family="Arial" font-size="12" fill="#6a5a3b">${(i * 3 + r) % 9 + 1}</text></g>`).join('')).join('')}</g>
      <g transform="translate(700 90)"><circle r="46" fill="#e8873a"/><text y="20" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="58" fill="#2b2140">?</text></g>`)
  };
  window.Covers = C;
})();
