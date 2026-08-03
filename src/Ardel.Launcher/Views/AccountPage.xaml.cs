using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;
using Ardel.Launcher.ViewModels;

namespace Ardel.Launcher.Views;

public sealed partial class AccountPage : Page
{
    private bool _dialogOpen;
    private bool _suppressItemClick;

    public AccountViewModel ViewModel { get; }

    public AccountPage()
    {
        ViewModel = App.Services.GetRequiredService<AccountViewModel>();
        InitializeComponent();
        Loaded += (_, _) => PlayFade(RootGrid, 0, 1, 240);
        RootGrid.Opacity = 0;
    }

    private static void PlayFade(UIElement target, double from, double to, int ms)
    {
        target.Opacity = from;
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, target);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Completed += (_, _) => target.Opacity = to;
        sb.Children.Add(anim);
        sb.Begin();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private void AccountList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_suppressItemClick)
        {
            _suppressItemClick = false;
            return;
        }

        if (e.ClickedItem is AccountItemViewModel item)
            ViewModel.SelectAccountCommand.Execute(item);
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

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        _suppressItemClick = true;
        if (XamlRoot is null || _dialogOpen ||
            sender is not FrameworkElement { Tag: AccountItemViewModel item })
            return;

        var store = App.Services.GetRequiredService<AccountStore>();
        var record = store.Find(item.Id);
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

    private async void SkinAccount_Click(object sender, RoutedEventArgs e)
    {
        _suppressItemClick = true;
        if (XamlRoot is null || _dialogOpen ||
            sender is not FrameworkElement { Tag: AccountItemViewModel item })
            return;

        var skins = App.Services.GetRequiredService<SkinLibraryStore>();
        var window = App.Services.GetRequiredService<Window>();
        var library = item.Kind == AccountKind.Microsoft
            ? SkinLibraryKind.Microsoft
            : SkinLibraryKind.Offline;

        _dialogOpen = true;
        try
        {
            var picked = await PickSkinDialog.ShowAsync(XamlRoot, skins, window, library, item.SkinId);
            if (!string.Equals(picked, item.SkinId, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SetAccountSkin(item.Id, picked);
                await ViewModel.RefreshAsync();
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        _suppressItemClick = true;
        if (XamlRoot is null || _dialogOpen ||
            sender is not FrameworkElement { Tag: AccountItemViewModel item })
            return;

        _dialogOpen = true;
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.Get(LocKeys.Account_DeleteTitle),
                Content = Loc.Format(LocKeys.Account_DeleteConfirm, item.DisplayName),
                PrimaryButtonText = Loc.Get(LocKeys.Action_Delete),
                CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                return;

            ViewModel.DeleteAccount(item.Id);
            await ViewModel.RefreshAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
