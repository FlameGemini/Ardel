using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ardel.Launcher.Helpers;

/// <summary>
/// Owns theme application. Restored from the known-good model (commit 0b44695):
/// mutate Ardel* SolidColorBrush colors in place, and mirror rail/canvas/hover/selected
/// into NavigationView ThemeDictionary brushes. No per-control chrome reinvention.
/// </summary>
public static class AppThemeController
{
    private static readonly Windows.UI.Color LightAccent = ColorHelper.FromArgb(255, 0, 120, 212);
    private static readonly Windows.UI.Color DarkAccent = ColorHelper.FromArgb(255, 122, 137, 150);

    public static void Apply(MainWindow? window, string? theme)
    {
        try
        {
            theme = NormalizeThemeCode(theme);

            var elementTheme = ResolveElementTheme(theme);

            if (window?.Content is FrameworkElement rootElement)
            {
                try
                {
                    rootElement.RequestedTheme = elementTheme;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppThemeController] RequestedTheme set failed: {ex.Message}");
                }
            }

            ApplyCaptionButtonColors(window);

            if (window is null)
                return;

            try
            {
                if (theme == "Samoyed")
                {
                    if (window.BackgroundImage is not null)
                    {
                        window.BackgroundImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/samoyed_fur.png"));
                        window.BackgroundImage.Opacity = 0.85;
                    }
                    if (window.RootGrid is not null)
                        window.RootGrid.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 245, 247, 250));
                    if (window.AppTitleBar is not null)
                        window.AppTitleBar.Background = null;
                }
                else
                {
                    if (window.BackgroundImage is not null)
                    {
                        window.BackgroundImage.Source = null;
                        window.BackgroundImage.Opacity = 0;
                    }
                    if (window.AppTitleBar is not null)
                        window.AppTitleBar.Background = null;

                    if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                    {
                        try
                        {
                            if (window.SystemBackdrop is not MicaBackdrop)
                                window.SystemBackdrop = new MicaBackdrop();
                            if (window.RootGrid is not null)
                                window.RootGrid.Background = null;
                        }
                        catch
                        {
                            try { window.SystemBackdrop = null; } catch { }
                            if (window.RootGrid is not null)
                                window.RootGrid.Background = Application.Current.Resources.TryGetValue("ArdelCanvasBrush", out var b) && b is Brush brush ? brush : null;
                        }
                    }
                    else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
                    {
                        try
                        {
                            if (window.SystemBackdrop is not DesktopAcrylicBackdrop)
                                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                            if (window.RootGrid is not null)
                                window.RootGrid.Background = null;
                        }
                        catch
                        {
                            try { window.SystemBackdrop = null; } catch { }
                            if (window.RootGrid is not null)
                                window.RootGrid.Background = Application.Current.Resources.TryGetValue("ArdelCanvasBrush", out var b) && b is Brush brush ? brush : null;
                        }
                    }
                    else
                    {
                        try { window.SystemBackdrop = null; } catch { }
                        if (window.RootGrid is not null)
                            window.RootGrid.Background = Application.Current.Resources.TryGetValue("ArdelCanvasBrush", out var b) && b is Brush brush ? brush : null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppThemeController] Backdrop configuration failed: {ex.Message}");
            }

            void UpdateBrush(string key, Windows.UI.Color color)
            {
                UpdateBrushDirect(key, color);

                // Mirror shell tokens into NavigationView brushes (same SolidColorBrush instances
                // live in ThemeDictionaries — mutating Color is how WinUI picks up the change).
                if (key == "ArdelRailBrush")
                {
                    UpdateBrushDirect("NavigationViewDefaultPaneBackground", color);
                    UpdateBrushDirect("NavigationViewExpandedPaneBackground", color);
                }
                else if (key == "ArdelCanvasBrush")
                {
                    UpdateBrushDirect("NavigationViewContentGridBackground", color);
                }
                else if (key == "ArdelHoverBrush")
                {
                    UpdateBrushDirect("NavigationViewItemBackgroundPointerOver", color);
                }
                else if (key == "ArdelSelectedBrush")
                {
                    UpdateBrushDirect("NavigationViewItemBackgroundPressed", color);
                    UpdateBrushDirect("NavigationViewItemBackgroundSelected", color);
                    UpdateBrushDirect("NavigationViewItemBackgroundSelectedPointerOver", color);
                }
            }

            static void UpdateBrushDirect(string key, Windows.UI.Color color)
            {
                try
                {
                    if (Application.Current?.Resources != null)
                    {
                        if (Application.Current.Resources.TryGetValue(key, out var obj) && obj is SolidColorBrush brush)
                            brush.Color = color;

                        if (Application.Current.Resources.ThemeDictionaries != null)
                        {
                            foreach (var dictKey in Application.Current.Resources.ThemeDictionaries.Keys)
                            {
                                try
                                {
                                    if (Application.Current.Resources.ThemeDictionaries[dictKey] is ResourceDictionary themeDict
                                        && themeDict.TryGetValue(key, out var themeObj)
                                        && themeObj is SolidColorBrush themeBrush)
                                    {
                                        themeBrush.Color = color;
                                    }
                                }
                                catch
                                {
                                    // ignore per-dict errors
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppThemeController] UpdateBrushDirect failed for {key}: {ex.Message}");
                }
            }

            static Windows.UI.Color DarkenColor(Windows.UI.Color c, double factor) =>
                Windows.UI.Color.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

            static Windows.UI.Color LightenColor(Windows.UI.Color c, double factor) =>
                Windows.UI.Color.FromArgb(c.A,
                    (byte)(c.R + (255 - c.R) * factor),
                    (byte)(c.G + (255 - c.G) * factor),
                    (byte)(c.B + (255 - c.B) * factor));

            Windows.UI.Color inkForTitle;

            void ApplyLightPalette(Windows.UI.Color accent)
            {
                UpdateBrush("ArdelAccentBrush", accent);
                UpdateBrush("ArdelAccentDarkBrush", DarkenColor(accent, 0.8));
                UpdateBrush("ArdelSoftAccentBrush", LightenColor(accent, 0.85));
                UpdateBrush("ArdelCanvasBrush", ColorHelper.FromArgb(255, 245, 247, 248));
                UpdateBrush("ArdelRailBrush", ColorHelper.FromArgb(255, 236, 239, 241));
                UpdateBrush("ArdelInkBrush", ColorHelper.FromArgb(255, 28, 32, 36));
                UpdateBrush("ArdelMuteBrush", ColorHelper.FromArgb(255, 96, 111, 123));
                UpdateBrush("ArdelHoverBrush", ColorHelper.FromArgb(10, 0, 0, 0));
                UpdateBrush("ArdelSelectedBrush", ColorHelper.FromArgb(20, 0, 0, 0));
                inkForTitle = ColorHelper.FromArgb(255, 28, 32, 36);
            }

            void ApplyDarkPalette(Windows.UI.Color accent)
            {
                UpdateBrush("ArdelAccentBrush", accent);
                UpdateBrush("ArdelAccentDarkBrush", DarkenColor(accent, 0.8));
                UpdateBrush("ArdelSoftAccentBrush", ColorHelper.FromArgb(255, 58, 68, 76));
                UpdateBrush("ArdelCanvasBrush", ColorHelper.FromArgb(255, 18, 21, 24));
                UpdateBrush("ArdelRailBrush", ColorHelper.FromArgb(255, 26, 29, 32));
                UpdateBrush("ArdelInkBrush", ColorHelper.FromArgb(255, 232, 236, 239));
                UpdateBrush("ArdelMuteBrush", ColorHelper.FromArgb(255, 154, 162, 171));
                UpdateBrush("ArdelHoverBrush", ColorHelper.FromArgb(25, 255, 255, 255));
                UpdateBrush("ArdelSelectedBrush", ColorHelper.FromArgb(50, 255, 255, 255));
                inkForTitle = ColorHelper.FromArgb(255, 232, 236, 239);
            }

            if (theme == "Sakura")
            {
                // Distinct strawberry pink shell — not a lightly tinted Light theme.
                UpdateBrush("ArdelAccentBrush", ColorHelper.FromArgb(255, 232, 64, 150));
                UpdateBrush("ArdelAccentDarkBrush", ColorHelper.FromArgb(255, 196, 32, 118));
                UpdateBrush("ArdelSoftAccentBrush", ColorHelper.FromArgb(255, 255, 214, 232));
                UpdateBrush("ArdelCanvasBrush", ColorHelper.FromArgb(255, 255, 228, 240));
                UpdateBrush("ArdelRailBrush", ColorHelper.FromArgb(255, 255, 182, 214));
                UpdateBrush("ArdelInkBrush", ColorHelper.FromArgb(255, 88, 24, 56));
                UpdateBrush("ArdelMuteBrush", ColorHelper.FromArgb(255, 168, 84, 120));
                UpdateBrush("ArdelHoverBrush", ColorHelper.FromArgb(36, 232, 64, 150));
                UpdateBrush("ArdelSelectedBrush", ColorHelper.FromArgb(64, 232, 64, 150));
                inkForTitle = ColorHelper.FromArgb(255, 88, 24, 56);
                if (window.AppTitleBar is not null)
                    window.AppTitleBar.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 182, 214));
                SetRadii(10, 8, 6);
            }
            else if (theme == "Samoyed")
            {
                UpdateBrush("ArdelAccentBrush", ColorHelper.FromArgb(255, 74, 105, 132));
                UpdateBrush("ArdelAccentDarkBrush", ColorHelper.FromArgb(255, 52, 77, 100));
                UpdateBrush("ArdelSoftAccentBrush", ColorHelper.FromArgb(255, 237, 241, 245));
                UpdateBrush("ArdelCanvasBrush", ColorHelper.FromArgb(0, 0, 0, 0));
                UpdateBrush("ArdelRailBrush", ColorHelper.FromArgb(200, 194, 208, 222));
                UpdateBrush("ArdelInkBrush", ColorHelper.FromArgb(255, 28, 42, 56));
                UpdateBrush("ArdelMuteBrush", ColorHelper.FromArgb(255, 96, 115, 134));
                UpdateBrush("ArdelHoverBrush", ColorHelper.FromArgb(64, 255, 255, 255));
                UpdateBrush("ArdelSelectedBrush", ColorHelper.FromArgb(120, 255, 255, 255));
                inkForTitle = ColorHelper.FromArgb(255, 28, 42, 56);
                // Same radii as Light — no claymorphism mega-rounding.
                SetRadii(8, 8, 6);
            }
            else if (theme == "Sweden")
            {
                // Scandinavian light shell — Swedish flag blue + gold.
                UpdateBrush("ArdelAccentBrush", ColorHelper.FromArgb(255, 0, 106, 167));
                UpdateBrush("ArdelAccentDarkBrush", ColorHelper.FromArgb(255, 0, 82, 130));
                UpdateBrush("ArdelSoftAccentBrush", ColorHelper.FromArgb(255, 255, 236, 160));
                UpdateBrush("ArdelCanvasBrush", ColorHelper.FromArgb(255, 245, 248, 252));
                UpdateBrush("ArdelRailBrush", ColorHelper.FromArgb(255, 214, 230, 242));
                UpdateBrush("ArdelInkBrush", ColorHelper.FromArgb(255, 18, 48, 78));
                UpdateBrush("ArdelMuteBrush", ColorHelper.FromArgb(255, 90, 122, 148));
                UpdateBrush("ArdelHoverBrush", ColorHelper.FromArgb(36, 0, 106, 167));
                UpdateBrush("ArdelSelectedBrush", ColorHelper.FromArgb(64, 0, 106, 167));
                inkForTitle = ColorHelper.FromArgb(255, 18, 48, 78);
                if (window.AppTitleBar is not null)
                    window.AppTitleBar.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 255, 220, 80));
                SetRadii(8, 8, 6);
            }
            else if (theme == "Light")
            {
                ApplyLightPalette(LightAccent);
                SetRadii(8, 8, 6);
            }
            else if (theme == "Dark")
            {
                ApplyDarkPalette(DarkAccent);
                SetRadii(8, 8, 6);
            }
            else // Default — same palette as Light/Dark; only follows Windows mode
            {
                if (ResolveSystemIsDark(window))
                    ApplyDarkPalette(DarkAccent);
                else
                    ApplyLightPalette(LightAccent);

                SetRadii(8, 8, 6);
            }

            // Never assign Foreground=null — that makes the title invisible in WinUI.
            if (window.AppTitleTextBlock is not null)
            {
                window.AppTitleTextBlock.Foreground = new SolidColorBrush(inkForTitle);
                window.AppTitleTextBlock.Opacity = 0.92;
            }

            // Clear any leftover NavView.Resources overrides from the broken chrome path.
            ClearNavViewLocalOverrides(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppThemeController] Apply failed: {ex}");
        }
    }

    public static ElementTheme ResolveElementTheme(string? theme)
    {
        theme = NormalizeThemeCode(theme);
        return theme switch
        {
            "Light" or "Sakura" or "Samoyed" or "Sweden" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    /// <summary>Maps removed / unknown theme codes to a shipped theme.</summary>
    public static string NormalizeThemeCode(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
            return "Default";

        theme = theme.Trim();
        return theme switch
        {
            "Default" or "Light" or "Dark" or "Sakura" or "Samoyed" or "Sweden" => theme,
            // Retired light / warm shells.
            "Arctic" or "Honey" or "Peach" or "Cedar" => "Light",
            // Retired dark shells.
            "Aurora" or "Cinder" or "Moss" or "Obsidian" or "Twilight" => "Dark",
            _ => "Default"
        };
    }

    public static bool ResolveSystemIsDark(MainWindow? window)
    {
        try
        {
            if (window?.Content is FrameworkElement root)
            {
                if (root.ActualTheme == ElementTheme.Dark)
                    return true;
                if (root.ActualTheme == ElementTheme.Light)
                    return false;
            }

            return Application.Current?.RequestedTheme == ApplicationTheme.Dark;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyCaptionButtonColors(MainWindow? window)
    {
        try
        {
            if (window?.AppWindow?.TitleBar is not { } titleBar)
                return;

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            var actual = ElementTheme.Default;
            if (window.Content is FrameworkElement root)
                actual = root.ActualTheme;

            var light = actual == ElementTheme.Light
                || (actual == ElementTheme.Default && (Application.Current?.RequestedTheme ?? ApplicationTheme.Dark) == ApplicationTheme.Light);

            if (light)
            {
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedForegroundColor = Colors.Black;
                titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(160, 0, 0, 0);
                titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(24, 0, 0, 0);
                titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(40, 0, 0, 0);
            }
            else
            {
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(160, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(24, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(40, 255, 255, 255);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppThemeController] ApplyCaptionButtonColors failed: {ex.Message}");
        }
    }

    private static void SetRadii(double card, double icon, double btn)
    {
        try
        {
            if (Application.Current?.Resources != null)
            {
                Application.Current.Resources["ArdelCardCornerRadius"] = new CornerRadius(card);
                Application.Current.Resources["ArdelIconCornerRadius"] = new CornerRadius(icon);
                Application.Current.Resources["ArdelBtnCornerRadius"] = new CornerRadius(btn);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppThemeController] SetRadii failed: {ex.Message}");
        }
    }

    private static void ClearNavViewLocalOverrides(MainWindow window)
    {
        try
        {
            if (window?.NavView?.Resources is null)
                return;

            var keys = new[]
            {
                "NavigationViewDefaultPaneBackground",
                "NavigationViewExpandedPaneBackground",
                "NavigationViewContentGridBackground",
                "NavigationViewItemBackgroundPointerOver",
                "NavigationViewItemBackgroundPressed",
                "NavigationViewItemBackgroundSelected",
                "NavigationViewItemBackgroundSelectedPointerOver",
                "NavigationViewItemBackgroundSelectedPressed",
                "NavigationViewItemForeground",
                "NavigationViewItemForegroundPointerOver",
                "NavigationViewItemForegroundPressed",
                "NavigationViewItemForegroundSelected",
                "NavigationViewItemForegroundSelectedPointerOver",
                "NavigationViewItemForegroundSelectedPressed",
                "NavigationViewItemForegroundDisabled",
                "NavigationViewItemHeaderForeground"
            };

            foreach (var key in keys)
            {
                try
                {
                    window.NavView.Resources.Remove(key);
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppThemeController] ClearNavViewLocalOverrides failed: {ex.Message}");
        }
    }
}
