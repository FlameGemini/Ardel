using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class ChangeNameDialog : UserControl
{
    private readonly string _accountId;
    private readonly AccountViewModel _accountsVm;
    private ContentDialog? _dialog;

    public ChangeNameDialog(string accountId, string currentName, AccountViewModel accountsVm)
    {
        _accountId = accountId;
        _accountsVm = accountsVm;

        InitializeComponent();
        CurrentNameText.Text = currentName;
        Loaded += (_, _) => _ = CheckEligibilityAsync();
    }

    private async Task CheckEligibilityAsync()
    {
        ProgressBar.Visibility = Visibility.Visible;
        try
        {
            var (allowed, _) = await _accountsVm.CheckNameChangeEligibilityAsync(_accountId);
            if (!allowed)
            {
                CooldownInfoBar.IsOpen = true;
                NewNameBox.IsEnabled = false;
                CheckButton.IsEnabled = false;
                if (_dialog is not null)
                    _dialog.IsPrimaryButtonEnabled = false;
            }
            else
            {
                CooldownInfoBar.IsOpen = false;
                NewNameBox.IsEnabled = true;
                CheckButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChangeNameDialog] Eligibility check warning: {ex.Message}");
        }
        finally
        {
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void NewNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        StatusText.Text = string.Empty;
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text.Trim();
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9_]{3,16}$"))
        {
            StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            StatusText.Text = Loc.Get(LocKeys.Account_NameUnavailable);
            return;
        }

        CheckButton.IsEnabled = false;
        try
        {
            var (available, _) = await _accountsVm.CheckNameAvailabilityAsync(_accountId, name);
            if (available)
            {
                StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                StatusText.Text = Loc.Get(LocKeys.Account_NameAvailable);
            }
            else
            {
                StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                StatusText.Text = Loc.Get(LocKeys.Account_NameUnavailable);
            }
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            StatusText.Text = ex.Message;
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    public static async Task<string?> ShowAsync(
        XamlRoot xamlRoot,
        string accountId,
        string currentName,
        AccountViewModel accountsVm)
    {
        var content = new ChangeNameDialog(accountId, currentName, accountsVm);
        string? resultName = null;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Account_TabName),
            Content = content,
            PrimaryButtonText = Loc.Get(LocKeys.Account_NameSave),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary
        };

        content._dialog = dialog;

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var name = content.NewNameBox.Text.Trim();
                if (!Regex.IsMatch(name, "^[a-zA-Z0-9_]{3,16}$"))
                {
                    content.StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                    content.StatusText.Text = Loc.Get(LocKeys.Account_NameUnavailable);
                    args.Cancel = true;
                    return;
                }

                content.ProgressBar.Visibility = Visibility.Visible;
                content.StatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                content.StatusText.Text = Loc.Get(LocKeys.Account_Saving);

                var updated = await accountsVm.ChangeMicrosoftAccountNameAsync(accountId, name);
                resultName = updated;
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                content.StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                content.StatusText.Text = Loc.Format(LocKeys.Account_NameFailed, ex.Message);
            }
            finally
            {
                content.ProgressBar.Visibility = Visibility.Collapsed;
                deferral.Complete();
            }
        };

        var result = await dialog.SafeShowAsync();
        return result == ContentDialogResult.Primary ? resultName : null;
    }
}
