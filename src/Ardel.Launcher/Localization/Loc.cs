using System.Collections.Concurrent;
using System.Globalization;
using Windows.ApplicationModel.Resources;

namespace Ardel.Launcher.Localization;

/// <summary>
/// App string lookup. Prefer <see cref="LocKeys"/> + <see cref="Get"/> / <see cref="Format"/>.
/// Backed by <c>Strings/en-US/Resources.resw</c> (and later sibling culture folders).
/// Thread-safe: resolved strings are cached so install progress can call Loc off the UI thread.
/// </summary>
public static partial class Loc
{
    /// <summary>Resolved UI tag: <c>en-US</c>, <c>zh-CN</c>, or <c>ja-JP</c>.</summary>
    public static string ActiveLanguageTag { get; private set; } = "en-US";

    private static ResourceLoader? _loader;
    private static bool _loaderFailed;
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Switch in-memory catalogs. Call before any UI strings resolve.</summary>
    public static void SetLanguage(string languageTag)
    {
        ActiveLanguageTag = string.IsNullOrWhiteSpace(languageTag) ? "en-US" : languageTag;
        ResetCache();
    }

    /// <summary>English fallbacks …keep in sync with Resources.resw until full i18n lands.</summary>
    private static readonly Dictionary<string, string> Fallback = new(StringComparer.Ordinal)
    {
        [LocKeys.Brand_Name] = "Ardel",

        [LocKeys.Nav_Play] = "Play",
        [LocKeys.Nav_Download] = "Download",
        [LocKeys.Nav_Instances] = "Profiles",
        [LocKeys.Nav_Settings] = "Settings",

        [LocKeys.Action_Launch] = "Launch",
        [LocKeys.Action_Cancel] = "Cancel",
        [LocKeys.Action_Download] = "Download",
        [LocKeys.Action_Refresh] = "Refresh",
        [LocKeys.Action_Search] = "Search",
        [LocKeys.Action_Reset] = "Reset",
        [LocKeys.Action_Save] = "Save",
        [LocKeys.Action_Rescan] = "Rescan",
        [LocKeys.Action_Browse] = "Browse…",
        [LocKeys.Action_OpenFolder] = "Open folder",
        [LocKeys.Action_OpenMinecraftFolder] = "Open .minecraft",
        [LocKeys.Action_Delete] = "Delete",

        [LocKeys.Home_Tagline] = "Launch Minecraft",
        [LocKeys.Home_Version] = "Version",
        [LocKeys.Home_PlayerOffline] = "Player (offline)",
        [LocKeys.Home_Ready] = "Ready",
        [LocKeys.Home_GoDownload] = "Go to Download to get a game",
        [LocKeys.Home_InitFailed] = "Init failed: {0}",
        [LocKeys.Home_Preparing] = "Preparing {0}…",
        [LocKeys.Home_Starting] = "Starting {0}…",
        [LocKeys.Home_ResolvingJava] = "Checking Java…",
        [LocKeys.Home_DownloadingJava] = "Downloading Java {0}…",
        [LocKeys.Home_LaunchingGame] = "Launching game…",
        [LocKeys.Home_WaitingForWindow] = "Waiting for the game window…",
        [LocKeys.Home_GameRunning] = "Game is running",
        [LocKeys.Home_GameExited] = "Game exited",
        [LocKeys.Home_LaunchFailed] = "Launch failed: {0}",
        [LocKeys.Home_Cancelled] = "Cancelled",
        [LocKeys.Home_Cancelling] = "Cancelling…",
        [LocKeys.Home_DownloadingBytes] = "Downloading {0} / {1}",

        [LocKeys.Instances_Title] = "Profiles",
        [LocKeys.Instances_Subtitle] = "Installed game versions on this PC",
        [LocKeys.Instances_Empty] = "No profiles yet — download one first",
        [LocKeys.Instances_Count] = "{0} profiles",
        [LocKeys.Instances_LoadFailed] = "Could not load profiles: {0}",
        [LocKeys.Instances_OpenSettings] = "Open profile folder",
        [LocKeys.Instances_OpenedFolder] = "Opened {0}",
        [LocKeys.Instances_DeleteTitle] = "Delete profile",
        [LocKeys.Instances_DeleteConfirm] = "Delete \"{0}\"? This removes the version folder and cannot be undone.",
        [LocKeys.Instances_Deleting] = "Deleting {0}…",
        [LocKeys.Instances_Deleted] = "Deleted {0}",
        [LocKeys.Instances_DeleteFailed] = "Could not delete {0}: {1}",
        [LocKeys.Instances_DeleteNoUi] = "Cannot show delete dialog — reopen the Profiles page and try again.",
        [LocKeys.Home_ProcessError] = "Process error: {0}",

        [LocKeys.Download_Type] = "Type",
        [LocKeys.Download_Search] = "Search",
        [LocKeys.Download_SearchPlaceholder] = "e.g. 1.21",
        [LocKeys.Download_Installed] = "Installed",
        [LocKeys.Download_JavaTag] = "Java {0}",
        [LocKeys.Download_JavaTagPending] = "Java …",
        [LocKeys.Download_SelectHint] = "Click a version to choose name and loader",
        [LocKeys.Download_SelectRelease] = "Click a release to install",
        [LocKeys.Download_SelectSnapshot] = "Snapshots may be unstable — click one to install",
        [LocKeys.Download_Fetching] = "Fetching version list…",
        [LocKeys.Download_Available] = "{0} versions available",
        [LocKeys.Download_AvailableBusy] = "{0} tasks · {1} versions available",
        [LocKeys.Download_LoadFailed] = "Load failed: {0}",
        [LocKeys.Download_Started] = "Started {0} · {1} active",
        [LocKeys.Download_Waiting] = "Waiting…",
        [LocKeys.Download_WaitingGate] = "Waiting for another task…",
        [LocKeys.Download_InitLauncher] = "Starting installer…",
        [LocKeys.Download_PreparingFiles] = "Preparing…",
        [LocKeys.Download_ResolvingFiles] = "Resolving version files…",
        [LocKeys.Download_ResolvingElapsed] = "Resolving version files… ({0}s)",
        [LocKeys.Download_CheckingFiles] = "Checking files {0}/{1}",
        [LocKeys.Download_DownloadingCount] = "Downloading {0}/{1}",
        [LocKeys.Download_Queued] = "Queued…",
        [LocKeys.Download_Cancelling] = "Cancelling…",
        [LocKeys.Download_Cancelled] = "Cancelled",
        [LocKeys.Download_CancelledNamed] = "Cancelled {0}",
        [LocKeys.Download_Downloaded] = "Downloaded",
        [LocKeys.Download_DownloadedNamed] = "Downloaded {0}",
        [LocKeys.Download_CompleteToast] = "Download complete · {0}",
        [LocKeys.Download_Failed] = "Failed {0}: {1}",
        [LocKeys.Download_CannotOpenFolder] = "Cannot open folder: {0}",
        [LocKeys.Download_FlyoutHeader] = "Tasks",
        [LocKeys.Download_FlyoutHeaderCount] = "Tasks ({0})",
        [LocKeys.Download_AlreadyRunning] = "{0} is already in the task list",
        [LocKeys.Category_Release] = "Release",
        [LocKeys.Category_Snapshot] = "Snapshot",
        [LocKeys.Mod_Keyword] = "Keyword",
        [LocKeys.Mod_KeywordPlaceholder] = "Name or slug",
        [LocKeys.Mod_Source] = "Source",
        [LocKeys.Mod_Version] = "Game version",
        [LocKeys.Mod_Category] = "Category",
        [LocKeys.Mod_Loader] = "Loader",
        [LocKeys.Mod_SourceAll] = "All sources",
        [LocKeys.Mod_SourceCurseForge] = "CurseForge",
        [LocKeys.Mod_SourceModrinth] = "Modrinth",
        [LocKeys.Mod_VersionAll] = "Any version",
        [LocKeys.Mod_LoaderAny] = "Any loader",
        [LocKeys.Mod_LoaderForge] = "Forge",
        [LocKeys.Mod_LoaderNeoForge] = "NeoForge",
        [LocKeys.Mod_LoaderFabric] = "Fabric",
        [LocKeys.Mod_LoaderQuilt] = "Quilt",
        [LocKeys.Mod_LoaderLiteLoader] = "LiteLoader",
        [LocKeys.Mod_CategoryAll] = "All categories",
        [LocKeys.Mod_CategoryWorldGen] = "World generation",
        [LocKeys.Mod_CategoryBiomes] = "Biomes",
        [LocKeys.Mod_CategoryDimensions] = "Dimensions",
        [LocKeys.Mod_CategoryOres] = "Ores and resources",
        [LocKeys.Mod_CategoryStructures] = "Structures",
        [LocKeys.Mod_CategoryTechnology] = "Technology",
        [LocKeys.Mod_CategoryLogistics] = "Logistics",
        [LocKeys.Mod_CategoryAutomation] = "Automation",
        [LocKeys.Mod_CategoryEnergy] = "Energy",
        [LocKeys.Mod_CategoryRedstone] = "Redstone",
        [LocKeys.Mod_CategoryFood] = "Food and cooking",
        [LocKeys.Mod_CategoryFarming] = "Farming",
        [LocKeys.Mod_CategoryGameMechanics] = "Game mechanics",
        [LocKeys.Mod_CategoryTransport] = "Transportation",
        [LocKeys.Mod_CategoryStorage] = "Storage",
        [LocKeys.Mod_CategoryMagic] = "Magic",
        [LocKeys.Mod_CategoryAdventure] = "Adventure",
        [LocKeys.Mod_CategoryDecoration] = "Decoration",
        [LocKeys.Mod_CategoryMobs] = "Mobs",
        [LocKeys.Mod_CategoryUtility] = "Utility",
        [LocKeys.Mod_CategoryEquipment] = "Equipment and tools",
        [LocKeys.Mod_CategoryCreative] = "Creative",
        [LocKeys.Mod_CategoryOptimization] = "Optimization",
        [LocKeys.Mod_CategoryInfo] = "Information",
        [LocKeys.Mod_CategorySocial] = "Multiplayer",
        [LocKeys.Mod_CategoryLibrary] = "Libraries",
        [LocKeys.Mod_SearchHint] = "Set filters, then search to list Mods.",
        [LocKeys.Mod_Searching] = "Searching…",
        [LocKeys.Mod_SearchEmpty] = "No Mods matched these filters.",
        [LocKeys.Mod_SearchCount] = "{0} Mods",
        [LocKeys.Mod_SearchCountWithWarning] = "{0} Mods — {1}",
        [LocKeys.Mod_SearchFailed] = "Search failed: {0}",
        [LocKeys.Mod_SearchBothFailed] = "Search failed. Modrinth: {0}; CurseForge: {1}",
        [LocKeys.Mod_SearchPartialModrinth] = "Modrinth unavailable ({0}); showing CurseForge only.",
        [LocKeys.Mod_SearchPartialCurseForge] = "CurseForge unavailable ({0}); showing Modrinth only.",
        [LocKeys.Mod_DownloadsExact] = "{0} downloads",
        [LocKeys.Mod_DownloadsThousands] = "{0}K downloads",
        [LocKeys.Mod_DownloadsMillions] = "{0}M downloads",

        [LocKeys.Settings_Title] = "Settings",
        [LocKeys.Settings_Java] = "Java",
        [LocKeys.Settings_JavaPlaceholder] = "Auto / not selected",
        [LocKeys.Settings_MaxMemory] = "Max memory",
        [LocKeys.Settings_MemoryUnitMb] = " MB",
        [LocKeys.Settings_BmclTitle] = "BMCLAPI mirror",
        [LocKeys.Settings_BmclHint] =
            "Optional mirror for faster assets and libraries in some regions",
        [LocKeys.Settings_Off] = "Off",
        [LocKeys.Settings_On] = "On",
        [LocKeys.Settings_GameDirectory] = "Game directory",
        [LocKeys.Settings_GameDirectoryHint] =
            "Fixed to .minecraft next to the exe (portable). Version isolation is forced: each version has its own mods / saves / config.",
        [LocKeys.Settings_SourceBmcl] = "Download source: BMCLAPI",
        [LocKeys.Settings_SourceOfficial] = "Download source: Official",
        [LocKeys.Settings_FoundJava] = "Found {0} Java install(s)",
        [LocKeys.Settings_ScanFailed] = "Scan failed: {0}",
        [LocKeys.Settings_SelectJavaExe] = "Please select java.exe",
        [LocKeys.Settings_JavaUpdated] = "Java path updated",
        [LocKeys.Settings_BrowseFailed] = "Browse failed: {0}",
        [LocKeys.Settings_CannotOpenFolder] = "Cannot open folder: {0}",
        [LocKeys.Settings_Saved] = "Saved",
        [LocKeys.Settings_SaveFailed] = "Save failed: {0}",
        [LocKeys.Settings_JavaAuto] = "Java will be chosen automatically",
        [LocKeys.Settings_JavaSelected] = "Using Java {0}",
        [LocKeys.Settings_Language] = "Language",
        [LocKeys.Settings_LanguageSystem] = "System default",
        [LocKeys.Settings_LanguageEnglish] = "English",
        [LocKeys.Settings_LanguageChinese] = "简体中文",
        [LocKeys.Settings_LanguageJapanese] = "日本語",
        [LocKeys.Settings_LanguageRestartHint] = "Pick a language, then click Apply.",
        [LocKeys.Settings_RestartNow] = "Apply",
        [LocKeys.Settings_LanguageApplied] = "Language: {0} · nav sample: {1}",
        [LocKeys.Settings_ScanningJava] = "Scanning Java…",

        [LocKeys.Version_Fabric] = "Fabric {0}",
        [LocKeys.Version_Forge] = "Forge {0}",
        [LocKeys.Version_NeoForge] = "NeoForge {0}",
        [LocKeys.Version_OptiFine] = "OptiFine {0}",
        [LocKeys.Version_Vanilla] = "Vanilla",
        [LocKeys.Version_Custom] = "Custom",
        [LocKeys.Java_NamedWithSource] = "Java {0} ({1})",
        [LocKeys.Java_NamedWithPath] = "Java {0} — {1}",
        [LocKeys.Java_SourceJavaHome] = "JAVA_HOME",
        [LocKeys.Java_SourcePath] = "PATH",
        [LocKeys.Java_SourceCommon] = "Common",
        [LocKeys.Java_SourceRegistry] = "Registry",
        [LocKeys.Java_SourceRegistryNamed] = "Registry ({0})",
        [LocKeys.Progress_FileFallback] = "file",
        [LocKeys.Unit_Byte] = "B",
        [LocKeys.Unit_Kilobyte] = "KB",
        [LocKeys.Unit_Megabyte] = "MB",
        [LocKeys.Unit_Gigabyte] = "GB",

        [LocKeys.Install_Title] = "Install {0}",
        [LocKeys.Install_VersionName] = "Version name",
        [LocKeys.Install_VersionNamePlaceholder] = "Folder name under versions/",
        [LocKeys.Install_Loader] = "Also install",
        [LocKeys.Install_LoaderNone] = "Vanilla only",
        [LocKeys.Install_LoaderFabric] = "Fabric",
        [LocKeys.Install_LoaderForge] = "Forge",
        [LocKeys.Install_LoaderNeoForge] = "NeoForge",
        [LocKeys.Install_LoaderOptiFine] = "OptiFine",
        [LocKeys.Install_LoaderExclusiveHint] = "Forge, Fabric, NeoForge, and OptiFine cannot be combined — pick at most one.",
        [LocKeys.Install_LoaderVersion] = "Loader version",
        [LocKeys.Install_OptiFineVersion] = "OptiFine version",
        [LocKeys.Install_LoadingLoaders] = "Loading loader versions…",
        [LocKeys.Install_LoadingOptiFine] = "Loading OptiFine versions…",
        [LocKeys.Install_NoLoaders] = "No loader builds found for this Minecraft version.",
        [LocKeys.Install_NoOptiFine] = "No OptiFine builds found for this Minecraft version.",
        [LocKeys.Install_LoaderCount] = "{0} builds available",
        [LocKeys.Install_OptiFineCount] = "{0} OptiFine builds available",
        [LocKeys.Install_LoaderLoadFailed] = "Could not load loader versions: {0}",
        [LocKeys.Install_OptiFineLoadFailed] = "Could not load OptiFine versions: {0}",
        [LocKeys.Install_SelectLoaderVersion] = "Select a loader version.",
        [LocKeys.Install_SelectOptiFineVersion] = "Select an OptiFine version.",
        [LocKeys.Install_FabricApi] = "Also install Fabric API",
        [LocKeys.Install_FabricApiHint] = "Downloads Fabric API into this version's mods folder (Modrinth, then CurseForge).",

        [LocKeys.Conflict_WithAddon] = "{0} is incompatible with {1}.",
        [LocKeys.Conflict_Incompatible] = "These addons cannot be combined.",

        [LocKeys.LoaderTag_Stable] = "stable",
        [LocKeys.LoaderTag_Unstable] = "unstable",
        [LocKeys.LoaderTag_Recommended] = "recommended",
        [LocKeys.LoaderTag_Latest] = "latest",
        [LocKeys.LoaderTag_Named] = "{0} ({1})",

        [LocKeys.Progress_FileCount] = "{0}  {1}/{2}",

        [LocKeys.FabricApi_Resolving] = "Finding Fabric API…",
        [LocKeys.FabricApi_Downloading] = "Downloading Fabric API ({1}): {0}",
        [LocKeys.FabricApi_Installed] = "Fabric API ready ({1}): {0}",
        [LocKeys.FabricApi_NotFound] = "No Fabric API build found for Minecraft {0}.",
        [LocKeys.FabricApi_ResolveFailed] = "Could not resolve Fabric API for {0}: {1}",
        [LocKeys.FabricApi_BothFailed] = "Modrinth: {0}; CurseForge: {1}",
        [LocKeys.FabricApi_SourceModrinth] = "Modrinth",
        [LocKeys.FabricApi_SourceCurseForge] = "CurseForge",

        [LocKeys.Error_BmclSetupFailed] = "Failed to configure BMCLAPI mirrors.",
        [LocKeys.Error_LoaderEmptyId] = "Mod loader install returned an empty version id.",
        [LocKeys.Error_FabricProfileInvalid] =
            "Fabric profile was not created correctly. Pick a version name different from the Minecraft id and try again.",
        [LocKeys.Error_JavaNotFound] = "Configured Java path was not found.",
        [LocKeys.Error_JavaTooOld] = "Minecraft {0} requires Java {1}+, but selected Java is {2}.",
        [LocKeys.Error_JavaExeNotFound] = "java.exe not found.",
        [LocKeys.Error_JavaProcessStart] = "Failed to start process: {0}",
        [LocKeys.Error_JavaVersionParse] = "Unable to parse Java version from output:{0}{1}",
        [LocKeys.Error_ProcessStartFailed] = "Failed to start the Minecraft process.",
        [LocKeys.Error_VersionFolderNotFound] = "Version folder not found: {0}",
        [LocKeys.Error_VersionAlreadyExists] = "Version already exists: {0}",
        [LocKeys.Error_VersionDeleteLocked] = "Could not delete \"{0}\" — close the game and any open folders, then try again.",
        [LocKeys.Error_JavaDownloadFailed] = "Failed to download Java {0}: {1}",
        [LocKeys.Error_AlreadyInstalling] = "Install already in progress.",
        [LocKeys.Error_FileDownloadFailed] = "Failed to download {0}",
        [LocKeys.Error_HttpStatus] = "HTTP {0}",
        [LocKeys.Error_TimedOut] = "Timed out",
        [LocKeys.Error_Unknown] = "unknown",

        [LocKeys.Validate_VersionEmpty] = "Version name cannot be empty.",
        [LocKeys.Validate_NameLeadingSpace] = "Name cannot start with a space.",
        [LocKeys.Validate_NameTrailingSpace] = "Name cannot end with a space.",
        [LocKeys.Validate_NameTrailingDot] = "Name cannot end with a period.",
        [LocKeys.Validate_NameTooLong] = "Name can be at most {0} characters.",
        [LocKeys.Validate_NameInvalidChar] = "Name cannot contain: {0}",
        [LocKeys.Validate_NameReserved] = "Name cannot be {0}.",
        [LocKeys.Validate_NameNtfs83] = "Name cannot use this special format.",
        [LocKeys.Validate_VersionExists] = "A version folder with this name already exists.",
        [LocKeys.Validate_LoaderNameEqualsMc] =
            "Loader profiles cannot use the same name as the Minecraft version (that overwrites vanilla). Try a name like 1.21.1-fabric.",
        [LocKeys.Validate_PlayerEmpty] = "Player name cannot be empty.",
        [LocKeys.Validate_PlayerQuote] = "Player name cannot contain quotes (\").",
        [LocKeys.Validate_PlayerTooLong] = "Player name must be 16 characters or fewer.",

        [LocKeys.Default_PlayerName] = "Player",
    };

    /// <summary>Drop cached strings after a language change (before restart).</summary>
    public static void ResetCache()
    {
        Cache.Clear();
        _loader = null;
        _loaderFailed = false;
    }

    /// <summary>Warm ResourceLoader + cache on the UI thread once at startup.</summary>
    public static void Warmup()
    {
        foreach (var key in Fallback.Keys)
            _ = Get(key);
    }

    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        // Cache per language so a previous zh/en resolve cannot leak after SetLanguage.
        var cacheKey = ActiveLanguageTag + "\u001f" + key;
        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resolved = Resolve(key);
        Cache[cacheKey] = resolved;
        return resolved;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static string Resolve(string key)
    {
        // Unpackaged WinUI often ignores PrimaryLanguageOverride for ResourceLoader.
        // Prefer explicit in-memory catalogs so Settings → Language actually works.
        var tag = ActiveLanguageTag;
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
            Chinese.TryGetValue(key, out var zh) && !string.IsNullOrEmpty(zh))
            return zh.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase) &&
            Japanese.TryGetValue(key, out var ja) && !string.IsNullOrEmpty(ja))
            return ja.Replace("\\n", "\n", StringComparison.Ordinal);

        if (Fallback.TryGetValue(key, out var en) && !string.IsNullOrEmpty(en))
            return en.Replace("\\n", "\n", StringComparison.Ordinal);

        if (!_loaderFailed)
        {
            try
            {
                _loader ??= ResourceLoader.GetForViewIndependentUse();
                var value = _loader.GetString(key);
                if (!string.IsNullOrEmpty(value))
                    return value.Replace("\\n", "\n", StringComparison.Ordinal);
            }
            catch
            {
                _loaderFailed = true;
            }
        }

        return key;
    }
}

