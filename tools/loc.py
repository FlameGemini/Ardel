#!/usr/bin/env python3
"""Keep Ardel localization catalogs and .resw files in sync.

Source of truth (runtime):
  LocKeys.cs          — key list
  Loc.cs Fallback     — English (en-US)
  Loc.Zh.cs           — 简体中文
  Loc.ZhHant.cs       — 繁體中文
  Loc.Ja.cs           — 日本語
  Loc.Fr.cs           — Français
  Loc.EnUk.cs         — British spelling overrides only (not a full catalog)

Generated:
  Strings/<culture>/Resources.resw

Usage:
  python tools/loc.py check          # report gaps; exit 1 if incomplete
  python tools/loc.py sync           # stub missing keys from English + rewrite resw
  python tools/loc.py unused         # LocKeys not referenced outside Loc*
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC_DIR = ROOT / "src" / "Ardel.Launcher" / "Localization"
LAUNCHER = ROOT / "src" / "Ardel.Launcher"
RESW_DIR = LAUNCHER / "Strings"

# Full catalogs must contain every LocKeys entry.
FULL_CATALOGS = [
    {
        "id": "en-US",
        "file": LOC_DIR / "Loc.cs",
        "dict": "Fallback",
        "resw": "en-US",
    },
    {
        "id": "zh-CN",
        "file": LOC_DIR / "Loc.Zh.cs",
        "dict": "Chinese",
        "resw": "zh-CN",
    },
    {
        "id": "zh-Hant",
        "file": LOC_DIR / "Loc.ZhHant.cs",
        "dict": "ChineseTraditional",
        "resw": None,
    },
    {
        "id": "ja-JP",
        "file": LOC_DIR / "Loc.Ja.cs",
        "dict": "Japanese",
        "resw": "ja-JP",
    },
    {
        "id": "fr",
        "file": LOC_DIR / "Loc.Fr.cs",
        "dict": "French",
        "resw": None,
    },
]

OVERRIDE_CATALOG = {
    "id": "en-UK",
    "file": LOC_DIR / "Loc.EnUk.cs",
    "dict": "EnglishUk",
}

# Language display names stay in their native script in every catalog.
SKIP_UNTRANSLATED = {
    "Brand_Name",
    "Settings_LanguageEnglish",
    "Settings_LanguageChinese",
    "Settings_LanguageJapanese",
    "Settings_LanguageEnglishUS",
    "Settings_LanguageEnglishUK",
    "Settings_LanguageChineseTraditional",
    "Settings_LanguageFrench",
    "FabricApi_SourceModrinth",
    "FabricApi_SourceCurseForge",
    "Install_LoaderFabric",
    "Install_LoaderForge",
    "Install_LoaderNeoForge",
    "Install_LoaderOptiFine",
    "About_LicenseName",
    "Version_NeoForge",
    "Version_OptiFine",
    "FabricApi_BothFailed",
    "InstanceSettings_JvmPresetShenandoah",
    "InstanceSettings_JvmPresetZgc",
    "InstanceSettings_JvmPresetGraalVm",
}

ENTRY_RE = re.compile(
    r"\[LocKeys\.(\w+)\]\s*=\s*((?:\"(?:\\.|[^\"\\])*\")(?:\s*\+\s*\"(?:\\.|[^\"\\])*\")*|AboutLegalNotice\.\w+)\s*,",
    re.S,
)

KEYS_RE = re.compile(r"public const string (\w+) =")

RESW_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""


def csharp_unescape(literal: str) -> str:
    """Decode one C# regular string body (no surrounding quotes)."""
    out: list[str] = []
    i = 0
    mapping = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\", '"': '"', "'": "'"}
    while i < len(literal):
        if literal[i] == "\\" and i + 1 < len(literal):
            nxt = literal[i + 1]
            out.append(mapping.get(nxt, nxt))
            i += 2
            continue
        out.append(literal[i])
        i += 1
    return "".join(out)


def csharp_escape(value: str) -> str:
    return (
        value.replace("\\", "\\\\")
        .replace('"', '\\"')
        .replace("\r", "\\r")
        .replace("\n", "\\n")
        .replace("\t", "\\t")
    )


def xml_escape(value: str) -> str:
    return (
        value.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )


def parse_concat_string(expr: str) -> str:
    if expr.startswith("AboutLegalNotice."):
        return "(Legal notice text)"
    parts = re.findall(r'"((?:\\.|[^"\\])*)"', expr)
    return "".join(csharp_unescape(p) for p in parts)


def load_keys() -> list[str]:
    text = (LOC_DIR / "LocKeys.cs").read_text(encoding="utf-8")
    keys = KEYS_RE.findall(text)
    if not keys:
        raise SystemExit("No LocKeys found")
    return keys


def parse_catalog(path: Path) -> dict[str, str]:
    text = path.read_text(encoding="utf-8")
    out: dict[str, str] = {}
    for match in ENTRY_RE.finditer(text):
        out[match.group(1)] = parse_concat_string(match.group(2))
    return out


def insert_missing_entries(path: Path, missing: list[tuple[str, str]]) -> None:
    """Append missing [LocKeys.X] = "..." entries before the catalog dictionary close."""
    text = path.read_text(encoding="utf-8")
    matches = list(ENTRY_RE.finditer(text))
    if not matches:
        raise SystemExit(f"No LocKeys entries in {path}")

    last = matches[-1]
    insert_at = last.end()
    chunk = "".join(
        f'\n        [LocKeys.{key}] = "{csharp_escape(value)}",' for key, value in missing
    )
    path.write_text(text[:insert_at] + chunk + text[insert_at:], encoding="utf-8")


def write_resw(culture: str, keys: list[str], catalog: dict[str, str]) -> None:
    folder = RESW_DIR / culture
    folder.mkdir(parents=True, exist_ok=True)
    lines = [RESW_HEADER.rstrip("\n")]
    for key in keys:
        value = catalog.get(key)
        if value is None:
            continue
        lines.append(f'  <data name="{key}" xml:space="preserve">')
        lines.append(f"    <value>{xml_escape(value)}</value>")
        lines.append("  </data>")
    lines.append("</root>")
    lines.append("")
    (folder / "Resources.resw").write_text("\n".join(lines), encoding="utf-8", newline="\n")


def parse_resw(path: Path) -> set[str]:
    text = path.read_text(encoding="utf-8")
    return set(re.findall(r'<data name="([^"]+)"', text))


def looks_untranslated(key: str, english: str, other: str) -> bool:
    if key in SKIP_UNTRANSLATED:
        return False
    if other != english:
        return False
    letters = sum(ch.isascii() and ch.isalpha() for ch in english)
    return letters >= 8 and " " in english


def collect() -> tuple[list[str], dict[str, dict[str, str]], dict[str, str]]:
    keys = load_keys()
    catalogs: dict[str, dict[str, str]] = {}
    for spec in FULL_CATALOGS:
        catalogs[spec["id"]] = parse_catalog(spec["file"])
    catalogs[OVERRIDE_CATALOG["id"]] = parse_catalog(OVERRIDE_CATALOG["file"])
    english = catalogs["en-US"]
    return keys, catalogs, english


def cmd_check(strict_unused: bool = False) -> int:
    keys, catalogs, english = collect()
    keyset = set(keys)
    problems = 0

    print(f"LocKeys: {len(keys)}")
    print()

    print("=== Missing keys (full catalogs) ===")
    for spec in FULL_CATALOGS:
        missing = [k for k in keys if k not in catalogs[spec["id"]]]
        extra = sorted(set(catalogs[spec["id"]]) - keyset)
        print(f"{spec['id']}: missing {len(missing)}, extra {len(extra)}")
        for k in missing:
            print(f"  - {k}")
            problems += 1
        for k in extra:
            print(f"  extra {k}")
            problems += 1

    uk = catalogs[OVERRIDE_CATALOG["id"]]
    uk_extra = sorted(set(uk) - keyset)
    print(f"en-UK overrides: {len(uk)} (ok if partial), extra {len(uk_extra)}")
    for k in uk_extra:
        print(f"  extra {k}")
        problems += 1
    print()

    print("=== resw vs LocKeys ===")
    for spec in FULL_CATALOGS:
        culture = spec["resw"]
        if not culture:
            continue
        path = RESW_DIR / culture / "Resources.resw"
        if not path.exists():
            print(f"{culture}: missing file")
            problems += 1
            continue
        resw_keys = parse_resw(path)
        missing = [k for k in keys if k not in resw_keys]
        extra = sorted(resw_keys - keyset)
        print(f"{culture}: missing {len(missing)}, extra {len(extra)}")
        for k in missing[:40]:
            print(f"  - {k}")
            problems += 1
        if len(missing) > 40:
            print(f"  ... {len(missing) - 40} more")
            problems += len(missing) - 40
        for k in extra:
            print(f"  extra {k}")
            problems += 1
    print()

    print("=== Same as English (likely untranslated) ===")
    for spec in FULL_CATALOGS:
        if spec["id"] == "en-US":
            continue
        hits = [
            k
            for k in keys
            if k in catalogs[spec["id"]]
            and k in english
            and looks_untranslated(k, english[k], catalogs[spec["id"]][k])
        ]
        print(f"{spec['id']}: {len(hits)}")
        for k in hits:
            print(f"  ~ {k}: {english[k][:80]}")
    print()

    print("=== CJK in English / French (UI chrome must not be Chinese) ===")
    cjk = re.compile(r"[\u4e00-\u9fff]")
    for spec_id in ("en-US", "fr"):
        hits = [
            k
            for k in keys
            if k not in SKIP_UNTRANSLATED and cjk.search(catalogs[spec_id].get(k, ""))
        ]
        print(f"{spec_id}: {len(hits)}")
        for k in hits:
            print(f"  ! {k}: {catalogs[spec_id][k][:80]}")
            problems += 1
    print()

    print("=== Identical to English (review for real translation) ===")
    for spec in FULL_CATALOGS:
        if spec["id"] == "en-US":
            continue
        hits = sorted(
            k
            for k in keys
            if k in catalogs[spec["id"]]
            and k in english
            and catalogs[spec["id"]][k] == english[k]
            and k not in SKIP_UNTRANSLATED
        )
        print(f"{spec['id']}: {len(hits)}")
        for k in hits[:40]:
            print(f"  = {k}: {english[k][:80]}")
        if len(hits) > 40:
            print(f"  ... {len(hits) - 40} more")
    print()

    unused = find_unused(keys)
    print(f"=== Unused LocKeys (not referenced outside Loc*) ===")
    print(f"count {len(unused)}")
    for k in unused:
        print(f"  {k}")
        if strict_unused:
            problems += 1

    if problems:
        print()
        print(f"FAILED: {problems} issue(s). Run: python tools/loc.py sync")
        return 1

    print()
    print("OK — catalogs and resw match LocKeys.")
    return 0


def find_unused(keys: list[str]) -> list[str]:
    contents: dict[Path, str] = {}
    for path in LAUNCHER.rglob("*"):
        if path.suffix not in {".cs", ".xaml"}:
            continue
        if "\\obj\\" in str(path) or "/obj/" in str(path):
            continue
        if "\\bin\\" in str(path) or "/bin/" in str(path):
            continue
        try:
            contents[path] = path.read_text(encoding="utf-8")
        except OSError:
            continue

    unused: list[str] = []
    for name in keys:
        hit = False
        for path, text in contents.items():
            base = path.name
            if base.startswith("Loc.") or base == "LocKeys.cs":
                continue
            if (
                f"LocKeys.{name}" in text
                or f"Key={name}" in text
                or f'Key="{name}"' in text
            ):
                hit = True
                break
        if not hit:
            unused.append(name)
    return unused


def cmd_sync() -> int:
    keys, catalogs, english = collect()
    missing_en = [k for k in keys if k not in english]
    if missing_en:
        print("English Fallback is missing keys — add them to Loc.cs first:")
        for k in missing_en:
            print(f"  {k}")
        return 1

    changed = False
    for spec in FULL_CATALOGS:
        catalog = catalogs[spec["id"]]
        missing = [(k, english[k]) for k in keys if k not in catalog]
        if missing:
            insert_missing_entries(spec["file"], missing)
            print(f"{spec['id']}: stubbed {len(missing)} key(s) from English")
            changed = True
            catalogs[spec["id"]] = parse_catalog(spec["file"])

    for spec in FULL_CATALOGS:
        culture = spec["resw"]
        if not culture:
            continue
        write_resw(culture, keys, catalogs[spec["id"]])
        print(f"wrote Strings/{culture}/Resources.resw ({len(keys)} keys)")

    if not changed:
        print("catalogs already complete; resw regenerated")
    return 0


def cmd_identical() -> int:
    keys, catalogs, english = collect()
    for spec in FULL_CATALOGS:
        if spec["id"] == "en-US":
            continue
        hits = sorted(
            k
            for k in keys
            if k in catalogs[spec["id"]]
            and catalogs[spec["id"]][k] == english.get(k)
            and k not in SKIP_UNTRANSLATED
        )
        print(f"{spec['id']}: {len(hits)} identical to en-US")
        for k in hits:
            print(f"  {k}: {english[k]}")
        print()
    return 0


def cmd_unused() -> int:
    keys = load_keys()
    unused = find_unused(keys)
    print(f"unused {len(unused)}")
    for k in unused:
        print(k)
    return 0


def main() -> int:
    os.chdir(ROOT)
    parser = argparse.ArgumentParser(description="Ardel localization sync")
    parser.add_argument(
        "command",
        choices=["check", "sync", "unused", "identical"],
        help="check catalogs, sync resw / stub missing, list unused, or list identical-to-English keys",
    )
    parser.add_argument(
        "--strict-unused",
        action="store_true",
        help="treat unused LocKeys as check failures",
    )
    args = parser.parse_args()
    if args.command == "check":
        return cmd_check(strict_unused=args.strict_unused)
    if args.command == "sync":
        return cmd_sync()
    if args.command == "identical":
        return cmd_identical()
    return cmd_unused()


if __name__ == "__main__":
    sys.exit(main())
