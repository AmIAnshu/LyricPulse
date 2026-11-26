// Web Audio API Synthesizer + File Upload Audio Analyser (32-band FFT + bass/mid/treble energy)
export class StudioAudioEngine {
  constructor() {
    this.ctx = null;
    this.analyser = null;
    this.masterGain = null;
    this.freqData = null;
    this.mode = 'synthetic'; // 'synthetic' | 'synth' | 'file'
    this.synthEnabled = false;
    this.audioEl = null;
    this.mediaSrcNode = null;
    this.nextStepIndex = -1;
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

  stepSynthAudio(songTimeSec, isPlaying) {
    if (!isPlaying || !this.synthEnabled || this.mode !== 'synth' || !this.ctx) return;
    const stepDur = 60 / 110 / 2; // 110 BPM eighth-notes (~0.2727s)
    const step = Math.floor(songTimeSec / stepDur);
    if (step === this.nextStepIndex) return;
    this.nextStepIndex = step;

    const t = this.ctx.currentTime + 0.015;
    const barStep = step % 8;

    if (barStep === 0 || barStep === 4) {
      this._playKick(t, 1.0);
    } else if (barStep === 7 && (Math.floor(step / 8) % 2 === 1)) {
      this._playKick(t, 0.72);
    }

    if (barStep === 2 || barStep === 6) {
      this._playSnare(t);
    }
    this._playHat(t, barStep % 2 === 1 ? 0.14 : 0.08);

    const bassNotes = [55.0, 55.0, 65.41, 73.42, 48.99, 48.99, 61.74, 65.41];
    const barIdx = Math.floor(step / 8) % bassNotes.length;
    this._playBass(t, bassNotes[barIdx]);

    const arpNotes = [220, 261.63, 329.63, 392, 440, 392, 329.63, 293.66];
    this._playArp(t, arpNotes[(step + barIdx) % arpNotes.length]);
  }

  _playKick(t, gainAmt) {
    const osc = this.ctx.createOscillator();
    const g = this.ctx.createGain();
    osc.type = 'sine';
    osc.frequency.setValueAtTime(145, t);
    osc.frequency.exponentialRampToValueAtTime(38, t + 0.16);
    g.gain.setValueAtTime(0.65 * gainAmt, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.28);
    osc.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.30);
  }

  _playSnare(t) {
    const len = this.ctx.sampleRate * 0.11;
    const buf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const data = buf.getChannelData(0);
    for (let i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / len);
    const noise = this.ctx.createBufferSource();
    noise.buffer = buf;
    const filter = this.ctx.createBiquadFilter();
    filter.type = 'highpass';
    filter.frequency.value = 1100;
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(0.24, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.11);
    noise.connect(filter);
    filter.connect(g);
    g.connect(this.masterGain);
    noise.start(t);
  }

  _playHat(t, vol) {
    const len = this.ctx.sampleRate * 0.04;
    const buf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const data = buf.getChannelData(0);
    for (let i = 0; i < len; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / len);
    const src = this.ctx.createBufferSource();
    src.buffer = buf;
    const hp = this.ctx.createBiquadFilter();
    hp.type = 'highpass';
    hp.frequency.value = 6500;
    const g = this.ctx.createGain();
    g.gain.setValueAtTime(vol, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.04);
    src.connect(hp);
    hp.connect(g);
    g.connect(this.masterGain);
    src.start(t);
  }

  _playBass(t, freq) {
    const osc = this.ctx.createOscillator();
    const filt = this.ctx.createBiquadFilter();
    const g = this.ctx.createGain();
    osc.type = 'sawtooth';
    osc.frequency.setValueAtTime(freq, t);
    filt.type = 'lowpass';
    filt.frequency.setValueAtTime(360, t);
    filt.frequency.exponentialRampToValueAtTime(140, t + 0.22);
    g.gain.setValueAtTime(0.22, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.24);
    osc.connect(filt);
    filt.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.25);
  }

  _playArp(t, freq) {
    const osc = this.ctx.createOscillator();
    const g = this.ctx.createGain();
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(freq, t);
    g.gain.setValueAtTime(0.11, t);
    g.gain.exponentialRampToValueAtTime(0.001, t + 0.20);
    osc.connect(g);
    g.connect(this.masterGain);
    osc.start(t);
    osc.stop(t + 0.22);
  }

  sampleMetrics(songTimeSec, dt, isPlaying) {
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

    return { bass, mid, treble, beatPulse, bands };
  }

  _syntheticEnergies(t) {
    const beatFrac = ((t * 110) / 60) % 1;
    const kick = Math.pow(Math.max(0, 1 - beatFrac * 2.4), 2.1);
    const wave = 0.5 + 0.5 * Math.sin(t * 1.9);
    const bass = Math.min(1, 0.28 + kick * 0.62 + wave * 0.12);
    const mid = Math.min(1, 0.32 + 0.25 * Math.sin(t * 3.1 + 1.0));
    const treble = Math.min(1, 0.26 + 0.24 * Math.cos(t * 5.2));
    const beatPulse = Math.min(1.15, kick * 0.92 + wave * 0.14);
    return { bass, mid, treble, beatPulse };
  }

  _fillSyntheticBands(t, bands) {
    const beatFrac = ((t * 110) / 60) % 1;
    const kick = Math.pow(Math.max(0, 1 - beatFrac * 2.3), 2);
    for (let i = 0; i < 32; i++) {
      const f = i / 31;
      const bassLift = f < 0.25 ? kick * (1 - f * 3) * 0.72 : 0;
      const wave = 0.22 + 0.30 * Math.abs(Math.sin(t * (2.4 + f * 4.5) + i * 0.42));
      bands[i] = Math.min(1, wave + bassLift);
    }
  }
}
