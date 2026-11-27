import { drawSemanticIcon, matchSemanticIcon } from './demo-data.js';

function hexToRgba(hex, alpha) {
  const clean = hex.replace('#', '');
  const n = parseInt(clean.length === 3 ? clean.replace(/(.)/g, '$1$1') : clean, 16);
  const r = (n >> 16) & 255;
  const g = (n >> 8) & 255;
  const b = n & 255;
  return `rgba(${r}, ${g}, ${b}, ${Math.max(0, Math.min(1, alpha)).toFixed(3)})`;
}

function clamp(v, min, max) {
  return Math.max(min, Math.min(max, v));
}

function project3D(x, y, z, pitchDeg, yawDeg, rollDeg, focalLength, ox, oy) {
  const f = Math.max(200, focalLength);
  const rx = (pitchDeg * Math.PI) / 180;
  const ry = (yawDeg * Math.PI) / 180;
  const rz = (rollDeg * Math.PI) / 180;
  const cx = Math.cos(rx), sx = Math.sin(rx);
  const cy = Math.cos(ry), sy = Math.sin(ry);
  const cz = Math.cos(rz), sz = Math.sin(rz);

  const dx = x - ox;
  const dy = y - oy;
  const y1 = dy * cx - z * sx;
  const z1 = dy * sx + z * cx;
  const x2 = dx * cy + z1 * sy;
  const z2 = -dx * sy + z1 * cy;
  const x3 = x2 * cz - y1 * sz;
  const y3 = x2 * sz + y1 * cz;

  const denom = Math.max(f * 0.28, f + z2);
  const scale = clamp(f / denom, 0.36, 2.15);
  return { x: ox + x3 * scale, y: oy + y3 * scale, scale };
}

function resolveSlice(doc, songTimeSec) {
  const lines = doc.lines;
  if (!lines || lines.length === 0) return { line: null, prev: null, next: null, wordIdx: 0, lineProgress: 0 };
  let idx = lines.findIndex((l) => songTimeSec >= l.start && songTimeSec < l.end);
  if (idx === -1) {
    idx = songTimeSec < lines[0].start ? 0 : lines.length - 1;
  }
  const line = lines[idx];
  let wordIdx = 0;
  for (let i = 0; i < line.words.length; i++) {
    if (songTimeSec >= line.words[i].start) wordIdx = i;
  }
  const span = Math.max(0.2, line.end - line.start);
  const lineProgress = clamp((songTimeSec - line.start) / span, 0, 1);
  return {
    line,
    lineIndex: idx,
    prev: idx > 0 ? lines[idx - 1] : lines[lines.length - 1],
    next: idx + 1 < lines.length ? lines[idx + 1] : lines[0],
    wordIdx,
    lineProgress,
  };
}

function drawLegibleWord(ctx, text, x, y, fontStr, fill, strokeAlpha = 0.78) {
  ctx.save();
  ctx.font = fontStr;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.lineJoin = 'round';
  ctx.lineCap = 'round';
  ctx.lineWidth = 4.2;
  ctx.strokeStyle = `rgba(6, 7, 14, ${strokeAlpha})`;
  ctx.strokeText(text, x, y);
  ctx.fillStyle = fill;
  ctx.fillText(text, x, y);
  ctx.restore();
}

function hexToRgb(hex) {
  const clean = (hex || '#818CF8').replace('#', '');
  const full = clean.length === 3 ? clean.replace(/(.)/g, '$1$1') : clean;
  const n = parseInt(full, 16) || 0;
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
}

function rgbToHsl(r, g, b) {
  const rn = r / 255, gn = g / 255, bn = b / 255;
  const max = Math.max(rn, gn, bn), min = Math.min(rn, gn, bn);
  const d = max - min;
  const l = (max + min) * 0.5;
  if (d === 0) return { h: 0, s: 0, l };
  const s = d / (1 - Math.abs(2 * l - 1));
  let h = 0;
  if (max === rn) h = ((gn - bn) / d) % 6;
  else if (max === gn) h = (bn - rn) / d + 2;
  else h = (rn - gn) / d + 4;
  h = ((h * 60) + 360) % 360;
  return { h, s, l };
}

function hsla(h, s, l, a = 1) {
  const hh = ((h % 360) + 360) % 360;
  const ss = clamp(s * 100, 0, 100).toFixed(1);
  const ll = clamp(l * 100, 0, 100).toFixed(1);
  return `hsla(${hh.toFixed(1)}, ${ss}%, ${ll}%, ${clamp(a, 0, 1).toFixed(3)})`;
}

const FONT_STACKS = {
  Jost: "'Jost', sans-serif",
  'Bodoni Moda': "'Bodoni Moda', Georgia, serif",
  Mulish: "'Mulish', sans-serif",
  Nunito: "'Nunito', sans-serif",
  Bahnschrift: "'Bahnschrift', 'Barlow Condensed', sans-serif",
  Georgia: "'Georgia', serif",
  Impact: "'Impact', 'Anton', sans-serif",
  'Sitka Display': "'Sitka Display', 'Playfair Display', Georgia, serif",
  'Ink Free': "'Ink Free', 'Caveat', cursive",
  'Cascadia Mono': "'Cascadia Mono', 'JetBrains Mono', monospace",
};

function resolveFontCss(weight, sizePx, fontFamily) {
  const stack = FONT_STACKS[fontFamily] || `'${fontFamily || 'Jost'}', sans-serif`;
  return `${weight} ${sizePx}px ${stack}`;
}

export class StudioRenderer {
  constructor() {
    this.rushPhase = 0;
    this.counterPhase = 0.37;
    this._edgeBeatEnv = 0;
    this._edgeBassSwell = 0;
    this._edgeRollingEnergy = 0.25;
    this._edgeDropSurge = 0;
    this._edgePrevBass = 0;
    this._cachedCoverSrc = null;
    this._circularCoverCanvas = null;
    this._blurredBgCanvas = null;
    this._glassLastLineIdx = -1;
    this._glassLineScrollY = 0;
    this.particles = Array.from({ length: 42 }, (_, i) => ({
      x: ((i * 73) % 100) / 100,
      y: ((i * 41) % 100) / 100,
      vx: ((i % 7) - 3) * 0.015,
      vy: ((i % 5) - 2) * 0.012 - 0.008,
      r: 1.4 + (i % 4) * 0.8,
    }));
  }

  _ensureGlassCoverTextures(coverCanvas) {
    if (!coverCanvas) return;
    if (this._cachedCoverSrc === coverCanvas && this._glassCoverCanvas && this._lightBlurBgCanvas) return;
    this._cachedCoverSrc = coverCanvas;

    const side = 320;
    const cCan = document.createElement('canvas');
    cCan.width = side;
    cCan.height = side;
    const cCtx = cCan.getContext('2d');
    cCtx.drawImage(coverCanvas, 0, 0, side, side);
    this._glassCoverCanvas = cCan;

    // Lightly blurred 256x256 full cover texture so real artwork shapes + color vibe remain recognizable across stage
    const bSide = 256;
    const bCan = document.createElement('canvas');
    bCan.width = bSide;
    bCan.height = bSide;
    const bCtx = bCan.getContext('2d');
    bCtx.filter = 'blur(10px)';
    bCtx.drawImage(coverCanvas, -14, -14, bSide + 28, bSide + 28);
    bCtx.filter = 'none';
    this._lightBlurBgCanvas = bCan;
  }

  _computeHarmoniousVinylTheme(palette, realTimeSec, beatPulse, reducedMotion) {
    const candidates = [palette.a1, palette.a2, palette.a3, palette.scrim].map((hex) => {
      const rgb = hexToRgb(hex);
      return rgbToHsl(rgb.r, rgb.g, rgb.b);
    });
    let anchor = candidates[1];
    for (const c of candidates) {
      if (c.s > anchor.s && c.l > 0.14 && c.l < 0.90) anchor = c;
    }
    const baseHue = anchor.s < 0.12 ? 36 : anchor.h;
    const baseSat = anchor.s < 0.12 ? 0.55 : clamp(anchor.s, 0.58, 0.94);

    const phase = reducedMotion ? 0 : realTimeSec * (0.58 + beatPulse * 0.18);
    const swingA = Math.sin(phase);
    const swingB = Math.cos(phase * 0.72);

    // Analogous harmonious orbit locked to cover color family (±28° so red stays crimson/cherry/coral/rose!)
    const hPrimary = baseHue + swingA * 22;
    const hSecondary = baseHue + 20 + swingB * 18;
    const hTertiary = baseHue - 18 + swingA * 14;
    const hAmbient = baseHue + swingB * 8;

    return {
      baseHue,
      baseSat,
      hPrimary,
      hSecondary,
      hAmbient,
      primary: hsla(hPrimary, clamp(baseSat * 0.96, 0.56, 0.96), clamp(0.68 + beatPulse * 0.05, 0.60, 0.80)),
      secondary: hsla(hSecondary, clamp(baseSat * 0.90, 0.50, 0.92), 0.60),
      tertiary: hsla(hTertiary, clamp(baseSat * 0.52, 0.25, 0.62), 0.78),
      deepAmbient: hsla(hAmbient, clamp(baseSat * 0.86, 0.45, 0.88), 0.20),
      washGlow: hsla(hAmbient, clamp(baseSat * 0.95, 0.52, 0.95), 0.44),
      primaryAlpha: (a) => hsla(hPrimary, clamp(baseSat * 0.96, 0.56, 0.96), 0.68, a),
      secondaryAlpha: (a) => hsla(hSecondary, clamp(baseSat * 0.90, 0.50, 0.92), 0.60, a),
      deepAlpha: (a) => hsla(hAmbient, clamp(baseSat * 0.86, 0.45, 0.88), 0.20, a),
      washAlpha: (a) => hsla(hAmbient, clamp(baseSat * 0.95, 0.52, 0.95), 0.44, a),
    };
  }

  renderFrame({ ctx, width, height, songTimeSec, realTimeSec, dt, doc, style, palette, metrics, settings, coverCanvas, isCompact = false }) {
    ctx.clearRect(0, 0, width, height);

    const slice = resolveSlice(doc, songTimeSec);
    if (!slice.line) return;

    const isNeonRushBlack = settings.edgeGlowMode === 'NeonRush'
      && !isCompact
      && style.id !== 'VinylGroove'
      && settings.backdropMode !== 'AlbumArtBlur';

    const theme = this._computeHarmoniousVinylTheme(palette, realTimeSec, metrics.beatPulse, settings.reducedMotion);
    if (coverCanvas) {
      this._ensureGlassCoverTextures(coverCanvas);
    }

    // 1. Backdrop (Album Art Blur or Pure Black; Ambient Mesh removed)
    if (style.id !== 'VinylGroove' && (!settings.overlaySim || isCompact)) {
      if (settings.backdropMode === 'PureBlack' || isNeonRushBlack) {
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, 0, width, height);
      } else if (this._lightBlurBgCanvas || this._glassCoverCanvas) {
        this.drawAlbumArtBackdrop(ctx, width, height, realTimeSec, metrics, settings, theme, isCompact);
      } else {
        ctx.fillStyle = '#07080D';
        ctx.fillRect(0, 0, width, height);
      }
    }

    // 2. Audio Visualizer layer
    if (!isCompact && settings.visualizerMode !== 'None') {
      this.drawVisualizer(ctx, width, height, realTimeSec, dt, palette, metrics, settings);
    }

    // 3. Style Renderer
    ctx.save();
    const baseSize = clamp(Math.min(width * 0.056, height * 0.115) * (isCompact ? 1.15 : settings.lyricScale), 13, 64);
    const fontFamily = settings.fontFamily || 'Jost';
    const rCtx = {
      ctx, width, height, realTimeSec, songTimeSec, doc, slice, palette, metrics, settings, coverCanvas,
      trackTitle: settings.trackTitle || 'Neon Horizon',
      trackArtist: settings.trackArtist || 'LyricPulse · Demo Synth',
      isCompact, baseSize, fontFamily, theme,
    };

    switch (style.id) {
      case 'Fisheye': this.drawFisheye(rCtx, false); break;
      case 'FisheyeVisual': this.drawFisheye(rCtx, true); break;
      case 'Codestyle': this.drawCodestyle(rCtx, false); break;
      case 'CodestyleVisual': this.drawCodestyle(rCtx, true); break;
      case 'VisualStack': this.drawVisualStack(rCtx); break;
      case 'Constellation': this.drawConstellation(rCtx); break;
      case 'SpidersWeb': this.drawSpidersWeb(rCtx); break;
      case 'NodeLine': this.drawNodeLine(rCtx); break;
      case 'KineticWall': this.drawKineticWall(rCtx); break;
      case 'Messages': this.drawMessages(rCtx); break;
      case 'VinylGroove': this.drawVinylGroove(rCtx); break;
      default: this.drawFisheye(rCtx, false); break;
    }
    ctx.restore();

    // 5. Edge Glow / Neon Rush
    if (!isCompact && settings.edgeGlowMode !== 'Off') {
      this.drawEdgeGlow(ctx, width, height, dt, palette, metrics, settings);
    }
  }

  drawVisualizer(ctx, w, h, t, dt, palette, metrics, settings) {
    const mode = settings.visualizerMode;
    const bands = metrics.bands;
    const a = settings.reducedMotion ? 0.35 : 0.65;

    ctx.save();
    if (mode === 'SpectrumBars') {
      const count = bands.length;
      const barW = (w * 0.62) / count;
      const startX = w * 0.19;
      for (let i = 0; i < count; i++) {
        const val = bands[i];
        const bh = Math.max(4, val * h * 0.22);
        const x = startX + i * barW;
        const grad = ctx.createLinearGradient(x, h - bh, x, h);
        grad.addColorStop(0, hexToRgba(i % 2 === 0 ? palette.a1 : palette.a2, 0.72 * a));
        grad.addColorStop(1, hexToRgba(palette.a3, 0.08));
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.roundRect(x + 2, h - bh - 6, Math.max(2, barW - 4), bh, 3);
        ctx.fill();
      }
    } else if (mode === 'RadialHalo') {
      const cx = w * 0.5, cy = h * 0.5;
      const baseR = Math.min(w, h) * 0.24;
      ctx.strokeStyle = hexToRgba(palette.a2, 0.55 * a);
      ctx.lineWidth = 2.4;
      ctx.beginPath();
      for (let i = 0; i <= 48; i++) {
        const ang = (i / 48) * Math.PI * 2;
        const mag = bands[i % bands.length] * 46;
        const px = cx + Math.cos(ang) * (baseR + mag);
        const py = cy + Math.sin(ang) * (baseR + mag);
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      }
      ctx.closePath();
      ctx.stroke();
    } else if (mode === 'FluidWave') {
      for (let layer = 0; layer < 2; layer++) {
        ctx.strokeStyle = hexToRgba(layer === 0 ? palette.a2 : palette.a3, (0.42 - layer * 0.14) * a);
        ctx.lineWidth = 2.6;
        ctx.beginPath();
        for (let x = 0; x <= w; x += 14) {
          const idx = Math.floor((x / w) * (bands.length - 1));
          const amp = bands[idx] * h * 0.14;
          const y = h * 0.82 + Math.sin(x * 0.015 + t * (2.4 + layer)) * amp;
          if (x === 0) ctx.moveTo(x, y);
          else ctx.lineTo(x, y);
        }
        ctx.stroke();
      }
    } else if (mode === 'ParticleField') {
      for (const p of this.particles) {
        if (!settings.reducedMotion) {
          p.x = (p.x + p.vx * dt * (1 + metrics.bass * 3) + 1) % 1;
          p.y = (p.y + p.vy * dt * (1 + metrics.bass * 3) + 1) % 1;
        }
        ctx.fillStyle = hexToRgba(palette.a2, (0.28 + metrics.beatPulse * 0.4) * a);
        ctx.beginPath();
        ctx.arc(p.x * w, p.y * h, p.r + metrics.bass * 2.2, 0, Math.PI * 2);
        ctx.fill();
      }
    } else if (mode === 'BassHorizon') {
      const grad = ctx.createLinearGradient(0, h * 0.65, 0, h);
      grad.addColorStop(0, 'rgba(0,0,0,0)');
      grad.addColorStop(1, hexToRgba(palette.a2, (0.26 + metrics.bass * 0.35) * a));
      ctx.fillStyle = grad;
      ctx.fillRect(0, h * 0.65, w, h * 0.35);
    }
    ctx.restore();
  }

  drawEdgeGlow(ctx, w, h, dt, palette, metrics, settings) {
    const animMode = settings.reducedMotion ? 'Still' : (settings.edgeAnimMode || 'MusicSync');
    const speed = settings.rushSpeed ?? 1.0;
    const thick = settings.rushThickness ?? 1.0;
    const spill = settings.edgeSpill ?? 1.0;
    const twoColor = settings.edgeTwoColor !== false;

    const cPrimary = settings.edgeOverrideColors ? (settings.edgePrimaryColor || palette.a1) : palette.a1;
    const cSecondary = twoColor
      ? (settings.edgeOverrideColors ? (settings.edgeSecondaryColor || palette.a2) : palette.a2)
      : cPrimary;
    const cTertiary = twoColor ? (palette.a3 || cSecondary) : cPrimary;

    // Asymmetric beat kick + adaptive drop surge envelope (fast attack, silky release)
    const bassRaw = clamp(metrics.bass || 0, 0, 1.5);
    const midRaw = clamp(metrics.mid || 0, 0, 1.5);
    const trebleRaw = clamp(metrics.treble || 0, 0, 1.5);
    const beatRaw = clamp(metrics.beatPulse || 0, 0, 1.5);
    const safeDt = clamp(dt || 0.016, 0.001, 0.05);

    if (animMode === 'MusicSync') {
      const bassFlux = Math.max(0, bassRaw - this._edgePrevBass);
      this._edgePrevBass = bassRaw;
      const kickTarget = clamp(Math.max(beatRaw, bassFlux * 3.0), 0, 1.45);
      const kickCoeff = kickTarget > this._edgeBeatEnv ? (1 - Math.exp(-safeDt * 42)) : (1 - Math.exp(-safeDt * 5.6));
      const bassCoeff = bassRaw > this._edgeBassSwell ? (1 - Math.exp(-safeDt * 24)) : (1 - Math.exp(-safeDt * 5.2));
      this._edgeBeatEnv += (kickTarget - this._edgeBeatEnv) * kickCoeff;
      this._edgeBassSwell += (bassRaw - this._edgeBassSwell) * bassCoeff;

      const instantEnergy = bassRaw * 0.5 + midRaw * 0.3 + trebleRaw * 0.2;
      this._edgeRollingEnergy += (instantEnergy - this._edgeRollingEnergy) * (1 - Math.exp(-safeDt * 0.45));
      const surgeTarget = clamp((instantEnergy - this._edgeRollingEnergy) * 2.5, 0, 1.2);
      const surgeCoeff = surgeTarget > this._edgeDropSurge ? (1 - Math.exp(-safeDt * 24)) : (1 - Math.exp(-safeDt * 2.8));
      this._edgeDropSurge += (surgeTarget - this._edgeDropSurge) * surgeCoeff;
    } else {
      this._edgeBeatEnv *= 0.9;
      this._edgeBassSwell *= 0.9;
      this._edgeDropSurge *= 0.9;
    }

    if (animMode !== 'Still') {
      const pReact = animMode === 'MusicSync'
        ? (0.58 + this._edgeBassSwell * 1.1 + this._edgeBeatEnv * 1.38 + this._edgeDropSurge * 0.75)
        : 0.62;
      const cReact = animMode === 'MusicSync'
        ? (0.42 + midRaw * 0.85 + trebleRaw * 0.65 + this._edgeBeatEnv * 0.55)
        : 0.42;
      this.rushPhase = (this.rushPhase + safeDt * 0.36 * speed * pReact) % 1;
      this.counterPhase = (this.counterPhase - safeDt * 0.24 * speed * cReact + 1) % 1;
    }

    const beatImpact = animMode === 'MusicSync'
      ? clamp(this._edgeBeatEnv * 0.82 + this._edgeDropSurge * 0.65, 0, 1.55)
      : animMode === 'AmbientDrift'
        ? 0.14 + 0.12 * Math.sin(this.rushPhase * Math.PI * 2)
        : 0;
    const autoIntensity = animMode === 'MusicSync'
      ? clamp(0.56 + this._edgeBassSwell * 0.48 + this._edgeBeatEnv * 0.66 + this._edgeDropSurge * 0.44, 0.44, 1.85)
      : animMode === 'AmbientDrift'
        ? 0.94 + 0.14 * Math.sin(this.rushPhase * Math.PI * 2)
        : 0.94;
    const autoReach = animMode === 'MusicSync'
      ? clamp(0.82 + this._edgeBassSwell * 0.36 + this._edgeBeatEnv * 0.42 + this._edgeDropSurge * 0.32, 0.78, 1.58)
      : 1.0;

    const minSide = Math.min(w, h);
    const mode = settings.edgeGlowMode;
    const isRush = mode === 'NeonRush';
    const isPulse = mode === 'BeatPulse';
    const isSoft = mode === 'SoftAmbient';

    const modeGain = isSoft ? 0.64 : isPulse ? clamp(0.32 + this._edgeBassSwell * 0.7 + this._edgeBeatEnv * 0.95, 0.28, 1.8) : isRush ? 1.05 : 0.88;
    const reach = clamp(minSide * (0.095 + 0.045 * spill) * (0.58 + 0.42 * thick) * autoReach, 20, minSide * 0.26);
    const baseAlpha = clamp(0.34 * modeGain * autoIntensity, 0.06, 0.68);

    const drawEdgeCurtain = (x0, y0, x1, y1, rx, ry, rw, rh, col, alpha) => {
      const g = ctx.createLinearGradient(x0, y0, x1, y1);
      g.addColorStop(0.00, hexToRgba(col, alpha));
      g.addColorStop(0.26, hexToRgba(col, alpha * 0.52));
      g.addColorStop(0.62, hexToRgba(col, alpha * 0.14));
      g.addColorStop(1.00, hexToRgba(col, 0));
      ctx.fillStyle = g;
      ctx.fillRect(rx, ry, rw, rh);
    };

    const perimeterPoint = (angleRad) => {
      const hw = w * 0.5;
      const hh = h * 0.5;
      const dx = Math.cos(angleRad);
      const dy = Math.sin(angleRad);
      const tx = hw / Math.max(1e-5, Math.abs(dx));
      const ty = hh / Math.max(1e-5, Math.abs(dy));
      if (tx <= ty) {
        const sx = dx >= 0 ? 1 : -1;
        return { x: hw + sx * hw, y: hh + dy * tx, nx: sx, ny: 0 };
      }
      const sy = dy >= 0 ? 1 : -1;
      return { x: hw + dx * ty, y: hh + sy * hh, nx: 0, ny: sy };
    };

    const drawPlasmaOrb = (cx, cy, rad, col, alpha) => {
      const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, rad);
      g.addColorStop(0.00, hexToRgba(col, alpha));
      g.addColorStop(0.32, hexToRgba(col, alpha * 0.50));
      g.addColorStop(0.68, hexToRgba(col, alpha * 0.13));
      g.addColorStop(1.00, hexToRgba(col, 0));
      ctx.fillStyle = g;
      ctx.beginPath();
      ctx.arc(cx, cy, rad, 0, Math.PI * 2);
      ctx.fill();
    };

    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, w, h);
    ctx.clip();

    // Pass 1: Strip-free feathered inward ambient aurora curtains along all 4 edges (zero visible border line!)
    drawEdgeCurtain(0, 0, 0, reach, 0, 0, w, reach, cPrimary, baseAlpha);
    drawEdgeCurtain(0, h, 0, h - reach, 0, h - reach, w, reach, cSecondary, baseAlpha);
    drawEdgeCurtain(0, 0, reach, 0, 0, 0, reach, h, cSecondary, baseAlpha * 0.92);
    drawEdgeCurtain(w, 0, w - reach, 0, w - reach, 0, reach, h, cPrimary, baseAlpha * 0.92);

    // Pass 2: Additive counter-revolving off-bezel plasma orbs & beat-reactive corner blooms
    ctx.globalCompositeOperation = 'screen';
    const orbRad = clamp(minSide * (0.18 + 0.06 * spill) * (0.62 + 0.38 * thick) * autoReach, 34, minSide * 0.35);
    const orbAlpha = clamp((isRush ? 0.56 : 0.38) * modeGain * autoIntensity, 0.08, 0.84);

    if (isRush && animMode !== 'Still') {
      const a1 = this.rushPhase * Math.PI * 2;
      const a2 = a1 + Math.PI;
      const p1 = perimeterPoint(a1);
      const p2 = perimeterPoint(a2);
      drawPlasmaOrb(p1.x + p1.nx * orbRad * 0.22, p1.y + p1.ny * orbRad * 0.22, orbRad * 1.12, cPrimary, orbAlpha);
      drawPlasmaOrb(p2.x + p2.nx * orbRad * 0.22, p2.y + p2.ny * orbRad * 0.22, orbRad * 1.06, cSecondary, orbAlpha * 0.92);

      // Counter-orbiting harmonic plasma stream creates living crossing swells on beats
      const a3 = this.counterPhase * Math.PI * 2;
      const a4 = a3 + Math.PI;
      const p3 = perimeterPoint(a3);
      const p4 = perimeterPoint(a4);
      const cAlpha = clamp(orbAlpha * (0.48 + beatImpact * 0.32), 0.06, 0.72);
      drawPlasmaOrb(p3.x + p3.nx * orbRad * 0.25, p3.y + p3.ny * orbRad * 0.25, orbRad * 0.94, cTertiary, cAlpha);
      drawPlasmaOrb(p4.x + p4.nx * orbRad * 0.25, p4.y + p4.ny * orbRad * 0.25, orbRad * 0.88, cPrimary, cAlpha * 0.82);
    }

    if (!isSoft || beatImpact > 0.20) {
      const cRad = clamp(orbRad * 0.88, 28, minSide * 0.30);
      const cAlpha = clamp(orbAlpha * (isRush ? 0.36 : 0.62) * (0.55 + beatImpact * 0.55), 0.04, 0.64);
      const off = cRad * 0.26;
      drawPlasmaOrb(-off, -off, cRad, cPrimary, cAlpha);
      drawPlasmaOrb(w + off, -off, cRad, cSecondary, cAlpha);
      drawPlasmaOrb(w + off, h + off, cRad, cPrimary, cAlpha);
      drawPlasmaOrb(-off, h + off, cRad, cSecondary, cAlpha);
    }

    ctx.restore();
  }

  // Styles 1 & 2: Fisheye & Fisheye Visual
  drawFisheye({ ctx, width, height, slice, palette, metrics, settings, isCompact, baseSize, fontFamily }, withIcons) {
    const words = slice.line.words;
    const cx = width * 0.5;
    const cy = height * 0.5;
    const start = Math.max(0, slice.wordIdx - 1);
    const visible = [];
    for (let i = start; i <= Math.min(words.length - 1, slice.wordIdx + 1); i++) {
      visible.push({ word: words[i], idx: i, rel: i - slice.wordIdx });
    }

    // Subtle spherical optical lens rim
    if (!isCompact) {
      ctx.save();
      const lensR = Math.min(width, height) * 0.34;
      ctx.strokeStyle = hexToRgba(palette.a3, 0.18 + metrics.beatPulse * 0.14);
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.ellipse(cx, cy, lensR * 1.32, lensR * 0.88, 0, 0, Math.PI * 2);
      ctx.stroke();
      ctx.restore();
    }

    for (const item of visible) {
      const distFactor = Math.abs(item.rel);
      const bulgeScale = item.rel === 0 ? 1.24 + metrics.beatPulse * 0.14 : 0.72;
      const y = cy + item.rel * baseSize * 1.18;
      const fSize = Math.round(baseSize * bulgeScale);
      const font = resolveFontCss(800, fSize, fontFamily);

      // Chromatic RGB split on beat pulse for active word
      if (item.rel === 0 && !settings.reducedMotion && metrics.beatPulse > 0.35) {
        const shift = metrics.beatPulse * (isCompact ? 1.5 : 3.2);
        drawLegibleWord(ctx, item.word.text, cx - shift, y, font, 'rgba(255, 56, 120, 0.42)', 0);
        drawLegibleWord(ctx, item.word.text, cx + shift, y, font, 'rgba(56, 220, 255, 0.42)', 0);
      }

      const color = item.rel === 0 ? palette.a1 : hexToRgba(palette.a3, item.rel < 0 ? 0.48 : 0.72);
      drawLegibleWord(ctx, item.word.text, cx, y, font, color, 0.85 - distFactor * 0.2);

      if (withIcons && item.rel === 0) {
        const icon = matchSemanticIcon(item.word.text) || 'spark';
        ctx.font = font;
        const tw = ctx.measureText(item.word.text).width;
        drawSemanticIcon(ctx, icon, cx + tw * 0.5 + fSize * 0.55, y - fSize * 0.05, fSize * 0.42, palette.a2, true);
      }
    }
  }

  // Styles 3 & 4: Codestyle & Codestyle Visual
  drawCodestyle({ ctx, width, height, realTimeSec, slice, palette, baseSize, fontFamily, isCompact }, withIcons) {
    const fs = clamp(baseSize * 0.58, 12, 30);
    const lineH = fs * 1.58;
    const cx = width * 0.5;
    const cy = height * 0.5;
    const leftX = Math.max(24, cx - Math.min(width * 0.36, fs * 12));

    const linesToShow = [
      { idx: slice.lineIndex - 1, line: slice.prev, active: false },
      { idx: slice.lineIndex, line: slice.line, active: true },
      { idx: slice.lineIndex + 1, line: slice.next, active: false },
    ];

    linesToShow.forEach((row, r) => {
      if (!row.line) return;
      const y = cy + (r - 1) * lineH;
      const num = String((row.idx + 1 + 12) % 12 || 12).padStart(2, '0');
      ctx.save();
      ctx.font = `500 ${fs}px 'JetBrains Mono', monospace`;
      ctx.textBaseline = 'middle';

      // Gutter line number
      ctx.fillStyle = hexToRgba(palette.a3, row.active ? 0.75 : 0.35);
      ctx.fillText(num, leftX, y);

      let cursorX = leftX + fs * 2.2;
      if (row.active) {
        ctx.fillStyle = '#818CF8';
        const kw = 'lyric ';
        ctx.fillText(kw, cursorX, y);
        cursorX += ctx.measureText(kw).width;

        row.line.words.forEach((w, wi) => {
          const isLit = wi <= slice.wordIdx;
          const isCurrent = wi === slice.wordIdx;
          ctx.fillStyle = isCurrent ? palette.a1 : isLit ? palette.a2 : hexToRgba(palette.a3, 0.55);
          ctx.fillText(w.text + ' ', cursorX, y);
          cursorX += ctx.measureText(w.text + ' ').width;
          if (isCurrent && Math.floor(realTimeSec * 2.4) % 2 === 0) {
            ctx.fillStyle = '#F43F5E';
            ctx.fillRect(cursorX - 2, y - fs * 0.55, 2.5, fs * 1.1);
          }
        });

        if (withIcons && !isCompact) {
          const activeWord = row.line.words[slice.wordIdx]?.text || '';
          const icon = matchSemanticIcon(activeWord) || 'code';
          drawSemanticIcon(ctx, icon, cursorX + fs * 0.75, y, fs * 0.52, palette.a2, true);
        }
      } else {
        ctx.fillStyle = hexToRgba(palette.a3, 0.38);
        ctx.fillText(`// ${row.line.text}`, cursorX, y);
      }
      ctx.restore();
    });
  }

  // Style 5: Visual Stack
  drawVisualStack({ ctx, width, height, slice, palette, metrics, baseSize, fontFamily }) {
    const words = slice.line.words;
    const cx = width * 0.5;
    const cy = height * 0.5;
    const active = words[slice.wordIdx];
    const icon = matchSemanticIcon(active.text) || 'star';
    const iconR = baseSize * (0.72 + metrics.beatPulse * 0.14);

    // Hero glow ring + vector icon above active word
    ctx.save();
    ctx.strokeStyle = hexToRgba(palette.a2, 0.35 + metrics.beatPulse * 0.3);
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(cx, cy - baseSize * 0.95, iconR * 1.38, 0, Math.PI * 2);
    ctx.stroke();
    ctx.restore();

    drawSemanticIcon(ctx, icon, cx, cy - baseSize * 0.95, iconR, palette.a2, true);
    drawLegibleWord(ctx, active.text, cx, cy + baseSize * 0.25, resolveFontCss(800, Math.round(baseSize * 1.15), fontFamily), palette.a1);
    drawLegibleWord(ctx, slice.line.text, cx, cy + baseSize * 1.28, resolveFontCss(600, Math.round(baseSize * 0.42), fontFamily), hexToRgba(palette.a3, 0.75));
  }

  // Style 6: Constellation
  drawConstellation({ ctx, width, height, realTimeSec, slice, palette, metrics, baseSize, fontFamily, isCompact }) {
    const words = slice.line.words;
    const n = words.length;
    const padX = width * 0.15;
    const spanX = width * 0.70;
    const points = words.map((w, i) => {
      const frac = n <= 1 ? 0.5 : i / (n - 1);
      const waveY = Math.sin(frac * Math.PI * 1.7 + slice.lineIndex * 1.3) * (height * 0.18);
      return {
        x: padX + frac * spanX,
        y: height * 0.48 + waveY,
        word: w.text,
        lit: i <= slice.wordIdx,
        active: i === slice.wordIdx,
      };
    });

    // Background celestial starfield
    if (!isCompact) {
      ctx.save();
      for (let i = 0; i < 28; i++) {
        const sx = ((i * 197 + 41) % 100) / 100 * width;
        const sy = ((i * 113 + 17) % 100) / 100 * height;
        const tw = 0.25 + 0.35 * Math.sin(realTimeSec * 2.1 + i);
        ctx.fillStyle = `rgba(226, 232, 240, ${tw})`;
        ctx.beginPath();
        ctx.arc(sx, sy, (i % 3 === 0 ? 1.8 : 1.0), 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    }

    // Silk connecting curves between stars
    ctx.save();
    for (let i = 0; i < points.length - 1; i++) {
      const p1 = points[i], p2 = points[i + 1];
      ctx.strokeStyle = hexToRgba(p2.lit ? palette.a2 : palette.a3, p2.lit ? 0.78 : 0.22);
      ctx.lineWidth = p2.lit ? 2.2 : 1.1;
      ctx.beginPath();
      ctx.moveTo(p1.x, p1.y);
      ctx.quadraticCurveTo((p1.x + p2.x) * 0.5, (p1.y + p2.y) * 0.5 - 18, p2.x, p2.y);
      ctx.stroke();
    }
    ctx.restore();

    // Stars & labels
    points.forEach((p) => {
      const flare = p.active ? 9 + metrics.beatPulse * 4 : p.lit ? 5 : 3;
      drawSemanticIcon(ctx, 'star', p.x, p.y, flare, p.active ? palette.a1 : p.lit ? palette.a2 : hexToRgba(palette.a3, 0.5), p.lit);
      const fs = Math.round(baseSize * (p.active ? 0.72 : 0.45));
      drawLegibleWord(ctx, p.word, p.x, p.y - flare - fs * 0.75, resolveFontCss(700, fs, fontFamily), p.active ? palette.a1 : hexToRgba(palette.a3, p.lit ? 0.9 : 0.48));
    });
  }

  // Style 7: Spider's Web
  drawSpidersWeb({ ctx, width, height, realTimeSec, slice, palette, metrics, baseSize, fontFamily, settings }) {
    const cx = width * 0.5;
    const cy = height * 0.5;
    const maxR = Math.min(width, height) * 0.37;
    const spokes = Math.max(6, slice.line.words.length);
    const spin = settings.reducedMotion ? 0 : realTimeSec * 0.08;

    ctx.save();
    ctx.strokeStyle = hexToRgba(palette.a3, 0.24);
    ctx.lineWidth = 1;
    // Rings
    for (let r = 1; r <= 4; r++) {
      const rad = (maxR * r) / 4;
      ctx.beginPath();
      for (let i = 0; i <= spokes; i++) {
        const a = (i / spokes) * Math.PI * 2 + spin;
        const px = cx + Math.cos(a) * rad;
        const py = cy + Math.sin(a) * rad;
        if (i === 0) ctx.moveTo(px, py);
        else ctx.lineTo(px, py);
      }
      ctx.stroke();
    }
    // Spokes
    for (let i = 0; i < spokes; i++) {
      const a = (i / spokes) * Math.PI * 2 + spin;
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.lineTo(cx + Math.cos(a) * maxR, cy + Math.sin(a) * maxR);
      ctx.stroke();
    }
    ctx.restore();

    slice.line.words.forEach((w, i) => {
      const a = (i / spokes) * Math.PI * 2 + spin - Math.PI * 0.5;
      const rad = i === slice.wordIdx ? maxR * (0.42 + metrics.beatPulse * 0.08) : maxR * 0.74;
      const wx = cx + Math.cos(a) * rad;
      const wy = cy + Math.sin(a) * rad;
      const fs = Math.round(baseSize * (i === slice.wordIdx ? 0.78 : 0.44));
      drawLegibleWord(ctx, w.text, wx, wy, resolveFontCss(700, fs, fontFamily), i === slice.wordIdx ? palette.a1 : hexToRgba(palette.a2, i < slice.wordIdx ? 0.85 : 0.45));
    });
  }

  // Style 8: Node Line
  drawNodeLine({ ctx, width, height, slice, palette, metrics, baseSize, fontFamily }) {
    const words = slice.line.words;
    const n = words.length;
    const pts = words.map((w, i) => {
      const frac = n <= 1 ? 0.5 : i / (n - 1);
      return {
        x: width * (0.14 + frac * 0.72),
        y: height * 0.5 + Math.sin(frac * Math.PI * 2 + slice.lineIndex) * (height * 0.1),
        word: w.text,
        lit: i <= slice.wordIdx,
        active: i === slice.wordIdx,
      };
    });

    // Draw flowing spline
    ctx.save();
    ctx.lineCap = 'round';
    ctx.strokeStyle = hexToRgba(palette.a3, 0.25);
    ctx.lineWidth = 3;
    ctx.beginPath();
    pts.forEach((p, i) => (i === 0 ? ctx.moveTo(p.x, p.y) : ctx.lineTo(p.x, p.y)));
    ctx.stroke();

    ctx.strokeStyle = palette.a2;
    ctx.lineWidth = 3.5;
    ctx.shadowColor = palette.a2;
    ctx.shadowBlur = 12;
    ctx.beginPath();
    for (let i = 0; i <= slice.wordIdx; i++) {
      if (i === 0) ctx.moveTo(pts[i].x, pts[i].y);
      else ctx.lineTo(pts[i].x, pts[i].y);
    }
    ctx.stroke();
    ctx.restore();

    pts.forEach((p, i) => {
      const nr = p.active ? 8 + metrics.beatPulse * 4 : p.lit ? 5 : 3.5;
      ctx.save();
      ctx.fillStyle = p.active ? palette.a1 : p.lit ? palette.a2 : hexToRgba(palette.a3, 0.4);
      ctx.beginPath();
      ctx.arc(p.x, p.y, nr, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();

      const offsetY = i % 2 === 0 ? -nr - baseSize * 0.52 : nr + baseSize * 0.62;
      const fs = Math.round(baseSize * (p.active ? 0.76 : 0.44));
      drawLegibleWord(ctx, p.word, p.x, p.y + offsetY, resolveFontCss(700, fs, fontFamily), p.active ? palette.a1 : hexToRgba(palette.a3, p.lit ? 0.85 : 0.45));
    });
  }

  // Style 9: Kinetic Wall (Strict Affine 3D Projection — matches v2.10 fix!)
  drawKineticWall({ ctx, width, height, realTimeSec, slice, palette, metrics, settings, baseSize, fontFamily, isCompact }) {
    const cx = width * 0.5;
    const cy = height * 0.5;
    const words = slice.line.words;
    const wordFrac = words.length <= 1 ? 0.5 : slice.wordIdx / (words.length - 1);
    const yawDeg = settings.reducedMotion ? 0 : (wordFrac - 0.5) * 18;
    const pitchDeg = settings.reducedMotion ? 0 : 6.0;
    const rollDeg = settings.reducedMotion ? 0 : Math.sin(realTimeSec * 0.7) * 1.4;
    const focal = 1150;

    // Monumental background watermark at Z = +120
    if (!isCompact) {
      const wmProj = project3D(cx, cy - baseSize * 0.3, 120, pitchDeg, yawDeg, rollDeg, focal, cx, cy);
      ctx.save();
      ctx.translate(wmProj.x, wmProj.y);
      ctx.scale(wmProj.scale, wmProj.scale);
      drawLegibleWord(ctx, words[slice.wordIdx].text.toUpperCase(), 0, 0, resolveFontCss(900, Math.round(baseSize * 2.5), fontFamily), hexToRgba(palette.a2, 0.1), 0);
      ctx.restore();
    }

    // Surrounding context rows at Z = +45 / -25
    const rows = [
      { text: slice.prev.text, yOff: -baseSize * 1.45, z: 55, active: false },
      { text: words[slice.wordIdx].text, yOff: 0, z: -32, active: true },
      { text: slice.next.text, yOff: baseSize * 1.45, z: 45, active: false },
    ];

    rows.forEach((row) => {
      const xShift = row.active ? (wordFrac - 0.5) * width * 0.18 : 0;
      const p = project3D(cx + xShift, cy + row.yOff, row.z, pitchDeg, yawDeg, rollDeg, focal, cx, cy);
      const scale = p.scale * (row.active ? 1.2 + metrics.beatPulse * 0.1 : 0.6);
      const fs = Math.round(baseSize * scale);
      if (row.active) {
        // Soft extruded 3D shadow layers strictly using affine translates
        drawLegibleWord(ctx, row.text, p.x + 5, p.y + 5, resolveFontCss(800, fs, fontFamily), hexToRgba(palette.a3, 0.35), 0);
        drawLegibleWord(ctx, row.text, p.x, p.y, resolveFontCss(800, fs, fontFamily), palette.a1, 0.86);
      } else {
        drawLegibleWord(ctx, row.text, p.x, p.y, resolveFontCss(600, fs, fontFamily), hexToRgba(palette.a3, 0.45), 0.55);
      }
    });
  }

  // Style 10: Messages (Phone Screen Chat Thread)
  drawMessages({ ctx, width, height, slice, palette, baseSize, isCompact }) {
    const padW = Math.min(width * 0.72, isCompact ? width * 0.85 : 460);
    const leftX = (width - padW) * 0.5;
    const fs = clamp(baseSize * 0.42, 11, 18);
    const msgs = [
      { text: slice.prev.text, incoming: true, active: false },
      { text: slice.line.text, incoming: false, active: true },
      { text: slice.next.text, incoming: true, active: false },
    ];

    msgs.forEach((m, i) => {
      const y = height * 0.5 + (i - 1) * (fs * 3.1);
      ctx.save();
      ctx.font = `600 ${fs}px 'Inter', system-ui, sans-serif`;
      const tw = Math.min(padW * 0.82, ctx.measureText(m.text).width + fs * 1.6);
      const bh = fs * 2.15;
      const bx = m.incoming ? leftX : leftX + padW - tw;

      ctx.fillStyle = m.active
        ? hexToRgba(palette.a1 === '#FFFFFF' ? '#2563EB' : palette.a1, 0.92)
        : 'rgba(28, 32, 46, 0.88)';
      ctx.beginPath();
      ctx.roundRect(bx, y - bh * 0.5, tw, bh, 14);
      ctx.fill();

      ctx.strokeStyle = m.active ? hexToRgba(palette.a2, 0.75) : 'rgba(255,255,255,0.12)';
      ctx.lineWidth = 1;
      ctx.stroke();

      ctx.fillStyle = m.active && palette.a1.toUpperCase() === '#FFFFFF' ? '#0C0D12' : '#F8FAFC';
      ctx.textAlign = 'left';
      ctx.textBaseline = 'middle';
      ctx.fillText(m.text, bx + fs * 0.75, y, tw - fs * 1.4);
      ctx.restore();
    });
  }

  drawAlbumArtBackdrop(ctx, width, height, realTimeSec, metrics, settings, theme, isCompact) {
    const bgGrad = ctx.createLinearGradient(0, 0, width, height);
    bgGrad.addColorStop(0, theme.deepAmbient);
    bgGrad.addColorStop(1, '#08090F');
    ctx.fillStyle = bgGrad;
    ctx.fillRect(0, 0, width, height);

    const coverBg = this._lightBlurBgCanvas || this._glassCoverCanvas;
    if (coverBg) {
      ctx.save();
      const breathe = settings.reducedMotion ? 1.10 : 1.10 + metrics.bass * 0.03 + Math.sin(realTimeSec * 0.28) * 0.016;
      const fillScale = Math.max(width / coverBg.width, height / coverBg.height) * breathe;
      const drawW = coverBg.width * fillScale;
      const drawH = coverBg.height * fillScale;
      const dx = settings.reducedMotion ? 0 : Math.sin(realTimeSec * 0.22) * width * 0.022;
      const dy = settings.reducedMotion ? 0 : Math.cos(realTimeSec * 0.19) * height * 0.020;
      ctx.globalAlpha = 0.80 + metrics.bass * 0.10;
      ctx.filter = isCompact ? 'blur(5px)' : 'blur(12px)';
      ctx.drawImage(coverBg, (width - drawW) * 0.5 + dx, (height - drawH) * 0.5 + dy, drawW, drawH);
      ctx.filter = 'none';
      ctx.restore();
    }

    // Soft luminance + directional contrast scrim keeping artwork color vibe while ensuring crisp legibility
    const scrim = ctx.createLinearGradient(0, 0, width, height);
    scrim.addColorStop(0.0, theme.deepAlpha(0.18));
    scrim.addColorStop(0.45, 'rgba(6, 7, 14, 0.26)');
    scrim.addColorStop(1.0, theme.deepAlpha(0.48));
    ctx.fillStyle = scrim;
    ctx.fillRect(0, 0, width, height);
  }

  _wrapWordRows(ctx, words, maxWidth, fontStr) {
    ctx.save();
    ctx.font = fontStr;
    const rows = [];
    let current = [];
    let curW = 0;
    const spaceW = ctx.measureText(' ').width;
    for (let i = 0; i < words.length; i++) {
      const w = words[i];
      const tokenW = ctx.measureText(w.text).width;
      const needed = current.length === 0 ? tokenW : curW + spaceW + tokenW;
      if (current.length > 0 && needed > maxWidth) {
        rows.push(current);
        current = [{ text: w.text, wordIdx: i, width: tokenW }];
        curW = tokenW;
      } else {
        current.push({ text: w.text, wordIdx: i, width: tokenW });
        curW = needed;
      }
    }
    if (current.length > 0) rows.push(current);
    ctx.restore();
    return rows;
  }

  _formatClock(sec) {
    const s = Math.max(0, Math.floor(sec || 0));
    const m = Math.floor(s / 60);
    const rem = s % 60;
    return `${m}:${String(rem).padStart(2, '0')}`;
  }

  // Style 11: Glass Stage v4.2 — Spatial 3D Stage + Orbital Glass Rings + Floating 3D Companion Visual Orbs
  drawVinylGroove({ ctx, width, height, realTimeSec, songTimeSec, doc, slice, palette, metrics, settings, coverCanvas, trackTitle, trackArtist, baseSize, fontFamily, isCompact, theme }) {
    this._ensureGlassCoverTextures(coverCanvas);

    const reduced = !!settings.reducedMotion;
    const beat = metrics.beatPulse || 0;
    const bass = metrics.bass || 0;
    const cardSide = isCompact
      ? clamp(Math.min(width * 0.275, height * 0.48), 46, 108)
      : clamp(Math.min(width * 0.275, height * 0.48), 112, 256);
    const cardCx = width * 0.26;
    const cardCy = height * 0.43;
    const cardX = cardCx - cardSide * 0.5;
    const cardY = cardCy - cardSide * 0.5;
    const cardRadius = clamp(cardSide * 0.068, 5, 16);

    const pitchDeg = reduced ? 0 : 5.2 + bass * 2.2;
    const yawDeg = reduced ? 0 : Math.sin(realTimeSec * 0.68) * 5.2;
    const rollDeg = reduced ? 0 : Math.sin(realTimeSec * 0.52) * 1.1;
    const focal = 1120;

    // Pass 0: Lightly-blurred playing album cover background
    if (!settings.overlaySim || isCompact) {
      if (settings.backdropMode === 'PureBlack') {
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, 0, width, height);
      } else {
        this.drawAlbumArtBackdrop(ctx, width, height, realTimeSec, metrics, settings, theme, isCompact);
      }
    }

    // Pass 0.5: Spatial 3D Stage Atmosphere — reflective horizon floor, perspective rays & 3D parallax bokeh motes
    if (!isCompact && !settings.overlaySim) {
      const horizonY = height * 0.78;
      ctx.save();
      const floorGrad = ctx.createLinearGradient(0, horizonY, 0, height);
      floorGrad.addColorStop(0, theme.primaryAlpha(0.0));
      floorGrad.addColorStop(0.28, theme.primaryAlpha(0.11 + beat * 0.06));
      floorGrad.addColorStop(1, theme.deepAlpha(0.42));
      ctx.fillStyle = floorGrad;
      ctx.fillRect(0, horizonY, width, height - horizonY);

      // Subtle horizon specular line + receding perspective light grid lines
      const lineGrad = ctx.createLinearGradient(width * 0.06, horizonY, width * 0.94, horizonY);
      lineGrad.addColorStop(0, 'rgba(255, 255, 255, 0)');
      lineGrad.addColorStop(0.26, theme.primaryAlpha(0.24 + beat * 0.14));
      lineGrad.addColorStop(0.72, theme.secondaryAlpha(0.22 + beat * 0.12));
      lineGrad.addColorStop(1, 'rgba(255, 255, 255, 0)');
      ctx.strokeStyle = lineGrad;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(width * 0.06, horizonY);
      ctx.lineTo(width * 0.94, horizonY);
      ctx.stroke();

      const vpX = width * 0.42 + (reduced ? 0 : Math.sin(realTimeSec * 0.45) * 18);
      ctx.strokeStyle = theme.primaryAlpha(0.075 + beat * 0.04);
      ctx.lineWidth = 0.85;
      for (let g = -4; g <= 4; g++) {
        ctx.beginPath();
        ctx.moveTo(vpX + g * width * 0.055, horizonY);
        ctx.lineTo(vpX + g * width * 0.19, height);
        ctx.stroke();
      }

      // 16 deterministic 3D depth-parallax bokeh motes
      const moteT = reduced ? 0 : realTimeSec;
      for (let m = 0; m < 16; m++) {
        const seedA = Math.sin(m * 12.9898 + 1.3) * 43758.5453;
        const seedB = Math.sin(m * 78.233 + 4.7) * 43758.5453;
        const fx = seedA - Math.floor(seedA);
        const fy = seedB - Math.floor(seedB);
        const depth = 0.25 + ((m * 0.37) % 0.75);
        const mx = ((fx + moteT * (0.008 + depth * 0.012)) % 1.12 - 0.06) * width;
        const my = ((fy - moteT * (0.005 + depth * 0.009) + 10) % 1.08 - 0.04) * height;
        const pr = (1.6 + depth * 4.4) * (1 + beat * 0.28);
        const pMote = project3D(mx, my, (1 - depth) * 180 - 60, pitchDeg * 0.55, yawDeg * 0.8, 0, focal, width * 0.5, height * 0.5);
        const alpha = (0.08 + depth * 0.22 + beat * 0.08) * (0.65 + 0.35 * Math.sin(moteT * 1.4 + m * 1.7));
        const gradM = ctx.createRadialGradient(pMote.x, pMote.y, 0, pMote.x, pMote.y, pr * 2.4);
        gradM.addColorStop(0, m % 3 === 0 ? `rgba(255, 255, 255, ${(alpha * 1.15).toFixed(3)})` : (m % 2 === 0 ? theme.primaryAlpha(alpha) : theme.secondaryAlpha(alpha)));
        gradM.addColorStop(1, 'rgba(255, 255, 255, 0)');
        ctx.fillStyle = gradM;
        ctx.beginPath();
        ctx.arc(pMote.x, pMote.y, pr * 2.4, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    }

    // Pass 1: Volumetric Backlit Aura + Dual 3D Tilted Orbital Glass Rings + Tilted 3D Album Slab
    if (!isCompact && !settings.overlaySim) {
      ctx.save();
      const auraR = cardSide * (0.86 + beat * 0.14);
      const aura = ctx.createRadialGradient(cardCx, cardCy, cardSide * 0.18, cardCx, cardCy, auraR);
      aura.addColorStop(0, theme.primaryAlpha(0.38 + beat * 0.18));
      aura.addColorStop(0.55, theme.secondaryAlpha(0.16 + bass * 0.09));
      aura.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = aura;
      ctx.beginPath();
      ctx.arc(cardCx, cardCy, auraR, 0, Math.PI * 2);
      ctx.fill();

      // Dual 3D orbital frosted-glass rings encircling the album slab
      for (let ring = 0; ring < 2; ring++) {
        const rx = cardSide * (ring === 0 ? 0.72 : 0.80) * (1 + beat * 0.035);
        const ry = rx * (ring === 0 ? 0.28 : 0.22);
        const tilt = (ring === 0 ? -0.34 : 0.31) + (reduced ? 0 : Math.sin(realTimeSec * (ring === 0 ? 0.55 : -0.44)) * 0.06);
        const phase = reduced ? ring * 2.1 : realTimeSec * (ring === 0 ? 0.95 : -0.72) + ring * 2.4;

        ctx.save();
        ctx.translate(cardCx, cardCy);
        ctx.rotate(tilt);
        ctx.strokeStyle = ring === 0 ? theme.primaryAlpha(0.32 + beat * 0.18) : theme.secondaryAlpha(0.24 + beat * 0.14);
        ctx.lineWidth = ring === 0 ? 1.5 : 1.1;
        ctx.beginPath();
        ctx.ellipse(0, 0, rx, ry, 0, 0, Math.PI * 2);
        ctx.stroke();

        // Orbiting specular energy node
        const nx = Math.cos(phase) * rx;
        const ny = Math.sin(phase) * ry;
        const nodeGlow = ctx.createRadialGradient(nx, ny, 0, nx, ny, 10 + beat * 5);
        nodeGlow.addColorStop(0, 'rgba(255, 255, 255, 0.95)');
        nodeGlow.addColorStop(0.38, ring === 0 ? theme.primaryAlpha(0.72) : theme.secondaryAlpha(0.68));
        nodeGlow.addColorStop(1, 'rgba(255, 255, 255, 0)');
        ctx.fillStyle = nodeGlow;
        ctx.beginPath();
        ctx.arc(nx, ny, 10 + beat * 5, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
      }
      ctx.restore();
    }

    // 3D Floating Album Cover Slab with subtle live tilt, extruded glass thickness edge, and diagonal liquid sheen
    const slabSkewY = isCompact || reduced ? 0 : Math.sin(realTimeSec * 0.62) * 0.024;
    const slabTiltRot = isCompact || reduced ? 0 : Math.sin(realTimeSec * 0.48) * 0.016;
    const slabScale = isCompact || reduced ? 1 : 1 + beat * 0.022;
    const slabShiftX = isCompact || reduced ? 0 : Math.cos(realTimeSec * 0.62) * 2.5;
    const slabShiftY = isCompact || reduced ? 0 : Math.sin(realTimeSec * 0.85) * 3.0;

    ctx.save();
    ctx.translate(cardCx + slabShiftX, cardCy + slabShiftY);
    if (!isCompact && !reduced) {
      ctx.rotate(slabTiltRot);
      ctx.transform(1, slabSkewY, 0, 1, 0, 0);
      ctx.scale(slabScale, slabScale);
    }
    ctx.translate(-cardCx, -cardCy);

    // 3-layer extruded frosted-glass slab thickness edge (gives tangible 3D block depth)
    if (!isCompact) {
      const extrudeDirX = reduced ? 3.5 : (3.5 - Math.sin(realTimeSec * 0.62) * 2.2);
      const extrudeDirY = 3.2;
      for (let layer = 3; layer >= 1; layer--) {
        const frac = layer / 3;
        ctx.fillStyle = layer === 3 ? 'rgba(0, 0, 0, 0.42)' : theme.primaryAlpha(0.16 + (1 - frac) * 0.14);
        ctx.beginPath();
        ctx.roundRect(cardX + extrudeDirX * frac * 2.4, cardY + extrudeDirY * frac * 2.0, cardSide, cardSide, cardRadius);
        ctx.fill();
      }
    }

    ctx.save();
    ctx.shadowColor = 'rgba(0, 0, 0, 0.62)';
    ctx.shadowBlur = isCompact ? 14 : 38;
    ctx.shadowOffsetY = isCompact ? 6 : 16;
    ctx.fillStyle = theme.deepAmbient;
    ctx.beginPath();
    ctx.roundRect(cardX, cardY, cardSide, cardSide, cardRadius);
    ctx.fill();
    ctx.restore();

    if (this._glassCoverCanvas) {
      ctx.save();
      ctx.beginPath();
      ctx.roundRect(cardX, cardY, cardSide, cardSide, cardRadius);
      ctx.clip();
      ctx.drawImage(this._glassCoverCanvas, cardX, cardY, cardSide, cardSide);

      const glaze = ctx.createLinearGradient(cardX, cardY, cardX + cardSide * 0.45, cardY + cardSide);
      glaze.addColorStop(0, 'rgba(255, 255, 255, 0.24)');
      glaze.addColorStop(0.32, 'rgba(255, 255, 255, 0.04)');
      glaze.addColorStop(1, 'rgba(0, 0, 0, 0.14)');
      ctx.fillStyle = glaze;
      ctx.fillRect(cardX, cardY, cardSide, cardSide);

      // Sweeping diagonal liquid-glass specular sheen
      if (!isCompact && !reduced) {
        const sheenCycle = ((realTimeSec * 0.22) % 1.8) - 0.4;
        const sheenX = cardX + sheenCycle * cardSide * 1.4;
        const sheenGrad = ctx.createLinearGradient(sheenX - cardSide * 0.28, cardY, sheenX + cardSide * 0.28, cardY + cardSide);
        sheenGrad.addColorStop(0, 'rgba(255, 255, 255, 0)');
        sheenGrad.addColorStop(0.48, `rgba(255, 255, 255, ${(0.14 + beat * 0.12).toFixed(3)})`);
        sheenGrad.addColorStop(0.54, 'rgba(255, 255, 255, 0.04)');
        sheenGrad.addColorStop(1, 'rgba(255, 255, 255, 0)');
        ctx.fillStyle = sheenGrad;
        ctx.fillRect(cardX, cardY, cardSide, cardSide);
      }
      ctx.restore();
    }

    const rimGrad = ctx.createLinearGradient(cardX, cardY, cardX + cardSide, cardY + cardSide);
    rimGrad.addColorStop(0, 'rgba(255, 255, 255, 0.68)');
    rimGrad.addColorStop(0.5, theme.primaryAlpha(0.45));
    rimGrad.addColorStop(1, theme.secondaryAlpha(0.38));
    ctx.strokeStyle = rimGrad;
    ctx.lineWidth = isCompact ? 1.0 : 1.6;
    ctx.beginPath();
    ctx.roundRect(cardX, cardY, cardSide, cardSide, cardRadius);
    ctx.stroke();
    ctx.restore();

    // Wet-glass mirror reflection fading gently directly beneath the album slab
    if (!isCompact && !settings.overlaySim && this._glassCoverCanvas) {
      const reflH = Math.min(26, cardSide * 0.12);
      const reflY = cardY + cardSide + 4;
      ctx.save();
      ctx.beginPath();
      ctx.roundRect(cardX + 2, reflY, cardSide - 4, reflH, 6);
      ctx.clip();
      ctx.globalAlpha = 0.16;
      ctx.translate(0, reflY + cardSide);
      ctx.scale(1, -1);
      ctx.drawImage(this._glassCoverCanvas, cardX, 0, cardSide, cardSide);
      ctx.restore();

      ctx.save();
      const maskGrad = ctx.createLinearGradient(0, reflY, 0, reflY + reflH);
      maskGrad.addColorStop(0, 'rgba(0, 0, 0, 0)');
      maskGrad.addColorStop(1, theme.deepAlpha(0.85));
      ctx.fillStyle = maskGrad;
      ctx.fillRect(cardX, reflY, cardSide, reflH);
      ctx.restore();
    }

    // Pass 2: Left Column Luminous Scrubber Bar, Timestamps & Track Metadata directly under cover card
    if (!isCompact) {
      const durationSec = Math.max(1, doc?.durationSec || 36);
      const pct = clamp((songTimeSec || 0) / durationSec, 0, 1);
      const barY = cardY + cardSide + clamp(height * 0.044, 15, 26);
      const barH = 4.5;

      ctx.save();
      ctx.fillStyle = 'rgba(255, 255, 255, 0.20)';
      ctx.beginPath();
      ctx.roundRect(cardX, barY, cardSide, barH, 2.25);
      ctx.fill();

      const fillW = Math.max(5, cardSide * pct);
      const barGrad = ctx.createLinearGradient(cardX, barY, cardX + fillW, barY);
      barGrad.addColorStop(0, theme.primary);
      barGrad.addColorStop(0.65, theme.secondary);
      barGrad.addColorStop(1, '#FFFFFF');
      ctx.fillStyle = barGrad;
      ctx.beginPath();
      ctx.roundRect(cardX, barY, fillW, barH, 2.25);
      ctx.fill();

      // Beat-pulsing luminous scrubber knob halo
      const knobX = cardX + fillW;
      const knobY = barY + barH * 0.5;
      const haloR = 8 + beat * 5;
      const knobHalo = ctx.createRadialGradient(knobX, knobY, 1, knobX, knobY, haloR);
      knobHalo.addColorStop(0, theme.primaryAlpha(0.65));
      knobHalo.addColorStop(1, 'rgba(255, 255, 255, 0)');
      ctx.fillStyle = knobHalo;
      ctx.beginPath();
      ctx.arc(knobX, knobY, haloR, 0, Math.PI * 2);
      ctx.fill();

      ctx.fillStyle = '#FFFFFF';
      ctx.shadowColor = theme.primary;
      ctx.shadowBlur = 10;
      ctx.beginPath();
      ctx.arc(knobX, knobY, 4.4 + beat * 0.8, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();

      const timeFs = clamp(Math.round(cardSide * 0.056), 10, 13);
      ctx.save();
      ctx.font = `600 ${timeFs}px 'JetBrains Mono', monospace`;
      ctx.fillStyle = 'rgba(255, 255, 255, 0.72)';
      ctx.textBaseline = 'top';
      ctx.textAlign = 'left';
      ctx.fillText(this._formatClock(songTimeSec), cardX, barY + 10);
      ctx.textAlign = 'right';
      ctx.fillText(this._formatClock(durationSec), cardX + cardSide, barY + 10);

      const metaCenterX = cardCx;
      const titleFs = clamp(Math.round(cardSide * 0.088), 14, 21);
      const artistFs = clamp(Math.round(cardSide * 0.068), 11, 16);
      ctx.textAlign = 'center';
      ctx.font = resolveFontCss(700, titleFs, fontFamily);
      ctx.fillStyle = '#FFFFFF';
      ctx.fillText(trackTitle, metaCenterX, barY + 29, cardSide * 1.18);

      ctx.font = resolveFontCss(500, artistFs, fontFamily);
      ctx.fillStyle = 'rgba(255, 255, 255, 0.74)';
      ctx.fillText(trackArtist, metaCenterX, barY + 29 + titleFs * 1.28, cardSide * 1.18);
      ctx.restore();
    } else {
      // Compact scrubber hint for Style Gallery card
      const barY = cardY + cardSide + 7;
      ctx.fillStyle = 'rgba(255, 255, 255, 0.25)';
      ctx.fillRect(cardX, barY, cardSide, 2);
      ctx.fillStyle = '#FFFFFF';
      ctx.fillRect(cardX, barY, cardSide * 0.56, 2);
    }

    // Pass 3: Right Column — Apple-Music-style Left-Aligned Vertical Scrolling 3D Lyrics + Spatial Effects
    const lines = doc?.lines || [];
    const activeIdx = slice.lineIndex ?? 0;
    const colX = isCompact ? width * 0.48 : width * 0.475;
    const colMaxW = Math.max(90, width * 0.465);
    const activeFontSize = isCompact
      ? clamp(baseSize * 0.76, 11, 18)
      : clamp(baseSize * 0.62 * (settings.lyricScale || 1), 17, 32);
    const ctxFontActive = resolveFontCss(800, Math.round(activeFontSize), fontFamily);
    const ctxFontPassive = resolveFontCss(700, Math.round(activeFontSize * 0.90), fontFamily);
    const rowStep = activeFontSize * 1.28;
    const lineGap = activeFontSize * (isCompact ? 0.42 : 0.62);

    if (activeIdx !== this._glassLastLineIdx) {
      if (this._glassLastLineIdx >= 0 && !reduced && !isCompact) {
        this._glassLineScrollY = clamp(rowStep * 1.1, 14, 42);
      } else {
        this._glassLineScrollY = 0;
      }
      this._glassLastLineIdx = activeIdx;
    } else if (!reduced && Math.abs(this._glassLineScrollY) > 0.2) {
      this._glassLineScrollY *= 0.80;
    } else {
      this._glassLineScrollY = 0;
    }

    const offsets = isCompact ? [-1, 0, 1] : [-2, -1, 0, 1, 2, 3];
    const blocks = [];
    for (const rel of offsets) {
      const idx = activeIdx + rel;
      if (idx < 0 || idx >= lines.length) continue;
      const lineObj = lines[idx];
      const fnt = rel === 0 ? ctxFontActive : ctxFontPassive;
      const rows = this._wrapWordRows(ctx, lineObj.words, colMaxW, fnt);
      blocks.push({ rel, rows, font: fnt, height: rows.length * rowStep });
    }

    const activeBlock = blocks.find((b) => b.rel === 0);
    const activeAnchorY = height * (isCompact ? 0.50 : 0.45) + (isCompact ? 0 : this._glassLineScrollY);
    const activeTopY = activeAnchorY - (activeBlock ? activeBlock.height * 0.5 : 0);

    // Position blocks above and below the smoothly gliding active lyric line
    const positioned = [];
    if (activeBlock) positioned.push({ ...activeBlock, topY: activeTopY });

    let cursorUp = activeTopY - lineGap;
    for (const b of blocks.filter((x) => x.rel < 0).sort((a, b2) => b2.rel - a.rel)) {
      const topY = cursorUp - b.height;
      positioned.push({ ...b, topY });
      cursorUp = topY - lineGap;
    }

    let cursorDown = activeTopY + (activeBlock ? activeBlock.height : rowStep) + lineGap;
    for (const b of blocks.filter((x) => x.rel > 0).sort((a, b2) => a.rel - b2.rel)) {
      positioned.push({ ...b, topY: cursorDown });
      cursorDown += b.height + lineGap;
    }

    const activeBottomY = activeTopY + (activeBlock ? activeBlock.height : rowStep);
    const activeMidY = (activeTopY + activeBottomY) * 0.5;
    const totalWordsInAct = Math.max(1, slice.line?.words?.length || 1);
    const actLineProg = clamp(((slice.wordIdx || 0) + (slice.lineProgress ?? 0.5)) / totalWordsInAct, 0.05, 0.98);

    const activeWordText = slice.line?.words?.[slice.wordIdx]?.text?.toUpperCase() || '';
    if (!isCompact && activeWordText) {
      ctx.save();
      ctx.beginPath();
      ctx.rect(colX - 24, 0, width - (colX - 24), height);
      ctx.clip();
      const wmCenterX = colX + colMaxW * 0.54;
      const wmCenterY = activeAnchorY - activeFontSize * 0.35;
      const wmFontSize = Math.round(activeFontSize * 2.25);
      ctx.font = resolveFontCss(900, wmFontSize, fontFamily);
      const wmMeasured = Math.max(1, ctx.measureText(activeWordText).width);
      const wmFit = Math.min(1, (colMaxW * 1.05) / wmMeasured);
      const wmProj = project3D(wmCenterX, wmCenterY, 145, pitchDeg * 0.6, yawDeg * 0.45, 0, focal, wmCenterX, wmCenterY);
      ctx.translate(wmProj.x, wmProj.y);
      ctx.scale(wmFit, wmFit);
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillStyle = theme.secondaryAlpha(reduced ? 0.042 : 0.045 + beat * 0.045);
      ctx.fillText(activeWordText, 0, 0);
      ctx.restore();
    }

    // True Elliptical Volumetric Spotlight + Open-Ended VisionOS Frosted Glass Focus Deck + Sweeping Anamorphic Flare
    if (!isCompact && !settings.overlaySim) {
      const deckLeft = colX - 12;
      const deckRight = colX + colMaxW + 8;
      const deckTop = activeTopY - 8;
      const deckBottom = activeBottomY + 10;
      const spotR = Math.max(140, colMaxW * 0.56);
      const spotX = colX + colMaxW * 0.42;
      const ellipseRy = Math.max(38, (activeBottomY - activeTopY) * 0.95 + 24);

      ctx.save();
      ctx.translate(spotX, activeMidY);
      ctx.scale(1, ellipseRy / spotR);
      const spotGrad = ctx.createRadialGradient(0, 0, 4, 0, 0, spotR);
      spotGrad.addColorStop(0, theme.primaryAlpha(0.16 + beat * 0.10));
      spotGrad.addColorStop(0.55, theme.secondaryAlpha(0.06 + bass * 0.04));
      spotGrad.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = spotGrad;
      ctx.beginPath();
      ctx.arc(0, 0, spotR, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();

      ctx.save();
      const deckFill = ctx.createLinearGradient(deckLeft, activeMidY, deckRight, activeMidY);
      deckFill.addColorStop(0, theme.primaryAlpha(0.18 + beat * 0.07));
      deckFill.addColorStop(0.55, theme.deepAlpha(0.18));
      deckFill.addColorStop(1, 'rgba(0, 0, 0, 0)');
      ctx.fillStyle = deckFill;
      ctx.beginPath();
      ctx.roundRect(deckLeft, deckTop, deckRight - deckLeft, deckBottom - deckTop, 16);
      ctx.fill();

      const deckRim = ctx.createLinearGradient(deckLeft, deckTop, deckRight, deckTop);
      deckRim.addColorStop(0, 'rgba(255, 255, 255, 0.34)');
      deckRim.addColorStop(0.5, theme.primaryAlpha(0.26));
      deckRim.addColorStop(1, 'rgba(255, 255, 255, 0)');
      ctx.strokeStyle = deckRim;
      ctx.lineWidth = 1.1;
      ctx.stroke();

      // Sweeping anamorphic top edge flare + 4-point star glint
      const flareX = colX + colMaxW * 0.85 * actLineProg;
      const flareHalf = clamp(colMaxW * 0.16, 36, 92);
      const flareGrad = ctx.createLinearGradient(flareX - flareHalf, deckTop, flareX + flareHalf, deckTop);
      flareGrad.addColorStop(0, 'rgba(255, 255, 255, 0)');
      flareGrad.addColorStop(0.5, `rgba(255, 255, 255, ${(0.58 + beat * 0.30).toFixed(2)})`);
      flareGrad.addColorStop(1, 'rgba(255, 255, 255, 0)');
      ctx.strokeStyle = flareGrad;
      ctx.lineWidth = 1.8;
      ctx.beginPath();
      ctx.moveTo(flareX - flareHalf, deckTop);
      ctx.lineTo(flareX + flareHalf, deckTop);
      ctx.stroke();
      ctx.restore();
    }

    if (!isCompact && lines.length > 0) {
      const railY = Math.max(54, cardY - 18);
      ctx.save();
      // Frosted Telemetry Capsule Header
      const capX = colX - 18;
      const capY = railY - 12;
      const capW = 104;
      const capGrad = ctx.createLinearGradient(capX, capY, capX + capW, capY + 24);
      capGrad.addColorStop(0, theme.primaryAlpha(0.24));
      capGrad.addColorStop(1, theme.deepAlpha(0.66));
      ctx.fillStyle = capGrad;
      ctx.beginPath();
      ctx.roundRect(capX, capY, capW, 24, 12);
      ctx.fill();
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.32)';
      ctx.lineWidth = 1;
      ctx.stroke();

      ctx.font = `600 10.5px 'JetBrains Mono', monospace`;
      ctx.textAlign = 'left';
      ctx.textBaseline = 'middle';
      ctx.fillStyle = 'rgba(255, 255, 255, 0.88)';
      ctx.fillText(`${String(activeIdx + 1).padStart(2, '0')}/${String(lines.length).padStart(2, '0')}`, capX + 12, railY);
      const bands = metrics.bands || [];
      for (let b = 0; b < 6 && b < bands.length; b++) {
        const mag = clamp(bands[(b * 3) % bands.length], 0.12, 1.0);
        const barH = 3.5 + mag * 9.5;
        const bx = capX + 54 + b * 5.2;
        ctx.fillStyle = b % 2 === 0 ? theme.primaryAlpha(0.55 + mag * 0.4) : theme.secondaryAlpha(0.55 + mag * 0.4);
        ctx.beginPath();
        ctx.roundRect(bx, railY - barH * 0.5, 2.8, barH, 1.4);
        ctx.fill();
      }

      // Sculpted 3D Harmonic Spine + audio ticks + active-line starburst prism node + horizontal laser bridge
      const vRailX = Math.max(cardX + cardSide + 18, colX - 20);
      const vRailTop = Math.max(capY + 40, height * 0.22);
      const vRailH = Math.max(60, height * 0.56);
      const vRailBot = vRailTop + vRailH;
      const linePct = lines.length > 1 ? clamp(activeIdx / (lines.length - 1), 0, 1) : 0.5;
      ctx.fillStyle = 'rgba(255, 255, 255, 0.13)';
      ctx.beginPath();
      ctx.roundRect(vRailX - 1.3, vRailTop, 2.6, vRailH, 1.3);
      ctx.fill();

      const beadY = vRailTop + vRailH * linePct;
      const vFillGrad = ctx.createLinearGradient(0, vRailTop, 0, beadY);
      vFillGrad.addColorStop(0, theme.secondaryAlpha(0.32));
      vFillGrad.addColorStop(1, theme.primary);
      ctx.fillStyle = vFillGrad;
      ctx.beginPath();
      ctx.roundRect(vRailX - 1.4, vRailTop, 2.8, Math.max(4, beadY - vRailTop), 1.4);
      ctx.fill();

      for (let tk = 0; tk < 10; tk++) {
        const tkFrac = tk / 9;
        const tkY = vRailTop + vRailH * tkFrac;
        const bandMag = clamp(bands[(tk * 3) % Math.max(1, bands.length)] || 0.25, 0.08, 1.0);
        const tkLen = 2 + bandMag * 9;
        ctx.fillStyle = tk % 2 === 0 ? theme.primaryAlpha(0.22 + bandMag * 0.28) : theme.secondaryAlpha(0.22 + bandMag * 0.28);
        ctx.beginPath();
        ctx.roundRect(vRailX - 4 - tkLen, tkY - 0.9, tkLen, 1.8, 0.9);
        ctx.fill();
      }

      const focusSpineY = clamp(activeMidY, vRailTop, vRailBot);
      const bridgeGrad = ctx.createLinearGradient(vRailX + 3, focusSpineY, colX - 4, focusSpineY);
      bridgeGrad.addColorStop(0, 'rgba(255, 255, 255, 0.85)');
      bridgeGrad.addColorStop(1, 'rgba(255, 255, 255, 0)');
      ctx.strokeStyle = bridgeGrad;
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(vRailX + 3, focusSpineY);
      ctx.lineTo(colX - 4, focusSpineY);
      ctx.stroke();

      ctx.fillStyle = '#FFFFFF';
      ctx.shadowColor = theme.primary;
      ctx.shadowBlur = 10;
      ctx.beginPath();
      ctx.arc(vRailX, beadY, 2.8, 0, Math.PI * 2);
      ctx.fill();

      // 4-Point Starburst Prism Node on Spine
      const sR = 5.8 + beat * 2.2;
      const iR = 1.8;
      ctx.beginPath();
      ctx.moveTo(vRailX, focusSpineY - sR);
      ctx.lineTo(vRailX + iR, focusSpineY - iR);
      ctx.lineTo(vRailX + sR, focusSpineY);
      ctx.lineTo(vRailX + iR, focusSpineY + iR);
      ctx.lineTo(vRailX, focusSpineY + sR);
      ctx.lineTo(vRailX - iR, focusSpineY + iR);
      ctx.lineTo(vRailX - sR, focusSpineY);
      ctx.lineTo(vRailX - iR, focusSpineY - iR);
      ctx.closePath();
      ctx.fill();
      ctx.restore();
    }

    for (const block of positioned) {
      const dist = Math.abs(block.rel);
      const isAct = block.rel === 0;
      const zBase = isAct ? -22 : dist * 38;
      const arcIndent = isAct || isCompact ? 0 : Math.min(colMaxW * 0.22, dist * dist * 7.5);
      const baseAlpha = isAct ? 1.0 : block.rel < 0 ? (dist === 1 ? 0.52 : 0.26) : (dist === 1 ? 0.60 : dist === 2 ? 0.34 : 0.17);

      block.rows.forEach((rowTokens, rIdx) => {
        const yRaw = block.topY + rIdx * rowStep + rowStep * 0.5;
        if (yRaw < -rowStep || yRaw > height + rowStep) return;

        const xInput = isAct ? colX : colX + arcIndent + (zBase * 0.04);
        const proj = project3D(xInput, yRaw, zBase, pitchDeg, yawDeg, isAct ? rollDeg : 0, focal, width * 0.70, activeAnchorY);
        const yDraw = proj.y;
        const scale = isAct ? 1.0 : proj.scale;

        ctx.save();
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        let cursorX = colX;

        if (!isAct) {
          const fs = Math.max(9, Math.round(activeFontSize * 0.88 * scale));
          ctx.font = resolveFontCss(700, fs, fontFamily);
          const textLine = rowTokens.map((t) => t.text).join(' ');
          if (!reduced && dist >= 2 && !isCompact) {
            ctx.filter = dist === 2 ? 'blur(0.75px)' : 'blur(1.5px)';
          }
          ctx.fillStyle = `rgba(255, 255, 255, ${baseAlpha.toFixed(3)})`;
          ctx.shadowColor = 'rgba(0, 0, 0, 0.35)';
          ctx.shadowBlur = 10;
          ctx.fillText(textLine, isCompact ? colX : proj.x, yDraw, Math.max(60, colMaxW - arcIndent));
          ctx.filter = 'none';
        } else {
          // Active Line: Prism Vocal Spotlight (volumetric aurora + 5-slice 3D + laser glint & star cursor + rising embers + persistent word charms)
          const baseFs = Math.round(activeFontSize);
          ctx.font = resolveFontCss(800, baseFs, fontFamily);
          const spaceW = ctx.measureText(' ').width;

          for (const tok of rowTokens) {
            const isWordActive = tok.wordIdx === slice.wordIdx;
            const isWordSung = tok.wordIdx <= slice.wordIdx;
            const popScale = isWordActive && !isCompact
              ? (1.12 + (reduced ? 0 : beat * 0.075))
              : 1.0;
            const wordFs = Math.round(baseFs * popScale);
            const yShift = (isWordActive && !isCompact ? -3.0 - (beat * 1.5) : 0) +
              (!isCompact && !reduced ? Math.sin(tok.wordIdx * 1.15 + activeIdx * 0.65) * activeFontSize * 0.08 : 0);
            const wordZ = isWordActive && !isCompact && !reduced
              ? (-64 - beat * 28)
              : !isWordSung && !reduced
                ? (16 + Math.sin(realTimeSec * 1.8 + tok.wordIdx * 1.1) * 16)
                : -8;
            const wProj = project3D(cursorX, yDraw + yShift, wordZ, pitchDeg, yawDeg, 0, focal, width * 0.70, activeAnchorY);
            const drawX = cursorX;
            const drawY = wProj.y;

            ctx.save();
            ctx.font = resolveFontCss(800, wordFs, fontFamily);
            const wWidth = ctx.measureText(tok.text).width;
            const semanticIcon = matchSemanticIcon(tok.text);

            if (isWordActive && !isCompact && !reduced) {
              const tiltRad = ((tok.wordIdx % 2 === 0 ? 1 : -1) * 0.032) * (0.45 + beat * 0.65);
              ctx.translate(drawX + wWidth * 0.5, drawY);
              ctx.rotate(tiltRad);

              // Volumetric radial aurora bloom behind active singing word (zero boxy outline border)
              const aurR = Math.max(30, wWidth * 0.72);
              const aurScaleY = (wordFs * 0.78) / aurR;
              ctx.save();
              ctx.scale(1, aurScaleY);
              const aurGrad = ctx.createRadialGradient(0, 0, 2, 0, 0, aurR);
              aurGrad.addColorStop(0, theme.primaryAlpha(0.28 + beat * 0.16));
              aurGrad.addColorStop(0.55, theme.secondaryAlpha(0.10));
              aurGrad.addColorStop(1, 'rgba(0, 0, 0, 0)');
              ctx.fillStyle = aurGrad;
              ctx.beginPath();
              ctx.arc(0, 0, aurR, 0, Math.PI * 2);
              ctx.fill();
              ctx.restore();

              // Chromatic RGB split on beat hits
              if (beat > 0.18) {
                const split = clamp(beat * 4.0, 1.2, 4.2);
                ctx.fillStyle = 'rgba(34, 211, 238, 0.32)';
                ctx.fillText(tok.text, -wWidth * 0.5 - split, 0);
                ctx.fillStyle = 'rgba(255, 56, 120, 0.32)';
                ctx.fillText(tok.text, -wWidth * 0.5 + split, 0);
              }

              // Deep 3D parallax cast shadow + 5-slice sculpted isometric extrusion
              const yawDir = clamp(yawDeg / 6.0, -1, 1);
              const stepX = (1.35 + beat * 0.55) * (0.75 - yawDir * 0.35);
              const stepY = 1.15 + beat * 0.48;
              ctx.fillStyle = 'rgba(0, 0, 0, 0.36)';
              ctx.fillText(tok.text, -wWidth * 0.5 + stepX * 6.5, stepY * 6.2);
              for (let s = 5; s >= 1; s--) {
                const frac = s / 5;
                ctx.fillStyle = s >= 4
                  ? theme.deepAlpha(0.24 + (1 - frac) * 0.24)
                  : s >= 2
                    ? theme.secondaryAlpha(0.34 + (1 - frac) * 0.20)
                    : theme.primaryAlpha(0.62);
                ctx.fillText(tok.text, -wWidth * 0.5 + stepX * s, stepY * s);
              }

              // Dynamic vocal progress linear gradient across glyphs
              const grad = ctx.createLinearGradient(-wWidth * 0.5, 0, wWidth * 0.5, 0);
              const prog = clamp(slice.lineProgress ?? 0.5, 0.12, 0.96);
              grad.addColorStop(0, '#FFFFFF');
              grad.addColorStop(Math.max(0.05, prog - 0.08), '#FFFFFF');
              grad.addColorStop(prog, theme.primary);
              grad.addColorStop(Math.min(1.0, prog + 0.14), '#FFFFFF');

              ctx.shadowColor = theme.primaryAlpha(0.85);
              ctx.shadowBlur = 18;
              ctx.fillStyle = grad;
              ctx.fillText(tok.text, -wWidth * 0.5, 0);

              // Crisp upper-left 3D specular edge gleam
              ctx.shadowBlur = 0;
              ctx.fillStyle = 'rgba(255, 255, 255, 0.36)';
              ctx.fillText(tok.text, -wWidth * 0.5 - 0.7, -0.8);

              // 6 rising vocal embers
              for (let k = 0; k < 6; k++) {
                const life = ((realTimeSec * (0.55 + (k % 3) * 0.18)) + k * 0.37) % 1.0;
                const eAlpha = Math.sin(life * Math.PI) * (0.45 + beat * 0.35);
                const ex = ((k / 5 - 0.5) * wWidth * 1.15) + Math.sin(realTimeSec * 2.1 + k) * 4;
                const ey = wordFs * 0.20 - life * wordFs * 1.15;
                ctx.fillStyle = k % 2 === 0 ? `rgba(255, 255, 255, ${eAlpha.toFixed(2)})` : theme.primaryAlpha(eAlpha);
                ctx.beginPath();
                ctx.arc(ex, ey, 1.1 + (k % 2) * 0.65, 0, Math.PI * 2);
                ctx.fill();
              }

              // Tapered laser glint + 4-point star singing cursor
              const barW = Math.max(10, wWidth * 0.96);
              const barL = -barW * 0.5;
              const barY = wordFs * 0.60;
              ctx.fillStyle = theme.primaryAlpha(0.18);
              ctx.fillRect(barL, barY, barW, 2.0);
              const fillW = Math.max(4, barW * clamp(beat * 0.4 + 0.55, 0.35, 1.0));
              const tipX = barL + fillW;
              const barGrad = ctx.createLinearGradient(barL, barY, tipX, barY);
              barGrad.addColorStop(0, theme.secondaryAlpha(0.65));
              barGrad.addColorStop(0.55, theme.primary);
              barGrad.addColorStop(1, '#FFFFFF');
              ctx.fillStyle = barGrad;
              ctx.fillRect(barL, barY, fillW, 2.2);

              const cR = 4.8 + beat * 2.0;
              ctx.fillStyle = '#FFFFFF';
              ctx.shadowColor = theme.primary;
              ctx.shadowBlur = 10;
              ctx.beginPath();
              ctx.moveTo(tipX, barY + 1 - cR);
              ctx.lineTo(tipX + 1.4, barY + 1 - 1.4);
              ctx.lineTo(tipX + cR, barY + 1);
              ctx.lineTo(tipX + 1.4, barY + 1 + 1.4);
              ctx.lineTo(tipX, barY + 1 + cR);
              ctx.lineTo(tipX - 1.4, barY + 1 + 1.4);
              ctx.lineTo(tipX - cR, barY + 1);
              ctx.lineTo(tipX - 1.4, barY + 1 - 1.4);
              ctx.closePath();
              ctx.fill();
              ctx.shadowBlur = 0;

              // 3D Floating Frosted-Glass Companion Visual Orb above active matched words
              if (semanticIcon) {
                const orbR = clamp(wordFs * 0.46, 12, 18);
                const bobY = Math.sin(realTimeSec * 4.4 + tok.wordIdx) * 3.0;
                const orbY = -(wordFs * 0.72 + orbR + 4) + bobY;

                ctx.strokeStyle = theme.primaryAlpha(0.32 + beat * 0.25);
                ctx.lineWidth = 1.0;
                ctx.beginPath();
                ctx.arc(0, orbY, orbR + 3 + beat * 2, 0, Math.PI * 2);
                ctx.stroke();

                ctx.save();
                ctx.shadowColor = 'rgba(0, 0, 0, 0.48)';
                ctx.shadowBlur = 14;
                ctx.shadowOffsetY = 5;
                const orbGrad = ctx.createRadialGradient(-orbR * 0.32, orbY - orbR * 0.32, 1, 0, orbY, orbR);
                orbGrad.addColorStop(0, 'rgba(255, 255, 255, 0.44)');
                orbGrad.addColorStop(0.55, theme.primaryAlpha(0.32 + beat * 0.16));
                orbGrad.addColorStop(1, theme.deepAlpha(0.68));
                ctx.fillStyle = orbGrad;
                ctx.beginPath();
                ctx.arc(0, orbY, orbR, 0, Math.PI * 2);
                ctx.fill();
                ctx.restore();

                ctx.strokeStyle = `rgba(255, 255, 255, ${(0.55 + beat * 0.25).toFixed(2)})`;
                ctx.lineWidth = 1.25;
                ctx.beginPath();
                ctx.arc(0, orbY, orbR, 0, Math.PI * 2);
                ctx.stroke();

                drawSemanticIcon(ctx, semanticIcon, 0, orbY, orbR * 0.54, '#FFFFFF', true);
              }
            } else {
              ctx.shadowColor = 'rgba(255, 255, 255, 0.25)';
              ctx.shadowBlur = 6;
              ctx.fillStyle = isWordSung ? '#FFFFFF' : 'rgba(255, 255, 255, 0.68)';
              ctx.fillText(tok.text, drawX, drawY);

              // Persistent Jewel Mini-Charm on previously sung matched words in the active line
              if (semanticIcon && isWordSung && !isCompact) {
                const charmX = drawX + wWidth * 0.5;
                const charmY = drawY - wordFs * 0.80;
                const charmR = clamp(wordFs * 0.28, 8.5, 13);
                ctx.fillStyle = theme.deepAlpha(0.66);
                ctx.beginPath();
                ctx.arc(charmX, charmY, charmR, 0, Math.PI * 2);
                ctx.fill();
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.46)';
                ctx.lineWidth = 0.9;
                ctx.stroke();
                drawSemanticIcon(ctx, semanticIcon, charmX, charmY, charmR * 0.55, '#FFFFFF', false);
              }
            }
            ctx.restore();

            ctx.font = resolveFontCss(800, baseFs, fontFamily);
            cursorX += (tok.width || wWidth) + spaceW;
          }
        }
        ctx.restore();
      });
    }
  }
}
