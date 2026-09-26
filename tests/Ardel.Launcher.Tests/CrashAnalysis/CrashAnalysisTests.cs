using Ardel.Launcher.Localization;
using Ardel.Launcher.Models;
using Ardel.Launcher.Services.CrashAnalysis;
using Xunit;

namespace Ardel.Launcher.Tests.CrashAnalysis;

/// <summary>
/// Unit tests use only the minimal rule needles under test — never fabricated full crash-report documents.
/// Real diagnosis quality must be validated against user-provided logs / on-disk reports.
/// </summary>
public class CrashExitGateTests
{
    [Fact]
    public void NormalQuit_NeverAnalyzes()
    {
        var settings = new LauncherSettings { CrashAnalysisEnabled = true, CrashAnalysisOnForceKill = true };
        Assert.False(CrashExitGate.ShouldAnalyze(CrashExitKind.NormalQuit, settings));
    }

    [Fact]
    public void ForceKill_RespectsSetting()
    {
        var off = new LauncherSettings { CrashAnalysisEnabled = true, CrashAnalysisOnForceKill = false };
        var on = new LauncherSettings { CrashAnalysisEnabled = true, CrashAnalysisOnForceKill = true };
        Assert.False(CrashExitGate.ShouldAnalyze(CrashExitKind.ArdelForceKill, off));
        Assert.True(CrashExitGate.ShouldAnalyze(CrashExitKind.ArdelForceKill, on));
    }

    [Fact]
    public void Disabled_BlocksEverything()
    {
        var settings = new LauncherSettings { CrashAnalysisEnabled = false, CrashAnalysisOnForceKill = true };
        Assert.False(CrashExitGate.ShouldAnalyze(CrashExitKind.CrashLike, settings));
        Assert.False(CrashExitGate.ShouldAnalyze(CrashExitKind.ArdelForceKill, settings));
    }

    [Fact]
    public void Classify_ForceKillWins()
    {
        Assert.Equal(
            CrashExitKind.ArdelForceKill,
            CrashExitGate.ClassifyExit(1, wasForceKilled: true));
    }

    [Fact]
    public void Classify_ExitZeroWithoutEvidence_IsNormal()
    {
        Assert.Equal(
            CrashExitKind.NormalQuit,
            CrashExitGate.ClassifyExit(0, wasForceKilled: false));
    }

    [Fact]
    public void Classify_LogNeedleAlone_DoesNotOpen()
    {
        // Log markers without an on-disk crash-report/hs_err must not reopen the dialog.
        var facts = new FactBag
        {
            MatchCorpus = "Manually triggered debug crash",
            Combined = "Manually triggered debug crash"
        };
        Assert.Equal(
            CrashExitKind.NormalQuit,
            CrashExitGate.ClassifyExit(0, wasForceKilled: false, facts));
    }

    [Fact]
    public void Classify_CrashReportArtifact_IsCrashLike()
    {
        Assert.Equal(
            CrashExitKind.CrashLike,
            CrashExitGate.ClassifyExit(
                0,
                wasForceKilled: false,
                new FactBag { HasCrashReport = true, CrashReport = "---- Minecraft Crash Report ----" }));
    }

    [Fact]
    public void Classify_ForceKill_NotOverriddenByLogNoise()
    {
        Assert.Equal(
            CrashExitKind.ArdelForceKill,
            CrashExitGate.ClassifyExit(
                1,
                wasForceKilled: true,
                new FactBag
                {
                    MatchCorpus = "java.lang.OutOfMemoryError",
                    Combined = "java.lang.OutOfMemoryError"
                }));
    }

    [Fact]
    public void Classify_NonZeroWithoutEvidence_IsCrashLike()
    {
        Assert.Equal(
            CrashExitKind.CrashLike,
            CrashExitGate.ClassifyExit(42, wasForceKilled: false, previewFacts: null));
        Assert.Equal(
            CrashExitKind.CrashLike,
            CrashExitGate.ClassifyExit(1, wasForceKilled: false, previewFacts: null));
    }

    [Fact]
    public void Classify_StaleFatalOutsideMatchCorpus_DoesNotOpen()
    {
        var facts = new FactBag
        {
            Combined = "[main/FATAL]: Failed to load mods (stale)\nPlayer joined the game",
            MatchCorpus = "Player joined the game"
        };
        Assert.Equal(
            CrashExitKind.NormalQuit,
            CrashExitGate.ClassifyExit(0, wasForceKilled: false, facts));
    }
}

public class CrashRuleEngineTests
{
    [Fact]
    public void R001_MatchesExactDebugCrashNeedle()
    {
        var facts = new FactBag
        {
            MatchCorpus = "Manually triggered debug crash",
            CrashReport = "Manually triggered debug crash",
            HasCrashReport = true
        };
        var match = CrashRuleEngine.Run(facts);
        Assert.NotNull(match);
        Assert.Equal("R001", match!.RuleId);
    }

    [Fact]
    public void R001_DoesNotMatchWhenNeedleAbsentFromCrashReport()
    {
        var facts = new FactBag
        {
            Combined = "Manually triggered debug crash",
            MatchCorpus = "java.lang.NullPointerException: Cannot invoke",
            CrashReport = "java.lang.NullPointerException: Cannot invoke",
            HasCrashReport = true
        };
        Assert.NotEqual("R001", CrashRuleEngine.Run(facts)?.RuleId);
    }

    [Fact]
    public void R002_MatchesOutOfMemoryNeedle()
    {
        var facts = new FactBag
        {
            MatchCorpus = "java.lang.OutOfMemoryError: Java heap space",
            CrashReport = "java.lang.OutOfMemoryError: Java heap space",
            HasCrashReport = true
        };
        Assert.Equal("R002", CrashRuleEngine.Run(facts)?.RuleId);
    }

    [Fact]
    public void R003_RequiresBothNeedles()
    {
        Assert.NotEqual(
            "R003",
            CrashRuleEngine.Run(new FactBag
            {
                MatchCorpus = "Could not reserve enough space for object heap",
                CrashReport = "Could not reserve enough space for object heap",
                HasCrashReport = true
            })?.RuleId);

        Assert.Equal(
            "R003",
            CrashRuleEngine.Run(new FactBag
            {
                MatchCorpus = "Could not reserve enough space for object heap\n1048576KB",
                CrashReport = "Could not reserve enough space for object heap\n1048576KB",
                HasCrashReport = true
            })?.RuleId);
    }

    [Fact]
    public void LogOnlyCorpus_NeverMatches()
    {
        Assert.Null(CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "java.lang.OutOfMemoryError: Java heap space",
            Combined = "java.lang.OutOfMemoryError: Java heap space",
            HasCrashReport = false,
            HasHsErr = false
        }));
    }

    [Fact]
    public void NoNeedle_ReturnsNull()
    {
        Assert.Null(CrashRuleEngine.Run(new FactBag { MatchCorpus = "Player joined the game." }));
    }

    [Fact]
    public void SoftSuspectSignals_DoNotSurface()
    {
        Assert.Null(CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "Suspected Mods: coolmod-1.0.jar",
            SuspectedModsFromReport = ["coolmod-1.0"],
            StackKeywords = ["SomeClass"],
            StackMappedMods = ["coolmod-1.0"]
        }));
    }

    [Fact]
    public void NamedMod_FallsBackToSuspectedUsr_WhenNoHardOwnership()
    {
        var match = CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "-- MOD coolmod --\nSuspected Mods: coolmod\njava.lang.NullPointerException",
            CrashReport = "-- MOD coolmod --\nSuspected Mods: coolmod\njava.lang.NullPointerException",
            HasCrashReport = true,
            SuspectedModsFromReport = ["coolmod"]
        });
        Assert.NotNull(match);
        Assert.Equal("USR001", match.RuleId);
        Assert.Contains("coolmod", match.Mods);
    }

    [Fact]
    public void FreshLogCorpus_MatchesHardRule()
    {
        var match = CrashRuleEngine.Run(new FactBag
        {
            RecentLogLines = ["java.lang.OutOfMemoryError: Java heap space"],
            HasCrashReport = false,
            HasHsErr = false
        });
        Assert.NotNull(match);
        Assert.Equal("R002", match.RuleId);
    }

    [Fact]
    public void UsrMatch_BuildsSuspectedTier()
    {
        var facts = new FactBag
        {
            HasCrashReport = true,
            CrashReport = "Some generic crash without a fatal rule\nSuspected Mods: somemod",
            SuspectedModsFromReport = ["somemod"],
            LogsFolderPath = @"C:\dummy\logs"
        };
        var settings = new LauncherSettings();
        var model = CrashAnalyzer.Analyze(new CrashAnalysisRequest
        {
            VersionId = "1.20.1",
            GameDirectory = @"C:\dummy",
            ExitCode = 1,
            ExitKind = CrashExitKind.CrashLike
        }, settings, facts);

        Assert.Equal(CrashTier.Suspected, model.Tier);
        Assert.Equal("USR001", model.RuleId);
        Assert.Contains("somemod", model.Mods);
    }

    [Fact]
    public void EntityBlockNeedles_SurfaceAsPrimaryNotFatal()
    {
        var entity = CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "Entity Type: minecraft:creeper\nEntity's Exact location: 1.0, 2.0, 3.0",
            CrashReport = "Entity Type: minecraft:creeper\nEntity's Exact location: 1.0, 2.0, 3.0",
            HasCrashReport = true
        });
        Assert.NotNull(entity);
        Assert.Equal("R039", entity!.RuleId);
        Assert.Equal(CrashPhase.Primary, entity.Phase);

        var block = CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "Block location: World: overworld\nBlock: Block{minecraft:stone}",
            CrashReport = "Block location: World: overworld\nBlock: Block{minecraft:stone}",
            HasCrashReport = true
        });
        Assert.NotNull(block);
        Assert.Equal("R038", block!.RuleId);
        Assert.Equal(CrashPhase.Primary, block.Phase);
    }

    [Fact]
    public void FabricSolution_SurfacesAsFatalKnown()
    {
        var match = CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "A potential solution has been determined",
            CrashReport = "A potential solution has been determined",
            HasCrashReport = true
        });
        Assert.Equal("R034", match?.RuleId);
        Assert.Equal(CrashPhase.Fatal, match!.Phase);
    }

    [Fact]
    public void PresentBuilder_PrimaryIsSuspected_FatalIsKnown()
    {
        var settings = new LauncherSettings { CrashAnalysisShowConfidence = true };
        var request = new CrashAnalysisRequest
        {
            VersionId = "test",
            GameDirectory = "C:\\tmp",
            ExitCode = 1,
            ExitKind = CrashExitKind.CrashLike
        };

        var suspectedFacts = new FactBag
        {
            Description = "Exception generating new chunk",
            ExceptionLine = "java.lang.NullPointerException: test",
            CrashReport = "Entity Type: minecraft:creeper\nEntity's Exact location: 1.0, 2.0, 3.0",
            MatchCorpus = "Entity Type: minecraft:creeper\nEntity's Exact location: 1.0, 2.0, 3.0",
            CrashReportLines =
            [
                "Description: Exception generating new chunk",
                "Entity Type: minecraft:creeper",
                "Entity's Exact location: 1.0, 2.0, 3.0"
            ],
            HasCrashReport = true,
            LogsFolderPath = "C:\\tmp\\logs"
        };
        var suspected = CrashAnalyzer.Analyze(request, settings, suspectedFacts);
        Assert.Equal(CrashTier.Suspected, suspected.Tier);
        Assert.Equal("R039", suspected.RuleId);
        Assert.Contains(suspected.EvidenceLines, l => l.Contains("Description", StringComparison.Ordinal));
        Assert.Contains(
            Loc.Get(LocKeys.Crash_Usr_NotCertain),
            suspected.Explain,
            StringComparison.Ordinal);

        var knownFacts = new FactBag
        {
            Description = "Mod loading failed",
            CrashReport = "A potential solution has been determined",
            MatchCorpus = "A potential solution has been determined",
            CrashReportLines = ["Description: Mod loading failed", "A potential solution has been determined"],
            HasCrashReport = true,
            SuspectedModsFromReport = ["innocent-mod"],
            LogsFolderPath = "C:\\tmp\\logs"
        };
        var known = CrashAnalyzer.Analyze(request, settings, knownFacts);
        Assert.Equal(CrashTier.Known, known.Tier);
        Assert.Equal("R034", known.RuleId);
        // Soft Suspected Mods list must never become guilt chips without ownership.
        Assert.DoesNotContain("innocent-mod", known.Mods);
        Assert.Empty(known.Mods);
    }

    [Fact]
    public void PresentBuilder_UnknownShowsRealReportHead()
    {
        var settings = new LauncherSettings();
        var model = CrashAnalyzer.Analyze(
            new CrashAnalysisRequest
            {
                VersionId = "test",
                GameDirectory = "C:\\tmp",
                ExitCode = 1,
                ExitKind = CrashExitKind.CrashLike
            },
            settings,
            new FactBag
            {
                Description = "Unexpected error",
                ExceptionLine = "java.lang.IllegalStateException: boom",
                CrashReportLines =
                [
                    "---- Minecraft Crash Report ----",
                    "Description: Unexpected error",
                    "java.lang.IllegalStateException: boom"
                ],
                HasCrashReport = true,
                SuspectedModsFromReport = [],
                LogsFolderPath = "C:\\tmp\\logs"
            });

        Assert.Equal(CrashTier.Unknown, model.Tier);
        Assert.Equal("Unexpected error", model.ReportDescription);
        Assert.Contains(model.EvidenceLines, l => l.Contains("Unexpected error", StringComparison.Ordinal));
        Assert.Empty(model.Mods);
    }

    [Fact]
    public void PrimaryWithoutCrashArtifact_ReturnsNull()
    {
        Assert.Null(CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "java.lang.NoSuchMethodError: 'void foo.Bar.baz()'\n-- Entity being ticked",
            HasCrashReport = false,
            HasHsErr = false
        }));
    }

    [Fact]
    public void ZipExceptionAlone_DoesNotClaimCorruptJar()
    {
        Assert.Null(CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "java.util.zip.ZipException: something else",
            CrashReport = "java.util.zip.ZipException: something else",
            HasCrashReport = true
        }));
    }

    [Fact]
    public void NoSuchMethod_SurfacesAsPrimaryWithoutOwnership()
    {
        var match = CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "java.lang.NoSuchMethodError: 'void foo.Bar.baz()'",
            CrashReport = "java.lang.NoSuchMethodError: 'void foo.Bar.baz()'",
            HasCrashReport = true
        });
        Assert.Equal("R068", match?.RuleId);
        Assert.Equal(CrashPhase.Primary, match!.Phase);
    }

    [Fact]
    public void CriticalInjection_SurfacesAsFatal()
    {
        var match = CrashRuleEngine.Run(new FactBag
        {
            MatchCorpus = "Critical injection failure: Mixin target not found",
            CrashReport = "Critical injection failure: Mixin target not found",
            HasCrashReport = true
        });
        Assert.Equal("R061", match?.RuleId);
        Assert.Equal(CrashPhase.Fatal, match!.Phase);
    }

    [Fact]
    public void SodiumNeedle_SurfacesAsFatalKnown()
    {
        var model = CrashAnalyzer.Analyze(
            new CrashAnalysisRequest
            {
                VersionId = "test",
                GameDirectory = "C:\\tmp",
                ExitCode = 1,
                ExitKind = CrashExitKind.CrashLike
            },
            new LauncherSettings(),
            new FactBag
            {
                MatchCorpus = "Mod X requires Sodium 0.5 but 0.4 is installed",
                CrashReport = "Mod X requires Sodium 0.5 but 0.4 is installed",
                HasCrashReport = true,
                LogsFolderPath = "C:\\tmp\\logs"
            });
        Assert.Equal(CrashTier.Known, model.Tier);
        Assert.Equal("R050", model.RuleId);
    }

    [Fact]
    public void TickingWorldDescription_SurfacesAsPrimary()
    {
        Assert.Equal(
            "R111",
            CrashRuleEngine.Run(new FactBag
            {
                MatchCorpus = "Description: Exception ticking world",
                CrashReport = "Description: Exception ticking world",
                Description = "Exception ticking world",
                HasCrashReport = true
            })?.RuleId);
    }

    [Fact]
    public void FabricSolutionLines_PrefixedIntoSolution()
    {
        var model = CrashAnalyzer.Analyze(
            new CrashAnalysisRequest
            {
                VersionId = "test",
                GameDirectory = "C:\\tmp",
                ExitCode = 1,
                ExitKind = CrashExitKind.CrashLike
            },
            new LauncherSettings(),
            new FactBag
            {
                MatchCorpus = "A potential solution has been determined:\n\t - Install fabric-api",
                CrashReport = "A potential solution has been determined:\n\t - Install fabric-api",
                HasCrashReport = true,
                LoaderSolutionLines =
                [
                    "A potential solution has been determined:",
                    "- Install fabric-api"
                ],
                LogsFolderPath = "C:\\tmp\\logs"
            });
        Assert.Equal("R034", model.RuleId);
        Assert.Contains("Install fabric-api", model.Solution, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gate_LogCaughtException_DoesNotOpenWithoutArtifact()
    {
        Assert.Equal(
            CrashExitKind.NormalQuit,
            CrashExitGate.ClassifyExit(
                0,
                wasForceKilled: false,
                new FactBag { MatchCorpus = "Caught exception from coolmod" }));
    }

    [Fact]
    public void CrashLocKeys_Resolve()
    {
        Assert.NotEqual(LocKeys.Crash_Unknown_Title, Loc.Get(LocKeys.Crash_Unknown_Title));
        Assert.NotEqual(LocKeys.Crash_R001_Title, Loc.Get(LocKeys.Crash_R001_Title));
        Assert.Contains("ChatGPT", Loc.Get(LocKeys.Crash_Unknown_Solution), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllCrashLocKeys_ResolveToNonKeyText()
    {
        var fields = typeof(LocKeys).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(k => k.StartsWith("Crash_", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(fields);
        foreach (var key in fields)
        {
            var value = Loc.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void MixinMod_AttachesToCrashMatch()
    {
        var facts = new FactBag
        {
            HasCrashReport = true,
            CrashReport = "org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [sodium.mixins.json:core.MinecraftClientMixin] from phase [DEFAULT] in config [sodium.mixins.json] FAILED during APPLY",
            CaughtExceptionFromMod = "sodium"
        };
        var match = CrashRuleEngine.Run(facts);
        Assert.NotNull(match);
        Assert.Contains("sodium", match.Mods);
    }
}
