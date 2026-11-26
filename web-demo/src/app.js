import {
  STYLE_CATALOG,
  STUDIO_PRESETS,
  NEON_HORIZON_LRC,
  parseLrc,
  createDefaultCoverCanvas,
  extractPaletteFromCanvas,
} from './demo-data.js';
import { StudioAudioEngine } from './audio-engine.js';
import { StudioRenderer } from './renderers.js';

const engine = new StudioAudioEngine();
const renderer = new StudioRenderer();
const thumbRenderer = new StudioRenderer();

const state = {
  doc: parseLrc(NEON_HORIZON_LRC, 36.0),
  styleIdx: 10, // Start showcasing flagship v2.10 Vinyl Groove!
  isPlaying: true,
  songTime: 25.7,
  coverCanvas: createDefaultCoverCanvas(),
  extractedPalette: null,
  filter: 'All',
  previewsPaused: false,
  settings: {
    fontFamily: 'Jost',
    lyricScale: 1.0,
    edgeGlowMode: 'NeonRush',
    rushSpeed: 1.0,
    rushThickness: 1.0,
    visualizerMode: 'SpectrumBars',
    beatDropEnabled: true,
    dropIntensity: 1.0,
    autoSyncColors: true,
    presetName: null,
    reducedMotion: window.matchMedia?.('(prefers-reduced-motion: reduce)').matches || false,
    overlaySim: false,
  },
};
state.extractedPalette = extractPaletteFromCanvas(state.coverCanvas);

// DOM refs
const el = {
  heroStage: document.getElementById('heroStage'),
  heroCanvas: document.getElementById('heroCanvas'),
  miniCover: document.getElementById('miniCoverCanvas'),
  heroCategory: document.getElementById('heroCategory'),
  heroCounter: document.getElementById('heroCounter'),
  heroTitle: document.getElementById('heroStyleTitle'),
  heroDesc: document.getElementById('heroStyleDesc'),
  pageDots: document.getElementById('pageDots'),
  swatch1: document.getElementById('swatch1'),
  swatch2: document.getElementById('swatch2'),
  swatch3: document.getElementById('swatch3'),
  trackTitle: document.getElementById('trackTitle'),
  trackArtist: document.getElementById('trackArtist'),
  seekSlider: document.getElementById('seekSlider'),
  currentTime: document.getElementById('currentTimeLabel'),
  durationLabel: document.getElementById('durationLabel'),
  playPauseBtn: document.getElementById('playPauseBtn'),
  playIcon: document.getElementById('playIcon'),
  pauseIcon: document.getElementById('pauseIcon'),
  soundToggleBtn: document.getElementById('soundToggleBtn'),
  soundToggleLabel: document.getElementById('soundToggleLabel'),
  playbackBadgeText: document.getElementById('playbackBadgeText'),
  dropBadge: document.getElementById('dropBadge'),
  fpsBadge: document.getElementById('fpsBadge'),
  stylesGrid: document.getElementById('stylesGrid'),
  galleryCount: document.getElementById('galleryCountCaption'),
  settingsDrawer: document.getElementById('settingsDrawer'),
  settingsToggleBtn: document.getElementById('settingsToggleBtn'),
  overlaySimBtn: document.getElementById('overlaySimBtn'),
  reducedMotionToggle: document.getElementById('reducedMotionToggle'),
};

function activeStyle() {
  return STYLE_CATALOG[state.styleIdx];
}

function resolvePalette() {
  if (state.settings.presetName && STUDIO_PRESETS[state.settings.presetName]) {
    return STUDIO_PRESETS[state.settings.presetName];
  }
  if (state.settings.autoSyncColors && state.extractedPalette) {
    // Blend album cover palette when user uploaded custom cover; otherwise style mood
    return state.customCoverLoaded ? state.extractedPalette : activeStyle().mood;
  }
  return activeStyle().mood;
}

function fmtTime(sec) {
  const s = Math.max(0, Math.floor(sec));
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
}

function syncMiniCover() {
  const mCtx = el.miniCover.getContext('2d');
  mCtx.clearRect(0, 0, 48, 48);
  mCtx.drawImage(state.coverCanvas, 0, 0, 48, 48);
}

function selectStyle(idx) {
  state.styleIdx = (idx + STYLE_CATALOG.length) % STYLE_CATALOG.length;
  const st = activeStyle();
  el.heroCategory.textContent = `${st.category} · ${st.chip.toUpperCase()}`;
  el.heroCounter.textContent = `${state.styleIdx + 1} / ${STYLE_CATALOG.length}`;
  el.heroTitle.textContent = st.name;
  el.heroDesc.textContent = st.description;

  document.querySelectorAll('.page-dot').forEach((d, i) => d.classList.toggle('active', i === state.styleIdx));
  document.querySelectorAll('.style-card').forEach((c) => {
    c.classList.toggle('active', Number(c.dataset.idx) === state.styleIdx);
  });
  updatePaletteSwatches();
}

function updatePaletteSwatches() {
  const p = resolvePalette();
  el.swatch1.style.background = p.a1;
  el.swatch2.style.background = p.a2;
  el.swatch3.style.background = p.a3;
}

function buildPaginationDots() {
  el.pageDots.innerHTML = '';
  STYLE_CATALOG.forEach((st, idx) => {
    const btn = document.createElement('button');
    btn.className = `page-dot ${idx === state.styleIdx ? 'active' : ''}`;
    btn.type = 'button';
    btn.title = st.name;
    btn.setAttribute('aria-label', `Select ${st.name}`);
    btn.addEventListener('click', () => selectStyle(idx));
    el.pageDots.appendChild(btn);
  });
}

const thumbItems = [];

function buildStyleGallery() {
  el.stylesGrid.innerHTML = '';
  thumbItems.length = 0;

  const filtered = STYLE_CATALOG.map((st, idx) => ({ st, idx })).filter(({ st }) => {
    if (state.filter === 'All') return true;
    if (state.filter === 'Visuals') return st.hasVisuals;
    return st.filterGroup === state.filter;
  });

  el.galleryCount.textContent = `${filtered.length} live interactive styles`;

  filtered.forEach(({ st, idx }) => {
    const card = document.createElement('button');
    card.type = 'button';
    card.className = `style-card ${idx === state.styleIdx ? 'active' : ''}`;
    card.dataset.idx = String(idx);
    card.innerHTML = `
      <div class="style-card-frame">
        <canvas></canvas>
        <span class="style-card-chip">${st.chip}</span>
      </div>
      <div class="style-card-meta">
        <strong>${st.name}</strong>
        <small>${st.category}</small>
      </div>
    `;
    card.addEventListener('click', () => {
      selectStyle(idx);
      if (window.scrollY > 120) el.heroStage.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
    el.stylesGrid.appendChild(card);
    const canvas = card.querySelector('canvas');
    thumbItems.push({ canvas, style: st, visible: true });
  });
}

function resizeCanvasDpi(canvas) {
  const rect = canvas.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0) return null;
  const dpr = Math.min(2, window.devicePixelRatio || 1);
  const pw = Math.round(rect.width * dpr);
  const ph = Math.round(rect.height * dpr);
  if (canvas.width !== pw || canvas.height !== ph) {
    canvas.width = pw;
    canvas.height = ph;
  }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  return { ctx, w: rect.width, h: rect.height };
}

function syncPlayUI() {
  el.playIcon.hidden = state.isPlaying;
  el.pauseIcon.hidden = !state.isPlaying;
  if (engine.audioEl) {
    if (state.isPlaying && engine.audioEl.paused) engine.audioEl.play().catch(() => {});
    if (!state.isPlaying && !engine.audioEl.paused) engine.audioEl.pause();
  }
}

// Event Listeners
document.getElementById('prevStyleBtn').addEventListener('click', () => selectStyle(state.styleIdx - 1));
document.getElementById('nextStyleBtn').addEventListener('click', () => selectStyle(state.styleIdx + 1));
document.getElementById('fullscreenBtn').addEventListener('click', () => {
  if (!document.fullscreenElement) el.heroStage.requestFullscreen?.();
  else document.exitFullscreen?.();
});

el.playPauseBtn.addEventListener('click', () => {
  state.isPlaying = !state.isPlaying;
  syncPlayUI();
});

el.soundToggleBtn.addEventListener('click', () => {
  const next = !engine.synthEnabled;
  engine.toggleSynth(next);
  el.soundToggleBtn.classList.toggle('active', next);
  el.soundToggleLabel.textContent = next ? (engine.mode === 'file' ? 'Audio On' : 'Synth Playing') : 'Play Synth Audio';
  el.playbackBadgeText.textContent = next ? (engine.mode === 'file' ? 'Local Track · Live FFT' : 'Web Synth · Live FFT') : 'Demo · Visual Clock';
});

document.getElementById('triggerDropBtn').addEventListener('click', () => {
  engine.triggerManualDrop();
});

el.seekSlider.addEventListener('input', (e) => {
  state.songTime = Number(e.target.value);
  if (engine.audioEl) engine.audioEl.currentTime = state.songTime;
});

el.overlaySimBtn.addEventListener('click', () => {
  state.settings.overlaySim = !state.settings.overlaySim;
  el.overlaySimBtn.classList.toggle('active', state.settings.overlaySim);
  el.heroStage.classList.toggle('overlay-sim', state.settings.overlaySim);
});

el.settingsToggleBtn.addEventListener('click', () => {
  const open = el.settingsDrawer.hidden;
  el.settingsDrawer.hidden = !open;
  el.settingsToggleBtn.setAttribute('aria-expanded', String(open));
});
document.getElementById('closeSettingsBtn').addEventListener('click', () => {
  el.settingsDrawer.hidden = true;
  el.settingsToggleBtn.setAttribute('aria-expanded', 'false');
});

document.getElementById('pauseThumbsBtn').addEventListener('click', (e) => {
  state.previewsPaused = !state.previewsPaused;
  e.currentTarget.textContent = state.previewsPaused ? 'Resume Previews' : 'Pause Previews';
});

document.querySelectorAll('.filter-chips .chip').forEach((btn) => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.filter-chips .chip').forEach((c) => c.classList.remove('active'));
    btn.classList.add('active');
    state.filter = btn.dataset.filter;
    buildStyleGallery();
  });
});

// Settings bindings
document.querySelectorAll('#fontSelector .seg').forEach((btn) => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('#fontSelector .seg').forEach((b) => b.classList.remove('active'));
    btn.classList.add('active');
    state.settings.fontFamily = btn.dataset.font;
  });
});

document.getElementById('lyricScaleSlider').addEventListener('input', (e) => {
  const val = Number(e.target.value);
  state.settings.lyricScale = val / 100;
  document.getElementById('lyricScaleVal').textContent = `${val}%`;
});

document.querySelectorAll('#edgeGlowModeRow .seg').forEach((btn) => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('#edgeGlowModeRow .seg').forEach((b) => b.classList.remove('active'));
    btn.classList.add('active');
    state.settings.edgeGlowMode = btn.dataset.edge;
  });
});

document.getElementById('rushSpeedSlider').addEventListener('input', (e) => {
  const val = Number(e.target.value);
  state.settings.rushSpeed = val / 100;
  document.getElementById('rushSpeedVal').textContent = `${val}%`;
});

document.getElementById('rushThickSlider').addEventListener('input', (e) => {
  const val = Number(e.target.value);
  state.settings.rushThickness = val / 100;
  document.getElementById('rushThickVal').textContent = `${val}%`;
});

document.querySelectorAll('#visModeRow .seg').forEach((btn) => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('#visModeRow .seg').forEach((b) => b.classList.remove('active'));
    btn.classList.add('active');
    state.settings.visualizerMode = btn.dataset.vis;
  });
});

document.getElementById('beatDropToggle').addEventListener('change', (e) => {
  state.settings.beatDropEnabled = e.target.checked;
});
document.getElementById('dropIntensitySlider').addEventListener('input', (e) => {
  const val = Number(e.target.value);
  state.settings.dropIntensity = val / 100;
  document.getElementById('dropIntensityVal').textContent = `${val}%`;
});
document.getElementById('autoSyncColorsToggle').addEventListener('change', (e) => {
  state.settings.autoSyncColors = e.target.checked;
  if (e.target.checked) state.settings.presetName = null;
  document.querySelectorAll('#presetGrid .seg').forEach((b) => b.classList.remove('active'));
  updatePaletteSwatches();
});

document.querySelectorAll('#presetGrid .seg').forEach((btn) => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('#presetGrid .seg').forEach((b) => b.classList.remove('active'));
    btn.classList.add('active');
    state.settings.presetName = btn.dataset.preset;
    state.settings.autoSyncColors = false;
    document.getElementById('autoSyncColorsToggle').checked = false;
    updatePaletteSwatches();
  });
});

el.reducedMotionToggle.checked = state.settings.reducedMotion;
el.reducedMotionToggle.addEventListener('change', (e) => {
  state.settings.reducedMotion = e.target.checked;
});

// File uploads (Local Audio / Cover Image / LRC)
document.getElementById('coverPickerBtn').addEventListener('click', () => document.getElementById('coverUploadInput').click());
document.getElementById('coverUploadInput').addEventListener('change', (e) => {
  const file = e.target.files?.[0];
  if (!file) return;
  const img = new Image();
  img.onload = () => {
    const c = document.createElement('canvas');
    c.width = 288;
    c.height = 288;
    const cx = c.getContext('2d');
    const size = Math.min(img.width, img.height);
    cx.drawImage(img, (img.width - size) / 2, (img.height - size) / 2, size, size, 0, 0, 288, 288);
    state.coverCanvas = c;
    state.customCoverLoaded = true;
    state.extractedPalette = extractPaletteFromCanvas(c);
    syncMiniCover();
    updatePaletteSwatches();
  };
  img.src = URL.createObjectURL(file);
});

document.getElementById('audioUploadInput').addEventListener('change', (e) => {
  const file = e.target.files?.[0];
  if (!file) return;
  const audio = engine.loadAudioFile(file);
  el.trackTitle.textContent = file.name.replace(/\.[^.]+$/, '');
  el.trackArtist.textContent = 'Local Audio · Live Web Audio FFT';
  el.soundToggleBtn.classList.add('active');
  el.soundToggleLabel.textContent = 'Audio On';
  el.playbackBadgeText.textContent = 'Local Track · Live FFT';
  state.isPlaying = true;
  audio.addEventListener('loadedmetadata', () => {
    if (Number.isFinite(audio.duration) && audio.duration > 1) {
      state.doc.durationSec = audio.duration;
      el.seekSlider.max = String(audio.duration);
      el.durationLabel.textContent = fmtTime(audio.duration);
    }
  });
  syncPlayUI();
});

document.getElementById('lrcUploadInput').addEventListener('change', async (e) => {
  const file = e.target.files?.[0];
  if (!file) return;
  const text = await file.text();
  const parsed = parseLrc(text, state.doc.durationSec);
  if (parsed.lines.length > 0) {
    state.doc = parsed;
    el.trackTitle.textContent = parsed.title;
    el.trackArtist.textContent = parsed.artist;
    el.seekSlider.max = String(parsed.durationSec);
    el.durationLabel.textContent = fmtTime(parsed.durationSec);
    state.songTime = 0;
  }
});

// Keyboard shortcuts
window.addEventListener('keydown', (e) => {
  if (e.target.tagName === 'INPUT') return;
  if (e.code === 'Space') { e.preventDefault(); state.isPlaying = !state.isPlaying; syncPlayUI(); }
  if (e.code === 'ArrowLeft') selectStyle(state.styleIdx - 1);
  if (e.code === 'ArrowRight') selectStyle(state.styleIdx + 1);
  if (e.key === 'd' || e.key === 'D') engine.triggerManualDrop();
  if (e.key === 'f' || e.key === 'F') document.getElementById('fullscreenBtn').click();
});

// Initialize UI
syncMiniCover();
buildPaginationDots();
buildStyleGallery();
selectStyle(state.styleIdx);

let lastMs = performance.now();
let fpsAccum = 0;
let fpsFrames = 0;

function tick(nowMs) {
  const dt = Math.min(0.05, Math.max(0.001, (nowMs - lastMs) / 1000));
  lastMs = nowMs;

  if (state.isPlaying) {
    if (engine.audioEl && !engine.audioEl.paused) {
      state.songTime = engine.audioEl.currentTime % state.doc.durationSec;
    } else {
      state.songTime = (state.songTime + dt) % state.doc.durationSec;
    }
    engine.stepSynthAudio(state.songTime, state.isPlaying);
  }

  el.seekSlider.value = String(state.songTime);
  el.currentTime.textContent = fmtTime(state.songTime);

  const realTimeSec = nowMs / 1000;
  const metrics = engine.sampleMetrics(state.songTime, dt, state.isPlaying, 1.0);
  el.dropBadge.hidden = !metrics.isClimaxActive || !state.settings.beatDropEnabled;

  // Render hero stage
  const heroBox = resizeCanvasDpi(el.heroCanvas);
  if (heroBox) {
    renderer.renderFrame({
      ctx: heroBox.ctx,
      width: heroBox.w,
      height: heroBox.h,
      songTimeSec: state.songTime,
      realTimeSec,
      dt,
      doc: state.doc,
      style: activeStyle(),
      palette: resolvePalette(),
      metrics,
      settings: state.settings,
      coverCanvas: state.coverCanvas,
      isCompact: false,
    });
  }

  // Render live gallery thumbnails
  if (!state.previewsPaused) {
    for (const item of thumbItems) {
      const box = resizeCanvasDpi(item.canvas);
      if (!box) continue;
      thumbRenderer.renderFrame({
        ctx: box.ctx,
        width: box.w,
        height: box.h,
        songTimeSec: (realTimeSec * 0.65 + item.style.previewOffsetSec) % state.doc.durationSec,
        realTimeSec,
        dt,
        doc: state.doc,
        style: item.style,
        palette: item.style.mood,
        metrics,
        settings: { ...state.settings, edgeGlowMode: 'Off', visualizerMode: 'None', overlaySim: false },
        coverCanvas: state.coverCanvas,
        isCompact: true,
      });
    }
  }

  fpsAccum += dt;
  fpsFrames += 1;
  if (fpsAccum >= 0.5) {
    el.fpsBadge.textContent = `${Math.round(fpsFrames / fpsAccum)} FPS`;
    fpsAccum = 0;
    fpsFrames = 0;
  }

  requestAnimationFrame(tick);
}

requestAnimationFrame(tick);
