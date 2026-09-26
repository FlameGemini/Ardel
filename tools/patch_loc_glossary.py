#!/usr/bin/env python3
"""Apply curated UI glossary overrides on translated Loc JSON files, then re-emit."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EN = json.loads((ROOT / "tools" / "_loc_en.json").read_text(encoding="utf-8"))

# key -> {lang: value}. Only overrides; missing langs keep MT.
GLOSSARY: dict[str, dict[str, str]] = {
    "Nav_Play": {
        "es": "Inicio",
        "ko": "홈",
        "de": "Startseite",
        "pt": "Início",
        "it": "Home",
        "ru": "Главная",
    },
    "Nav_Download": {
        "es": "Descargar",
        "ko": "다운로드",
        "de": "Download",
        "pt": "Baixar",
        "it": "Download",
        "ru": "Загрузки",
    },
    "Nav_Instances": {
        "es": "Perfiles",
        "ko": "프로필",
        "de": "Profile",
        "pt": "Perfis",
        "it": "Profili",
        "ru": "Профили",
    },
    "Nav_Settings": {
        "es": "Configuración",
        "ko": "설정",
        "de": "Einstellungen",
        "pt": "Configurações",
        "it": "Impostazioni",
        "ru": "Настройки",
    },
    "Nav_Account": {
        "es": "Cuenta",
        "ko": "계정",
        "de": "Konto",
        "pt": "Conta",
        "it": "Account",
        "ru": "Аккаунт",
    },
    "Nav_Skins": {
        "es": "Skins",
        "ko": "스킨",
        "de": "Skins",
        "pt": "Skins",
        "it": "Skin",
        "ru": "Скины",
    },
    "Nav_About": {
        "es": "Acerca de",
        "ko": "정보",
        "de": "Info",
        "pt": "Sobre",
        "it": "Informazioni",
        "ru": "О программе",
    },
    "Action_Launch": {
        "es": "Iniciar",
        "ko": "실행",
        "de": "Starten",
        "pt": "Iniciar",
        "it": "Avvia",
        "ru": "Запустить",
    },
    "Action_Cancel": {
        "es": "Cancelar",
        "ko": "취소",
        "de": "Abbrechen",
        "pt": "Cancelar",
        "it": "Annulla",
        "ru": "Отмена",
    },
    "Action_Download": {
        "es": "Descargar",
        "ko": "다운로드",
        "de": "Herunterladen",
        "pt": "Baixar",
        "it": "Scarica",
        "ru": "Скачать",
    },
    "Action_Refresh": {
        "es": "Actualizar",
        "ko": "새로고침",
        "de": "Aktualisieren",
        "pt": "Atualizar",
        "it": "Aggiorna",
        "ru": "Обновить",
    },
    "Action_Search": {
        "es": "Buscar",
        "ko": "검색",
        "de": "Suchen",
        "pt": "Pesquisar",
        "it": "Cerca",
        "ru": "Поиск",
    },
    "Action_Reset": {
        "es": "Restablecer",
        "ko": "초기화",
        "de": "Zurücksetzen",
        "pt": "Redefinir",
        "it": "Reimposta",
        "ru": "Сбросить",
    },
    "Action_Save": {
        "es": "Guardar",
        "ko": "저장",
        "de": "Speichern",
        "pt": "Salvar",
        "it": "Salva",
        "ru": "Сохранить",
    },
    "Action_Rescan": {
        "es": "Volver a escanear",
        "ko": "다시 검색",
        "de": "Erneut scannen",
        "pt": "Verificar de novo",
        "it": "Riscansiona",
        "ru": "Пересканировать",
    },
    "Action_Browse": {
        "es": "Examinar…",
        "ko": "찾아보기…",
        "de": "Durchsuchen…",
        "pt": "Procurar…",
        "it": "Sfoglia…",
        "ru": "Обзор…",
    },
    "Action_OpenFolder": {
        "es": "Abrir carpeta",
        "ko": "폴더 열기",
        "de": "Ordner öffnen",
        "pt": "Abrir pasta",
        "it": "Apri cartella",
        "ru": "Открыть папку",
    },
    "Action_Delete": {
        "es": "Eliminar",
        "ko": "삭제",
        "de": "Löschen",
        "pt": "Excluir",
        "it": "Elimina",
        "ru": "Удалить",
    },
    "Action_Back": {
        "es": "Atrás",
        "ko": "뒤로",
        "de": "Zurück",
        "pt": "Voltar",
        "it": "Indietro",
        "ru": "Назад",
    },
    "Action_Apply": {
        "es": "Aplicar",
        "ko": "적용",
        "de": "Übernehmen",
        "pt": "Aplicar",
        "it": "Applica",
        "ru": "Применить",
    },
    "Action_Close": {
        "es": "Cerrar",
        "ko": "닫기",
        "de": "Schließen",
        "pt": "Fechar",
        "it": "Chiudi",
        "ru": "Закрыть",
    },
    "Action_ClearSelection": {
        "es": "Borrar selección",
        "ko": "선택 해제",
        "de": "Auswahl aufheben",
        "pt": "Limpar seleção",
        "it": "Deseleziona",
        "ru": "Снять выбор",
    },
    "Home_GreetingEarlyMorning": {
        "es": "Buenos días",
        "ko": "좋은 아침",
        "de": "Guten Morgen",
        "pt": "Bom dia",
        "it": "Buongiorno",
        "ru": "Доброе утро",
    },
    "Home_GreetingMorning": {
        "es": "Buenos días",
        "ko": "좋은 아침",
        "de": "Guten Morgen",
        "pt": "Bom dia",
        "it": "Buongiorno",
        "ru": "Доброе утро",
    },
    "Home_GreetingNoon": {
        "es": "Buen mediodía",
        "ko": "좋은 점심",
        "de": "Guten Mittag",
        "pt": "Boa tarde",
        "it": "Buon mezzogiorno",
        "ru": "Добрый день",
    },
    "Home_GreetingAfternoon": {
        "es": "Buenas tardes",
        "ko": "좋은 오후",
        "de": "Guten Tag",
        "pt": "Boa tarde",
        "it": "Buon pomeriggio",
        "ru": "Добрый день",
    },
    "Home_GreetingEvening": {
        "es": "Buenas noches",
        "ko": "좋은 저녁",
        "de": "Guten Abend",
        "pt": "Boa noite",
        "it": "Buonasera",
        "ru": "Добрый вечер",
    },
    "Home_LaunchGame": {
        "es": "Iniciar juego",
        "ko": "게임 시작",
        "de": "Spiel starten",
        "pt": "Iniciar jogo",
        "it": "Avvia gioco",
        "ru": "Запустить игру",
    },
    "Home_Tagline": {
        "es": "Inicia Minecraft",
        "ko": "Minecraft 실행",
        "de": "Minecraft starten",
        "pt": "Inicie o Minecraft",
        "it": "Avvia Minecraft",
        "ru": "Запуск Minecraft",
    },
    "Home_Version": {
        "es": "Versión",
        "ko": "버전",
        "de": "Version",
        "pt": "Versão",
        "it": "Versione",
        "ru": "Версия",
    },
    "Home_Ready": {
        "es": "Listo",
        "ko": "준비됨",
        "de": "Bereit",
        "pt": "Pronto",
        "it": "Pronto",
        "ru": "Готово",
    },
    "Home_NoAccount": {
        "es": "Selecciona una cuenta",
        "ko": "계정 선택",
        "de": "Konto auswählen",
        "pt": "Selecione uma conta",
        "it": "Seleziona un account",
        "ru": "Выберите аккаунт",
    },
    "Home_ManageProfiles": {
        "es": "Administrar perfiles",
        "ko": "프로필 관리",
        "de": "Profile verwalten",
        "pt": "Gerenciar perfis",
        "it": "Gestisci profili",
        "ru": "Управление профилями",
    },
    "Startup_Loading": {
        "es": "Iniciando…",
        "ko": "시작 중…",
        "de": "Wird gestartet…",
        "pt": "Iniciando…",
        "it": "Avvio…",
        "ru": "Запуск…",
    },
    "Settings_Language": {
        "es": "Idioma",
        "ko": "언어",
        "de": "Sprache",
        "pt": "Idioma",
        "it": "Lingua",
        "ru": "Язык",
    },
    "Settings_LanguageSystem": {
        "es": "Predeterminado del sistema",
        "ko": "시스템 기본값",
        "de": "Systemstandard",
        "pt": "Padrão do sistema",
        "it": "Predefinita di sistema",
        "ru": "Системный",
    },
    "Settings_LanguageRestartHint": {
        "es": "Se aplica de inmediato en el launcher.",
        "ko": "런처에 바로 적용됩니다.",
        "de": "Wird sofort im Launcher übernommen.",
        "pt": "Aplica-se imediatamente no launcher.",
        "it": "Si applica subito nel launcher.",
        "ru": "Применяется сразу в лаунчере.",
    },
    "Settings_Theme": {
        "es": "Tema",
        "ko": "테마",
        "de": "Design",
        "pt": "Tema",
        "it": "Tema",
        "ru": "Тема",
    },
    "Settings_General": {
        "es": "General",
        "ko": "일반",
        "de": "Allgemein",
        "pt": "Geral",
        "it": "Generale",
        "ru": "Общие",
    },
}

FORCE_LITERAL = {
    "Brand_Name": "Ardel",
    "Settings_LanguageEnglish": "English (US)",
    "Settings_LanguageEnglishUS": "English (US)",
    "Settings_LanguageEnglishUK": "English (UK)",
    "Settings_LanguageChinese": "简体中文",
    "Settings_LanguageChineseTraditional": "繁體中文",
    "Settings_LanguageJapanese": "日本語",
    "Settings_LanguageFrench": "Français",
    "Settings_LanguageSpanish": "Español",
    "Settings_LanguageKorean": "한국어",
    "Settings_LanguageGerman": "Deutsch",
    "Settings_LanguagePortuguese": "Português (Brasil)",
    "Settings_LanguageItalian": "Italiano",
    "Settings_LanguageRussian": "Русский",
}


def main() -> None:
    langs = ["es", "ko", "de", "pt", "it", "ru"]
    for code in langs:
        path = ROOT / "tools" / f"_loc_{code}.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        for k, v in FORCE_LITERAL.items():
            if k in EN:
                data[k] = v
        for key, by_lang in GLOSSARY.items():
            if key in EN and code in by_lang:
                data[key] = by_lang[code]
        ordered = {k: data[k] for k in EN}
        path.write_text(json.dumps(ordered, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"patched {code}")

    for code in langs:
        r = subprocess.run(
            [sys.executable, str(ROOT / "tools" / "emit_loc_catalog.py"), code, str(ROOT / "tools" / f"_loc_{code}.json")],
            check=True,
            cwd=ROOT,
        )
        _ = r


if __name__ == "__main__":
    main()
