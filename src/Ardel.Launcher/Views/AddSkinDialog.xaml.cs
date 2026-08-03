using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.Views;

public sealed partial class AddSkinDialog : UserControl
{
    private readonly SkinLibraryStore _store;
    private readonly Window _window;
    private SkinLibraryKind _library = SkinLibraryKind.Offline;
    private string? _pickedPath;

    public AddSkinDialog(SkinLibraryStore store, Window window, SkinLibraryKind? fixedLibrary = null)
    {
        _store = store;
        _window = window;
        InitializeComponent();
        if (fixedLibrary is { } lib)
        {
            _library = lib;
            LibrarySwitch.Visibility = Visibility.Collapsed;
            if (lib == SkinLibraryKind.Microsoft)
                MicrosoftLibRadio.IsChecked = true;
            else
                OfflineLibRadio.IsChecked = true;
        }

        NameBox.TextChanged += (_, _) => RefreshValidity();
        RefreshValidity();
    }

    public bool IsValid { get; private set; }
    public event EventHandler? ValidityChanged;

    public async Task ApplyAsync()
    {
        if (!IsValid || _pickedPath is null)
            throw new InvalidOperationException(Loc.Get(LocKeys.Skin_ImportRequired));

        var model = SlimRadio.IsChecked == true ? SkinArmModel.Slim : SkinArmModel.Classic;
        await _store.ImportAsync(NameBox.Text.Trim(), _library, model, _pickedPath)
            .ConfigureAwait(true);
    }

    private void LibraryKind_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        _library = MicrosoftLibRadio.IsChecked == true
            ? SkinLibraryKind.Microsoft
            : SkinLibraryKind.Offline;
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
            picker.FileTypeFilter.Add(".png");
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            _pickedPath = file.Path;
            PathText.Text = file.Path;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = Path.GetFileNameWithoutExtension(file.Name);
            RefreshValidity();
        }
        catch (Exception ex)
        {
            ErrorText.Text = Loc.Format(LocKeys.Skin_ImportFailed, ex.Message);
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void RefreshValidity()
    {
        var nameError = NameRules.ValidateSkinName(NameBox.Text);
        var fileOk = !string.IsNullOrWhiteSpace(_pickedPath) && File.Exists(_pickedPath);
        if (nameError is not null)
        {
            ErrorText.Text = nameError;
            ErrorText.Visibility = Visibility.Visible;
        }
        else if (!fileOk)
        {
            ErrorText.Text = Loc.Get(LocKeys.Skin_ImportRequired);
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = string.Empty;
        }

        IsValid = nameError is null && fileOk;
        ValidityChanged?.Invoke(this, EventArgs.Empty);
    }

    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        SkinLibraryStore store,
        Window window,
        SkinLibraryKind? fixedLibrary = null)
    {
        await Task.Yield();
        var content = new AddSkinDialog(store, window, fixedLibrary);
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Skin_AddTitle),
            Content = content,
            PrimaryButtonText = Loc.Get(LocKeys.Action_Save),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false
        };
        content.ValidityChanged += (_, _) => dialog.IsPrimaryButtonEnabled = content.IsValid;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return false;

        await content.ApplyAsync().ConfigureAwait(true);
        return true;
    }
}
