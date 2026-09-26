#!/usr/bin/env python3
"""Apply localization patches from JSON translation files to Loc.*.cs catalogs."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC_DIR = ROOT / "src" / "Ardel.Launcher" / "Localization"
TRANS_DIR = Path(__file__).resolve().parent / "loc-translations"

LANG_FILES = {
    "zh-Hant": "Loc.ZhHant.cs",
    "ja": "Loc.Ja.cs",
    "ko": "Loc.Ko.cs",
    "fr": "Loc.Fr.cs",
    "de": "Loc.De.cs",
    "es": "Loc.Es.cs",
    "it": "Loc.It.cs",
    "pt": "Loc.Pt.cs",
    "ru": "Loc.Ru.cs",
}


def escape_csharp(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def apply_file(lang: str, translations: dict[str, str]) -> int:
    path = LOC_DIR / LANG_FILES[lang]
    text = path.read_text(encoding="utf-8")
    count = 0
    for key, value in translations.items():
        pattern = rf"(\[LocKeys\.{re.escape(key)}\]\s*=\s*)\"(?:\\.|[^\"\\\\])*\""
        replacement = rf'\1"{escape_csharp(value)}"'
        new_text, n = re.subn(pattern, replacement, text, count=1)
        if n:
            text = new_text
            count += 1
    path.write_text(text, encoding="utf-8")
    return count


def add_missing_entries(lang: str, entries: dict[str, str]) -> int:
    """Append missing [LocKeys.X] = \"...\", lines before closing };"""
    path = LOC_DIR / LANG_FILES[lang]
    text = path.read_text(encoding="utf-8")
    existing = set(re.findall(r"\[LocKeys\.(\w+)\]", text))
    lines = []
    for key, value in entries.items():
        if key in existing:
            continue
        lines.append(f'        [LocKeys.{key}] = "{escape_csharp(value)}",')
    if not lines:
        return 0
    text = text.replace("\n    };\n}\n", "\n" + "\n".join(lines) + "\n    };\n}\n", 1)
    path.write_text(text, encoding="utf-8")
    return len(lines)


def main() -> int:
    total = 0
    json_files = sorted(TRANS_DIR.glob("*.json"))
    for json_path in json_files:
        lang = json_path.stem
        if lang.startswith("round2-"):
            lang = lang.removeprefix("round2-")
        elif lang not in LANG_FILES:
            continue
        data = json.loads(json_path.read_text(encoding="utf-8"))
        updates = data.get("update", {})
        adds = data.get("add", {})
        u = apply_file(lang, updates)
        a = add_missing_entries(lang, adds)
        print(f"{json_path.name}: updated {u}, added {a}")
        total += u + a
    print(f"done, {total} changes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
