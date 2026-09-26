#!/usr/bin/env python3
import re
from pathlib import Path

LOC_DIR = Path(__file__).resolve().parents[1] / "src" / "Ardel.Launcher" / "Localization"

def parse_catalog(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    result: dict[str, str] = {}
    for match in re.finditer(r"\[LocKeys\.(\w+)\]\s*=\s*(.+?),\s*\n", text, re.DOTALL):
        key, val = match.group(1), match.group(2).strip()
        if val.startswith('"') and not val.startswith('"""'):
            # single-line string
            if val.endswith('"') and val.count('"') >= 2:
                result[key] = val[1:-1]
        elif val.startswith("AboutLegalNotice."):
            result[key] = val
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
        missing = sorted(k for k in fb if k not in cat)
        same = sorted(
            k
            for k, v in cat.items()
            if k in fb and isinstance(v, str) and not v.startswith("AboutLegalNotice.")
            and fb[k] and v == fb[k]
        )
        print(f"{lang}: missing={len(missing)} same_as_en={len(same)}")
        if missing:
            print(f"  missing: {', '.join(missing)}")
        out = LOC_DIR / f"_audit_{lang}.txt"
        with out.open("w", encoding="utf-8") as f:
            for k in same:
                f.write(f"{k}\t{fb[k][:200].replace(chr(10), ' ')}\n")
        print(f"  wrote {out.name}")

if __name__ == "__main__":
    main()
