// ══════════════════════════════════════════════════════════════════════════
//  SiteToolsGatherer.cs — MAT-SCHED-9 Revit-side inputs for the tools model.
//
//  Two facts the model cannot measure: how long the job runs, and how many
//  storeys it has. Duration comes from a ProjectInformation parameter, storeys
//  are counted from the model's own Levels, and the command prompts when the
//  duration is missing rather than guessing one.
//
//  Duration is the denominator of every gang size. Defaulting it would produce
//  a confident tool list for a programme nobody stated, so a missing duration
//  yields NOTHING and says so.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.BOQ.MaterialSchedule
{
    internal static class SiteToolsGatherer
    {
        /// <summary>The ProjectInformation parameter carrying programme length.</summary>
        public const string DurationParam = "PRJ_DURATION_DAYS";

        /// <summary>Programme length in days from ProjectInformation, or 0 when
        /// absent/unparseable — the caller then asks the user.</summary>
        public static int ReadDurationDays(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return 0;
                var p = pi.LookupParameter(DurationParam);
                if (p == null || !p.HasValue) return 0;

                if (p.StorageType == StorageType.Integer) return Math.Max(0, p.AsInteger());
                if (p.StorageType == StorageType.Double) return (int)Math.Max(0, Math.Round(p.AsDouble()));
                return int.TryParse((p.AsString() ?? "").Trim(), out int v) ? Math.Max(0, v) : 0;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SiteToolsGatherer.ReadDurationDays: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Storeys counted from the model's own Levels — real data beats a typed
        /// number. Levels below the project base (basements, and the datum-only
        /// levels people leave lying around) are excluded, and the count never
        /// drops below 1.
        /// </summary>
        public static int CountStoreys(Document doc)
        {
            try
            {
                if (doc == null) return 1;
                int n = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .Count(l => l != null && l.Elevation > -0.01);
                return Math.Max(1, n);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SiteToolsGatherer.CountStoreys: {ex.Message}");
                return 1;
            }
        }

        /// <summary>Measured work for the gang model, taken from the commodities
        /// the schedule has already produced — so the tools follow the same
        /// quantities the bill does, not a second parallel take-off.</summary>
        public static SiteToolsInput FromDocument(MaterialScheduleDocument msDoc,
                                                  int durationDays, int storeys)
        {
            var input = new SiteToolsInput { DurationDays = durationDays, Storeys = storeys };
            if (msDoc == null) return input;

            double Sum(string key) => msDoc.Stages
                .SelectMany(s => s.Commodities)
                .Where(c => string.Equals(c.CommodityKey, key, StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.OrderQuantity);

            input.BlockworkM2 = Sum("block") / 12.5;      // pieces back to wall area
            input.BrickworkM2 = Sum("brick") / 60.0;
            input.RebarKg = Sum("rebar");
            input.FormworkM2 = Sum("formwork-timber");
            input.ConcreteM3 = Sum("concrete-ready");
            return input;
        }
    }
}
