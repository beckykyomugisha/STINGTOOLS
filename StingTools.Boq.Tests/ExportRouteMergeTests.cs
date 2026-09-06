using System;
using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Boq.Tests
{
    /// <summary>
    /// FOLDER — an export-type key added to the shipped defaults never reached a
    /// project that already had a persisted project_setup.json: ProjectSetup.Load
    /// only null-guarded ExportRoutes, it never backfilled new keys. In CdeFirst
    /// mode that is not cosmetic — GetExportFolder returns MISC for any key the
    /// routes do not carry, so the export silently lands in the wrong folder.
    ///
    /// The merge must ADD missing keys and never overwrite a customised one.
    /// </summary>
    public class ExportRouteMergeTests
    {
        private static Dictionary<string, string> Routes(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        [Fact]
        public void A_Missing_Key_Is_Added()
        {
            var target = Routes("PDF", "DRAWINGS");
            var defaults = Routes("PDF", "DRAWINGS", "MaterialSchedule", "WIP|Schedules");

            int added = ExportRouteMerge.MergeMissing(target, defaults);

            Assert.Equal(1, added);
            Assert.Equal("WIP|Schedules", target["MaterialSchedule"]);
        }

        [Fact]
        public void A_Customised_Route_Is_Never_Overwritten()
        {
            var target = Routes("PDF", "MY_CUSTOM_FOLDER");
            var defaults = Routes("PDF", "DRAWINGS");

            int added = ExportRouteMerge.MergeMissing(target, defaults);

            Assert.Equal(0, added);
            Assert.Equal("MY_CUSTOM_FOLDER", target["PDF"]);
        }

        [Fact]
        public void A_Deliberately_Blanked_Route_Counts_As_Present_And_Stays_Blank()
        {
            // A user who cleared a route meant to clear it. Treating "" as absent
            // would silently resurrect the default on every load.
            var target = Routes("PDF", "");
            var defaults = Routes("PDF", "DRAWINGS");

            int added = ExportRouteMerge.MergeMissing(target, defaults);

            Assert.Equal(0, added);
            Assert.Equal("", target["PDF"]);
        }

        [Fact]
        public void Matching_Is_Case_Insensitive_So_No_Duplicate_Key_Is_Minted()
        {
            var target = Routes("materialschedule", "SOMEWHERE");
            var defaults = Routes("MaterialSchedule", "WIP|Schedules");

            int added = ExportRouteMerge.MergeMissing(target, defaults);

            Assert.Equal(0, added);
            Assert.Single(target);
        }

        [Fact]
        public void Null_Inputs_Are_A_No_Op_Not_A_Crash()
        {
            Assert.Equal(0, ExportRouteMerge.MergeMissing(null, Routes("A", "B")));
            Assert.Equal(0, ExportRouteMerge.MergeMissing(Routes("A", "B"), null));
        }

        [Fact]
        public void Nothing_Missing_Reports_Zero_So_The_Caller_Skips_The_Re_Save()
        {
            var target = Routes("PDF", "DRAWINGS", "IFC", "MODELS");
            var defaults = Routes("PDF", "DRAWINGS", "IFC", "MODELS");

            Assert.Equal(0, ExportRouteMerge.MergeMissing(target, defaults));
        }
    }
}
