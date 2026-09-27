# -*- coding: utf-8 -*-
"""Builds a standalone single-file Ardel.exe."""
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
LAUNCHER_BIN = SRC_DIR / "Ardel.Launcher" / "bin" / "x64" / "Release" / "net8.0-windows10.0.19041.0"
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
        "-p:PublishReadyToRun=false",
        "-p:UseSharedCompilation=false",
        "-o", str(launcher_pub_dir)
    ])

    launcher_exe = launcher_pub_dir / "Ardel.Launcher.exe"
    if not launcher_exe.exists():
        print(f"Error: {launcher_exe} does not exist.", file=sys.stderr)
        return 1

    time.sleep(1.0)

    print("=== Step 2: Compressing runtime payload ===")
    payload_zip = BOOTSTRAPPER_DIR / "payload.zip"
    if payload_zip.exists():
        payload_zip.unlink()

    with zipfile.ZipFile(payload_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for file in launcher_pub_dir.rglob("*"):
            if file.is_file():
                # Avoid packing huge pdb symbols to keep single-file exe compact
                if file.suffix.lower() == ".pdb":
                    continue
                arcname = file.relative_to(launcher_pub_dir)
                zf.write(file, arcname)

    payload_size_mb = payload_zip.stat().st_size / (1024 * 1024)
    print(f"Payload created: {payload_zip.name} ({payload_size_mb:.2f} MB)")

    print("=== Step 3: Publishing Ardel Single-File Executable ===")
    if SINGLE_OUT.exists():
        shutil.rmtree(SINGLE_OUT, ignore_errors=True)
    SINGLE_OUT.mkdir(parents=True, exist_ok=True)

    run_cmd([
        "dotnet", "publish", str(BOOTSTRAPPER_PROJ),
        "-c", "Release",
        "-r", "win-x64",
        "-p:PublishSingleFile=true",
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

    # Clean up intermediate payload.zip
    if payload_zip.exists():
        payload_zip.unlink()

    exe_size_mb = dest_exe.stat().st_size / (1024 * 1024)
    print("=======================================================")
    print(f"SUCCESS! Single-File Executable generated:")
    print(f"-> {dest_exe} ({exe_size_mb:.2f} MB)")
    print(f"-> You can place this single Ardel.exe into ANY folder with .minecraft and run directly!")
    print("=======================================================")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
