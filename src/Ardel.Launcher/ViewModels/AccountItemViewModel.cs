using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using Ardel.Launcher.Helpers;
using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services;

namespace Ardel.Launcher.ViewModels;

public partial class AccountItemViewModel : ObservableObject
{
    private readonly SkinLibraryStore _skins;

    public AccountItemViewModel(AccountRecord record, SkinLibraryStore skins, bool isActive)
    {
        _skins = skins;
        Id = record.Id;
        Kind = record.Kind;
        DisplayName = record.DisplayName;
        Uuid = record.Uuid;
        SkinId = record.SkinId;
        IsActive = isActive;
        KindLabel = record.Kind == AccountKind.Microsoft
            ? Loc.Get(LocKeys.Account_KindMicrosoft)
            : Loc.Get(LocKeys.Account_KindOffline);
        var skin = skins.Find(record.SkinId);
        SkinLabel = skin is null
            ? Loc.Get(LocKeys.Account_SkinNone)
            : $"{Loc.Get(LocKeys.Account_Skin)}: {skin.Name}";
        AvatarInitial = string.IsNullOrWhiteSpace(record.DisplayName)
            ? "?"
            : record.DisplayName.Trim()[..1].ToUpperInvariant();
    }

    public string Id { get; }
    public AccountKind Kind { get; }
    public string DisplayName { get; }
    public string Uuid { get; }
    public string? SkinId { get; }
    public string KindLabel { get; }
    public string SkinLabel { get; }
    public string AvatarInitial { get; }

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private BitmapImage? _avatarImage;
    [ObservableProperty] private bool _hasAvatarImage;

    public async Task LoadAvatarAsync()
    {
        var skin = _skins.Find(SkinId);
        if (skin is null)
        {
            AvatarImage = null;
            HasAvatarImage = false;
            return;
        }

        var path = _skins.GetAbsolutePath(skin);
        var image = await SkinPreviewHelper.TryCreateHeadPreviewAsync(path, displaySize: 112)
            .ConfigureAwait(true);
        AvatarImage = image;
        HasAvatarImage = image is not null;
    }
}
