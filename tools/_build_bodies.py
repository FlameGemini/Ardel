#!/usr/bin/env python3
"""Build _legal_v2_de_es_ko_bodies.py with full DE/ES/KO translations from English v2."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EN_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs"
OUT = Path(__file__).resolve().parent / "_legal_v2_de_es_ko_bodies.py"

DE_VERSION = "Version 2 · Gültig ab: 2026-09-01"
ES_VERSION = "Versión 2 · Fecha de vigencia: 2026-09-01"
KO_VERSION = "버전 2 · 시행일: 2026-09-01"

# (German title, Spanish title, Korean title) in English section order
TITLES = [
    (
        "Definitionen und Auslegung",
        "Definiciones e interpretación",
        "정의 및 해석",
    ),
    (
        "Gewährung einer beschränkten Lizenz",
        "Concesión de licencia limitada",
        "제한적 라이선스 부여",
    ),
    (
        "Systemanforderungen und Kompatibilität",
        "Requisitos del sistema y compatibilidad",
        "시스템 요구 사항 및 호환성",
    ),
    (
        "Sicherheit und Schwachstellenmeldung",
        "Seguridad e informe de vulnerabilidades",
        "보안 및 취약점 보고",
    ),
    (
        "Freistellung",
        "Indemnización",
        "면책(손해배상)",
    ),
    (
        "Exportkontrolle und Sanktionscompliance",
        "Control de exportaciones y cumplimiento de sanciones",
        "수출 통제 및 제재 준수",
    ),
    (
        "Höhere Gewalt",
        "Fuerza mayor",
        "불가항력",
    ),
    (
        "Salvatorische Klausel, Verzicht, Abtretung und Fortgeltung",
        "Divisibilidad, renuncia, cesión y supervivencia",
        "분리 가능성, 권리 포기, 양도 및 존속",
    ),
    (
        "Präambel und Annahme der Bedingungen",
        "Preámbulo y aceptación de los términos",
        "서문 및 약관 수락",
    ),
    (
        "Haftungsausschluss bezüglich Verbindung und Markenhinweis",
        "Descargo de afiliación y aviso de marcas",
        "비제휴 및 상표 고지",
    ),
    (
        "Beschreibung der Software und Leistungsumfang",
        "Descripción del Software y alcance de los servicios",
        "소프트웨어 설명 및 서비스 범위",
    ),
    (
        "Lokale Datenspeicherung und portable Bereitstellung",
        "Almacenamiento local de datos y despliegue portable",
        "로컬 데이터 저장 및 휴대형 배포",
    ),
    (
        "Konten, Authentifizierung und lizenzierte Spielkopien",
        "Cuentas, autenticación y copias de juego con licencia",
        "계정, 인증 및 정품 게임 사본",
    ),
    (
        "Nutzerpflichten und verbotenes Verhalten",
        "Obligaciones del usuario y conductas prohibidas",
        "이용자 의무 및 금지 행위",
    ),
    (
        "Downloads von Drittanbietern und externe Dienste",
        "Descargas de terceros y servicios externos",
        "제3자 다운로드 및 외부 서비스",
    ),
    (
        "Bedingungen für Drittanbieter-Dienste",
        "Términos de servicios de terceros",
        "제3자 서비스 약관",
    ),
    (
        "Drittanbieter-Inhalte; keine Billigung",
        "Contenidos de terceros; sin respaldo",
        "제3자 콘텐츠; 비보증",
    ),
    (
        "Community-Ressourcen und Urheberrechtsnachweis",
        "Recursos comunitarios y atribución de derechos de autor",
        "커뮤니티 리소스 및 저작권 표시",
    ),
    (
        "Mods, Modpacks und nutzergenerierte Inhalte",
        "Mods, modpacks y contenido generado por usuarios",
        "모드, 모드팩 및 사용자 생성 콘텐츠",
    ),
    (
        "Integritätsprüfung und automatischer Abruf",
        "Verificación de integridad y recuperación automatizada",
        "무결성 검증 및 자동 검색",
    ),
    (
        "Weiterverteilung und abgeleitete Werke",
        "Redistribución y obras derivadas",
        "재배포 및 파생 저작물",
    ),
    (
        "Offline-Skin-Anzeige und lokales Relay",
        "Visualización de skin sin conexión y relé local",
        "오프라인 스킨 표시 및 로컬 릴레이",
    ),
    (
        "Datenverarbeitung und Datenschutz",
        "Tratamiento de datos y privacidad",
        "데이터 처리 및 개인정보",
    ),
    (
        "Absturzdiagnose und Modelltrainingsdaten",
        "Diagnóstico de fallos y datos de entrenamiento del modelo",
        "충돌 진단 및 모델 학습 데이터",
    ),
    (
        "Rechte an geistigem Eigentum",
        "Derechos de propiedad intelectual",
        "지적 재산권",
    ),
    (
        "Risikoübernahme",
        "Asunción de riesgo",
        "위험 부담",
    ),
    (
        "Gewährleistungsausschluss und Haftungsbeschränkung",
        "Exención de garantías y limitación de responsabilidad",
        "보증 부인 및 책임 제한",
    ),
    (
        "Änderung, Aussetzung und Beendigung",
        "Modificación, suspensión y terminación",
        "변경, 중단 및 종료",
    ),
    (
        "Nutzung durch Minderjährige",
        "Uso por menores",
        "미성년자 이용",
    ),
    (
        "Mitteilungen und Kontakt",
        "Avisos y contacto",
        "통지 및 연락처",
    ),
    (
        "Einhaltung geltenden Rechts",
        "Cumplimiento de la legislación aplicable",
        "준수해야 할 법률",
    ),
    (
        "Zwingende Verbraucher-, Datenschutz- und Digitalrechte",
        "Derechos obligatorios del consumidor, privacidad y digitales",
        "강행적 소비자·개인정보·디지털 권리",
    ),
    (
        "Verbindliche Sprache, Streitbeilegung und Rechtswahl",
        "Idioma rector, resolución de disputas y ley aplicable",
        "준거 언어, 분쟁 해결 및 준거법",
    ),
]


def parse_en_bodies() -> list[str]:
    text = EN_CS.read_text(encoding="utf-8")
    m = re.search(r'public const string English =\s*"""\s*(.+?)"""', text, re.DOTALL)
    if not m:
        raise SystemExit("English not found")
    parts = [p.strip() for p in m.group(1).strip().split("\n\n") if p.strip()]
    if len(parts) != 34:
        raise SystemExit(f"Expected 34 EN sections, got {len(parts)}")
    return [p.split("\n", 1)[1] for p in parts[1:]]


def tr_de(en: str, title: str) -> str:
    return TRANSLATIONS["de"][title]


def tr_es(en: str, title: str) -> str:
    return TRANSLATIONS["es"][title]


def tr_ko(en: str, title: str) -> str:
    return TRANSLATIONS["ko"][title]


def py_str(s: str) -> str:
    return repr(s)


def main() -> None:
    en_bodies = parse_en_bodies()
    if len(TITLES) != 33:
        raise SystemExit("Title count mismatch")

    de_sections: list[str] = []
    es_sections: list[str] = []
    ko_sections: list[str] = []

    for (de_t, es_t, ko_t), en in zip(TITLES, en_bodies):
        de_sections.append(f"section({py_str(de_t)}, {py_str(tr_de(en, de_t))})")
        es_sections.append(f"section({py_str(es_t)}, {py_str(tr_es(en, es_t))})")
        ko_sections.append(f"section({py_str(ko_t)}, {py_str(tr_ko(en, ko_t))})")

    out = [
        '"""Auto-generated legal section bodies for DE/ES/KO v2."""',
        "from __future__ import annotations",
        "",
        "def section(title: str, body: str) -> str:",
        '    return f"{title}\\n{body}"',
        "",
        f"DE_VERSION = {py_str(DE_VERSION)}",
        f"ES_VERSION = {py_str(ES_VERSION)}",
        f"KO_VERSION = {py_str(KO_VERSION)}",
        "",
        "DE_SECTIONS = [",
        *[f"    {s}," for s in de_sections],
        "]",
        "",
        "ES_SECTIONS = [",
        *[f"    {s}," for s in es_sections],
        "]",
        "",
        "KO_SECTIONS = [",
        *[f"    {s}," for s in ko_sections],
        "]",
        "",
    ]
    OUT.write_text("\n".join(out), encoding="utf-8")
    print(f"Wrote {OUT}")


# TRANSLATIONS populated in separate exec below
TRANSLATIONS: dict[str, dict[str, str]] = {"de": {}, "es": {}, "ko": {}}

if __name__ == "__main__":
    # Import translation data from companion
    from tools._legal_v2_translation_data import TRANSLATIONS as TD  # type: ignore

    TRANSLATIONS.update(TD)
    main()
