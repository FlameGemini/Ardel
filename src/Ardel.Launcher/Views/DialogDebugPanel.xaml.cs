using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.Services.CrashAnalysis;

namespace Ardel.Launcher.Views;

/// <summary>
/// Dev-only UI layout check for crash dialogs. Does not invent crash evidence —
/// only shows Loc title/explain/solution with an explicit empty-evidence note.
/// </summary>
public sealed partial class DialogDebugPanel : UserControl
{
    private sealed record RuleOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    public DialogDebugPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (RuleCombo.Items.Count > 0)
            return;

        var options = new List<RuleOption>();
        foreach (var field in typeof(LocKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
                continue;
            var key = (string)field.GetRawConstantValue()!;
            if (!key.StartsWith("Crash_", StringComparison.Ordinal) || !key.EndsWith("_Title", StringComparison.Ordinal))
                continue;
            var body = key["Crash_".Length..^"_Title".Length];
            // Layout preview is for high-confidence R Loc only (USR is not user-facing).
            if (!body.StartsWith("R", StringComparison.Ordinal))
                continue;

            options.Add(new RuleOption(body, $"{body} — {Loc.Get(key)}"));
        }

        options.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        foreach (var opt in options)
            RuleCombo.Items.Add(opt);

        if (RuleCombo.Items.Count > 0)
            RuleCombo.SelectedIndex = 0;
    }

    private XamlRoot? RequireRoot()
    {
        var root = XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;
        if (root is null)
            SetStatus("No XamlRoot.");
        return root;
    }

    private static string LogsPath
    {
        get
        {
            var path = Path.Combine(Path.GetTempPath(), "ardel-dialog-debug", "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private async void PreviewKnown_Click(object sender, RoutedEventArgs e) =>
        await ShowCrashAsync(BuildKnown("R001")).ConfigureAwait(true);

    private async void PreviewUnknown_Click(object sender, RoutedEventArgs e) =>
        await ShowCrashAsync(BuildUnknown()).ConfigureAwait(true);

    private async void PreviewSelectedRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleCombo.SelectedItem is not RuleOption opt)
        {
            SetStatus(Loc.Get(LocKeys.Settings_DialogDebugPickRule));
            return;
        }

        await ShowCrashAsync(BuildKnown(opt.Id)).ConfigureAwait(true);
    }

    private async Task ShowCrashAsync(CrashPresentModel model)
    {
        var root = RequireRoot();
        if (root is null)
            return;
        await CrashAnalysisDialog.ShowAsync(root, model).ConfigureAwait(true);
        SetStatus($"{model.Tier} / {model.RuleId ?? "—"}");
    }

    private static LauncherSettings ReadSettings()
    {
        try
        {
            return App.Services.GetRequiredService<SettingsService>().Load();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    private CrashPresentModel BuildKnown(string ruleId)
    {
        var settings = ReadSettings();
        return new CrashPresentModel
        {
            Tier = CrashTier.Known,
            RuleId = ruleId,
            Title = Loc.Get($"Crash_{ruleId}_Title"),
            Explain = Loc.Get($"Crash_{ruleId}_Explain"),
            Solution = Loc.Get($"Crash_{ruleId}_Solution"),
            EvidenceLines = [],
            LogsFolderPath = LogsPath,
            AutoOpenedLogs = settings.CrashAnalysisAutoOpenLogs,
            ShowConfidence = false,
            ExpandEvidence = false,
            ConfidenceLabel = null
        };
    }

    private CrashPresentModel BuildUnknown()
    {
        var settings = ReadSettings();
        return new CrashPresentModel
        {
            Tier = CrashTier.Unknown,
            Title = Loc.Get(LocKeys.Crash_Unknown_Title),
            Explain = Loc.Get(LocKeys.Crash_Unknown_Explain),
            Solution = Loc.Get(LocKeys.Crash_Unknown_Solution),
            EvidenceLines = [],
            LogsFolderPath = LogsPath,
            AutoOpenedLogs = settings.CrashAnalysisAutoOpenLogs,
            ShowConfidence = false,
            ExpandEvidence = false
        };
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
