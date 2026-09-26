#!/usr/bin/env python3
"""Extract Loc.cs Fallback dictionary to tools/_loc_en.json."""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC = ROOT / "src" / "Ardel.Launcher" / "Localization" / "Loc.cs"
OUT = ROOT / "tools" / "_loc_en.json"


def unescape_csharp(s: str) -> str:
    out: list[str] = []
    i = 0
    while i < len(s):
        if s[i] == "\\" and i + 1 < len(s):
            n = s[i + 1]
            if n == "n":
                out.append("\n")
            elif n == "t":
                out.append("\t")
            elif n == "r":
                out.append("\r")
            elif n == '"':
                out.append('"')
            elif n == "\\":
                out.append("\\")
            else:
                out.append(n)
            i += 2
            continue
        out.append(s[i])
        i += 1
    return "".join(out)


def main() -> None:
    text = LOC.read_text(encoding="utf-8")
    m = re.search(
        r"private static readonly Dictionary<string, string> Fallback = new\(StringComparer\.Ordinal\)\s*\{(.*?)\n    \};",
        text,
        re.S,
    )
    if not m:
        raise SystemExit("Fallback dictionary not found")
    body = m.group(1)
    entries: dict[str, str] = {}
    i = 0
    while True:
        m2 = re.search(r"\[LocKeys\.(\w+)\]\s*=\s*", body[i:])
        if not m2:
            break
        key = m2.group(1)
        j = i + m2.end()
        if body.startswith('@"', j):
            j += 2
            buf: list[str] = []
            while j < len(body):
                if body.startswith('""', j):
                    buf.append('"')
                    j += 2
                elif body[j] == '"':
                    j += 1
                    break
                else:
                    buf.append(body[j])
                    j += 1
            val = "".join(buf)
        else:
            if body[j] != '"':
                raise SystemExit(f"Expected string start for {key} at {j}")
            j += 1
            buf = []
            while j < len(body):
                if body[j] == "\\" and j + 1 < len(body):
                    buf.append(body[j : j + 2])
                    j += 2
                elif body[j] == '"':
                    j += 1
                    break
                else:
                    buf.append(body[j])
                    j += 1
            val = unescape_csharp("".join(buf))
        entries[key] = val
        while j < len(body) and body[j] in " \t\r\n":
            j += 1
        if j < len(body) and body[j] == ",":
            j += 1
        i = j

    OUT.write_text(json.dumps(entries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {OUT} ({len(entries)} keys)")


if __name__ == "__main__":
    main()
