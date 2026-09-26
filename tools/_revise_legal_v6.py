# -*- coding: utf-8 -*-
"""Revise AboutLegalNotice to v6 (Nominatim + authcompleted) and export all website languages."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

LOC = Path(r"d:/Project/Ardel/src/Ardel.Launcher/Localization")
WEB = Path(r"D:/Project/Ardel-Website")
FILES = [
    LOC / "AboutLegalNotice.cs",
    LOC / "AboutLegalNotice.Locales.cs",
    LOC / "AboutLegalNotice.Locales2.cs",
]

AUTH = "https://ardel.ice-tea.top/authcompleted"
TERMS = "https://ardel.ice-tea.top/terms"

REPLACEMENTS = [
    # EN
    (
        "Weather and geocoding data may be supplied by services such as Open-Meteo.",
        "When the optional Home weather widget is enabled, forecast and place-search data may be requested from Open-Meteo, and reverse geocoding may use the OpenStreetMap Nominatim service; such requests may transmit place-name queries or approximate coordinates and your IP address to those operators.",
    ),
    (
        "Open-Meteo (weather and geocoding), GitHub (voluntary feedback via Issues), and authlib-injector (when offline skin relay is enabled).",
        f"Open-Meteo (optional Home weather and place search), OpenStreetMap Nominatim (optional reverse geocoding), the Developer's static website used for Microsoft sign-in completion ({AUTH}), GitHub (voluntary feedback via Issues), and authlib-injector (when offline skin relay is enabled).",
    ),
    (
        "Network communications with third parties occur only in connection with features you actively use (downloads, mirrors, weather, authentication), as described herein.",
        "Network communications with third parties occur only in connection with features you actively use (downloads, mirrors, optional weather/geocoding, Microsoft authentication and the sign-in completion page, and similar integrations), as described herein.",
    ),
    (
        "Where you enable features contacting third parties (Microsoft authentication, mirrors, Modrinth, weather APIs, etc.), such providers process data under their own policies and legal bases.",
        "Where you enable features contacting third parties (Microsoft authentication and the sign-in completion page, mirrors, Modrinth, optional weather/geocoding APIs, etc.), such providers process data under their own policies and legal bases.",
    ),
    (
        "material amendments take effect upon publication on the About page with an updated version number and effective date.",
        f"material amendments take effect upon publication on the About page and at {TERMS} with an updated version number and effective date.",
    ),
    # ZH
    (
        "天气及地理编码数据可能由 Open-Meteo 等服务提供。",
        "若您启用可选的主页天气小组件，预报与地点搜索数据可能向 Open-Meteo 请求，反向地理编码可能使用 OpenStreetMap Nominatim 服务；此类请求可能向相关运营方传输地点名称查询或近似坐标及您的 IP 地址。",
    ),
    (
        "Open-Meteo（天气与地理编码）、GitHub（通过 Issues 之自愿反馈）、authlib-injector（启用离线皮肤中继时）。",
        f"Open-Meteo（可选主页天气与地点搜索）、OpenStreetMap Nominatim（可选反向地理编码）、开发者用于 Microsoft 登录完成页之静态网站（{AUTH}）、GitHub（通过 Issues 之自愿反馈）、authlib-injector（启用离线皮肤中继时）。",
    ),
    (
        "与第三方之网络通信仅发生于用户实际使用之功能（如下载、镜像、天气、登录），详见上文。",
        "与第三方之网络通信仅发生于用户实际使用之功能（如下载、镜像、可选天气/地理编码、Microsoft 认证及登录完成页等集成），详见上文。",
    ),
    (
        "用户启用联系第三方之功能（Microsoft 登录、镜像、Modrinth、天气 API 等）时，相关数据处理由该等提供方依其自身政策及法律依据进行。",
        "用户启用联系第三方之功能（Microsoft 登录及登录完成页、镜像、Modrinth、可选天气/地理编码 API 等）时，相关数据处理由该等提供方依其自身政策及法律依据进行。",
    ),
    (
        "重大修订自关于页面公布并更新版本号及生效日期起生效，用户继续使用即构成对修订条款之接受。",
        f"重大修订自关于页面及 {TERMS} 公布并更新版本号及生效日期起生效，用户继续使用即构成对修订条款之接受。",
    ),
    # ZH-Hant
    (
        "天氣及地理編碼資料可能由 Open-Meteo 等服務提供。",
        "若您啟用可選的主頁天氣小組件，預報與地點搜尋資料可能向 Open-Meteo 請求，反向地理編碼可能使用 OpenStreetMap Nominatim 服務；此類請求可能向相關營運方傳輸地點名稱查詢或近似座標及您的 IP 位址。",
    ),
    (
        "Open-Meteo（天氣與地理編碼）、GitHub（透過 Issues 之自願反饋）、authlib-injector（啟用離線皮膚中繼時）。",
        f"Open-Meteo（可選主頁天氣與地點搜尋）、OpenStreetMap Nominatim（可選反向地理編碼）、開發者用於 Microsoft 登入完成頁之靜態網站（{AUTH}）、GitHub（透過 Issues 之自願反饋）、authlib-injector（啟用離線皮膚中繼時）。",
    ),
    (
        "與第三方之網路通訊僅發生於使用者實際使用之功能（如下載、映象、天氣、登入），詳見上文。",
        "與第三方之網路通訊僅發生於使用者實際使用之功能（如下載、映象、可選天氣/地理編碼、Microsoft 認證及登入完成頁等整合），詳見上文。",
    ),
    (
        "使用者啟用聯絡第三方之功能（Microsoft 登入、映象、Modrinth、天氣 API 等）時，相關資料處理由該等提供方依其自身政策及法律依據進行。",
        "使用者啟用聯絡第三方之功能（Microsoft 登入及登入完成頁、映象、Modrinth、可選天氣/地理編碼 API 等）時，相關資料處理由該等提供方依其自身政策及法律依據進行。",
    ),
    (
        "重大修訂自關於頁面公佈並更新版本號及生效日期起生效，使用者繼續使用即構成對修訂條款之接受。",
        f"重大修訂自關於頁面及 {TERMS} 公佈並更新版本號及生效日期起生效，使用者繼續使用即構成對修訂條款之接受。",
    ),
    # JA
    (
        "天気およびジオコーディングデータは、Open-Meteo などのサービスにより提供される場合があります。",
        "任意のホーム天気ウィジェットを有効にした場合、予報および場所検索データは Open-Meteo に要求され、逆ジオコーディングには OpenStreetMap Nominatim が使用される場合があります。当該リクエストにより、場所名クエリまたは概算座標およびお客様の IP アドレスが各事業者に送信される場合があります。",
    ),
    (
        "Open-Meteo（天気およびジオコーディング）、GitHub（Issues 経由の任意のフィードバック）、authlib-injector（オフラインスキン中継が有効な場合）が含まれます。",
        f"Open-Meteo（任意のホーム天気および場所検索）、OpenStreetMap Nominatim（任意の逆ジオコーディング）、Microsoft サインイン完了に用いる開発者の静的サイト（{AUTH}）、GitHub（Issues 経由の任意のフィードバック）、authlib-injector（オフラインスキン中継が有効な場合）が含まれます。",
    ),
    (
        "第三者とのネットワーク通信は、お客様が積極的に使用する機能（ダウンロード、ミラー、天気、認証）に関連してのみ発生し、本規約に記載されています。",
        "第三者とのネットワーク通信は、お客様が積極的に使用する機能（ダウンロード、ミラー、任意の天気/ジオコーディング、Microsoft 認証およびサインイン完了ページ等）に関連してのみ発生し、本規約に記載されています。",
    ),
    (
        "Microsoft 認証、ミラー、Modrinth、天気 API など第三者に接続する機能を有効にした場合、当該プロバイダーは、独自のポリシーおよび法的根拠の下でデータを処理します。",
        "Microsoft 認証およびサインイン完了ページ、ミラー、Modrinth、任意の天気/ジオコーディング API など第三者に接続する機能を有効にした場合、当該プロバイダーは、独自のポリシーおよび法的根拠の下でデータを処理します。",
    ),
    # FR
    (
        "Les données météorologiques et de géocodage peuvent être fournies par des services tels qu'Open-Meteo.",
        "Lorsque le widget météo facultatif de l'accueil est activé, les données de prévision et de recherche de lieux peuvent être demandées à Open-Meteo, et le géocodage inverse peut utiliser le service Nominatim d'OpenStreetMap ; ces requêtes peuvent transmettre des requêtes de noms de lieux ou des coordonnées approximatives ainsi que votre adresse IP aux opérateurs concernés.",
    ),
    (
        "Open-Meteo (météo et géocodage), GitHub (retour facultatif via Issues), authlib-injector (relais de skin hors ligne si activé).",
        f"Open-Meteo (météo et recherche de lieux facultatives sur l'accueil), OpenStreetMap Nominatim (géocodage inverse facultatif), le site statique du Développeur servant à l'achèvement de la connexion Microsoft ({AUTH}), GitHub (retour facultatif via Issues), authlib-injector (relais de skin hors ligne si activé).",
    ),
    # DE
    (
        "Wetter- und Geokodierungsdaten können von Diensten wie Open-Meteo bereitgestellt werden.",
        "Wenn das optionale Home-Wetter-Widget aktiviert ist, können Vorhersage- und Ortssuchdaten bei Open-Meteo angefordert werden, und Reverse-Geocoding kann den OpenStreetMap-Nominatim-Dienst nutzen; solche Anfragen können Ortsnamensuchen oder ungefähre Koordinaten sowie Ihre IP-Adresse an die jeweiligen Betreiber übermitteln.",
    ),
    (
        "Open-Meteo (Wetter und Geokodierung), GitHub (freiwilliges Feedback über Probleme) und authlib-injector (wenn Offline-Skin-Relay aktiviert ist).",
        f"Open-Meteo (optionales Home-Wetter und Ortssuche), OpenStreetMap Nominatim (optionales Reverse-Geocoding), die statische Website des Entwicklers zur Microsoft-Anmeldeabschlussseite ({AUTH}), GitHub (freiwilliges Feedback über Probleme) und authlib-injector (wenn Offline-Skin-Relay aktiviert ist).",
    ),
    # ES
    (
        "Los datos meteorológicos y de codificación geográfica pueden ser proporcionados por servicios como Open-Meteo.",
        "Cuando el widget meteorológico opcional de inicio está activado, los datos de previsión y búsqueda de lugares pueden solicitarse a Open-Meteo, y la geocodificación inversa puede usar el servicio Nominatim de OpenStreetMap; tales solicitudes pueden transmitir consultas de nombres de lugar o coordenadas aproximadas y su dirección IP a los operadores correspondientes.",
    ),
    (
        "Open-Meteo (clima y codificación geográfica), GitHub (comentarios voluntarios a través de Problemas) y authlib-injector (cuando la retransmisión de máscara sin conexión está habilitada).",
        f"Open-Meteo (clima y búsqueda de lugares opcionales en inicio), OpenStreetMap Nominatim (geocodificación inversa opcional), el sitio estático del Desarrollador usado para completar el inicio de sesión de Microsoft ({AUTH}), GitHub (comentarios voluntarios a través de Problemas) y authlib-injector (cuando la retransmisión de máscara sin conexión está habilitada).",
    ),
    # KO
    (
        "날씨 및 지오코딩 데이터는 Open-Meteo와 같은 서비스를 통해 제공될 수 있습니다.",
        "선택적 홈 날씨 위젯을 사용하면 예보 및 장소 검색 데이터가 Open-Meteo에 요청될 수 있고, 역지오코딩에는 OpenStreetMap Nominatim 서비스가 사용될 수 있으며, 이러한 요청은 장소명 쿼리 또는 대략적 좌표와 IP 주소를 해당 운영자에게 전송할 수 있습니다.",
    ),
    (
        "Open-Meteo(날씨 및 지오코딩), GitHub(문제를 통한 자발적 피드백) 및 authlib-injector(오프라인 스킨 릴레이가 활성화된 경우)",
        f"Open-Meteo(선택적 홈 날씨 및 장소 검색), OpenStreetMap Nominatim(선택적 역지오코딩), Microsoft 로그인 완료에 사용되는 개발자의 정적 웹사이트({AUTH}), GitHub(문제를 통한 자발적 피드백) 및 authlib-injector(오프라인 스킨 릴레이가 활성화된 경우)",
    ),
    # IT
    (
        "I dati meteo e di geocodifica possono essere forniti da servizi quali Open-Meteo.",
        "Quando il widget meteo facoltativo della Home è attivo, i dati di previsione e di ricerca luoghi possono essere richiesti a Open-Meteo, e la geocodifica inversa può usare il servizio Nominatim di OpenStreetMap; tali richieste possono trasmettere query di nomi di luoghi o coordinate approssimative e il vostro indirizzo IP ai rispettivi operatori.",
    ),
    (
        "Open-Meteo (meteo e geocodifica), GitHub (feedback volontario tramite Issues) e authlib-injector (quando il relay skin offline è abilitato).",
        f"Open-Meteo (meteo e ricerca luoghi facoltativi sulla Home), OpenStreetMap Nominatim (geocodifica inversa facoltativa), il sito statico dello Sviluppatore usato per il completamento dell'accesso Microsoft ({AUTH}), GitHub (feedback volontario tramite Issues) e authlib-injector (quando il relay skin offline è abilitato).",
    ),
    # PT
    (
        "Dados meteorológicos e de geocodificação podem ser fornecidos por serviços como o Open-Meteo.",
        "Quando o widget de clima opcional da página inicial estiver ativado, dados de previsão e busca de locais podem ser solicitados ao Open-Meteo, e a geocodificação inversa pode usar o serviço Nominatim do OpenStreetMap; tais solicitações podem transmitir consultas de nomes de locais ou coordenadas aproximadas e seu endereço IP aos respectivos operadores.",
    ),
    (
        "Open-Meteo (meteorologia e geocodificação), GitHub (feedback voluntário via Issues) e authlib-injector (quando o relé de skin offline está ativado).",
        f"Open-Meteo (clima e busca de locais opcionais na página inicial), OpenStreetMap Nominatim (geocodificação inversa opcional), o site estático do Desenvolvedor usado para a conclusão do login Microsoft ({AUTH}), GitHub (feedback voluntário via Issues) e authlib-injector (quando o relé de skin offline está ativado).",
    ),
    # RU
    (
        "Данные о погоде и геокодировании могут предоставляться сервисами, такими как Open-Meteo.",
        "При включении необязательного виджета погоды на главной странице данные прогноза и поиска мест могут запрашиваться у Open-Meteo, а обратное геокодирование может использовать сервис OpenStreetMap Nominatim; такие запросы могут передавать запросы названий мест или приблизительные координаты и ваш IP-адрес соответствующим операторам.",
    ),
    (
        "Open-Meteo (погода и геокодирование), GitHub (добровольная обратная связь через Issues) и authlib-injector (при включённом офлайн-ретрансляторе скинов).",
        f"Open-Meteo (необязательная погода и поиск мест на главной), OpenStreetMap Nominatim (необязательное обратное геокодирование), статический сайт Разработчика для завершения входа Microsoft ({AUTH}), GitHub (добровольная обратная связь через Issues) и authlib-injector (при включённом офлайн-ретрансляторе скинов).",
    ),
]

META = {
    "en": {"pageTitle": "Terms of Service", "brand": "Ardel", "meta": "Version 6 · Effective 2026-09-11", "note": "These Terms of Service also appear in Ardel Desktop under Settings → About.", "const": "English", "htmlLang": "en", "label": "EN"},
    "zh": {"pageTitle": "服务条款", "brand": "Ardel", "meta": "版本 6 · 生效日期 2026-09-11", "note": "本服务条款与 Ardel Desktop「设置 → 关于」中的文本一致。", "const": "Chinese", "htmlLang": "zh-Hans", "label": "简中"},
    "zh-hant": {"pageTitle": "服務條款", "brand": "Ardel", "meta": "版本 6 · 生效日期 2026-09-11", "note": "本服務條款與 Ardel Desktop「設定 → 關於」中的文字一致。", "const": "ChineseTraditional", "htmlLang": "zh-Hant", "label": "繁中"},
    "ja": {"pageTitle": "利用規約", "brand": "Ardel", "meta": "バージョン 6 · 発効日 2026-09-11", "note": "本利用規約は Ardel Desktop の「設定 → バージョン情報」にも表示されます。", "const": "Japanese", "htmlLang": "ja", "label": "日本語"},
    "fr": {"pageTitle": "Conditions d'utilisation", "brand": "Ardel", "meta": "Version 6 · Entrée en vigueur 2026-09-11", "note": "Ces conditions figurent aussi dans Ardel Desktop sous Paramètres → À propos.", "const": "French", "htmlLang": "fr", "label": "FR"},
    "de": {"pageTitle": "Nutzungsbedingungen", "brand": "Ardel", "meta": "Version 6 · Gültig ab 2026-09-11", "note": "Diese Bedingungen erscheinen auch in Ardel Desktop unter Einstellungen → Info.", "const": "German", "htmlLang": "de", "label": "DE"},
    "es": {"pageTitle": "Términos de servicio", "brand": "Ardel", "meta": "Versión 6 · Vigente desde 2026-09-11", "note": "Estos términos también aparecen en Ardel Desktop en Configuración → Acerca de.", "const": "Spanish", "htmlLang": "es", "label": "ES"},
    "ko": {"pageTitle": "서비스 약관", "brand": "Ardel", "meta": "버전 6 · 시행일 2026-09-11", "note": "본 약관은 Ardel Desktop의 설정 → 정보에도 표시됩니다.", "const": "Korean", "htmlLang": "ko", "label": "한국어"},
    "it": {"pageTitle": "Termini di servizio", "brand": "Ardel", "meta": "Versione 6 · In vigore dal 2026-09-11", "note": "Questi termini compaiono anche in Ardel Desktop in Impostazioni → Informazioni.", "const": "Italian", "htmlLang": "it", "label": "IT"},
    "pt": {"pageTitle": "Termos de serviço", "brand": "Ardel", "meta": "Versão 6 · Vigente desde 2026-09-11", "note": "Estes termos também aparecem no Ardel Desktop em Configurações → Sobre.", "const": "Portuguese", "htmlLang": "pt", "label": "PT"},
    "ru": {"pageTitle": "Условия использования", "brand": "Ardel", "meta": "Версия 6 · Действует с 2026-09-11", "note": "Эти условия также отображаются в Ardel Desktop: Параметры → О программе.", "const": "Russian", "htmlLang": "ru", "label": "RU"},
}


def log(msg: str) -> None:
    sys.stdout.buffer.write((msg + "\n").encode("utf-8", errors="replace"))
    sys.stdout.buffer.flush()


def extract_consts(text: str) -> dict[str, str]:
    return {
        m.group(1): m.group(2)
        for m in re.finditer(r'public const string (\w+)\s*=\s*"""\r?\n(.*?)""";', text, re.S)
    }


def parse_sections(raw: str) -> list[dict[str, str]]:
    sections = []
    for block in re.split(r"(?:\r?\n){2,}", raw.strip()):
        lines = [ln.strip() for ln in block.splitlines() if ln.strip()]
        if len(lines) < 2:
            continue
        sections.append({"title": lines[0], "body": " ".join(lines[1:])})
    return sections


def main() -> None:
    misses = []
    hits = 0
    for path in FILES:
        text = path.read_text(encoding="utf-8")
        original = text
        for old, new in REPLACEMENTS:
            if old in text:
                text = text.replace(old, new)
                hits += 1
            else:
                # Only record miss if this file could contain it (heuristic: first 20 chars)
                if any(ch in text for ch in old[:8] if ch.isalpha()):
                    misses.append(f"{path.name} :: {old[:80]}")
        if path.name == "AboutLegalNotice.cs":
            text2, n = re.subn(r"public const int Version = \d+;", "public const int Version = 6;", text, count=1)
            text = text2
            hits += n
        if text != original:
            path.write_text(text, encoding="utf-8", newline="\n")
            log(f"Updated {path.name}")
        else:
            log(f"No write {path.name}")

    log(f"hits={hits} candidate_misses={len(misses)}")
    Path(r"d:/Project/Ardel/tools/_revise_legal_v6_misses.txt").write_text(
        "\n".join(misses), encoding="utf-8"
    )

    merged = {}
    for path in FILES:
        merged.update(extract_consts(path.read_text(encoding="utf-8")))

    for name, body in merged.items():
        log(f"{name}: Nominatim={'Y' if 'Nominatim' in body else 'N'} authcompleted={'Y' if 'authcompleted' in body else 'N'}")

    payload = {
        "version": 6,
        "effectiveDate": "2026-09-11",
        "languages": [{"id": k, "label": v["label"], "htmlLang": v["htmlLang"]} for k, v in META.items()],
    }
    for lang, meta in META.items():
        body = merged[meta["const"]]
        payload[lang] = {
            "pageTitle": meta["pageTitle"],
            "brand": meta["brand"],
            "meta": meta["meta"],
            "note": meta["note"],
            "sections": parse_sections(body),
        }
        log(f"{lang}: {len(payload[lang]['sections'])} sections")

    out = WEB / "assets" / "terms-content.json"
    out.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    log(f"Wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
