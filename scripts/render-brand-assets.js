/*
 * Regenerates the HAMMOR static brand assets from the approved Logo board
 * (Sovereign Deep identity, V2 Lens mark).
 *
 * Writes:
 *   src/HAMMOR.App/Assets/Brand/*.svg   vector masters
 *   src/HAMMOR.App/Assets/HAMMOR.ico    application / taskbar icon
 *   src/HAMMOR.App/Assets/Splash.png    startup splash
 *
 * Optional, developer-only tooling: the generated files are committed, so the
 * app never needs this script to build. Requires Node 18+ and Playwright with
 * Chromium:
 *
 *   npm install --no-save playwright && npx playwright install chromium
 *   node scripts/render-brand-assets.js
 *
 * Geometry is the Logo board's 120-unit box. Per-size weights follow the
 * board: full lens at 64, 48, 40 and 32 px; solid lens with a dark core at 24,
 * 20 and 16 px; the orb at 256 px. The seal ring belongs to the standalone
 * mark; inside the app-icon tile the tile itself is the container.
 *
 * The splash wordmarks are outlines of Alexandria Medium (SIL Open Font
 * License 1.1), shaped with HarfBuzz, so rendering needs no installed font.
 */

'use strict';

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const ROOT = path.resolve(__dirname, '..');
const ASSETS = path.join(ROOT, 'src', 'HAMMOR.App', 'Assets');
const BRAND = path.join(ASSETS, 'Brand');

// ---- Sovereign Deep palette (Logo board) ----
const PEARL = '#EAF0F5';
const WHITE = '#FFFFFF';
const ABYSS = '#04070A';
const ACCENT = '#3ED0C8';
const SPOT = '#E69F68';

// ---- V2 Lens geometry (120-unit box) ----
const crest = (innerRadius) =>
  `M 21.9 60 A 44 44 0 0 1 98.1 60 A ${innerRadius} ${innerRadius} 0 0 0 21.9 60 Z`;
const SOLID_LENS = 'M 21.9 60 A 44 44 0 0 1 98.1 60 A 44 44 0 0 1 21.9 60 Z';
const BELLY = 'M 98.1 60 A 44 44 0 0 1 26 65.93';
const CORE_X = 76;
const CORE_Y = 57.5;

const scaleAboutCentre = (s) => `translate(60 60) scale(${s}) translate(-60 -60)`;

const DEFS = `
  <linearGradient id="tile" x1="0" y1="0" x2="0" y2="120" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#17212D"/><stop offset="1" stop-color="${ABYSS}"/></linearGradient>
  <radialGradient id="tglow" cx="60%" cy="48%" r="50%"><stop offset="0" stop-color="#147078" stop-opacity="0.9"/><stop offset="1" stop-color="#147078" stop-opacity="0"/></radialGradient>
  <radialGradient id="pg"><stop offset="0" stop-color="${WHITE}" stop-opacity="0.9"/><stop offset="0.25" stop-color="#F3EFE6" stop-opacity="0.35"/><stop offset="1" stop-color="#F3EFE6" stop-opacity="0"/></radialGradient>
  <radialGradient id="body" cx="70" cy="52" r="60" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#0B292C"/><stop offset="0.7" stop-color="#071016"/><stop offset="1" stop-color="#04080C"/></radialGradient>
  <linearGradient id="crest" x1="21.9" y1="0" x2="98.1" y2="0" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="${ACCENT}" stop-opacity="0.15"/><stop offset="0.55" stop-color="${ACCENT}" stop-opacity="0.85"/><stop offset="1" stop-color="${ACCENT}"/></linearGradient>`;

const svgDoc = (width, height, viewBox, body) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="${viewBox}"><defs>${DEFS}
</defs>${body}</svg>\n`;

const tileBackground = ({ glow = true, border = false } = {}) =>
  `<rect width="120" height="120" rx="27" fill="url(#tile)"/>` +
  (glow ? `<rect width="120" height="120" rx="27" fill="url(#tglow)"/>` : '') +
  (border
    ? `<rect x="0.5" y="0.5" width="119" height="119" rx="26.5" fill="none" stroke="#D6E8FF" stroke-opacity="0.12"/>`
    : '');

// Full lens inside the tile (64 to 32 px and the large tile master).
const tileLens = ({ scale, innerRadius, belly, core, glowRadius = 0 }) =>
  `<g transform="${scaleAboutCentre(scale)}">` +
  `<path d="${crest(innerRadius)}" fill="${PEARL}"/>` +
  `<path d="${BELLY}" fill="none" stroke="${PEARL}" stroke-width="${belly}" stroke-linecap="round"/>` +
  (glowRadius ? `<circle cx="${CORE_X}" cy="${CORE_Y}" r="${glowRadius}" fill="url(#pg)"/>` : '') +
  `<circle cx="${CORE_X}" cy="${CORE_Y}" r="${core}" fill="${WHITE}"/></g>`;

// Solid lens with a dark core (24, 20 and 16 px).
const tileSolidLens = ({ scale, core }) =>
  `<g transform="${scaleAboutCentre(scale)}">` +
  `<path d="${SOLID_LENS}" fill="${PEARL}"/>` +
  `<circle cx="${CORE_X}" cy="${CORE_Y}" r="${core}" fill="${ABYSS}"/></g>`;

// The orb: a still Living Core in the tile, for 256 px only.
const orb = () =>
  `<g transform="${scaleAboutCentre(0.74)}">` +
  `<circle cx="60" cy="60" r="58" fill="none" stroke="${ACCENT}" stroke-opacity="0.3" stroke-width="0.8"/>` +
  `<circle cx="60" cy="60" r="55" fill="none" stroke="${ACCENT}" stroke-opacity="0.7" stroke-width="1.4" stroke-dasharray="30 315.6" stroke-dashoffset="-210" stroke-linecap="round"/>` +
  `<circle cx="60" cy="60" r="55" fill="none" stroke="${WHITE}" stroke-opacity="0.9" stroke-width="1" stroke-dasharray="7 338.6" stroke-dashoffset="-233" stroke-linecap="round"/>` +
  `<circle cx="60" cy="60" r="50" fill="url(#body)"/>` +
  `<circle cx="60" cy="60" r="50" fill="none" stroke="${ACCENT}" stroke-opacity="0.65" stroke-width="1"/>` +
  `<circle cx="36.6" cy="44" r="0.9" fill="${WHITE}" fill-opacity="0.6"/><circle cx="85.3" cy="36.1" r="0.8" fill="${WHITE}" fill-opacity="0.5"/>` +
  `<circle cx="72.8" cy="89.5" r="0.9" fill="${WHITE}" fill-opacity="0.6"/><circle cx="27.8" cy="69.9" r="0.8" fill="${WHITE}" fill-opacity="0.5"/>` +
  `<circle cx="51.9" cy="76.2" r="2.2" fill="${SPOT}"/><circle cx="35.6" cy="67.4" r="1.5" fill="${SPOT}"/>` +
  `<circle cx="75.65" cy="75.4" r="1.6" fill="${ACCENT}"/><circle cx="52.7" cy="49.8" r="1.8" fill="${ACCENT}" fill-opacity="0.8"/>` +
  `<path d="${crest(51.19)}" fill="url(#crest)"/>` +
  `<path d="${BELLY}" fill="none" stroke="${ACCENT}" stroke-opacity="0.7" stroke-width="1.6" stroke-linecap="round"/>` +
  `<circle cx="${CORE_X}" cy="${CORE_Y}" r="20" fill="url(#pg)"/>` +
  `<circle cx="${CORE_X}" cy="${CORE_Y}" r="7.5" fill="${WHITE}"/></g>`;

// Per-size table for the ICO (Logo board; 40 and 20 px interpolated).
const ICON_SIZES = [
  { px: 16, body: () => tileBackground({ glow: false }) + tileSolidLens({ scale: 1.26, core: 10 }) },
  { px: 20, body: () => tileBackground() + tileSolidLens({ scale: 1.25, core: 9.5 }) },
  { px: 24, body: () => tileBackground() + tileSolidLens({ scale: 1.24, core: 9 }) },
  { px: 32, body: () => tileBackground() + tileLens({ scale: 1.22, innerRadius: 55.89, belly: 5, core: 8 }) },
  { px: 40, body: () => tileBackground() + tileLens({ scale: 1.2, innerRadius: 55.89, belly: 4.4, core: 7.8 }) },
  { px: 48, body: () => tileBackground() + tileLens({ scale: 1.18, innerRadius: 51.19, belly: 3.8, core: 7.5 }) },
  { px: 64, body: () => tileBackground() + tileLens({ scale: 1.18, innerRadius: 51.19, belly: 3.2, core: 7.2 }) },
  { px: 256, body: () => tileBackground({ border: true }) + orb(), png: true },
];

// ---- Vector masters ----
const MASTERS = {
  // The primary mark: seal ring, lens, open rear, white core.
  'hammor-mark.svg': svgDoc(120, 120, '0 0 120 120',
    `<circle cx="60" cy="60" r="50" fill="none" stroke="${PEARL}" stroke-width="3.4"/>` +
    `<path d="${crest(51.19)}" fill="${PEARL}"/>` +
    `<path d="${BELLY}" fill="none" stroke="${PEARL}" stroke-width="2.5" stroke-linecap="round"/>` +
    `<circle cx="${CORE_X}" cy="${CORE_Y}" r="7.5" fill="${WHITE}"/>`),
  // Collapsed sidebar: the compact lens, ring dropped.
  'hammor-lens.svg': svgDoc(120, 120, '0 0 120 120',
    `<g transform="${scaleAboutCentre(1.24)}">` +
    `<path d="${crest(55.89)}" fill="${PEARL}"/>` +
    `<path d="${BELLY}" fill="none" stroke="${PEARL}" stroke-width="5" stroke-linecap="round"/>` +
    `<circle cx="${CORE_X}" cy="${CORE_Y}" r="8" fill="${WHITE}"/></g>`),
  // App icon, tile form (taskbar and pinned), large master.
  'hammor-icon-tile.svg': svgDoc(120, 120, '0 0 120 120',
    tileBackground({ border: true }) +
    tileLens({ scale: 1.18, innerRadius: 51.19, belly: 2.8, core: 7, glowRadius: 17 })),
  // App icon, orb form (Start menu, store, 256 px).
  'hammor-icon-orb.svg': svgDoc(120, 120, '0 0 120 120', tileBackground({ border: true }) + orb()),
};

// ---- Splash (Logo board: 560 x 315, mark 96 px, wordmarks below) ----
const WORDMARK_EN = { d: 'M1.55 -12.6H3.96V-7.16H10.24V-12.6H12.67V0H10.24V-5.17H3.96V0H1.55ZM25.15 -12.6H27.61L33.07 0H30.49L26.3 -10.17L22.12 0H19.6ZM22.5 -4.72H30.01V-2.75H22.5ZM40.01 -12.6H42.77L46.94 -4.34L51.08 -12.6H53.84V0H51.62V-9.23L47.7 -1.3H46.15L42.21 -9.23V0H40.01ZM62.33 -12.6H65.09L69.26 -4.34L73.4 -12.6H76.16V0H73.94V-9.23L70.02 -1.3H68.47L64.53 -9.23V0H62.33ZM90.38 -12.74Q91.8 -12.74 93.02 -12.26Q94.25 -11.77 95.16 -10.89Q96.07 -10.01 96.58 -8.85Q97.09 -7.69 97.09 -6.32Q97.09 -4.97 96.58 -3.78Q96.07 -2.59 95.16 -1.71Q94.25 -0.83 93.02 -0.33Q91.8 0.16 90.38 0.16Q88.96 0.16 87.74 -0.33Q86.53 -0.83 85.61 -1.71Q84.69 -2.59 84.18 -3.77Q83.66 -4.95 83.66 -6.32Q83.66 -7.69 84.18 -8.86Q84.69 -10.03 85.61 -10.9Q86.53 -11.77 87.74 -12.26Q88.96 -12.74 90.38 -12.74ZM90.41 -10.71Q89.53 -10.71 88.76 -10.38Q87.98 -10.04 87.39 -9.44Q86.8 -8.84 86.46 -8.05Q86.13 -7.25 86.13 -6.32Q86.13 -5.38 86.47 -4.58Q86.81 -3.78 87.41 -3.17Q88 -2.56 88.78 -2.21Q89.55 -1.87 90.41 -1.87Q91.28 -1.87 92.04 -2.21Q92.81 -2.56 93.38 -3.17Q93.96 -3.78 94.29 -4.58Q94.63 -5.38 94.63 -6.32Q94.63 -7.25 94.29 -8.05Q93.96 -8.84 93.38 -9.44Q92.81 -10.04 92.04 -10.38Q91.28 -10.71 90.41 -10.71ZM109.87 -12.6Q112.32 -12.6 113.65 -11.47Q114.98 -10.33 114.98 -8.28Q114.98 -6.14 113.65 -4.95Q112.32 -3.76 109.87 -3.76H107.03V0H104.62V-12.6ZM109.87 -5.76Q111.22 -5.76 111.96 -6.37Q112.7 -6.98 112.7 -8.23Q112.7 -9.41 111.96 -10Q111.22 -10.58 109.87 -10.58H107.03V-5.76ZM109.78 -4.68H112.14L115.24 0H112.5Z', width: 116.082 };
const WORDMARK_AR = { d: 'M0 2.47Q0.83 2.47 1.33 2.29Q1.82 2.11 2.03 1.7Q2.24 1.29 2.24 0.68V-8.33H4.18V0.68Q4.18 1.89 3.71 2.75Q3.25 3.62 2.41 4.09Q1.56 4.56 0.36 4.59ZM13.26 0V-2.23H15.76V0ZM15.76 0V-2.23Q15.93 -2.23 16.01 -1.92Q16.1 -1.62 16.1 -1.12Q16.1 -0.61 16.01 -0.31Q15.93 0 15.76 0ZM6.56 4.59V2.38H9.81Q10.88 2.38 11.34 1.96Q11.8 1.55 11.8 0.68V-7.94H13.74V0.68Q13.74 1.92 13.26 2.8Q12.78 3.67 11.9 4.13Q11.02 4.59 9.81 4.59ZM10.35 0Q8.99 0 7.99 -0.59Q6.99 -1.17 6.43 -2.17Q5.87 -3.16 5.87 -4.33Q5.87 -5.3 6.21 -6.12Q6.56 -6.94 7.19 -7.55Q7.82 -8.16 8.64 -8.5Q9.45 -8.84 10.39 -8.84Q11.32 -8.84 12.19 -8.56Q13.06 -8.28 13.74 -7.94L12.9 -5.95Q11.49 -6.68 10.39 -6.68Q9.66 -6.68 9.07 -6.37Q8.48 -6.05 8.14 -5.53Q7.8 -5 7.8 -4.33Q7.8 -3.72 8.12 -3.23Q8.43 -2.74 9 -2.45Q9.57 -2.16 10.35 -2.16H12.87V0ZM15.76 0V-2.23Q16.56 -2.23 17.09 -2.52Q17.61 -2.82 18.03 -3.36Q18.45 -3.89 18.85 -4.59Q19.26 -5.25 19.71 -5.92Q20.16 -6.58 20.71 -7.11Q21.27 -7.65 21.93 -7.98Q22.59 -8.31 23.44 -8.31Q24.36 -8.31 25.02 -7.93Q25.69 -7.55 26.1 -6.89Q26.52 -6.24 26.72 -5.42Q26.91 -4.61 26.91 -3.74Q26.91 -2.84 26.67 -1.74Q26.44 -0.65 25.91 0.29L24.17 -0.71Q24.57 -1.39 24.77 -2.17Q24.97 -2.94 24.97 -3.74Q24.97 -4.95 24.57 -5.53Q24.17 -6.1 23.44 -6.1Q22.97 -6.1 22.52 -5.86Q22.08 -5.61 21.61 -5.02Q21.13 -4.44 20.5 -3.42Q19.79 -2.26 19.12 -1.5Q18.45 -0.73 17.65 -0.37Q16.85 0 15.76 0ZM25.81 -1.92 25.91 0.29Q24.34 0.37 23.19 0.26Q22.03 0.15 21.17 -0.14Q20.32 -0.44 19.64 -0.9Q18.97 -1.36 18.39 -1.96L19.75 -3.43Q20.37 -2.81 21.16 -2.44Q21.96 -2.07 23.09 -1.96Q24.23 -1.84 25.81 -1.92ZM15.76 0Q15.57 0 15.5 -0.31Q15.42 -0.61 15.42 -1.14Q15.42 -1.63 15.5 -1.93Q15.57 -2.23 15.76 -2.23ZM31.14 0V-2.23H33.17V0ZM29.24 0V-14.14H31.18V0ZM33.17 0V-2.23Q33.34 -2.23 33.42 -1.92Q33.51 -1.62 33.51 -1.12Q33.51 -0.61 33.42 -0.31Q33.34 0 33.17 0ZM40.89 0.03Q39.68 0.03 38.46 -0.34Q37.25 -0.71 36.25 -1.42Q35.26 -2.12 34.66 -3.11Q34.07 -4.1 34.07 -5.34Q34.07 -6.39 34.51 -7.28Q34.95 -8.16 35.73 -8.7Q36.52 -9.23 37.52 -9.23Q38.27 -9.23 38.91 -8.93Q39.56 -8.62 40.04 -8.08Q40.51 -7.55 40.78 -6.86Q41.05 -6.17 41.05 -5.41Q41.05 -4.35 40.52 -3.38Q39.98 -2.41 38.96 -1.65Q37.93 -0.88 36.47 -0.44Q35 0 33.17 0V-2.23Q34.48 -2.23 35.57 -2.5Q36.67 -2.77 37.48 -3.21Q38.3 -3.66 38.75 -4.23Q39.2 -4.81 39.2 -5.42Q39.2 -5.9 38.97 -6.27Q38.74 -6.65 38.37 -6.88Q38 -7.11 37.52 -7.11Q37.08 -7.11 36.71 -6.89Q36.35 -6.66 36.13 -6.29Q35.92 -5.92 35.92 -5.46Q35.92 -4.74 36.39 -4.11Q36.86 -3.49 37.63 -3.01Q38.4 -2.53 39.32 -2.27Q40.24 -2.01 41.14 -2.01Q41.8 -2.01 42.32 -2.18Q42.84 -2.36 43.14 -2.75Q43.44 -3.13 43.44 -3.77Q43.44 -4.69 42.69 -5.34Q41.94 -5.98 40.35 -6.46Q38.76 -6.94 36.23 -7.38Q36.14 -7.34 36.04 -7.34Q35.94 -7.34 35.84 -7.38Q35.73 -7.41 35.62 -7.43Q35.12 -7.51 34.57 -7.59Q34.02 -7.67 33.44 -7.75L33.69 -9.89Q36.28 -9.54 38.43 -9.11Q40.58 -8.69 42.13 -8.06Q43.69 -7.43 44.53 -6.42Q45.37 -5.41 45.37 -3.86Q45.37 -2.81 45.02 -2.07Q44.66 -1.33 44.02 -0.87Q43.38 -0.41 42.59 -0.19Q41.79 0.03 40.89 0.03ZM33.17 0Q32.98 0 32.9 -0.31Q32.83 -0.61 32.83 -1.14Q32.83 -1.63 32.9 -1.93Q32.98 -2.23 33.17 -2.23Z', width: 46.206 };

function splashSvg() {
  const w = 560;
  const h = 315;
  const markSize = 96;
  const markX = (w - markSize) / 2;
  const markY = 74.2;
  const s = markSize / 120;
  const enX = (w - WORDMARK_EN.width) / 2;
  const arX = (w - WORDMARK_AR.width) / 2;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><defs>${DEFS}
  <radialGradient id="splash" cx="50%" cy="44%" r="55%"><stop offset="0" stop-color="#147078" stop-opacity="0.45"/><stop offset="1" stop-color="#147078" stop-opacity="0"/></radialGradient>
</defs>` +
    `<rect width="${w}" height="${h}" rx="20" fill="${ABYSS}"/>` +
    `<rect width="${w}" height="${h}" rx="20" fill="url(#splash)"/>` +
    `<rect x="0.5" y="0.5" width="${w - 1}" height="${h - 1}" rx="19.5" fill="none" stroke="#D6E8FF" stroke-opacity="0.08"/>` +
    `<g transform="translate(${markX} ${markY}) scale(${s})">` +
    `<circle cx="60" cy="60" r="50" fill="none" stroke="${PEARL}" stroke-width="3.6"/>` +
    `<path d="${crest(51.19)}" fill="${PEARL}"/>` +
    `<path d="${BELLY}" fill="none" stroke="${PEARL}" stroke-width="2.6" stroke-linecap="round"/>` +
    `<circle cx="${CORE_X}" cy="${CORE_Y}" r="20" fill="url(#pg)"/>` +
    `<circle cx="${CORE_X}" cy="${CORE_Y}" r="7.5" fill="${WHITE}"/></g>` +
    `<path transform="translate(${enX.toFixed(2)} 201.6)" d="${WORDMARK_EN.d}" fill="${PEARL}"/>` +
    `<path transform="translate(${arX.toFixed(2)} 236.6)" d="${WORDMARK_AR.d}" fill="#A7B3BF"/>` +
    `</svg>\n`;
}

// ---- Rasterising (Chromium canvas) ----
async function rasterise(page, svg, width, height) {
  return page.evaluate(async ({ markup, w, h }) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(markup)));
    await img.decode();
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(img, 0, 0, w, h);
    const rgba = ctx.getImageData(0, 0, w, h).data;
    let binary = '';
    for (let i = 0; i < rgba.length; i++) {
      binary += String.fromCharCode(rgba[i]);
    }
    return { rgba: btoa(binary), png: canvas.toDataURL('image/png').split(',')[1] };
  }, { markup: svg, w: width, h: height });
}

// 32 bpp DIB (BITMAPINFOHEADER + bottom-up BGRA + AND mask) for one entry.
function dibEntry(rgba, size) {
  const header = Buffer.alloc(40);
  const maskStride = ((size + 31) >> 5) << 2;
  const xorBytes = size * size * 4;
  const maskBytes = maskStride * size;
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8);
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  header.writeUInt32LE(0, 16);
  header.writeUInt32LE(xorBytes + maskBytes, 20);

  const xor = Buffer.alloc(xorBytes);
  const mask = Buffer.alloc(maskBytes);
  for (let y = 0; y < size; y++) {
    const srcRow = y;
    const dstRow = size - 1 - y;
    for (let x = 0; x < size; x++) {
      const s = (srcRow * size + x) * 4;
      const d = (dstRow * size + x) * 4;
      xor[d] = rgba[s + 2];
      xor[d + 1] = rgba[s + 1];
      xor[d + 2] = rgba[s];
      xor[d + 3] = rgba[s + 3];
      if (rgba[s + 3] === 0) {
        mask[dstRow * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
      }
    }
  }
  return Buffer.concat([header, xor, mask]);
}

function packIco(entries) {
  const head = Buffer.alloc(6 + 16 * entries.length);
  head.writeUInt16LE(0, 0);
  head.writeUInt16LE(1, 2);
  head.writeUInt16LE(entries.length, 4);
  let offset = head.length;
  entries.forEach((entry, i) => {
    const o = 6 + i * 16;
    head.writeUInt8(entry.px >= 256 ? 0 : entry.px, o);
    head.writeUInt8(entry.px >= 256 ? 0 : entry.px, o + 1);
    head.writeUInt8(0, o + 2);
    head.writeUInt8(0, o + 3);
    head.writeUInt16LE(1, o + 4);
    head.writeUInt16LE(32, o + 6);
    head.writeUInt32LE(entry.data.length, o + 8);
    head.writeUInt32LE(offset, o + 12);
    offset += entry.data.length;
  });
  return Buffer.concat([head, ...entries.map((e) => e.data)]);
}

async function main() {
  fs.mkdirSync(BRAND, { recursive: true });

  for (const [name, svg] of Object.entries(MASTERS)) {
    fs.writeFileSync(path.join(BRAND, name), svg);
  }
  const splash = splashSvg();
  fs.writeFileSync(path.join(BRAND, 'hammor-splash.svg'), splash);

  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    await page.setContent('<!doctype html><html><body></body></html>');

    const entries = [];
    for (const size of ICON_SIZES) {
      const svg = svgDoc(size.px, size.px, '0 0 120 120', size.body());
      const { rgba, png } = await rasterise(page, svg, size.px, size.px);
      const data = size.png
        ? Buffer.from(png, 'base64')
        : dibEntry(Buffer.from(rgba, 'base64'), size.px);
      entries.push({ px: size.px, data });
    }
    fs.writeFileSync(path.join(ASSETS, 'HAMMOR.ico'), packIco(entries));

    const { png } = await rasterise(page, splash, 560, 315);
    fs.writeFileSync(path.join(ASSETS, 'Splash.png'), Buffer.from(png, 'base64'));
  } finally {
    await browser.close();
  }

  console.log('Brand assets written to', path.relative(ROOT, ASSETS));
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
