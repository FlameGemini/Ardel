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
    /// <summary>Resolved UI tag: <c>en-US</c>, <c>en-UK</c>, <c>zh-CN</c>, <c>zh-Hant</c>, <c>ja-JP</c>, or <c>fr</c>.</summary>
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

        [LocKeys.Nav_Play] = "Home",
        [LocKeys.Nav_Download] = "Download",
        [LocKeys.Nav_Instances] = "Profiles",
        [LocKeys.Nav_Settings] = "Settings",
        [LocKeys.Nav_Account] = "Account",
        [LocKeys.Nav_Skins] = "Skins",
        [LocKeys.Nav_About] = "About",

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
        [LocKeys.Action_Back] = "Back",
        [LocKeys.Action_Apply] = "Apply",
        [LocKeys.Action_Close] = "Close",
        [LocKeys.Action_ClearSelection] = "Clear selection",

        [LocKeys.Home_GreetingEarlyMorning] = "Good morning",
        [LocKeys.Home_GreetingMorning] = "Good morning",
        [LocKeys.Home_GreetingNoon] = "Good noon",
        [LocKeys.Home_GreetingAfternoon] = "Good afternoon",
        [LocKeys.Home_GreetingEvening] = "Good evening",
        [LocKeys.Home_LaunchGame] = "Launch game",
        [LocKeys.Home_QuickLaunchNone] = "Pin an instance in its settings to use quick launch.",
        [LocKeys.Home_QuickLaunchMissing] = "Pinned instance “{0}” was not found.",
        [LocKeys.Home_Tagline] = "Launch Minecraft",
        [LocKeys.Home_Version] = "Version",
        [LocKeys.Home_PlayerOffline] = "Player (offline)",
        [LocKeys.Home_Ready] = "Ready",
        [LocKeys.Home_ManageProfiles] = "Manage profiles",
        [LocKeys.Home_NoAccount] = "Select an account",
        [LocKeys.Startup_Loading] = "Starting…",
        [LocKeys.Home_GoDownload] = "Go to Download to get a game",
        [LocKeys.Home_InitFailed] = "Init failed: {0}",
        [LocKeys.Home_Preparing] = "Preparing {0}…",
        [LocKeys.Home_PreparingOfflineSkin] = "Starting Ardel skin relay…",
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
        [LocKeys.Instances_Empty] = "No instances yet — download one first",
        [LocKeys.Instances_GoDownload] = "Go to Download",
        [LocKeys.Instances_Loading] = "Looking for installed profiles…",
        [LocKeys.Instances_Count] = "{0} profiles",
        [LocKeys.Instances_LoadFailed] = "Could not load profiles: {0}",
        [LocKeys.Instances_OpenSettings] = "Instance settings",
        [LocKeys.Instances_OpenedFolder] = "Opened {0}",
        [LocKeys.Instances_DeleteTitle] = "Delete profile",
        [LocKeys.Instances_DeleteConfirm] = "Delete \"{0}\"? This removes the version folder and cannot be undone.",
        [LocKeys.Instances_Deleting] = "Deleting {0}…",
        [LocKeys.Instances_Deleted] = "Deleted {0}",
        [LocKeys.Instances_DeleteFailed] = "Could not delete {0}: {1}",
        [LocKeys.Instances_DeleteNoUi] = "Cannot show delete dialog — reopen the Profiles page and try again.",
        [LocKeys.Instances_StopAll] = "Stop all games",
        [LocKeys.Instances_GamesStopped] = "All game processes stopped",
        [LocKeys.Instances_GamesRunning] = "{0} games running",

        [LocKeys.InstanceSettings_Title] = "Settings — {0}",
        [LocKeys.InstanceSettings_Isolation] = "Isolated folder",
        [LocKeys.InstanceSettings_IsolationHint] =
            "Version isolation is always on for this profile.",
        [LocKeys.InstanceSettings_OpenFolder] = "Open instance folder",
        [LocKeys.InstanceSettings_OpenSubfolder] = "Open…",
        [LocKeys.InstanceSettings_DeleteHint] =
            "Permanently removes this profile folder and unused dependency versions.",
        [LocKeys.InstanceSettings_FollowGlobal] = "Use launcher",
        [LocKeys.InstanceSettings_Custom] = "Custom",
        [LocKeys.InstanceSettings_GlobalMemoryHint] = "Using launcher default:",
        [LocKeys.InstanceSettings_JvmArgs] = "Extra JVM arguments",
        [LocKeys.InstanceSettings_JvmArgsPlaceholder] = "e.g. -XX:+UseG1GC",
        [LocKeys.InstanceSettings_GameArgs] = "Extra game arguments",
        [LocKeys.InstanceSettings_GameArgsPlaceholder] = "Optional Minecraft args",
        [LocKeys.InstanceSettings_Window] = "Window",
        [LocKeys.InstanceSettings_Width] = "Width",
        [LocKeys.InstanceSettings_Height] = "Height",
        [LocKeys.InstanceSettings_ResolutionDefault] = "Default",
        [LocKeys.InstanceSettings_FullScreen] = "Fullscreen",
        [LocKeys.InstanceSettings_Server] = "Join server",
        [LocKeys.InstanceSettings_ServerHint] = "Optional. Connects to this address when the game starts.",
        [LocKeys.InstanceSettings_ServerIp] = "Address",
        [LocKeys.InstanceSettings_ServerIpPlaceholder] = "play.example.com",
        [LocKeys.InstanceSettings_ServerPort] = "Port",
        [LocKeys.InstanceSettings_Saved] = "Saved",
        [LocKeys.InstanceSettings_JavaFollowGlobal] = "Using the Java path from launcher Settings.",
        [LocKeys.InstanceSettings_JavaMissing] = "Selected Java path does not exist.",
        [LocKeys.InstanceSettings_InvalidResolution] = "Width and height must be positive numbers (or empty for default).",
        [LocKeys.InstanceSettings_InvalidPort] = "Port must be between 1 and 65535 (or empty).",
        [LocKeys.InstanceSettings_NavOverview] = "Overview",
        [LocKeys.InstanceSettings_NavRuntime] = "Runtime",
        [LocKeys.InstanceSettings_NavWindow] = "Window & server",
        [LocKeys.InstanceSettings_NavAdvanced] = "Advanced",
        [LocKeys.InstanceSettings_NavSettings] = "Settings",
        [LocKeys.InstanceSettings_NavManage] = "Manage",
        [LocKeys.InstanceSettings_NavMods] = "Mods",
        [LocKeys.InstanceSettings_NavResourcepacks] = "Resource Packs",
        [LocKeys.InstanceSettings_NavShaderpacks] = "Shader Packs",
        [LocKeys.InstanceSettings_NavSaves] = "Saves",
        [LocKeys.InstanceSettings_NavStatistics] = "Statistics",
        [LocKeys.InstanceSettings_StatsTotalPlayTime] = "Total play time",
        [LocKeys.InstanceSettings_StatsLaunchCount] = "Launches",
        [LocKeys.InstanceSettings_StatsLastPlayed] = "Last played",
        [LocKeys.InstanceSettings_StatsAvgSession] = "Avg. session",
        [LocKeys.InstanceSettings_StatsPlayTrend] = "Play time (last 14 days)",
        [LocKeys.InstanceSettings_StatsScreenshots] = "Screenshots",
        [LocKeys.InstanceSettings_StatsNoData] = "No play data yet. Launch this instance to start tracking.",
        [LocKeys.InstanceSettings_StatsOpenScreenshots] = "Open screenshots folder",
        [LocKeys.InstanceSettings_StatsDayTooltip] = "{0} · {1}",
        [LocKeys.InstanceSettings_Name] = "Instance name",
        [LocKeys.InstanceSettings_Rename] = "Rename",
        [LocKeys.InstanceSettings_Renamed] = "Renamed to {0}",
        [LocKeys.InstanceSettings_RenameFailed] = "Could not rename: {0}",
        [LocKeys.InstanceSettings_InfoType] = "Type",
        [LocKeys.InstanceSettings_InfoBase] = "Base game",
        [LocKeys.InstanceSettings_InfoJava] = "Suggested Java",
        [LocKeys.InstanceSettings_InfoJavaUnknown] = "Not detected",
        [LocKeys.InstanceSettings_Folders] = "Folders",
        [LocKeys.InstanceSettings_FolderMods] = "mods",
        [LocKeys.InstanceSettings_FolderSaves] = "saves",
        [LocKeys.InstanceSettings_FolderConfig] = "config",
        [LocKeys.InstanceSettings_FolderResourcepacks] = "resourcepacks",
        [LocKeys.InstanceSettings_FolderShaderpacks] = "shaderpacks",
        [LocKeys.InstanceSettings_FolderDatapacks] = "datapacks",
        [LocKeys.InstanceSettings_FolderScreenshots] = "screenshots",
        [LocKeys.InstanceSettings_FolderLogs] = "logs",
        [LocKeys.InstanceSettings_ManageTitle] = "Manage",
        [LocKeys.InstanceSettings_RuntimeTitle] = "Runtime",
        [LocKeys.InstanceSettings_WindowTitle] = "Window & server",
        [LocKeys.InstanceSettings_AdvancedTitle] = "Advanced",
        [LocKeys.InstanceSettings_OverviewTitle] = "Overview",
        [LocKeys.InstanceSettings_Notes] = "Notes",
        [LocKeys.InstanceSettings_NotesPlaceholder] = "Shown under this profile on the list",
        [LocKeys.InstanceSettings_PinQuickLaunch] = "Pin to quick launch",
        [LocKeys.InstanceSettings_PinQuickLaunchHint] = "Only one instance can be pinned. It appears on Home for one-click launch.",
        [LocKeys.InstanceSettings_CopyPath] = "Copy path",
        [LocKeys.InstanceSettings_PathCopied] = "Path copied",
        [LocKeys.InstanceSettings_MaxMemory] = "Maximum",
        [LocKeys.InstanceSettings_MinMemory] = "Minimum",
        [LocKeys.InstanceSettings_MinMemoryHint] = "Minimum heap size (-Xms). Keep it at or below maximum.",
        [LocKeys.InstanceSettings_MemoryUsed] = "In use",
        [LocKeys.InstanceSettings_MemoryGame] = "Game allocation",
        [LocKeys.InstanceSettings_MemoryMap] = "Allocation overview",
        [LocKeys.InstanceSettings_MemoryTotal] = "total",
        [LocKeys.InstanceSettings_MemoryFree] = "Available",
        [LocKeys.InstanceSettings_GameWindowTitle] = "Game window title",
        [LocKeys.InstanceSettings_GameWindowTitlePlaceholder] = "Leave empty for default",
        [LocKeys.InstanceSettings_GameWindowTitleHint] = "Placeholders: {version} {user}. Applied after the game window opens.",
        [LocKeys.InstanceSettings_ResolutionPreset] = "Resolution preset",
        [LocKeys.InstanceSettings_ResolutionCustom] = "Custom",
        [LocKeys.InstanceSettings_ResetTitle] = "Reset settings",
        [LocKeys.InstanceSettings_ResetHint] = "Clear Java, memory, window, server, and argument overrides for this profile.",
        [LocKeys.InstanceSettings_Reset] = "Reset",
        [LocKeys.InstanceSettings_ResetConfirm] = "Reset all launch overrides for this profile?",
        [LocKeys.InstanceSettings_ResetDone] = "Instance settings reset",
        [LocKeys.InstanceSettings_IconPresetLib] = "Preset Icon Library",
        [LocKeys.InstanceSettings_JvmPresetTitle] = "Optimization Presets",
        [LocKeys.InstanceSettings_JvmPresetClean] = "Clear All Arguments",
        [LocKeys.InstanceSettings_JvmPresetG1Gc] = "G1GC (Standard)",
        [LocKeys.InstanceSettings_JvmPresetShenandoah] = "ShenandoahGC (Java 11+)",
        [LocKeys.InstanceSettings_JvmPresetZgc] = "ZGC (Java 15+)",
        [LocKeys.InstanceSettings_JvmPresetGenZgc] = "Generational ZGC (Java 21+)",
        [LocKeys.InstanceSettings_JvmPresetGraalVm] = "GraalVM",
        [LocKeys.InstanceSettings_JvmPresetLowLatency] = "Low Latency G1GC",
        [LocKeys.InstanceSettings_JvmPresetLowMemory] = "Low Memory SerialGC",
        [LocKeys.InstanceSettings_JvmPresetAikar] = "Aikar's Flags",
        [LocKeys.InstanceSettings_Icon] = "Icon",
        [LocKeys.InstanceSettings_IconChange] = "Change icon",
        [LocKeys.InstanceSettings_IconClear] = "Remove icon",
        [LocKeys.InstanceSettings_IconUpdated] = "Icon updated",
        [LocKeys.InstanceSettings_IconCleared] = "Icon removed",
        [LocKeys.InstanceSettings_IconFailed] = "Could not set icon: {0}",
        [LocKeys.InstanceSettings_DuplicateTitle] = "Duplicate",
        [LocKeys.InstanceSettings_DuplicateHint] = "Creates a full copy of this profile (mods, saves, settings) under a new name.",
        [LocKeys.InstanceSettings_Duplicate] = "Duplicate profile",
        [LocKeys.InstanceSettings_Duplicated] = "Duplicated as {0}",
        [LocKeys.InstanceSettings_DuplicateFailed] = "Could not duplicate: {0}",
        [LocKeys.InstanceSettings_ExportTitle] = "Export",
        [LocKeys.InstanceSettings_ExportHint] =
            "Export this instance as a Modrinth modpack (.mrpack) you can share or reinstall.",
        [LocKeys.InstanceSettings_Export] = "Export as .mrpack",
        [LocKeys.InstanceSettings_Exporting] = "Exporting…",
        [LocKeys.InstanceSettings_Exported] = "Exported to {0}",
        [LocKeys.InstanceSettings_ExportFailed] = "Could not export: {0}",
        [LocKeys.InstanceSettings_ModPage] = "Web Page",
        [LocKeys.InstanceSettings_ModFolder] = "Folder",
        [LocKeys.InstanceSettings_ModDelete] = "Delete",
        [LocKeys.InstanceSettings_ModInfoTitle] = "Mod Information",
        [LocKeys.InstanceSettings_OverrideJava] = "Enable custom Java path",
        [LocKeys.InstanceSettings_OverrideMemory] = "Enable custom memory allocation",
        [LocKeys.InstanceSettings_ToggleDisable] = "Disable",
        [LocKeys.InstanceSettings_ToggleEnable] = "Enable",
        [LocKeys.InstanceSettings_ModFilterAll] = "All",
        [LocKeys.InstanceSettings_ModFilterEnabled] = "Enabled",
        [LocKeys.InstanceSettings_ModFilterDisabled] = "Disabled",
        [LocKeys.InstanceSettings_ModFilterUpdatable] = "Updates",
        [LocKeys.InstanceSettings_ModFilterUpdatableCount] = "Updates ({0})",
        [LocKeys.InstanceSettings_ModUpdateChecking] = "Checking for mod updates…",
        [LocKeys.InstanceSettings_RefreshingResources] = "Refreshing…",
        [LocKeys.InstanceSettings_ModUpdateAvailable] = "{0} mod(s) have updates for this instance.",
        [LocKeys.InstanceSettings_ModUpdateVersionChange] = "{0} → {1}",
        [LocKeys.InstanceSettings_ModUpdateAction] = "Update",
        [LocKeys.InstanceSettings_ModUpdateSelectAll] = "Select all",
        [LocKeys.InstanceSettings_ModUpdateSelectNone] = "Select none",
        [LocKeys.InstanceSettings_ModUpdateProgress] = "Updating {0}/{1}: {2}",
        [LocKeys.InstanceSettings_ModUpdateDone] = "Updated {0} mod(s).",
        [LocKeys.InstanceSettings_ModUpdateDoneWithErrors] = "Updated {0} mod(s), {1} failed.",
        [LocKeys.InstanceSettings_PackFolder] = "Folder",
        [LocKeys.InstanceSettings_PackInfoTitle] = "Pack Information",
        [LocKeys.InstanceSettings_PackUpdateChecking] = "Checking for pack updates…",
        [LocKeys.InstanceSettings_PackUpdateAvailable] = "{0} pack(s) have updates for this instance.",
        [LocKeys.InstanceSettings_PackUpdateDone] = "Updated {0} pack(s).",
        [LocKeys.InstanceSettings_PackUpdateDoneWithErrors] = "Updated {0} pack(s), {1} failed.",
        [LocKeys.InstanceSettings_FileSize] = "Size",
        [LocKeys.InstanceSettings_Modified] = "Modified",
        [LocKeys.InstanceSettings_NoOnlineMatch] = "No online catalog match.",
        [LocKeys.InstanceSettings_EmptyMods] = "No mods in this profile yet.",
        [LocKeys.InstanceSettings_EmptyModsVanilla] = "This profile has no mod loader. Install Fabric, Forge, or NeoForge to use mods.",
        [LocKeys.InstanceSettings_EmptyResourcePacks] = "No resource packs in this profile yet.",
        [LocKeys.InstanceSettings_EmptyDatapacks] = "No datapacks in this profile folder yet.",
        [LocKeys.InstanceSettings_EmptyShaderPacks] = "No shader packs in this profile yet.",
        [LocKeys.InstanceSettings_EmptySaves] = "No worlds in this profile yet.",
        [LocKeys.InstanceSettings_EmptyDropHint] = "You can also drop files here.",
        [LocKeys.InstanceSettings_DeleteConfirm] = "Move “{0}” to the Recycle Bin?",
        [LocKeys.InstanceSettings_DeleteConfirmMany] = "Move {0} items to the Recycle Bin?",
        [LocKeys.InstanceSettings_OverwriteConfirm] = "“{0}” already exists. Replace it?",
        [LocKeys.InstanceSettings_DropAdded] = "Added {0}.",
        [LocKeys.InstanceSettings_DropFailed] = "Could not add {0}: {1}",
        [LocKeys.InstanceSettings_DropRejected] = "This file type cannot be added here.",
        [LocKeys.InstanceSettings_SelectedCount] = "{0} selected",
        [LocKeys.InstanceSettings_OpenWeb] = "Web page",
        [LocKeys.InstanceSettings_OpenItemFolder] = "Show in folder",
        [LocKeys.InstanceSettings_DeleteItem] = "Delete",
        [LocKeys.InstanceSettings_SaveAdvancedTitle] = "Save Properties",
        [LocKeys.InstanceSettings_SaveAdvancedButton] = "Advanced properties",
        [LocKeys.InstanceSettings_SaveSectionWorld] = "World",
        [LocKeys.InstanceSettings_SaveSectionStorage] = "Storage",
        [LocKeys.InstanceSettings_SaveGameMode] = "Game mode",
        [LocKeys.InstanceSettings_SaveDifficulty] = "Difficulty",
        [LocKeys.InstanceSettings_SaveSeed] = "Seed",
        [LocKeys.InstanceSettings_SaveSpawn] = "Spawn",
        [LocKeys.InstanceSettings_SaveDay] = "Day",
        [LocKeys.InstanceSettings_SaveTime] = "Time",
        [LocKeys.InstanceSettings_SaveHardcore] = "Hardcore",
        [LocKeys.InstanceSettings_SaveAllowCommands] = "Allow commands",
        [LocKeys.InstanceSettings_SaveLastPlayed] = "Last played",
        [LocKeys.InstanceSettings_SaveVersion] = "Created in version",
        [LocKeys.InstanceSettings_SaveDataVersion] = "Data version",
        [LocKeys.InstanceSettings_SaveLastModified] = "Last modified",
        [LocKeys.InstanceSettings_SaveNoLevelDat] = "Could not read level.dat from this world.",
        [LocKeys.InstanceSettings_SaveUnknown] = "Unknown",
        [LocKeys.InstanceSettings_SaveValueYes] = "Yes",
        [LocKeys.InstanceSettings_SaveValueNo] = "No",
        [LocKeys.InstanceSettings_SaveGameModeSurvival] = "Survival",
        [LocKeys.InstanceSettings_SaveGameModeCreative] = "Creative",
        [LocKeys.InstanceSettings_SaveGameModeAdventure] = "Adventure",
        [LocKeys.InstanceSettings_SaveGameModeSpectator] = "Spectator",
        [LocKeys.InstanceSettings_SaveDifficultyPeaceful] = "Peaceful",
        [LocKeys.InstanceSettings_SaveDifficultyEasy] = "Easy",
        [LocKeys.InstanceSettings_SaveDifficultyNormal] = "Normal",
        [LocKeys.InstanceSettings_SaveDifficultyHard] = "Hard",
        [LocKeys.InstanceSettings_SaveWorldName] = "World name",
        [LocKeys.InstanceSettings_SaveSectionInfo] = "Info",
        [LocKeys.InstanceSettings_SaveSaveFailed] = "Could not save level.dat: {0}",
        [LocKeys.InstanceSettings_SaveInvalidWorldName] = "Enter a world name.",
        [LocKeys.InstanceSettings_SaveInvalidSeed] = "Enter a valid seed number.",
        [LocKeys.InstanceSettings_SaveInvalidDay] = "Day must be at least 1.",
        [LocKeys.InstanceSettings_SaveLoading] = "Reading world data…",
        [LocKeys.InstanceSettings_SaveRestoreBackup] = "Restore from backup",
        [LocKeys.InstanceSettings_SaveRestored] = "Restored level.dat from backup.",
        [LocKeys.InstanceSettings_SaveCover] = "World cover",

        [LocKeys.Home_ProcessError] = "Process error: {0}",

        [LocKeys.Account_Title] = "Account",
        [LocKeys.Account_Subtitle] = "Tap a card to sign in.",
        [LocKeys.Account_PlayerName] = "Player name",
        [LocKeys.Account_PlayerNameHint] = "Steve_01",
        [LocKeys.Account_Saved] = "Player name saved",
        [LocKeys.Account_Add] = "Add account",
        [LocKeys.Account_AddTitle] = "Add account",
        [LocKeys.Account_EditTitle] = "Edit account",
        [LocKeys.Account_Edit] = "Edit",
        [LocKeys.Account_Skin] = "Skin",
        [LocKeys.Account_SkinNone] = "No skin selected",
        [LocKeys.Account_PickSkinTitle] = "Choose skin",
        [LocKeys.Account_Empty] = "No accounts yet — add one to get started",
        [LocKeys.Account_EmptyHint] = "Use Add in the top right.",
        [LocKeys.Account_Reorder] = "Drag to reorder",
        [LocKeys.Account_ActiveBadge] = "Active",
        [LocKeys.Account_KindOffline] = "Offline",
        [LocKeys.Account_KindMicrosoft] = "Microsoft",
        [LocKeys.Account_Type] = "Account type",
        [LocKeys.Account_SkinCustom] = "Custom",
        [LocKeys.Account_SkinCustom1] = "Custom 1",
        [LocKeys.Account_SkinCustom2] = "Custom 2",
        [LocKeys.Account_SkinSlotEmpty] = "Not set — import a PNG",
        [LocKeys.Account_SkinReplace] = "Replace…",
        [LocKeys.Account_UuidLabel] = "UUID: {0}",
        [LocKeys.Account_MicrosoftPlaceholder] = "Sign in with the Microsoft account that owns Minecraft: Java Edition. A browser window will open.",
        [LocKeys.Account_MicrosoftComingSoon] = "This action is not available for Microsoft accounts.",
        [LocKeys.Account_MicrosoftSignIn] = "Sign in with Microsoft",
        [LocKeys.Account_MicrosoftSigningIn] = "Signing in…",
        [LocKeys.Account_MicrosoftSignInFailed] = "Microsoft sign-in failed: {0}",
        [LocKeys.Account_MicrosoftAppNotAllowlisted] = "Microsoft signed in, but Minecraft Services rejected this app (Client ID not allowlisted yet). Wait for Mojang approval, then try again.",
        [LocKeys.Account_MicrosoftNeedOwn] = "This Microsoft account does not own Minecraft: Java Edition.",
        [LocKeys.Account_MicrosoftRefreshFailed] = "Could not refresh the Microsoft session. Sign in again.",
        [LocKeys.Account_MicrosoftRefresh] = "Refresh Microsoft profile & skin",
        [LocKeys.Account_MicrosoftRefreshing] = "Syncing Microsoft profile & skin…",
        [LocKeys.Account_MicrosoftRefreshed] = "Microsoft profile and skin synced successfully",
        [LocKeys.Account_LoggingIn] = "Signing in as {0}…",
        [LocKeys.Account_LoginOffline] = "Switching offline profile…",
        [LocKeys.Account_LoggedIn] = "Signed in as {0}",
        [LocKeys.Account_LoggedOut] = "Signed out ({0})",
        [LocKeys.Account_LoginFailed] = "Sign-in failed: {0}",
        [LocKeys.Account_DeleteTitle] = "Delete account",
        [LocKeys.Account_DeleteConfirm] = "Delete \"{0}\"? This cannot be undone.",
        [LocKeys.Account_NeedLogin] = "Sign in on the Account page (tap a card) before launching.",
        [LocKeys.Account_NeedSkin] = "Choose a skin for this account first.",
        [LocKeys.Account_OpenSkinLibrary] = "Open skin library",
        [LocKeys.Account_SetActive] = "Set as Active",
        [LocKeys.Account_Deactivate] = "Sign Out",
        [LocKeys.Account_SkinDescMicrosoft] = "Official Microsoft skin synced with Mojang cloud servers.",
        [LocKeys.Account_SkinDescOffline] = "Offline skin saved in your local skin library.",
        [LocKeys.Account_CapeSubtitle] = "Official capes owned by this account. Click to equip or unequip.",
        [LocKeys.Account_SkinPick] = "Select from Library",
        [LocKeys.Account_ConfirmUploadSkin] = "Upload to Mojang",
        [LocKeys.Account_OfflineRequiresLicensedAccount] = "Please bind at least one licensed Microsoft account before adding offline accounts.",
        [LocKeys.Account_OfflineLockedHint] = "Offline mode is locked. Sign in to a licensed Microsoft account first to unlock offline mode.",
        [LocKeys.Account_OfflineLockedBadge] = "Locked",
        [LocKeys.Account_OfflineSegmentDisabledHint] = "Sign in with a licensed Microsoft account first to unlock offline mode.",
        [LocKeys.Account_OfflineLockedTitle] = "Licensed Account Required",
        [LocKeys.Account_OfflineLockedDetail] = "Under the Terms of Service and licensing policy, you must bind at least one valid licensed Microsoft account before using offline mode.",
        [LocKeys.Account_OfflineLockedGoSignIn] = "Sign in with Microsoft",
        [LocKeys.Account_OfflineLockedBuyLink] = "Buy Minecraft",

        [LocKeys.Download_SectionSkins] = "Skins",
        [LocKeys.Skin_LibraryTitle] = "Skin library",
        [LocKeys.Skin_LibrarySubtitle] = "Import and preview skins for offline or Microsoft profiles",
        [LocKeys.Skin_Add] = "Add skin",
        [LocKeys.Skin_AddTitle] = "Add skin",
        [LocKeys.Skin_Name] = "Skin name",
        [LocKeys.Skin_NamePlaceholder] = "Display name in the library",
        [LocKeys.Skin_NameRequired] = "Enter a skin name.",
        [LocKeys.Skin_ArmModel] = "Arm model",
        [LocKeys.Skin_ModelSteve] = "Steve (classic)",
        [LocKeys.Skin_ModelAlex] = "Alex (slim)",
        [LocKeys.Skin_Import] = "Import PNG…",
        [LocKeys.Skin_ImportRequired] = "Choose a skin PNG to import.",
        [LocKeys.Skin_ImportFailed] = "Could not import skin: {0}",
        [LocKeys.Skin_Empty] = "No skins yet — Steve and Alex are seeded automatically",
        [LocKeys.Skin_Count] = "{0} skins",
        [LocKeys.Skin_CannotDeleteBuiltIn] = "Built-in Steve/Alex skins cannot be deleted.",
        [LocKeys.Skin_Official] = "Skin",
        [LocKeys.Skin_OfficialSubtitle] = "Skin synced from cloud",
        [LocKeys.Account_ManageAppearance] = "Appearance & Personalization",
        [LocKeys.Account_TabSkin] = "Skin",
        [LocKeys.Account_TabCape] = "Cape",
        [LocKeys.Account_TabName] = "Change Name",
        [LocKeys.Account_ChangeSkinSubtitle] = "Select a PNG skin file and arm model to sync with your account",
        [LocKeys.Account_SelectSkinPng] = "Select Skin File…",
        [LocKeys.Account_ArmModel_Classic] = "Classic (4px)",
        [LocKeys.Account_ArmModel_Slim] = "Slim (3px)",
        [LocKeys.Account_ArmModelLabel] = "Arm Model",
        [LocKeys.Account_SkinSave] = "Save & Apply Skin",
        [LocKeys.Account_SkinSuccess] = "Skin updated successfully",
        [LocKeys.Account_SkinFailed] = "Failed to update skin: {0}",
        [LocKeys.Account_CapeNone] = "None (Unequipped)",
        [LocKeys.Account_CapeEmpty] = "No capes available for this account",
        [LocKeys.Account_CapeSave] = "Save Cape",
        [LocKeys.Account_CapeSuccess] = "Cape updated successfully",
        [LocKeys.Account_CapeFailed] = "Failed to update cape: {0}",
        [LocKeys.Account_CapeActive] = "Equipped",
        [LocKeys.Account_MojangRateLimit] = "Operation too frequent. Mojang servers rate limit cape changes; please wait a moment.",
        [LocKeys.Account_MojangRateLimitWait] = "Mojang rate limit cooldown. Please wait {0} seconds before trying again.",
        [LocKeys.Account_NameCurrent] = "Current Name",
        [LocKeys.Account_NameNew] = "New Name",
        [LocKeys.Account_NameCheck] = "Check Availability",
        [LocKeys.Account_NameCooldown] = "Minecraft names can only be changed once every 30 days. Currently in cooldown.",
        [LocKeys.Account_NameAvailable] = "This username is available",
        [LocKeys.Account_NameUnavailable] = "This username is unavailable or already taken",
        [LocKeys.Account_NameSave] = "Confirm Name Change",
        [LocKeys.Account_NameSuccess] = "Player name changed to: {0}",
        [LocKeys.Account_NameFailed] = "Failed to change name: {0}",
        [LocKeys.Account_Saving] = "Saving…",

        [LocKeys.Download_Type] = "Type",
        [LocKeys.Download_SectionMinecraft] = "Minecraft",
        [LocKeys.Download_SectionMod] = "Mod",
        [LocKeys.Download_SectionResourcePack] = "Resource Pack",
        [LocKeys.Download_SectionDatapack] = "Data Pack",
        [LocKeys.Download_SectionShaderPack] = "Shader Pack",
        [LocKeys.Download_SectionModpack] = "Modpack",
        [LocKeys.Catalog_DetailTitle] = "Details",
        [LocKeys.Download_Search] = "Search",
        [LocKeys.Download_SearchPlaceholder] = "e.g. 1.21",
        [LocKeys.Download_Installed] = "Installed",
        [LocKeys.Download_JavaTag] = "Java {0}",
        [LocKeys.Download_JavaTagPending] = "Java …",
        [LocKeys.Download_SelectHint] = "Click a version to choose name and loader",
        [LocKeys.Download_SelectRelease] = "Click a release to install",
        [LocKeys.Download_SelectSnapshot] = "Snapshots may be unstable — click one to install",
        [LocKeys.Download_SelectAprilFools] = "April Fools / joke clients — click one to install",
        [LocKeys.Download_NoResults] = "No versions match these filters.",
        [LocKeys.Catalog_KindMod] = "mods",
        [LocKeys.Catalog_KindResourcePack] = "resource packs",
        [LocKeys.Catalog_KindDatapack] = "datapacks",
        [LocKeys.Catalog_KindShaderPack] = "shader packs",
        [LocKeys.Catalog_KindModpack] = "modpacks",
        [LocKeys.Catalog_SearchHint] = "Set filters, then search to list {0}.",
        [LocKeys.Catalog_SearchEmpty] = "No matching {0}.",
        [LocKeys.Catalog_InstallDialogTitle] = "Install {0}",
        [LocKeys.Catalog_InstallNoCompatiblePack] = "No compatible profiles. Need a matching game version.",
        [LocKeys.Catalog_UpdatedJustNow] = "Updated just now",
        [LocKeys.Catalog_UpdatedMinutes] = "Updated {0}m ago",
        [LocKeys.Catalog_UpdatedHours] = "Updated {0}h ago",
        [LocKeys.Catalog_UpdatedDays] = "Updated {0}d ago",
        [LocKeys.Catalog_DatapackTargetHeader] = "Install target",
        [LocKeys.Catalog_DatapackTargetInstance] = "Instance datapacks",
        [LocKeys.Catalog_DatapackTargetWorld] = "Specific world",
        [LocKeys.Catalog_DatapackTargetInstanceHint] = "Applies to all worlds in this instance (instance/datapacks).",
        [LocKeys.Catalog_DatapackTargetWorldHint] = "Installs into one save's datapacks folder (saves/<world>/datapacks).",
        [LocKeys.Catalog_DatapackNoWorlds] = "No saves found. Create a world in this instance first.",
        [LocKeys.Catalog_DatapackPickWorld] = "Pick a world in {0}",
        [LocKeys.Catalog_DatapackInstallJobName] = "{0} → {1} / {2}",
        [LocKeys.Catalog_DetailEmptyRetry] = "No files for these filters. Change the version filter or retry.",
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
        [LocKeys.Download_ClearFinished] = "Clear finished",
        [LocKeys.Download_Retry] = "Retry",
        [LocKeys.Download_Dismiss] = "Remove from list",
        [LocKeys.Download_AlreadyRunning] = "{0} is already in the task list",
        [LocKeys.Category_Release] = "Release",
        [LocKeys.Category_Snapshot] = "Snapshot",
        [LocKeys.Category_AprilFools] = "April Fools",
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
        [LocKeys.Mod_SearchCountFiltered] = "{0} Mods · {1}",
        [LocKeys.Mod_SearchCountFilteredEmpty] = "No Mods for {0}",
        [LocKeys.Mod_SearchCountFilteredWithWarning] = "{0} Mods · {1} — {2}",
        [LocKeys.Mod_SearchFailed] = "Search failed: {0}",
        [LocKeys.Mod_SearchBothFailed] = "Search failed. Modrinth: {0}; CurseForge: {1}",
        [LocKeys.Mod_SearchPartialModrinth] = "Modrinth unavailable ({0}); showing CurseForge only.",
        [LocKeys.Mod_SearchPartialCurseForge] = "CurseForge unavailable ({0}); showing Modrinth only.",
        [LocKeys.Mod_DownloadsExact] = "{0} downloads",
        [LocKeys.Mod_DownloadsThousands] = "{0}K downloads",
        [LocKeys.Mod_DownloadsMillions] = "{0}M downloads",
        [LocKeys.Mod_PagePrevious] = "Previous",
        [LocKeys.Mod_PageNext] = "Next",
        [LocKeys.Mod_PageLabel] = "Page {0}",
        [LocKeys.Mod_VersionsEllipsis] = "{0}…",
        [LocKeys.Mod_LoaderLiteLoaderUnsupported] = "LiteLoader (unsupported)",
        [LocKeys.Mod_LiteLoaderUnsupportedHint] =
            "LiteLoader only supports Minecraft 1.5.2–1.12.2. It is not available for {0}.",
        [LocKeys.Mod_LiteLoaderUnsupportedStatus] =
            "LiteLoader does not support Minecraft {0}.",
        [LocKeys.Mod_DetailBack] = "Back",
        [LocKeys.Mod_DetailTitle] = "Mod",
        [LocKeys.Mod_DetailLoading] = "Loading Mod details…",
        [LocKeys.Mod_DetailLoadFailed] = "Could not load Mod details.",
        [LocKeys.Mod_DetailLoadFailedNamed] = "Could not load Mod details: {0}",
        [LocKeys.Mod_DetailUnknownSource] = "Unknown Mod source: {0}",
        [LocKeys.Mod_DetailInvalidId] = "Invalid Mod id: {0}",
        [LocKeys.Mod_DetailNoFiles] = "No downloadable files found.",
        [LocKeys.Mod_DetailFileCount] = "{0} files",
        [LocKeys.Mod_DetailVersionsHeader] = "Versions",
        [LocKeys.Mod_DetailOpenModrinth] = "Open on Modrinth",
        [LocKeys.Mod_DetailOpenCurseForge] = "Open on CurseForge",
        [LocKeys.Mod_DetailCopyName] = "Copy name",
        [LocKeys.Mod_DetailCopiedName] = "Name copied",
        [LocKeys.Mod_DetailChannelRelease] = "Release",
        [LocKeys.Mod_DetailChannelBeta] = "Beta",
        [LocKeys.Mod_DetailChannelAlpha] = "Alpha",
        [LocKeys.Mod_DetailFilterVersion] = "Match game version",
        [LocKeys.Mod_DetailFilterLoader] = "Match loader",
        [LocKeys.Mod_DetailFilterAllVersions] = "All versions",
        [LocKeys.Mod_DetailFilterAllLoaders] = "All loaders",
        [LocKeys.Mod_DetailDependenciesHeader] = "Dependencies",
        [LocKeys.Mod_DependencyRequired] = "Required",
        [LocKeys.Mod_DependencyOptional] = "Optional",
        [LocKeys.Mod_InstallDialogTitle] = "Install Mod",
        [LocKeys.Mod_InstallTitle] = "Install {0}",
        [LocKeys.Mod_InstallSubtitle] = "{0}",
        [LocKeys.Mod_InstallPickInstance] = "Choose a compatible instance (grouped by loader + version)",
        [LocKeys.Mod_InstallNoInstances] = "No instances installed yet — download a game version first.",
        [LocKeys.Mod_InstallNoCompatible] = "No compatible instances. Need a matching game version and loader — vanilla cannot install Mods.",
        [LocKeys.Mod_DetailRetry] = "Retry",
        [LocKeys.Mod_InstallPreferred] = "Matched",
        [LocKeys.Mod_InstallFileName] = "File name",
        [LocKeys.Mod_InstallJobName] = "{0} → {1}",
        [LocKeys.Mod_InstallDependencyJobName] = "{0} (dependency) → {1}",
        [LocKeys.Mod_InstallMissingDepsTitle] = "Missing dependencies",
        [LocKeys.Mod_InstallMissingDepsBody] = "This mod requires {1} dependencies that are not installed on this instance:\n\n{0}\n\nInstall them together, or install only this mod?",
        [LocKeys.Mod_InstallModOnly] = "Install mod only",
        [LocKeys.Mod_InstallWithDependencies] = "Install all ({0})",
        [LocKeys.Mod_InstallCheckingDeps] = "Checking dependencies…",
        [LocKeys.Mod_InstallDownloading] = "Downloading resource…",
        [LocKeys.Mod_InstallProgress] = "Downloading {0}",
        [LocKeys.Mod_InstallComplete] = "Installed {0} into {1}",
        [LocKeys.Mod_InstallModpackUnavailable] = "Modpack install is not available yet.",
        [LocKeys.Modpack_DialogTitle] = "Install modpack",
        [LocKeys.Modpack_InstallTitle] = "Install {0}",
        [LocKeys.Modpack_InstallSubtitle] = "{0}",
        [LocKeys.Modpack_InstanceName] = "Instance name",
        [LocKeys.Modpack_InstanceNamePlaceholder] = "Folder name under versions/",
        [LocKeys.Modpack_InstallHint] = "Creates a new isolated instance, installs the required loader, then downloads pack files.",
        [LocKeys.Modpack_JobName] = "{0} → {1}",
        [LocKeys.Modpack_Installing] = "Installing modpack…",
        [LocKeys.Modpack_DownloadingPack] = "Downloading modpack archive…",
        [LocKeys.Modpack_Parsing] = "Reading modpack manifest…",
        [LocKeys.Modpack_InstallingLoader] = "Installing Minecraft {0} + loader…",
        [LocKeys.Modpack_ApplyingOverrides] = "Applying overrides…",
        [LocKeys.Modpack_InstallComplete] = "Modpack installed as {0}",
        [LocKeys.Modpack_InvalidManifest] = "Invalid modpack manifest.",
        [LocKeys.Modpack_MissingManifest] = "No CurseForge manifest.json found in the archive.",
        [LocKeys.Modpack_MissingMrpackIndex] = "No modrinth.index.json found in the archive.",
        [LocKeys.Modpack_MissingMinecraft] = "Modpack does not specify a Minecraft version.",
        [LocKeys.Modpack_UnsupportedLoader] = "This modpack uses an unsupported loader.",
        [LocKeys.Modpack_UnsafePath] = "Unsafe pack path rejected: {0}",
        [LocKeys.Modpack_HashMismatch] = "Downloaded file failed hash verification.",
        [LocKeys.Modpack_FileDownloadFailed] = "{0} pack files failed. First error: {1}",
        [LocKeys.Modpack_UnresolvedFiles] = "{0} pack files could not be resolved from CurseForge. First: {1}",
        [LocKeys.Modpack_FileDownloadFailedGeneric] = "Could not download a pack file.",
        [LocKeys.Modpack_ResolvingFiles] = "Resolving pack files…",
        [LocKeys.Modpack_MissingFile] = "Missing pack file: {0}",
        [LocKeys.Modpack_Install] = "Install",
        [LocKeys.Mod_InstallFile] = "Install",
        [LocKeys.Modpack_ViewMods] = "Mods",
        [LocKeys.Modpack_ContentsTitle] = "Included mods",
        [LocKeys.Modpack_ContentsSubtitle] = "{0} · {1} mods",
        [LocKeys.Modpack_ContentsLoading] = "Loading pack contents…",
        [LocKeys.Modpack_ContentsEmpty] = "No included mods were listed for this version.",
        [LocKeys.Modpack_ContentsCount] = "{0} mods",
        [LocKeys.Modpack_OpenInLauncher] = "Open in Ardel",
        [LocKeys.Modpack_ImportTitle] = "Import modpack",
        [LocKeys.Modpack_ImportSectionHint] =
            "Install a local Modrinth (.mrpack) or CurseForge modpack as a new instance.",
        [LocKeys.Modpack_ImportBrowse] = "Import from file…",
        [LocKeys.Modpack_ImportDropHint] = "You can also drag a .mrpack or CurseForge zip onto the window.",
        [LocKeys.Modpack_ExportDialogTitle] = "Export modpack",
        [LocKeys.Modpack_ExportDialogSubtitle] =
            "Choose what to include. Thin packs download known mods from Modrinth instead of embedding jars.",
        [LocKeys.Modpack_ExportIncludeHeading] = "Include",
        [LocKeys.Modpack_ExportIncludeMods] = "Mods",
        [LocKeys.Modpack_ExportIncludeConfig] = "Config",
        [LocKeys.Modpack_ExportIncludeResourcePacks] = "Resource packs",
        [LocKeys.Modpack_ExportIncludeShaderPacks] = "Shader packs",
        [LocKeys.Modpack_ExportIncludeDatapacks] = "Datapacks",
        [LocKeys.Modpack_ExportIncludeOptions] = "Options / servers",
        [LocKeys.Modpack_ExportIncludeSaves] = "Worlds (saves)",
        [LocKeys.Modpack_ExportIncludeScreenshots] = "Screenshots",
        [LocKeys.Modpack_ExportThinPack] = "Prefer thin pack (Modrinth downloads)",
        [LocKeys.Modpack_ExportThinPackHint] =
            "Jars found on Modrinth go into files[] with URLs; others stay in overrides/.",
        [LocKeys.Modpack_ExportUnsupportedLoader] = "This instance’s loader cannot be exported as an .mrpack.",
        [LocKeys.Modpack_ExportResolvingThin] = "Resolving mods on Modrinth…",
        [LocKeys.Modpack_ExportThinProgress] = "Resolved {0}/{1} mods…",

        [LocKeys.Settings_Title] = "Settings",
        [LocKeys.Settings_Java] = "Java",
        [LocKeys.Settings_JavaPlaceholder] = "Auto / not selected",
        [LocKeys.Settings_MaxMemory] = "Max memory",
        [LocKeys.Settings_MemoryUnitMb] = " MB",
        [LocKeys.Memory_Custom] = "Custom",
        [LocKeys.Memory_FollowDefault] = "Follow launcher defaults",
        [LocKeys.Memory_FollowDefaultCustom] = "Uses the launcher default maximum: {0} MB.",
        [LocKeys.Memory_FollowDefaultDynamic] = "Uses the launcher default: dynamic allocation at launch.",
        [LocKeys.Memory_Dynamic] = "Dynamic allocation",
        [LocKeys.Memory_DynamicHint] =
            "At launch, scales heap targets with the mods folder size and claims free system RAM in tiers, keeping an OS reserve.",
        [LocKeys.Memory_Beta] = "Beta",
        [LocKeys.Settings_BmclTitle] = "BMCLAPI mirror",
        [LocKeys.Settings_BmclHint] =
            "Community mirror that usually speeds up Minecraft assets, libraries, and loader downloads in Asia (and other regions where Mojang CDNs are slow)",
        [LocKeys.Settings_ArdoTitle] = "Ardo download engine",
        [LocKeys.Settings_ArdoHint] = "Controls parallel downloads, speed limits, and integrity checks for game files.",
        [LocKeys.Settings_ArdoThreads] = "Download threads",
        [LocKeys.Settings_ArdoSpeedLimit] = "Speed limit (KB/s)",
        [LocKeys.Settings_ArdoSpeedLimitHint] = "0 = unlimited",
        [LocKeys.Settings_ArdoChunkSize] = "Chunk size (MiB)",
        [LocKeys.Settings_ArdoVerifySha1] = "Verify SHA1 after download",
        [LocKeys.Settings_ArdoMaxRetries] = "Retries per file",
        [LocKeys.Settings_ArdoSummary] = "Ardo · {0} threads · {1}",
        [LocKeys.Settings_ArdoUnlimited] = "no limit",
        [LocKeys.Settings_BehaviorTitle] = "After game launch",
        [LocKeys.Settings_BehaviorHint] = "What to do with Ardel’s window, mirrors, and logs once the game has started.",
        [LocKeys.Settings_PostLaunchAction] = "Window action",
        [LocKeys.Settings_PostLaunchNone] = "Do nothing",
        [LocKeys.Settings_PostLaunchMinimize] = "Minimize Ardel",
        [LocKeys.Settings_PostLaunchMinimizeUntilExit] = "Minimize until game exits",
        [LocKeys.Settings_PostLaunchExit] = "Exit Ardel",
        [LocKeys.Settings_BmclUsageMode] = "Mirror usage",
        [LocKeys.Settings_BmclUsageAlways] = "Always use BMCLAPI",
        [LocKeys.Settings_BmclUsageFallback] = "Fallback when official is slow",
        [LocKeys.Settings_BmclUsageDisabledHint] = "Enable the BMCLAPI mirror above to configure usage.",
        [LocKeys.Settings_GameLogViewer] = "Game log viewer",
        [LocKeys.Settings_GameLogViewerHint] = "Open a separate window that tails latest.log when the game starts (does not close Ardel when the game exits).",
        [LocKeys.Settings_ResourceRepairTitle] = "Resource repair",
        [LocKeys.Settings_ResourceRepairHint] = "Scan and repair game files before launch.",
        [LocKeys.Settings_ResourceRepairEnabled] = "Enable resource repair",
        [LocKeys.Settings_ResourceRepairConcurrency] = "Max parallel checks",
        [LocKeys.Settings_ResourceVerifyMode] = "Verify mode before launch",
        [LocKeys.Settings_ResourceVerifyTrustReady] = "Trust .ardel-ready marker",
        [LocKeys.Settings_ResourceVerifyQuick] = "Quick existence check",
        [LocKeys.Settings_ResourceVerifyFull] = "Full SHA1 verify",
        [LocKeys.Settings_ResourceRepairAutoDownload] = "Auto-download missing files",
        [LocKeys.Settings_ResourceRepairWriteReady] = "Write .ardel-ready when complete",
        [LocKeys.Settings_ResourceRepairBlockLaunch] = "Block launch on repair failure",
        [LocKeys.Settings_CrashAnalysisTitle] = "Crash analysis",
        [LocKeys.Settings_CrashAnalysisHint] = "Only opens when a crash-report or hs_err from this session exists (or a native abort exit). Speaks only for matched log patterns — prefer miss over a wrong diagnosis. Soft guesses are disabled.",
        [LocKeys.Settings_CrashAnalysisEnabled] = "Enable crash analysis",
        [LocKeys.Settings_CrashAnalysisOnForceKill] = "Show on force stop",
        [LocKeys.Settings_CrashAnalysisAutoOpenLogs] = "Open logs folder automatically",
        [LocKeys.Settings_CrashAnalysisKeepRecent] = "Keep recent reports",
        [LocKeys.Settings_CrashAnalysisPreIndex] = "Pre-index log paths at launch",
        [LocKeys.Settings_CrashAnalysisVerbose] = "Verbose debug info",
        [LocKeys.Settings_CrashAnalysisShowConfidence] = "Show confidence percentage",
        [LocKeys.Settings_DialogDebugTitle] = "Crash dialog layout (dev)",
        [LocKeys.Settings_DialogDebugDevLabel] = "DEV ONLY",
        [LocKeys.Settings_DialogDebugEntry] = "Open crash-dialog layout check…",
        [LocKeys.Settings_DialogDebugHint] = "Layout check only (Loc title/explain/solution). Does not invent crash-reports or log excerpts. Real analysis only quotes files on disk.",
        [LocKeys.Settings_DialogDebugCrashSection] = "Crash dialog layouts",
        [LocKeys.Settings_DialogDebugCrashKnown] = "Known layout",
        [LocKeys.Settings_DialogDebugCrashSuspected] = "Suspected layout",
        [LocKeys.Settings_DialogDebugCrashUnknown] = "Unknown layout",
        [LocKeys.Settings_DialogDebugPickRule] = "Rule Loc to preview",
        [LocKeys.Settings_DialogDebugPreviewSelected] = "Show",
        [LocKeys.GameLog_Title] = "Game log",
        [LocKeys.GameLog_Waiting] = "Waiting for latest.log…",
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
        [LocKeys.Settings_JavaNone] = "No Java found. Scan again or browse to java.exe.",
        [LocKeys.Settings_JavaSelected] = "Using Java {0}",
        [LocKeys.Settings_Language] = "Language",
        [LocKeys.Settings_LanguageSystem] = "System default",
        [LocKeys.Settings_LanguageEnglish] = "English (US)",
        [LocKeys.Settings_LanguageChinese] = "简体中文",
        [LocKeys.Settings_LanguageJapanese] = "日本語",
        [LocKeys.Settings_LanguageEnglishUS] = "English (US)",
        [LocKeys.Settings_LanguageEnglishUK] = "English (UK)",
        [LocKeys.Settings_LanguageChineseTraditional] = "Traditional Chinese",
        [LocKeys.Settings_LanguageFrench] = "Français",
        [LocKeys.Settings_LanguageSpanish] = "Español",
        [LocKeys.Settings_LanguageKorean] = "한국어",
        [LocKeys.Settings_LanguageGerman] = "Deutsch",
        [LocKeys.Settings_LanguagePortuguese] = "Português (Brasil)",
        [LocKeys.Settings_LanguageItalian] = "Italiano",
        [LocKeys.Settings_LanguageRussian] = "Русский",
        [LocKeys.Settings_LanguageRestartHint] = "Applies immediately across the launcher.",
        [LocKeys.Settings_RestartNow] = "Apply",
        [LocKeys.Settings_LanguageApplied] = "Language: {0} · nav sample: {1}",
        [LocKeys.Settings_ScanningJava] = "Scanning Java…",
        [LocKeys.Settings_Theme] = "Theme",
        [LocKeys.Settings_ThemeDefault] = "Follow system",
        [LocKeys.Settings_ThemeLight] = "Light",
        [LocKeys.Settings_ThemeDark] = "Dark",
        [LocKeys.Settings_ThemeSakura] = "Sakura Pink",
        [LocKeys.Settings_ThemeSamoyed] = "Samoyed",
        [LocKeys.Settings_ThemeSweden] = "Sweden",
        [LocKeys.Settings_ThemeArctic] = "Arctic",
        [LocKeys.Settings_ThemeAurora] = "Aurora",
        [LocKeys.Settings_ThemeCedar] = "Cedar",
        [LocKeys.Settings_ThemeCinder] = "Cinder",
        [LocKeys.Settings_ThemeHoney] = "Honey",
        [LocKeys.Settings_ThemeMoss] = "Moss",
        [LocKeys.Settings_ThemeObsidian] = "Obsidian",
        [LocKeys.Settings_ThemePeach] = "Peach",
        [LocKeys.Settings_ThemeTwilight] = "Twilight",
        [LocKeys.Settings_ThemeDescDefault] = "Follows Windows light/dark mode with the same palette as Light and Dark, and Mica.",
        [LocKeys.Settings_ThemeDescLight] = "A bright, clean interface with a classic blue accent.",
        [LocKeys.Settings_ThemeDescDark] = "A sleek dark interface with a soft gray accent.",
        [LocKeys.Settings_ThemeDescSakura] = "A soft pastel pink theme with strawberry accents.",
        [LocKeys.Settings_ThemeDescSamoyed] = "A cozy white fluffy Samoyed fur texture background with cool blue-gray accents. — Dedicated to the memory of my beloved Samoyed.",
        [LocKeys.Settings_ThemeDescSweden] = "A Scandinavian light shell with Swedish blue and gold accents.",
        [LocKeys.Settings_ThemeDescArctic] = "A cold near-white shell with ice-blue accents.",
        [LocKeys.Settings_ThemeDescAurora] = "A night-blue canvas with aurora green accents.",
        [LocKeys.Settings_ThemeDescCedar] = "Warm wood tones with cedar-brown accents.",
        [LocKeys.Settings_ThemeDescCinder] = "Charcoal night with ember-orange accents.",
        [LocKeys.Settings_ThemeDescHoney] = "Cream canvas with honey-gold accents.",
        [LocKeys.Settings_ThemeDescMoss] = "Quiet forest dark with moss-green accents.",
        [LocKeys.Settings_ThemeDescObsidian] = "Near-black, sharp corners, pale graphite accents.",
        [LocKeys.Settings_ThemeDescPeach] = "Soft peach shell with ripe coral accents.",
        [LocKeys.Settings_ThemeDescTwilight] = "Dusk-blue night with muted violet-blue accents.",
        [LocKeys.Settings_StartupSplash] = "Startup splash",
        [LocKeys.Settings_StartupSplashDesc] = "Customize the Ardel badge screen shown while the launcher starts.",
        [LocKeys.Settings_StartupSplashEnabled] = "Show startup splash",
        [LocKeys.Settings_StartupSplashProgress] = "Show progress bar",
        [LocKeys.Settings_StartupSplashDuration] = "Minimum display time",
        [LocKeys.Settings_StartupSplashDurationUnit] = " ms",
        [LocKeys.Settings_StartupSplashDurationHint] = "How long the splash stays visible at minimum (1000–12000 ms).",
        [LocKeys.Settings_StartupSplashBrand] = "Show Ardel badge",
        [LocKeys.Settings_StartupSplashPreview] = "Preview splash",
        [LocKeys.Settings_StartupSplashPreviewDone] = "Splash preview finished.",
        [LocKeys.Settings_StartupSplashPreviewDisabled] = "Enable the startup splash to preview it.",
        [LocKeys.Settings_QuickLaunch] = "Quick launch",
        [LocKeys.Settings_QuickLaunchDesc] = "Control the Home quick-launch button for your pinned instance.",
        [LocKeys.Settings_QuickLaunchShowOnHome] = "Show quick launch button on Home",
        [LocKeys.Settings_HomeWidgets] = "Home widgets",
        [LocKeys.Settings_HomeWidgetsDesc] =
            "Show weather and date at the top of the Home page. Set a region to enable weather.",
        [LocKeys.Settings_HomeWidgetsShowWeather] = "Show weather",
        [LocKeys.Settings_HomeWidgetsShowCalendar] = "Show date",
        [LocKeys.Settings_HomeWidgetsUse12HourClock] = "Use 12-hour clock (AM/PM)",
        [LocKeys.Settings_HomeWidgetsRegion] = "Weather region",
        [LocKeys.Settings_HomeWidgetsRegionHint] =
            "Open the region dialog to search, or use current location.",
        [LocKeys.Settings_HomeWidgetsRegionEnglishTip] =
            "Search by city name in your language, or use current location.",
        [LocKeys.Settings_HomeWidgetsRegionPlaceholder] = "City name",
        [LocKeys.Settings_HomeWidgetsRegionDialogTitle] = "Choose weather region",
        [LocKeys.Settings_HomeWidgetsChooseRegion] = "Choose region…",
        [LocKeys.Settings_HomeWidgetsRegionNone] = "No region set",
        [LocKeys.Settings_HomeWidgetsSearching] = "Searching…",
        [LocKeys.Settings_HomeWidgetsNoResults] = "No places found. Try another spelling.",
        [LocKeys.Settings_HomeWidgetsSearchFailed] = "Search failed: {0}",
        [LocKeys.Settings_HomeWidgetsClearRegion] = "Clear region",
        [LocKeys.Settings_HomeWidgetsUseCurrentLocation] = "Use current location",
        [LocKeys.Settings_HomeWidgetsLocating] = "Getting location…",
        [LocKeys.Settings_HomeWidgetsLocationDenied] = "Location access was denied.",
        [LocKeys.Settings_HomeWidgetsLocationFailed] = "Could not get location: {0}",
        [LocKeys.Settings_HomeWidgetsCurrentLocationLabel] = "Current location",
        [LocKeys.Settings_HomeWidgetsUseFahrenheit] = "Use Fahrenheit (°F)",
        [LocKeys.Settings_HomeWidgetsRegionSet] = "Region set to {0}",
        [LocKeys.Home_WeatherNeedRegion] = "Set a region in Personalization → Home widgets",
        [LocKeys.Home_WeatherLoading] = "Loading weather…",
        [LocKeys.Home_WeatherFailed] = "Weather unavailable",
        [LocKeys.Home_WeatherTodayRange] = "{0} – {1}",
        [LocKeys.Home_WeatherConditionTodayRange] = "{0} Today {1}",
        [LocKeys.Weather_Clear] = "Clear",
        [LocKeys.Weather_ClearNight] = "Clear night",
        [LocKeys.Weather_MainlyClear] = "Mostly clear",
        [LocKeys.Weather_PartlyCloudy] = "Partly cloudy",
        [LocKeys.Weather_Overcast] = "Overcast",
        [LocKeys.Weather_Fog] = "Fog",
        [LocKeys.Weather_Drizzle] = "Drizzle",
        [LocKeys.Weather_FreezingDrizzle] = "Freezing drizzle",
        [LocKeys.Weather_Rain] = "Rain",
        [LocKeys.Weather_FreezingRain] = "Freezing rain",
        [LocKeys.Weather_Snow] = "Snow",
        [LocKeys.Weather_RainShowers] = "Showers",
        [LocKeys.Weather_SnowShowers] = "Snow showers",
        [LocKeys.Weather_Thunderstorm] = "Thunderstorm",
        [LocKeys.Weather_ThunderstormHail] = "Thunderstorm with hail",
        [LocKeys.Weather_Unknown] = "Weather",
        [LocKeys.Settings_Subtitle] =
            "Personalization and launcher defaults — instances inherit these unless overridden.",
        [LocKeys.Settings_SectionAppearance] = "Personalization",
        [LocKeys.Settings_SectionAppearanceDesc] =
            "Choose the interface language and accent theme applied across Ardel.",
        [LocKeys.Settings_SectionDefaults] = "Defaults",
        [LocKeys.Settings_SectionDefaultsDesc] =
            "Java, memory, download mirror, and Minecraft folder used when an instance has no override.",
        [LocKeys.Settings_SectionAbout] = "About",
        [LocKeys.Settings_SectionAboutDesc] =
            "Version, license, open-source credits, and terms of service.",
        [LocKeys.Settings_CheckUpdate] = "Check for updates",
        [LocKeys.Settings_DownloadUpdate] = "Download update",
        [LocKeys.Settings_UpdateChecking] = "Checking for updates...",
        [LocKeys.Settings_UpdateUpToDate] = "Ardel is up to date.",
        [LocKeys.Settings_UpdateAvailable] = "Version {0} is available.",
        [LocKeys.Settings_UpdateDownloading] = "Downloading update... {0:0}%",
        [LocKeys.Settings_UpdateRestart] = "Restart to update",
        [LocKeys.Settings_UpdateReadyToRestart] = "Update downloaded. Tap the button to restart and apply.",
        [LocKeys.Settings_UpdateFailed] = "Update check failed: {0}",
        [LocKeys.Settings_UpdateReleaseNotes] = "Release notes",
        [LocKeys.Skin_NameSteve] = "Steve",
        [LocKeys.Skin_NameAlex] = "Alex",

        [LocKeys.About_Title] = "About",
        [LocKeys.About_Subtitle] =
            "A portable Minecraft launcher for Windows — local instances, offline play, and curated downloads.",
        [LocKeys.About_Version] = "v{0}",
        [LocKeys.About_LicenseHeading] = "License",
        [LocKeys.About_LicenseName] = "Open Software License",
        [LocKeys.About_LicenseId] = "OSL-3.0",
        [LocKeys.About_License] =
            "Ardel is distributed under OSL-3.0. You may use, modify, and redistribute it under the terms of that license.",
        [LocKeys.About_LegalHeading] = "Terms of Service",
        [LocKeys.About_LegalUpdated] = "Version {0} - Effective date: {1}",
        [LocKeys.About_LegalIntro] =
            "The following terms apply to all users of Ardel. Read each section carefully; choosing a display language does not change your legal obligations.",
        [LocKeys.About_LegalFeedback] = "Report a legal or compliance issue",
        [LocKeys.About_CopyrightNoticeHeading] = "Copyright Notice",
        [LocKeys.About_CopyrightNoticeBody] =
            "Copyright © 2026 FlameGemini. All rights reserved.\n\nArdel is distributed under the Open Software License version 3.0 (OSL-3.0). Any modification, derivative work, or redistribution of this software must strictly comply with OSL-3.0 terms, including the mandatory availability of complete source code.\n\nClosed-source bundling, unauthorized commercial reselling, removal of copyright notices or author attribution, and intellectual property infringement are strictly prohibited. This project is not affiliated with, endorsed by, or sponsored by Mojang Studios or Microsoft Corporation. The author reserves all legal rights to enforce compliance and seek damages and legal costs via statutory DMCA procedures and applicable laws.",
        [LocKeys.About_OpenNoticeFile] = "View NOTICE.txt",
        [LocKeys.About_SourceLink] = "View source",
        [LocKeys.About_RerunSetup] = "Run setup wizard again",
        [LocKeys.About_OpenLink] = "Open",
        [LocKeys.About_CopyVersion] = "Copy version",
        [LocKeys.About_CreditsHeading] = "Acknowledgments",
        [LocKeys.About_CreditsIntro] =
            "Third-party software and services that power Ardel. Tap a row to open its home page.",
        [LocKeys.About_LibrariesHeading] = "Libraries",
        [LocKeys.About_ServicesHeading] = "Services & APIs",
        [LocKeys.About_FontsHeading] = "Fonts",
        [LocKeys.About_Credit_CmlLib] =
            "Minecraft launch and install core by CmlLib / AlphaBs.",
        [LocKeys.About_Credit_CmlLibForge] =
            "Forge and NeoForge installer modules by CmlLib contributors.",
        [LocKeys.About_Credit_OptifineInstaller] =
            "Community helper for OptiFine installation workflows.",
        [LocKeys.About_Credit_CommunityToolkit] =
            ".NET Community Toolkit MVVM helpers for observable UI state.",
        [LocKeys.About_Credit_AuthlibInjector] =
            "Offline skin relay agent by yushijinhun.",
        [LocKeys.About_Credit_MinecraftSkinRender] =
            "Minecraft skin rendering component and 3D head generator by Coloryr.",
        [LocKeys.About_Credit_SkiaSharp] =
            "Cross-platform 2D graphics API based on Google's Skia, used for skin rendering.",
        [LocKeys.About_Credit_XboxAuthNet] =
            "Xbox Live authentication and MSAL token integration by CmlLib.",
        [LocKeys.About_Credit_BmclApi] =
            "Optional Asia-friendly download mirror for Minecraft files, operated by bangbang93.",
        [LocKeys.About_Credit_Adoptium] =
            "Eclipse Adoptium API for automated Temurin OpenJDK runtime downloads.",
        [LocKeys.About_Credit_MsDi] =
            "Microsoft.Extensions dependency injection used for launcher services.",
        [LocKeys.About_Credit_Wasdk] =
            "Windows App SDK and WinUI 3 shell framework by Microsoft.",
        [LocKeys.About_Credit_DotNet] =
            ".NET runtime and base class libraries used by the launcher.",
        [LocKeys.About_Credit_Modrinth] =
            "Mod / modpack catalog and downloads via the Modrinth API.",
        [LocKeys.About_Credit_CurseForge] =
            "CurseForge catalog access through the community curse.tools proxy.",
        [LocKeys.About_Credit_JetBrainsMono] =
            "Packaged monospace (SIL OFL 1.1). Home greeting and clock in every language.",
        [LocKeys.About_Credit_Consolas] =
            "English UI body and display default; also used in the game log panel. System font — not redistributed.",
        [LocKeys.About_Credit_SegoeUiVariable] =
            "Non-English shell UI. System font — not redistributed.",
        [LocKeys.About_Credit_YaHeiUi] =
            "Simplified Chinese UI. System font — not redistributed.",
        [LocKeys.About_Credit_JhengHeiUi] =
            "Traditional Chinese UI. System font — not redistributed.",
        [LocKeys.About_Credit_YuGothicUi] =
            "Japanese UI. System font — not redistributed.",
        [LocKeys.About_Credit_MeiryoUi] =
            "Japanese UI fallback. System font — not redistributed.",
        [LocKeys.About_Credit_MalgunGothic] =
            "Korean UI. System font — not redistributed.",
        [LocKeys.About_Credit_SegoeUi] =
            "General UI fallback across locales. System font — not redistributed.",
        [LocKeys.About_Credit_SegoeFluentIcons] =
            "Icon glyphs in the game log panel. System font — not redistributed.",
        [LocKeys.About_Credit_OpenMeteo] =
            "Open-Meteo weather and geocoding APIs for the Home weather widget. No API key required.",
        [LocKeys.About_Credit_Nominatim] =
            "OpenStreetMap Nominatim search API for location lookup.",
        [LocKeys.About_OfficialPurchaseEncourage] =
            "We encourage purchasing and activating genuine Minecraft: Java Edition through official channels such as the Microsoft Store or minecraft.net.",
        [LocKeys.About_Disclaimer] = AboutLegalNotice.English,

        [LocKeys.Oobe_Previous] = "Previous",
        [LocKeys.Oobe_Next] = "Next",
        [LocKeys.Oobe_Finish] = "Get started",
        [LocKeys.Oobe_StepLanguage_Title] = "Choose your language",
        [LocKeys.Oobe_StepLanguage_Subtitle] = "You can change this later in Settings.",
        [LocKeys.Oobe_StepWelcome_Title] = "Welcome to Ardel Launcher",
        [LocKeys.Oobe_StepWelcome_Subtitle] = "We're glad you chose our product.",
        [LocKeys.Oobe_StepLicense_Title] = "Terms of use",
        [LocKeys.Oobe_StepLicense_Agree] = "I have read and agree to the terms",
        [LocKeys.Oobe_StepTheme_Title] = "Pick a theme",
        [LocKeys.Oobe_StepTheme_Subtitle] = "Preview applies to the launcher and this setup.",
        [LocKeys.Oobe_StepTutorial_Title] = "Quick start",
        [LocKeys.Oobe_StepTutorial_Subtitle] = "Follow these five steps in order to play for the first time.",
        [LocKeys.Oobe_StepTutorial_Step1_Title] = "Open Download",
        [LocKeys.Oobe_StepTutorial_Step1_Detail] = "In the sidebar, open Download to browse available Minecraft versions.",
        [LocKeys.Oobe_StepTutorial_Step2_Title] = "Install a version",
        [LocKeys.Oobe_StepTutorial_Step2_Detail] = "Pick a release and start the install. A game profile is created automatically.",
        [LocKeys.Oobe_StepTutorial_Step3_Title] = "Wait for the download",
        [LocKeys.Oobe_StepTutorial_Step3_Detail] = "Let the download task finish. Track progress from the sidebar indicator.",
        [LocKeys.Oobe_StepTutorial_Step4_Title] = "Sign in to an account",
        [LocKeys.Oobe_StepTutorial_Step4_Detail] = "Open Account, add your Minecraft account, and tap the card to sign in.",
        [LocKeys.Oobe_StepTutorial_Step5_Title] = "Launch the game",
        [LocKeys.Oobe_StepTutorial_Step5_Detail] = "Open Profiles and tap Launch on your install. Minecraft opens in a new window once launch starts.",
        [LocKeys.Oobe_StepBreak_Title] = "Water bar",
        [LocKeys.Oobe_StepBreak_Body] = "",
        [LocKeys.Oobe_StepAccount_Title] = "Add an offline account",
        [LocKeys.Oobe_StepAccount_Subtitle] = "Optional — you can add more accounts later.",
        [LocKeys.Oobe_StepHydrate_Title] = "Water",
        [LocKeys.Oobe_StepHydrate_Body] = "",
        [LocKeys.Oobe_StepComplete_Title] = "You're all set",
        [LocKeys.Oobe_StepComplete_Subtitle] = "Tap Finish to enter Ardel, then download a version, sign in, and launch when you're ready.",

        [LocKeys.Version_Fabric] = "Fabric {0}",
        [LocKeys.Version_Quilt] = "Quilt {0}",
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
        [LocKeys.Install_LoaderQuilt] = "Quilt",
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
        [LocKeys.Error_QuiltProfileInvalid] =
            "Quilt profile was not created correctly. Pick a version name different from the Minecraft id and try again.",
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
        [LocKeys.Validate_PlayerLength] = "Player name must be 3–16 characters.",
        [LocKeys.Validate_PlayerCharset] = "Invalid characters in player name.",
        [LocKeys.Validate_SkinNameInvalid] = "Skin name cannot contain path characters.",
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

        if (key.StartsWith("Crash_", StringComparison.Ordinal) &&
            TryResolveCrash(key, tag, out var crash))
            return crash.Replace("\\n", "\n", StringComparison.Ordinal);

        if (IsTag(tag, "zh-Hant") &&
            ChineseTraditional.TryGetValue(key, out var hant) && !string.IsNullOrEmpty(hant))
            return hant.Replace("\\n", "\n", StringComparison.Ordinal);

        if ((IsTag(tag, "zh-CN") || IsTag(tag, "zh-Hans") ||
             (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && !IsTag(tag, "zh-Hant"))) &&
            Chinese.TryGetValue(key, out var zh) && !string.IsNullOrEmpty(zh))
            return zh.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase) &&
            Japanese.TryGetValue(key, out var ja) && !string.IsNullOrEmpty(ja))
            return ja.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("fr", StringComparison.OrdinalIgnoreCase) &&
            French.TryGetValue(key, out var fr) && !string.IsNullOrEmpty(fr))
            return fr.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("es", StringComparison.OrdinalIgnoreCase) &&
            Spanish.TryGetValue(key, out var es) && !string.IsNullOrEmpty(es))
            return es.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase) &&
            Korean.TryGetValue(key, out var ko) && !string.IsNullOrEmpty(ko))
            return ko.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("de", StringComparison.OrdinalIgnoreCase) &&
            German.TryGetValue(key, out var de) && !string.IsNullOrEmpty(de))
            return de.Replace("\\n", "\n", StringComparison.Ordinal);

        if ((tag.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) &&
            Portuguese.TryGetValue(key, out var pt) && !string.IsNullOrEmpty(pt))
            return pt.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("it", StringComparison.OrdinalIgnoreCase) &&
            Italian.TryGetValue(key, out var it) && !string.IsNullOrEmpty(it))
            return it.Replace("\\n", "\n", StringComparison.Ordinal);

        if (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) &&
            Russian.TryGetValue(key, out var ru) && !string.IsNullOrEmpty(ru))
            return ru.Replace("\\n", "\n", StringComparison.Ordinal);

        if (IsEnglishUk(tag) &&
            EnglishUk.TryGetValue(key, out var uk) && !string.IsNullOrEmpty(uk))
            return uk.Replace("\\n", "\n", StringComparison.Ordinal);

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

    private static bool IsTag(string tag, string expected) =>
        tag.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsEnglishUk(string tag) =>
        IsTag(tag, "en-UK") || IsTag(tag, "en-GB");

    private static bool TryResolveCrash(string key, string tag, out string value)
    {
        value = string.Empty;
        Dictionary<string, string>? table = null;
        if (IsTag(tag, "zh-Hant"))
            table = CrashStringCatalog.ChineseTraditional;
        else if (IsTag(tag, "zh-CN") || IsTag(tag, "zh-Hans") ||
                 (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && !IsTag(tag, "zh-Hant")))
            table = CrashStringCatalog.Chinese;
        else if (tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.Japanese;
        else if (tag.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.French;
        else if (tag.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.Spanish;
        else if (tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.Korean;
        else if (tag.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.German;
        else if (tag.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.Portuguese;
        else if (tag.StartsWith("it", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.Italian;
        else if (tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            table = CrashStringCatalog.Russian;
        else
            table = CrashStringCatalog.English;

        if (table.TryGetValue(key, out var hit) && !string.IsNullOrEmpty(hit))
        {
            value = hit;
            return true;
        }

        if (!ReferenceEquals(table, CrashStringCatalog.English) &&
            CrashStringCatalog.English.TryGetValue(key, out var en) && !string.IsNullOrEmpty(en))
        {
            value = en;
            return true;
        }

        return false;
    }
}

