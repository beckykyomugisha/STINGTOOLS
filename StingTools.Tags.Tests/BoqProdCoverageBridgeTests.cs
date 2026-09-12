// ══════════════════════════════════════════════════════════════════════════
//  BoqProdCoverageBridgeTests.cs — the gate on W6.
//
//  ProjectKickoff ends at CreateRevision; BOQ_FullRefresh starts at
//  Cost_MigrateESEntities. Nothing joined them, and an unruled PROD code is a
//  wrong rate — the rate chain's most specific pass keys on DISC|PROD, so a
//  family no rule covers takes a code by category fallback and prices as the
//  category average with nothing said.
//
//  W6 puts Prod_CoverageAudit ahead of BOQRefresh in both BOQ presets. The two
//  things worth holding are the ORDER (after it, the audit names families whose
//  rates are already in the sheet) and the OPTIONAL flag.
//
//  RED / GREEN, recorded 2026-09-10 on this machine:
//    the step moved after BOQRefresh
//      RED   "audits coverage AFTER the rates are already in the sheet"   (2 of 6)
//      GREEN index 3, before BOQRefresh, in both presets
//    optional removed
//      RED   "is not optional"                                            (2 of 6)
//      GREEN optional in both
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StingTools.Tags.Tests
{
    public class BoqProdCoverageBridgeTests
    {
        private static string DataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "StingTools", "Data")))
                dir = dir.Parent;
            Assert.True(dir != null, "Could not locate StingTools/Data from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "StingTools", "Data");
        }

        private static JArray Steps(string preset)
        {
            string p = Path.Combine(DataDir(), preset);
            Assert.True(File.Exists(p), "preset missing: " + preset);
            var j = JObject.Parse(File.ReadAllText(p));
            var steps = j["steps"] as JArray;
            Assert.True(steps != null && steps.Count > 0, preset + " has no steps");
            return steps;
        }

        public static TheoryData<string> BoqPresets => new TheoryData<string>
        {
            "WORKFLOW_BOQ_FullRefresh.json",
            "WORKFLOW_BOQ_TenderPack.json",
        };

        [Theory]
        [MemberData(nameof(BoqPresets))]
        public void Prod_Coverage_Is_Audited_Before_The_Boq_Is_Rebuilt(string preset)
        {
            var tags = Steps(preset).Select(s => (string)s["commandTag"]).ToList();

            int audit = tags.IndexOf("Prod_CoverageAudit");
            int refresh = tags.IndexOf("BOQRefresh");

            Assert.True(audit >= 0, preset + " does not audit PROD coverage at all");
            Assert.True(refresh >= 0, preset + " has no BOQRefresh");
            Assert.True(audit < refresh,
                preset + " audits coverage AFTER the rates are already in the sheet. An "
              + "unruled family takes a PROD code by category fallback and prices as the "
              + "category average; naming it afterwards is naming it too late.");
        }

        [Theory]
        [MemberData(nameof(BoqPresets))]
        public void The_Audit_Is_Optional_Because_It_Only_Reports(string preset)
        {
            var step = Steps(preset).First(s => (string)s["commandTag"] == "Prod_CoverageAudit");
            Assert.True((bool?)step["optional"] == true,
                preset + ": Prod_CoverageAudit is not optional. It is read-only — it must not "
              + "be able to halt a BOQ rebuild or a tender pack on its own.");
        }

        [Theory]
        [MemberData(nameof(BoqPresets))]
        public void The_Audit_Is_Added_Once_And_Changes_Nothing_Else(string preset)
        {
            var tags = Steps(preset).Select(s => (string)s["commandTag"]).ToList();

            Assert.Equal(1, tags.Count(t => t == "Prod_CoverageAudit"));

            // The rest of the chain is untouched and still in order — W6 is one
            // insertion, not a re-plan of the BOQ workflow.
            var rest = tags.Where(t => t != "Prod_CoverageAudit").ToList();
            Assert.Equal("BOQRefresh", rest[rest.IndexOf("Cost_ValidateAll") + 1 == rest.Count
                                            ? rest.Count - 1
                                            : rest.IndexOf("BOQRefresh")]);
            Assert.Contains("BOQSnapshotSave", rest);
            Assert.True(rest.IndexOf("BOQRefresh") < rest.IndexOf("BOQSnapshotSave"));
        }
    }
}
