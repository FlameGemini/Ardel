import re
from pathlib import Path

base = Path(__file__).resolve().parents[1] / "src/Ardel.Launcher/Localization"
for f in ["AboutLegalNotice.cs", "AboutLegalNotice.Locales.cs", "AboutLegalNotice.Locales2.cs"]:
    s = (base / f).read_text(encoding="utf-8")
    for name in re.findall(r"public const string (\w+) =", s):
        m = re.search(
            rf'public const string {name} =\s+"""(.*?)""";',
            s,
            re.S,
        )
        if m:
            print(f"{f}:{name}={len(m.group(1))}")
