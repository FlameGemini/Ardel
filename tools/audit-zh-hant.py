#!/usr/bin/env python3
"""Find keys where zh-Hant still equals English but zh-CN is translated."""
import re
from pathlib import Path

LOC_DIR = Path(__file__).resolve().parents[1] / "src" / "Ardel.Launcher" / "Localization"

def parse_catalog(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    result: dict[str, str] = {}
    for match in re.finditer(r"\[LocKeys\.(\w+)\]\s*=\s*(.+?),\s*\n", text, re.DOTALL):
        key, val = match.group(1), match.group(2).strip()
        if val.startswith('"') and val.endswith('"') and not val.startswith('"""'):
            result[key] = val[1:-1]
    return result

fb = parse_catalog(LOC_DIR / "Loc.cs")
zh = parse_catalog(LOC_DIR / "Loc.Zh.cs")
hant = parse_catalog(LOC_DIR / "Loc.ZhHant.cs")

candidates = []
for k in sorted(hant):
    if k not in fb or k not in zh:
        continue
    if hant[k] == fb[k] and zh[k] != fb[k]:
        candidates.append((k, zh[k]))

print(f"zh-Hant can inherit from zh-CN for {len(candidates)} keys")
for k, v in candidates[:20]:
    print(f"  {k}: {v[:60]}")
