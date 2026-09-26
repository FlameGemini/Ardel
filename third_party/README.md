# Vendored installer patches (Ardel)

Patched forks of:

- [CmlLib.Core.Installer.Forge](https://github.com/CmlLib/CmlLib.Core.Installer.Forge) (MIT)
- [CmlLib.Core.Installer.NeoForge](https://github.com/Gml-Launcher/CmlLib.Core.Installer.NeoForge) (MIT)
- [MinecraftSkinRender](https://github.com/Coloryr/MinecraftSkinRender) (MIT) — `MinecraftSkinRender.Image` only, retargeted to `net8.0` for Home 3D head previews (`Skin3DHeadTypeA`)

## Why

Upstream Forge/NeoForge installers open `adfoc.us` in the default browser after install
(`Process.Start` with `UseShellExecute`). In WinUI that hijacks the browser and can
surface as a confusing `COMException` failure toast even when files installed correctly.

MinecraftSkinRender NuGet 1.2.0 targets `net10.0` only; we vendor the Image project on `net8.0`.

## Ardel changes

- Removed post-install adfoc / browser launch (`showAd`).
- Intercept adfoc download hrefs → unwrap real maven URLs.
- Loader **lists**: try official sources (~3s); if that fails, fall back to BMCLAPI list
  endpoints. Download/install mirroring still follows `settings.UseBmclApi` only.
- Forge version resolve: official maven-metadata → BMCL forge index → HTML scrape.
- MinecraftSkinRender.Image: `net8.0` + compile only head/cape helpers (no `SkinType` core dep).
