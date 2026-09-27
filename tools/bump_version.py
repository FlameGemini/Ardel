# -*- coding: utf-8 -*-
"""Ardel version and update manifest manager."""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WEBSITE_DIR = ROOT.parent / "Ardel-Website"
VERSION_JSON = WEBSITE_DIR / "version.json"


def compute_sha256(file_path: Path) -> str:
    sha = hashlib.sha256()
    with file_path.open("rb") as f:
        while chunk := f.read(1024 * 1024):
            sha.update(chunk)
    return sha.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description="Update Ardel version.json and optionally deploy to Cloudflare Pages.")
    parser.add_argument("--version", "-v", required=True, help="New version string (e.g. 1.0.1)")
    parser.add_argument("--notes-zh", nargs="+", help="Changelog bullet points in Simplified Chinese")
    parser.add_argument("--notes-en", nargs="+", help="Changelog bullet points in English")
    parser.add_argument("--zip-file", type=Path, help="Path to local win-x64 .zip release artifact to compute sha256 & size")
    parser.add_argument("--deploy", action="store_true", help="Automatically deploy to Cloudflare Pages after updating")

    args = parser.parse_args()

    if not VERSION_JSON.exists():
        print(f"Error: {VERSION_JSON} not found.", file=sys.stderr)
        return 1

    with VERSION_JSON.open("r", encoding="utf-8") as f:
        data = json.load(f)

    new_ver = args.version.lstrip("v")
    data["version"] = new_ver
    data["releaseDate"] = date.today().isoformat()

    zip_filename = f"Ardel-v{new_ver}-win-x64.zip"
    github_base = "https://github.com/FlameGemini/Ardel/releases/download"
    data["downloads"]["windows_x64_zip"]["name"] = zip_filename
    data["downloads"]["windows_x64_zip"]["url"] = f"{github_base}/v{new_ver}/{zip_filename}"
    data["downloads"]["windows_x64_zip"]["mirrorUrl"] = f"https://mirror.ghproxy.com/{github_base}/v{new_ver}/{zip_filename}"

    if args.zip_file and args.zip_file.exists():
        data["downloads"]["windows_x64_zip"]["sha256"] = compute_sha256(args.zip_file)
        data["downloads"]["windows_x64_zip"]["size"] = args.zip_file.stat().st_size
        print(f"Computed SHA-256 for {args.zip_file.name}: {data['downloads']['windows_x64_zip']['sha256']}")

    if args.notes_zh:
        data["changelog"]["zh"] = args.notes_zh
    if args.notes_en:
        data["changelog"]["en"] = args.notes_en

    with VERSION_JSON.open("w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print(f"Successfully updated {VERSION_JSON} to v{new_ver}")

    if args.deploy:
        print("Deploying to Cloudflare Pages via wrangler...")
        cmd = ["npx", "wrangler", "pages", "deploy", ".", "--project-name=ardel"]
        subprocess.run(cmd, cwd=WEBSITE_DIR, shell=True, check=True)
        print("Deploy complete!")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
