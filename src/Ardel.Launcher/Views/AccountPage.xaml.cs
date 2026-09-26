using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class AccountPage : Page
{
    private bool _dialogOpen;
    private bool _suppressItemClick;
    private bool _reordering;

    private byte[]? _pendingSkinBytes;
    private string? _pendingSkinPath;

    public AccountViewModel ViewModel { get; }

    public AccountPage()
    {
        ViewModel = App.Services.GetRequiredService<AccountViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await Task.Yield();
        await ViewModel.RefreshAsync(reloadAvatars: ViewModel.Items.Count == 0);
        ResetPendingSkin();
    }

    private void DetailScrollViewer_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        // Prevent WinUI from auto-scrolling down to focused child elements (e.g. RadioButtons)
        args.Handled = true;
    }

    private void AccountList_ItemClick(object sender, ItemClickEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("AccountPage.AccountList_ItemClick");
        if (_suppressItemClick || _reordering)
        {
            _suppressItemClick = false;
            return;
        }

        if (e.ClickedItem is AccountItemViewModel item)
        {
            ViewModel.SelectedAccount = item;
            ResetPendingSkin();
        }
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        using var _ = InteractionWatchdog.Profile("AccountPage.AccountList_SelectionChanged");
        if (_reordering)
            return;

        if (AccountList.SelectedItem is AccountItemViewModel item && item != ViewModel.SelectedAccount)
        {
            ViewModel.SelectedAccount = item;
            ResetPendingSkin();
        }
    }

    private void AccountList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        _reordering = true;
        _suppressItemClick = true;
    }

    private void AccountList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ViewModel.PersistOrder();
            _reordering = false;
        });
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || _dialogOpen)
            return;

        _dialogOpen = true;
        try
        {
            if (await AddAccountDialog.ShowAsync(XamlRoot, ViewModel))
                await ViewModel.RefreshAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private void SelectedAccountActivate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is not null)
        {
            ViewModel.SelectAccountCommand.Execute(ViewModel.SelectedAccount);
        }
    }

    private async void ChangeName_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || _dialogOpen || ViewModel.SelectedAccount is null)
            return;

        _dialogOpen = true;
        try
        {
            var updatedName = await ChangeNameDialog.ShowAsync(
                XamlRoot,
                ViewModel.SelectedAccount.Id,
                ViewModel.SelectedAccount.DisplayName,
                ViewModel);

            if (!string.IsNullOrWhiteSpace(updatedName))
            {
                ViewModel.SelectedAccount.DisplayName = updatedName;
                await ViewModel.RefreshAsync(reloadAvatars: false);
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void RenameOffline_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || _dialogOpen || ViewModel.SelectedAccount is null)
            return;

        var store = App.Services.GetRequiredService<AccountStore>();
        var record = store.Find(ViewModel.SelectedAccount.Id);
        if (record is null)
            return;

        _dialogOpen = true;
        try
        {
            if (await AddAccountDialog.ShowAsync(XamlRoot, ViewModel, record))
                await ViewModel.RefreshAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void RefreshSelectedMicrosoft_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
            return;

        try
        {
            ViewModel.StatusText = Loc.Get(LocKeys.Account_MicrosoftRefreshing);
            await ViewModel.RefreshMicrosoftAccountAsync(ViewModel.SelectedAccount.Id);
            await ViewModel.RefreshAsync(reloadAvatars: true);
            await ViewModel.LoadCapesForSelectedAccountAsync(force: true);
            ViewModel.StatusText = Loc.Get(LocKeys.Account_MicrosoftRefreshed);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = Loc.Format(LocKeys.Account_MicrosoftRefreshFailed, ex.Message);
        }
    }

    private async void DeleteSelectedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || _dialogOpen || ViewModel.SelectedAccount is null)
            return;

        var target = ViewModel.SelectedAccount;
        _dialogOpen = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get(LocKeys.Account_DeleteTitle),
                Content = Loc.Format(LocKeys.Account_DeleteConfirm, target.DisplayName),
                PrimaryButtonText = Loc.Get(LocKeys.Action_Delete),
                CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
                DefaultButton = ContentDialogButton.Close
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.DeleteAccount(target.Id);
                await ViewModel.RefreshAsync();
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void PickOfflineSkin_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || _dialogOpen || ViewModel.SelectedAccount is null)
            return;

        var skins = App.Services.GetRequiredService<SkinLibraryStore>();
        var window = App.Services.GetRequiredService<Window>();
        var target = ViewModel.SelectedAccount;

        _dialogOpen = true;
        try
        {
            var picked = await PickSkinDialog.ShowAsync(
                XamlRoot,
                skins,
                window,
                SkinLibraryKind.Offline,
                target.SkinId,
                target.Uuid);

            if (!string.Equals(picked, target.SkinId, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SetAccountSkin(target.Id, picked);
                await ViewModel.RefreshAsync();
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    #region Skin Management

    private async void UploadSkin_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
            return;

        try
        {
            var window = App.Services.GetRequiredService<Window>();
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            picker.FileTypeFilter.Add(".png");
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var bytes = await File.ReadAllBytesAsync(file.Path);
            if (bytes.Length < 64)
            {
                SkinStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                SkinStatusText.Text = Loc.Get(LocKeys.Skin_ImportRequired);
                return;
            }

            _pendingSkinBytes = bytes;
            _pendingSkinPath = file.Path;
            PendingSkinNameText.Text = Path.GetFileName(file.Path);
            PendingSkinBar.Visibility = Visibility.Visible;
            SaveArmModelButton.Visibility = Visibility.Collapsed;

            var preview = await Skin3DHeadHelper.TryCreateAsync(file.Path);
            if (preview is not null)
                DetailSkinPreview.Source = preview;

            SkinStatusText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            SkinStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            SkinStatusText.Text = ex.Message;
        }
    }

    private async void ConfirmSkinUpload_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is null || _pendingSkinBytes is null)
            return;

        var armModel = ViewModel.IsSlimArmModel ? SkinArmModel.Slim : SkinArmModel.Classic;

        ConfirmSkinUploadButton.IsEnabled = false;
        CancelSkinUploadButton.IsEnabled = false;
        UploadSkinButton.IsEnabled = false;
        SkinProgressBar.Visibility = Visibility.Visible;
        SkinStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        SkinStatusText.Text = Loc.Get(LocKeys.Account_Saving);

        try
        {
            await ViewModel.UploadMicrosoftSkinAsync(ViewModel.SelectedAccount.Id, _pendingSkinBytes, armModel);
            await ViewModel.RefreshAsync(reloadAvatars: true);

            ResetPendingSkin();
            SkinStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            SkinStatusText.Text = Loc.Get(LocKeys.Account_SkinSuccess);
        }
        catch (Exception ex)
        {
            SkinStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            SkinStatusText.Text = Loc.Format(LocKeys.Account_SkinFailed, ex.Message);
        }
        finally
        {
            ConfirmSkinUploadButton.IsEnabled = true;
            CancelSkinUploadButton.IsEnabled = true;
            UploadSkinButton.IsEnabled = true;
            SkinProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelSkinUpload_Click(object sender, RoutedEventArgs e)
    {
        ResetPendingSkin();
    }

    private void ArmModelRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (PendingSkinBar.Visibility != Visibility.Visible && ViewModel.SelectedAccount?.Kind == AccountKind.Microsoft)
        {
            var skins = App.Services.GetRequiredService<SkinLibraryStore>();
            var skin = skins.Find(ViewModel.SelectedAccount.SkinId);
            var isCurrentSlim = skin?.ArmModel == SkinArmModel.Slim;
            if (isCurrentSlim != ViewModel.IsSlimArmModel)
            {
                SaveArmModelButton.Visibility = Visibility.Visible;
            }
            else
            {
                SaveArmModelButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async void SaveArmModel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
            return;

        var skins = App.Services.GetRequiredService<SkinLibraryStore>();
        var skin = skins.Find(ViewModel.SelectedAccount.SkinId);
        byte[]? pngBytes = null;
        if (skin is not null)
        {
            var path = skins.GetAbsolutePath(skin);
            if (File.Exists(path))
                pngBytes = await File.ReadAllBytesAsync(path);
        }

        if (pngBytes is null)
            return;

        var armModel = ViewModel.IsSlimArmModel ? SkinArmModel.Slim : SkinArmModel.Classic;
        SaveArmModelButton.IsEnabled = false;
        SkinProgressBar.Visibility = Visibility.Visible;
        SkinStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        SkinStatusText.Text = Loc.Get(LocKeys.Account_Saving);

        try
        {
            await ViewModel.UploadMicrosoftSkinAsync(ViewModel.SelectedAccount.Id, pngBytes, armModel);
            await ViewModel.RefreshAsync(reloadAvatars: true);
            SaveArmModelButton.Visibility = Visibility.Collapsed;

            SkinStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            SkinStatusText.Text = Loc.Get(LocKeys.Account_SkinSuccess);
        }
        catch (Exception ex)
        {
            SkinStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            SkinStatusText.Text = Loc.Format(LocKeys.Account_SkinFailed, ex.Message);
        }
        finally
        {
            SaveArmModelButton.IsEnabled = true;
            SkinProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ResetPendingSkin()
    {
        _pendingSkinBytes = null;
        _pendingSkinPath = null;
        if (PendingSkinBar is not null)
            PendingSkinBar.Visibility = Visibility.Collapsed;
        if (SaveArmModelButton is not null)
            SaveArmModelButton.Visibility = Visibility.Collapsed;
        if (SkinStatusText is not null)
            SkinStatusText.Text = string.Empty;
        if (ViewModel.SelectedAccount?.AvatarImage is not null && DetailSkinPreview is not null)
        {
            DetailSkinPreview.Source = ViewModel.SelectedAccount.AvatarImage;
        }
    }

    #endregion

    #region Cape Management

    private DateTimeOffset _capeCooldownUntil = DateTimeOffset.MinValue;
    private bool _isCapeOperating;

    private async void CapeCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CapeItemViewModel clicked })
            await ToggleCapeAsync(clicked);
    }

    private async void CapeGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CapeItemViewModel clicked)
            await ToggleCapeAsync(clicked);
    }

    private async Task ToggleCapeAsync(CapeItemViewModel clicked)
    {
        if (ViewModel.SelectedAccount is null || _isCapeOperating)
            return;

        if (DateTimeOffset.UtcNow < _capeCooldownUntil)
        {
            var waitSec = (int)Math.Ceiling((_capeCooldownUntil - DateTimeOffset.UtcNow).TotalSeconds);
            CapeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            CapeStatusText.Text = Loc.Format(LocKeys.Account_MojangRateLimitWait, waitSec);
            return;
        }

        _isCapeOperating = true;
        CapeStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        CapeStatusText.Text = Loc.Get(LocKeys.Account_Saving);

        try
        {
            if (clicked.IsCurrentlyActive)
            {
                await ViewModel.EquipSelectedAccountCapeAsync(null);
            }
            else
            {
                await ViewModel.EquipSelectedAccountCapeAsync(clicked.Id);
            }

            _capeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            CapeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            CapeStatusText.Text = Loc.Get(LocKeys.Account_CapeSuccess);
        }
        catch (Exception ex)
        {
            CapeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            if (ex.Message.Contains("MojangRateLimit", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("429"))
            {
                _capeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(15);
                CapeStatusText.Text = Loc.Get(LocKeys.Account_MojangRateLimit);
            }
            else
            {
                CapeStatusText.Text = Loc.Format(LocKeys.Account_CapeFailed, ex.Message);
            }
        }
        finally
        {
            _isCapeOperating = false;
        }
    }

    private async void UnequipCape_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is null || _isCapeOperating)
            return;

        if (DateTimeOffset.UtcNow < _capeCooldownUntil)
        {
            var waitSec = (int)Math.Ceiling((_capeCooldownUntil - DateTimeOffset.UtcNow).TotalSeconds);
            CapeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            CapeStatusText.Text = Loc.Format(LocKeys.Account_MojangRateLimitWait, waitSec);
            return;
        }

        _isCapeOperating = true;
        CapeStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        CapeStatusText.Text = Loc.Get(LocKeys.Account_Saving);

        try
        {
            await ViewModel.EquipSelectedAccountCapeAsync(null);
            _capeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            CapeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            CapeStatusText.Text = Loc.Get(LocKeys.Account_CapeSuccess);
        }
        catch (Exception ex)
        {
            CapeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            if (ex.Message.Contains("MojangRateLimit", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("429"))
            {
                _capeCooldownUntil = DateTimeOffset.UtcNow.AddSeconds(15);
                CapeStatusText.Text = Loc.Get(LocKeys.Account_MojangRateLimit);
            }
            else
            {
                CapeStatusText.Text = Loc.Format(LocKeys.Account_CapeFailed, ex.Message);
            }
        }
        finally
        {
            _isCapeOperating = false;
        }
    }

    #endregion
}
