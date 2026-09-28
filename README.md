<div align="center">

<img src="src/Ardel.Launcher/Assets/ardel-logo.png" alt="Ardel Logo" width="128" height="128" />

# Ardel

**Modern, High-Performance Minecraft Launcher for Windows**

*Crafted with C# / .NET 8 / WinUI 3 (Windows App SDK) & CmlLib.Core*

<br />

<p align="center">
  <a href="https://github.com/FlameGemini/Ardel/releases/latest">
    <img src="https://img.shields.io/badge/Release-v1.5.2-007ACC?style=for-the-badge&logo=github&logoColor=white" alt="Latest Release" />
  </a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0">
    <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8" />
  </a>
  <a href="https://learn.microsoft.com/en-us/windows/apps/winui/winui3/">
    <img src="https://img.shields.io/badge/UI-WinUI%203-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="WinUI 3" />
  </a>
  <a href="https://www.microsoft.com/windows">
    <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows11&logoColor=white" alt="Platform" />
  </a>
  <a href="https://github.com/FlameGemini/Ardel/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/License-OSL--3.0-2EA44F?style=for-the-badge" alt="License" />
  </a>
  <a href="https://ardel.ice-tea.top">
    <img src="https://img.shields.io/badge/Website-ardel.ice--tea.top-E36209?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Official Website" />
  </a>
</p>

<p align="center">
  <a href="#overview">Overview</a> &bull;
  <a href="#key-features">Key Features</a> &bull;
  <a href="#downloads--installation">Downloads</a> &bull;
  <a href="#building-from-source">Building from Source</a> &bull;
  <a href="#project-structure">Architecture</a> &bull;
  <a href="#license">License</a>
</p>

</div>

---

## Overview

Ardel is an open-source, client-side Minecraft launcher engineered for speed, stability, and aesthetic excellence on modern Windows. Built from the ground up on native **WinUI 3** and **.NET 8**, Ardel avoids heavyweight web runtimes and Electron overhead to deliver sub-second cold starts, minimal system resource consumption, comprehensive version isolation, and frictionless multi-account management.

---

## Key Features

### Native Windows 11 & Fluent Design
- **Mica Alt Backdrop**: Native Windows 11 Mica Alt material integration with dynamic acrylic fallback on Windows 10.
- **Crafted Theme System**: 9 bespoke palettes (*Arctic, Aurora, Cedar, Cinder, Honey, Moss, Obsidian, Peach, Twilight*) with seamless dark/light mode switching.
- **Sub-Second Hydration**: Asynchronous initialization with deferred non-critical network requests for instantaneous UI responsiveness.

### Complete Version & Loader Ecosystem
- **All Minecraft Editions**: Seamless support for Official Releases, Snapshots, Beta, Alpha, and April Fools historical releases.
- **Automated Loader Provisioning**: One-click install for **Forge**, **NeoForge**, **Fabric**, **Quilt**, and **OptiFine**.
- **Strict Version Isolation**: Independent mod directories, configuration trees, world saves, resource packs, and shader packs per version instance.
- **High-Speed Acceleration**: Intelligent fallback and download acceleration via the BMCLAPI community mirror network.

### Authentication & Player Identity
- **Official Microsoft OAuth2**: Direct, secure cloud sign-in via Microsoft Authentication Library (MSAL) and Xbox Live services.
- **Skin & Cape Synchronization**: Real-time retrieval and preview of official player skins (Classic 4px / Slim 3px) and Mojang capes.
- **Offline Custom Skins**: Built-in 3D skin head renderer and local texture preview for custom offline profiles.
- **Gamertag Cooldown Management**: In-app Microsoft username availability checks with 30-day cooldown tracking.

### Modpack & Addon Center
- **Cross-Platform Indexing**: Integrated search, filtering, and installation from **Modrinth** and **CurseForge** APIs.
- **Standard Archive Support**: One-click import and export of `.mrpack` (Modrinth) and CurseForge modpack packages.
- **World & Save Diagnostics**: Level data parser, world seed inspection, game mode toggles, and world snapshot restoration.

### Reliability & Diagnostic Tools
- **Standalone Log Viewer (`Ardel.LogViewer`)**: Independent diagnostic tool that survives game crashes and launcher restarts, with real-time log streaming and regex filtering.
- **Smart Java Locator**: Automated detection and capability validation across JRE/JDK 8 through Java 25 with custom JVM parameter optimization.
- **Crash Root-Cause Analysis**: Automatic stack trace decoding to identify faulting mods and library conflicts.

### Internationalization
Full localization support across 11 languages with real-time UI switching:
`English`, `简体中文`, `繁體中文`, `日本語`, `Français`, `Deutsch`, `Español`, `Italiano`, `한국어`, `Português`, `Русский`.

---

## Downloads & Installation

### Option 1: Standalone Portable Executable (Recommended)
Download the single-file portable release `Ardel.exe` from the [Releases](https://github.com/FlameGemini/Ardel/releases) page.
- No installer required.
- Place `Ardel.exe` into any folder and run directly.
- All game files and settings are safely stored alongside the launcher in `.minecraft/`.

### Option 2: Web Installer
Download `ArdelSetup.exe` from the official website at [https://ardel.ice-tea.top](https://ardel.ice-tea.top).

---

## System Requirements

| Specification | Minimum | Recommended |
| :--- | :--- | :--- |
| **Operating System** | Windows 10 Version 1809 (Build 17763) | Windows 11 Version 22H2 or higher |
| **Architecture** | x64 / ARM64 | x64 / ARM64 |
| **Runtime** | Self-contained in release binaries | .NET 8 Desktop Runtime (for source builds) |
| **Graphics** | DirectX 11 compatible GPU | DirectX 12 compatible GPU |
| **Java** | Java 8 (for legacy MC) | Java 17 / 21+ (for modern MC) |

---

## Building from Source

### Prerequisites
- Visual Studio 2022 (v17.8+) with **.NET desktop development** workload, or Visual Studio Code with the C# Dev Kit.
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (x64 / ARM64).
- [Node.js](https://nodejs.org/) (v18+) for building embedded voice assets.

### 1. Clone Repository
```powershell
git clone https://github.com/FlameGemini/Ardel.git
cd Ardel
```

### 2. Restore Dependencies & Build
```powershell
# Restore NuGet dependencies
dotnet restore Ardel.slnx

# Build launcher in Debug mode
dotnet build src/Ardel.Launcher/Ardel.Launcher.csproj -c Debug -r win-x64
```

### 3. Run Launcher
```powershell
dotnet run --project src/Ardel.Launcher/Ardel.Launcher.csproj -c Debug -r win-x64
```

### 4. Build Standalone Single-File Executable
```powershell
python tools/build_single_exe.py
```
The resulting portable single-file binary will be generated at `publish/Ardel.exe`.

---

## Project Structure

```
Ardel/
├── src/
│   ├── Ardel.Launcher/              # Main WinUI 3 desktop application
│   │   ├── Assets/                  # High-DPI icons, logos, WebRTC voice assets
│   │   ├── Converters/              # XAML UI value converters
│   │   ├── Helpers/                 # Window chrome, Java locator, theme engine, skin rendering
│   │   ├── Localization/            # 11-language loc catalogs and legal notices
│   │   ├── Models/                  # Data contracts, version records, account schemas
│   │   ├── Services/                # Launch pipeline, MSAL auth, skin store, download engine
│   │   ├── ViewModels/              # MVVM presentation logic (CommunityToolkit.Mvvm)
│   │   └── Views/                   # XAML pages, custom dialogs, OOBE wizard
│   ├── Ardel.Bootstrapper/          # Single-file portable self-extracting runner
│   └── Ardel.LogViewer/             # Standalone high-performance diagnostic log viewer
├── third_party/                     # Embedded dependencies (MinecraftSkinRender, CmlLib installers)
├── tools/                           # Build, single-file packing, and localization tools
├── LICENSE                          # Open Software License version 3.0 (OSL-3.0)
└── NOTICE.txt                       # Open source attribution notices
```

---

## Disclaimer

- Ardel is an independent third-party project and is **not affiliated with, endorsed by, or sponsored by Mojang Studios or Microsoft Corporation**.
- "Minecraft" is a registered trademark of Mojang Synergies AB.
- All trademarks and brand names belong to their respective owners.

---

## License

Copyright (c) 2026 Ice Tea Studio.

Ardel is open-source software licensed under the [Open Software License version 3.0](LICENSE) (OSL-3.0).
Third-party libraries and components are distributed under their respective licenses detailed in [NOTICE.txt](NOTICE.txt).
