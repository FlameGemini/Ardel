#!/usr/bin/env python3
"""Build patch-legal-v2-de-es-ko.py with DE/ES/KO translations from English v2."""
from __future__ import annotations

import json
import re
import time
from pathlib import Path

from deep_translator import GoogleTranslator

ROOT = Path(__file__).resolve().parent.parent
TOOLS = Path(__file__).resolve().parent
EN_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs"
OUT = TOOLS / "patch-legal-v2-de-es-ko.py"

DE_VERSION = "Version 2 · Gültig ab: 2026-09-01"
ES_VERSION = "Versión 2 · Fecha de vigencia: 2026-09-01"
KO_VERSION = "버전 2 · 시행일: 2026-09-01"

TITLES = {
    "de": [
        "Definitionen und Auslegung",
        "Gewährung einer beschränkten Lizenz",
        "Systemanforderungen und Kompatibilität",
        "Sicherheit und Schwachstellenmeldung",
        "Freistellung",
        "Exportkontrolle und Sanktionscompliance",
        "Höhere Gewalt",
        "Salvatorische Klausel, Verzicht, Abtretung und Fortgeltung",
        "Präambel und Annahme der Bedingungen",
        "Haftungsausschluss bezüglich Verbindung und Markenhinweis",
        "Beschreibung der Software und Leistungsumfang",
        "Lokale Datenspeicherung und portable Bereitstellung",
        "Konten, Authentifizierung und lizenzierte Spielkopien",
        "Nutzerpflichten und verbotenes Verhalten",
        "Downloads von Drittanbietern und externe Dienste",
        "Bedingungen für Drittanbieter-Dienste",
        "Drittanbieter-Inhalte; keine Billigung",
        "Community-Ressourcen und Urheberrechtsnachweis",
        "Mods, Modpacks und nutzergenerierte Inhalte",
        "Integritätsprüfung und automatischer Abruf",
        "Weiterverteilung und abgeleitete Werke",
        "Offline-Skin-Anzeige und lokales Relay",
        "Datenverarbeitung und Datenschutz",
        "Absturzdiagnose und Modelltrainingsdaten",
        "Rechte an geistigem Eigentum",
        "Risikoübernahme",
        "Gewährleistungsausschluss und Haftungsbeschränkung",
        "Änderung, Aussetzung und Beendigung",
        "Nutzung durch Minderjährige",
        "Mitteilungen und Kontakt",
        "Einhaltung geltenden Rechts",
        "Zwingende Verbraucher-, Datenschutz- und Digitalrechte",
        "Verbindliche Sprache, Streitbeilegung und Rechtswahl",
    ],
    "es": [
        "Definiciones e interpretación",
        "Concesión de licencia limitada",
        "Requisitos del sistema y compatibilidad",
        "Seguridad e informe de vulnerabilidades",
        "Indemnización",
        "Control de exportaciones y cumplimiento de sanciones",
        "Fuerza mayor",
        "Divisibilidad, renuncia, cesión y supervivencia",
        "Preámbulo y aceptación de los términos",
        "Descargo de afiliación y aviso de marcas",
        "Descripción del Software y alcance de los servicios",
        "Almacenamiento local de datos y despliegue portable",
        "Cuentas, autenticación y copias de juego con licencia",
        "Obligaciones del usuario y conductas prohibidas",
        "Descargas de terceros y servicios externos",
        "Términos de servicios de terceros",
        "Contenidos de terceros; sin respaldo",
        "Recursos comunitarios y atribución de derechos de autor",
        "Mods, modpacks y contenido generado por usuarios",
        "Verificación de integridad y recuperación automatizada",
        "Redistribución y obras derivadas",
        "Visualización de skin sin conexión y relé local",
        "Tratamiento de datos y privacidad",
        "Diagnóstico de fallos y datos de entrenamiento del modelo",
        "Derechos de propiedad intelectual",
        "Asunción de riesgo",
        "Exención de garantías y limitación de responsabilidad",
        "Modificación, suspensión y terminación",
        "Uso por menores",
        "Avisos y contacto",
        "Cumplimiento de la legislación aplicable",
        "Derechos obligatorios del consumidor, privacidad y digitales",
        "Idioma rector, resolución de disputas y ley aplicable",
    ],
    "ko": [
        "정의 및 해석",
        "제한적 라이선스 부여",
        "시스템 요구 사항 및 호환성",
        "보안 및 취약점 보고",
        "면책(손해배상)",
        "수출 통제 및 제재 준수",
        "불가항력",
        "분리 가능성, 권리 포기, 양도 및 존속",
        "서문 및 약관 수락",
        "비제휴 및 상표 고지",
        "소프트웨어 설명 및 서비스 범위",
        "로컬 데이터 저장 및 휴대형 배포",
        "계정, 인증 및 정품 게임 사본",
        "이용자 의무 및 금지 행위",
        "제3자 다운로드 및 외부 서비스",
        "제3자 서비스 약관",
        "제3자 콘텐츠; 비보증",
        "커뮤니티 리소스 및 저작권 표시",
        "모드, 모드팩 및 사용자 생성 콘텐츠",
        "무결성 검증 및 자동 검색",
        "재배포 및 파생 저작물",
        "오프라인 스킨 표시 및 로컬 릴레이",
        "데이터 처리 및 개인정보",
        "충돌 진단 및 모델 학습 데이터",
        "지적 재산권",
        "위험 부담",
        "보증 부인 및 책임 제한",
        "변경, 중단 및 종료",
        "미성년자 이용",
        "통지 및 연락처",
        "준수해야 할 법률",
        "강행적 소비자·개인정보·디지털 권리",
        "준거 언어, 분쟁 해결 및 준거법",
    ],
}

# Tokens that must survive translation unchanged
PROTECTED = [
    "GET /ardel/skin/{id}.png",
    "/authenticate",
    "/refresh",
    "/validate",
    "/invalidate",
    "127.0.0.1",
    "github.com/FlameGemini/Ardel",
    "OSL-3.0",
    "crash_classifier.onnx",
    "launch_timing.log",
    "authlib-injector",
    "Microsoft.ML.OnnxRuntime",
    "tests/fixtures/crash/",
    "BMCLAPI",
    "curse.tools",
    "Open-Meteo",
    "WebView2",
    "Modrinth",
    "CurseForge",
    "NeoForge",
    "OptiFine",
    "mclo.gs",
    "HuggingFace",
    "mc-logs",
    "CmlLib",
    "WinUI",
    ".minecraft",
    "minecraft.net",
    "FlameGemini",
    "Mojang Studios",
    "Microsoft Corporation",
    "Mojang Synergies AB",
    "Game Pass",
    "Realms",
    "OneDrive",
    "NTFS",
    "ARM64",
    "x64",
    "ONNX",
    "GDPR",
    "CCPA/CPRA",
    "LGPD",
    "PIPEDA",
    "APPI",
    "PIPA",
    "PIPL",
    "EEA",
    "DPA",
    "COPPA",
    "GDPR-K",
    "DDoS",
    "Yggdrasil",
    "OAuth",
    "Forge",
    "Fabric",
    "Java Edition",
    "Minecraft: Java Edition",
    "Minecraft",
    "Ardel",
]


CACHE = TOOLS / "_legal_v2_translate_cache.json"


def load_cache() -> dict:
    if CACHE.exists():
        return json.loads(CACHE.read_text(encoding="utf-8"))
    return {}


def save_cache(cache: dict) -> None:
    CACHE.write_text(json.dumps(cache, ensure_ascii=False, indent=2), encoding="utf-8")


def parse_en_bodies() -> list[str]:
    text = EN_CS.read_text(encoding="utf-8")
    m = re.search(r'public const string English =\s*"""\s*(.+?)"""', text, re.DOTALL)
    if not m:
        raise SystemExit("English block not found")
    parts = [p.strip() for p in m.group(1).strip().split("\n\n") if p.strip()]
    if len(parts) != 34:
        raise SystemExit(f"Expected 34 sections, got {len(parts)}")
    return [p.split("\n", 1)[1] for p in parts[1:]]


def protect(text: str) -> tuple[str, dict[str, str]]:
    mapping: dict[str, str] = {}
    out = text
    for i, token in enumerate(PROTECTED):
        placeholder = f"__PH{i:03d}__"
        if token in out:
            out = out.replace(token, placeholder)
            mapping[placeholder] = token
    return out, mapping


def restore(text: str, mapping: dict[str, str]) -> str:
    for ph, token in mapping.items():
        text = text.replace(ph, token)
    return text


def translate_text(text: str, target: str) -> str:
    protected, mapping = protect(text)
    chunks: list[str] = []
    buf = protected
    max_chunk = 3500
    while buf:
        if len(buf) <= max_chunk:
            chunks.append(buf)
            break
        split_at = buf.rfind(". ", 0, max_chunk)
        if split_at < max_chunk // 2:
            split_at = buf.rfind(" ", 0, max_chunk)
        if split_at < 1:
            split_at = max_chunk
        chunks.append(buf[: split_at + 1].strip())
        buf = buf[split_at + 1 :].strip()

    translator = GoogleTranslator(source="en", target=target)
    translated_parts = []
    for chunk in chunks:
        last_err: Exception | None = None
        for attempt in range(5):
            try:
                translated_parts.append(translator.translate(chunk))
                last_err = None
                break
            except Exception as exc:
                last_err = exc
                time.sleep(3 * (attempt + 1))
        if last_err is not None:
            raise RuntimeError(f"Translation failed for {target}: {last_err}") from last_err
        time.sleep(0.5)
    return restore(" ".join(translated_parts), mapping)


def build_locale_sections(bodies: list[str], lang: str, cache: dict) -> list[tuple[str, str]]:
    target = {"de": "de", "es": "es", "ko": "ko"}[lang]
    titles = TITLES[lang]
    result = []
    for i, (title, body) in enumerate(zip(titles, bodies)):
        key = f"{lang}:{i}"
        if key in cache:
            result.append((title, cache[key]))
            continue
        print(f"  [{lang}] section {i + 1}/33", flush=True)
        translated = translate_text(body, target)
        cache[key] = translated
        save_cache(cache)
        result.append((title, translated))
    return result


def emit_patch_script(locales: dict[str, list[tuple[str, str]]]) -> None:
    def fmt_sections(items: list[tuple[str, str]]) -> str:
        lines = []
        for title, body in items:
            lines.append(f"    section({title!r},")
            lines.append(f"        {body!r}),")
        return "\n".join(lines)

    content = f'''#!/usr/bin/env python3
"""Patch V2 legal notices (German, Spanish, Korean) into AboutLegalNotice.Locales.cs."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EN_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs"
LOCALES_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales.cs"


def section(title: str, body: str) -> str:
    return f"{{title}}\\n{{body}}"


def build_locale(version_line: str, sections: list[str]) -> str:
    return "\\n\\n".join([version_line, *sections])


def indent_csharp(text: str) -> str:
    return "\\n".join("        " + line if line else "" for line in text.split("\\n"))


def csharp_const(name: str, text: str) -> str:
    return f\'    public const string {{name}} =\\n        """\\n{{indent_csharp(text)}}\\n        """;\'


def count_sections(text: str) -> int:
    return len([b for b in text.strip().split("\\n\\n") if b.strip()])


def patch_const(cs_text: str, const_name: str, body: str) -> str:
    pattern = rf\'(public const string {{const_name}} =\\s*""")\\s*[\\s\\S]*?(\\s*""";)\'
    replacement = rf"\\1\\n{{indent_csharp(body)}}\\n        \\2"
    new_text, n = re.subn(pattern, replacement, cs_text, count=1)
    if n != 1:
        raise SystemExit(f"Failed to patch {{const_name}}")
    return new_text


DE_VERSION = {DE_VERSION!r}
ES_VERSION = {ES_VERSION!r}
KO_VERSION = {KO_VERSION!r}

DE_SECTIONS = [
{fmt_sections(locales["de"])}
]

ES_SECTIONS = [
{fmt_sections(locales["es"])}
]

KO_SECTIONS = [
{fmt_sections(locales["ko"])}
]


def main() -> None:
    en_m = re.search(
        r\'public const string English =\\s*"""\\s*(.+?)"""\',
        EN_CS.read_text(encoding="utf-8"),
        re.DOTALL,
    )
    if not en_m:
        raise SystemExit("English block not found")
    en_count = count_sections(en_m.group(1).strip())

    locales_text = LOCALES_CS.read_text(encoding="utf-8")
    locales_text = patch_const(locales_text, "German", build_locale(DE_VERSION, DE_SECTIONS))
    locales_text = patch_const(locales_text, "Spanish", build_locale(ES_VERSION, ES_SECTIONS))
    locales_text = patch_const(locales_text, "Korean", build_locale(KO_VERSION, KO_SECTIONS))
    LOCALES_CS.write_text(locales_text, encoding="utf-8")
    print(f"Patched {{LOCALES_CS}}")

    de = build_locale(DE_VERSION, DE_SECTIONS)
    es = build_locale(ES_VERSION, ES_SECTIONS)
    ko = build_locale(KO_VERSION, KO_SECTIONS)
    print(f"EN sections: {{en_count}}")
    print(f"DE sections: {{count_sections(de)}}")
    print(f"ES sections: {{count_sections(es)}}")
    print(f"KO sections: {{count_sections(ko)}}")


if __name__ == "__main__":
    main()
'''
    OUT.write_text(content, encoding="utf-8")
    print(f"Wrote {OUT} ({OUT.stat().st_size:,} bytes)")


def main() -> None:
    bodies = parse_en_bodies()
    cache = load_cache()
    locales: dict[str, list[tuple[str, str]]] = {}
    for lang in ("de", "es", "ko"):
        print(f"Translating to {lang}...", flush=True)
        locales[lang] = build_locale_sections(bodies, lang, cache)
    emit_patch_script(locales)


if __name__ == "__main__":
    main()
