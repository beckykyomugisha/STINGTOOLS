// Source-level regression guards for the live + scheduled clash wireup.
//
// The live clash path failed silently for a long time because three
// independent files had to agree:
//
//   1. StingToolsApp.OnStartup must register the *real*
//      Core.Clash.LiveClashUpdater (the file with AddTrigger calls), not the
//      Phase-106 stub that used to live in StingTools.Clash.
//   2. StingToolsApp.OnStartup must call LiveClashWireup.Subscribe so
//      DocumentChanged drains LiveClashUpdater.DirtyQueue. Without this,
//      ClashSession.LastDirtyAtUtc is never advanced and ClashScheduler.OnTick
//      suppresses every tick after the first.
//   3. The command-tag dispatcher (StingCommandHandler) and WorkflowEngine
//      must route ClashDetect / ClashDetection to Core.Clash.ClashRunCommand,
//      not the legacy AABB-only Temp.ClashDetectionCommand.
//
// These can't be tested via reflection because the test project deliberately
// avoids referencing Revit. Read the source files instead — the failure modes
// are textual and stable.
using System;
using System.IO;
using Xunit;

namespace StingTools.Clash.Tests
{
    public class ClashWireupRegressionTests
    {
        private static string RepoRoot()
        {
            // The test binary lives under StingTools.Clash.Tests/bin/.../net8.0/.
            // Walk up until we find StingTools.csproj.
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "StingTools", "StingTools.csproj")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
        }

        private static string Read(string relPath) =>
            File.ReadAllText(Path.Combine(RepoRoot(), relPath));

        [Fact]
        public void StingToolsApp_RegistersRealLiveClashUpdater()
        {
            var src = Read("StingTools/Core/StingToolsApp.cs");

            // The using directive must point at Core.Clash so the unqualified
            // LiveClashUpdater resolves to the real one with triggers.
            Assert.Contains("using StingTools.Core.Clash;", src);

            // The legacy Phase-106 stub namespace must not be imported.
            Assert.DoesNotContain("using StingTools.Clash;", src);

            // The Register call must actually be present.
            Assert.Contains("LiveClashUpdater.Register(application)", src);
        }

        [Fact]
        public void StingToolsApp_SubscribesLiveClashWireup()
        {
            var src = Read("StingTools/Core/StingToolsApp.cs");
            Assert.Contains("LiveClashWireup.Subscribe(application)", src);
        }

        [Fact]
        public void CommandHandler_RoutesClashDetectToRunCommand()
        {
            var src = Read("StingTools/UI/StingCommandHandler.cs");

            Assert.Contains("case \"ClashDetect\": RunCommand<Core.Clash.ClashRunCommand>(app);", src);
            Assert.Contains("case \"ClashDetection\": RunCommand<Core.Clash.ClashRunCommand>(app);", src);
            Assert.Contains("case \"ClashRun\":              RunCommand<Core.Clash.ClashRunCommand>(app);", src);

            // Legacy Temp routing for these two tags must be gone.
            Assert.DoesNotContain("case \"ClashDetect\": RunCommand<Temp.ClashDetectionCommand>(app);", src);
            Assert.DoesNotContain("case \"ClashDetection\": RunCommand<Temp.ClashDetectionCommand>(app);", src);
        }

        [Fact]
        public void WorkflowEngine_RoutesClashDetectionToRunCommand()
        {
            var src = Read("StingTools/Core/WorkflowEngine.cs");

            Assert.Contains("case \"ClashDetection\":          return new Core.Clash.ClashRunCommand();", src);
            Assert.DoesNotContain("case \"ClashDetection\":          return new Temp.ClashDetectionCommand();", src);
        }

        [Fact]
        public void DuplicateLiveClashUpdater_FileIsRemoved()
        {
            // The legacy ClashDetectionCommands.cs in StingTools.Clash held a
            // stub LiveClashUpdater + a parallel ClashSession + ClashIdentity
            // that competed with the real types via overload resolution.
            string legacy = Path.Combine(RepoRoot(), "StingTools", "Clash", "ClashDetectionCommands.cs");
            Assert.False(File.Exists(legacy),
                "Legacy ClashDetectionCommands.cs must remain deleted; it shipped duplicate stubs that hijacked LiveClashUpdater.Register.");
        }

        [Fact]
        public void LegacyTempClashCommand_IsObsolete()
        {
            var src = Read("StingTools/Temp/DataPipelineCommands.cs");
            // Find the obsolete attribute immediately above the class declaration.
            int classIdx = src.IndexOf("public class ClashDetectionCommand", StringComparison.Ordinal);
            Assert.True(classIdx > 0, "Temp.ClashDetectionCommand class declaration not found.");
            string preamble = src.Substring(Math.Max(0, classIdx - 600), 600);
            Assert.Contains("[Obsolete(", preamble);
            Assert.Contains("ClashRunCommand", preamble);
        }

        [Fact]
        public void LiveClashHandler_RoundRobinsWatchedElements()
        {
            // The watched-element re-evaluation must use a cursor so a large
            // pinned set doesn't starve the 200 ms tick budget on the first N.
            var src = Read("StingTools/Clash/LiveClashHandler.cs");
            Assert.Contains("_watchedCursor", src);
            // Cursor must be modulo the watched-set size to wrap.
            Assert.Matches(@"_watchedCursor\s*=\s*\(.*?\)\s*%\s*ordered\.Count", src);
        }
    }
}
