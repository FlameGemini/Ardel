using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class AddAccountDialog : UserControl
{
    private readonly AccountViewModel _accounts;
    private readonly bool _isEdit;
    private readonly string? _editId;
    private AccountKind _kind = AccountKind.Offline;
    private bool _segmentReady;

    public AddAccountDialog(AccountViewModel accounts, AccountRecord? edit = null)
    {
        _accounts = accounts;
        _isEdit = edit is not null;
        _editId = edit?.Id;
        InitializeComponent();

        if (edit is not null)
        {
            NameBox.Text = edit.DisplayName;
            _kind = edit.Kind;
            if (edit.Kind == AccountKind.Microsoft)
            {
                OfflineSegmentButton.IsEnabled = false;
                MicrosoftSegmentButton.IsEnabled = false;
            }
        }

        NameBox.TextChanged += (_, _) => RefreshOfflineFields();
        NameBox.BeforeTextChanging += OnNameBeforeTextChanging;
        NameBox.InputScope = new Microsoft.UI.Xaml.Input.InputScope
        {
            Names =
            {
                new Microsoft.UI.Xaml.Input.InputScopeName(
                    Microsoft.UI.Xaml.Input.InputScopeNameValue.AlphanumericHalfWidth)
            }
        };

        Loaded += OnLoaded;
        SizeChanged += (_, _) => LayoutSegment(animate: false);
        RefreshModeVisual(animate: false);
        RefreshOfflineFields();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _segmentReady = true;
        LayoutSegment(animate: false);
    }

    private static void OnNameBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        foreach (var ch in args.NewText)
        {
            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
                continue;
            args.Cancel = true;
            return;
        }
    }

    public bool IsValid { get; private set; }
    public event EventHandler? ValidityChanged;
    public bool IsOffline => _kind == AccountKind.Offline;

    public void Apply()
    {
        if (_kind != AccountKind.Offline || !IsValid)
            throw new InvalidOperationException(Loc.Get(LocKeys.Account_MicrosoftComingSoon));

        var name = NameBox.Text.Trim();
        if (_isEdit && _editId is not null)
            _accounts.UpdateOfflineAccountName(_editId, name);
        else
            _accounts.CreateOfflineAccount(name, SkinLibraryStore.BuiltinSteveOfflineId);
    }

    private void OfflineSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_isEdit && _kind == AccountKind.Microsoft)
            return;
        _kind = AccountKind.Offline;
        RefreshModeVisual(animate: true);
        RefreshOfflineFields();
    }

    private void MicrosoftSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_isEdit)
            return;
        _kind = AccountKind.Microsoft;
        RefreshModeVisual(animate: true);
        RefreshOfflineFields();
    }

    private void RefreshModeVisual(bool animate)
    {
        var offline = _kind == AccountKind.Offline;
        OfflinePane.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        MicrosoftPane.Visibility = offline ? Visibility.Collapsed : Visibility.Visible;
        LayoutSegment(animate);

        OfflineSegmentLabel.Foreground = offline
            ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        OfflineSegmentLabel.Opacity = offline ? 1 : 0.7;
        MicrosoftSegmentLabel.Foreground = !offline
            ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        MicrosoftSegmentLabel.Opacity = !offline ? 1 : 0.7;
    }

    private void LayoutSegment(bool animate)
    {
        if (!_segmentReady || SegmentTrack.ActualWidth <= 0)
            return;

        var half = SegmentTrack.ActualWidth / 2;
        SegmentThumb.Width = half;
        var target = _kind == AccountKind.Offline ? 0 : half;

        if (!animate)
        {
            SegmentThumbOffset.X = target;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, SegmentThumbOffset);
        Storyboard.SetTargetProperty(anim, "X");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void RefreshOfflineFields()
    {
        if (_kind != AccountKind.Offline)
        {
            IsValid = false;
            ValidityChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var trimmed = NameBox.Text.Trim();
        var error = NameRules.ValidatePlayerName(NameBox.Text);
        if (error is null)
        {
            UuidText.Text = Loc.Format(LocKeys.Account_UuidLabel, OfflinePlayerUuid.FromPlayerName(trimmed));
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = string.Empty;
            IsValid = true;
        }
        else
        {
            UuidText.Text = Loc.Format(LocKeys.Account_UuidLabel, "—");
            // Empty: just disable Save. Only show an error once the user has typed something invalid.
            if (string.IsNullOrEmpty(trimmed))
            {
                ErrorText.Visibility = Visibility.Collapsed;
                ErrorText.Text = string.Empty;
            }
            else
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
            }

            IsValid = false;
        }

        ValidityChanged?.Invoke(this, EventArgs.Empty);
    }

    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        AccountViewModel accounts,
        AccountRecord? edit = null)
    {
        await Task.Yield();
        await accounts.SkinsForAsync(AccountKind.Offline).ConfigureAwait(true);
        var content = new AddAccountDialog(accounts, edit);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = edit is null
                ? Loc.Get(LocKeys.Account_AddTitle)
                : Loc.Get(LocKeys.Account_EditTitle),
            Content = content,
            PrimaryButtonText = Loc.Get(LocKeys.Action_Save),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = content.IsValid
        };

        content.ValidityChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = content.IsValid && content.IsOffline;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return false;

        content.Apply();
        return true;
    }
}
