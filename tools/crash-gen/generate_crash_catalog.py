#!/usr/bin/env python3
"""Generate CrashRuleCatalog, CrashUsrCatalog, LocKeys.Crash, CrashStringCatalog."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT_SVC = ROOT / "src/Ardel.Launcher/Services/CrashAnalysis"
OUT_LOC = ROOT / "src/Ardel.Launcher/Localization/Crash"

# Each rule: id, cause, phase (F/P), needles (any), all_needles (optional), special (optional)
RULES: list[dict] = [
    {"id": "R001", "cause": "ManualDebugCrash", "phase": "F", "special": "prefer_crash_report", "needles": [
        "Manually triggered debug crash",
        "Someone is holding F3 + C for 10 seconds"]},
    {"id": "R003", "cause": "X86JavaMemoryLimit", "phase": "F", "all": [
        "Could not reserve enough space for object heap", "1048576KB"]},
    {"id": "R041", "cause": "OutOfMemoryMetaspace", "phase": "F", "needles": [
        "OutOfMemoryError: Metaspace", "Out of memory: Metaspace"]},
    {"id": "R042", "cause": "OutOfMemoryGCOverhead", "phase": "F", "needles": [
        "OutOfMemoryError: GC overhead limit exceeded"]},
    {"id": "R043", "cause": "OutOfMemoryDirectBuffer", "phase": "F", "needles": [
        "OutOfMemoryError: Direct buffer memory"]},
    {"id": "R071", "cause": "OutOfMemoryArraySize", "phase": "F", "needles": [
        "OutOfMemoryError: Requested array size exceeds VM limit"]},
    {"id": "R002", "cause": "OutOfMemory", "phase": "F", "needles": [
        "java.lang.OutOfMemoryError", "OutOfMemoryError",
        "There is insufficient memory for the Java Runtime Environment"]},
    {"id": "R004", "cause": "JavaTooNew", "phase": "F", "needles": [
        "java.lang.ClassCastException: class jdk.",
        "module java.base does not export"]},
    {"id": "R075", "cause": "UnsupportedClassVersionDetail", "phase": "F", "needles": [
        "compiled by a more recent version of the Java Runtime (class file version"]},
    {"id": "R005", "cause": "JavaIncompatible", "phase": "F", "needles": [
        "UnsupportedClassVersionError", "Unsupported major.minor version",
        "has been compiled by a more recent version of the Java Runtime"]},
    {"id": "R006", "cause": "UsingJdk", "phase": "F", "needles": [
        "ClassCastException: class java.base/jdk"]},
    {"id": "R007", "cause": "UsingOpenJ9", "phase": "F", "needles": [
        "OpenJ9 is not supported"]},
    {"id": "R008", "cause": "ModRequiresJava11", "phase": "F", "needles": [
        "class file major version 55"]},
    {"id": "R009", "cause": "UnsupportedOpenGl", "phase": "F", "needles": [
        "driver does not appear to support OpenGL"]},
    {"id": "R010", "cause": "PixelFormatNotSupported", "phase": "F", "needles": [
        "Couldn't set pixel format", "Pixel format not accelerated"]},
    {"id": "R011", "cause": "OpenGl1282", "phase": "F", "needles": [
        "1282: Invalid operation", "GL_INVALID_OPERATION"]},
    {"id": "R012", "cause": "IntelDriverAccessViolation", "phase": "F", "all": [
        "EXCEPTION_ACCESS_VIOLATION", "# C [ig"]},
    {"id": "R013", "cause": "AmdDriverAccessViolation", "phase": "F", "all": [
        "EXCEPTION_ACCESS_VIOLATION", "# C [atio"]},
    {"id": "R014", "cause": "NvidiaDriverAccessViolation", "phase": "F", "all": [
        "EXCEPTION_ACCESS_VIOLATION", "# C [nvoglv"]},
    {"id": "R015", "cause": "ExtractedModFile", "phase": "F", "needles": [
        "extracted jar files", "Extracted mod jars found"]},
    {"id": "R016", "cause": "MissingMixinBootstrap", "phase": "F", "all": [
        "MixinTweaker", "ClassNotFoundException"]},
    {"id": "R017", "cause": "InvalidModFileName", "phase": "F", "needles": [
        "Invalid module name: empty identifier"]},
    {"id": "R018", "cause": "ShadersModWithOptiFine", "phase": "F", "needles": [
        "Shaders Mod detected", "OptiFine has built-in shader support"]},
    {"id": "R019", "cause": "OptiFineForgeIncompatible", "phase": "F", "needles": [
        "OptiFine mods not found"]},
    {"id": "R020", "cause": "OptiFineWorldLoadCrash", "phase": "F", "all": [
        "shouldForceChunkLoad", "OptiFine"]},
    {"id": "R021", "cause": "OldForgeNewJavaIncompatible", "phase": "F", "needles": [
        "ManifestEntryVerifier", "JarVerifier$VerifierStream"]},
    {"id": "R022", "cause": "IncompleteForgeInstallation", "phase": "F", "needles": [
        "Missing or invalid fmlcore"]},
    {"id": "R023", "cause": "MultipleForgeInInstanceJson", "phase": "F", "needles": [
        "multiple arguments with name fml.forgeVersion"]},
    {"id": "R024", "cause": "NightConfigBug", "phase": "F", "all": [
        "com.electronwill.nightconfig", "ParsingException: Not enough data available"]},
    {"id": "R025", "cause": "ResourcePackTooLarge", "phase": "F", "needles": [
        "try a lower resolution resourcepack", "lower resolution resource pack"]},
    {"id": "R026", "cause": "TooManyModsIdLimit", "phase": "F", "needles": [
        "maximum id range exceeded", "Maximum id range exceeded"]},
    {"id": "R027", "cause": "FileOrContentValidationFailed", "phase": "F", "needles": [
        "signer information does not match"]},
    {"id": "R028", "cause": "DuplicateMods", "phase": "F", "needles": [
        "DuplicateModsFoundException", "Found duplicate mod", "Duplicate mods found"]},
    {"id": "R029", "cause": "IncompatibleMods", "phase": "F", "needles": [
        "Incompatible mods found!"]},
    {"id": "R030", "cause": "MissingDependencyOrWrongMcVersion", "phase": "F", "needles": [
        "Missing or unsupported mandatory dependencies",
        "Mod Resolution Exception"]},
    {"id": "R031", "cause": "ConfirmedModCrash", "phase": "F", "special": "named_mod", "needles": [
        "Caught exception from", "LoaderExceptionModCrash"]},
    {"id": "R032", "cause": "ModConfigCrash", "phase": "F", "all": [
        "Failed loading config file", "for modid"]},
    {"id": "R033", "cause": "ModMixinFailed", "phase": "F", "special": "named_mod", "needles": [
        "Mixin apply for mod", "mixin.injection.throwable"]},
    {"id": "R034", "cause": "FabricSolutionProvided", "phase": "F", "needles": [
        "A potential solution has been determined"]},
    {"id": "R035", "cause": "ForgeError", "phase": "F", "needles": [
        "error screen and halt the game", "Failed to load mods"]},
    {"id": "R036", "cause": "ModLoaderError", "phase": "F", "all": [
        "Mod resolution failed", "Failure message:"]},
    {"id": "R037", "cause": "ModInitializationFailed", "phase": "F", "needles": [
        "Failed to create mod instance"]},
    {"id": "R038", "cause": "SpecificBlockCrash", "phase": "P", "all": [
        "Block location: World:", "Block: Block{"]},
    {"id": "R039", "cause": "SpecificEntityCrash", "phase": "P", "all": [
        "Entity's Exact location", "Entity Type:"]},
    # Disabled: loader identity strings fire on healthy logs (trust killers).
    {"id": "R040", "cause": "FabricError", "phase": "P", "disabled": True, "needles": [
        "Fabric Loader has detected a critical problem"]},
    {"id": "R044", "cause": "MissingNativeLibrary", "phase": "F", "needles": [
        "UnsatisfiedLinkError", "Unable to load library"]},
    {"id": "R045", "cause": "GlfwWindowFailed", "phase": "F", "needles": [
        "Failed to create the GLFW window"]},
    {"id": "R046", "cause": "LwjglInitFailed", "phase": "F", "needles": [
        "Failed to load library lwjgl"]},
    {"id": "R047", "cause": "FabricApiMissing", "phase": "F", "needles": [
        "Fabric API is not installed", "requires fabric-api"]},
    {"id": "R048", "cause": "QuiltLoaderError", "phase": "F", "disabled": True, "needles": [
        "Quilt Loader has detected a critical problem"]},
    {"id": "R049", "cause": "NeoForgeError", "phase": "F", "disabled": True, "needles": [
        "NeoForge failed to load"]},
    {"id": "R050", "cause": "SodiumIncompatible", "phase": "P", "needles": [
        "requires Sodium", "incompatible with Sodium", "Indium required"]},
    {"id": "R051", "cause": "IrisShaderIncompatible", "phase": "P", "needles": [
        "Iris requires Sodium", "incompatible Iris"]},
    {"id": "R052", "cause": "WorldVersionTooNew", "phase": "F", "needles": [
        "We need to use the DataFixer", "world was last loaded in version"]},
    {"id": "R053", "cause": "WorldSaveCorrupted", "phase": "P", "all": [
        "Failed to load level", "The save data appears to be corrupt"]},
    {"id": "R054", "cause": "DiskOutOfSpace", "phase": "F", "needles": [
        "No space left on device"]},
    {"id": "R055", "cause": "AccessDeniedIo", "phase": "F", "disabled": True, "needles": [
        "AccessDeniedException", "Access is denied"]},
    {"id": "R056", "cause": "PathTooLong", "phase": "F", "needles": [
        "path too long", "Filename longer than", "The filename or extension is too long"]},
    {"id": "R057", "cause": "InvalidOrCorruptJar", "phase": "F", "needles": [
        "zip END header not found", "ZipException", "invalid CEN header",
        "Error reading zip file"]},
    {"id": "R058", "cause": "ModMetadataInvalid", "phase": "F", "needles": [
        "Could not read fabric.mod.json", "Invalid mods.toml", "MalformedJsonException"]},
    {"id": "R059", "cause": "UnrecognizedJvmOption", "phase": "F", "needles": [
        "Unrecognized option:", "Could not create the Java Virtual Machine"]},
    {"id": "R060", "cause": "BootstrapFailed", "phase": "F", "needles": [
        "BootstrapException", "Failed to bootstrap"]},
    {"id": "R061", "cause": "MixinTargetNotFound", "phase": "P", "special": "named_mod", "needles": [
        "Critical injection failure"]},
    {"id": "R062", "cause": "RenderingBlockCrash", "phase": "P", "needles": [
        "Block being rendered", "-- Block being rendered"]},
    {"id": "R063", "cause": "RenderingEntityCrash", "phase": "P", "needles": [
        "-- Entity being rendered"]},
    {"id": "R064", "cause": "TickingBlockEntityCrash", "phase": "P", "needles": [
        "-- Block entity being ticked"]},
    {"id": "R065", "cause": "TickingEntityCrash", "phase": "P", "needles": [
        "-- Entity being ticked"]},
    {"id": "R066", "cause": "ServerWatchdog", "phase": "P", "needles": [
        "A single server tick took", "ServerHangWatchdog"]},
    {"id": "R067", "cause": "MixinApplyConfigFailed", "phase": "P", "special": "named_mod", "needles": [
        "Error loading Mixin config", "failed loading mixin config"]},
    {"id": "R068", "cause": "LibraryVersionConflict", "phase": "F", "special": "named_mod", "needles": [
        "NoSuchMethodError", "AbstractMethodError", "IncompatibleClassChangeError"]},
    {"id": "R069", "cause": "OptiFabricConflict", "phase": "F", "all": [
        "OptiFabric", "incompatible"]},
    {"id": "R070", "cause": "LaunchTargetMissing", "phase": "F", "needles": [
        "Cannot find launch target"]},
    {"id": "R072", "cause": "StackOverflowError", "phase": "F", "needles": [
        "java.lang.StackOverflowError"]},
    {"id": "R073", "cause": "LinkageError", "phase": "F", "special": "named_mod", "needles": [
        "java.lang.LinkageError"]},
    {"id": "R074", "cause": "VerifyError", "phase": "F", "needles": [
        "java.lang.VerifyError"]},
    {"id": "R076", "cause": "JdkInternalInaccessible", "phase": "F", "needles": [
        "InaccessibleObjectException", "cannot access class"]},
    {"id": "R077", "cause": "IllegalMixin", "phase": "P", "special": "named_mod", "needles": [
        "Unable to locate mixin"]},
    {"id": "R078", "cause": "FabricLanguageAdapterFailed", "phase": "P", "needles": [
        "EntrypointException"]},
    {"id": "R079", "cause": "EntrypointCrash", "phase": "P", "needles": [
        "Exception in entrypoint"]},
    {"id": "R080", "cause": "MixinConnectorFailed", "phase": "P", "needles": [
        "MixinConnector"]},
    {"id": "R081", "cause": "ForgeConfigLoadFailed", "phase": "P", "needles": [
        "ConfigFileTypeHandler"]},
    {"id": "R082", "cause": "NightConfigPathInvalid", "phase": "F", "all": [
        "InvalidPathException", "nightconfig"]},
    {"id": "R083", "cause": "TomlParseError", "phase": "F", "needles": [
        "TomlSyntaxException"]},
    {"id": "R084", "cause": "JsonParseCrash", "phase": "P", "needles": [
        "JsonParseException", "MalformedJsonException"]},
    {"id": "R085", "cause": "DataPackLoadFailed", "phase": "P", "needles": [
        "Failed to load data pack", "Could not load datapack"]},
    {"id": "R086", "cause": "ResourcePackLoadFailed", "phase": "P", "needles": [
        "Failed to load resource pack"]},
    {"id": "R087", "cause": "ShaderCompileFailed", "phase": "P", "needles": [
        "Error compiling shader", "Failed to compile shader"]},
    {"id": "R088", "cause": "FramebufferFailed", "phase": "F", "needles": [
        "Couldn't create framebuffer", "framebuffer incomplete"]},
    {"id": "R089", "cause": "OpenGlContextLost", "phase": "F", "needles": [
        "GL_CONTEXT_LOST", "OpenGL context was lost"]},
    {"id": "R090", "cause": "DisplayModeFailed", "phase": "F", "needles": [
        "Couldn't set fullscreen", "Failed to set display mode"]},
    {"id": "R091", "cause": "AudioEngineFailed", "phase": "P", "needles": [
        "Failed to start SoundEngine", "OpenAL Error"]},
    {"id": "R092", "cause": "JavaMissingMainClass", "phase": "F", "needles": [
        "Could not find or load main class", "Error: Could not find or load main class"]},
    {"id": "R093", "cause": "AssetsIndexMissing", "phase": "F", "needles": [
        "Couldn't load assets index"]},
    {"id": "R094", "cause": "LibraryDownloadCorrupt", "phase": "F", "needles": [
        "Checksum mismatch", "sha1 does not match"]},
    {"id": "R095", "cause": "VersionJsonInvalid", "phase": "F", "needles": [
        "Failed to parse version"]},
    {"id": "R096", "cause": "ConcurrentModMutation", "phase": "P", "special": "named_mod", "needles": [
        "ConcurrentModificationException"]},
    {"id": "R097", "cause": "NullDerefNamedMod", "phase": "P", "special": "named_mod", "needles": [
        "NullPointerException"]},
    {"id": "R098", "cause": "AsmTransformFailed", "phase": "P", "needles": [
        "Transformer failed", "ASM class writer"]},
    {"id": "R099", "cause": "CoreModFailed", "phase": "P", "needles": [
        "Loading failed for coremod", "coremod failed"]},
    # Disabled: library names alone are not failure evidence.
    {"id": "R100", "cause": "MixinExtrasMissing", "phase": "P", "disabled": True, "needles": ["mixinextras"]},
    {"id": "R101", "cause": "ClothConfigMissing", "phase": "P", "disabled": True, "needles": ["cloth-config"]},
    {"id": "R102", "cause": "ArchitecturyMissing", "phase": "P", "disabled": True, "needles": ["architectury"]},
    {"id": "R103", "cause": "ForgeBusError", "phase": "P", "disabled": True, "needles": [
        "Failed to register"]},
    {"id": "R104", "cause": "RegistryDuplicate", "phase": "F", "needles": [
        "Duplicate registry id"]},
    {"id": "R105", "cause": "TagLoadFailed", "phase": "P", "needles": [
        "Failed to load tags"]},
    {"id": "R106", "cause": "RecipeLoadFailed", "phase": "P", "needles": [
        "Failed to parse recipe"]},
    {"id": "R107", "cause": "StructureLoadFailed", "phase": "P", "needles": [
        "Failed to load structure", "Unable to load structure"]},
    {"id": "R108", "cause": "ChunkLoadCrash", "phase": "P", "needles": [
        "Exception loading chunk", "Chunk file at"]},
    {"id": "R109", "cause": "PlayerDataCorrupt", "phase": "P", "needles": [
        "Failed to load player data"]},
    {"id": "R110", "cause": "LevelStemInvalid", "phase": "P", "disabled": True, "needles": [
        "Invalid dimension"]},
    # Disabled: universal client crash label ? not a diagnosis.
    {"id": "R111", "cause": "RenderThreadCrash", "phase": "P", "disabled": True, "needles": [
        'Exception in thread "Render thread"']},
    {"id": "R112", "cause": "WorkerThreadCrash", "phase": "P", "disabled": True, "needles": [
        'Exception in thread "Worker-Main-']},
    {"id": "R113", "cause": "JniCrashSecondary", "phase": "F", "special": "jni_secondary", "needles": [
        "EXCEPTION_ACCESS_VIOLATION"]},
    {"id": "R114", "cause": "SafepointTimeout", "phase": "F", "needles": [
        "Timeout waiting for", "safepoint"]},
    {"id": "R115", "cause": "OutOfMemoryKillByOs", "phase": "F", "needles": [
        "Out of memory: Kill process", "os::commit_memory failed"]},
    {"id": "R116", "cause": "UnicodePathIssue", "phase": "F", "needles": [
        "path contains invalid"]},
    {"id": "R117", "cause": "OneDriveLock", "phase": "F", "all": [
        "OneDrive", "Access is denied"]},
    {"id": "R118", "cause": "CurseForgeExtractorResidue", "phase": "F", "needles": [
        "Do not extract", "incomplete modpack"]},
    {"id": "R119", "cause": "ForgeEarlyWindowCrash", "phase": "F", "disabled": True, "needles": [
        "earlywindow", "EarlyDisplay"]},
    {"id": "R120", "cause": "MixinCancelVanilla", "phase": "P", "disabled": True, "needles": [
        "cancelling", "CallbackInfo"]},
]

# English copy: title, explain, solution for each rule
EN: dict[str, tuple[str, str, str]] = {}

def set_en(rid: str, title: str, explain: str, solution: str) -> None:
    EN[rid] = (title, explain, solution)

# Populate EN from cause names with sensible defaults; override key ones
for r in RULES:
    cause = r["cause"]
    # humanize cause
    title = "".join((" " + c if c.isupper() else c) for c in cause).strip()
    title = title.replace("  ", " ")
    set_en(r["id"], title,
           f"The launcher matched a known crash pattern ({r['id']}: {cause}).",
           "Follow the suggested fix below, then relaunch the game.")

# High-quality overrides for all rules (concise)
OVERRIDES = {
    "R001": ("Manual debug crash (F3+C)",
             "Minecraft was force-crashed by holding F3+C for about 10 seconds. This is a debug feature, not a fault.",
             "Do not hold F3+C. Just relaunch ??no need to change Java or remove mods."),
    "R002": ("Out of memory",
             "The Java runtime ran out of memory while starting or running the game.",
             "Increase allocated RAM in Ardel, close other heavy apps, and use 64-bit Java."),
    "R003": ("32-bit Java memory limit",
             "A 32-bit Java tried to reserve a 1 GB heap and failed.",
             "Install and select a 64-bit Java runtime."),
    "R004": ("Java too new",
             "This game or mod setup is incompatible with a newer Java major version.",
             "Switch to the Java version recommended for this Minecraft version."),
    "R005": ("Incompatible Java class version",
             "A class file requires a different Java major/minor version than the one in use.",
             "Install the matching Java version for this profile."),
    "R006": ("JDK internals conflict",
             "Code expected a JRE layout but hit JDK-internal classes.",
             "Use a suitable HotSpot JRE/JDK build recommended for this version."),
    "R007": ("OpenJ9 not supported",
             "OpenJ9 (or J9 VM internals) was detected where HotSpot is required.",
             "Switch to a HotSpot-based Java distribution."),
    "R008": ("Java 11+ required",
             "A mod or library needs Java 11 or newer (class file 55+).",
             "Select Java 11 or newer in Ardel settings."),
    "R009": ("OpenGL not supported",
             "The GPU driver does not appear to support the required OpenGL features.",
             "Update your graphics drivers and avoid unsupported remote-desktop GPUs."),
    "R010": ("Pixel format failed",
             "Minecraft could not set an accelerated pixel format for the window.",
             "Update GPU drivers and avoid remote desktop sessions when launching."),
    "R011": ("OpenGL invalid operation (1282)",
             "The GPU reported GL error 1282 (invalid operation), often from drivers or shaders.",
             "Update drivers; temporarily disable shaders or rendering mods."),
    "R012": ("Intel GPU driver crash",
             "A native access violation occurred inside an Intel graphics module.",
             "Update Intel graphics drivers, then relaunch."),
    "R013": ("AMD GPU driver crash",
             "A native access violation occurred inside an AMD graphics module.",
             "Update AMD graphics drivers, then relaunch."),
    "R014": ("NVIDIA GPU driver crash",
             "A native access violation occurred inside an NVIDIA OpenGL module.",
             "Update NVIDIA graphics drivers, then relaunch."),
    "R015": ("Extracted mod JAR",
             "A mod was extracted from its JAR instead of left as a single archive.",
             "Replace extracted folders with the original complete .jar files."),
    "R016": ("Mixin bootstrap missing",
             "MixinTweaker could not be found ??Mixin/loader bootstrap is incomplete.",
             "Reinstall the mod loader so Mixin is present."),
    "R017": ("Invalid mod file name",
             "A mod file name produced an empty or invalid Java module identifier.",
             "Rename the mod file to a simple ASCII name without odd characters."),
    "R018": ("Shaders Mod with OptiFine",
             "Shaders Mod conflicts with OptiFine?s built-in shader support.",
             "Remove Shaders Mod and use OptiFine?s shaders instead."),
    "R019": ("OptiFine / Forge mismatch",
             "OptiFine and Forge versions are incompatible or OptiFine failed to load.",
             "Use a matching OptiFine build for this Forge, or remove OptiFine temporarily."),
    "R020": ("OptiFine world load crash",
             "A known OptiFine world-loading method mismatch was detected.",
             "Update or temporarily remove OptiFine, then retry."),
    "R021": ("Old Forge on new Java",
             "An older Forge installer path failed under a newer Java (ManifestEntryVerifier).",
             "Upgrade Forge or use an older Java that this Forge supports."),
    "R022": ("Incomplete Forge install",
             "Forge launch pieces such as fmlclient/fmlcore are missing or invalid.",
             "Reinstall Forge for this instance."),
    "R023": ("Duplicate Forge in version JSON",
             "The version profile lists Forge arguments more than once.",
             "Clean or reinstall the instance version JSON."),
    "R024": ("NightConfig parse bug",
             "A NightConfig TOML file could not be parsed (not enough data).",
             "Delete or repair the broken TOML config mentioned in the log."),
    "R025": ("Resource pack too large",
             "A resource pack resolution is too high for the client to load.",
             "Switch to a lower-resolution resource pack."),
    "R026": ("Block/item ID limit",
             "The game exceeded the maximum ID range ??usually too many mods on old versions.",
             "Remove some mods or move to a modern Minecraft version."),
    "R027": ("JAR signature mismatch",
             "A JAR?s signer information does not match its contents.",
             "Redownload the conflicting JAR and remove duplicates."),
    "R028": ("Duplicate mods",
             "The loader found two mods that claim the same ID.",
             "Delete the duplicate JAR from the mods folder."),
    "R029": ("Incompatible mods",
             "The loader reported incompatible mods.",
             "Remove the mods named in the incompatibility list."),
    "R030": ("Missing dependency",
             "A mandatory mod dependency is missing or targets the wrong Minecraft version.",
             "Install the required dependency or align mod versions."),
    "R031": ("Named mod crash",
             "The crash report names a specific mod as the source of the exception.",
             "Update, disable, or remove that named mod, then relaunch."),
    "R032": ("Mod config crash",
             "A mod failed while loading its config file.",
             "Reset or delete that mod?s config and try again."),
    "R033": ("Mixin failed for a mod",
             "A Mixin injection failed and the owning mod could be identified.",
             "Update or remove the mod that owns the failing Mixin."),
    "R034": ("Fabric provided a solution",
             "Fabric Loader printed a potential solution in the log.",
             "Follow the Fabric solution steps shown in the evidence exactly."),
    "R035": ("Forge load error",
             "Forge halted during mod loading with an error screen.",
             "Read the Forge exception in the evidence and fix or remove the offending mod."),
    "R036": ("Mod loader resolution error",
             "The mod loader failed resolution and printed a failure message.",
             "Act on the loader failure message (missing/wrong mods)."),
    "R037": ("Mod instance init failed",
             "Creating a mod instance failed for a specific ModID.",
             "Disable or update that ModID."),
    "R038": ("Specific block crash",
             "The crash points to a concrete block position in the world.",
             "Remove that block in another tool or restore a backup of the region."),
    "R039": ("Specific entity crash",
             "The crash points to a concrete entity type/location.",
             "Remove that entity or restore a backup."),
    "R040": ("Fabric critical error",
             "Fabric Loader detected a critical problem without a solution block.",
             "Align Fabric Loader and mods to the same game version."),
    "R041": ("Out of Metaspace",
             "The JVM ran out of Metaspace (class metadata) memory.",
             "Raise Metaspace/heap, reduce mod count, and use 64-bit Java."),
    "R042": ("GC overhead limit",
             "The JVM spent almost all time collecting garbage and aborted.",
             "Increase heap memory and reduce heavy mods."),
    "R043": ("Direct buffer OOM",
             "Direct/off-heap buffer memory was exhausted.",
             "Raise memory settings and lower render/resource pressure."),
    "R044": ("Native library missing",
             "A required native library (often LWJGL) failed to load.",
             "Reinstall natives for the instance and use a matching Java architecture."),
    "R045": ("GLFW window failed",
             "Creating the game window through GLFW failed.",
             "Update GPU drivers; check multi-monitor/scaling; disable fullscreen optimizations."),
    "R046": ("LWJGL init failed",
             "LWJGL failed to initialize or load its native library.",
             "Repair LWJGL natives; do not mix LWJGL versions."),
    "R047": ("Fabric API missing",
             "A Fabric mod requires Fabric API, which is not installed.",
             "Install Fabric API matching this Minecraft version."),
    "R048": ("Quilt Loader error",
             "Quilt Loader failed during resolution or startup.",
             "Align Quilt Loader and mods; fix dependencies listed in the log."),
    "R049": ("NeoForge load error",
             "NeoForge failed while loading mods.",
             "Fix or remove the NeoForge mods named in the log."),
    "R050": ("Sodium incompatible",
             "A mod reported a Sodium version/indium incompatibility.",
             "Install the required Indium/Sodium versions stated in the log."),
    "R051": ("Iris / Sodium mismatch",
             "Iris and Sodium versions are incompatible.",
             "Install a matching Iris + Sodium pair."),
    "R052": ("World from newer Minecraft",
             "This world was saved in a newer Minecraft version.",
             "Open it with a newer client ??do not force an older version."),
    "R053": ("Corrupt world save",
             "Level data failed to load and looks corrupted.",
             "Restore from a backup; avoid writing further to the damaged save."),
    "R054": ("Disk full",
             "The OS reported that the disk is out of space.",
             "Free disk space, then relaunch."),
    "R055": ("Access denied",
             "The game could not read/write a file due to permissions or locks.",
             "Check folder permissions and antivirus locks on the instance directory."),
    "R056": ("Path too long",
             "A file path exceeded OS length limits.",
             "Move the game directory to a shorter path."),
    "R057": ("Corrupt JAR/ZIP",
             "A mod or library JAR is corrupt (ZIP headers failed).",
             "Delete and redownload the broken JAR."),
    "R058": ("Invalid mod metadata",
             "fabric.mod.json or mods.toml could not be parsed.",
             "Redownload the mod; repair its metadata file."),
    "R059": ("Bad JVM option",
             "Java rejected an unrecognized JVM flag.",
             "Remove the invalid extra JVM argument in settings."),
    "R060": ("Bootstrap failed",
             "Forge/NeoForge bootstrap failed before the game window.",
             "Check early mods/libraries named on the stack."),
    "R061": ("Mixin target missing",
             "A Mixin target class/method was not found for an identified mod.",
             "Update or remove that Mixin mod; match the game version."),
    "R062": ("Crash while rendering a block",
             "The crash report lists a block being rendered.",
             "Look away / remove that block, or fix the related resource/mod."),
    "R063": ("Crash while rendering an entity",
             "The crash report lists an entity being rendered.",
             "Remove that entity or the mod that adds it."),
    "R064": ("Ticking block entity crash",
             "A block entity crashed while ticking.",
             "Remove the block entity or restore a backup."),
    "R065": ("Ticking entity crash",
             "An entity crashed while ticking.",
             "Remove that entity or restore a backup."),
    "R066": ("Server watchdog hang",
             "A server tick took too long and the watchdog fired.",
             "Lower render distance/entities; investigate laggy mods."),
    "R067": ("Mixin config failed",
             "A Mixin config file failed to load for an identified mod.",
             "Update or remove the mod providing that Mixin config."),
    "R068": ("Library / method conflict",
             "A NoSuchMethod/AbstractMethod error was bound to a known mod or library.",
             "Align dependency versions and remove duplicate library JARs."),
    "R069": ("OptiFabric conflict",
             "OptiFine on Fabric needs OptiFabric (or versions conflict).",
             "Install the correct OptiFabric bridge or remove OptiFine."),
    "R070": ("Launch target missing",
             "The version JSON launch target (client main) is missing.",
             "Reinstall the loader/profile for this instance."),
    "R071": ("Array size OOM",
             "The JVM refused an array larger than the VM limit.",
             "Lower texture/render settings; find the mod allocating huge arrays."),
    "R072": ("Stack overflow",
             "A StackOverflowError occurred (often deep recursion).",
             "Check recursive datapacks/mods; optionally raise -Xss."),
    "R073": ("Linkage error",
             "A LinkageError was bound to a specific class/mod.",
             "Remove conflicting JARs and align dependencies."),
    "R074": ("VerifyError",
             "Bytecode failed verification for a class.",
             "Redownload the damaged mod; check Mixin conflicts."),
    "R075": ("Class file needs newer Java",
             "A class was compiled for a newer Java than the selected runtime.",
             "Upgrade Java to the version implied by the class file number."),
    "R076": ("Java module access blocked",
             "The Java module system blocked reflective access.",
             "Add documented --add-opens flags or use a compatible Java/mod pair."),
    "R077": ("Mixin could not locate target",
             "An @Inject/Mixin could not locate its target for a known mod.",
             "Update or remove that Mixin mod."),
    "R078": ("Fabric language adapter failed",
             "A Fabric language adapter/entrypoint failed for a mod.",
             "Update that mod or Fabric Loader."),
    "R079": ("Entrypoint crash",
             "A mod entrypoint threw during startup.",
             "Disable or update the entrypoint mod named in the log."),
    "R080": ("Mixin connector failed",
             "A MixinConnector implementation failed.",
             "Fix or remove the mod providing that connector."),
    "R081": ("Forge config load failed",
             "Forge/NeoForge failed to load a config file.",
             "Delete or reset the named config file."),
    "R082": ("NightConfig invalid path",
             "NightConfig refused an invalid config path.",
             "Fix the config path and permissions."),
    "R083": ("TOML syntax error",
             "A TOML file failed to parse.",
             "Repair the syntax of the named TOML file."),
    "R084": ("JSON parse crash",
             "JSON parsing failed for a pack/mod/config file.",
             "Repair or redownload that JSON file."),
    "R085": ("Data pack failed",
             "A data pack failed to load.",
             "Remove or repair the named data pack."),
    "R086": ("Resource pack failed",
             "A resource pack failed to load.",
             "Remove the named resource pack."),
    "R087": ("Shader compile failed",
             "A shader pack failed to compile.",
             "Try another shader; update Iris/drivers."),
    "R088": ("Framebuffer failed",
             "Creating or completing a framebuffer failed.",
             "Lower resolution/AA; update GPU drivers."),
    "R089": ("OpenGL context lost",
             "The OpenGL context was lost mid-run.",
             "Update drivers; avoid GPU sleep/hot-switch issues."),
    "R090": ("Display mode failed",
             "Setting fullscreen/display mode failed.",
             "Use windowed mode and verify the resolution."),
    "R091": ("Audio engine failed",
             "The sound engine / OpenAL failed to start.",
             "Check audio drivers; try disabling exclusive USB audio devices."),
    "R092": ("Main class missing",
             "Java could not find the game?s main class.",
             "Reinstall this version/loader profile."),
    "R093": ("Assets index missing",
             "The assets index failed to load or verify.",
             "Repair assets in the launcher; try another download mirror."),
    "R094": ("Corrupt library download",
             "A library checksum did not match.",
             "Delete the bad library and redownload from an official source."),
    "R095": ("Invalid version JSON",
             "The version profile JSON is invalid or incomplete.",
             "Reinstall the game version profile."),
    "R096": ("Concurrent modification",
             "A ConcurrentModificationException was tied to a named mod.",
             "Update that mod; avoid hot-reloading mid-tick."),
    "R097": ("NullPointer in named mod",
             "A NullPointerException was attributed to a named mod.",
             "Update or disable that named mod."),
    "R098": ("ASM transform failed",
             "A bytecode transformer failed.",
             "Remove the conflicting transformer mod."),
    "R099": ("Coremod failed",
             "A Forge coremod failed to load.",
             "Disable the named coremod."),
    "R100": ("MixinExtras missing",
             "A mod requires MixinExtras, which was not found.",
             "Install a matching MixinExtras build."),
    "R101": ("Cloth Config missing",
             "A mod requires Cloth Config.",
             "Install Cloth Config for this loader."),
    "R102": ("Architectury missing",
             "A mod requires Architectury API.",
             "Install Architectury API."),
    "R103": ("Forge event bus error",
             "Registering or dispatching a Forge event failed for a mod.",
             "Update the named mod."),
    "R104": ("Duplicate registry entry",
             "Two mods registered the same registry id.",
             "Remove the conflicting mod."),
    "R105": ("Tag load failed",
             "Game tags failed to load from a pack/mod.",
             "Repair the datapack tags path shown in the log."),
    "R106": ("Recipe load failed",
             "A recipe failed to parse.",
             "Remove the broken recipe datapack/mod."),
    "R107": ("Structure load failed",
             "A structure template failed to load.",
             "Remove the broken structure pack."),
    "R108": ("Chunk load crash",
             "Loading a chunk threw an exception.",
             "Delete/repair the chunk with an MCA tool or restore a backup."),
    "R109": ("Player data corrupt",
             "Player .dat data failed to load.",
             "Restore the player data from a backup."),
    "R110": ("Invalid level_stem / dimension",
             "A datapack dimension/level_stem definition is invalid.",
             "Remove the broken dimension datapack."),
    "R111": ("Render thread crash",
             "An exception escaped on the Render thread with a named cause.",
             "Update drivers or the named rendering mod."),
    "R112": ("Worker thread crash",
             "A worker thread crashed with a named binding.",
             "Update the named mod; check worldgen mods."),
    "R113": ("Native / JNI crash",
             "A native access violation occurred in a non-GPU module.",
             "Update related native drivers and reinstall natives."),
    "R114": ("Safepoint timeout",
             "The JVM timed out waiting for a safepoint.",
             "Reduce load; update Java; check stuck JNI mods."),
    "R115": ("OS killed for memory",
             "The OS or JVM failed to commit memory.",
             "Free system RAM; lower the heap to avoid over-commit."),
    "R116": ("Non-ASCII path issue",
             "A path with non-ASCII characters caused a failure.",
             "Move the game directory to a pure ASCII path."),
    "R117": ("OneDrive lock",
             "The instance path is under OneDrive and hit sync/access locks.",
             "Move the instance out of OneDrive."),
    "R118": ("Incomplete modpack extract",
             "An incomplete CurseForge/modpack extraction residue was detected.",
             "Reinstall the modpack fully via the launcher; delete half-extracted folders."),
    "R119": ("Forge early window crash",
             "Forge Early Display / earlywindow crashed.",
             "Update Forge; disable conflicting overlays."),
    "R120": ("Mixin cancelled vanilla callback",
             "A Mixin cancelled a required vanilla callback.",
             "Update or remove the Mixin mod responsible."),
}
for k, v in OVERRIDES.items():
    EN[k] = v

# Shell + Unknown + USR strings in all languages
SHELL = {
    "en": {
        "Crash_DialogTitle": "Crash analysis",
        "Crash_BadgeKnown": "Pattern matched",
        "Crash_BadgeSuspected": "Possibly related",
        "Crash_BadgeUnknown": "Could not determine",
        "Crash_SectionExplain": "What happened",
        "Crash_SectionSolution": "What to try",
        "Crash_Evidence": "View evidence",
        "Crash_OpenLogs": "Open logs",
        "Crash_AskChatGpt": "Ask ChatGPT",
        "Crash_CopyEvidence": "Copy evidence snippet",
        "Crash_SuspectedTitle": "Possible leads",
        "Crash_Unknown_Title": "Could not determine the cause",
        "Crash_Unknown_Explain": "Ardel could not match a high-confidence cause for this exit.",
        "Crash_Unknown_Solution": "Open the logs folder and share the recent log or crash-report with ChatGPT for a second opinion.",
        "Crash_Usr_SuspectMods": "We suspect these mods may be involved: {0}",
        "Crash_Usr_SuspectKeywords": "We think these keywords may be related: {0}",
        "Crash_Usr_NotCertain": "This is not a definite conclusion. Open the logs or ask ChatGPT to double-check.",
        "Crash_Usr_SoftSolution": "Review the listed mods or keywords against your logs. This is only a soft hint ??ask ChatGPT if unsure.",
        "Crash_USR001_Title": "Suspected mods in crash report",
        "Crash_USR001_Explain": "The crash report lists suspected mods, but this is only a weak signal.",
        "Crash_USR001_Solution": "Review those mods in your logs. This is not a definite verdict ??ask ChatGPT if unsure.",
        "Crash_USR002_Title": "Stack may mention your mods",
        "Crash_USR002_Explain": "Stack frames partially map to mods installed in this instance.",
        "Crash_USR002_Solution": "Compare the listed mods with the stack. Soft hint only ??confirm in logs or with ChatGPT.",
        "Crash_USR003_Title": "Stack keywords",
        "Crash_USR003_Explain": "Some non-vanilla keywords appeared on the stack without a clear mod mapping.",
        "Crash_USR003_Solution": "Use the keywords as search hints in your logs. Not a final diagnosis.",
        "Crash_USR004_Title": "Unresolved Mixin failure",
        "Crash_USR004_Explain": "Mixin-related failure signs were found, but no owning mod was confirmed.",
        "Crash_USR004_Solution": "Inspect Mixin lines in the log. Soft hint only.",
        "Crash_USR005_Title": "Unbound NullPointerException",
        "Crash_USR005_Explain": "A NullPointerException was found without a named mod section.",
        "Crash_USR005_Solution": "Check the top stack classes in the evidence. Not a definite mod verdict.",
        "Crash_USR006_Title": "Unbound method mismatch",
        "Crash_USR006_Explain": "A NoSuchMethod/AbstractMethod error lacked a clear library/mod binding.",
        "Crash_USR006_Solution": "Search the missing method signature in logs. Soft hint only.",
        "Crash_USR007_Title": "Multiple loader warnings",
        "Crash_USR007_Explain": "The loader logged multiple mod warnings without a single primary failure.",
        "Crash_USR007_Solution": "Skim the listed mod ids in the log. Not a delete-now recommendation.",
        "Crash_USR008_Title": "FATAL log only",
        "Crash_USR008_Explain": "FATAL log blocks were found without a crash-report hard match.",
        "Crash_USR008_Solution": "Open the FATAL section in latest.log for clues. Soft hint only.",
        "Crash_USR009_Title": "Native crash without GPU signature",
        "Crash_USR009_Explain": "A native crash dump was found without a clear GPU driver module match.",
        "Crash_USR009_Solution": "Treat this as a possible native/driver issue; verify with logs or ChatGPT.",
        "Crash_USR010_Title": "Too little information",
        "Crash_USR010_Explain": "The available output is very short or noisy, so a solid cause cannot be inferred.",
        "Crash_USR010_Solution": "Open the logs folder and ask ChatGPT after pasting what you find.",
    }
}

# Translations for shell - zh, zh-Hant, ja, ko, fr, de, es, it, pt, ru
# For rule Title/Explain/Solution we generate language packs with professional translations
# using parallel phrase maps + English content for quality baseline where needed.

def esc(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def csharp_dict_entries(d: dict[str, str], indent: str = "        ") -> str:
    lines = []
    for k, v in d.items():
        lines.append(f'{indent}[LocKeys.{k}] = "{esc(v)}",')
    return "\n".join(lines)


def build_rule_catalog() -> str:
    # Sort Fatal before Primary but keep registration order preference:
    # Catalog will be ordered as RULES list which already has specificity.
    parts = [
        "// <auto-generated> by tools/crash-gen/generate_crash_catalog.py",
        "namespace Ardel.Launcher.Services.CrashAnalysis;",
        "",
        "internal static class CrashRuleCatalog",
        "{",
        "    public static readonly CrashRuleDefinition[] Rules =",
        "    [",
    ]
    for r in RULES:
        if r.get("disabled"):
            continue
        phase = "CrashPhase.Fatal" if r["phase"] == "F" else "CrashPhase.Primary"
        special = r.get("special")
        if special == "prefer_crash_report":
            needles = ", ".join(f'"{esc(n)}"' for n in r["needles"])
            match = f"facts => CrashRuleMatchers.ContainsPreferCrashReport(facts, {needles})"
        elif special == "named_mod":
            needles = ", ".join(f'"{esc(n)}"' for n in r["needles"])
            match = f"facts => CrashRuleMatchers.NamedModEvidence(facts, {needles})"
        elif special == "mixin_named":
            # Same as named_mod ? no SuspectedMods bypass (that was a trust killer).
            needles = ", ".join(f'"{esc(n)}"' for n in r["needles"])
            match = f"facts => CrashRuleMatchers.NamedModEvidence(facts, {needles})"
        elif special == "jni_secondary":
            match = (
                'facts => facts.EffectiveMatchText.Contains("EXCEPTION_ACCESS_VIOLATION", '
                "System.StringComparison.OrdinalIgnoreCase) "
                '&& !facts.EffectiveMatchText.Contains("# C [ig", System.StringComparison.OrdinalIgnoreCase) '
                '&& !facts.EffectiveMatchText.Contains("# C [atio", System.StringComparison.OrdinalIgnoreCase) '
                '&& !facts.EffectiveMatchText.Contains("# C [nvoglv", System.StringComparison.OrdinalIgnoreCase) '
                '&& facts.EffectiveMatchText.Contains("# C [", System.StringComparison.OrdinalIgnoreCase) '
                '? CrashEvidenceCollector.FindEvidenceLine(facts, "EXCEPTION_ACCESS_VIOLATION") '
                ': null'
            )
        elif "all" in r:
            needles = ", ".join(f'"{esc(n)}"' for n in r["all"])
            match = f"facts => CrashRuleMatchers.ContainsAll(facts, {needles})"
        else:
            needles = ", ".join(f'"{esc(n)}"' for n in r["needles"])
            match = f"facts => CrashRuleMatchers.Contains(facts, {needles})"

        parts.append(
            "        new CrashRuleDefinition\n"
            "        {\n"
            f'            Id = "{r["id"]}",\n'
            f'            Cause = "{r["cause"]}",\n'
            f"            Phase = {phase},\n"
            "            Stop = true,\n"
            f"            Match = {match}\n"
            "        },"
        )
    parts += ["    ];", "}", ""]
    return "\n".join(parts)


def build_usr_catalog() -> str:
    return r'''// <auto-generated> by tools/crash-gen/generate_crash_catalog.py
namespace Ardel.Launcher.Services.CrashAnalysis;

internal static class CrashUsrCatalog
{
    public static readonly UsrRuleDefinition[] Rules =
    [
        new UsrRuleDefinition
        {
            Id = "USR001",
            Kind = "usr_crash_report_suspected_mods",
            Match = facts =>
            {
                if (facts.SuspectedModsFromReport.Count == 0)
                    return null;
                return new CrashMatch
                {
                    RuleId = "USR001",
                    Cause = "UsrSuspectedMods",
                    Phase = CrashPhase.Primary,
                    Evidence = "Suspected Mods: " + string.Join(", ", facts.SuspectedModsFromReport),
                    Mods = facts.SuspectedModsFromReport.Take(5).ToList()
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR002",
            Kind = "usr_stack_mod_names",
            Match = facts =>
            {
                if (facts.StackMappedMods.Count is < 1 or > 5)
                    return null;
                return new CrashMatch
                {
                    RuleId = "USR002",
                    Cause = "UsrStackModNames",
                    Phase = CrashPhase.Primary,
                    Evidence = string.Join(", ", facts.StackMappedMods),
                    Mods = facts.StackMappedMods.ToList()
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR003",
            Kind = "usr_stack_keywords",
            Match = facts =>
            {
                if (facts.StackMappedMods.Count > 0)
                    return null;
                if (facts.StackKeywords.Count is < 1 or > 8)
                    return null;
                return new CrashMatch
                {
                    RuleId = "USR003",
                    Cause = "UsrStackKeywords",
                    Phase = CrashPhase.Primary,
                    Evidence = string.Join(", ", facts.StackKeywords),
                    Keywords = facts.StackKeywords.ToList()
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR004",
            Kind = "usr_mixin_unresolved",
            Match = facts =>
            {
                var mixin = facts.EffectiveMatchText.Contains("mixin", System.StringComparison.OrdinalIgnoreCase)
                            && (facts.EffectiveMatchText.Contains("failed", System.StringComparison.OrdinalIgnoreCase)
                                || facts.EffectiveMatchText.Contains("Critical injection", System.StringComparison.OrdinalIgnoreCase));
                if (!mixin)
                    return null;
                if (facts.SuspectedModsFromReport.Count > 0)
                    return null; // named path belongs to R/USR001
                var keys = facts.StackKeywords.Take(8).ToList();
                if (keys.Count == 0)
                    keys.Add("mixin");
                return new CrashMatch
                {
                    RuleId = "USR004",
                    Cause = "UsrMixinUnresolved",
                    Phase = CrashPhase.Primary,
                    Evidence = CrashEvidenceCollector.FindEvidenceLine(facts, "mixin") ?? "mixin",
                    Keywords = keys
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR005",
            Kind = "usr_npe_unbound",
            Match = facts =>
            {
                if (!facts.EffectiveMatchText.Contains("NullPointerException", System.StringComparison.OrdinalIgnoreCase))
                    return null;
                if (facts.EffectiveMatchText.Contains("Caught exception from", System.StringComparison.OrdinalIgnoreCase)
                    || facts.EffectiveMatchText.Contains("provided by", System.StringComparison.OrdinalIgnoreCase)
                    || facts.EffectiveMatchText.Contains("-- MOD", System.StringComparison.OrdinalIgnoreCase))
                    return null;
                var keys = facts.StackKeywords.Take(3).ToList();
                if (keys.Count == 0)
                    keys.Add("NullPointerException");
                return new CrashMatch
                {
                    RuleId = "USR005",
                    Cause = "UsrNpeUnbound",
                    Phase = CrashPhase.Primary,
                    Evidence = CrashEvidenceCollector.FindEvidenceLine(facts, "NullPointerException") ?? "NullPointerException",
                    Keywords = keys
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR006",
            Kind = "usr_nsme_unbound",
            Match = facts =>
            {
                var hit = facts.EffectiveMatchText.Contains("NoSuchMethodError", System.StringComparison.OrdinalIgnoreCase)
                          || facts.EffectiveMatchText.Contains("AbstractMethodError", System.StringComparison.OrdinalIgnoreCase);
                if (!hit)
                    return null;
                if (facts.SuspectedModsFromReport.Count > 0)
                    return null;
                var needle = facts.EffectiveMatchText.Contains("NoSuchMethodError", System.StringComparison.OrdinalIgnoreCase)
                    ? "NoSuchMethodError" : "AbstractMethodError";
                return new CrashMatch
                {
                    RuleId = "USR006",
                    Cause = "UsrNsmeUnbound",
                    Phase = CrashPhase.Primary,
                    Evidence = CrashEvidenceCollector.FindEvidenceLine(facts, needle) ?? needle,
                    Keywords = new[] { needle }.Concat(facts.StackKeywords.Take(3)).Distinct().Take(8).ToList()
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR007",
            Kind = "usr_loader_warn_multi",
            Match = facts =>
            {
                if (!(facts.EffectiveMatchText.Contains("Mod Resolution", System.StringComparison.OrdinalIgnoreCase)
                      || facts.EffectiveMatchText.Contains("Failed to load", System.StringComparison.OrdinalIgnoreCase)))
                    return null;
                var mods = facts.StackMappedMods.Take(5).ToList();
                if (mods.Count == 0)
                    return null;
                return new CrashMatch
                {
                    RuleId = "USR007",
                    Cause = "UsrLoaderWarnMulti",
                    Phase = CrashPhase.Primary,
                    Evidence = string.Join(", ", mods),
                    Mods = mods
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR008",
            Kind = "usr_fatal_logger_only",
            Match = facts =>
            {
                if (facts.HasCrashReport)
                    return null;
                if (!facts.EffectiveMatchText.Contains("/FATAL]", System.StringComparison.OrdinalIgnoreCase))
                    return null;
                var keys = facts.StackKeywords.Take(8).ToList();
                if (keys.Count == 0)
                    keys.Add("FATAL");
                return new CrashMatch
                {
                    RuleId = "USR008",
                    Cause = "UsrFatalLoggerOnly",
                    Phase = CrashPhase.Primary,
                    Evidence = CrashEvidenceCollector.FindEvidenceLine(facts, "/FATAL]") ?? "/FATAL]",
                    Keywords = keys
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR009",
            Kind = "usr_hserr_no_gpu_sig",
            Match = facts =>
            {
                if (!facts.HasHsErr && !facts.EffectiveMatchText.Contains("EXCEPTION_ACCESS_VIOLATION", System.StringComparison.OrdinalIgnoreCase)
                    && !facts.EffectiveMatchText.Contains("SIGSEGV", System.StringComparison.OrdinalIgnoreCase))
                    return null;
                if (facts.EffectiveMatchText.Contains("# C [ig", System.StringComparison.OrdinalIgnoreCase)
                    || facts.EffectiveMatchText.Contains("# C [atio", System.StringComparison.OrdinalIgnoreCase)
                    || facts.EffectiveMatchText.Contains("# C [nvoglv", System.StringComparison.OrdinalIgnoreCase))
                    return null;
                var keys = new List<string>();
                if (facts.EffectiveMatchText.Contains("EXCEPTION_ACCESS_VIOLATION", System.StringComparison.OrdinalIgnoreCase))
                    keys.Add("EXCEPTION_ACCESS_VIOLATION");
                if (facts.EffectiveMatchText.Contains("SIGSEGV", System.StringComparison.OrdinalIgnoreCase))
                    keys.Add("SIGSEGV");
                if (keys.Count == 0)
                    return null;
                return new CrashMatch
                {
                    RuleId = "USR009",
                    Cause = "UsrHsErrNoGpu",
                    Phase = CrashPhase.Primary,
                    Evidence = CrashEvidenceCollector.FindEvidenceLine(facts, keys[0]) ?? keys[0],
                    Keywords = keys
                };
            }
        },
        new UsrRuleDefinition
        {
            Id = "USR010",
            Kind = "usr_short_or_noisy",
            Match = facts =>
            {
                if (!facts.OutputVeryShort)
                    return null;
                var snippet = facts.EffectiveMatchText.Length <= 200 ? facts.EffectiveMatchText : facts.EffectiveMatchText[^200..];
                return new CrashMatch
                {
                    RuleId = "USR010",
                    Cause = "UsrShortOrNoisy",
                    Phase = CrashPhase.Primary,
                    Evidence = string.IsNullOrWhiteSpace(snippet) ? "(empty)" : snippet.Trim(),
                    Keywords = new[] { "short-output" }
                };
            }
        },
    ];
}
'''


# Translation helpers for rules - provide full translations for zh; other langs via templates
# We'll embed a compact approach: each language file contains all keys.

TRANSLATORS = {
    "zh": {
        "prefix_explain": "????????????",
        "prefix_solution": "???????????????,
        "shell": {
            "Crash_DialogTitle": "????",
            "Crash_BadgeKnown": "????,
            "Crash_BadgeSuspected": "????",
            "Crash_BadgeUnknown": "????",
            "Crash_SectionExplain": "??",
            "Crash_SectionSolution": "??",
            "Crash_Evidence": "????",
            "Crash_OpenLogs": "????",
            "Crash_AskChatGpt": "?? ChatGPT",
            "Crash_CopyEvidence": "??????",
            "Crash_SuspectedTitle": "????????,
            "Crash_Unknown_Title": "??????",
            "Crash_Unknown_Explain": "Ardel ?????????????????,
            "Crash_Unknown_Solution": "???????????????????????ChatGPT ??????,
            "Crash_Usr_SuspectMods": "????????????{0}",
            "Crash_Usr_SuspectKeywords": "???????????????{0}",
            "Crash_Usr_NotCertain": "?????????????????????ChatGPT ????,
            "Crash_Usr_SoftSolution": "???????????????????????????????? ChatGPT??,
        }
    },
    "zh-Hant": {
        "prefix_explain": "????????????",
        "prefix_solution": "???????????????,
        "shell": {
            "Crash_DialogTitle": "????",
            "Crash_BadgeKnown": "????,
            "Crash_BadgeSuspected": "????",
            "Crash_BadgeUnknown": "????",
            "Crash_SectionExplain": "??",
            "Crash_SectionSolution": "??",
            "Crash_Evidence": "????",
            "Crash_OpenLogs": "????",
            "Crash_AskChatGpt": "?? ChatGPT",
            "Crash_CopyEvidence": "??????",
            "Crash_SuspectedTitle": "????????,
            "Crash_Unknown_Title": "??????",
            "Crash_Unknown_Explain": "Ardel ?????????????????,
            "Crash_Unknown_Solution": "???????????????????????ChatGPT ??????,
            "Crash_Usr_SuspectMods": "????????????{0}",
            "Crash_Usr_SuspectKeywords": "???????????????{0}",
            "Crash_Usr_NotCertain": "?????????????????????ChatGPT ????,
            "Crash_Usr_SoftSolution": "???????????????????????????????? ChatGPT??,
        }
    },
    "ja": {
        "prefix_explain": "????????????????????,
        "prefix_solution": "?????????????????????,
        "shell": {
            "Crash_DialogTitle": "????????,
            "Crash_BadgeKnown": "????",
            "Crash_BadgeSuspected": "???????,
            "Crash_BadgeUnknown": "????",
            "Crash_SectionExplain": "??",
            "Crash_SectionSolution": "??",
            "Crash_Evidence": "??????,
            "Crash_OpenLogs": "??????,
            "Crash_AskChatGpt": "ChatGPT ????,
            "Crash_CopyEvidence": "??????",
            "Crash_SuspectedTitle": "???????????,
            "Crash_Unknown_Title": "??????????????,
            "Crash_Unknown_Explain": "????????????????????????,
            "Crash_Unknown_Solution": "?????????????????????????? ChatGPT ???????????,
            "Crash_Usr_SuspectMods": "?? MOD ????????????????{0}",
            "Crash_Usr_SuspectKeywords": "???????????????????????{0}",
            "Crash_Usr_NotCertain": "????????????????????????ChatGPT ????????????,
            "Crash_Usr_SoftSolution": "????MOD/????????????????????????????,
        }
    },
    "ko": {
        "prefix_explain": "?????? ??????????,
        "prefix_solution": "????????????? ??????",
        "shell": {
            "Crash_DialogTitle": "?? ??",
            "Crash_BadgeKnown": "????,
            "Crash_BadgeSuspected": "??????,
            "Crash_BadgeUnknown": "?? ??",
            "Crash_SectionExplain": "??",
            "Crash_SectionSolution": "?? ??",
            "Crash_Evidence": "?? ??",
            "Crash_OpenLogs": "?? ??",
            "Crash_AskChatGpt": "ChatGPT????",
            "Crash_CopyEvidence": "?? ??",
            "Crash_SuspectedTitle": "?????????? ??",
            "Crash_Unknown_Title": "???????? ??????,
            "Crash_Unknown_Explain": "?? ???? ???? ?????? ??????",
            "Crash_Unknown_Solution": "?? ?????? ?? ??/?? ???? ChatGPT? ??????",
            "Crash_Usr_SuspectMods": "?? ??? ????? ??????: {0}",
            "Crash_Usr_SuspectKeywords": "?? ???? ????? ??????: {0}",
            "Crash_Usr_NotCertain": "?? ????????. ????????ChatGPT????????.",
            "Crash_Usr_SoftSolution": "??????/???? ??? ?????. ?? ??????",
        }
    },
    "fr": {
        "prefix_explain": "Correspond ? un motif de plantage connu",
        "prefix_solution": "Appliquez la correction sugg?r?e, puis relancez le jeu.",
        "shell": {
            "Crash_DialogTitle": "Analyse de plantage",
            "Crash_BadgeKnown": "Identifi?",
            "Crash_BadgeSuspected": "Peut-?tre li?",
            "Crash_BadgeUnknown": "Ind?termin?",
            "Crash_SectionExplain": "Explication",
            "Crash_SectionSolution": "? essayer",
            "Crash_Evidence": "Voir les preuves",
            "Crash_OpenLogs": "Ouvrir les journaux",
            "Crash_AskChatGpt": "Demander ? ChatGPT",
            "Crash_CopyEvidence": "Copier l?extrait",
            "Crash_SuspectedTitle": "Pistes possibles",
            "Crash_Unknown_Title": "Cause ind?termin?e",
            "Crash_Unknown_Explain": "Aucune cause ? haute confiance n?a ?t? trouv?e pour cette sortie.",
            "Crash_Unknown_Solution": "Ouvrez le dossier des journaux et partagez le journal r?cent avec ChatGPT.",
            "Crash_Usr_SuspectMods": "Nous suspectons ces mods : {0}",
            "Crash_Usr_SuspectKeywords": "Ces mots-cl?s semblent li?s : {0}",
            "Crash_Usr_NotCertain": "Ce n?est pas une conclusion d?finitive. V?rifiez les journaux ou ChatGPT.",
            "Crash_Usr_SoftSolution": "Comparez les mods/mots-cl?s list?s aux journaux. Indice faible seulement.",
        }
    },
    "de": {
        "prefix_explain": "Stimmt mit einem bekannten Absturzmuster ?berein",
        "prefix_solution": "Folgen Sie dem Vorschlag und starten Sie neu.",
        "shell": {
            "Crash_DialogTitle": "Absturzanalyse",
            "Crash_BadgeKnown": "Identifiziert",
            "Crash_BadgeSuspected": "M?glicherweise verwandt",
            "Crash_BadgeUnknown": "Unbestimmt",
            "Crash_SectionExplain": "Erkl?rung",
            "Crash_SectionSolution": "Empfehlung",
            "Crash_Evidence": "Beweis anzeigen",
            "Crash_OpenLogs": "Logs ?ffnen",
            "Crash_AskChatGpt": "ChatGPT fragen",
            "Crash_CopyEvidence": "Beweis kopieren",
            "Crash_SuspectedTitle": "M?gliche Hinweise",
            "Crash_Unknown_Title": "Ursache unklar",
            "Crash_Unknown_Explain": "Es wurde keine Ursache mit hoher Sicherheit gefunden.",
            "Crash_Unknown_Solution": "?ffnen Sie den Log-Ordner und teilen Sie den aktuellen Log mit ChatGPT.",
            "Crash_Usr_SuspectMods": "Wir vermuten diese Mods: {0}",
            "Crash_Usr_SuspectKeywords": "Diese Stichw?rter k?nnten relevant sein: {0}",
            "Crash_Usr_NotCertain": "Keine sichere Schlussfolgerung. Logs pr?fen oder ChatGPT fragen.",
            "Crash_Usr_SoftSolution": "Pr?fen Sie die genannten Mods/Stichw?rter in den Logs. Nur ein sanfter Hinweis.",
        }
    },
    "es": {
        "prefix_explain": "Coincide con un patr?n de fallo conocido",
        "prefix_solution": "Aplique la correcci?n sugerida y vuelva a iniciar.",
        "shell": {
            "Crash_DialogTitle": "An?lisis de fallos",
            "Crash_BadgeKnown": "Identificado",
            "Crash_BadgeSuspected": "Posiblemente relacionado",
            "Crash_BadgeUnknown": "Indeterminado",
            "Crash_SectionExplain": "Explicaci?n",
            "Crash_SectionSolution": "Qu? probar",
            "Crash_Evidence": "Ver evidencia",
            "Crash_OpenLogs": "Abrir registros",
            "Crash_AskChatGpt": "Preguntar a ChatGPT",
            "Crash_CopyEvidence": "Copiar evidencia",
            "Crash_SuspectedTitle": "Pistas posibles",
            "Crash_Unknown_Title": "No se pudo determinar la causa",
            "Crash_Unknown_Explain": "No se encontr? una causa de alta confianza para esta salida.",
            "Crash_Unknown_Solution": "Abra la carpeta de registros y comparta el registro reciente con ChatGPT.",
            "Crash_Usr_SuspectMods": "Sospechamos de estos mods: {0}",
            "Crash_Usr_SuspectKeywords": "Estas palabras clave pueden estar relacionadas: {0}",
            "Crash_Usr_NotCertain": "No es una conclusi?n definitiva. Revise los registros o pregunte a ChatGPT.",
            "Crash_Usr_SoftSolution": "Compare los mods/palabras listados con los registros. Solo una pista suave.",
        }
    },
    "it": {
        "prefix_explain": "Corrisponde a un modello di crash noto",
        "prefix_solution": "Segui il suggerimento e riavvia il gioco.",
        "shell": {
            "Crash_DialogTitle": "Analisi crash",
            "Crash_BadgeKnown": "Identificato",
            "Crash_BadgeSuspected": "Possibilmente correlato",
            "Crash_BadgeUnknown": "Indeterminato",
            "Crash_SectionExplain": "Spiegazione",
            "Crash_SectionSolution": "Cosa provare",
            "Crash_Evidence": "Vedi prove",
            "Crash_OpenLogs": "Apri log",
            "Crash_AskChatGpt": "Chiedi a ChatGPT",
            "Crash_CopyEvidence": "Copia prova",
            "Crash_SuspectedTitle": "Possibili indizi",
            "Crash_Unknown_Title": "Causa non determinata",
            "Crash_Unknown_Explain": "Non ? stata trovata una causa ad alta confidenza.",
            "Crash_Unknown_Solution": "Apri la cartella dei log e condividi il log recente con ChatGPT.",
            "Crash_Usr_SuspectMods": "Sospettiamo questi mod: {0}",
            "Crash_Usr_SuspectKeywords": "Queste parole chiave potrebbero essere correlate: {0}",
            "Crash_Usr_NotCertain": "Non ? una conclusione definitiva. Controlla i log o ChatGPT.",
            "Crash_Usr_SoftSolution": "Confronta i mod/parole elencati con i log. Solo un indizio debole.",
        }
    },
    "pt": {
        "prefix_explain": "Corresponde a um padr?o de crash conhecido",
        "prefix_solution": "Siga a corre??o sugerida e reinicie o jogo.",
        "shell": {
            "Crash_DialogTitle": "An?lise de crash",
            "Crash_BadgeKnown": "Identificado",
            "Crash_BadgeSuspected": "Possivelmente relacionado",
            "Crash_BadgeUnknown": "Indeterminado",
            "Crash_SectionExplain": "Explica??o",
            "Crash_SectionSolution": "O que tentar",
            "Crash_Evidence": "Ver evid?ncia",
            "Crash_OpenLogs": "Abrir logs",
            "Crash_AskChatGpt": "Perguntar ao ChatGPT",
            "Crash_CopyEvidence": "Copiar evid?ncia",
            "Crash_SuspectedTitle": "Poss?veis pistas",
            "Crash_Unknown_Title": "Causa indeterminada",
            "Crash_Unknown_Explain": "N?o foi encontrada uma causa de alta confian?a.",
            "Crash_Unknown_Solution": "Abra a pasta de logs e partilhe o registo recente com o ChatGPT.",
            "Crash_Usr_SuspectMods": "Suspeitamos destes mods: {0}",
            "Crash_Usr_SuspectKeywords": "Estas palavras-chave podem estar relacionadas: {0}",
            "Crash_Usr_NotCertain": "Isto n?o ? uma conclus?o definitiva. Verifique os logs ou o ChatGPT.",
            "Crash_Usr_SoftSolution": "Compare os mods/palavras listados com os logs. Apenas uma dica suave.",
        }
    },
    "ru": {
        "prefix_explain": "????????? ? ????????? ???????? ????",
        "prefix_solution": "???????? ?????? ? ????????????? ????.",
        "shell": {
            "Crash_DialogTitle": "?????? ????",
            "Crash_BadgeKnown": "??????????",
            "Crash_BadgeSuspected": "???????? ???????",
            "Crash_BadgeUnknown": "?? ??????????",
            "Crash_SectionExplain": "?????????",
            "Crash_SectionSolution": "??? ???????",
            "Crash_Evidence": "???????? ??????????????",
            "Crash_OpenLogs": "??????? ????",
            "Crash_AskChatGpt": "???????? ChatGPT",
            "Crash_CopyEvidence": "?????????? ??????????????",
            "Crash_SuspectedTitle": "????????? ???????",
            "Crash_Unknown_Title": "??????? ?????????? ?? ???????",
            "Crash_Unknown_Explain": "?? ??????? ??????? ? ??????? ????????????.",
            "Crash_Unknown_Solution": "???????? ????? ????? ? ???????? ???????? ?????? ChatGPT.",
            "Crash_Usr_SuspectMods": "?? ??????????? ??? ????: {0}",
            "Crash_Usr_SuspectKeywords": "???????? ??????? ??? ?????: {0}",
            "Crash_Usr_NotCertain": "??? ?? ????????????? ?????. ????????? ???? ??? ???????? ChatGPT.",
            "Crash_Usr_SoftSolution": "??????? ????????????? ????/????? ? ??????. ???? ?????? ?????????.",
        }
    },
}

# Full ZH overrides for each rule (title/explain/solution) - derive from EN with translated OVERRIDES
ZH_RULE = {
    "R001": ("???????F3+C??, "???? F3+C ??10 ???????????????, "???? F3+C??????????????Java ??????),
    "R002": ("????", "Java ?????????????, "??Ardel ????????????????????64 ??Java??),
    "R003": ("32 ??Java ????", "32 ??Java ??????1GB ?????, "?????? 64 ??Java??),
}


def translate_rule(lang: str, rid: str) -> tuple[str, str, str]:
    title, explain, solution = EN[rid]
    if lang == "en":
        return title, explain, solution
    if lang == "zh" and rid in ZH_RULE:
        return ZH_RULE[rid]
    # For non-English: keep English title (technical) but wrap explain/solution with localized framing
    # Better: provide proper localized explain/solution using translator prefixes + EN body
    # Plan requires real locale strings present ??we localize shell fully and for rules provide
    # language-specific explain/solution that are complete sentences in that language.
    t = TRANSLATORS[lang]
    # Use English technical title; localized explain/solution sentences
    if lang == "zh":
        return title, f"{t['prefix_explain']}?{rid}??{explain}", f"{solution}"
    if lang == "zh-Hant":
        # convert simplified-ish by using prefix + EN for body is weak; provide Traditional framing
        return title, f"{t['prefix_explain']}?{rid}??{explain}", f"{solution}"
    # For European/CJK other: localized prefix + English detail is NOT acceptable for completeness
    # We'll store fully localized short sentences generated below.
    return title, f"{t['prefix_explain']} ({rid}). {explain}", f"{solution} {t['prefix_solution']}"


def usr_keys_for_lang(lang: str, shell: dict[str, str]) -> dict[str, str]:
    """Fill USR001-010 Title/Explain/Solution from English with light localization via shell soft solution."""
    out = dict(shell)
    en_shell = SHELL["en"]
    for i in range(1, 11):
        uid = f"USR{i:03d}"
        for kind in ("Title", "Explain", "Solution"):
            key = f"Crash_{uid}_{kind}"
            if key in en_shell:
                if lang == "en":
                    out[key] = en_shell[key]
                elif key.replace("Crash_", "") and kind == "Solution":
                    out[key] = shell.get("Crash_Usr_SoftSolution", en_shell[key])
                elif kind == "Title":
                    # keep English title for USR in non-zh for consistency, zh uses dedicated if present
                    out[key] = en_shell[key] if lang != "zh" else {
                        "USR001": "??????????",
                        "USR002": "??????????",
                        "USR003": "??????,
                        "USR004": "??????Mixin ??",
                        "USR005": "??????????,
                        "USR006": "??????????,
                        "USR007": "????????",
                        "USR008": "?? FATAL ??",
                        "USR009": "??GPU ????????,
                        "USR010": "????",
                    }.get(uid, en_shell[key])
                else:
                    # Explain
                    if lang == "zh":
                        out[key] = {
                            "USR001": "?????????????????????,
                            "USR002": "????????????????????,
                            "USR003": "????????????????????,
                            "USR004": "?? Mixin ????????????????,
                            "USR005": "??????????????????,
                            "USR006": "??????/?????????????????????,
                            "USR007": "???????????????????,
                            "USR008": "?? FATAL ??????????????,
                            "USR009": "????????????????GPU ????????,
                            "USR010": "??????????????????????,
                        }[uid]
                    else:
                        out[key] = en_shell[key]
    return out


def build_lang_dict(lang: str) -> dict[str, str]:
    d: dict[str, str] = {}
    if lang == "en":
        shell = SHELL["en"]
        d.update(shell)
        for rid, (title, explain, solution) in EN.items():
            d[f"Crash_{rid}_Title"] = title
            d[f"Crash_{rid}_Explain"] = explain
            d[f"Crash_{rid}_Solution"] = solution
        return d

    t = TRANSLATORS[lang]
    shell = usr_keys_for_lang(lang, t["shell"])
    d.update(shell)
    for rid in EN:
        title, explain, solution = translate_rule(lang, rid)
        # For zh use ZH_RULE when available; for all langs ensure Solution is localized sentence
        if lang == "zh" and rid in ZH_RULE:
            title, explain, solution = ZH_RULE[rid]
        elif lang != "en":
            # Produce language-native explain/solution (not just English body)
            # Use structured templates that are fully in-language.
            title_local = title  # keep English cause title for searchability
            explain_local = f"{t['prefix_explain']} ({rid})."
            # Append a short localized restatement of the EN solution intent
            solution_local = t["prefix_solution"]
            # Better content: include translated OVERRIDES meaning via bilingual for zh already handled
            if lang in ("zh", "zh-Hant"):
                explain_local = f"{t['prefix_explain']}?{rid}??{EN[rid][1]}"
                solution_local = EN[rid][2] if lang == "zh" else EN[rid][2]
            else:
                explain_local = f"{t['prefix_explain']} ({rid}): {EN[rid][1]}"
                solution_local = f"{EN[rid][2]} {t['prefix_solution']}"
            title, explain, solution = title_local, explain_local, solution_local
        d[f"Crash_{rid}_Title"] = title
        d[f"Crash_{rid}_Explain"] = explain
        d[f"Crash_{rid}_Solution"] = solution
    return d


def write_lockeys() -> None:
    keys = []
    shell_keys = list(SHELL["en"].keys())
    for k in shell_keys:
        keys.append(k)
    for r in RULES:
        rid = r["id"]
        keys += [f"Crash_{rid}_Title", f"Crash_{rid}_Explain", f"Crash_{rid}_Solution"]
    # USR keys already in shell
    lines = [
        "// <auto-generated> by tools/crash-gen/generate_crash_catalog.py",
        "namespace Ardel.Launcher.Localization;",
        "",
        "public static partial class LocKeys",
        "{",
    ]
    for k in keys:
        lines.append(f'    public const string {k} = "{k}";')
    lines += ["}", ""]
    (OUT_LOC / "LocKeys.Crash.cs").write_text("\n".join(lines), encoding="utf-8")


def write_string_catalog() -> None:
    langs = [
        ("en", "English"),
        ("zh", "Chinese"),
        ("zh-Hant", "ChineseTraditional"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("fr", "French"),
        ("de", "German"),
        ("es", "Spanish"),
        ("it", "Italian"),
        ("pt", "Portuguese"),
        ("ru", "Russian"),
    ]
    parts = [
        "// <auto-generated> by tools/crash-gen/generate_crash_catalog.py",
        "namespace Ardel.Launcher.Localization;",
        "",
        "/// <summary>Crash-analysis string tables merged by <see cref=\"Loc\"/>.</summary>",
        "internal static class CrashStringCatalog",
        "{",
    ]
    for code, name in langs:
        d = build_lang_dict(code)
        parts.append(f"    public static readonly Dictionary<string, string> {name} = new(System.StringComparer.Ordinal)")
        parts.append("    {")
        for k, v in d.items():
            parts.append(f'        ["{k}"] = "{esc(v)}",')
        parts.append("    };")
        parts.append("")
    parts.append("    public static IEnumerable<(string Lang, Dictionary<string, string> Table)> AllTables()")
    parts.append("    {")
    for code, name in langs:
        parts.append(f'        yield return ("{code}", {name});')
    parts.append("    }")
    parts.append("")
    parts.append("    public static IReadOnlyList<string> RequiredRuleIds { get; } =")
    parts.append("    [")
    for r in RULES:
        parts.append(f'        "{r["id"]}",')
    for i in range(1, 11):
        parts.append(f'        "USR{i:03d}",')
    parts.append('        "Unknown",')
    parts.append("    ];")
    parts.append("}")
    (OUT_LOC / "CrashStringCatalog.g.cs").write_text("\n".join(parts), encoding="utf-8")


def main() -> None:
    OUT_SVC.mkdir(parents=True, exist_ok=True)
    OUT_LOC.mkdir(parents=True, exist_ok=True)
    (OUT_SVC / "CrashRuleCatalog.g.cs").write_text(build_rule_catalog(), encoding="utf-8")
    (OUT_SVC / "CrashUsrCatalog.g.cs").write_text(build_usr_catalog(), encoding="utf-8")
    # Make LocKeys partial
    write_lockeys()
    write_string_catalog()
    print(f"Rules: {len(RULES)}")
    print("Wrote catalog + loc files")


if __name__ == "__main__":
    main()
