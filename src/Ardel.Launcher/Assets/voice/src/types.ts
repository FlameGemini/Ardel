export type VoiceBoot = {
  ws?: string;
  name?: string;
  theme?: string;
  i18n?: Record<string, string>;
};

export type VoiceFieldHandle = {
  setLive(live: boolean): void;
  destroy(): void;
};

export type VoiceFieldApi = {
  mount(
    host: HTMLElement,
    options?: { ink?: string; accent?: string }
  ): VoiceFieldHandle;
};

declare global {
  interface Window {
    __ARDEL_VOICE__?: VoiceBoot;
    VoiceField?: VoiceFieldApi;
  }
}

export {};
