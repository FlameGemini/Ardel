using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(60);

    private Storyboard? _fadeStoryboard;
    private int _fadeGeneration;
    private bool _suppressAnim;
    private bool _suppressComboWrite;

    private bool _appearanceSectionRealized;
    private bool _defaultsSectionRealized;
    private bool _defaultsDetailRealized;
    private bool _aboutSectionRealized;

    private AboutViewModel? _about;

    public SettingsViewModel ViewModel { get; }
    public AboutViewModel About => _about ??= App.Services.GetRequiredService<AboutViewModel>();

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        ViewModel.EnsureInitialized();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncSectionTabs();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        StopFade();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Opacity = 1;
        _suppressAnim = true;
        SyncSectionTabs();
        ViewModel.RefreshLaunchBinding();

        var returning = _appearanceSectionRealized || _defaultsSectionRealized || _aboutSectionRealized;

        if (ViewModel.IsAppearanceSection)
            EnsureAppearanceSection();
        if (ViewModel.IsDefaultsSection)
            EnsureDefaultsSection();
        if (ViewModel.IsAboutSection)
            EnsureAboutSection();
        if (ViewModel.IsDefaultsSection && ViewModel.SelectedDefaultsTopic != DefaultsTopic.None)
            EnsureDefaultsDetailPanel();

        if (ViewModel.IsDefaultsSection || _defaultsSectionRealized)
            ApplyDefaultsInstant(showDetail: ViewModel.SelectedDefaultsTopic != DefaultsTopic.None);
        if (ViewModel.IsAppearanceSection || _appearanceSectionRealized)
            ApplyAppearanceInstant(showDetail: ViewModel.SelectedPersonalizationTopic != PersonalizationTopic.None);
        _suppressAnim = false;

        // Cached return: skip FadeIn replay so the page snaps in.
        if (returning)
            return;

        if (ViewModel.IsDefaultsSection)
            FadeIn(DefaultsContentRoot);
        else if (ViewModel.IsAppearanceSection)
            FadeIn(AppearanceContentRoot);
        else if (ViewModel.IsAboutSection)
            FadeIn(AboutContentRoot);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressAnim)
            return;

        if (e.PropertyName == nameof(SettingsViewModel.SelectedDefaultsTopic))
        {
            var showDetail = ViewModel.SelectedDefaultsTopic != DefaultsTopic.None;
            if (showDetail)
                EnsureDefaultsDetailPanel();
            ApplyDefaultsInstant(showDetail);
            FadeIn(showDetail ? DefaultsDetailPanel : DefaultsHubPanel, scrollToTop: true);
            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.SelectedPersonalizationTopic))
        {
            EnsureAppearanceSection();
            var showDetail = ViewModel.SelectedPersonalizationTopic != PersonalizationTopic.None;
            ApplyAppearanceInstant(showDetail);
            FadeIn(showDetail ? AppearanceDetailPanel : AppearanceHubPanel, scrollToTop: true);
            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.SelectedSection) &&
            ViewModel.SelectedSection == SettingsSection.Defaults)
        {
            EnsureDefaultsSection();
            ApplyDefaultsInstant(showDetail: false);
            FadeIn(DefaultsContentRoot, scrollToTop: true);
            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.SelectedSection) &&
            ViewModel.SelectedSection == SettingsSection.Appearance)
        {
            EnsureAppearanceSection();
            ApplyAppearanceInstant(showDetail: false);
            FadeIn(AppearanceContentRoot, scrollToTop: true);
            return;
        }

        if (e.PropertyName == nameof(SettingsViewModel.SelectedSection) &&
            ViewModel.SelectedSection == SettingsSection.About)
        {
            EnsureAboutSection();
            FadeIn(AboutContentRoot, scrollToTop: true);
        }
    }

    private void EnsureAppearanceSection()
    {
        if (_appearanceSectionRealized)
            return;

        _appearanceSectionRealized = true;
        FindName(nameof(AppearanceSectionHost));
    }

    private void EnsureDefaultsSection()
    {
        if (_defaultsSectionRealized)
            return;

        _defaultsSectionRealized = true;
        FindName(nameof(DefaultsSectionHost));
    }

    private void EnsureDefaultsDetailPanel()
    {
        if (_defaultsDetailRealized)
            return;

        _defaultsDetailRealized = true;
        FindName(nameof(DefaultsDetailPanel));
    }

    private void EnsureAboutSection()
    {
        if (_aboutSectionRealized)
            return;

        _aboutSectionRealized = true;
        FindName(nameof(AboutContentRoot));
        About.EnsureInitialized();
    }

    private void ScrollSettingsToTop()
    {
        void PinTop()
        {
            try
            {
                SettingsScrollViewer.UpdateLayout();
                SettingsScrollViewer.ChangeView(0, 0, null, disableAnimation: true);
            }
            catch
            {
                // ignore
            }
        }

        PinTop();
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, PinTop);
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, PinTop);
    }

    private void SyncSectionTabs()
    {
        AppearanceTab.IsChecked = ViewModel.SelectedSection == SettingsSection.Appearance;
        DefaultsTab.IsChecked = ViewModel.SelectedSection == SettingsSection.Defaults;
        AboutTab.IsChecked = ViewModel.SelectedSection == SettingsSection.About;
    }

    private void SectionTab_Click(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("SettingsPage.SectionTab_Click");
        if (sender is not RadioButton { Tag: string tag })
            return;

        ViewModel.SelectedSection = tag switch
        {
            "defaults" => SettingsSection.Defaults,
            "about" => SettingsSection.About,
            _ => SettingsSection.Appearance
        };
        SyncSectionTabs();
    }

    private void LanguageListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("SettingsPage.LanguageListView_ItemClick");
        if (e.ClickedItem is not LanguageOption { Code: var code })
            return;
        if (string.Equals(ViewModel.UiLanguageCode, code, StringComparison.OrdinalIgnoreCase))
            return;
        ViewModel.UiLanguageCode = code;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("SettingsPage.ThemeButton_Click");
        if (sender is Button { Tag: string code })
            ViewModel.AppThemeCode = code;
    }

    private void CreditButton_Click(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("SettingsPage.CreditButton_Click");
        if (sender is FrameworkElement { Tag: Ardel.Launcher.Models.AboutCreditItem item })
            About.OpenCreditCommand.Execute(item);
    }

    private void DefaultMemoryMode_Checked(object sender, RoutedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("SettingsPage.DefaultMemoryMode_Checked");
        if (sender is not RadioButton { IsChecked: true, Tag: string tag })
            return;
        if (!int.TryParse(tag, out var mode) || mode is < 0 or > 1)
            return;
        if (ViewModel.MemoryMode == mode)
            return;
        ViewModel.MemoryMode = mode;
    }

    private void PostLaunchActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnim || _suppressComboWrite)
            return;
        if (ViewModel.SelectedDefaultsTopic != DefaultsTopic.Behavior)
            return;
        if (sender is ComboBox { SelectedIndex: >= 0 and var index })
            ViewModel.PostLaunchWindowAction = index;
    }

    private void BmclUsageModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnim || _suppressComboWrite)
            return;
        if (ViewModel.SelectedDefaultsTopic != DefaultsTopic.Behavior)
            return;
        if (sender is ComboBox { SelectedIndex: >= 0 and var index })
            ViewModel.BmclUsageMode = index;
    }

    private void ResourceVerifyModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAnim || _suppressComboWrite)
            return;
        if (ViewModel.SelectedDefaultsTopic != DefaultsTopic.ResourceRepair)
            return;
        if (sender is ComboBox { SelectedIndex: >= 0 and var index })
            ViewModel.ResourceVerifyMode = index;
    }

    private void ApplyDefaultsInstant(bool showDetail)
    {
        if (!_defaultsSectionRealized && !ViewModel.IsDefaultsSection)
            return;

        if (showDetail)
            EnsureDefaultsDetailPanel();
        if (DefaultsHubPanel is null || DefaultsDetailPanel is null)
            return;

        _suppressComboWrite = true;
        try
        {
            SetPane(DefaultsHubPanel, show: !showDetail);
            SetPane(DefaultsDetailPanel, show: showDetail);
        }
        finally
        {
            _suppressComboWrite = false;
        }
    }

    private void ApplyAppearanceInstant(bool showDetail)
    {
        if (!_appearanceSectionRealized && !ViewModel.IsAppearanceSection)
            return;

        EnsureAppearanceSection();
        if (AppearanceHubPanel is null || AppearanceDetailPanel is null)
            return;

        SetPane(AppearanceHubPanel, show: !showDetail);
        SetPane(AppearanceDetailPanel, show: showDetail);
    }

    private static void SetPane(UIElement pane, bool show)
    {
        pane.Opacity = show ? 1 : 0;
        pane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (pane is FrameworkElement fe)
            fe.IsHitTestVisible = show;
    }

    private void FadeIn(UIElement? target, bool scrollToTop = false)
    {
        if (target is null)
            return;

        StopFade();
        if (target is FrameworkElement fe)
        {
            fe.Visibility = Visibility.Visible;
            fe.IsHitTestVisible = true;
        }

        target.Opacity = 0;
        if (scrollToTop)
            ScrollSettingsToTop();

        var generation = ++_fadeGeneration;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        sb.Children.Add(CreateDoubleAnimation(target, "Opacity", 0, 1, new Duration(FadeDuration), ease));
        sb.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration)
                return;
            target.Opacity = 1;
            _fadeStoryboard = null;
        };

        _fadeStoryboard = sb;
        sb.Begin();
    }

    private void StopFade()
    {
        _fadeGeneration++;
        if (_fadeStoryboard is null)
            return;
        try { _fadeStoryboard.Stop(); } catch { /* ignore */ }
        _fadeStoryboard = null;
    }

    private static DoubleAnimation CreateDoubleAnimation(
        DependencyObject target,
        string propertyPath,
        double from,
        double to,
        Duration duration,
        EasingFunctionBase easing)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, propertyPath);
        return anim;
    }
}
