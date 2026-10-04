using System;
using System.IO;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// KUT deep review MEP-1 / MEP-2 / MEP-13 — the COBie builder (BIMManagerCommands
    /// BuildCOBieData). Source guards, because the builder needs a Revit document.
    /// <list type="bullet">
    ///   <item>MEP-1: the component lookup was built with doc.GetElement(ExternalIdentifier).
    ///         That identifier is the 22-character IFC GlobalId and GetElement(string) only
    ///         resolves a Revit UniqueId, so the map was always empty and the Attribute, Job,
    ///         Impact and System passes skipped every component.</item>
    ///   <item>MEP-2: a missing installation date became the project issue date or today, and
    ///         warranty start copied it.</item>
    ///   <item>MEP-13: Job frequency came from a per-system default table and every job got a
    ///         fixed four-hour duration.</item>
    /// </list>
    /// </summary>
    public class CobieExportIntegrityTests
    {
        private static string Source()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StingTools", "StingTools.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string s = File.ReadAllText(Path.Combine(dir.FullName, "StingTools", "BIMManager", "BIMManagerCommands.cs"));
            int a = s.IndexOf("var components = new List<Dictionary<string, string>>();", StringComparison.Ordinal);
            int b = s.IndexOf("data[\"Job\"] = jobs;", a < 0 ? 0 : a, StringComparison.Ordinal);
            Assert.True(a > 0 && b > a, "could not locate the COBie component-to-job section");
            return s.Substring(a, b - a);
        }

        [Fact]
        public void ComponentsAreNotLookedUpByIfcGuidThroughGetElement()
        {
            string s = Source();
            Assert.DoesNotContain("doc.GetElement(cid)", s);
            Assert.Contains("elemByExtId[extId] = el", s);
        }

        [Fact]
        public void NoInstallationOrWarrantyDateIsInvented()
        {
            string s = Source();
            Assert.DoesNotContain("PROJECT_ISSUE_DATE", s);
            Assert.DoesNotContain("warrantyStart = installDate", s);
        }

        [Fact]
        public void NoMaintenanceFrequencyOrDurationIsInvented()
        {
            string s = Source();
            Assert.DoesNotContain("defaultMaintFreq", s);
            Assert.DoesNotContain("[\"Duration\"] = \"4\"", s);
            Assert.Contains("ASS_MAINTENANCE_FREQUENCY_MONTHS", s);
        }
    }
}
