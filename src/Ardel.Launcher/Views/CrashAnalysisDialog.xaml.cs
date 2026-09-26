using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Services.CrashAnalysis;

namespace Ardel.Launcher.Views;

public sealed partial class CrashAnalysisDialog : UserControl
{
    private CrashPresentModel _model = null!;

    public CrashAnalysisDialog()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(XamlRoot xamlRoot, CrashPresentModel model)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(model);

        var content = new CrashAnalysisDialog();
        content.Apply(model);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Crash_DialogTitle),
            Content = content,
            CloseButtonText = Loc.Get(LocKeys.Action_Close),
            DefaultButton = ContentDialogButton.Close
        };

        ConfigureButtons(dialog, model);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await content.HandlePrimaryAsync(model);
        else if (result == ContentDialogResult.Secondary)
            await content.HandleSecondaryAsync(model);
    }

    private static void ConfigureButtons(ContentDialog dialog, CrashPresentModel model)
    {
        switch (model.Tier)
        {
            case CrashTier.Known:
                if (!model.AutoOpenedLogs)
                    dialog.PrimaryButtonText = Loc.Get(LocKeys.Crash_OpenLogs);
                break;

            case CrashTier.Suspected:
                if (model.AutoOpenedLogs)
                {
                    dialog.PrimaryButtonText = Loc.Get(LocKeys.Crash_AskChatGpt);
                }
                else
                {
                    dialog.PrimaryButtonText = Loc.Get(LocKeys.Crash_OpenLogs);
                    dialog.SecondaryButtonText = Loc.Get(LocKeys.Crash_AskChatGpt);
                }
                break;

            case CrashTier.Unknown:
                dialog.PrimaryButtonText = Loc.Get(LocKeys.Crash_AskChatGpt);
                if (!model.AutoOpenedLogs)
                    dialog.SecondaryButtonText = Loc.Get(LocKeys.Crash_OpenLogs);
                break;
        }
    }

    private void Apply(CrashPresentModel model)
    {
        _model = model;
        TitleText.Text = model.Title;
        ExplainText.Text = model.Explain;
        SolutionText.Text = model.Solution;
        RuleIdText.Text = model.RuleId ?? string.Empty;
        RuleIdText.Visibility = string.IsNullOrEmpty(model.RuleId) ? Visibility.Collapsed : Visibility.Visible;

        if (model.ShowConfidence && !string.IsNullOrEmpty(model.ConfidenceLabel))
        {
            ConfidenceText.Text = model.ConfidenceLabel;
            ConfidenceText.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(model.ReportDescription))
        {
            ReportHeadText.Text = model.ReportDescription;
            ReportHeadBar.Visibility = Visibility.Visible;
        }

        EvidenceBox.Text = string.Join(Environment.NewLine, model.EvidenceLines);
        EvidenceExpander.IsExpanded = model.ExpandEvidence && model.EvidenceLines.Count > 0;
        EvidenceExpander.Visibility = model.EvidenceLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var chips = model.Mods
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m =>
            {
                var trimmed = m.Trim();
                if (trimmed.StartsWith("Caught exception from", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Suspected", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Mod:", StringComparison.OrdinalIgnoreCase))
                    return trimmed;
                return model.Tier == CrashTier.Suspected
                    ? "Suspected: " + trimmed
                    : "Caught exception from: " + trimmed;
            })
            .Take(8)
            .ToList();
        if (chips.Count > 0)
        {
            ChipList.ItemsSource = chips;
            ChipList.Visibility = Visibility.Visible;
        }

        switch (model.Tier)
        {
            case CrashTier.Known:
                BadgeText.Text = Loc.Get(LocKeys.Crash_BadgeKnown);
                BadgeIcon.Glyph = "\uE8A5"; // Page / document — pattern from file, not a green check verdict
                BadgeIcon.Foreground = (Brush)Application.Current.Resources["ArdelAccentBrush"];
                break;
            case CrashTier.Suspected:
                BadgeText.Text = Loc.Get(LocKeys.Crash_BadgeSuspected);
                BadgeIcon.Glyph = "\uE946"; // Info
                if (Application.Current.Resources.TryGetValue("SystemFillColorCautionBrush", out var caution) &&
                    caution is Brush cautionBrush)
                    BadgeIcon.Foreground = cautionBrush;
                break;
            default:
                BadgeText.Text = Loc.Get(LocKeys.Crash_BadgeUnknown);
                BadgeIcon.Glyph = "\uE9CE";
                BadgeIcon.Opacity = 0.7;
                break;
        }
    }

    private Task HandlePrimaryAsync(CrashPresentModel model)
    {
        switch (model.Tier)
        {
            case CrashTier.Known:
                CrashAnalyzer.OpenLogsFolder(model.LogsFolderPath);
                break;
            case CrashTier.Suspected:
                if (model.AutoOpenedLogs)
                    CrashAnalyzer.OpenChatGpt(model);
                else
                    CrashAnalyzer.OpenLogsFolder(model.LogsFolderPath);
                break;
            case CrashTier.Unknown:
                CrashAnalyzer.OpenChatGpt(model);
                break;
        }

        return Task.CompletedTask;
    }

    private Task HandleSecondaryAsync(CrashPresentModel model)
    {
        switch (model.Tier)
        {
            case CrashTier.Suspected:
                CrashAnalyzer.OpenChatGpt(model);
                break;
            case CrashTier.Unknown:
                CrashAnalyzer.OpenLogsFolder(model.LogsFolderPath);
                break;
        }

        return Task.CompletedTask;
    }

    private void CopyEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(EvidenceBox.Text ?? string.Empty);
            Clipboard.SetContent(data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CrashAnalysisDialog] copy failed: {ex.Message}");
        }
    }
}
