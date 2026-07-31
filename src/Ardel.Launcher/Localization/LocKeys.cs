namespace Ardel.Launcher.Localization;

/// <summary>
/// Stable resource keys. Never put user-facing English in call sites — use <see cref="Loc"/>.
/// </summary>
public static class LocKeys
{
    // Brand (usually not translated)
    public const string Brand_Name = "Brand_Name";

    // Shell / nav
    public const string Nav_Play = "Nav_Play";
    public const string Nav_Download = "Nav_Download";
    public const string Nav_Instances = "Nav_Instances";
    public const string Nav_Settings = "Nav_Settings";

    // Common actions
    public const string Action_Launch = "Action_Launch";
    public const string Action_Cancel = "Action_Cancel";
    public const string Action_Download = "Action_Download";
    public const string Action_Refresh = "Action_Refresh";
    public const string Action_Search = "Action_Search";
    public const string Action_Reset = "Action_Reset";
    public const string Action_Save = "Action_Save";
    public const string Action_Rescan = "Action_Rescan";
    public const string Action_Browse = "Action_Browse";
    public const string Action_OpenFolder = "Action_OpenFolder";
    public const string Action_OpenMinecraftFolder = "Action_OpenMinecraftFolder";
    public const string Action_Delete = "Action_Delete";

    // Home
    public const string Home_Tagline = "Home_Tagline";
    public const string Home_Version = "Home_Version";
    public const string Home_PlayerOffline = "Home_PlayerOffline";
    public const string Home_Ready = "Home_Ready";
    public const string Home_GoDownload = "Home_GoDownload";
    public const string Home_InitFailed = "Home_InitFailed";
    public const string Home_Preparing = "Home_Preparing";
    public const string Home_Starting = "Home_Starting";
    public const string Home_ResolvingJava = "Home_ResolvingJava";
    public const string Home_DownloadingJava = "Home_DownloadingJava";
    public const string Home_LaunchingGame = "Home_LaunchingGame";
    public const string Home_WaitingForWindow = "Home_WaitingForWindow";
    public const string Home_GameRunning = "Home_GameRunning";
    public const string Home_GameExited = "Home_GameExited";
    public const string Home_LaunchFailed = "Home_LaunchFailed";
    public const string Home_Cancelled = "Home_Cancelled";
    public const string Home_Cancelling = "Home_Cancelling";
    public const string Home_DownloadingBytes = "Home_DownloadingBytes";

    // Instances
    public const string Instances_Title = "Instances_Title";
    public const string Instances_Subtitle = "Instances_Subtitle";
    public const string Instances_Empty = "Instances_Empty";
    public const string Instances_Count = "Instances_Count";
    public const string Instances_LoadFailed = "Instances_LoadFailed";
    public const string Instances_OpenSettings = "Instances_OpenSettings";
    public const string Instances_OpenedFolder = "Instances_OpenedFolder";
    public const string Instances_DeleteTitle = "Instances_DeleteTitle";
    public const string Instances_DeleteConfirm = "Instances_DeleteConfirm";
    public const string Instances_Deleting = "Instances_Deleting";
    public const string Instances_Deleted = "Instances_Deleted";
    public const string Instances_DeleteFailed = "Instances_DeleteFailed";
    public const string Instances_DeleteNoUi = "Instances_DeleteNoUi";
    public const string Home_ProcessError = "Home_ProcessError";

    // Download page
    public const string Download_Type = "Download_Type";
    public const string Download_Search = "Download_Search";
    public const string Download_SearchPlaceholder = "Download_SearchPlaceholder";
    public const string Download_Installed = "Download_Installed";
    public const string Download_JavaTag = "Download_JavaTag";
    public const string Download_JavaTagPending = "Download_JavaTagPending";
    public const string Download_SelectHint = "Download_SelectHint";
    public const string Download_SelectRelease = "Download_SelectRelease";
    public const string Download_SelectSnapshot = "Download_SelectSnapshot";
    public const string Download_Fetching = "Download_Fetching";
    public const string Download_Available = "Download_Available";
    public const string Download_AvailableBusy = "Download_AvailableBusy";
    public const string Download_LoadFailed = "Download_LoadFailed";
    public const string Download_Started = "Download_Started";
    public const string Download_Waiting = "Download_Waiting";
    public const string Download_WaitingGate = "Download_WaitingGate";
    public const string Download_InitLauncher = "Download_InitLauncher";
    public const string Download_PreparingFiles = "Download_PreparingFiles";
    public const string Download_ResolvingFiles = "Download_ResolvingFiles";
    public const string Download_ResolvingElapsed = "Download_ResolvingElapsed";
    public const string Download_CheckingFiles = "Download_CheckingFiles";
    public const string Download_DownloadingCount = "Download_DownloadingCount";
    public const string Download_Queued = "Download_Queued";
    public const string Download_Cancelling = "Download_Cancelling";
    public const string Download_Cancelled = "Download_Cancelled";
    public const string Download_CancelledNamed = "Download_CancelledNamed";
    public const string Download_Downloaded = "Download_Downloaded";
    public const string Download_DownloadedNamed = "Download_DownloadedNamed";
    public const string Download_CompleteToast = "Download_CompleteToast";
    public const string Download_Failed = "Download_Failed";
    public const string Download_CannotOpenFolder = "Download_CannotOpenFolder";
    public const string Download_FlyoutHeader = "Download_FlyoutHeader";
    public const string Download_FlyoutHeaderCount = "Download_FlyoutHeaderCount";
    public const string Category_Release = "Category_Release";
    public const string Category_Snapshot = "Category_Snapshot";

    // Mod download filters
    public const string Mod_Keyword = "Mod_Keyword";
    public const string Mod_KeywordPlaceholder = "Mod_KeywordPlaceholder";
    public const string Mod_Source = "Mod_Source";
    public const string Mod_Version = "Mod_Version";
    public const string Mod_Category = "Mod_Category";
    public const string Mod_Loader = "Mod_Loader";
    public const string Mod_SourceAll = "Mod_SourceAll";
    public const string Mod_SourceCurseForge = "Mod_SourceCurseForge";
    public const string Mod_SourceModrinth = "Mod_SourceModrinth";
    public const string Mod_VersionAll = "Mod_VersionAll";
    public const string Mod_LoaderAny = "Mod_LoaderAny";
    public const string Mod_LoaderForge = "Mod_LoaderForge";
    public const string Mod_LoaderNeoForge = "Mod_LoaderNeoForge";
    public const string Mod_LoaderFabric = "Mod_LoaderFabric";
    public const string Mod_LoaderQuilt = "Mod_LoaderQuilt";
    public const string Mod_LoaderLiteLoader = "Mod_LoaderLiteLoader";
    public const string Mod_CategoryAll = "Mod_CategoryAll";
    public const string Mod_CategoryWorldGen = "Mod_CategoryWorldGen";
    public const string Mod_CategoryBiomes = "Mod_CategoryBiomes";
    public const string Mod_CategoryDimensions = "Mod_CategoryDimensions";
    public const string Mod_CategoryOres = "Mod_CategoryOres";
    public const string Mod_CategoryStructures = "Mod_CategoryStructures";
    public const string Mod_CategoryTechnology = "Mod_CategoryTechnology";
    public const string Mod_CategoryLogistics = "Mod_CategoryLogistics";
    public const string Mod_CategoryAutomation = "Mod_CategoryAutomation";
    public const string Mod_CategoryEnergy = "Mod_CategoryEnergy";
    public const string Mod_CategoryRedstone = "Mod_CategoryRedstone";
    public const string Mod_CategoryFood = "Mod_CategoryFood";
    public const string Mod_CategoryFarming = "Mod_CategoryFarming";
    public const string Mod_CategoryGameMechanics = "Mod_CategoryGameMechanics";
    public const string Mod_CategoryTransport = "Mod_CategoryTransport";
    public const string Mod_CategoryStorage = "Mod_CategoryStorage";
    public const string Mod_CategoryMagic = "Mod_CategoryMagic";
    public const string Mod_CategoryAdventure = "Mod_CategoryAdventure";
    public const string Mod_CategoryDecoration = "Mod_CategoryDecoration";
    public const string Mod_CategoryMobs = "Mod_CategoryMobs";
    public const string Mod_CategoryUtility = "Mod_CategoryUtility";
    public const string Mod_CategoryEquipment = "Mod_CategoryEquipment";
    public const string Mod_CategoryCreative = "Mod_CategoryCreative";
    public const string Mod_CategoryOptimization = "Mod_CategoryOptimization";
    public const string Mod_CategoryInfo = "Mod_CategoryInfo";
    public const string Mod_CategorySocial = "Mod_CategorySocial";
    public const string Mod_CategoryLibrary = "Mod_CategoryLibrary";
    public const string Mod_SearchHint = "Mod_SearchHint";
    public const string Mod_Searching = "Mod_Searching";
    public const string Mod_SearchEmpty = "Mod_SearchEmpty";
    public const string Mod_SearchCount = "Mod_SearchCount";
    public const string Mod_SearchCountWithWarning = "Mod_SearchCountWithWarning";
    public const string Mod_SearchFailed = "Mod_SearchFailed";
    public const string Mod_SearchBothFailed = "Mod_SearchBothFailed";
    public const string Mod_SearchPartialModrinth = "Mod_SearchPartialModrinth";
    public const string Mod_SearchPartialCurseForge = "Mod_SearchPartialCurseForge";
    public const string Mod_DownloadsExact = "Mod_DownloadsExact";
    public const string Mod_DownloadsThousands = "Mod_DownloadsThousands";
    public const string Mod_DownloadsMillions = "Mod_DownloadsMillions";
    public const string Mod_PagePrevious = "Mod_PagePrevious";
    public const string Mod_PageNext = "Mod_PageNext";
    public const string Mod_PageLabel = "Mod_PageLabel";
    public const string Mod_VersionsEllipsis = "Mod_VersionsEllipsis";

    // Settings
    public const string Settings_Title = "Settings_Title";
    public const string Settings_Java = "Settings_Java";
    public const string Settings_JavaPlaceholder = "Settings_JavaPlaceholder";
    public const string Settings_MaxMemory = "Settings_MaxMemory";
    public const string Settings_MemoryUnitMb = "Settings_MemoryUnitMb";
    public const string Settings_BmclTitle = "Settings_BmclTitle";
    public const string Settings_BmclHint = "Settings_BmclHint";
    public const string Settings_Off = "Settings_Off";
    public const string Settings_On = "Settings_On";
    public const string Settings_GameDirectory = "Settings_GameDirectory";
    public const string Settings_GameDirectoryHint = "Settings_GameDirectoryHint";
    public const string Settings_SourceBmcl = "Settings_SourceBmcl";
    public const string Settings_SourceOfficial = "Settings_SourceOfficial";
    public const string Settings_FoundJava = "Settings_FoundJava";
    public const string Settings_ScanFailed = "Settings_ScanFailed";
    public const string Settings_SelectJavaExe = "Settings_SelectJavaExe";
    public const string Settings_JavaUpdated = "Settings_JavaUpdated";
    public const string Settings_BrowseFailed = "Settings_BrowseFailed";
    public const string Settings_CannotOpenFolder = "Settings_CannotOpenFolder";
    public const string Settings_Saved = "Settings_Saved";
    public const string Settings_SaveFailed = "Settings_SaveFailed";
    public const string Settings_JavaAuto = "Settings_JavaAuto";
    public const string Settings_JavaSelected = "Settings_JavaSelected";
    public const string Settings_Language = "Settings_Language";
    public const string Settings_LanguageSystem = "Settings_LanguageSystem";
    public const string Settings_LanguageEnglish = "Settings_LanguageEnglish";
    public const string Settings_LanguageChinese = "Settings_LanguageChinese";
    public const string Settings_LanguageJapanese = "Settings_LanguageJapanese";
    public const string Settings_LanguageRestartHint = "Settings_LanguageRestartHint";
    public const string Settings_RestartNow = "Settings_RestartNow";
    public const string Settings_LanguageApplied = "Settings_LanguageApplied";
    public const string Settings_ScanningJava = "Settings_ScanningJava";

    // Version / Java display
    public const string Version_Fabric = "Version_Fabric";
    public const string Version_Forge = "Version_Forge";
    public const string Version_NeoForge = "Version_NeoForge";
    public const string Version_OptiFine = "Version_OptiFine";
    public const string Version_Vanilla = "Version_Vanilla";
    public const string Version_Custom = "Version_Custom";
    public const string Java_NamedWithSource = "Java_NamedWithSource";
    public const string Java_NamedWithPath = "Java_NamedWithPath";
    public const string Java_SourceJavaHome = "Java_SourceJavaHome";
    public const string Java_SourcePath = "Java_SourcePath";
    public const string Java_SourceCommon = "Java_SourceCommon";
    public const string Java_SourceRegistry = "Java_SourceRegistry";
    public const string Java_SourceRegistryNamed = "Java_SourceRegistryNamed";
    public const string Progress_FileFallback = "Progress_FileFallback";
    public const string Unit_Byte = "Unit_Byte";
    public const string Unit_Kilobyte = "Unit_Kilobyte";
    public const string Unit_Megabyte = "Unit_Megabyte";
    public const string Unit_Gigabyte = "Unit_Gigabyte";

    // Install dialog
    public const string Install_Title = "Install_Title";
    public const string Install_VersionName = "Install_VersionName";
    public const string Install_VersionNamePlaceholder = "Install_VersionNamePlaceholder";
    public const string Install_Loader = "Install_Loader";
    public const string Install_LoaderNone = "Install_LoaderNone";
    public const string Install_LoaderFabric = "Install_LoaderFabric";
    public const string Install_LoaderForge = "Install_LoaderForge";
    public const string Install_LoaderNeoForge = "Install_LoaderNeoForge";
    public const string Install_LoaderOptiFine = "Install_LoaderOptiFine";
    public const string Install_LoaderExclusiveHint = "Install_LoaderExclusiveHint";
    public const string Install_LoaderVersion = "Install_LoaderVersion";
    public const string Install_OptiFineVersion = "Install_OptiFineVersion";
    public const string Install_LoadingLoaders = "Install_LoadingLoaders";
    public const string Install_LoadingOptiFine = "Install_LoadingOptiFine";
    public const string Install_NoLoaders = "Install_NoLoaders";
    public const string Install_NoOptiFine = "Install_NoOptiFine";
    public const string Install_LoaderCount = "Install_LoaderCount";
    public const string Install_OptiFineCount = "Install_OptiFineCount";
    public const string Install_LoaderLoadFailed = "Install_LoaderLoadFailed";
    public const string Install_OptiFineLoadFailed = "Install_OptiFineLoadFailed";
    public const string Install_SelectLoaderVersion = "Install_SelectLoaderVersion";
    public const string Install_SelectOptiFineVersion = "Install_SelectOptiFineVersion";
    public const string Install_FabricApi = "Install_FabricApi";
    public const string Install_FabricApiHint = "Install_FabricApiHint";
    public const string Download_AlreadyRunning = "Download_AlreadyRunning";

    public const string Conflict_WithAddon = "Conflict_WithAddon";
    public const string Conflict_Incompatible = "Conflict_Incompatible";

    public const string LoaderTag_Stable = "LoaderTag_Stable";
    public const string LoaderTag_Unstable = "LoaderTag_Unstable";
    public const string LoaderTag_Recommended = "LoaderTag_Recommended";
    public const string LoaderTag_Latest = "LoaderTag_Latest";
    public const string LoaderTag_Named = "LoaderTag_Named";

    public const string Progress_FileCount = "Progress_FileCount";

    public const string FabricApi_Resolving = "FabricApi_Resolving";
    public const string FabricApi_Downloading = "FabricApi_Downloading";
    public const string FabricApi_Installed = "FabricApi_Installed";
    public const string FabricApi_NotFound = "FabricApi_NotFound";
    public const string FabricApi_ResolveFailed = "FabricApi_ResolveFailed";
    public const string FabricApi_BothFailed = "FabricApi_BothFailed";
    public const string FabricApi_SourceModrinth = "FabricApi_SourceModrinth";
    public const string FabricApi_SourceCurseForge = "FabricApi_SourceCurseForge";

    public const string Error_BmclSetupFailed = "Error_BmclSetupFailed";
    public const string Error_LoaderEmptyId = "Error_LoaderEmptyId";
    public const string Error_FabricProfileInvalid = "Error_FabricProfileInvalid";
    public const string Error_JavaNotFound = "Error_JavaNotFound";
    public const string Error_JavaTooOld = "Error_JavaTooOld";
    public const string Error_JavaExeNotFound = "Error_JavaExeNotFound";
    public const string Error_JavaProcessStart = "Error_JavaProcessStart";
    public const string Error_JavaVersionParse = "Error_JavaVersionParse";
    public const string Error_ProcessStartFailed = "Error_ProcessStartFailed";
    public const string Error_VersionFolderNotFound = "Error_VersionFolderNotFound";
    public const string Error_VersionAlreadyExists = "Error_VersionAlreadyExists";
    public const string Error_VersionDeleteLocked = "Error_VersionDeleteLocked";
    public const string Error_JavaDownloadFailed = "Error_JavaDownloadFailed";
    public const string Error_AlreadyInstalling = "Error_AlreadyInstalling";
    public const string Error_FileDownloadFailed = "Error_FileDownloadFailed";
    public const string Error_HttpStatus = "Error_HttpStatus";
    public const string Error_TimedOut = "Error_TimedOut";
    public const string Error_Unknown = "Error_Unknown";

    // Validation
    public const string Validate_VersionEmpty = "Validate_VersionEmpty";
    public const string Validate_NameLeadingSpace = "Validate_NameLeadingSpace";
    public const string Validate_NameTrailingSpace = "Validate_NameTrailingSpace";
    public const string Validate_NameTrailingDot = "Validate_NameTrailingDot";
    public const string Validate_NameTooLong = "Validate_NameTooLong";
    public const string Validate_NameInvalidChar = "Validate_NameInvalidChar";
    public const string Validate_NameReserved = "Validate_NameReserved";
    public const string Validate_NameNtfs83 = "Validate_NameNtfs83";
    public const string Validate_VersionExists = "Validate_VersionExists";
    public const string Validate_LoaderNameEqualsMc = "Validate_LoaderNameEqualsMc";
    public const string Validate_PlayerEmpty = "Validate_PlayerEmpty";
    public const string Validate_PlayerQuote = "Validate_PlayerQuote";
    public const string Validate_PlayerTooLong = "Validate_PlayerTooLong";

    // Defaults
    public const string Default_PlayerName = "Default_PlayerName";
}

