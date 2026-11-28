export const STYLE_CATALOG = [
  {
    id: 'Fisheye',
    name: 'Fisheye',
    category: 'KINETIC TYPE',
    chip: 'Lens Warp',
    description: 'Tight 3-word stack bulging outward through a spherical optical lens distortion.',
    hasVisuals: false,
    filterGroup: 'All',
    previewOffsetSec: 0.2,
    mood: { a1: '#EC4899', a2: '#FB7185', a3: '#A855F7', scrim: '#160B1A' },
  },
  {
    id: 'FisheyeVisual',
    name: 'Fisheye Visual',
    category: 'KINETIC TYPE',
    chip: 'Visual Icons',
    description: 'Spherical Fisheye typography paired with glowing semantic icons beside matching words.',
    hasVisuals: true,
    filterGroup: 'Visuals',
    previewOffsetSec: 3.0,
    mood: { a1: '#F97316', a2: '#EC4899', a3: '#FBBF24', scrim: '#1B0D14' },
  },
  {
    id: 'Codestyle',
    name: 'Codestyle',
    category: 'MONOSPACE',
    chip: 'Monospace',
    description: 'Lyrics typed out live as syntax-highlighted code beneath a blinking crimson cursor.',
    hasVisuals: false,
    filterGroup: 'All',
    previewOffsetSec: 14.4,
    mood: { a1: '#38BDF8', a2: '#F43F5E', a3: '#818CF8', scrim: '#0B101E' },
  },
  {
    id: 'CodestyleVisual',
    name: 'Codestyle Visual',
    category: 'MONOSPACE',
    chip: 'Visual Icons',
    description: 'Live syntax-highlighted code lyrics enhanced with inline semantic word icons.',
    hasVisuals: true,
    filterGroup: 'Visuals',
    previewOffsetSec: 17.3,
    mood: { a1: '#A855F7', a2: '#EC4899', a3: '#38BDF8', scrim: '#130B20' },
  },
  {
    id: 'VisualStack',
    name: 'Visual Stack',
    category: 'VISUALS',
    chip: 'Visual Icons',
    description: 'Centered kinetic word column pairing every matching lyric word with an expressive vector icon.',
    hasVisuals: true,
    filterGroup: 'Visuals',
    previewOffsetSec: 5.9,
    mood: { a1: '#10B981', a2: '#F59E0B', a3: '#34D399', scrim: '#091612' },
  },
  {
    id: 'Constellation',
    name: 'Constellation',
    category: 'GEOMETRY',
    chip: 'Vector Path',
    description: 'Each lyric line traces across space as a connected glowing constellation, star by star.',
    hasVisuals: false,
    filterGroup: 'Geometry',
    previewOffsetSec: 8.8,
    mood: { a1: '#6366F1', a2: '#C084FC', a3: '#818CF8', scrim: '#0D0C22' },
  },
  {
    id: 'SpidersWeb',
    name: "Spider's Web",
    category: 'GEOMETRY',
    chip: 'Geometric',
    description: 'Words spin outward into an intricate geometric web with luminous iridescent silk strands.',
    hasVisuals: false,
    filterGroup: 'Geometry',
    previewOffsetSec: 11.7,
    mood: { a1: '#F59E0B', a2: '#EA580C', a3: '#FDBA74', scrim: '#1C0F08' },
  },
  {
    id: 'NodeLine',
    name: 'Node Line',
    category: 'GEOMETRY',
    chip: 'Vector Path',
    description: 'A flowing spline draws itself across the stage as every word illuminates a glowing node.',
    hasVisuals: false,
    filterGroup: 'Geometry',
    previewOffsetSec: 20.1,
    mood: { a1: '#22C55E', a2: '#2DD4BF', a3: '#86EFAC', scrim: '#081811' },
  },
  {
    id: 'KineticWall',
    name: 'Kinetic Wall',
    category: '3D STAGE',
    chip: '3D Motion',
    description: 'Words land onto a monumental typographic wall drifting smoothly in 3D perspective.',
    hasVisuals: false,
    filterGroup: '3D',
    previewOffsetSec: 28.6,
    mood: { a1: '#0EA5E9', a2: '#FB923C', a3: '#38BDF8', scrim: '#09131E' },
  },
  {
    id: 'Messages',
    name: 'Messages',
    category: 'PHONE SCREEN',
    chip: 'Chat Thread',
    description: 'Song lines arrive as a sleek phone message thread with timed frosted chat bubbles per line.',
    hasVisuals: false,
    filterGroup: 'All',
    previewOffsetSec: 22.9,
    mood: { a1: '#2563EB', a2: '#06B6D4', a3: '#60A5FA', scrim: '#091224' },
  },
  {
    id: 'VinylGroove',
    name: 'Glass Stage',
    category: 'ALBUM GLASS',
    chip: 'GlassOS UI',
    description: 'Lightly blurred album cover room with an Apple GlassOS cover card, hover controls, and 3D depth lyrics.',
    hasVisuals: false,
    filterGroup: '3D',
    previewOffsetSec: 25.7,
    mood: { a1: '#FB7185', a2: '#EF4444', a3: '#F97316', scrim: '#24080E' },
  },
];

export const STUDIO_PRESETS = {
  'Pure Studio': { a1: '#F8FAFC', a2: '#818CF8', a3: '#38BDF8', scrim: '#0F1022' },
  'Vinyl Warm': { a1: '#FEF3C7', a2: '#F59E0B', a3: '#FB7185', scrim: '#1A0F12' },
  'Mint Pulse': { a1: '#ECFDF5', a2: '#34D399', a3: '#2DD4BF', scrim: '#091A16' },
  'Monochrome': { a1: '#FFFFFF', a2: '#CBD5E1', a3: '#64748B', scrim: '#101218' },
};

export const NEON_HORIZON_LRC = `[ti:Neon Horizon]
[ar:LyricPulse Studio]
[al:Demo Preview]
[length:00:36.00]
[00:00.20]<00:00.20>Chasing <00:00.65>a <00:00.85>silver <00:01.40>star <00:02.05>tonight
[00:03.00]<00:03.00>Sparks <00:03.50>of <00:03.75>fire <00:04.35>burn <00:04.85>so <00:05.15>bright
[00:05.90]<00:05.90>Across <00:06.45>the <00:06.70>midnight <00:07.35>ocean <00:08.05>tide
[00:08.80]<00:08.80>Quiet <00:09.35>night <00:09.95>where <00:10.30>shadows <00:10.95>hide
[00:11.70]<00:11.70>Feel <00:12.15>the <00:12.40>rhythm <00:13.00>in <00:13.25>my <00:13.55>heart
[00:14.40]<00:14.40>Writing <00:14.95>clean <00:15.40>code <00:16.00>in <00:16.25>the <00:16.50>dark
[00:17.30]<00:17.30>Silver <00:17.85>rain <00:18.45>on <00:18.70>neon <00:19.25>glass
[00:20.10]<00:20.10>Through <00:20.60>the <00:20.85>waking <00:21.45>city <00:22.10>lights
[00:22.90]<00:22.90>Take <00:23.35>a <00:23.55>sudden <00:24.15>midnight <00:24.80>flight
[00:25.70]<00:25.70>Hold <00:26.15>onto <00:26.60>every <00:27.15>inch <00:27.55>of <00:27.80>time
[00:28.60]<00:28.60>Follow <00:29.15>every <00:29.65>beam <00:30.15>of <00:30.40>light
[00:31.30]<00:31.30>Living <00:31.85>inside <00:32.45>this <00:32.80>electric <00:33.55>dream`;

function parseTagTime(tag) {
  const m = /(\d{2}):(\d{2}(?:\.\d{1,3})?)/.exec(tag);
  if (!m) return 0;
  return Number(m[1]) * 60 + Number(m[2]);
}

const MAX_LRC_CHARS = 512_000;
const MAX_LRC_LINES = 2_000;
const MAX_LRC_WORDS_PER_LINE = 256;

export function parseLrc(rawText, fallbackDurationSec = 36.0) {
  let title = 'Neon Horizon';
  let artist = 'LyricPulse Studio';
  let durationSec = fallbackDurationSec;
  const lines = [];

  if (typeof rawText !== 'string' || !rawText.trim()) {
    return { title, artist, durationSec, lines };
  }

  const boundedText = rawText.length > MAX_LRC_CHARS ? rawText.slice(0, MAX_LRC_CHARS) : rawText;

  for (const rawLine of boundedText.split(/\r?\n/)) {
    if (lines.length >= MAX_LRC_LINES) break;
    const line = rawLine.trim();
    if (!line) continue;
    const metaMatch = /^\[(ti|ar|al|length):(.+)\]$/i.exec(line);
    if (metaMatch) {
      const k = metaMatch[1].toLowerCase();
      const v = metaMatch[2].trim().slice(0, 160);
      if (k === 'ti') title = v;
      if (k === 'ar') artist = v;
      if (k === 'length') durationSec = Math.max(5, Math.min(86400, parseTagTime(v)));
      continue;
    }

    const lineTimeMatch = /^\[(\d{2}:\d{2}(?:\.\d{1,3})?)\](.*)$/.exec(line);
    if (!lineTimeMatch) continue;
    const lineStart = parseTagTime(lineTimeMatch[1]);
    const rest = lineTimeMatch[2].trim();
    if (!rest) continue;

    const wordRegex = /<(\d{2}:\d{2}(?:\.\d{1,3})?)>([^<]+)/g;
    const words = [];
    let match;
    while ((match = wordRegex.exec(rest)) !== null) {
      if (words.length >= MAX_LRC_WORDS_PER_LINE) break;
      const wStart = parseTagTime(match[1]);
      const text = match[2].trim();
      if (text) words.push({ text, start: wStart, end: wStart + 0.55 });
    }

    if (words.length === 0) {
      const plainWords = rest
        .replace(/<\d{2}:\d{2}(?:\.\d{1,3})?>/g, '')
        .trim()
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, MAX_LRC_WORDS_PER_LINE);
      const step = Math.max(0.28, 2.4 / Math.max(1, plainWords.length));
      plainWords.forEach((w, i) => {
        words.push({ text: w, start: lineStart + i * step, end: lineStart + (i + 1) * step });
      });
    } else {
      for (let i = 0; i < words.length - 1; i++) {
        words[i].end = Math.max(words[i].start + 0.12, words[i + 1].start);
      }
    }

    lines.push({
      index: lines.length,
      start: lineStart,
      end: words.length ? words[words.length - 1].end : lineStart + 2.6,
      text: words.map((w) => w.text).join(' '),
      words,
    });
  }

  for (let i = 0; i < lines.length; i++) {
    const nextStart = i + 1 < lines.length ? lines[i + 1].start : Math.max(durationSec, lines[i].start + 3.0);
    lines[i].end = Math.max(lines[i].end, nextStart);
    if (lines[i].words.length > 0) {
      const last = lines[i].words[lines[i].words.length - 1];
      last.end = Math.max(last.start + 0.25, nextStart - 0.08);
    }
  }

  const maxLineEnd = lines.length ? lines[lines.length - 1].end : fallbackDurationSec;
  return { title, artist, durationSec: Math.max(durationSec, maxLineEnd), lines };
}

export function createDefaultCoverCanvas() {
  const c = document.createElement('canvas');
  c.width = 288;
  c.height = 288;
  const ctx = c.getContext('2d');

  // Deep atmospheric synthwave sunset gradient cover
  const bg = ctx.createLinearGradient(0, 0, 288, 288);
  bg.addColorStop(0, '#0F0C29');
  bg.addColorStop(0.45, '#302B63');
  bg.addColorStop(0.75, '#7C2AE8');
  bg.addColorStop(1, '#F97316');
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, 288, 288);

  // Glowing synth sun
  const sun = ctx.createLinearGradient(144, 64, 144, 210);
  sun.addColorStop(0, '#FDE047');
  sun.addColorStop(0.55, '#FB7185');
  sun.addColorStop(1, '#EC4899');
  ctx.fillStyle = sun;
  ctx.beginPath();
  ctx.arc(144, 138, 72, 0, Math.PI * 2);
  ctx.fill();

  // Scanline horizon cuts across sun
  ctx.fillStyle = 'rgba(18, 12, 42, 0.9)';
  for (let y = 148; y < 212; y += 11) {
    ctx.fillRect(60, y, 168, (y - 140) * 0.08 + 2);
  }

  // Horizon wireframe grid
  ctx.strokeStyle = 'rgba(56, 189, 248, 0.7)';
  ctx.lineWidth = 1.6;
  ctx.beginPath();
  for (let i = -5; i <= 5; i++) {
    ctx.moveTo(144 + i * 16, 198);
    ctx.lineTo(144 + i * 52, 288);
  }
  for (let r = 0; r < 5; r++) {
    const yy = 198 + r * r * 5.2;
    ctx.moveTo(0, yy);
    ctx.lineTo(288, yy);
  }
  ctx.stroke();

  // Crisp subtle monogram arc
  ctx.fillStyle = 'rgba(255,255,255,0.88)';
  ctx.font = '700 18px Jost, sans-serif';
  ctx.textAlign = 'center';
  ctx.fillText('NEON HORIZON', 144, 40);

  return c;
}

export function extractPaletteFromCanvas(canvas) {
  const w = 48;
  const h = 48;
  const tmp = document.createElement('canvas');
  tmp.width = w;
  tmp.height = h;
  const ctx = tmp.getContext('2d');
  ctx.drawImage(canvas, 0, 0, w, h);
  const { data } = ctx.getImageData(0, 0, w, h);

  const buckets = new Map();
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] < 128) continue;
    const r = data[i];
    const g = data[i + 1];
    const b = data[i + 2];
    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    const sat = max === 0 ? 0 : (max - min) / max;
    const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    if (lum < 0.12 || lum > 0.94) continue;
    const key = `${Math.round(r / 32) * 32},${Math.round(g / 32) * 32},${Math.round(b / 32) * 32}`;
    const prev = buckets.get(key) || { score: 0, r: 0, g: 0, b: 0, n: 0 };
    const weight = 0.35 + sat * 2.4;
    prev.score += weight;
    prev.r += r;
    prev.g += g;
    prev.b += b;
    prev.n += 1;
    buckets.set(key, prev);
  }

  const sorted = [...buckets.values()]
    .map((x) => ({
      score: x.score,
      r: Math.min(255, Math.round((x.r / x.n) * 1.15 + 18)),
      g: Math.min(255, Math.round((x.g / x.n) * 1.15 + 18)),
      b: Math.min(255, Math.round((x.b / x.n) * 1.15 + 18)),
    }))
    .sort((a, b) => b.score - a.score);

  if (sorted.length === 0) return STUDIO_PRESETS['Pure Studio'];

  const hex = (c) => '#' + [c.r, c.g, c.b].map((v) => v.toString(16).padStart(2, '0')).join('');
  const c1 = sorted[0];
  const dist = (a, b) => Math.hypot(a.r - b.r, a.g - b.g, a.b - b.b);
  const c2 = sorted.find((c) => dist(c, c1) > 70) || {
    r: Math.min(255, c1.b + 40),
    g: Math.min(255, c1.r),
    b: Math.min(255, c1.g + 50),
  };
  const c3 = sorted.find((c) => dist(c, c1) > 45 && dist(c, c2) > 45) || {
    r: Math.min(255, Math.round((c1.r + c2.r) * 0.55)),
    g: Math.min(255, Math.round((c1.g + c2.g) * 0.55)),
    b: Math.min(255, Math.round((c1.b + c2.b) * 0.55)),
  };

  return {
    a1: hex(c1),
    a2: hex(c2),
    a3: hex(c3),
    scrim: '#0F0F23',
  };
}

const KEYWORD_ICON_MAP = [
  ['star', 'star'], ['stars', 'star'], ['taara', 'star'], ['tara', 'star'], ['taare', 'star'], ['sitara', 'star'], ['sitare', 'star'], ['तारा', 'star'], ['तारे', 'star'], ['सितारा', 'star'], ['सितारे', 'star'], ['sajda', 'star'], ['सजदा', 'star'], ['hasrat', 'star'], ['हसरत', 'star'],
  ['tonight', 'moon'], ['night', 'moon'], ['midnight', 'moon'], ['dark', 'moon'], ['raat', 'moon'], ['chaand', 'moon'], ['chand', 'moon'], ['chandni', 'moon'], ['shaam', 'moon'], ['रात', 'moon'], ['चांद', 'moon'], ['चांदनी', 'moon'], ['शाम', 'moon'],
  ['fire', 'flame'], ['burn', 'flame'], ['aag', 'flame'], ['shola', 'flame'], ['jwala', 'flame'], ['jalna', 'flame'], ['आग', 'flame'], ['शोला', 'flame'], ['ज्वाला', 'flame'],
  ['sparks', 'spark'], ['chingari', 'spark'], ['chamak', 'spark'], ['चिंगारी', 'spark'], ['चमक', 'spark'], ['fikar', 'spark'], ['फिकर', 'spark'], ['बेफिकर', 'spark'],
  ['bright', 'sun'], ['light', 'sun'], ['lights', 'sun'], ['beam', 'sun'], ['suraj', 'sun'], ['sooraj', 'sun'], ['subah', 'sun'], ['dhoop', 'sun'], ['roshni', 'sun'], ['ujala', 'sun'], ['सूरज', 'sun'], ['सुबह', 'sun'], ['धूप', 'sun'], ['रोशनी', 'sun'],
  ['ocean', 'wave'], ['tide', 'wave'], ['samundar', 'wave'], ['samandar', 'wave'], ['sagar', 'wave'], ['lehar', 'wave'], ['lehrein', 'wave'], ['nadi', 'wave'], ['समंदर', 'wave'], ['सागर', 'wave'], ['लहर', 'wave'], ['नदी', 'wave'],
  ['rain', 'drop'], ['baarish', 'drop'], ['barish', 'drop'], ['barsaat', 'drop'], ['saawan', 'drop'], ['aansu', 'drop'], ['ansu', 'drop'], ['paani', 'drop'], ['boond', 'drop'], ['बारिश', 'drop'], ['बरसात', 'drop'], ['आंसू', 'drop'], ['पानी', 'drop'], ['बूंद', 'drop'],
  ['silver', 'gem'], ['glass', 'gem'], ['heera', 'gem'], ['hira', 'gem'], ['sona', 'gem'], ['chaandi', 'gem'], ['हीरा', 'gem'], ['सोना', 'gem'], ['चांदी', 'gem'],
  ['shadows', 'cloud'], ['baadal', 'cloud'], ['badal', 'cloud'], ['aasmaan', 'cloud'], ['asman', 'cloud'], ['hawa', 'cloud'], ['बादल', 'cloud'], ['आसमान', 'cloud'], ['हवा', 'cloud'],
  ['quiet', 'note'], ['geet', 'note'], ['gaana', 'note'], ['sangeet', 'note'], ['dhun', 'note'], ['गीत', 'note'], ['गाना', 'note'], ['संगीत', 'note'], ['धुन', 'note'],
  ['rhythm', 'pulse'], ['dhadkan', 'pulse'], ['dhol', 'pulse'], ['tabla', 'pulse'], ['zindagi', 'pulse'], ['saans', 'pulse'], ['mehsoos', 'pulse'], ['धड़कन', 'pulse'], ['ढोल', 'pulse'], ['जिंदगी', 'pulse'], ['सांस', 'pulse'], ['महसूस', 'pulse'],
  ['heart', 'heart'], ['dil', 'heart'], ['pyar', 'heart'], ['pyaar', 'heart'], ['ishq', 'heart'], ['ishk', 'heart'], ['mohabbat', 'heart'], ['sanam', 'heart'], ['jaan', 'heart'], ['mehboob', 'heart'], ['deewana', 'heart'], ['dard', 'heart'], ['phool', 'heart'], ['gulab', 'heart'], ['wafa', 'heart'], ['fida', 'heart'], ['दिल', 'heart'], ['प्यार', 'heart'], ['इश्क', 'heart'], ['मोहब्बत', 'heart'], ['जान', 'heart'], ['सनम', 'heart'], ['दर्द', 'heart'], ['फूल', 'heart'], ['गुलाब', 'heart'], ['वफा', 'heart'], ['फिदा', 'heart'],
  ['code', 'code'], ['writing', 'code'], ['shayari', 'code'], ['shayar', 'code'], ['kahaani', 'code'], ['शायरी', 'code'], ['कहानी', 'code'],
  ['neon', 'bolt'], ['electric', 'bolt'], ['sudden', 'bolt'], ['bijli', 'bolt'], ['toofan', 'bolt'], ['junoon', 'bolt'], ['बिजली', 'bolt'], ['तूफान', 'bolt'], ['जुनून', 'bolt'],
  ['city', 'city'], ['sheher', 'city'], ['shahar', 'city'], ['gali', 'city'], ['duniya', 'city'], ['शहर', 'city'], ['गली', 'city'], ['दुनिया', 'city'],
  ['flight', 'plane'], ['udaan', 'plane'], ['pankh', 'plane'], ['parinda', 'plane'], ['aazaad', 'plane'], ['उड़ान', 'plane'], ['पंख', 'plane'], ['परिंदा', 'plane'], ['आजाद', 'plane'],
  ['time', 'clock'], ['waqt', 'clock'], ['wakt', 'clock'], ['samay', 'clock'], ['pal', 'clock'], ['lamha', 'clock'], ['intezaar', 'clock'], ['वक्त', 'clock'], ['समय', 'clock'], ['पल', 'clock'], ['इंतजार', 'clock'],
  ['hold', 'shield'], ['yaar', 'shield'], ['dost', 'shield'], ['humsafar', 'shield'], ['यार', 'shield'], ['दोस्त', 'shield'], ['हमसफर', 'shield'],
  ['dream', 'orbit'], ['sapna', 'orbit'], ['sapne', 'orbit'], ['khwaab', 'orbit'], ['khwab', 'orbit'], ['jaadu', 'orbit'], ['jannat', 'orbit'], ['सपना', 'orbit'], ['सपने', 'orbit'], ['ख्वाब', 'orbit'], ['जादू', 'orbit'], ['जन्नत', 'orbit'],
  ['follow', 'compass'], ['chasing', 'compass'], ['safar', 'compass'], ['raasta', 'compass'], ['manzil', 'compass'], ['सफर', 'compass'], ['रास्ता', 'compass'], ['मंजिल', 'compass'],
];

export function matchSemanticIcon(wordText) {
  if (!wordText) return null;
  const clean = wordText
    .toLowerCase()
    .replace(/\u093C/g, '')
    .replace(/\u0901/g, '\u0902')
    .replace(/[^a-z0-9\u0900-\u0963]/g, '');
  if (clean.length < 2) return null;
  const exact = KEYWORD_ICON_MAP.find(([k]) => clean === k);
  if (exact) return exact[1];
  const partial = KEYWORD_ICON_MAP.find(([k]) => k.length >= 4 && clean.includes(k));
  return partial ? partial[1] : null;
}

export function drawSemanticIcon(ctx, iconName, cx, cy, radius, color, glow = false) {
  if (!iconName) return;
  ctx.save();
  ctx.translate(cx, cy);
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = Math.max(1.6, radius * 0.17);
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  if (glow) {
    ctx.shadowColor = color;
    ctx.shadowBlur = radius * 0.85;
  }

  const r = radius;
  switch (iconName) {
    case 'star':
    case 'spark': {
      ctx.beginPath();
      for (let i = 0; i < 8; i++) {
        const rad = i % 2 === 0 ? r : r * 0.36;
        const a = (i * Math.PI) / 4 - Math.PI / 2;
        if (i === 0) ctx.moveTo(Math.cos(a) * rad, Math.sin(a) * rad);
        else ctx.lineTo(Math.cos(a) * rad, Math.sin(a) * rad);
      }
      ctx.closePath();
      ctx.fill();
      break;
    }
    case 'moon': {
      ctx.beginPath();
      ctx.arc(0, 0, r * 0.82, 0.35, Math.PI * 1.65);
      ctx.arc(r * 0.36, -r * 0.14, r * 0.65, Math.PI * 1.55, 0.45, true);
      ctx.closePath();
      ctx.fill();
      break;
    }
    case 'flame': {
      ctx.beginPath();
      ctx.moveTo(0, -r);
      ctx.bezierCurveTo(r * 0.78, -r * 0.25, r * 0.85, r * 0.75, 0, r * 0.95);
      ctx.bezierCurveTo(-r * 0.85, r * 0.75, -r * 0.75, -r * 0.1, 0, -r);
      ctx.fill();
      break;
    }
    case 'sun': {
      ctx.beginPath();
      ctx.arc(0, 0, r * 0.44, 0, Math.PI * 2);
      ctx.fill();
      ctx.beginPath();
      for (let i = 0; i < 8; i++) {
        const a = (i * Math.PI) / 4;
        ctx.moveTo(Math.cos(a) * r * 0.65, Math.sin(a) * r * 0.65);
        ctx.lineTo(Math.cos(a) * r * 0.98, Math.sin(a) * r * 0.98);
      }
      ctx.stroke();
      break;
    }
    case 'wave': {
      for (let row = -1; row <= 1; row += 2) {
        const y = row * r * 0.26;
        ctx.beginPath();
        ctx.moveTo(-r * 0.9, y);
        ctx.bezierCurveTo(-r * 0.4, y - r * 0.35, 0, y + r * 0.35, r * 0.9, y);
        ctx.stroke();
      }
      break;
    }
    case 'drop': {
      ctx.beginPath();
      ctx.moveTo(0, -r * 0.95);
      ctx.bezierCurveTo(r * 0.75, 0, r * 0.68, r * 0.85, 0, r * 0.85);
      ctx.bezierCurveTo(-r * 0.68, r * 0.85, -r * 0.75, 0, 0, -r * 0.95);
      ctx.fill();
      break;
    }
    case 'heart': {
      ctx.beginPath();
      ctx.moveTo(0, r * 0.78);
      ctx.bezierCurveTo(-r, r * 0.2, -r * 0.95, -r * 0.72, 0, -r * 0.24);
      ctx.bezierCurveTo(r * 0.95, -r * 0.72, r, r * 0.2, 0, r * 0.78);
      ctx.fill();
      break;
    }
    case 'code': {
      ctx.beginPath();
      ctx.moveTo(-r * 0.36, -r * 0.55);
      ctx.lineTo(-r * 0.85, 0);
      ctx.lineTo(-r * 0.36, r * 0.55);
      ctx.moveTo(r * 0.36, -r * 0.55);
      ctx.lineTo(r * 0.85, 0);
      ctx.lineTo(r * 0.36, r * 0.55);
      ctx.moveTo(r * 0.12, -r * 0.65);
      ctx.lineTo(-r * 0.12, r * 0.65);
      ctx.stroke();
      break;
    }
    case 'bolt': {
      ctx.beginPath();
      ctx.moveTo(r * 0.16, -r * 0.95);
      ctx.lineTo(-r * 0.52, r * 0.08);
      ctx.lineTo(r * 0.04, r * 0.08);
      ctx.lineTo(-r * 0.16, r * 0.95);
      ctx.lineTo(r * 0.55, -r * 0.1);
      ctx.lineTo(-r * 0.02, -r * 0.1);
      ctx.closePath();
      ctx.fill();
      break;
    }
    case 'plane': {
      ctx.beginPath();
      ctx.moveTo(r * 0.9, -r * 0.45);
      ctx.lineTo(-r * 0.85, 0);
      ctx.lineTo(-r * 0.15, r * 0.25);
      ctx.lineTo(0, r * 0.85);
      ctx.lineTo(r * 0.25, r * 0.32);
      ctx.closePath();
      ctx.fill();
      break;
    }
    case 'clock': {
      ctx.beginPath();
      ctx.arc(0, 0, r * 0.82, 0, Math.PI * 2);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(0, -r * 0.45);
      ctx.lineTo(0, 0);
      ctx.lineTo(r * 0.38, r * 0.22);
      ctx.stroke();
      break;
    }
    case 'city': {
      ctx.fillRect(-r * 0.78, -r * 0.2, r * 0.42, r * 0.95);
      ctx.fillRect(-r * 0.24, -r * 0.75, r * 0.48, r * 1.5);
      ctx.fillRect(r * 0.36, -r * 0.4, r * 0.42, r * 1.15);
      break;
    }
    case 'pulse': {
      ctx.beginPath();
      ctx.moveTo(-r * 0.95, 0);
      ctx.lineTo(-r * 0.45, 0);
      ctx.lineTo(-r * 0.18, -r * 0.65);
      ctx.lineTo(r * 0.2, r * 0.68);
      ctx.lineTo(r * 0.5, -r * 0.22);
      ctx.lineTo(r * 0.95, 0);
      ctx.stroke();
      break;
    }
    case 'gem': {
      ctx.beginPath();
      ctx.moveTo(0, -r * 0.9);
      ctx.lineTo(r * 0.75, 0);
      ctx.lineTo(0, r * 0.9);
      ctx.lineTo(-r * 0.75, 0);
      ctx.closePath();
      ctx.stroke();
      break;
    }
    default: {
      ctx.beginPath();
      ctx.arc(0, 0, r * 0.72, 0, Math.PI * 2);
      ctx.stroke();
      ctx.beginPath();
      ctx.arc(0, 0, r * 0.25, 0, Math.PI * 2);
      ctx.fill();
      break;
    }
  }
  ctx.restore();
}
