# -*- coding: utf-8 -*-
"""Export AboutLegalNotice locales to Ardel-Website assets/terms-content.json."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

LOC = Path(r"d:/Project/Ardel/src/Ardel.Launcher/Localization")
WEB = Path(r"d:/Project/Ardel-Website")
FILES = [
    LOC / "AboutLegalNotice.cs",
    LOC / "AboutLegalNotice.Locales.cs",
    LOC / "AboutLegalNotice.Locales2.cs",
]

META = {
    "en": {
        "pageTitle": "Terms of Service",
        "brand": "Ardel",
        "meta_fmt": "Version {v} · Effective {d}",
        "note": "These Terms of Service also appear in Ardel Desktop under Settings → About.",
        "const": "English",
        "htmlLang": "en",
        "label": "EN",
    },
    "zh": {
        "pageTitle": "服务条款",
        "brand": "Ardel",
        "meta_fmt": "版本 {v} · 生效日期 {d}",
        "note": "本服务条款与 Ardel Desktop「设置 → 关于」中的文本一致。",
        "const": "Chinese",
        "htmlLang": "zh-Hans",
        "label": "简中",
    },
    "zh-hant": {
        "pageTitle": "服務條款",
        "brand": "Ardel",
        "meta_fmt": "版本 {v} · 生效日期 {d}",
        "note": "本服務條款與 Ardel Desktop「設定 → 關於」中的文字一致。",
        "const": "ChineseTraditional",
        "htmlLang": "zh-Hant",
        "label": "繁中",
    },
    "ja": {
        "pageTitle": "利用規約",
        "brand": "Ardel",
        "meta_fmt": "バージョン {v} · 発効日 {d}",
        "note": "本利用規約は Ardel Desktop の「設定 → バージョン情報」にも表示されます。",
        "const": "Japanese",
        "htmlLang": "ja",
        "label": "日本語",
    },
    "fr": {
        "pageTitle": "Conditions d'utilisation",
        "brand": "Ardel",
        "meta_fmt": "Version {v} · Entrée en vigueur {d}",
        "note": "Ces conditions figurent aussi dans Ardel Desktop sous Paramètres → À propos.",
        "const": "French",
        "htmlLang": "fr",
        "label": "FR",
    },
    "de": {
        "pageTitle": "Nutzungsbedingungen",
        "brand": "Ardel",
        "meta_fmt": "Version {v} · Gültig ab {d}",
        "note": "Diese Bedingungen erscheinen auch in Ardel Desktop unter Einstellungen → Info.",
        "const": "German",
        "htmlLang": "de",
        "label": "DE",
    },
    "es": {
        "pageTitle": "Términos de servicio",
        "brand": "Ardel",
        "meta_fmt": "Versión {v} · Vigente desde {d}",
        "note": "Estos términos también aparecen en Ardel Desktop en Configuración → Acerca de.",
        "const": "Spanish",
        "htmlLang": "es",
        "label": "ES",
    },
    "ko": {
        "pageTitle": "서비스 약관",
        "brand": "Ardel",
        "meta_fmt": "버전 {v} · 시행일 {d}",
        "note": "본 약관은 Ardel Desktop의 설정 → 정보에도 표시됩니다.",
        "const": "Korean",
        "htmlLang": "ko",
        "label": "한국어",
    },
    "it": {
        "pageTitle": "Termini di servizio",
        "brand": "Ardel",
        "meta_fmt": "Versione {v} · In vigore dal {d}",
        "note": "Questi termini compaiono anche in Ardel Desktop in Impostazioni → Informazioni.",
        "const": "Italian",
        "htmlLang": "it",
        "label": "IT",
    },
    "pt": {
        "pageTitle": "Termos de serviço",
        "brand": "Ardel",
        "meta_fmt": "Versão {v} · Vigente desde {d}",
        "note": "Estes termos também aparecem no Ardel Desktop em Configurações → Sobre.",
        "const": "Portuguese",
        "htmlLang": "pt",
        "label": "PT",
    },
    "ru": {
        "pageTitle": "Условия использования",
        "brand": "Ardel",
        "meta_fmt": "Версия {v} · Действует с {d}",
        "note": "Эти условия также отображаются в Ardel Desktop: Параметры → О программе.",
        "const": "Russian",
        "htmlLang": "ru",
        "label": "RU",
    },
}


def log(msg: str) -> None:
    sys.stdout.buffer.write((msg + "\n").encode("utf-8", errors="replace"))
    sys.stdout.buffer.flush()


def extract_consts(text: str) -> dict[str, str]:
    return {
        m.group(1): m.group(2)
        for m in re.finditer(r'public const string (\w+)\s*=\s*"""\r?\n(.*?)""";', text, re.S)
    }


def parse_sections(raw: str) -> list[dict[str, str]]:
    sections = []
    for block in re.split(r"(?:\r?\n){2,}", raw.strip()):
        lines = [ln.strip() for ln in block.splitlines() if ln.strip()]
        if len(lines) < 2:
            continue
        sections.append({"title": lines[0], "body": " ".join(lines[1:])})
    return sections


def main() -> None:
    main_cs = (LOC / "AboutLegalNotice.cs").read_text(encoding="utf-8")
    version = int(re.search(r"public const int Version = (\d+);", main_cs).group(1))
    effective = re.search(r'public const string EffectiveDate = "([^"]+)";', main_cs).group(1)

    merged: dict[str, str] = {}
    for path in FILES:
        merged.update(extract_consts(path.read_text(encoding="utf-8")))

    payload = {
        "version": version,
        "effectiveDate": effective,
        "languages": [
            {"id": k, "label": v["label"], "htmlLang": v["htmlLang"]} for k, v in META.items()
        ],
    }

    for lang, meta in META.items():
        body = merged[meta["const"]]
        sections = parse_sections(body)
        payload[lang] = {
            "pageTitle": meta["pageTitle"],
            "brand": meta["brand"],
            "meta": meta["meta_fmt"].format(v=version, d=effective),
            "note": meta["note"],
            "sections": sections,
        }
        log(f"{lang}: {len(sections)} sections")
        if len(sections) != 39:
            raise SystemExit(f"{lang} expected 39 sections, got {len(sections)}")

    # Sanity: EN still has core clauses; crash-model training section must be gone
    titles = [s["title"] for s in payload["en"]["sections"]]
    for need in [
        "Free software and absence of service levels",
        "Security-software false positives",
        "No legal advice",
    ]:
        if need not in titles:
            raise SystemExit(f"EN missing section: {need}")
    if "Crash diagnostics and model training data" in titles:
        raise SystemExit("EN still has crash model-training section")

    out = WEB / "assets" / "terms-content.json"
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    log(f"Wrote {out} version={version} date={effective} size={out.stat().st_size}")


if __name__ == "__main__":
    main()
