#!/usr/bin/env python3
"""Append es/ko/de/pt/it/ru slots onto day-observances.json (and rebuild script)."""
from __future__ import annotations

import json
import re
import sys
import time
from pathlib import Path

from deep_translator import GoogleTranslator

ROOT = Path(__file__).resolve().parents[1]
OUT_JSON = ROOT / "src" / "Ardel.Launcher" / "Assets" / "Data" / "day-observances.json"
SCRIPT = ROOT / "tools" / "rebuild_observances.py"
CACHE = ROOT / "tools" / "_observance_extra.json"

# Existing JSON: [en, zh-Hans, zh-Hant, ja, fr]
# Append: es, ko, de, pt, it, ru
LANGS = [("es", "es"), ("ko", "ko"), ("de", "de"), ("pt", "pt"), ("it", "it"), ("ru", "ru")]


def translate_names(names: list[str], google: str) -> list[str]:
    tr = GoogleTranslator(source="en", target=google)
    out: list[str] = []
    i = 0
    while i < len(names):
        batch = names[i : i + 25]
        for attempt in range(6):
            try:
                part = tr.translate_batch(batch)
                if not isinstance(part, list) or len(part) != len(batch):
                    raise RuntimeError(f"bad batch result {part!r}")
                out.extend(p if p else batch[j] for j, p in enumerate(part))
                break
            except Exception as ex:
                wait = min(2 ** attempt, 20)
                print(f"  [{google}] retry {attempt+1}: {ex!r}; sleep {wait}s", flush=True)
                time.sleep(wait)
        else:
            for n in batch:
                try:
                    out.append(tr.translate(n) or n)
                except Exception:
                    out.append(n)
                time.sleep(0.08)
        i += len(batch)
        print(f"  [{google}] {min(i, len(names))}/{len(names)}", flush=True)
        time.sleep(0.25)
    return out


def main() -> None:
    data = json.loads(OUT_JSON.read_text(encoding="utf-8"))
    keys = sorted(data.keys())
    sample = data[keys[0]]
    if len(sample) >= 11:
        print("JSON already has 11 slots", flush=True)
        return

    if len(sample) != 5:
        print(f"unexpected slot count {len(sample)}", file=sys.stderr)
        sys.exit(1)

    en_names = [data[k][0] for k in keys]
    extras: dict[str, list[str]] = {k: [] for k in keys}

    if CACHE.exists():
        cached = json.loads(CACHE.read_text(encoding="utf-8"))
        if set(cached.keys()) == set(keys) and all(len(cached[k]) == 6 for k in keys):
            print("using cache", flush=True)
            extras = cached
        else:
            print("cache incomplete, regenerating", flush=True)
            CACHE.unlink()

    if not extras[keys[0]]:
        for code, google in LANGS:
            print(f"translate → {code}", flush=True)
            results = translate_names(en_names, google)
            assert len(results) == len(keys)
            for k, r in zip(keys, results, strict=True):
                extras[k].append(r)
            CACHE.write_text(json.dumps(extras, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    for k in keys:
        data[k] = list(data[k][:5]) + extras[k]

    OUT_JSON.write_text(
        json.dumps(data, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
        newline="\n",
    )
    print(f"wrote {OUT_JSON} ({len(keys)} days, {len(data[keys[0]])} slots)", flush=True)

    # Patch rebuild_observances comment + make main() preserve/write 11 slots from JSON is hard;
    # instead rewrite DAYS tuples from final JSON for future rebuilds.
    lines = [
        "# key MMDD -> (en, zh-Hans, zh-Hant, ja, fr, es, ko, de, pt, it, ru)",
        "DAYS: dict[str, tuple[str, str, str, str, str, str, str, str, str, str, str]] = {",
    ]
    months = {
        "01": "January", "02": "February", "03": "March", "04": "April",
        "05": "May", "06": "June", "07": "July", "08": "August",
        "09": "September", "10": "October", "11": "November", "12": "December",
    }
    last = ""
    for k in keys:
        m = k[:2]
        if m != last:
            lines.append(f"    # {months.get(m, m)}")
            last = m
        vals = ", ".join(json.dumps(v, ensure_ascii=False) for v in data[k])
        lines.append(f'    "{k}": ({vals}),')
    lines.append("}")
    block = "\n".join(lines)

    text = SCRIPT.read_text(encoding="utf-8")
    text2, n = re.subn(
        r"# key MMDD ->.*?\nDAYS: dict\[str, tuple\[.*?\]\] = \{.*?\}",
        block,
        text,
        count=1,
        flags=re.S,
    )
    if n != 1:
        print("warning: could not rewrite DAYS in rebuild_observances.py", flush=True)
    else:
        SCRIPT.write_text(text2, encoding="utf-8", newline="\n")
        print("updated rebuild_observances.py", flush=True)


if __name__ == "__main__":
    main()
