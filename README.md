# Ardel

Modern, high-performance Minecraft launcher built with **C# / .NET 8 / WinUI 3** (Windows App SDK) and **CmlLib.Core**.

---

## Overview

Ardel is designed from the ground up for speed, reliability, and visual craftsmanship. Combining Microsoft Fluent Design principles with deep Minecraft game lifecycle management, Ardel delivers sub-second cold start times, comprehensive modding platform integrations, cloud-synced account profiles, intelligent crash diagnostics, and built-in peer-to-peer voice communications.

---

## Key Features

### Native WinUI 3 Experience
- **Fluent Design System**: Native Windows 11 Mica Alt backdrop, integrated title bar chrome, smooth transitions, and high-DPI scaling.
- **Crafted Themes**: 9 hand-tuned color palettes (Arctic, Aurora, Cedar, Cinder, Honey, Moss, Obsidian, Peach, Twilight) with instant dark/light switching.
- **Sub-Second Cold Start**: Optimized local asset hydration with deferred network calls for instant launch readiness.

### Version & Mod Loader Management
- **Comprehensive Catalog**: Official Release, Snapshot, and April Fools versions.
- **Loader Automation**: One-click installation for Forge, NeoForge, Fabric, Quilt, and OptiFine.
- **Strict Version Isolation**: Each Minecraft version maintains dedicated mods, configs, saves, resource packs, and shader packs.
- **Mirror Acceleration**: Optional BMCLAPI mirror fallback for high-speed metadata and asset downloads.

### Account & Identity Management
- **Microsoft OAuth2**: Official Microsoft / Mojang cloud authentication via MSAL.
- **Cloud Skin & Cape Sync**: Real-time synchronization of official player skins (Classic 4px / Slim 3px) and Mojang capes.
- **Local Skin Library**: Import, organize, and preview custom offline skin collections with high-resolution 3D head rendering.
- **In-App Name Management**: In-app Microsoft username eligibility verification and 30-day cooldown enforcement.
- **Licensed Account Verification**: Enforces valid Microsoft account binding to protect ecosystem compliance.

### Modpack & Addon Center
- **Cross-Platform Indexing**: Search, inspect, and install mods, resource packs, shaders, and data packs directly from Modrinth and CurseForge.
- **Modpack Compatibility**: Import and export `.mrpack` (Modrinth) and CurseForge modpack archives.
- **Save & World Inspection**: Level data analyzer, seed inspection, game mode toggles, and world backup restoration.

### Low-Latency Voice & Networking
- **Decentralized P2P Voice**: Built-in WebRTC audio communication with zero dedicated voice server requirement.
- **NAT Traversal**: Regional STUN/TURN fallback network architecture for seamless cross-network team calls.

### Reliability & Diagnostics
- **Intelligent Crash Analysis**: Automatic root-cause detection, mod stack identification, and actionable diagnostic guidance upon game crash.
- **Embedded Log Viewer**: Multi-threaded game log streaming with real-time level filtering and regex search.
- **Smart Java Locator**: Automated detection and validation of JRE/JDK installations across Java 8 through Java 25.

### Internationalization
Fully localized in 11 languages with runtime language switching:
- Simplified Chinese (`zh-CN`)
- Traditional Chinese (`zh-TW`)
- English (`en-US`)
- Japanese (`ja-JP`)
- French (`fr-FR`)
- German (`de-DE`)
- Spanish (`es-ES`)
- Italian (`it-IT`)
- Korean (`ko-KR`)
- Portuguese (`pt-BR`)
- Russian (`ru-RU`)

---

## Requirements

- **Operating System**: Windows 10 Version 1809 (Build 17763) or higher / Windows 11
- **Architecture**: x64 / ARM64
- **Runtime**: Windows App SDK 1.7+ (Self-contained build recommended)
- **Development Tools**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and Visual Studio 2022 (v17.8+) or Visual Studio Code

---

## Building from Source

### 1. Clone Repository
```powershell
git clone https://github.com/FlameGemini/Ardel.git
cd Ardel
```

### 2. Restore and Build
```powershell
# Restore NuGet dependencies
dotnet restore Ardel.sln

# Build launcher in Debug configuration
dotnet build Ardel.sln -c Debug -p:Platform=x64
```

### 3. Run Application
```powershell
dotnet run --project src/Ardel.Launcher/Ardel.Launcher.csproj -c Debug -p:Platform=x64
```

### 4. Publish Self-Contained Release
```powershell
dotnet publish src/Ardel.Launcher/Ardel.Launcher.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
```

---

## Project Structure

```
Ardel/
├── src/
│   ├── Ardel.Launcher/              # Main WinUI 3 desktop application
│   │   ├── Assets/                  # Branding, icons, WebRTC voice web frontend
│   │   ├── Converters/              # XAML data-binding value converters
│   │   ├── Helpers/                 # Window chrome, Java locator, theme engine, skin rendering
│   │   ├── Localization/            # Multilingual catalogs, legal notice, loc helper
│   │   ├── Models/                  # Data contracts, version records, account schemas
│   │   ├── Services/                # Launch pipeline, Microsoft auth, skin store, download engine
│   │   ├── ViewModels/              # MVVM presentation logic (CommunityToolkit.Mvvm)
│   │   └── Views/                   # XAML pages, dialogs, OOBE wizard, panels
│   └── Ardel.LogViewer/             # Standalone high-performance game log viewer
├── third_party/                     # Embedded dependencies (MinecraftSkinRender, CmlLib installers)
├── tools/                           # Localization sync and export automation scripts
├── LICENSE                          # Open Software License version 3.0 (OSL-3.0)
└── NOTICE.txt                       # Open source attribution notices
```

---

## Architecture Notes

- **Launch Engine**: Powered by `CmlLib.Core` 4.x with custom process isolation and launch pipelines.
- **Threading Model**: Strict WinUI 3 UI thread dispatching with async/await background tasks.
- **Data Persistence**: Portable application data stored in `{exe}/.minecraft` and user configurations in `%LocalAppData%\Ardel\`.

---

## License

Copyright (c) 2026 FlameGemini.

Ardel is open-source software licensed under the [Open Software License version 3.0](LICENSE) (OSL-3.0).
Third-party libraries and assets are subject to their respective open-source licenses detailed in [NOTICE.txt](NOTICE.txt).
