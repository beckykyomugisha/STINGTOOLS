using System;
using System.IO;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review PERF-1. Revit raises DocumentOpened for linked models. TagConfig is one
    /// static object for the process, so every link reloaded it with its own config and the last
    /// link loaded won — the host was then tagged with a link's codes — and the project-folder
    /// bootstrap and climate stamp ran against the link. Source guard: the handler cannot run
    /// without Revit.
    /// </summary>
    public class LinkedDocumentGuardTests
    {
        [Fact]
        public void DocumentOpenedReturnsForALinkBeforeTouchingAnything()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string s = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "Core", "StingToolsApp.cs"));
            int a = s.IndexOf("private static void OnDocumentOpened(", StringComparison.Ordinal);
            Assert.True(a > 0);
            int guard = s.IndexOf("e.Document.IsLinked", a, StringComparison.Ordinal);
            int firstWork = s.IndexOf("FormulaEngine.ClearCache()", a, StringComparison.Ordinal);
            Assert.True(guard > 0, "OnDocumentOpened has no IsLinked guard");
            Assert.True(guard < firstWork, "the IsLinked guard must come before any work");
        }
    }
}
