#!/usr/bin/env python3
"""Generate zh-Hant translations from zh-CN via OpenCC."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

import opencc

ROOT = Path(__file__).resolve().parents[1]
LOC_DIR = ROOT / "src" / "Ardel.Launcher" / "Localization"
OUT = Path(__file__).resolve().parent / "loc-translations" / "zh-Hant.json"
sys.path.insert(0, str(Path(__file__).resolve().parent))
from loc_skip_keys import SKIP_KEYS  # noqa: E402


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
    zh = parse_catalog(LOC_DIR / "Loc.Zh.cs")
    hant = parse_catalog(LOC_DIR / "Loc.ZhHant.cs")
    cc = opencc.OpenCC("s2t")

    updates: dict[str, str] = {}
    for key, hant_val in hant.items():
        if key in SKIP_KEYS:
            continue
        if key not in fb or hant_val != fb[key]:
            continue
        if key in zh and zh[key] != fb[key]:
            updates[key] = cc.convert(zh[key])
        elif key.startswith("Oobe_"):
            continue

    # Manual zh-Hant overrides where s2t differs from TW usage
    overrides = {
        "Settings_LanguageChinese": "簡體中文",
        "Nav_Instances": "實例",
        "InstanceSettings_FolderScreenshots": "screenshots",
        "Software": "軟體",
    }
    updates.update({k: v for k, v in overrides.items() if k in hant})

    adds = {
        "Oobe_StepBreak_Title": "水吧",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "新增離線帳戶",
        "Oobe_StepAccount_Subtitle": "可選 — 之後可在帳戶頁新增更多。",
        "Oobe_StepHydrate_Title": "水",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "正在解析整合包檔案…",
        "Modpack_MissingFile": "缺少整合包檔案：{0}",
    }

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"update": updates, "add": adds}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"zh-Hant: {len(updates)} updates, {len(adds)} adds -> {OUT}")


if __name__ == "__main__":
    main()
