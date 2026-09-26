#!/usr/bin/env python3
import re
import sys
from pathlib import Path

LOC_DIR = Path(__file__).resolve().parents[1] / "src" / "Ardel.Launcher" / "Localization"
sys.path.insert(0, str(Path(__file__).resolve().parent))
from loc_skip_keys import SKIP_KEYS


def parse_catalog(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    result: dict[str, str] = {}
    for match in re.finditer(r"\[LocKeys\.(\w+)\]\s*=\s*(.+?),\s*\n", text, re.DOTALL):
        key, val = match.group(1), match.group(2).strip()
        if val.startswith('"') and val.endswith('"') and not val.startswith('"""'):
            result[key] = val[1:-1]
    return result


def main() -> None:
    fb = parse_catalog(LOC_DIR / "Loc.cs")
    langs = {
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
    for lang, fn in sorted(langs.items()):
        cat = parse_catalog(LOC_DIR / fn)
        same = sorted(
            k
            for k, v in cat.items()
            if k in fb and k not in SKIP_KEYS and v == fb[k]
        )
        print(f"{lang}: {len(same)} untranslated (excl skip)")
        out = LOC_DIR / f"_audit_{lang}_real.txt"
        with out.open("w", encoding="utf-8") as f:
            for k in same:
                f.write(f"{k}\t{fb[k][:200].replace(chr(10), ' ')}\n")


if __name__ == "__main__":
    main()
