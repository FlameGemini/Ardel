import Paho from "paho-mqtt";

export interface SignalingMessage {
  type: string;
  peerId?: string;
  code?: string;
  name?: string;
  peers?: { id: string; name?: string }[];
  from?: string;
  to?: string;
  payload?: {
    kind?: string;
    description?: RTCSessionDescriptionInit;
    candidate?: RTCIceCandidateInit;
  };
  message?: string;
}

export interface SignalingCallbacks {
  onWelcome: (peerId: string) => void;
  onCreated: (code: string, peerId: string) => void;
  onJoined: (code: string, peerId: string) => void;
  onPeers: (peers: { id: string; name?: string }[]) => void;
  onSignal: (from: string, payload: any) => void;
  onError: (errMsg: string) => void;
  onDisconnect: () => void;
}

// Public zero-cost high-availability MQTT brokers with TLS WebSocket support
const MQTT_BROKERS = [
  { host: "broker.emqx.io", port: 8084, path: "/mqtt", useSSL: true },
  { host: "broker.hivemq.com", port: 8884, path: "/mqtt", useSSL: true },
  { host: "test.mosquitto.org", port: 8081, path: "/mqtt", useSSL: true },
];

function randomCode(len = 6): string {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let res = "";
  for (let i = 0; i < len; i++) {
    res += chars[Math.floor(Math.random() * chars.length)];
  }
  return res;
}

function randomId(): string {
  return Array.from(crypto.getRandomValues(new Uint8Array(8)))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

export class DecentralizedSignalingChannel {
  private client: Paho.Client | null = null;
  private localWs: WebSocket | null = null;
  private selfId: string = randomId();
  private displayName: string = "Guest";
  private sessionCode: string | null = null;
  private members = new Map<string, string>();
  private useLocalFallback = false;
  private callbacks: SignalingCallbacks;

  constructor(callbacks: SignalingCallbacks) {
    this.callbacks = callbacks;
  }

  public setDisplayName(name: string): void {
    this.displayName = name || "Guest";
  }

  public getSelfId(): string {
    return this.selfId;
  }

  public async start(create: boolean, code?: string, localWsUrl?: string): Promise<void> {
    this.selfId = randomId();
    this.members.clear();
    this.members.set(this.selfId, this.displayName);

    if (create) {
      this.sessionCode = randomCode(6);
    } else {
      this.sessionCode = (code || "").trim().toUpperCase();
      if (!this.sessionCode) {
        throw new Error("Missing session code");
      }
    }

    try {
      await this.connectMqtt();
    } catch (mqttErr) {
      console.warn("[Voice] Public MQTT signaling failed, trying local fallback:", mqttErr);
      if (localWsUrl) {
        await this.connectLocalWs(localWsUrl, create);
        return;
      }
      throw mqttErr;
    }

    if (create) {
      this.callbacks.onCreated(this.sessionCode, this.selfId);
    } else {
      this.callbacks.onJoined(this.sessionCode, this.selfId);
      this.broadcastPresence("join");
    }
  }

  private connectMqtt(): Promise<void> {
    return new Promise((resolve, reject) => {
      let connected = false;
      let brokerIdx = 0;

      const tryNextBroker = () => {
        if (brokerIdx >= MQTT_BROKERS.length) {
          reject(new Error("All public signaling brokers unreachable"));
          return;
        }

        const broker = MQTT_BROKERS[brokerIdx++];
        const clientId = `ardel_voice_${this.selfId}_${Math.floor(Math.random() * 10000)}`;

        try {
          const client = new Paho.Client(broker.host, broker.port, broker.path, clientId);
          this.client = client;

          client.onConnectionLost = (resp) => {
            console.warn("[Voice] MQTT connection lost:", resp.errorMessage);
            if (connected) {
              this.callbacks.onDisconnect();
            }
          };

          client.onMessageArrived = (msg) => {
            this.handleMqttMessage(msg.destinationName, msg.payloadString);
          };

          client.connect({
            useSSL: broker.useSSL,
            timeout: 5,
            keepAliveInterval: 30,
            cleanSession: true,
            onSuccess: () => {
              connected = true;
              this.subscribeTopics();
              resolve();
            },
            onFailure: (err) => {
              console.warn(`[Voice] MQTT broker ${broker.host} connect failed:`, err.errorMessage);
              tryNextBroker();
            },
          });
        } catch (e) {
          tryNextBroker();
        }
      };

      tryNextBroker();
    });
  }

  private subscribeTopics(): void {
    if (!this.client || !this.sessionCode) return;
    const topicPresence = `ardel/v1/voice/${this.sessionCode}/presence`;
    const topicSignal = `ardel/v1/voice/${this.sessionCode}/signal`;
    this.client.subscribe(topicPresence, { qos: 1 });
    this.client.subscribe(topicSignal, { qos: 1 });
  }

  private handleMqttMessage(topic: string, payloadStr: string): void {
    let data: SignalingMessage;
    try {
      data = JSON.parse(payloadStr);
    } catch {
      return;
    }

    if (data.from === this.selfId) {
      return; // Ignore own broadcast messages
    }

    if (topic.endsWith("/presence")) {
      if (data.type === "join") {
        if (data.peerId && data.peerId !== this.selfId) {
          this.members.set(data.peerId, data.name || "Guest");
          // Reply with our presence so the newcomer knows about us
          this.broadcastPresence("presence");
          this.emitPeers();
        }
      } else if (data.type === "presence") {
        if (data.peerId && data.peerId !== this.selfId) {
          this.members.set(data.peerId, data.name || "Guest");
          this.emitPeers();
        }
      } else if (data.type === "leave") {
        if (data.peerId) {
          this.members.delete(data.peerId);
          this.emitPeers();
        }
      }
    } else if (topic.endsWith("/signal")) {
      if (data.to === this.selfId && data.from && data.payload) {
        this.callbacks.onSignal(data.from, data.payload);
      }
    }
  }

  private broadcastPresence(type: "join" | "presence" | "leave"): void {
    if (!this.client || !this.sessionCode) return;
    const topic = `ardel/v1/voice/${this.sessionCode}/presence`;
    const msg = new Paho.Message(
      JSON.stringify({
        type,
        from: this.selfId,
        peerId: this.selfId,
        name: this.displayName,
      })
    );
    msg.destinationName = topic;
    msg.qos = 1;
    this.client.send(msg);
  }

  private emitPeers(): void {
    const list = Array.from(this.members.entries()).map(([id, name]) => ({ id, name }));
    this.callbacks.onPeers(list);
  }

  public sendSignal(toPeerId: string, payload: any): void {
    if (this.useLocalFallback && this.localWs && this.localWs.readyState === WebSocket.OPEN) {
      this.localWs.send(
        JSON.stringify({
          type: "signal",
          to: toPeerId,
          payload,
        })
      );
      return;
    }

    if (!this.client || !this.sessionCode) return;
    const topic = `ardel/v1/voice/${this.sessionCode}/signal`;
    const msg = new Paho.Message(
      JSON.stringify({
        type: "signal",
        from: this.selfId,
        to: toPeerId,
        payload,
      })
    );
    msg.destinationName = topic;
    msg.qos = 1;
    this.client.send(msg);
  }

  private connectLocalWs(wsUrl: string, create: boolean): Promise<void> {
    return new Promise((resolve, reject) => {
      this.useLocalFallback = true;
      const ws = new WebSocket(wsUrl);
      this.localWs = ws;
      ws.onopen = () => {
        ws.send(JSON.stringify({ type: "hello", displayName: this.displayName }));
        if (create) {
          ws.send(JSON.stringify({ type: "create" }));
        } else {
          ws.send(JSON.stringify({ type: "join", code: this.sessionCode }));
        }
        resolve();
      };
      ws.onerror = () => reject(new Error("Local signaling failed"));
      ws.onclose = () => this.callbacks.onDisconnect();
      ws.onmessage = (ev) => {
        let msg: SignalingMessage;
        try {
          msg = JSON.parse(String(ev.data));
        } catch {
          return;
        }
        switch (msg.type) {
          case "welcome":
            this.selfId = msg.peerId || this.selfId;
            break;
          case "created":
            this.sessionCode = msg.code || this.sessionCode;
            this.selfId = msg.peerId || this.selfId;
            this.callbacks.onCreated(this.sessionCode!, this.selfId);
            break;
          case "joined":
            this.sessionCode = msg.code || this.sessionCode;
            this.selfId = msg.peerId || this.selfId;
            this.callbacks.onJoined(this.sessionCode!, this.selfId);
            break;
          case "peers":
            this.callbacks.onPeers(msg.peers || []);
            break;
          case "signal":
            if (msg.from) this.callbacks.onSignal(msg.from, msg.payload);
            break;
          case "error":
            this.callbacks.onError(msg.message || "Generic error");
            break;
        }
      };
    });
  }

  public leave(): void {
    if (this.client) {
      try {
        this.broadcastPresence("leave");
        this.client.disconnect();
      } catch {}
      this.client = null;
    }
    if (this.localWs) {
      try {
        this.localWs.send(JSON.stringify({ type: "leave" }));
        this.localWs.close();
      } catch {}
      this.localWs = null;
    }
    this.members.clear();
    this.sessionCode = null;
  }
}
