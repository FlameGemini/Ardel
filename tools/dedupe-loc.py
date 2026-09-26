#!/usr/bin/env python3
"""Remove duplicate [LocKeys.X] entries from Loc catalog files, keeping best translation."""
from __future__ import annotations

import re
import sys
from pathlib import Path

LOC_DIR = Path(__file__).resolve().parents[1] / "src" / "Ardel.Launcher" / "Localization"
FB_PATH = LOC_DIR / "Loc.cs"


def parse_fallback() -> dict[str, str]:
    text = FB_PATH.read_text(encoding="utf-8")
    fb: dict[str, str] = {}
    for match in re.finditer(r"\[LocKeys\.(\w+)\]\s*=\s*(.+?),\s*\n", text, re.DOTALL):
        key, val = match.group(1), match.group(2).strip()
        if val.startswith('"') and val.endswith('"'):
            fb[key] = val[1:-1]
    return fb


def score_value(key: str, value: str, fb: dict[str, str]) -> int:
    if value.startswith("AboutLegalNotice."):
        return 100
    if key in fb and value != fb[key]:
        return 50
    if value and value != fb.get(key, ""):
        return 40
    return 0


def dedupe_file(path: Path, fb: dict[str, str]) -> int:
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    entry_re = re.compile(r"^(\s*)\[LocKeys\.(\w+)\]\s*=\s*(.+),\s*$")
    entries: dict[str, list[tuple[int, str, str]]] = {}
    for i, line in enumerate(lines):
        m = entry_re.match(line.rstrip("\r\n"))
        if not m:
            continue
        indent, key, val = m.group(1), m.group(2), m.group(3)
        entries.setdefault(key, []).append((i, indent, val))

    remove: set[int] = set()
    for key, occ in entries.items():
        if len(occ) <= 1:
            continue
        best = max(occ, key=lambda t: (score_value(key, t[2].strip().strip('"'), fb), -t[0]))
        for idx, _, val in occ:
            if idx != best[0]:
                remove.add(idx)

    if not remove:
        return 0
    new_lines = [line for i, line in enumerate(lines) if i not in remove]
    path.write_text("".join(new_lines), encoding="utf-8")
    return len(remove)


def main() -> int:
    fb = parse_fallback()
    total = 0
    for path in sorted(LOC_DIR.glob("Loc.*.cs")):
        if path.name in ("LocKeys.cs", "Loc.EnUk.cs"):
            continue
        n = dedupe_file(path, fb)
        if n:
            print(f"{path.name}: removed {n} duplicate lines")
            total += n
    print(f"total removed: {total}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
