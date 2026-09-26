"""Fix raw string indentation in AboutLegalNotice.cs locale blocks."""
import re
from pathlib import Path

path = Path(__file__).resolve().parents[1] / "src/Ardel.Launcher/Localization/AboutLegalNotice.cs"
text = path.read_text(encoding="utf-8")

INDENT = "        "  # 8 spaces to match closing """


def fix_block(match: re.Match) -> str:
    prefix = match.group(1)
    body = match.group(2)
    suffix = match.group(3)
    lines = body.split("\n")
    fixed = []
    for line in lines:
        if line.strip() == "":
            fixed.append("")
        else:
            stripped = line.lstrip()
            fixed.append(INDENT + stripped)
    return prefix + "\n".join(fixed) + suffix


pattern = re.compile(
    r'(public const string (?:English|Chinese|ChineseTraditional) =\s+"""\n)(.*?)(\n        """;)',
    re.S,
)
text = pattern.sub(fix_block, text)
path.write_text(text, encoding="utf-8")
print("Fixed indentation for EN/ZH/ZH-Hant blocks")
