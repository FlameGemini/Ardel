#!/usr/bin/env python3
"""Emit Loc.<Lang>.cs from a translated JSON catalog."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC_DIR = ROOT / "src" / "Ardel.Launcher" / "Localization"
EN = ROOT / "tools" / "_loc_en.json"

LANGS = {
    "es": ("Es", "Spanish", "es", "Spanish"),
    "ko": ("Ko", "Korean", "ko", "Korean"),
    "de": ("De", "German", "de", "German"),
    "pt": ("Pt", "Portuguese", "pt-BR", "Portuguese"),
    "it": ("It", "Italian", "it", "Italian"),
    "ru": ("Ru", "Russian", "ru", "Russian"),
}


def csharp_escape(s: str) -> str:
    return (
        s.replace("\\", "\\\\")
        .replace('"', '\\"')
        .replace("\r\n", "\\n")
        .replace("\n", "\\n")
        .replace("\r", "\\n")
        .replace("\t", "\\t")
    )


def emit(lang: str, translations: dict[str, str], out: Path) -> None:
    file_stem, dict_name, tag, label = LANGS[lang]
    en = json.loads(EN.read_text(encoding="utf-8"))
    missing = [k for k in en if k not in translations or translations[k] is None]
    if missing:
        raise SystemExit(f"{lang}: missing {len(missing)} keys, e.g. {missing[:5]}")
    extra = [k for k in translations if k not in en]
    if extra:
        raise SystemExit(f"{lang}: unexpected keys {extra[:5]}")

    lines = [
        "namespace Ardel.Launcher.Localization;",
        "",
        f"/// <summary>{label} catalog for Loc ({tag}).</summary>",
        "public static partial class Loc",
        "{",
        f"    private static readonly Dictionary<string, string> {dict_name} = new(StringComparer.Ordinal)",
        "    {",
    ]
    for key in en:
        val = translations[key]
        # Keep empty strings as empty
        lines.append(f'        [LocKeys.{key}] = "{csharp_escape(str(val))}",')
    lines.append("    };")
    lines.append("}")
    lines.append("")
    out.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {out} ({len(en)} entries)")


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("lang", choices=sorted(LANGS))
    p.add_argument("json", type=Path)
    p.add_argument("-o", "--out", type=Path, default=None)
    args = p.parse_args()
    data = json.loads(args.json.read_text(encoding="utf-8"))
    out = args.out or LOC_DIR / f"Loc.{LANGS[args.lang][0]}.cs"
    emit(args.lang, data, out)


if __name__ == "__main__":
    main()
