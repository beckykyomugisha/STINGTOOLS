using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>
    /// Plots time-current coordination curves as SVG, one file per
    /// upstream/downstream OCPD pair. Drops them in
    /// <c>&lt;output&gt;/electrical/tcc/</c> for inclusion in shop drawings
    /// or the design-pack PDF. Picks pairs from
    /// <see cref="StingElectricalCommandHandler.LastSelectiveCoordResults"/>
    /// when available; otherwise samples every distinct rating in the
    /// TCC database and pairs adjacent sizes.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TccPlotCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var db = TccDatabaseLoader.Load(null);
            if (db.Entries.Count == 0)
            {
                TaskDialog.Show("STING TCC Plot",
                    "TCC database is empty. Provide STING_TCC_DATABASE.json with `points` arrays for log-log plots.");
                return Result.Cancelled;
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical", "tcc");
            Directory.CreateDirectory(outDir);

            var pairs = BuildPairsFromDatabase(db);
            int written = 0;
            foreach (var (upstream, downstream, faultKa) in pairs)
            {
                try
                {
                    var series = new List<TccPlotSeries>
                    {
                        TccPlotSeries.FromTccEntry(upstream,   $"Upstream — {upstream.DeviceLabel} ({upstream.Type})"),
                        TccPlotSeries.FromTccEntry(downstream, $"Downstream — {downstream.DeviceLabel} ({downstream.Type})")
                    };
                    string title = $"TCC Coordination — {upstream.DeviceLabel} / {downstream.DeviceLabel}";
                    string fname = $"TCC_{upstream.DeviceLabel}_to_{downstream.DeviceLabel}.svg".Replace("/", "_");
                    TccCurvePlotter.WriteSvgFile(Path.Combine(outDir, fname), series, faultKa, title);
                    written++;
                }
                catch (Exception ex) { StingLog.Warn($"TCC plot pair: {ex.Message}"); }
            }

            TaskDialog.Show("STING TCC Plot",
                $"Wrote {written} TCC SVG plot(s) to:\n{outDir}\n\n" +
                "Embed in shop drawings via Image import or open the SVGs directly in any browser.");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static List<(TccEntry up, TccEntry dn, double psc)> BuildPairsFromDatabase(TccDatabase db)
        {
            // Pair each entry with the next-bigger entry (same Type group),
            // representing a typical cascaded discrimination check.
            var result = new List<(TccEntry, TccEntry, double)>();
            foreach (var grp in db.Entries.GroupBy(e => e.Type))
            {
                var ordered = grp.OrderBy(e => ParseRating(e.DeviceLabel)).ToList();
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    var dn = ordered[i];
                    var up = ordered[i + 1];
                    double psc = Math.Min(up.MaxFaultKa, dn.MaxFaultKa);
                    result.Add((up, dn, psc));
                }
            }
            return result;
        }

        private static double ParseRating(string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            string digits = new string(label.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return double.TryParse(digits, out double v) ? v : 0;
        }
    }
}
