/** Mic capture constraints, VAD gate, and mute cue tones. */

/** Prefer Chromium voiceIsolation + classic AEC/NS for speakerphone rooms. */
export function micAudioConstraints(): MediaTrackConstraints {
  return {
    channelCount: 1,
    echoCancellation: { ideal: true },
    noiseSuppression: { ideal: true },
    autoGainControl: { ideal: true },
    // Chromium 120+ — isolates speech from keyboard / room noise.
    // Cast: not yet in every lib.dom MediaTrackConstraints typing.
    ...({ voiceIsolation: { ideal: true } } as MediaTrackConstraints),
  };
}

export type VadGateHandle = {
  setUserMuted(muted: boolean): void;
  /** Apply enabled = !userMuted && (speaking || hangover). */
  refresh(): void;
  stop(): void;
};

/**
 * Energy VAD: keep the outbound track open while speaking (+ hangover),
 * otherwise send silence so bottom noise is not blasted to peers.
 */
export function startVadGate(stream: MediaStream): VadGateHandle {
  const track = stream.getAudioTracks()[0] ?? null;
  let userMuted = false;
  let speaking = false;
  let hangoverUntil = 0;
  let timer = 0;
  let ctx: AudioContext | null = null;
  let source: MediaStreamAudioSourceNode | null = null;
  let analyser: AnalyserNode | null = null;
  let probe: MediaStream | null = null;

  const SPEECH_RMS = 0.018;
  const SILENCE_RMS = 0.010;
  const HANGOVER_MS = 380;
  const POLL_MS = 50;

  const apply = () => {
    if (!track) return;
    const open =
      !userMuted && (speaking || performance.now() < hangoverUntil);
    if (track.enabled !== open) track.enabled = open;
  };

  try {
    const AC =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext?: typeof AudioContext })
        .webkitAudioContext;
    if (AC && track) {
      ctx = new AC();
      // Clone so the analyser is not a second consumer fighting WebRTC send.
      probe = stream.clone();
      for (const t of probe.getAudioTracks()) t.enabled = true;
      source = ctx.createMediaStreamSource(probe);
      analyser = ctx.createAnalyser();
      analyser.fftSize = 512;
      analyser.smoothingTimeConstant = 0.55;
      source.connect(analyser);
      // Do not connect to destination — meter only.

      const buf = new Float32Array(analyser.fftSize);
      timer = window.setInterval(() => {
        if (!analyser || userMuted) {
          speaking = false;
          apply();
          return;
        }
        if (document.hidden) {
          // Background: keep last hangover, skip FFT work.
          apply();
          return;
        }
        analyser.getFloatTimeDomainData(buf);
        let sum = 0;
        for (let i = 0; i < buf.length; i++) {
          const v = buf[i];
          sum += v * v;
        }
        const rms = Math.sqrt(sum / buf.length);
        if (rms >= SPEECH_RMS) {
          speaking = true;
          hangoverUntil = performance.now() + HANGOVER_MS;
        } else if (rms <= SILENCE_RMS) {
          speaking = false;
        }
        apply();
      }, POLL_MS);
    }
  } catch {
    // VAD optional — constraints still apply.
  }

  apply();

  return {
    setUserMuted(muted: boolean) {
      userMuted = muted;
      if (muted) {
        speaking = false;
        hangoverUntil = 0;
      }
      apply();
    },
    refresh: apply,
    stop() {
      window.clearInterval(timer);
      timer = 0;
      try {
        source?.disconnect();
      } catch {
        /* ignore */
      }
      try {
        void ctx?.close();
      } catch {
        /* ignore */
      }
      if (probe) {
        for (const t of probe.getTracks()) t.stop();
        probe = null;
      }
      source = null;
      analyser = null;
      ctx = null;
      if (track && !userMuted) track.enabled = true;
    },
  };
}

let cueCtx: AudioContext | null = null;

function ensureCueCtx(): AudioContext | null {
  try {
    if (cueCtx && cueCtx.state !== "closed") return cueCtx;
    const AC =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext?: typeof AudioContext })
        .webkitAudioContext;
    if (!AC) return null;
    cueCtx = new AC();
    return cueCtx;
  } catch {
    return null;
  }
}

/** Soft two-tone cue: mute descends, unmute ascends. Local only. */
export async function playMuteCue(muted: boolean): Promise<void> {
  const ctx = ensureCueCtx();
  if (!ctx) return;
  try {
    if (ctx.state === "suspended") await ctx.resume();
  } catch {
    return;
  }

  const now = ctx.currentTime;
  const freqs = muted ? [660, 440] : [520, 780];
  const gain = ctx.createGain();
  gain.gain.setValueAtTime(0.0001, now);
  gain.gain.exponentialRampToValueAtTime(0.07, now + 0.02);
  gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.22);
  gain.connect(ctx.destination);

  for (let i = 0; i < freqs.length; i++) {
    const osc = ctx.createOscillator();
    osc.type = "sine";
    osc.frequency.value = freqs[i];
    const g = ctx.createGain();
    const t0 = now + i * 0.09;
    g.gain.setValueAtTime(0.0001, t0);
    g.gain.exponentialRampToValueAtTime(0.9, t0 + 0.015);
    g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.1);
    osc.connect(g);
    g.connect(gain);
    osc.start(t0);
    osc.stop(t0 + 0.12);
  }
}
