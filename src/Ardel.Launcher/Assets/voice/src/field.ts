import type { VoiceFieldApi, VoiceFieldHandle } from "./types";

function color(value: string | undefined, fallback: string): string {
  const s = String(value || "").trim();
  return s || fallback;
}

type Ring = { r: number; n: number; w: number; phase: number };

function mount(
  host: HTMLElement,
  options?: { ink?: string; accent?: string }
): VoiceFieldHandle {
  const opts = options || {};
  const canvas = document.createElement("canvas");
  host.appendChild(canvas);
  const ctx = canvas.getContext("2d");
  if (!ctx) return { setLive() {}, destroy() {} };

  let ink = color(opts.ink, "#8b949e");
  let accent = color(opts.accent, "#4db3a4");
  let live = false;
  let raf = 0;
  let alive = true;
  let lastPaint = 0;
  const t0 = performance.now();

  // Cap draw rate — idle home used to burn a full rAF loop in the background.
  const IDLE_MS = 1000 / 8;
  const LIVE_MS = 1000 / 20;

  const rings: Ring[] = [
    { r: 0.28, n: 16, w: 1.0, phase: 0.2 },
    { r: 0.46, n: 24, w: 1.3, phase: 0.55 },
    { r: 0.64, n: 32, w: 1.6, phase: 0.95 },
  ];

  const paint = (now: number) => {
    if (!alive) return;
    raf = requestAnimationFrame(paint);

    if (document.hidden) return;

    const interval = live ? LIVE_MS : IDLE_MS;
    if (now - lastPaint < interval) return;
    lastPaint = now;

    const elapsed = (now - t0) / 1000;
    const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
    const w = host.clientWidth || 180;
    const h = host.clientHeight || 180;
    const bw = Math.max(1, Math.round(w * dpr));
    const bh = Math.max(1, Math.round(h * dpr));
    if (canvas.width !== bw || canvas.height !== bh) {
      canvas.width = bw;
      canvas.height = bh;
    }

    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);

    const cx = w * 0.5;
    const cy = h * 0.5;
    const scale = Math.min(w, h) * 0.5;
    const pulse = live ? 1.08 : 1;
    const spin = live ? 0.7 : 0.28;

    ctx.strokeStyle = ink;
    ctx.globalAlpha = 0.2;
    ctx.lineWidth = 1;
    for (const ring of rings) {
      ctx.beginPath();
      ctx.arc(cx, cy, scale * ring.r * pulse, 0, Math.PI * 2);
      ctx.stroke();
    }

    for (let i = 0; i < rings.length; i++) {
      const ring = rings[i];
      const radius = scale * ring.r * pulse;
      const drift = elapsed * spin + ring.phase;
      const dir = i % 2 === 0 ? 1 : -1;
      for (let k = 0; k < ring.n; k++) {
        const a = (k / ring.n) * Math.PI * 2 + drift * dir;
        const bump =
          0.55 + 0.45 * Math.sin(elapsed * (1.5 + i * 0.3) + k * 0.5);
        const len = ring.w * (0.75 + bump) * (live ? 1.25 : 1);
        const hot = (Math.sin(elapsed * 2 + k * 0.35 + i) + 1) * 0.5;
        ctx.strokeStyle = hot > 0.7 ? accent : ink;
        ctx.globalAlpha = 0.28 + hot * 0.5;
        ctx.lineWidth = hot > 0.7 ? 2 : 1.15;
        ctx.beginPath();
        ctx.moveTo(
          cx + Math.cos(a) * (radius - len),
          cy + Math.sin(a) * (radius - len)
        );
        ctx.lineTo(
          cx + Math.cos(a) * (radius + len),
          cy + Math.sin(a) * (radius + len)
        );
        ctx.stroke();
      }
    }

    const hub =
      4.5 + (live ? 1.8 * Math.sin(elapsed * 4) : 0.8 * Math.sin(elapsed * 1.5));
    ctx.globalAlpha = 0.9;
    ctx.fillStyle = accent;
    ctx.beginPath();
    ctx.arc(cx, cy, Math.max(3, hub), 0, Math.PI * 2);
    ctx.fill();

    const sweep = (elapsed * (live ? 1.9 : 0.75)) % (Math.PI * 2);
    ctx.globalAlpha = 0.2;
    ctx.strokeStyle = accent;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(cx, cy, scale * 0.78 * pulse, sweep, sweep + 0.5);
    ctx.stroke();
    ctx.globalAlpha = 1;
  };

  const onVisibility = () => {
    if (!alive) return;
    if (!document.hidden && !raf) raf = requestAnimationFrame(paint);
  };
  document.addEventListener("visibilitychange", onVisibility);

  raf = requestAnimationFrame(paint);

  return {
    setLive(v: boolean) {
      live = !!v;
    },
    destroy() {
      alive = false;
      cancelAnimationFrame(raf);
      raf = 0;
      document.removeEventListener("visibilitychange", onVisibility);
      canvas.remove();
    },
  };
}

const api: VoiceFieldApi = { mount };
window.VoiceField = api;
