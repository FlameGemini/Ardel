#!/usr/bin/env python3
"""Generate tools/patch-legal-v2-de-es-ko.py with embedded DE/ES/KO v2 legal text."""
from __future__ import annotations

import re
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
EN_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs"
IT_TXT = ROOT / "src/Ardel.Launcher/Localization/_v2_it.txt"
OUT = TOOLS / "patch-legal-v2-de-es-ko.py"

# Import translation bodies (title -> body per locale)
sys.path.insert(0, str(TOOLS))
from _legal_v2_translation_data import DE, ES, KO  # noqa: E402

DE_VERSION = "Version 2 · Gültig ab: 2026-09-01"
ES_VERSION = "Versión 2 · Fecha de vigencia: 2026-09-01"
KO_VERSION = "버전 2 · 시행일: 2026-09-01"


def parse_it_titles() -> list[str]:
    parts = [p.strip() for p in IT_TXT.read_text(encoding="utf-8").strip().split("\n\n") if p.strip()]
    return [p.split("\n", 1)[0] for p in parts[1:]]


def section(title: str, body: str) -> str:
    return f"section({body!r}, {title!r})"  # wrong order


def main() -> None:
    # titles from DE dict keys preserve EN order
    de_titles = list(DE.keys())
    es_titles = list(ES.keys())
    ko_titles = list(KO.keys())
    if not (len(de_titles) == len(es_titles) == len(ko_titles) == 33):
        raise SystemExit(f"Expected 33 sections, got {len(de_titles)}/{len(es_titles)}/{len(ko_titles)}")

    def fmt_sections(locale: dict[str, str]) -> str:
        lines = []
        for title, body in locale.items():
            lines.append(f'    section({title!r},')
            lines.append(f"        {body!r}),")
        return "\n".join(lines)

    header = '''#!/usr/bin/env python3
"""Patch V2 legal notices (German, Spanish, Korean) into AboutLegalNotice.Locales.cs."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EN_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs"
LOCALES_CS = ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales.cs"


def section(title: str, body: str) -> str:
    return f"{title}\\n{body}"


def build_locale(version_line: str, sections: list[str]) -> str:
    return "\\n\\n".join([version_line, *sections])


def indent_csharp(text: str) -> str:
    return "\\n".join("        " + line if line else "" for line in text.split("\\n"))


def csharp_const(name: str, text: str) -> str:
    return f\'    public const string {name} =\\n        """\\n{indent_csharp(text)}\\n        """;\'


def count_sections(text: str) -> int:
    return len([b for b in text.strip().split("\\n\\n") if b.strip()])


def patch_const(cs_text: str, const_name: str, body: str) -> str:
    pattern = rf\'(public const string {const_name} =\\s*""")\\s*[\\s\\S]*?(\\s*""";)\'
    replacement = rf"\\1\\n{indent_csharp(body)}\\n        \\2"
    new_text, n = re.subn(pattern, replacement, cs_text, count=1)
    if n != 1:
        raise SystemExit(f"Failed to patch {const_name}")
    return new_text


DE_VERSION = {DE_VERSION!r}
ES_VERSION = {ES_VERSION!r}
KO_VERSION = {KO_VERSION!r}

DE_SECTIONS = [
{fmt_sections(DE)}
]

ES_SECTIONS = [
{fmt_sections(ES)}
]

KO_SECTIONS = [
{fmt_sections(KO)}
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

    locales = LOCALES_CS.read_text(encoding="utf-8")
    locales = patch_const(locales, "German", build_locale(DE_VERSION, DE_SECTIONS))
    locales = patch_const(locales, "Spanish", build_locale(ES_VERSION, ES_SECTIONS))
    locales = patch_const(locales, "Korean", build_locale(KO_VERSION, KO_SECTIONS))
    LOCALES_CS.write_text(locales, encoding="utf-8")
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
    OUT.write_text(header, encoding="utf-8")
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
