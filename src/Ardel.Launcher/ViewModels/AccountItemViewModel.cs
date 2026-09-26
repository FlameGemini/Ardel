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

    public AccountItemViewModel(AccountRecord record, SkinLibraryStore skins, bool isActive, bool isLocked = false)
    {
        _skins = skins;
        Id = record.Id;
        Kind = record.Kind;
        _displayName = record.DisplayName;
        Uuid = record.Uuid;
        _skinId = record.SkinId;
        MicrosoftAccountId = record.MicrosoftAccountId;
        IsActive = isActive;
        _isLocked = isLocked;
    }

    public string Id { get; }
    public AccountKind Kind { get; }
    public string Uuid { get; }
    public string? MicrosoftAccountId { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanActivate))]
    private bool _isLocked;

    public bool CanActivate => !IsLocked;

    public string LockedBadgeText => Loc.Get(LocKeys.Account_OfflineLockedBadge);
    public string LockedToolTip => Loc.Get(LocKeys.Account_OfflineLockedHint);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarInitial))]
    private string _displayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkinLabel))]
    private string? _skinId;

    public string AvatarInitial => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : DisplayName.Trim()[..1].ToUpperInvariant();

    public string KindLabel => Kind == AccountKind.Microsoft
        ? Loc.Get(LocKeys.Account_KindMicrosoft)
        : Loc.Get(LocKeys.Account_KindOffline);

    public string SkinLabel
    {
        get
        {
            var skin = _skins.Find(SkinId);
            if (Kind == AccountKind.Microsoft)
            {
                var arm = skin?.ArmModel == SkinArmModel.Slim
                    ? Loc.Get(LocKeys.Skin_ModelAlex)
                    : Loc.Get(LocKeys.Skin_ModelSteve);
                return $"{Loc.Get(LocKeys.Account_Skin)} ({arm})";
            }

            return skin is null
                ? Loc.Get(LocKeys.Account_SkinNone)
                : $"{Loc.Get(LocKeys.Account_Skin)}: {skin.GetDisplayName()}";
        }
    }

    public string EditButtonGlyph => Kind == AccountKind.Microsoft ? "\uE72C" : "\uE70F";
    public string EditButtonToolTip => Kind == AccountKind.Microsoft
        ? Loc.Get(LocKeys.Account_MicrosoftRefresh)
        : Loc.Get(LocKeys.Account_Edit);
    public string AppearanceButtonToolTip => Kind == AccountKind.Microsoft
        ? Loc.Get(LocKeys.Account_ManageAppearance)
        : Loc.Get(LocKeys.Account_Skin);

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private BitmapImage? _avatarImage;
    [ObservableProperty] private bool _hasAvatarImage;

    public void NotifyLocalization()
    {
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(SkinLabel));
        OnPropertyChanged(nameof(EditButtonToolTip));
        OnPropertyChanged(nameof(AppearanceButtonToolTip));
        OnPropertyChanged(nameof(LockedBadgeText));
        OnPropertyChanged(nameof(LockedToolTip));
    }

    public async Task LoadAvatarAsync()
    {
        var skin = _skins.Find(SkinId);
        if (skin is null && Kind == AccountKind.Microsoft && !string.IsNullOrWhiteSpace(Uuid))
        {
            var clean = Uuid.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
            skin = _skins.Find($"official-{clean}");
        }

        if (skin is null || (!skin.IsBuiltIn && !skin.IsConfigured))
        {
            AvatarImage = null;
            HasAvatarImage = false;
            return;
        }

        var path = _skins.GetAbsolutePath(skin);
        var image = await Skin3DHeadHelper.TryCreateAsync(path).ConfigureAwait(true)
                    ?? await SkinPreviewHelper.TryCreateHeadPreviewAsync(path, displaySize: 112)
                        .ConfigureAwait(true);
        AvatarImage = image;
        HasAvatarImage = image is not null;
    }
}
