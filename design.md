# Ardel Launcher — Design System

> Source of truth for UI/UX consistency across WinUI 3 pages.
> Stack: **.NET 8 · WinUI 3 · Windows App SDK · Fluent Design**

---

## 0. Localization (required)

Ardel will ship **multi-language**. Treat every user-visible phrase as a resource — never hardcode UI copy in XAML or C#.

| Do | Don't |
|----|--------|
| `Loc.Get(LocKeys.…)` / `Loc.Format(LocKeys.…, …)` in code | `"Cancelled"`, `$"Failed {id}"` inline |
| `{loc:String Key=Home_Tagline}` in XAML | `Text="Launch Minecraft"` |
| Stable logic IDs (`release` / `snapshot`) + localized display names | Compare UI strings in `if` / `switch` |
| Add keys to `LocKeys` + `Strings/en-US/Resources.resw` + `Loc` fallback map | Invent one-off English only in a ViewModel |

**Layout**

- Keys: `src/Ardel.Launcher/Localization/LocKeys.cs`
- Lookup: `Localization/Loc.cs` (ResourceLoader + English fallback)
- XAML helper: `{loc:String Key=…}` → `StringExtension`
- Catalog: `Strings/<culture>/Resources.resw` (start with `en-US`; add `zh-CN`, etc. later)

**Exceptions (OK to leave literal)**

- Product brand **Ardel** (key `Brand_Name` — usually not translated)
- Technical tokens: version ids (`1.21.1`), file names (`java.exe`), paths, log/Debug lines
- Exception messages from OS / libraries when surfaced as `{0}` in a localized template

**When adding UI**

1. Add a `LocKeys` constant  
2. Add the same name to `Resources.resw` **and** `Loc` fallback dictionary  
3. Bind XAML with `{loc:String Key=…}` or call `Loc` from the ViewModel  

---

## 1. Brand & Product Signal

| Token | Value |
|-------|-------|
| Product name | **Ardel** |
| Full title | Ardel Launcher |
| Voice | Calm, precise, craft-oriented — a modern workshop for Minecraft, not an arcade skin |
| Brand test | With chrome removed, the first viewport must still read as *Ardel* (title bar wordmark + emerald accent), not a generic launcher |

**Do not** bury the name as a tiny nav caption. Home first viewport: giant **Ardel** wordmark is the hero; title bar echo is secondary.

---

## 2. Visual Direction

### Theme

- **Primary surface:** Warm stone / moss atelier wash (not stock Mica chrome)
- **Mode:** Follow system light/dark via `ThemeDictionaries`
- **Mood:** Quiet workshop — emerald mark, Syne display + Outfit body (shipped OFL fonts)
- **Avoid looking like:** Default WinUI Settings / Microsoft Store shell

### Scale

- Default window ≈ **88% of work area** (cap 1920×1200), min **1280×800**
- Home brand: **~80px** Syne wordmark + short emerald rule
- Primary CTA: **56px** tall, min width **200**, custom template (not `AccentButtonStyle`)
- Page padding Home: **72×56**; Download/Settings: **64×48**

### Cold start

- Shell paints **before** navigating Home
- Local version scan deferred (Low priority dispatcher)
- **CmlLib not loaded** until Download/Launch (`Lazy<MinecraftLaunchService>`)

### Color tokens

| Role | Light | Dark | Notes |
|------|-------|------|-------|
| Accent | `#1F7A4D` | `#3DAA72` | Primary CTA / focus |
| Accent hover | `#145C38` | `#2D8A5B` | Pressed / hover |
| Soft accent | `#D8EBDD` | `#1A3326` | Nav selection wash |
| Canvas | `#F3EFE6` | `#121A16` | Shell base |
| Rail | `#E8E2D6` | `#18211C` | Left nav |
| Ink | `#1A2420` | `#E8F0EA` | Primary text |
| Mute | `#5C6B62` | `#9AABA0` | Captions |

**Avoid (project anti-patterns)**

- Purple / indigo default AI themes
- Warm cream + terracotta “editorial” look
- Neon glow, CRT scanlines, synthwave chrome
- Dense newspaper / broadsheet layouts
- Emoji as icons (use `FontIcon` Segoe Fluent glyphs)
- Stock Fluent blue accent / default `AccentButtonStyle` for brand CTAs

### Typography

| Use | Font |
|-----|------|
| Brand / page titles | **Syne** (`Assets/Fonts/Syne.ttf`) |
| Body / forms | **Outfit** (`Assets/Fonts/Outfit.ttf`) |
| Icons only | Segoe Fluent Icons |

Do not default body UI to Segoe UI Variable for brand surfaces.

---

## 3. Layout Grammar

### Shell

```
┌─────────────────────────────────────────────┐
│ [■] Ardel  Launcher          (drag region) │  ← custom title bar (48px)
├──────────┬──────────────────────────────────┤
│ 启动     │                                  │
│ 下载     │         ContentFrame             │
│ 设置     │         (page padding 48×40)     │
└──────────┴──────────────────────────────────┘
```

- Default window **1440×900**, min **1100×700**, centered
- `ExtendsContentIntoTitleBar = true`
- `NavigationView` left pane, `OpenPaneLength = 160` — labels: **启动 / 下载 / 设置**
- No footer items (no fake “Offline” status)
- Page padding: **48,40**
- Prefer **flat layout** — avoid card stacks on Home/Settings
- Corner radius: **8** for primary buttons only

### One job per section

| Page | Job |
|------|-----|
| 启动 | 选已下载版本 → 启动；进度仅在启动中显示 |
| 下载 | 正式版 / 快照；下载原版客户端 |
| 设置 | Java、内存、BMCLAPI（默认关）、目录 |

### Clarity rules (anti-mislead)

- Do **not** imply Fabric/Forge one-click install until that feature ships
- Player name is **offline only** — label it as such
- BMCLAPI is opt-in, default **off**
- Cold start loads **local versions only** (no network / Java scan)
- Game data: **`{exe}/.minecraft`** (portable, like [PCL](https://github.com/Meloong-Git/PCL))
- **Version isolation forced**: instance files under `.minecraft/versions/<id>/`

### Cards policy

Default: **no cards**. Flat controls on Mica.

---

## 4. Components

### Navigation

| Item | Glyph | Tag |
|------|-------|-----|
| 启动 | `E768` | `home` |
| 下载 | `E896` | `download` |
| 设置 | `E713` | `settings` |

Icons: **Segoe Fluent Icons** only. Consistent 16–20px.

### Primary CTA — Launch

- Style: `LaunchButtonStyle` (Accent, min 180×48, SemiBold 16)
- States: default / launching (swap to Cancel) / disabled when no version
- Never place secondary marketing CTAs beside Launch

### Progress

- Height **6–8**, corner radius **3–4**
- Pair with a single status line (file name or byte progress)
- Prefer determinate bar once totals are known; indeterminate only during prep

### Form controls

- `ComboBox` for version / Java lists
- `Slider` for RAM (1024–16384, step 256)
- `ToggleSwitch` for BMCLAPI (Off = Official, On = BMCLAPI)
- File / folder pickers via Windows App SDK pickers

### Player identity (Home)

- Circular emerald avatar with initials
- Inline editable player name (offline session)
- No floating badges over imagery

---

## 5. Motion

Ship intentional, quiet motion — not noise:

1. **Page enter:** `EntranceNavigationTransitionInfo` on `Frame.Navigate`
2. **Progress:** smooth `ProgressBar` value updates (UI thread via `DispatcherQueue`)
3. **Optional later:** subtle opacity fade on status text change (150–200ms)

Respect `prefers-reduced-motion` / system animation settings when adding Storyboards.

Hover: color / opacity only — **no layout-shifting scale**.

---

## 6. Content patterns

### Home first viewport

Allowed:

1. Brand (title bar)
2. Page title + one supporting line
3. Player chip
4. Version ComboBox + Launch
5. Progress / status

Disallowed in first viewport: version catalogs, mirror essays, Java tables, promo rows.

### Empty / error / busy copy

| State | Tone |
|-------|------|
| Ready | Short: `Ready` |
| Busy | Verb + object: `Downloading… 12 MB / 40 MB` |
| Error | Cause first: `Launch failed: Java 17 required` |
| Cancel | `Launch cancelled` |

Never show raw stack traces in the UI; log to `Debug` / future log pane.

---

## 7. Accessibility

- Contrast ≥ 4.5:1 for body text on Mica surfaces
- Focus visuals: rely on Fluent focus rings; do not remove
- Keyboard: NavigationView + tab order through Launch / Cancel
- Color is not the only status signal (always keep `StatusText`)
- Hit targets ≥ 40×40 for primary actions

---

## 8. XAML resource map

Defined in `App.xaml`:

| Key | Purpose |
|-----|---------|
| `ArdelAccentColor` / `ArdelAccentBrush` | Brand accent |
| `ArdelAccentDarkBrush` | Hover / pressed |
| `ArdelPageTitleStyle` | Page H1 |
| `ArdelSectionCaptionStyle` | Supporting line |
| `LaunchButtonStyle` | Primary launch CTA |
| `BoolToVisibility` / `InverseBoolToVisibility` | State visibility |

Prefer **theme resources** (`CardBackgroundFillColorDefaultBrush`, etc.) over hard-coded greys.

---

## 9. Implementation checklist

- [ ] Mica enabled; title bar extended
- [ ] Brand wordmark visible in title bar
- [ ] No emoji icons
- [ ] Cards only for interactive groups
- [ ] One primary CTA on Home
- [ ] Progress + status always paired
- [ ] Light and dark both readable on Mica
- [ ] Settings changes persist via `SettingsService`
- [ ] BMCLAPI toggle clearly labeled Official vs BMCLAPI

---

## 10. Future UI extensions (out of scope now)

When adding Microsoft account login, mod browser, or news:

- Keep Home launch composition intact
- Put new surfaces on new nav items or secondary panes
- Reuse accent / radius / padding tokens from this document
- Do not introduce a second accent hue without updating this file
