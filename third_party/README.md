# Vendored installer patches (Ardel)

Patched forks of:

- [CmlLib.Core.Installer.Forge](https://github.com/CmlLib/CmlLib.Core.Installer.Forge) (MIT)
- [CmlLib.Core.Installer.NeoForge](https://github.com/Gml-Launcher/CmlLib.Core.Installer.NeoForge) (MIT)

## Why

Upstream Forge/NeoForge installers open `adfoc.us` in the default browser after install
(`Process.Start` with `UseShellExecute`). In WinUI that hijacks the browser and can
surface as a confusing `COMException` failure toast even when files installed correctly.

## Ardel changes

- Removed post-install adfoc / browser launch (`showAd`).
- Intercept adfoc download hrefs → unwrap real maven URLs.
- Loader **lists**: try official sources (~3s); if that fails, fall back to BMCLAPI list
  endpoints. Download/install mirroring still follows `settings.UseBmclApi` only.
- Forge version resolve: official maven-metadata → BMCL forge index → HTML scrape.
