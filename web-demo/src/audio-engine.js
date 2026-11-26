// Dual-mode Web Audio API soundtrack synthesizer + AnalyserNode + local audio uploader
export class StudioAudioEngine {
  constructor() {
    this.ctx = null;
    this.analyser = null;
    this.masterGain = null;
    this.freqData = null;
    this.synthEnabled = false;
    this.mode = 'synthetic'; // 'synthetic' | 'synth' | 'file'
    this.audioEl = null;
    this.mediaSrcNode = null;
    this.nextStepIndex = -1;

    this.subBassBaseline = 0.25;
    this.lufsShort = 0.35;
    this.dropCooldown = 0;
    this.climaxIntensity = 0;
    this.manualDropQueued = false;
  }

  ensureAudioContext() {
    if (!this.ctx) {
      const AC = window.AudioContext || window.webkitAudioContext;
      this.ctx = new AC();
      this.analyser = this.ctx.createAnalyser();
      this.analyser.fftSize = 256;
      this.analyser.smoothingTimeConstant = 0.72;
      this.masterGain = this.ctx.createGain();
      this.masterGain.gain.value = 0.42;
      this.masterGain.connect(this.analyser);
      this.analyser.connect(this.ctx.destination);
      this.freqData = new Uint8Array(this.analyser.frequencyBinCount);
    }
    if (this.ctx.state === 'suspended') {
      this.ctx.resume();
    }
  }

  toggleSynth(enable) {
    this.synthEnabled = enable;
    if (enable) {
      this.ensureAudioContext();
      this.mode = this.audioEl && !this.audioEl.paused ? 'file' : 'synth';
    } else if (this.mode === 'synth') {
      this.mode = 'synthetic';
    }
  }

  loadAudioFile(file) {
    this.ensureAudioContext();
    if (this.audioEl) {
      this.audioEl.pause();
      URL.revokeObjectURL(this.audioEl.src);
    }
    const url = URL.createObjectURL(file);
    this.audioEl = new Audio(url);
    this.audioEl.loop = true;
    this.audioEl.crossOrigin = 'anonymous';
    this.mediaSrcNode = this.ctx.createMediaElementSource(this.audioEl);
    this.mediaSrcNode.connect(this.masterGain);
    this.mode = 'file';
    this.synthEnabled = true;
    return this.audioEl;
  }

  triggerManualDrop() {
    this.manualDropQueued = true;
    this.climaxIntensity = 1.0;
    if (this.ctx && this.synthEnabled && this.mode === 'synth') {
      this.playSubDrop(this.ctx.currentTime + 0.01);
    }
  }

  stepSynthAudio(songTimeSec, isPlaying) {
    if (!isPlaying || !this.synthEnabled || this.mode !== 'synth' || !this.ctx) return;
    const stepDur = 60 / 110 / 2; // 110 BPM eighth-notes (~0.2727s)
    const step = Math.floor(songTimeSec / stepDur);
    if (step === this.nextStepIndex) return;
    this.nextStepIndex = step;

    const t = this.ctx.currentTime + 0.015;
    const barStep = step % 8;

    // Kick on 0, 4 (and double kick before drop)
    if (barStep === 0 || barStep === 4) {
      this.playKick(t);
    }
    // Snare/clap on 2, 6
    if (barStep === 2 || barStep === 6) {
      this.playSnare(t);
    }
    // Crisp closed hat every off-beat
    if (barStep % 2 === 1) {
      this.playHat(t);
    }

    // Bass + arp notes tuned to Neon Horizon A minor -> F -> C -> G
    const roots = [55.0, 43.65, 65.41, 49.0]; // A1, F1, C2, G1
    const chords = [
      [220, 261.63, 329.63],
      [174.61, 220, 261.63],
      [261.63, 329.63, 392.0],
      [196.0, 246.94, 293.66],
    ];
    const barIdx = Math.floor(step / 8) % roots.length;
    this.playBass(t, roots[barIdx]);
    const chord = chords[barIdx];
    this.playArp(t, chord[barStep % chord.length] * (barStep === 7 ? 2 : 1));

    // Choreographed bass drops matching Neon Horizon chorus moments
    const dropMoments = [5.9, 17.3, 25.7, 31.3];
    if (dropMoments.some((m) => Math.abs(songTimeSec - m) < stepDur * 0.65)) {
      this.playSubDrop(t);
    }
  }

  playKick(t) {
    const osc = this.ctx.createOscillator();
    const g = this.ctx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(145, t);
    osc.frequency.exponentialRampToValueAtTime(42, t + 0.14);
    g.gain.setValueAtTime(0.85, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.22);
    osc.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.23);
  }

  playSubDrop(t) {
    const osc = this.ctx.createOscillator();
    const g = this.ctx.createGain();
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(110, t);
    osc.frequency.exponentialRampToValueAtTime(34, t + 0.65);
    g.gain.setValueAtTime(0.95, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.72);
    osc.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.74);
  }

  playSnare(t) {
    const bufferSize = this.ctx.sampleRate * 0.11;
    const buffer = this.ctx.createBuffer(1, bufferSize, this.ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < bufferSize; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / bufferSize);
    const noise = this.ctx.createBufferSource();
    noise.buffer = buffer;
    const filter = this.ctx.createBiquadFilter();
    filter.type = 'bandpass';
    filter.frequency.value = 1800;
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(0.32, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.12);
    noise.connect(filter);
    filter.connect(g);
    g.connect(this.masterGain);
    noise.start(t);
  }

  playHat(t) {
    const bufferSize = Math.floor(this.ctx.sampleRate * 0.04);
    const buffer = this.ctx.createBuffer(1, bufferSize, this.ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < bufferSize; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / bufferSize);
    const src = this.ctx.createBufferSource();
    src.buffer = buffer;
    const filter = this.ctx.createBiquadFilter();
    filter.type = 'highpass';
    filter.frequency.value = 6500;
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(0.11, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.04);
    src.connect(filter);
    filter.connect(g);
    g.connect(this.masterGain);
    src.start(t);
  }

  playBass(t, freq) {
    const osc = this.ctx.createOscillator();
    const filter = this.ctx.createBiquadFilter();
    const g = this.ctx.createGain();
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(freq, t);
    filter.type = 'lowpass';
    filter.frequency.setValueAtTime(360, t);
    g.gain.setValueAtTime(0.24, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.24);
    osc.connect(filter);
    filter.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.25);
  }

  playArp(t, freq) {
    const osc = this.ctx.createOscillator();
    const g = this.ctx.createGain();
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(freq, t);
    g.gain.setValueAtTime(0.14, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.21);
    osc.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.22);
  }

  sampleMetrics(songTimeSec, dt, isPlaying, dropSensitivity = 1.0) {
    this.dropCooldown = Math.max(0, this.dropCooldown - dt);
    this.climaxIntensity = Math.max(0, this.climaxIntensity - dt * 0.55);

    let bass = 0.22;
    let mid = 0.25;
    let treble = 0.18;
    let beatPulse = 0.15;
    const bands = new Float32Array(32);

    if (this.analyser && this.freqData && isPlaying && this.synthEnabled) {
      this.analyser.getByteFrequencyData(this.freqData);
      const count = this.freqData.length;
      let sum = 0;
      for (let i = 0; i < 32; i++) {
        const idx = Math.min(count - 1, Math.floor((i / 32) * (count * 0.72)));
        const val = this.freqData[idx] / 255;
        bands[i] = val;
        sum += val;
      }
      if (sum > 0.35) {
        bass = (bands[0] + bands[1] + bands[2] + bands[3]) / 4;
        mid = (bands[6] + bands[8] + bands[10] + bands[12]) / 4;
        treble = (bands[18] + bands[22] + bands[26] + bands[30]) / 4;
        beatPulse = Math.min(1.2, bass * 1.15);
      } else {
        this._fillSyntheticBands(songTimeSec, bands);
        ({ bass, mid, treble, beatPulse } = this._syntheticEnergies(songTimeSec));
      }
    } else if (isPlaying) {
      this._fillSyntheticBands(songTimeSec, bands);
      ({ bass, mid, treble, beatPulse } = this._syntheticEnergies(songTimeSec));
    }

    // Mastered sub-bass ratio climax detection (mirrors LyricPulse AudioSpectrumAnalyzer)
    const subRatio = bass / Math.max(0.12, mid + 0.08);
    this.subBassBaseline = this.subBassBaseline * 0.96 + bass * 0.04;
    this.lufsShort = this.lufsShort * 0.92 + ((bass + mid + treble) / 3) * 0.08;

    let dropTrigger = 0;
    const dropThreshold = 1.48 / Math.max(0.5, dropSensitivity);
    const isSyntheticDrop =
      isPlaying &&
      !this.audioEl &&
      [5.9, 17.3, 25.7, 31.3].some((m) => songTimeSec >= m && songTimeSec < m + 0.09);

    if (this.manualDropQueued || isSyntheticDrop || (bass > 0.56 && subRatio > dropThreshold && this.dropCooldown <= 0)) {
      if (this.dropCooldown <= 0 || this.manualDropQueued) {
        dropTrigger = 1.0;
        this.dropCooldown = 0.85;
        this.climaxIntensity = 1.0;
      }
      this.manualDropQueued = false;
    }

    return {
      bass,
      mid,
      treble,
      beatPulse,
      bands,
      dropTrigger,
      climaxIntensity: this.climaxIntensity,
      isClimaxActive: this.climaxIntensity > 0.35,
    };
  }

  _syntheticEnergies(t) {
    const beatFrac = ((t * 110) / 60) % 1;
    const kick = Math.pow(Math.max(0, 1 - beatFrac * 2.4), 2.1);
    const wave = 0.5 + 0.5 * Math.sin(t * 1.9);
    const bass = Math.min(1, 0.28 + kick * 0.62 + wave * 0.12);
    const mid = Math.min(1, 0.32 + 0.25 * Math.sin(t * 3.1 + 1.0));
    const treble = Math.min(1, 0.24 + 0.28 * Math.abs(Math.sin(t * 5.2)));
    const beatPulse = Math.min(1.1, kick * 0.95 + 0.15);
    return { bass, mid, treble, beatPulse };
  }

  _fillSyntheticBands(t, bands) {
    const { bass, mid, treble } = this._syntheticEnergies(t);
    for (let i = 0; i < bands.length; i++) {
      const f = i / 31;
      const base = f < 0.25 ? bass : f < 0.65 ? mid : treble;
      const ripple = 0.5 + 0.5 * Math.sin(t * (3.2 + i * 0.35) + i * 0.6);
      bands[i] = Math.min(0.98, Math.max(0.06, base * (0.62 + 0.42 * ripple)));
    }
  }
}
