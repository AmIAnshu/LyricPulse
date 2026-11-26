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

export class StudioRenderer {
  constructor() {
    this.rushPhase = 0;
    this._cachedCoverSrc = null;
    this._circularCoverCanvas = null;
    this._blurredBgCanvas = null;
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

    // 1. Backdrop (Ambient Mesh, selectable Album Art Blur for ALL 11 styles, or Pure Black)
    if (style.id !== 'VinylGroove' && (!settings.overlaySim || isCompact)) {
      if (settings.backdropMode === 'AlbumArtBlur' && (this._lightBlurBgCanvas || this._glassCoverCanvas)) {
        this.drawAlbumArtBackdrop(ctx, width, height, realTimeSec, metrics, settings, theme, isCompact);
      } else if (settings.backdropMode === 'PureBlack' || isNeonRushBlack) {
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, 0, width, height);
      } else {
        ctx.fillStyle = '#08090D';
        ctx.fillRect(0, 0, width, height);
        const pulseBoost = settings.reducedMotion ? 0 : metrics.beatPulse * 0.14;
        const g1 = ctx.createRadialGradient(width * 0.28, height * 0.35, 10, width * 0.28, height * 0.35, width * 0.65);
        g1.addColorStop(0, hexToRgba(palette.a2, 0.19 + pulseBoost));
        g1.addColorStop(1, 'rgba(0,0,0,0)');
        ctx.fillStyle = g1;
        ctx.fillRect(0, 0, width, height);

        const g2 = ctx.createRadialGradient(width * 0.74, height * 0.68, 10, width * 0.74, height * 0.68, width * 0.6);
        g2.addColorStop(0, hexToRgba(palette.a3, 0.15 + pulseBoost * 0.7));
        g2.addColorStop(1, 'rgba(0,0,0,0)');
        ctx.fillStyle = g2;
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

    if (animMode !== 'Still') {
      const musicDrive = animMode === 'MusicSync' ? (0.85 + metrics.bass * 1.15 + metrics.beatPulse * 0.70) : 0.62;
      this.rushPhase = (this.rushPhase + dt * 0.42 * speed * musicDrive) % 1;
    }

    const pulseScale = animMode === 'MusicSync'
      ? (1.0 + metrics.bass * 0.36 + metrics.beatPulse * 0.48)
      : animMode === 'AmbientDrift'
        ? (1.0 + 0.08 * Math.sin(this.rushPhase * Math.PI * 2))
        : 1.0;

    const minSide = Math.min(w, h);
    const radius = clamp(minSide * 0.026, 12, 28);
    const mode = settings.edgeGlowMode;
    const isRush = mode === 'NeonRush';
    const isRim = mode === 'NeonRim';

    const buildRevolvingBeamGradient = (lowAlpha, peakAlpha, withWhiteCrest) => {
      const grad = ctx.createConicGradient(this.rushPhase * Math.PI * 2, w * 0.5, h * 0.5);
      if (isRush && animMode !== 'Still') {
        grad.addColorStop(0.00, hexToRgba(cPrimary, peakAlpha));
        grad.addColorStop(0.08, withWhiteCrest ? 'rgba(255, 255, 255, 0.98)' : hexToRgba(cPrimary, peakAlpha));
        grad.addColorStop(0.18, hexToRgba(cSecondary, peakAlpha * 0.78));
        grad.addColorStop(0.32, hexToRgba(cPrimary, lowAlpha));
        grad.addColorStop(0.46, hexToRgba(cSecondary, lowAlpha * 1.15));
        grad.addColorStop(0.54, hexToRgba(cSecondary, peakAlpha * 0.92));
        grad.addColorStop(0.62, withWhiteCrest ? 'rgba(255, 255, 255, 0.92)' : hexToRgba(cSecondary, peakAlpha));
        grad.addColorStop(0.72, hexToRgba(cPrimary, peakAlpha * 0.72));
        grad.addColorStop(0.86, hexToRgba(cSecondary, lowAlpha));
        grad.addColorStop(1.00, hexToRgba(cPrimary, peakAlpha));
      } else {
        grad.addColorStop(0.00, hexToRgba(cPrimary, peakAlpha));
        grad.addColorStop(0.32, hexToRgba(cPrimary, peakAlpha * 0.92));
        grad.addColorStop(0.58, hexToRgba(cSecondary, peakAlpha * 0.92));
        grad.addColorStop(0.84, hexToRgba(cSecondary, peakAlpha * 0.88));
        grad.addColorStop(1.00, hexToRgba(cPrimary, peakAlpha));
      }
      return grad;
    };

    ctx.save();
    ctx.beginPath();
    ctx.roundRect(0, 0, w, h, radius);
    ctx.clip();

    // Pass 1: Wide inward Lumn Gaussian glow wash hugging rounded edges (revolving orbital hotspots in NeonRush)
    const spillReach = clamp(minSide * 0.08 * spill * pulseScale, 16, minSide * 0.22);
    const baseGrad = buildRevolvingBeamGradient(isRush ? 0.12 : 0.36, isRush ? 0.78 : isRim ? 0.56 : 0.44, false);

    ctx.strokeStyle = baseGrad;
    ctx.lineWidth = clamp(12 * thick * spill * pulseScale, 6, 48);
    ctx.shadowColor = cPrimary;
    ctx.shadowBlur = spillReach;
    ctx.beginPath();
    ctx.roundRect(2, 2, w - 4, h - 4, Math.max(6, radius - 2));
    ctx.stroke();

    // Pass 2: Luminous bezel tube + Revolving Neon Rush comet tails
    if (isRush || isRim) {
      const tubeGrad = buildRevolvingBeamGradient(isRush ? 0.09 : 0.72, 0.96, isRush);
      ctx.shadowBlur = clamp(minSide * 0.025 * thick, 6, 22);
      ctx.shadowColor = cSecondary;
      ctx.strokeStyle = tubeGrad;
      ctx.lineWidth = clamp(3.4 * thick * (0.95 + metrics.beatPulse * 0.28), 2, 12);
      ctx.beginPath();
      ctx.roundRect(3, 3, w - 6, h - 6, Math.max(4, radius - 3));
      ctx.stroke();
    }

    // Pass 3: Razor-sharp Neon Rush orbital rim filament
    if (isRush) {
      const coreGrad = buildRevolvingBeamGradient(0.05, 1.0, true);
      ctx.shadowBlur = 4;
      ctx.shadowColor = '#FFFFFF';
      ctx.strokeStyle = coreGrad;
      ctx.lineWidth = clamp(1.5 * thick, 1.2, 4.2);
      ctx.beginPath();
      ctx.roundRect(2, 2, w - 4, h - 4, Math.max(4, radius - 2));
      ctx.stroke();
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
      const font = `800 ${fSize}px '${fontFamily}', sans-serif`;

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
    drawLegibleWord(ctx, active.text, cx, cy + baseSize * 0.25, `800 ${Math.round(baseSize * 1.15)}px '${fontFamily}', sans-serif`, palette.a1);
    drawLegibleWord(ctx, slice.line.text, cx, cy + baseSize * 1.28, `600 ${Math.round(baseSize * 0.42)}px '${fontFamily}', sans-serif`, hexToRgba(palette.a3, 0.75));
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
      drawLegibleWord(ctx, p.word, p.x, p.y - flare - fs * 0.75, `700 ${fs}px '${fontFamily}', sans-serif`, p.active ? palette.a1 : hexToRgba(palette.a3, p.lit ? 0.9 : 0.48));
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
      drawLegibleWord(ctx, w.text, wx, wy, `700 ${fs}px '${fontFamily}', sans-serif`, i === slice.wordIdx ? palette.a1 : hexToRgba(palette.a2, i < slice.wordIdx ? 0.85 : 0.45));
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
      drawLegibleWord(ctx, p.word, p.x, p.y + offsetY, `700 ${fs}px '${fontFamily}', sans-serif`, p.active ? palette.a1 : hexToRgba(palette.a3, p.lit ? 0.85 : 0.45));
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
      drawLegibleWord(ctx, words[slice.wordIdx].text.toUpperCase(), 0, 0, `900 ${Math.round(baseSize * 2.5)}px '${fontFamily}', sans-serif`, hexToRgba(palette.a2, 0.1), 0);
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
        drawLegibleWord(ctx, row.text, p.x + 5, p.y + 5, `800 ${fs}px '${fontFamily}', sans-serif`, hexToRgba(palette.a3, 0.35), 0);
        drawLegibleWord(ctx, row.text, p.x, p.y, `800 ${fs}px '${fontFamily}', sans-serif`, palette.a1, 0.86);
      } else {
        drawLegibleWord(ctx, row.text, p.x, p.y, `600 ${fs}px '${fontFamily}', sans-serif`, hexToRgba(palette.a3, 0.45), 0.55);
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

  // Style 11: Glass Stage v4.1 — Apple-Music-Inspired Split Stage + Left Scrubber Card + Right Scrolling 3D Lyrics
  drawVinylGroove({ ctx, width, height, realTimeSec, songTimeSec, doc, slice, palette, metrics, settings, coverCanvas, trackTitle, trackArtist, baseSize, fontFamily, isCompact, theme }) {
    this._ensureGlassCoverTextures(coverCanvas);

    const cardSide = isCompact
      ? clamp(Math.min(width * 0.275, height * 0.48), 46, 108)
      : clamp(Math.min(width * 0.275, height * 0.48), 112, 256);
    const cardCx = width * 0.26;
    const cardCy = height * 0.43;
    const cardX = cardCx - cardSide * 0.5;
    const cardY = cardCy - cardSide * 0.5;
    const cardRadius = clamp(cardSide * 0.068, 5, 16);

    // Pass 0: Lightly-blurred playing album cover background
    if (!settings.overlaySim || isCompact) {
      if (settings.backdropMode === 'PureBlack') {
        ctx.fillStyle = '#000000';
        ctx.fillRect(0, 0, width, height);
      } else {
        this.drawAlbumArtBackdrop(ctx, width, height, realTimeSec, metrics, settings, theme, isCompact);
      }
    }

    // Pass 1: Sleek Square Rounded Album Cover Card (with subtle beat glow & GlassOS rim)
    ctx.save();
    ctx.shadowColor = 'rgba(0, 0, 0, 0.58)';
    ctx.shadowBlur = isCompact ? 14 : 34;
    ctx.shadowOffsetY = isCompact ? 6 : 14;
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
      glaze.addColorStop(0, 'rgba(255, 255, 255, 0.22)');
      glaze.addColorStop(0.32, 'rgba(255, 255, 255, 0.04)');
      glaze.addColorStop(1, 'rgba(0, 0, 0, 0.14)');
      ctx.fillStyle = glaze;
      ctx.fillRect(cardX, cardY, cardSide, cardSide);
      ctx.restore();
    }

    const rimGrad = ctx.createLinearGradient(cardX, cardY, cardX + cardSide, cardY + cardSide);
    rimGrad.addColorStop(0, 'rgba(255, 255, 255, 0.58)');
    rimGrad.addColorStop(1, theme.primaryAlpha(0.34));
    ctx.strokeStyle = rimGrad;
    ctx.lineWidth = isCompact ? 1.0 : 1.4;
    ctx.beginPath();
    ctx.roundRect(cardX, cardY, cardSide, cardSide, cardRadius);
    ctx.stroke();

    // Pass 2: Left Column Scrubber Bar, Timestamps & Track Metadata directly under cover card
    if (!isCompact) {
      const durationSec = Math.max(1, doc?.durationSec || 36);
      const pct = clamp((songTimeSec || 0) / durationSec, 0, 1);
      const barY = cardY + cardSide + clamp(height * 0.040, 12, 24);
      const barH = 4;

      ctx.save();
      ctx.fillStyle = 'rgba(255, 255, 255, 0.22)';
      ctx.beginPath();
      ctx.roundRect(cardX, barY, cardSide, barH, 2);
      ctx.fill();

      const fillW = Math.max(4, cardSide * pct);
      ctx.fillStyle = 'rgba(255, 255, 255, 0.92)';
      ctx.beginPath();
      ctx.roundRect(cardX, barY, fillW, barH, 2);
      ctx.fill();

      ctx.fillStyle = '#FFFFFF';
      ctx.shadowColor = theme.primary;
      ctx.shadowBlur = 8;
      ctx.beginPath();
      ctx.arc(cardX + fillW, barY + barH * 0.5, 4.2, 0, Math.PI * 2);
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
      ctx.font = `700 ${titleFs}px '${fontFamily}', sans-serif`;
      ctx.fillStyle = '#FFFFFF';
      ctx.fillText(trackTitle, metaCenterX, barY + 29, cardSide * 1.18);

      ctx.font = `500 ${artistFs}px '${fontFamily}', sans-serif`;
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

    // Pass 3: Right Column — Apple-Music-style Left-Aligned Vertical Scrolling 3D Lyrics
    const lines = doc?.lines || [];
    const activeIdx = slice.lineIndex ?? 0;
    const colX = isCompact ? width * 0.48 : width * 0.475;
    const colMaxW = Math.max(90, width * 0.465);
    const activeFontSize = isCompact
      ? clamp(baseSize * 0.76, 11, 18)
      : clamp(baseSize * 0.62 * (settings.lyricScale || 1), 17, 32);
    const ctxFontActive = `800 ${Math.round(activeFontSize)}px '${fontFamily}', sans-serif`;
    const ctxFontPassive = `700 ${Math.round(activeFontSize * 0.90)}px '${fontFamily}', sans-serif`;
    const rowStep = activeFontSize * 1.24;
    const lineGap = activeFontSize * (isCompact ? 0.42 : 0.62);

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
    const activeAnchorY = height * (isCompact ? 0.50 : 0.45);
    const activeTopY = activeAnchorY - (activeBlock ? activeBlock.height * 0.5 : 0);

    // Position blocks above and below the vertically centered active lyric line
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

    const yawDeg = settings.reducedMotion ? 0 : Math.sin(realTimeSec * 0.68) * 3.8;
    const focal = 1120;

    for (const block of positioned) {
      const dist = Math.abs(block.rel);
      const isAct = block.rel === 0;
      const zBase = isAct ? -14 : dist === 1 ? 65 : dist === 2 ? 135 : 200;
      const baseAlpha = isAct ? 1.0 : dist === 1 ? 0.40 : dist === 2 ? 0.24 : 0.12;

      block.rows.forEach((rowTokens, rIdx) => {
        const yRaw = block.topY + rIdx * rowStep + rowStep * 0.5;
        if (yRaw < -rowStep || yRaw > height + rowStep) return;

        const proj = project3D(colX, yRaw, zBase, 0, yawDeg, 0, focal, width * 0.70, activeAnchorY);
        const yDraw = proj.y;
        const scale = isAct ? 1.0 : proj.scale;

        ctx.save();
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        let cursorX = colX;

        if (!isAct) {
          const fs = Math.max(9, Math.round(activeFontSize * 0.90 * scale));
          ctx.font = `700 ${fs}px '${fontFamily}', sans-serif`;
          const textLine = rowTokens.map((t) => t.text).join(' ');
          ctx.fillStyle = `rgba(255, 255, 255, ${baseAlpha.toFixed(3)})`;
          ctx.shadowColor = 'rgba(0, 0, 0, 0.35)';
          ctx.shadowBlur = 10;
          ctx.fillText(textLine, cursorX, yDraw);
        } else {
          // Active Line: bright crisp white with word-level 3D Z-depth extrusion + chromatic bloom
          const baseFs = Math.round(activeFontSize);
          ctx.font = `800 ${baseFs}px '${fontFamily}', sans-serif`;
          const spaceW = ctx.measureText(' ').width;

          for (const tok of rowTokens) {
            const isWordActive = tok.wordIdx === slice.wordIdx;
            const isWordSung = tok.wordIdx <= slice.wordIdx;
            const popScale = isWordActive && !isCompact
              ? (1.07 + (settings.reducedMotion ? 0 : metrics.beatPulse * 0.045))
              : 1.0;
            const wordFs = Math.round(baseFs * popScale);
            const yShift = isWordActive && !isCompact ? -1.5 : 0;

            ctx.font = `800 ${wordFs}px '${fontFamily}', sans-serif`;
            const wWidth = ctx.measureText(tok.text).width;

            if (isWordActive && !isCompact && !settings.reducedMotion) {
              // 3D layered chromatic depth shadows behind popping word
              ctx.fillStyle = theme.secondaryAlpha(0.32);
              ctx.fillText(tok.text, cursorX + 3.2, yDraw + yShift + 3.2);
              ctx.fillStyle = theme.primaryAlpha(0.42);
              ctx.fillText(tok.text, cursorX + 1.6, yDraw + yShift + 1.6);
            }

            ctx.shadowColor = isWordActive ? theme.primaryAlpha(0.75) : 'rgba(255, 255, 255, 0.25)';
            ctx.shadowBlur = isWordActive ? 16 : 6;
            ctx.fillStyle = isWordSung ? '#FFFFFF' : 'rgba(255, 255, 255, 0.62)';
            ctx.fillText(tok.text, cursorX, yDraw + yShift);

            ctx.font = `800 ${baseFs}px '${fontFamily}', sans-serif`;
            cursorX += (tok.width || wWidth) + spaceW;
          }
        }
        ctx.restore();
      });
    }
  }
}
