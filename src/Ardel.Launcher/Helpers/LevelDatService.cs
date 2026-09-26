using System.IO.Compression;
using Ardel.Launcher.Models;

namespace Ardel.Launcher.Helpers;

/// <summary>Loads, edits, and saves Minecraft world <c>level.dat</c>.</summary>
public static class LevelDatService
{
    public static LevelDatDocument? TryLoad(string worldFolderPath)
    {
        if (string.IsNullOrWhiteSpace(worldFolderPath) || !Directory.Exists(worldFolderPath))
            return null;

        return TryReadFile(Path.Combine(worldFolderPath, "level.dat"));
    }

    public static bool HasReadableBackup(string worldFolderPath)
    {
        var backup = BackupPath(worldFolderPath);
        return backup is not null && TryReadFile(backup) is not null;
    }

    public static LevelDatDocument? RestoreBackup(string worldFolderPath)
    {
        var backup = BackupPath(worldFolderPath);
        if (backup is null)
            return null;

        var recovered = TryReadFile(backup);
        if (recovered is null)
            return null;

        var target = Path.Combine(worldFolderPath, "level.dat");
        File.Copy(backup, target, overwrite: true);
        return new LevelDatDocument(target, recovered.Root);
    }

    public static void Save(LevelDatDocument document, SaveWorldPropertiesEdit edit)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(edit);

        if (!NbtReader.TryGetCompound(document.Root.Compound, "Data", out var data))
            throw new InvalidDataException("level.dat is missing the Data compound.");

        // Only rewrite tags that already exist. Never create or touch hardcore —
        // that flag is easy to set and hard for the game to recover from.
        SetString(data, "LevelName", edit.LevelName.Trim());
        SetNumeric(data, "GameType", edit.GameType);
        SetNumeric(data, "Difficulty", edit.Difficulty);
        SetBool(data, "allowCommands", edit.AllowCommands);

        var target = document.FilePath;
        var backup = target + "_old";
        var temp = target + ".tmp";

        try
        {
            using (var fileStream = File.Create(temp))
            using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                NbtWriter.WriteRoot(gzip, document.Root);
            }

            if (File.Exists(target))
                File.Copy(target, backup, overwrite: true);

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static string? BackupPath(string worldFolderPath)
    {
        if (string.IsNullOrWhiteSpace(worldFolderPath))
            return null;

        var backup = Path.Combine(worldFolderPath, "level.dat_old");
        return File.Exists(backup) ? backup : null;
    }

    private static LevelDatDocument? TryReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var root = TryReadGzipRoot(path);
            if (root is null || !NbtReader.TryGetCompound(root.Compound, "Data", out _))
                return null;

            return new LevelDatDocument(path, root);
        }
        catch
        {
            return null;
        }
    }

    internal static NbtRoot? TryReadGzipRoot(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var fileStream = File.OpenRead(path);
            using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
            return NbtReader.ReadRoot(gzip);
        }
        catch
        {
            return null;
        }
    }

    private static void SetString(NbtCompoundValue map, string key, string value)
    {
        if (!map.Children.TryGetValue(key, out var existing) || existing is not NbtStringValue)
            return;

        map.Children[key] = new NbtStringValue(value);
    }

    private static void SetNumeric(NbtCompoundValue map, string key, int value)
    {
        if (!map.Children.TryGetValue(key, out var existing))
            return;

        map.Children[key] = existing switch
        {
            NbtByteValue => new NbtByteValue((sbyte)value),
            NbtShortValue => new NbtShortValue((short)value),
            NbtIntValue => new NbtIntValue(value),
            NbtLongValue => new NbtLongValue(value),
            _ => existing
        };
    }

    private static void SetBool(NbtCompoundValue map, string key, bool value) =>
        SetNumeric(map, key, value ? 1 : 0);
}

public sealed class LevelDatDocument
{
    internal LevelDatDocument(string filePath, NbtRoot root)
    {
        FilePath = filePath;
        Root = root;
    }

    public string FilePath { get; }
    internal NbtRoot Root { get; }

    public SaveWorldMetadata ToMetadata()
    {
        if (!NbtReader.TryGetCompound(Root.Compound, "Data", out var data))
            return new SaveWorldMetadata();

        var worldFolder = Path.GetDirectoryName(FilePath);
        long? seed = ReadSeed(data, worldFolder);

        ReadSpawn(data, out var spawnX, out var spawnY, out var spawnZ);

        string? gameVersion = null;
        if (NbtReader.TryGetCompound(data, "Version", out var versionComp))
        {
            if (NbtReader.TryGetString(versionComp, "Name", out var name))
                gameVersion = name;
        }

        return new SaveWorldMetadata
        {
            Found = true,
            LevelName = NbtReader.TryGetString(data, "LevelName", out var levelName) ? levelName : null,
            GameType = NbtReader.TryGetInt(data, "GameType", out var gameType) ? gameType : null,
            Difficulty = ReadDifficulty(data),
            Seed = seed,
            AllowCommands = NbtReader.TryGetBool(data, "allowCommands", out var allowCommands) ? allowCommands : null,
            SpawnX = spawnX,
            SpawnY = spawnY,
            SpawnZ = spawnZ,
            GameVersion = gameVersion,
            DataVersion = NbtReader.TryGetInt(data, "DataVersion", out var dv) ? dv : null,
            LastPlayed = NbtReader.TryGetLong(data, "LastPlayed", out var lp) ? lp : null,
            Time = NbtReader.TryGetLong(data, "Time", out var t) ? t : null,
            Hardcore = NbtReader.TryGetBool(data, "hardcore", out var hc) ? hc : null
        };
    }

    private static long? ReadSeed(NbtCompoundValue data, string? worldFolder)
    {
        // Pre-1.16: Data.RandomSeed
        if (TryReadNumericSeed(data, "RandomSeed", out var seed))
            return seed;

        // 1.16–26.0: Data.WorldGenSettings.seed
        if (NbtReader.TryGetCompound(data, "WorldGenSettings", out var gen) &&
            TryReadNumericSeed(gen, "seed", out seed))
            return seed;

        if (NbtReader.TryGetCompound(data, "world_gen_settings", out var genSnake) &&
            TryReadNumericSeed(genSnake, "seed", out seed))
            return seed;

        // 26.1+: data/minecraft/world_gen_settings.dat → data.seed
        if (!string.IsNullOrWhiteSpace(worldFolder))
        {
            var sidecar = Path.Combine(worldFolder, "data", "minecraft", "world_gen_settings.dat");
            var root = LevelDatService.TryReadGzipRoot(sidecar);
            if (root is not null)
            {
                if (NbtReader.TryGetCompound(root.Compound, "data", out var settings) &&
                    TryReadNumericSeed(settings, "seed", out seed))
                    return seed;

                if (TryReadNumericSeed(root.Compound, "seed", out seed))
                    return seed;
            }
        }

        return null;
    }

    private static bool TryReadNumericSeed(NbtCompoundValue map, string key, out long seed)
    {
        if (NbtReader.TryGetLong(map, key, out seed))
            return true;

        if (NbtReader.TryGetString(map, key, out var text) &&
            !string.IsNullOrWhiteSpace(text) &&
            long.TryParse(text.Trim(), out seed))
            return true;

        seed = 0;
        return false;
    }

    private static int? ReadDifficulty(NbtCompoundValue data)
    {
        if (NbtReader.TryGetInt(data, "Difficulty", out var difficulty))
            return difficulty;

        if (NbtReader.TryGetCompound(data, "difficulty_settings", out var settings) &&
            NbtReader.TryGetString(settings, "difficulty", out var name) &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name.ToLowerInvariant() switch
            {
                "peaceful" => 0,
                "easy" => 1,
                "normal" => 2,
                "hard" => 3,
                _ => null
            };
        }

        return null;
    }

    private static void ReadSpawn(NbtCompoundValue data, out int? spawnX, out int? spawnY, out int? spawnZ)
    {
        spawnX = NbtReader.TryGetInt(data, "SpawnX", out var x) ? x : null;
        spawnY = NbtReader.TryGetInt(data, "SpawnY", out var y) ? y : null;
        spawnZ = NbtReader.TryGetInt(data, "SpawnZ", out var z) ? z : null;
        if (spawnX is not null && spawnY is not null && spawnZ is not null)
            return;

        if (!NbtReader.TryGetCompound(data, "spawn", out var spawn) ||
            !NbtReader.TryGetIntArray(spawn, "pos", out var pos) ||
            pos.Length < 3)
            return;

        spawnX ??= pos[0];
        spawnY ??= pos[1];
        spawnZ ??= pos[2];
    }
}
