using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.Views;

public sealed partial class PickSkinDialog : UserControl
{
    private readonly SkinLibraryStore _store;
    private readonly Window _window;
    private readonly SkinLibraryKind _library;
    private readonly List<SkinOptionRow> _rows = [];
    private string? _selectedId;

    public PickSkinDialog(
        SkinLibraryStore store,
        Window window,
        SkinLibraryKind library,
        string? currentSkinId)
    {
        _store = store;
        _window = window;
        _library = library;
        _selectedId = currentSkinId;
        InitializeComponent();
    }

    public bool IsValid { get; private set; }
    public string? SelectedSkinId => _selectedId;
    public event EventHandler? ValidityChanged;

    public async Task LoadAsync()
    {
        await _store.EnsureReadyAsync().ConfigureAwait(true);
        OptionsPanel.Children.Clear();
        _rows.Clear();

        foreach (var skin in _store.ListPickerOptions(_library))
        {
            var row = await CreateRowAsync(skin).ConfigureAwait(true);
            _rows.Add(row);
            OptionsPanel.Children.Add(row.Root);
        }

        if (_rows.Count == 0)
        {
            IsValid = false;
            ValidityChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var match = _rows.FirstOrDefault(r =>
            string.Equals(r.Skin.Id, _selectedId, StringComparison.OrdinalIgnoreCase));
        SelectRow(match ?? _rows[0]);
    }

    private async Task<SkinOptionRow> CreateRowAsync(SkinRecord skin)
    {
        var radio = new RadioButton
        {
            GroupName = "PickSkinOption",
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var previewBorder = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1)
        };
        var previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed
        };
        previewBorder.Child = previewImage;

        var title = new TextBlock
        {
            Text = skin.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var subtitle = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        };

        var textStack = new StackPanel { Spacing = 4 };
        textStack.Children.Add(title);
        textStack.Children.Add(subtitle);

        ComboBox? armModelBox = null;
        if (skin.IsCustomSlot)
        {
            armModelBox = new ComboBox
            {
                MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
                SelectedIndex = skin.ArmModel == SkinArmModel.Slim ? 1 : 0
            };
            armModelBox.Items.Add(Loc.Get(LocKeys.Skin_ModelSteve));
            armModelBox.Items.Add(Loc.Get(LocKeys.Skin_ModelAlex));
            textStack.Children.Add(armModelBox);
            subtitle.Text = skin.IsConfigured
                ? Loc.Get(LocKeys.Skin_ArmModel)
                : Loc.Get(LocKeys.Account_SkinSlotEmpty);
        }
        else
        {
            UpdateSubtitle(subtitle, skin);
        }

        var importButton = new Button
        {
            Content = skin.IsCustomSlot && !skin.IsConfigured
                ? Loc.Get(LocKeys.Skin_Import)
                : Loc.Get(LocKeys.Account_SkinReplace),
            Visibility = skin.IsCustomSlot ? Visibility.Visible : Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 6, 10, 6)
        };

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 8, 10, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(radio);
        Grid.SetColumn(previewBorder, 1);
        grid.Children.Add(previewBorder);
        Grid.SetColumn(textStack, 2);
        grid.Children.Add(textStack);
        Grid.SetColumn(importButton, 3);
        grid.Children.Add(importButton);

        var card = new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1)
        };

        var row = new SkinOptionRow(skin, card, radio, previewImage, subtitle, importButton, armModelBox);

        radio.Checked += (_, _) => SelectRow(row);
        card.Tapped += (_, _) =>
        {
            radio.IsChecked = true;
            SelectRow(row);
        };
        importButton.Click += async (_, _) => await ImportIntoSlotAsync(row).ConfigureAwait(true);

        if (armModelBox is not null)
        {
            armModelBox.SelectionChanged += (_, _) => PersistCustomArm(row);
            armModelBox.Tapped += (_, e) => e.Handled = true;
        }

        if (skin.IsConfigured || skin.IsBuiltIn)
        {
            var path = _store.GetAbsolutePath(skin);
            var image = await SkinPreviewHelper.TryCreateHeadPreviewAsync(path, 96).ConfigureAwait(true);
            if (image is not null)
            {
                previewImage.Source = image;
                previewImage.Visibility = Visibility.Visible;
            }
        }

        return row;
    }

    private void PersistCustomArm(SkinOptionRow row)
    {
        if (!row.Skin.IsCustomSlot)
            return;

        var model = row.SelectedArmModel;
        row.Skin.ArmModel = model;
        try
        {
            _store.SetSlotArmModel(row.Skin.Id, model);
        }
        catch
        {
            // ignore transient store errors while UI is open
        }
    }

    private static void UpdateSubtitle(TextBlock subtitle, SkinRecord skin)
    {
        if (skin.IsCustomSlot && !skin.IsConfigured)
        {
            subtitle.Text = Loc.Get(LocKeys.Account_SkinSlotEmpty);
            return;
        }

        subtitle.Text = skin.ArmModel == SkinArmModel.Slim
            ? Loc.Get(LocKeys.Skin_ModelAlex)
            : Loc.Get(LocKeys.Skin_ModelSteve);
    }

    private void SelectRow(SkinOptionRow row)
    {
        foreach (var r in _rows)
            r.Radio.IsChecked = ReferenceEquals(r, row);

        _selectedId = row.Skin.Id;
        IsValid = row.Skin.IsBuiltIn || row.Skin.IsConfigured;
        ValidityChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ImportIntoSlotAsync(SkinOptionRow row)
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

            var model = row.SelectedArmModel;
            var updated = await _store.ReplaceSlotAsync(row.Skin.Id, file.Path, model).ConfigureAwait(true);
            row.Skin.IsConfigured = updated.IsConfigured;
            row.Skin.ArmModel = updated.ArmModel;
            row.ImportButton.Content = Loc.Get(LocKeys.Account_SkinReplace);
            if (row.Skin.IsCustomSlot)
                row.Subtitle.Text = Loc.Get(LocKeys.Skin_ArmModel);
            else
                UpdateSubtitle(row.Subtitle, row.Skin);

            var path = _store.GetAbsolutePath(row.Skin);
            var image = await SkinPreviewHelper.TryCreateHeadPreviewAsync(path, 96).ConfigureAwait(true);
            row.Preview.Source = image;
            row.Preview.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;

            row.Radio.IsChecked = true;
            SelectRow(row);
        }
        catch (Exception ex)
        {
            row.Subtitle.Text = Loc.Format(LocKeys.Skin_ImportFailed, ex.Message);
        }
    }

    public static async Task<string?> ShowAsync(
        XamlRoot xamlRoot,
        SkinLibraryStore store,
        Window window,
        SkinLibraryKind library,
        string? currentSkinId)
    {
        await Task.Yield();
        var content = new PickSkinDialog(store, window, library, currentSkinId);
        await content.LoadAsync().ConfigureAwait(true);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Loc.Get(LocKeys.Account_PickSkinTitle),
            Content = content,
            PrimaryButtonText = Loc.Get(LocKeys.Action_Save),
            CloseButtonText = Loc.Get(LocKeys.Action_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = content.IsValid
        };
        content.ValidityChanged += (_, _) => dialog.IsPrimaryButtonEnabled = content.IsValid;

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return currentSkinId;

        return content.SelectedSkinId ?? currentSkinId;
    }

    private sealed class SkinOptionRow(
        SkinRecord skin,
        Border root,
        RadioButton radio,
        Image preview,
        TextBlock subtitle,
        Button importButton,
        ComboBox? armModelBox)
    {
        public SkinRecord Skin { get; } = skin;
        public Border Root { get; } = root;
        public RadioButton Radio { get; } = radio;
        public Image Preview { get; } = preview;
        public TextBlock Subtitle { get; } = subtitle;
        public Button ImportButton { get; } = importButton;

        public SkinArmModel SelectedArmModel =>
            armModelBox?.SelectedIndex == 1 ? SkinArmModel.Slim : SkinArmModel.Classic;
    }
}
