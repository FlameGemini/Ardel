using System.Collections.ObjectModel;
using Ardel.Launcher;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ardel.Launcher.ViewModels;

public sealed partial class OobeViewModel : ObservableObject
{
    public const int StepCount = 6;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private string _selectedLanguageCode = string.Empty;

    [ObservableProperty]
    private string _selectedThemeCode = "Default";

    [ObservableProperty]
    private bool _licenseAccepted;

    [ObservableProperty]
    private bool _showLangLogo;

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = [];
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = [];
    public ObservableCollection<OobeTutorialStepItem> TutorialSteps { get; } = [];

    public OobeViewModel()
    {
        // Defer About legal body until the License step — it is multi-KB per language.
        RefreshLocalizedStrings(includeLegal: false);
    }

    public bool IsWelcomeStep => CurrentStep == (int)OobeStep.Welcome;
    public bool IsThemeStep => CurrentStep == (int)OobeStep.Theme;
    public bool IsTutorialStep => CurrentStep == (int)OobeStep.Tutorial;
    public bool IsCompleteStep => CurrentStep == (int)OobeStep.Complete;
    public bool IsSplitLayout => CurrentStep != (int)OobeStep.Welcome;
    public bool UsesFullWidthContent => IsThemeStep || IsTutorialStep || IsCompleteStep;
    public bool ShowSideArt => IsSplitLayout && !UsesFullWidthContent;
    public bool ShowLanguageLogoArt => CurrentStep == (int)OobeStep.Language && ShowLangLogo;
    public int ContentColumn => UsesFullWidthContent ? 0 : 2;
    public int ContentColumnSpan => UsesFullWidthContent ? 3 : 1;
    public bool IsLastStep => CurrentStep >= StepCount - 1;
    public bool CanGoBack => CurrentStep > 0;

    public bool CanGoNext => CurrentStep switch
    {
        (int)OobeStep.Language => true,
        (int)OobeStep.Welcome => true,
        (int)OobeStep.License => LicenseAccepted,
        (int)OobeStep.Theme => !string.IsNullOrWhiteSpace(SelectedThemeCode),
        (int)OobeStep.Tutorial => true,
        (int)OobeStep.Complete => true,
        _ => false
    };

    public string PreviousLabel { get; private set; } = string.Empty;
    public string NextButtonText { get; private set; } = string.Empty;
    public string LegalNoticeText { get; private set; } = string.Empty;
    public string OfficialPurchaseEncourageText { get; private set; } = string.Empty;

    public string StepLanguageTitle { get; private set; } = string.Empty;
    public string StepLanguageSubtitle { get; private set; } = string.Empty;
    public string StepWelcomeTitle { get; private set; } = string.Empty;
    public string StepWelcomeSubtitle { get; private set; } = string.Empty;
    public string StepLicenseTitle { get; private set; } = string.Empty;
    public string StepLicenseAgree { get; private set; } = string.Empty;
    public string StepThemeTitle { get; private set; } = string.Empty;
    public string StepThemeSubtitle { get; private set; } = string.Empty;
    public string SelectedThemeDescription { get; private set; } = string.Empty;
    public string TutorialTitle { get; private set; } = string.Empty;
    public string TutorialSubtitle { get; private set; } = string.Empty;
    public string StepCompleteTitle { get; private set; } = string.Empty;
    public string StepCompleteSubtitle { get; private set; } = string.Empty;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsThemeStep));
        OnPropertyChanged(nameof(IsTutorialStep));
        OnPropertyChanged(nameof(IsCompleteStep));
        OnPropertyChanged(nameof(IsSplitLayout));
        OnPropertyChanged(nameof(UsesFullWidthContent));
        OnPropertyChanged(nameof(ShowSideArt));
        OnPropertyChanged(nameof(ShowLanguageLogoArt));
        OnPropertyChanged(nameof(ContentColumn));
        OnPropertyChanged(nameof(ContentColumnSpan));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        RefreshNextButtonText();
        RefreshCommands();
    }

    partial void OnSelectedLanguageCodeChanged(string value)
    {
        PersonalizationOptions.RefreshLanguageSelection(LanguageOptions, value);
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnSelectedThemeCodeChanged(string value)
    {
        PersonalizationOptions.RefreshThemeSelection(ThemeOptions, value);
        UpdateSelectedThemeDescription();
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnLicenseAcceptedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoNext));
        RefreshCommands();
    }

    partial void OnShowLangLogoChanged(bool value) =>
        OnPropertyChanged(nameof(ShowLanguageLogoArt));

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (CurrentStep <= 0)
            return;

        CurrentStep--;
        if (CurrentStep == (int)OobeStep.Language)
            ShowLangLogo = false;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void GoNext()
    {
        if (!CanGoNext)
            return;

        if (CurrentStep == (int)OobeStep.Language)
        {
            App.ApplyOobeUiLanguage(SelectedLanguageCode);
            RefreshLocalizedStrings(includeLegal: false);
        }

        if (IsLastStep)
        {
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentStep++;
    }

    public event EventHandler? Completed;

    public void SelectLanguage(string code)
    {
        SelectedLanguageCode = code;
        ShowLangLogo = true;
    }

    public void SelectTheme(string code) => SelectedThemeCode = code;

    public void EnsureLegalNoticeLoaded()
    {
        if (!string.IsNullOrEmpty(LegalNoticeText))
            return;

        LegalNoticeText = Loc.Get(LocKeys.About_Disclaimer);
        OnPropertyChanged(nameof(LegalNoticeText));
    }

    public void RefreshLocalizedStrings(bool includeLegal = true)
    {
        PersonalizationOptions.FillLanguageOptions(LanguageOptions, SelectedLanguageCode);
        PersonalizationOptions.FillThemeOptions(ThemeOptions, SelectedThemeCode);

        PreviousLabel = Loc.Get(LocKeys.Oobe_Previous);
        if (includeLegal || CurrentStep == (int)OobeStep.License)
            LegalNoticeText = Loc.Get(LocKeys.About_Disclaimer);
        else
            LegalNoticeText = string.Empty;
        OfficialPurchaseEncourageText = Loc.Get(LocKeys.About_OfficialPurchaseEncourage);
        StepLanguageTitle = Loc.Get(LocKeys.Oobe_StepLanguage_Title);
        StepLanguageSubtitle = Loc.Get(LocKeys.Oobe_StepLanguage_Subtitle);
        StepWelcomeTitle = Loc.Get(LocKeys.Oobe_StepWelcome_Title);
        StepWelcomeSubtitle = Loc.Get(LocKeys.Oobe_StepWelcome_Subtitle);
        StepLicenseTitle = Loc.Get(LocKeys.Oobe_StepLicense_Title);
        StepLicenseAgree = Loc.Get(LocKeys.Oobe_StepLicense_Agree);
        StepThemeTitle = Loc.Get(LocKeys.Oobe_StepTheme_Title);
        StepThemeSubtitle = Loc.Get(LocKeys.Oobe_StepTheme_Subtitle);
        TutorialTitle = Loc.Get(LocKeys.Oobe_StepTutorial_Title);
        TutorialSubtitle = Loc.Get(LocKeys.Oobe_StepTutorial_Subtitle);
        RefreshTutorialSteps();
        StepCompleteTitle = Loc.Get(LocKeys.Oobe_StepComplete_Title);
        StepCompleteSubtitle = Loc.Get(LocKeys.Oobe_StepComplete_Subtitle);
        RefreshNextButtonText();
        UpdateSelectedThemeDescription();

        NotifyLocalizedProperties();
        RefreshCommands();
    }

    private void RefreshNextButtonText()
    {
        NextButtonText = IsLastStep ? Loc.Get(LocKeys.Oobe_Finish) : Loc.Get(LocKeys.Oobe_Next);
        OnPropertyChanged(nameof(NextButtonText));
    }

    private void NotifyLocalizedProperties()
    {
        OnPropertyChanged(nameof(PreviousLabel));
        OnPropertyChanged(nameof(LegalNoticeText));
        OnPropertyChanged(nameof(OfficialPurchaseEncourageText));
        OnPropertyChanged(nameof(StepLanguageTitle));
        OnPropertyChanged(nameof(StepLanguageSubtitle));
        OnPropertyChanged(nameof(StepWelcomeTitle));
        OnPropertyChanged(nameof(StepWelcomeSubtitle));
        OnPropertyChanged(nameof(StepLicenseTitle));
        OnPropertyChanged(nameof(StepLicenseAgree));
        OnPropertyChanged(nameof(StepThemeTitle));
        OnPropertyChanged(nameof(StepThemeSubtitle));
        OnPropertyChanged(nameof(TutorialTitle));
        OnPropertyChanged(nameof(TutorialSubtitle));
        OnPropertyChanged(nameof(TutorialSteps));
        OnPropertyChanged(nameof(StepCompleteTitle));
        OnPropertyChanged(nameof(StepCompleteSubtitle));
    }

    private void UpdateSelectedThemeDescription()
    {
        var code = string.IsNullOrWhiteSpace(SelectedThemeCode) ? "Default" : SelectedThemeCode.Trim();
        var match = ThemeOptions.FirstOrDefault(
            option => string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase));
        SelectedThemeDescription = match?.Description ?? string.Empty;
        OnPropertyChanged(nameof(SelectedThemeDescription));
    }

    private void RefreshTutorialSteps()
    {
        TutorialSteps.Clear();
        TutorialSteps.Add(new OobeTutorialStepItem
        {
            Index = 1,
            Title = Loc.Get(LocKeys.Oobe_StepTutorial_Step1_Title),
            Detail = Loc.Get(LocKeys.Oobe_StepTutorial_Step1_Detail),
            IconGlyph = "\uE896",
        });
        TutorialSteps.Add(new OobeTutorialStepItem
        {
            Index = 2,
            Title = Loc.Get(LocKeys.Oobe_StepTutorial_Step2_Title),
            Detail = Loc.Get(LocKeys.Oobe_StepTutorial_Step2_Detail),
            IconGlyph = "\uE898",
        });
        TutorialSteps.Add(new OobeTutorialStepItem
        {
            Index = 3,
            Title = Loc.Get(LocKeys.Oobe_StepTutorial_Step3_Title),
            Detail = Loc.Get(LocKeys.Oobe_StepTutorial_Step3_Detail),
            IconGlyph = "\uE895",
        });
        TutorialSteps.Add(new OobeTutorialStepItem
        {
            Index = 4,
            Title = Loc.Get(LocKeys.Oobe_StepTutorial_Step4_Title),
            Detail = Loc.Get(LocKeys.Oobe_StepTutorial_Step4_Detail),
            IconGlyph = "\uE77B",
        });
        TutorialSteps.Add(new OobeTutorialStepItem
        {
            Index = 5,
            Title = Loc.Get(LocKeys.Oobe_StepTutorial_Step5_Title),
            Detail = Loc.Get(LocKeys.Oobe_StepTutorial_Step5_Detail),
            IconGlyph = "\uE768",
            IsLast = true,
        });
    }

    private void RefreshCommands()
    {
        GoBackCommand.NotifyCanExecuteChanged();
        GoNextCommand.NotifyCanExecuteChanged();
    }
}
