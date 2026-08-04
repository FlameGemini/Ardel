# Ardel Launcher

Modern Minecraft launcher built with **C# / .NET 8 / WinUI 3** and **CmlLib.Core**.

## Features

- Fluent shell with Mica backdrop and custom title bar
- Home: version select, offline player, one-click launch, live download progress
- Download: release / snapshot catalog, download vanilla client
- Settings: Java auto-scan (deferred), RAM slider, BMCLAPI mirror toggle (**default off**)
- Cold start loads **local** installs only (target &lt; 1s); remote catalog on **下载** page

## Requirements

- Windows 10 1809+ / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/) runtime (or self-contained publish)

## Build

```powershell
dotnet restore Ardel.sln
dotnet build Ardel.sln -c Debug -p:Platform=x64
dotnet run --project src/Ardel.Launcher/Ardel.Launcher.csproj -c Debug -p:Platform=x64
```

## Project layout

```
src/Ardel.Launcher/
  Views/           HomePage, DownloadPage, SettingsPage
  ViewModels/      LaunchViewModel, DownloadViewModel, SettingsViewModel
  Services/        MinecraftLaunchService, SettingsService
  Helpers/         JavaLocator
  Models/          settings & version DTOs
design.md          UI design system
```

## Notes

- CmlLib.Core **4.x** uses `MinecraftLauncher` (successor of `CMLauncher`) and `MSession.CreateOfflineSession`.
- BMCLAPI base: `https://bmclapi2.bangbang93.com`
- Default game dir: `{exe}/.minecraft` (portable, PCL-style)
- **Forced version isolation**: each version uses its own mods/saves/config under `versions/<id>/`
- Settings file: `%LocalAppData%\Ardel\settings.json`

## License

Copyright (c) 2026 FlameGemini

Licensed under the [Open Software License version 3.0](LICENSE) (OSL-3.0).
