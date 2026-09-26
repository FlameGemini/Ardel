# -*- coding: utf-8 -*-
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

PATCHES = [
    (
        ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales.cs",
        "        로컬 데이터 저장 및 휴대형 배포",
        "        에디션 범위 및 침해 방지",
        """        에디션 범위 및 침해 방지
        본 소프트웨어는 공식 Mojang 및 Microsoft 채널을 통해 배포되는 Minecraft: Java Edition만을 대상으로 합니다. 다른 퍼블리셔가 운영하는 지역 독점 라이선스 Minecraft 에디션에 대한 실행, 인증 또는 상호운용 기능을 제공하지 않습니다. 저작권 침해, 게임 파일의 무단 배포, 소유권 검증 우회, 또는 무단 공개 게임 서버의 상업적 운영을 유도·조장·용이하게 하기 위해 본 소프트웨어를 사용해서는 안 됩니다. 오프라인 표시 이름 및 관련 편의 기능은 라이선스가 요구되는 경우의 정식 사본을 대체하지 않습니다. 문서, 마케팅 및 UI는 본 소프트웨어를 무단으로 Minecraft를 취득하는 수단으로 제시해서는 안 됩니다. 인스턴스의 "서버 참가" 필드(있는 경우)는 사용자가 입력한 주소를 클라이언트에 전달할 뿐이며 개발자가 운영하는 매치메이킹 또는 호스팅을 구성하지 않습니다. 법이 허용하는 범위에서 개발자는 불법 남용을 인지한 경우 기능을 변경·중단·비활성화할 수 있습니다. 본 약관은 특정 사용이 귀하의 관할권에서 합법이라는 법률 의견을 구성하지 않으며, 관련 법 준수는 귀하의 책임입니다.

        로컬 데이터 저장 및 휴대형 배포""",
    ),
    (
        ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales2.cs",
        "        Archiviazione locale dei dati e distribuzione portatile",
        "        Ambito di edizione e prevenzione delle violazioni",
        """        Ambito di edizione e prevenzione delle violazioni
        Il Software è destinato esclusivamente a Minecraft: Java Edition distribuito tramite i canali ufficiali Mojang e Microsoft. Non fornisce funzioni di avvio, autenticazione o interoperabilità per edizioni Minecraft con licenza esclusiva regionale gestite da altri editori. Non devi usare il Software per indurre, incoraggiare o facilitare violazioni del copyright, distribuzione non autorizzata di file di gioco, elusione della verifica di proprietà, né l'esercizio commerciale di server pubblici non autorizzati. I nomi offline e le funzioni di comodità correlate non sostituiscono una copia debitamente licenziata ove richiesta. Documentazione, marketing e interfaccia non devono presentare il Software come mezzo per ottenere Minecraft senza autorizzazione. I campi istanza "unisci al server", se presenti, trasmettono soltanto un indirizzo da te fornito al client e non costituiscono matchmaking né hosting gestito dallo Sviluppatore. Nella misura consentita dalla legge, lo Sviluppatore può modificare, sospendere o disabilitare funzioni se viene a conoscenza di abusi illeciti. I presenti Termini non costituiscono un parere legale secondo cui un uso particolare sia lecito nella tua giurisdizione; la conformità alla legge applicabile resta tua responsabilità.

        Archiviazione locale dei dati e distribuzione portatile""",
    ),
    (
        ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales2.cs",
        "        Armazenamento local de dados e implementação portátil",
        "        Âmbito da edição e prevenção de infrações",
        """        Âmbito da edição e prevenção de infrações
        O Software destina-se exclusivamente ao Minecraft: Java Edition distribuído através dos canais oficiais da Mojang e da Microsoft. Não fornece funções de arranque, autenticação ou interoperabilidade para qualquer edição de Minecraft com licença exclusiva regional operada por outros editores. Não deve utilizar o Software para induzir, incentivar ou facilitar infrações de direitos de autor, distribuição não autorizada de ficheiros do jogo, contorno da verificação de propriedade, nem a exploração comercial de servidores públicos não autorizados. Nomes offline e funcionalidades de conveniência relacionadas não substituem uma cópia devidamente licenciada quando a licença é necessária. Documentação, marketing e interface não devem apresentar o Software como meio de obter Minecraft sem autorização. Os campos de instância "entrar no servidor", se existirem, apenas passam um endereço que fornece ao cliente e não constituem matchmaking nem alojamento operado pelo Desenvolvedor. Na medida permitida por lei, o Desenvolvedor pode modificar, suspender ou desativar funcionalidades ao tomar conhecimento de abusos ilícitos. Estes Termos não constituem parecer jurídico de que determinada utilização seja lícita na sua jurisdição; o cumprimento da lei aplicável continua a ser da sua responsabilidade.

        Armazenamento local de dados e implementação portátil""",
    ),
    (
        ROOT / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales2.cs",
        "        Локальное хранение данных и портативное развёртывание",
        "        Область изданий и предотвращение нарушений",
        """        Область изданий и предотвращение нарушений
        Программное обеспечение предназначено исключительно для Minecraft: Java Edition, распространяемой через официальные каналы Mojang и Microsoft. Оно не предоставляет запуск, аутентификацию или взаимодействие для каких-либо регионально эксклюзивных лицензионных изданий Minecraft, управляемых другими издателями. Вы не должны использовать Программное обеспечение для побуждения, поощрения или содействия нарушению авторских прав, нелицензированному распространению игровых файлов, обходу проверки владения или коммерческой эксплуатации неавторизованных публичных игровых серверов. Офлайн-имена и связанные удобства не заменяют надлежащим образом лицензированную копию, когда лицензия требуется. Документация, маркетинг и интерфейс не должны представлять Программное обеспечение как способ получить Minecraft без разрешения. Поля экземпляра «подключиться к серверу», если есть, лишь передают указанный вами адрес клиенту и не являются управляемым Разработчиком матчмейкингом или хостингом. В пределах, допускаемых законом, Разработчик может изменять, приостанавливать или отключать функции при выявлении незаконного злоупотребления. Настоящие Условия не являются юридическим заключением о законности конкретного использования в вашей юрисдикции; соблюдение применимого права остаётся вашей обязанностью.

        Локальное хранение данных и портативное развёртывание""",
    ),
]


def main() -> None:
    for path, needle, marker, block in PATCHES:
        text = path.read_text(encoding="utf-8")
        if marker in text:
            print("SKIP", path.name)
            continue
        if needle not in text:
            print("MISSING", path.name)
            continue
        path.write_text(text.replace(needle, block, 1), encoding="utf-8")
        print("OK", path.name)


if __name__ == "__main__":
    main()
