# -*- coding: utf-8 -*-
from pathlib import Path

root = Path("src/Ardel.Launcher/Localization")
markers = [
    "Edition scope",
    "版本范围",
    "版本範圍",
    "エディション範囲",
    "Portée d'édition",
    "Editionsumfang",
    "Alcance de edición",
    "에디션 범위",
    "Ambito di edizione",
    "Âmbito da edição",
    "Область изданий",
]
for p in sorted(root.glob("AboutLegalNotice*.cs")):
    t = p.read_text(encoding="utf-8")
    bad = ("网易" in t) or ("我的世界" in t)
    found = [m for m in markers if m in t]
    print(p.name, "bad=", bad, "markers=", len(found))
