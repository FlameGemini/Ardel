using Ardel.Launcher.Helpers;
using Ardel.Launcher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Ardel.Launcher.Views.Oobe;

public sealed partial class OobeWizard : UserControl
{
    private readonly Border[] _progressSegments;

    public OobeWizard()
    {
        ViewModel = new OobeViewModel();
        ViewModel.Completed += (_, _) => Completed?.Invoke(this, EventArgs.Empty);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        InitializeComponent();
        _progressSegments = [ProgressSeg0, ProgressSeg1, ProgressSeg2, ProgressSeg3, ProgressSeg4, ProgressSeg5];
        PersonalizationTemplateHelper.WireThemeSelection(ThemeOptionsRepeater, code =>
        {
            ViewModel.SelectTheme(code);
            // Must ApplyTheme so ArdelCanvas/Ink brushes update — RequestedTheme alone
            // leaves a dark canvas under light ink (unreadable tutorial step).
            App.ApplyTheme(code);
            OobeHost.SyncOverlayTheme(code);
            SyncOobeLogos();
        });
        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => SyncOobeLogos();
    }

    public OobeViewModel ViewModel { get; }

    public event EventHandler? Completed;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.ApplyOobeUiLanguage(ViewModel.SelectedLanguageCode);
        ViewModel.RefreshLocalizedStrings(includeLegal: false);
        var theme = string.IsNullOrWhiteSpace(ViewModel.SelectedThemeCode)
            ? App.ActiveThemeCode
            : ViewModel.SelectedThemeCode;
        App.ApplyTheme(theme);
        OobeHost.SyncOverlayTheme(theme);
        SyncOobeLogos();
        UpdateProgressSegments();
        RunStepAnimation(ViewModel.CurrentStep);
        OobeAnimations.FadeIn(BodyHost);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OobeViewModel.CurrentStep))
        {
            OobeAnimations.ResetVisual(AnimGlobe);
            OobeAnimations.ResetVisual(AnimPen);
            OobeAnimations.ResetVisual(AnimPalette);
            OobeAnimations.ResetVisual(AnimLangLogo);
            UpdateProgressSegments();
            if (ViewModel.CurrentStep == (int)OobeStep.License)
                ViewModel.EnsureLegalNoticeLoaded();
            if (!ViewModel.IsWelcomeStep)
                SplitPanel.Opacity = 1;
            OobeAnimations.CrossfadeIn(ViewModel.IsWelcomeStep ? WelcomePanel : ContentHost);
            RunStepAnimation(ViewModel.CurrentStep);
            if (ViewModel.IsWelcomeStep)
                SyncOobeLogos();
        }
        else if (e.PropertyName == nameof(OobeViewModel.ShowLangLogo) && ViewModel.ShowLanguageLogoArt)
        {
            OobeAnimations.FadeInLogo(AnimLangLogo);
        }
    }

    private void UpdateProgressSegments()
    {
        for (var i = 0; i < _progressSegments.Length; i++)
        {
            var active = i <= ViewModel.CurrentStep;
            _progressSegments[i].Style = (Style)Resources[
                active ? "OobeProgressSegmentActiveStyle" : "OobeProgressSegmentStyle"];
        }
    }

    private void RunStepAnimation(int step)
    {
        switch (step)
        {
            case (int)OobeStep.Language:
                OobeAnimations.SlideInFromLeft(AnimGlobe);
                break;
            case (int)OobeStep.Welcome:
                OobeAnimations.RunWelcomeTransition(WelcomePanel);
                OobeAnimations.PopIn(WelcomeLogo);
                break;
            case (int)OobeStep.License:
                OobeAnimations.FadeIn(AnimPen, 280);
                break;
            case (int)OobeStep.Theme:
                OobeAnimations.FadeIn(ThemeStepPanel, 280);
                break;
            case (int)OobeStep.Tutorial:
                OobeAnimations.FadeIn(TutorialStepPanel, 280);
                break;
            case (int)OobeStep.Complete:
                OobeAnimations.PopIn(AnimCompleteCheck);
                break;
        }
    }

    private void LanguageListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LanguageOption { Code: var code })
            return;

        if (string.Equals(ViewModel.SelectedLanguageCode, code, StringComparison.OrdinalIgnoreCase))
            return;

        ViewModel.SelectLanguage(code);
        App.ApplyOobeUiLanguage(code);
        // Skip huge legal text until the License step to keep language switching snappy.
        ViewModel.RefreshLocalizedStrings(includeLegal: false);
    }

    private void SyncOobeLogos()
    {
        var lightShell = ActualTheme != ElementTheme.Dark;
        var uri = new Uri(lightShell
            ? "ms-appx:///Assets/ardel-logo-ink.png"
            : "ms-appx:///Assets/ardel-logo.png");
        var image = new BitmapImage(uri);
        WelcomeLogo.Source = image;
        AnimLangLogo.Source = image;
    }
}
