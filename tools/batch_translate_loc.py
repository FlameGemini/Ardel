#!/usr/bin/env python3
"""Batch-translate tools/_loc_en.json into es/ko/de/pt/it/ru JSON catalogs."""
from __future__ import annotations

import json
import re
import time
from pathlib import Path

from deep_translator import GoogleTranslator

ROOT = Path(__file__).resolve().parents[1]
EN_PATH = ROOT / "tools" / "_loc_en.json"

# deep_translator target codes
TARGETS = {
    "es": "es",
    "ko": "ko",
    "de": "de",
    "pt": "pt",  # Google pt ≈ Brazilian for UI
    "it": "it",
    "ru": "ru",
}

# Keys that must stay as endonyms / English product labels (same across catalogs).
FORCE_LITERAL: dict[str, str] = {
    "Brand_Name": "Ardel",
    "Settings_LanguageEnglish": "English (US)",
    "Settings_LanguageEnglishUS": "English (US)",
    "Settings_LanguageEnglishUK": "English (UK)",
    "Settings_LanguageChinese": "简体中文",
    "Settings_LanguageChineseTraditional": "繁體中文",
    "Settings_LanguageJapanese": "日本語",
    "Settings_LanguageFrench": "Français",
    "Settings_LanguageSpanish": "Español",
    "Settings_LanguageKorean": "한국어",
    "Settings_LanguageGerman": "Deutsch",
    "Settings_LanguagePortuguese": "Português (Brasil)",
    "Settings_LanguageItalian": "Italiano",
    "Settings_LanguageRussian": "Русский",
}

PLACEHOLDER_RE = re.compile(r"\{(\d+)\}")


def protect(s: str) -> tuple[str, list[str]]:
    """Replace {n} with tokens Google won't mangle."""
    held: list[str] = []

    def repl(m: re.Match[str]) -> str:
        held.append(m.group(0))
        return f"⟦{len(held) - 1}⟧"

    return PLACEHOLDER_RE.sub(repl, s), held


def restore(s: str, held: list[str]) -> str:
    for i, ph in enumerate(held):
        s = s.replace(f"⟦{i}⟧", ph)
        s = s.replace(f"[[{i}]]", ph)
        s = s.replace(f"[{i}]", ph)
    # Fix common Google mangling of ellipsis
    s = s.replace("...", "…") if "…" in "".join(held) else s
    return s


def translate_map(en: dict[str, str], target: str) -> dict[str, str]:
    translator = GoogleTranslator(source="en", target=target)
    out: dict[str, str] = {}
    keys = list(en.keys())
    batch_src: list[str] = []
    batch_meta: list[tuple[str, list[str]]] = []  # key, held

    def flush() -> None:
        nonlocal batch_src, batch_meta
        if not batch_src:
            return
        for attempt in range(5):
            try:
                # deep_translator translate_batch
                results = translator.translate_batch(batch_src)
                break
            except Exception as ex:  # noqa: BLE001
                wait = 2 ** attempt
                print(f"  retry {attempt + 1} after {ex!r}, sleep {wait}s")
                time.sleep(wait)
        else:
            # fallback one-by-one
            results = []
            for text in batch_src:
                try:
                    results.append(translator.translate(text))
                    time.sleep(0.05)
                except Exception:
                    results.append(text)

        for (key, held), translated in zip(batch_meta, results, strict=True):
            if not translated:
                translated = en[key]
            out[key] = restore(translated, held)
        batch_src = []
        batch_meta = []

    for key in keys:
        if key in FORCE_LITERAL:
            out[key] = FORCE_LITERAL[key]
            continue
        raw = en[key]
        if not raw or raw.strip() == "":
            out[key] = raw
            continue
        # Skip pure symbols / numbers
        if raw in {"·", "/", "|", "—", "–", "-", "…"} or raw.isascii() and len(raw) <= 2 and not raw.isalpha():
            # still translate short words like "OK" — only skip non-alpha short
            if not any(c.isalpha() for c in raw):
                out[key] = raw
                continue

        protected, held = protect(raw)
        batch_src.append(protected)
        batch_meta.append((key, held))
        if len(batch_src) >= 40:
            flush()
            time.sleep(0.35)
            print(f"  {target}: {len(out)}/{len(keys)}")

    flush()
    return out


def main() -> None:
    en = json.loads(EN_PATH.read_text(encoding="utf-8"))
    for code, google in TARGETS.items():
        out_path = ROOT / "tools" / f"_loc_{code}.json"
        if out_path.exists():
            existing = json.loads(out_path.read_text(encoding="utf-8"))
            if set(existing) == set(en):
                print(f"skip {code} (already complete)")
                continue
        print(f"translating → {code} ({google})")
        translated = translate_map(en, google)
        assert set(translated) == set(en), (set(en) - set(translated), set(translated) - set(en))
        out_path.write_text(
            json.dumps(translated, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(f"wrote {out_path} ({len(translated)})")


if __name__ == "__main__":
    main()
