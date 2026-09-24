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
    /// Plots generic IEC 60898-1 time-current BANDS (min-trip and max-clear
    /// edges) as SVG, one file per adjacent-size MCB pair in the TCC database,
    /// with the band selectivity verdict in the title. Drops them in
    /// <c>&lt;output&gt;/electrical/tcc/</c>. MCCB / ACB entries have no
    /// generic characteristic and are reported, not drawn.
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
                    "TCC database is empty. Provide STING_TCC_DATABASE.json entries (deviceLabel + type MCB-B / MCB-C / MCB-D).");
                return Result.Cancelled;
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical", "tcc");
            Directory.CreateDirectory(outDir);

            var pairs = BuildPairsFromDatabase(db, out int noData);
            int written = 0;
            var verdicts = new List<string>();
            foreach (var (upstream, downstream, faultKa) in pairs)
            {
                try
                {
                    var ub = upstream.ToBand();
                    var dbnd = downstream.ToBand();
                    var check = IecMcbBands.Check(ub, dbnd, faultKa);
                    var series = new List<TccPlotSeries>
                    {
                        TccPlotSeries.FromBand(ub,   $"Upstream {ub} — max clear", maxEdge: true),
                        TccPlotSeries.FromBand(ub,   $"Upstream {ub} — min trip",  maxEdge: false),
                        TccPlotSeries.FromBand(dbnd, $"Downstream {dbnd} — max clear", maxEdge: true),
                        TccPlotSeries.FromBand(dbnd, $"Downstream {dbnd} — min trip",  maxEdge: false)
                    };
                    string title = $"TCC bands — {ub} / {dbnd} — {check.Verdict}";
                    string note = $"{IecMcbBands.Basis}. Fault level = breaking capacity (assumed), not a project value.";
                    string fname = $"TCC_{upstream.DeviceLabel}_to_{downstream.DeviceLabel}.svg".Replace("/", "_");
                    TccCurvePlotter.WriteSvgFile(Path.Combine(outDir, fname), series, faultKa, title, note);
                    verdicts.Add($"{ub} / {dbnd}: {check.Verdict}");
                    written++;
                }
                catch (Exception ex) { StingLog.Warn($"TCC plot pair: {ex.Message}"); }
            }
            StingLog.Info($"TccPlot: wrote {written} band plot(s); {noData} database entr(ies) without a generic band. {string.Join("; ", verdicts)}");

            TaskDialog.Show("STING TCC Plot",
                $"Wrote {written} TCC band plot(s) to:\n{outDir}\n\n" +
                $"{noData} device(s) (MCCB / ACB / unknown curve) have no curve data and were not plotted.\n\n" +
                $"Basis: {IecMcbBands.Basis}.");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch (Exception ex) { StingLog.Warn($"TCC plot open folder: {ex.Message}"); }
            return Result.Succeeded;
        }

        private static List<(TccEntry up, TccEntry dn, double psc)> BuildPairsFromDatabase(TccDatabase db, out int noData)
        {
            // Pair each MCB entry with the next-bigger MCB of the same curve letter.
            // Entries with no generic band (MCCB / ACB / unknown curve) are counted,
            // never plotted — there is nothing defensible to draw for them.
            var result = new List<(TccEntry, TccEntry, double)>();
            var banded = db.Entries.Where(e => e.ToBand().HasBand).ToList();
            noData = db.Entries.Count - banded.Count;
            foreach (var grp in banded.GroupBy(e => e.ToBand().Curve))
            {
                var ordered = grp.OrderBy(e => e.ToBand().RatingA).ToList();
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
    }
}
