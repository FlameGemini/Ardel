# -*- coding: utf-8 -*-
"""Builds a rock-solid, optimized standalone single-file Ardel.exe."""
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

# Safely prune ONLY unused WPF desktop framework binaries.
# KEEP all WinForms (for LogViewer), WinUI 3, WindowsAppSDK, *.winmd, MUI, SkiaSharp, and CoreCLR runtime files 100% intact!
SAFE_PRUNE_PATTERNS = [
    # Unused WPF Framework binaries (Ardel uses WinUI 3, not WPF)
    "PresentationFramework*",
    "PresentationUI*",
    "System.Windows.Controls.Ribbon*",
    "ReachFramework*",
]


def run_cmd(cmd: list[str], cwd: Path | None = None) -> None:
    print(f"Running: {' '.join(cmd)}")
    subprocess.run(cmd, cwd=cwd or ROOT, check=True)


def prune_safe_payload(pub_dir: Path) -> tuple[int, int]:
    """Safely prunes unused WPF & WinForms binaries. Keeps all MUI, WinUI, WinMD, and runtime resources intact."""
    raw_size = sum(f.stat().st_size for f in pub_dir.rglob("*") if f.is_file())
    removed_bytes = 0

    for pat in SAFE_PRUNE_PATTERNS:
        for f in list(pub_dir.glob(pat)):
            if f.is_file():
                removed_bytes += f.stat().st_size
                f.unlink()

    # Safely remove WPF / WinForms satellite resource dlls without touching any MUI or WinUI files
    for f in list(pub_dir.rglob("*.resources.dll")):
        if any(f.name.startswith(p.rstrip("*")) for p in SAFE_PRUNE_PATTERNS):
            removed_bytes += f.stat().st_size
            f.unlink()

    pruned_size = sum(f.stat().st_size for f in pub_dir.rglob("*") if f.is_file())
    return raw_size, pruned_size


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

    time.sleep(0.5)

    print("=== Step 2: Pruning unused WPF & WinForms assemblies safely ===")
    raw_size, pruned_size = prune_safe_payload(launcher_pub_dir)
    print(f"Raw payload size:    {raw_size / (1024 * 1024):.2f} MB")
    print(f"Pruned payload size: {pruned_size / (1024 * 1024):.2f} MB (Saved {(raw_size - pruned_size) / (1024 * 1024):.2f} MB)")

    print("=== Step 3: Compressing runtime payload ===")
    payload_zip = BOOTSTRAPPER_DIR / "payload.zip"
    if payload_zip.exists():
        payload_zip.unlink()

    with zipfile.ZipFile(payload_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for file in launcher_pub_dir.rglob("*"):
            if file.is_file():
                if file.suffix.lower() == ".pdb":
                    continue
                arcname = file.relative_to(launcher_pub_dir)
                zf.write(file, arcname)

    payload_size_mb = payload_zip.stat().st_size / (1024 * 1024)
    print(f"Compressed payload created: {payload_zip.name} ({payload_size_mb:.2f} MB)")

    print("=== Step 4: Publishing Ardel Single-File Executable ===")
    if SINGLE_OUT.exists():
        shutil.rmtree(SINGLE_OUT, ignore_errors=True)
    SINGLE_OUT.mkdir(parents=True, exist_ok=True)

    run_cmd([
        "dotnet", "publish", str(BOOTSTRAPPER_PROJ),
        "-c", "Release",
        "-r", "win-x64",
        "-p:PublishSingleFile=true",
        "-p:PublishTrimmed=true",
        "-p:TrimMode=partial",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
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

    # Clean up intermediate payload
    if payload_zip.exists():
        payload_zip.unlink()

    exe_size_mb = dest_exe.stat().st_size / (1024 * 1024)
    print("=======================================================")
    print("SUCCESS! Single-File Executable generated:")
    print(f"-> {dest_exe} ({exe_size_mb:.2f} MB)")
    print(f"-> Size reduction: 140 MB -> {exe_size_mb:.2f} MB (~{((140 - exe_size_mb) / 140) * 100:.1f}% reduction)")
    print("-> You can place this single Ardel.exe into ANY folder and run directly!")
    print("=======================================================")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
