#!/usr/bin/env python3
"""Generate loc-translations/*.json patch files from audit lists."""
from __future__ import annotations

import json
import sys
from importlib.machinery import SourceFileLoader
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOC_DIR = ROOT / "src" / "Ardel.Launcher" / "Localization"
OUT_DIR = Path(__file__).resolve().parent / "loc-translations"

skip_mod = SourceFileLoader("skip", str(Path(__file__).resolve().parent / "loc-skip-keys.py")).load_module()
SKIP_KEYS = skip_mod.SKIP_KEYS

ADDS = {
    "de": {
        "Oobe_StepBreak_Title": "Wasserstation",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "Offline-Konto hinzufügen",
        "Oobe_StepAccount_Subtitle": "Optional — Sie können später weitere Konten hinzufügen.",
        "Oobe_StepHydrate_Title": "Wasser",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "Modpaketdateien werden aufgelöst…",
        "Modpack_MissingFile": "Fehlende Modpaketdatei: {0}",
    },
    "ko": {
        "Oobe_StepBreak_Title": "워터 바",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "오프라인 계정 추가",
        "Oobe_StepAccount_Subtitle": "선택 사항 — 나중에 계정 페이지에서 더 추가할 수 있습니다.",
        "Oobe_StepHydrate_Title": "물",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "모드팩 파일 확인 중…",
        "Modpack_MissingFile": "모드팩 파일이 없습니다: {0}",
    },
    "ru": {
        "Oobe_StepBreak_Title": "Водная станция",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "Добавить офлайн-аккаунт",
        "Oobe_StepAccount_Subtitle": "Необязательно — вы сможете добавить другие аккаунты позже.",
        "Oobe_StepHydrate_Title": "Вода",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "Разрешение файлов модпака…",
        "Modpack_MissingFile": "Отсутствует файл модпака: {0}",
    },
    "es": {
        "Oobe_StepBreak_Title": "Bar de agua",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "Añadir cuenta sin conexión",
        "Oobe_StepAccount_Subtitle": "Opcional — puede añadir más cuentas más tarde.",
        "Oobe_StepHydrate_Title": "Agua",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "Resolviendo archivos del modpack…",
        "Modpack_MissingFile": "Falta un archivo del modpack: {0}",
    },
    "it": {
        "Oobe_StepBreak_Title": "Angolo dell'acqua",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "Aggiungi un account offline",
        "Oobe_StepAccount_Subtitle": "Facoltativo — puoi aggiungere altri account in seguito.",
        "Oobe_StepHydrate_Title": "Acqua",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "Risoluzione dei file del modpack…",
        "Modpack_MissingFile": "File del modpack mancante: {0}",
    },
    "pt": {
        "Oobe_StepBreak_Title": "Bar de água",
        "Oobe_StepBreak_Body": "",
        "Oobe_StepAccount_Title": "Adicionar conta offline",
        "Oobe_StepAccount_Subtitle": "Opcional — pode adicionar mais contas mais tarde.",
        "Oobe_StepHydrate_Title": "Água",
        "Oobe_StepHydrate_Body": "",
        "Modpack_ResolvingFiles": "A resolver ficheiros do modpack…",
        "Modpack_MissingFile": "Ficheiro do modpack em falta: {0}",
    },
}

UPDATES: dict[str, dict[str, str]] = {
    "de": {
        "Account_KindOffline": "Ohne Anmeldung",
        "Catalog_DetailTitle": "Einzelheiten",
        "Download_JavaTag": "Java {0}",
        "Download_JavaTagPending": "Java …",
        "Download_SectionMinecraft": "Minecraft",
        "Download_SectionMod": "Mods",
        "Download_SectionModpack": "Modpaket",
        "Download_SectionSkins": "Skins",
        "FabricApi_BothFailed": "Modrinth: {0}; CurseForge: {1}",
        "Home_Version": "Version",
        "InstanceSettings_JvmPresetG1Gc": "G1GC (Vorgabe)",
        "InstanceSettings_JvmPresetShenandoah": "ShenandoahGC (Java 11 oder höher)",
        "InstanceSettings_JvmPresetZgc": "ZGC (Java 15 oder höher)",
        "InstanceSettings_NavMods": "Modifikationen",
        "InstanceSettings_SaveDifficultyNormal": "Normal",
        "InstanceSettings_SaveHardcore": "Hardcore",
        "InstanceSettings_SaveSpawn": "Spawnpunkt",
        "Java_NamedWithPath": "Java {0} — {1}",
        "Java_NamedWithSource": "Java {0} ({1})",
        "LoaderTag_Named": "{0} ({1})",
        "Mod_CategoryMobs": "Kreaturen",
        "Mod_CategoryRedstone": "Redstone",
        "Mod_DependencyOptional": "Nicht erforderlich",
        "Mod_DetailChannelAlpha": "Alpha",
        "Mod_DetailChannelBeta": "Beta",
        "Mod_DetailTitle": "Modifikation",
        "Mod_SearchCount": "{0} Mods",
        "Mod_SearchCountWithWarning": "{0} Mods — {1}",
        "Modpack_ViewMods": "Mods",
        "Nav_Download": "Herunterladen",
        "Nav_Skins": "Skins",
        "Settings_Java": "Java",
        "Settings_ThemeAurora": "Polarlicht",
        "Settings_ThemeObsidian": "Obsidian",
    },
    "ko": {
        "LoaderTag_Named": "{0}（{1}）",
    },
    "ru": {
        "Java_NamedWithSource": "Java\u202f{0}\u202f({1})",
        "LoaderTag_Named": "{0}\u202f({1})",
    },
    "es": {
        "Download_JavaTag": "Java {0}",
        "InstanceSettings_JvmPresetShenandoah": "ShenandoahGC (Java 11 o posterior)",
        "InstanceSettings_JvmPresetZgc": "ZGC (Java 15 o posterior)",
        "InstanceSettings_MemoryTotal": "en total",
        "InstanceSettings_SaveDifficultyNormal": "Normal",
        "InstanceSettings_SaveValueNo": "No",
        "Java_NamedWithPath": "Java {0} — {1}",
        "Java_NamedWithSource": "Java {0} ({1})",
        "LoaderTag_Named": "{0} ({1})",
        "Mod_DetailChannelBeta": "Beta",
        "Nav_Skins": "Aspectos",
        "Settings_Java": "Java",
        "Settings_ThemeAurora": "Aurora boreal",
    },
    "it": {
        "Download_JavaTag": "Java {0}",
        "Download_SectionMinecraft": "Minecraft",
        "Download_SectionMod": "Modifiche",
        "InstanceSettings_JvmPresetShenandoah": "ShenandoahGC (Java 11 o successivo)",
        "InstanceSettings_JvmPresetZgc": "ZGC (Java 15 o successivo)",
        "InstanceSettings_SaveValueNo": "No",
        "Java_NamedWithPath": "Java {0} — {1}",
        "Java_NamedWithSource": "Java {0} ({1})",
        "LoaderTag_Named": "{0} ({1})",
        "Mod_DetailChannelBeta": "Beta",
        "Mod_DetailTitle": "Modifica",
        "Nav_Account": "Profilo",
        "Nav_Download": "Scarica",
        "Nav_Play": "Inizio",
        "Progress_FileFallback": "archivio",
        "Settings_ThemeAurora": "Aurora boreale",
    },
    "pt": {
        "Catalog_KindModpack": "pacotes de mods",
        "Download_JavaTag": "Java {0}",
        "Download_SectionMinecraft": "Minecraft",
        "FabricApi_BothFailed": "Falha no Modrinth: {0}; CurseForge: {1}",
        "InstanceSettings_JvmPresetShenandoah": "ShenandoahGC (Java 11 ou superior)",
        "InstanceSettings_JvmPresetZgc": "ZGC (Java 15 ou superior)",
        "InstanceSettings_MemoryTotal": "no total",
        "Java_NamedWithPath": "Java {0} — {1}",
        "Java_NamedWithSource": "Java {0} ({1})",
        "LoaderTag_Named": "{0} ({1})",
        "Mod_CategoryRedstone": "Redstone",
        "Mod_DetailChannelBeta": "Beta",
        "Mod_DownloadsExact": "{0} transferências",
        "Mod_DownloadsThousands": "{0} mil transferências",
        "Modpack_ContentsCount": "{0} mods",
        "Modpack_ContentsSubtitle": "{0} · {1} mods",
        "Nav_Skins": "Aspectos",
        "Settings_Java": "Java",
        "Skin_Count": "{0} skins",
    },
}


def audit_keys(lang: str) -> list[str]:
    path = LOC_DIR / f"_audit_{lang}.txt"
    keys: list[str] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        key = line.split("\t", 1)[0]
        if key not in SKIP_KEYS:
            keys.append(key)
    return keys


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    counts: dict[str, tuple[int, int]] = {}
    for lang in ("de", "ko", "ru", "es", "it", "pt"):
        expected = audit_keys(lang)
        updates = UPDATES.get(lang, {})
        missing = [k for k in expected if k not in updates]
        extra = [k for k in updates if k not in expected]
        if missing:
            print(f"ERROR {lang}: missing updates for {missing}", file=sys.stderr)
            return 1
        if extra:
            print(f"WARN {lang}: extra updates not in audit: {extra}", file=sys.stderr)
        payload = {"update": {k: updates[k] for k in expected}, "add": ADDS[lang]}
        out = OUT_DIR / f"{lang}.json"
        out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        counts[lang] = (len(payload["update"]), len(payload["add"]))
        print(f"{lang}: {counts[lang][0]} updates, {counts[lang][1]} adds -> {out.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
