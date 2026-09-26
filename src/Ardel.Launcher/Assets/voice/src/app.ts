import type { VoiceBoot } from "./types";
import {
  micAudioConstraints,
  playMuteCue,
  startVadGate,
  type VadGateHandle,
} from "./audio";
import { DecentralizedSignalingChannel } from "./signaling";

(() => {
  const boot: VoiceBoot = window.__ARDEL_VOICE__ || {};
  const params = new URLSearchParams(location.search);
  const wsUrl = boot.ws || params.get("ws") || "ws://127.0.0.1:17865/voice";
  const theme = boot.theme || params.get("theme") || "theme-dark";

  // Comprehensive global and domestic STUN server pool for high-success NAT traversal
  const iceServers: RTCIceServer[] = [
    { urls: "stun:stun.l.google.com:19302" },
    { urls: "stun:stun1.l.google.com:19302" },
    { urls: "stun:stun2.l.google.com:19302" },
    { urls: "stun:stun.cloudflare.com:3478" },
    { urls: "stun:stun.qq.com:3478" },
    { urls: "stun:stun.miwifi.com:3478" },
    { urls: "stun:stun.syncthing.net:3478" },
  ];

  let i18n: Record<string, string> = {};
  if (boot.i18n && typeof boot.i18n === "object") {
    i18n = boot.i18n;
  } else {
    try {
      i18n = JSON.parse(params.get("i18n") || "{}") as Record<string, string>;
    } catch {
      i18n = {};
    }
  }

  function t(key: string, fallback: string): string {
    const v = i18n[key];
    return typeof v === "string" && v.length ? v : fallback || key;
  }

  function applyI18n(): void {
    document.querySelectorAll("[data-i18n]").forEach((node) => {
      const key = node.getAttribute("data-i18n");
      if (key) node.textContent = t(key, node.textContent || "");
    });
    document.querySelectorAll("[data-i18n-placeholder]").forEach((node) => {
      const key = node.getAttribute("data-i18n-placeholder");
      if (key) {
        node.setAttribute(
          "placeholder",
          t(key, node.getAttribute("placeholder") || "")
        );
      }
    });
  }

  document.body.classList.remove("theme-dark", "theme-light");
  document.body.classList.add(theme === "theme-light" ? "theme-light" : "theme-dark");
  document.title = t("title", "Ardel Voice");
  applyI18n();

  const ui = {
    home: document.getElementById("stepHome") as HTMLElement,
    join: document.getElementById("stepJoin") as HTMLElement,
    live: document.getElementById("stepLive") as HTMLElement,
    displayName: document.getElementById("displayName") as HTMLInputElement,
    sessionCode: document.getElementById("sessionCode") as HTMLInputElement,
    joinHint: document.getElementById("joinHint") as HTMLElement,
    codeLabel: document.getElementById("codeLabel") as HTMLElement,
    status: document.getElementById("status") as HTMLElement,
    members: document.getElementById("members") as HTMLElement,
    audioSink: document.getElementById("audioSink") as HTMLElement,
    fieldHost: document.getElementById("fieldHost") as HTMLElement,
    btnCreate: document.getElementById("btnCreate") as HTMLButtonElement,
    btnGoJoin: document.getElementById("btnGoJoin") as HTMLButtonElement,
    btnBackHome: document.getElementById("btnBackHome") as HTMLButtonElement,
    btnJoin: document.getElementById("btnJoin") as HTMLButtonElement,
    btnMute: document.getElementById("btnMute") as HTMLButtonElement,
    btnLeave: document.getElementById("btnLeave") as HTMLButtonElement,
    btnCopy: document.getElementById("btnCopy") as HTMLButtonElement,
  };

  const bootName = typeof boot.name === "string" ? boot.name : "";
  ui.displayName.value =
    bootName || decodeURIComponent(params.get("name") || t("guest", "Guest"));

  const themeStyles = getComputedStyle(document.body);
  const field = window.VoiceField
    ? window.VoiceField.mount(ui.fieldHost, {
        ink: themeStyles.getPropertyValue("--field-ink").trim(),
        accent: themeStyles.getPropertyValue("--field-acc").trim(),
      })
    : null;

  type StepName = "home" | "join" | "live";

  let signaling: DecentralizedSignalingChannel | null = null;
  let selfId: string | null = null;
  let sessionCode: string | null = null;
  let localStream: MediaStream | null = null;
  let muted = false;
  let vad: VadGateHandle | null = null;
  let currentStep: StepName = "home";
  let stepBusy = false;
  let connectedTimer = 0;
  const stepOrder: Record<StepName, number> = { home: 0, join: 1, live: 2 };
  const pcs = new Map<string, RTCPeerConnection>();
  const names = new Map<string, string>();
  const connectedBanner = document.getElementById("connectedBanner");

  function displayName(): string {
    return ui.displayName.value.trim() || t("guest", "Guest");
  }

  function flashConnected(): void {
    if (!connectedBanner) return;
    connectedBanner.classList.remove("is-out");
    connectedBanner.classList.add("is-on");
    connectedBanner.setAttribute("aria-hidden", "false");
    window.clearTimeout(connectedTimer);
    connectedTimer = window.setTimeout(() => {
      connectedBanner.classList.remove("is-on");
      connectedBanner.classList.add("is-out");
      window.setTimeout(() => {
        connectedBanner.classList.remove("is-out");
        connectedBanner.setAttribute("aria-hidden", "true");
      }, 400);
    }, 2000);
  }

  function stepEl(name: StepName): HTMLElement {
    if (name === "home") return ui.home;
    if (name === "join") return ui.join;
    return ui.live;
  }

  function notifyHostLayout(step: StepName): void {
    try {
      const host = (window as unknown as {
        chrome?: { webview?: { postMessage: (msg: unknown) => void } };
      }).chrome?.webview;
      if (!host) return;

      const payload: {
        type: string;
        step: StepName;
        members?: number;
        height?: number;
      } = { type: "layout", step };

      if (step === "join" || step === "live") {
        const panel = step === "live" ? ui.live : ui.join;
        if (step === "live") {
          const count = ui.members.querySelectorAll("li").length;
          payload.members = Math.max(1, count);
        }
        const card = panel.querySelector(".form-card") as HTMLElement | null;
        if (card) {
          const styles = getComputedStyle(panel);
          const padY =
            (parseFloat(styles.paddingTop) || 0) +
            (parseFloat(styles.paddingBottom) || 0);
          payload.height = Math.ceil(
            card.getBoundingClientRect().height + padY + 120
          );
        }
      }

      host.postMessage(payload);
    } catch {
      /* not hosted in WebView2 */
    }
  }

  function scheduleHostLayout(step: StepName): void {
    window.requestAnimationFrame(() => notifyHostLayout(step));
  }

  function showStep(name: StepName): void {
    if (name === currentStep || stepBusy) {
      if (name === currentStep) {
        document.body.classList.toggle("view-home", name === "home");
        document.body.classList.toggle("view-form", name !== "home");
        field?.setLive(name === "live");
        scheduleHostLayout(name);
      }
      return;
    }

    const from = stepEl(currentStep);
    const to = stepEl(name);
    const forward = stepOrder[name] >= stepOrder[currentStep];
    stepBusy = true;

    from.classList.remove("is-active", "from-left", "from-right");
    from.classList.add("is-leaving", forward ? "to-left" : "to-right");
    from.setAttribute("aria-hidden", "true");

    to.classList.remove("is-leaving", "to-left", "to-right");
    to.classList.add(forward ? "from-right" : "from-left");
    void to.offsetWidth;
    to.classList.add("is-active");
    to.classList.remove("from-left", "from-right");
    to.setAttribute("aria-hidden", "false");

    currentStep = name;
    document.body.classList.toggle("view-home", name === "home");
    document.body.classList.toggle("view-form", name !== "home");
    field?.setLive(name === "live");
    scheduleHostLayout(name);

    window.setTimeout(() => {
      from.classList.remove("is-leaving", "to-left", "to-right");
      stepBusy = false;
      if (name === "join") ui.sessionCode.focus();
      if (name === "live") scheduleHostLayout("live");
    }, 320);
  }

  function setStatus(text: string): void {
    ui.status.textContent = text;
  }

  function setJoinHint(text: string): void {
    ui.joinHint.textContent = text || "";
  }

  function mapError(code: string): string {
    if (code === "not_found") return t("errNotFound", "Session not found");
    if (code === "full") return t("errFull", "Session is full");
    return t("errGeneric", "Something went wrong");
  }

  function renderMembers(): void {
    ui.members.innerHTML = "";
    const rows: { id: string | null; name: string }[] = [
      { id: selfId, name: `${displayName()} ${t("youSuffix", "(you)")}` },
    ];
    for (const [id, name] of names) {
      if (id !== selfId) rows.push({ id, name });
    }
    for (const row of rows) {
      if (!row.id) continue;
      const li = document.createElement("li");
      li.textContent = row.name;
      ui.members.appendChild(li);
    }
    if (currentStep === "live") scheduleHostLayout("live");
  }

  async function ensureMic(): Promise<MediaStream> {
    if (localStream) return localStream;
    localStream = await navigator.mediaDevices.getUserMedia({
      audio: micAudioConstraints(),
      video: false,
    });

    for (const track of localStream.getAudioTracks()) {
      try {
        await track.applyConstraints(micAudioConstraints());
      } catch {
        /* constraint subset unsupported */
      }
    }

    vad?.stop();
    vad = startVadGate(localStream);
    vad.setUserMuted(muted);
    return localStream;
  }

  function stopMicPipeline(): void {
    vad?.stop();
    vad = null;
    if (localStream) {
      for (const track of localStream.getTracks()) track.stop();
      localStream = null;
    }
  }

  function closePeer(id: string): void {
    const pc = pcs.get(id);
    if (!pc) return;
    try {
      pc.close();
    } catch {
      /* ignore */
    }
    pcs.delete(id);
    document.getElementById(`audio-${id}`)?.remove();
  }

  function closeAllPeers(): void {
    for (const id of [...pcs.keys()]) closePeer(id);
    names.clear();
  }

  async function createPeer(remoteId: string): Promise<void> {
    if (pcs.has(remoteId) || remoteId === selfId) return;

    const pc = new RTCPeerConnection({ iceServers });
    pcs.set(remoteId, pc);

    const stream = await ensureMic();
    for (const track of stream.getTracks()) pc.addTrack(track, stream);

    pc.onicecandidate = (ev) => {
      if (!ev.candidate || !signaling) return;
      signaling.sendSignal(remoteId, { kind: "ice", candidate: ev.candidate });
    };

    pc.ontrack = (ev) => {
      let audio = document.getElementById(`audio-${remoteId}`) as HTMLAudioElement | null;
      if (!audio) {
        audio = document.createElement("audio");
        audio.id = `audio-${remoteId}`;
        audio.autoplay = true;
        audio.setAttribute("playsinline", "true");
        audio.volume = 1;
        ui.audioSink.appendChild(audio);
      }
      audio.srcObject = ev.streams[0] ?? null;
      void audio.play().catch(() => {
        /* autoplay policy */
      });
    };

    pc.onconnectionstatechange = () => {
      if (pc.connectionState === "failed" || pc.connectionState === "closed") {
        closePeer(remoteId);
      }
    };

    if (selfId != null && selfId > remoteId) {
      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      signaling?.sendSignal(remoteId, { kind: "sdp", description: pc.localDescription });
    }
  }

  async function syncPeers(
    peerList: { id: string; name?: string }[]
  ): Promise<void> {
    const remoteIds = new Set<string>();
    names.clear();
    for (const p of peerList) {
      names.set(p.id, p.name || t("guest", "Guest"));
      if (p.id !== selfId) remoteIds.add(p.id);
    }
    for (const id of [...pcs.keys()]) {
      if (!remoteIds.has(id)) closePeer(id);
    }
    for (const id of remoteIds) await createPeer(id);
    renderMembers();
  }

  async function onSignal(
    from: string,
    payload: { kind?: string; description?: RTCSessionDescriptionInit; candidate?: RTCIceCandidateInit }
  ): Promise<void> {
    if (!payload || !from) return;
    let pc = pcs.get(from);
    if (!pc) {
      await createPeer(from);
      pc = pcs.get(from);
    }
    if (!pc) return;

    if (payload.kind === "sdp" && payload.description) {
      const desc = payload.description;
      await pc.setRemoteDescription(desc);
      if (desc.type === "offer") {
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        signaling?.sendSignal(from, { kind: "sdp", description: pc.localDescription });
      }
    } else if (payload.kind === "ice" && payload.candidate) {
      try {
        await pc.addIceCandidate(payload.candidate);
      } catch {
        /* ignore */
      }
    }
  }

  function initSignaling(): DecentralizedSignalingChannel {
    const chan = new DecentralizedSignalingChannel({
      onWelcome: (id) => {
        selfId = id;
      },
      onCreated: (code, id) => {
        sessionCode = code;
        selfId = id;
        ui.codeLabel.textContent = sessionCode || "";
        showStep("live");
        setStatus(t("statusInSession", "In session"));
      },
      onJoined: (code, id) => {
        sessionCode = code;
        selfId = id;
        ui.codeLabel.textContent = sessionCode || "";
        showStep("live");
        setStatus(t("statusInSession", "In session"));
        flashConnected();
      },
      onPeers: async (peers) => {
        await syncPeers(peers);
      },
      onSignal: async (from, payload) => {
        await onSignal(from, payload);
      },
      onError: (err) => {
        const msg = mapError(err);
        if (currentStep === "join") setJoinHint(msg);
        else setStatus(msg);
      },
      onDisconnect: () => {
        if (currentStep === "live") {
          setStatus(t("statusDisconnected", "Disconnected"));
        }
      },
    });
    chan.setDisplayName(displayName());
    return chan;
  }

  async function startSession(create: boolean): Promise<void> {
    if (create) scheduleHostLayout("live");
    try {
      setJoinHint("");
      setStatus(t("statusMic", "Requesting microphone…"));
      await ensureMic();

      if (!signaling) {
        signaling = initSignaling();
      }
      signaling.setDisplayName(displayName());

      const code = create ? undefined : (ui.sessionCode.value || "").trim().toUpperCase();
      if (!create && !code) {
        setJoinHint(t("statusNeedCode", "Enter an invite code"));
        return;
      }

      setStatus(t("statusSignaling", "Connecting…"));
      await signaling.start(create, code, wsUrl);
    } catch (err) {
      const msg =
        err instanceof Error && err.message
          ? err.message
          : t("errGeneric", "Something went wrong");
      if (currentStep === "join") setJoinHint(msg);
      else setStatus(msg);
      if (create && currentStep === "home") scheduleHostLayout("home");
    }
  }

  function leave(): void {
    if (signaling) {
      signaling.leave();
      signaling = null;
    }
    closeAllPeers();
    stopMicPipeline();
    sessionCode = null;
    ui.members.innerHTML = "";
    muted = false;
    ui.btnMute.textContent = t("mute", "Mute mic");
    setJoinHint("");
    showStep("home");
  }

  ui.btnCreate.addEventListener("click", () => void startSession(true));
  ui.btnGoJoin.addEventListener("click", () => {
    setJoinHint("");
    showStep("join");
  });
  ui.btnBackHome.addEventListener("click", () => showStep("home"));
  ui.btnJoin.addEventListener("click", () => void startSession(false));
  ui.btnLeave.addEventListener("click", leave);
  ui.btnMute.addEventListener("click", () => {
    if (!localStream) return;
    muted = !muted;
    vad?.setUserMuted(muted);
    if (!vad) {
      for (const track of localStream.getAudioTracks()) track.enabled = !muted;
    }
    ui.btnMute.textContent = muted
      ? t("unmute", "Unmute mic")
      : t("mute", "Mute mic");
    void playMuteCue(muted);
  });
  ui.btnCopy.addEventListener("click", async () => {
    if (!sessionCode) return;
    try {
      await navigator.clipboard.writeText(sessionCode);
      setStatus(t("statusCopied", "Invite code copied"));
    } catch {
      setStatus(t("statusCopyFail", "Could not copy"));
    }
  });

  document.body.classList.add("view-home");
  document.body.classList.remove("view-form");
})();
