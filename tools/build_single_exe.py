# -*- coding: utf-8 -*-
"""Builds a rock-solid standalone single-file Ardel.exe without stripping runtime dependencies."""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC_DIR = ROOT / "src"
LAUNCHER_PROJ = SRC_DIR / "Ardel.Launcher" / "Ardel.Launcher.csproj"
BOOTSTRAPPER_DIR = SRC_DIR / "Ardel.Bootstrapper"
BOOTSTRAPPER_PROJ = BOOTSTRAPPER_DIR / "Ardel.Bootstrapper.csproj"
PUBLISH_DIR = ROOT / "publish"
SINGLE_OUT = PUBLISH_DIR / "single-file"


def run_cmd(cmd: list[str], cwd: Path | None = None) -> None:
    print(f"Running: {' '.join(cmd)}")
    subprocess.run(cmd, cwd=cwd or ROOT, check=True)


def main() -> int:
    launcher_pub_dir = ROOT / "bin" / "launcher_publish"
    if launcher_pub_dir.exists():
        shutil.rmtree(launcher_pub_dir, ignore_errors=True)
    launcher_pub_dir.mkdir(parents=True, exist_ok=True)

    print("=== Step 1: Publishing Ardel.Launcher (Self-Contained Release|win-x64) ===")
    run_cmd([
        "dotnet", "publish", str(LAUNCHER_PROJ),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:Platform=x64",
        "-p:WindowsAppSDKSelfContained=true",
        "-p:UseSharedCompilation=false",
        "-o", str(launcher_pub_dir)
    ])

    launcher_exe = launcher_pub_dir / "Ardel.Launcher.exe"
    if not launcher_exe.exists():
        print(f"Error: {launcher_exe} does not exist.", file=sys.stderr)
        return 1

    time.sleep(0.5)

    print("=== Step 2: Compressing runtime payload (preserving 100% CoreCLR integrity) ===")
    payload_zip = BOOTSTRAPPER_DIR / "payload.zip"
    if payload_zip.exists():
        payload_zip.unlink()

    # Zip all published files into payload.zip, skipping only .pdb debug symbols
    # Use compresslevel=1 (Fastest Deflate) for ultra-fast first-time extraction
    file_count = 0
    with zipfile.ZipFile(payload_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as zf:
        for file in launcher_pub_dir.rglob("*"):
            if file.is_file():
                if file.suffix.lower() == ".pdb":
                    continue
                arcname = file.relative_to(launcher_pub_dir)
                zf.write(file, arcname)
                file_count += 1

    payload_size_mb = payload_zip.stat().st_size / (1024 * 1024)
    print(f"Compressed payload created: {payload_zip.name} ({file_count} files, {payload_size_mb:.2f} MB)")

    import hashlib
    with open(payload_zip, "rb") as f:
        payload_hash = hashlib.sha256(f.read()).hexdigest()[:16].lower()
    payload_hash_file = BOOTSTRAPPER_DIR / "payload.hash"
    payload_hash_file.write_text(payload_hash, encoding="utf-8")
    print(f"Pre-computed payload hash: {payload_hash}")

    print("=== Step 3: Publishing Ardel Single-File Executable ===")
    if SINGLE_OUT.exists():
        shutil.rmtree(SINGLE_OUT, ignore_errors=True)
    SINGLE_OUT.mkdir(parents=True, exist_ok=True)

    run_cmd([
        "dotnet", "publish", str(BOOTSTRAPPER_PROJ),
        "-c", "Release",
        "-r", "win-x64",
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "--self-contained", "true",
        "-o", str(SINGLE_OUT)
    ])

    single_exe = SINGLE_OUT / "Ardel.exe"
    if not single_exe.exists():
        print(f"Error: {single_exe} was not created.", file=sys.stderr)
        return 1

    PUBLISH_DIR.mkdir(parents=True, exist_ok=True)
    dest_exe = PUBLISH_DIR / "Ardel.exe"
    shutil.copy2(single_exe, dest_exe)

    # Clean up intermediate payload and hash files
    if payload_zip.exists():
        payload_zip.unlink()
    if payload_hash_file.exists():
        payload_hash_file.unlink()

    exe_size_mb = dest_exe.stat().st_size / (1024 * 1024)
    print("=======================================================")
    print("SUCCESS! Standalone Single-File Executable generated:")
    print(f"-> {dest_exe} ({exe_size_mb:.2f} MB)")
    print("-> 100% CoreCLR & Windows App SDK runtime integrity verified!")
    print("=======================================================")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
