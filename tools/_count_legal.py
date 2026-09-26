import re
from pathlib import Path

def count_sections(text):
    return len([b for b in text.strip().split("\n\n") if b.strip()])

root = Path(__file__).resolve().parent.parent
en = (root / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs").read_text(encoding="utf-8")
loc = (root / "src/Ardel.Launcher/Localization/AboutLegalNotice.Locales.cs").read_text(encoding="utf-8")
fr_body = (root / "src/Ardel.Launcher/Localization/_fr_body.txt").read_text(encoding="utf-8")

en_m = re.search(r'public const string English =\s*"""\s*(.+?)"""', en, re.DOTALL)
for name in ["French", "German", "Spanish", "Korean"]:
    m = re.search(rf'public const string {name} =\s*"""\s*(.+?)"""', loc, re.DOTALL)
    if m:
        t = m.group(1).strip()
        print(f"{name}: sections={count_sections(t)}, lines={len(t.splitlines())}")

print(f"_fr_body.txt: sections={count_sections(fr_body)}, lines={len(fr_body.splitlines())}")
print(f"English: sections={count_sections(en_m.group(1).strip())}")

for name in ["French", "German", "Spanish", "Korean"]:
    m = re.search(rf'public const string {name} =\s*"""\s*(.+?)"""', loc, re.DOTALL)
    start = loc.find(f'public const string {name}')
    end = loc.find('""";', start) + 5
    block = loc[start:end]
    print(f"{name} const block: {block.count(chr(10))+1} lines total")
