using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// A11 — Procurement hand-off.
    ///
    /// Scans every Material with Manufacturer + Model fields populated
    /// and emits an RFQ-ready CSV under the project output dir.
    /// Drops materials with no procurement metadata silently; the user
    /// can populate Manufacturer / Model / URL inline via the Browse
    /// grid or in Revit's Material Browser, then re-run the generator.
    ///
    /// Output schema (intentionally close to the format most procurement
    /// systems accept on import):
    ///
    ///   LineRef | MaterialName | Manufacturer | Model | URL | UsageCount |
    ///   UnitCost | EstimatedTotal | Class | EpdSource | Notes |
    ///   RequestedBy | RequestedAt
    /// </summary>
    public class RfqRow
    {
        public string LineRef { get; set; }
        public string MaterialName { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string Url { get; set; }
        public int UsageCount { get; set; }
        public double UnitCost { get; set; }
        public double EstimatedTotal { get; set; }
        public string Class { get; set; }
        public string EpdSource { get; set; }
        public string Notes { get; set; }

        // A5/G29 — NRM2 cost split (zero when material has no split).
        public double SupplyCost { get; set; }
        public double InstallCost { get; set; }
        public double VatPct { get; set; }
        public double NetTotal => UnitCost * Math.Max(1, UsageCount);
        public double VatAmount => NetTotal * VatPct / 100.0;
        public double GrossTotal => NetTotal + VatAmount;
    }

    public static class MaterialRfqGenerator
    {
        public static List<RfqRow> Build(Document doc)
        {
            var rows = new List<RfqRow>();
            if (doc == null) return rows;

            var usage = MaterialRowBuilder.ComputeUsageCounts(doc);
            int line = 1;
            foreach (var mat in new FilteredElementCollector(doc).OfClass(typeof(Material))
                                     .Cast<Material>().OrderBy(m => m.Name))
            {
                string mfr   = Read(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                string model = Read(mat, BuiltInParameter.ALL_MODEL_MODEL);
                string url   = Read(mat, BuiltInParameter.ALL_MODEL_URL);
                // Only quote material that's actually procurable.
                if (string.IsNullOrWhiteSpace(mfr) && string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(url))
                    continue;

                double cost = 0;
                try
                {
                    var cp = mat.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                    if (cp != null && cp.StorageType == StorageType.Double) cost = cp.AsDouble();
                }
                catch (Exception ex) { StingLog.Warn($"Rfq cost '{mat.Name}': {ex.Message}"); }

                // A5/G29 — cost split. Zero when not populated; RFQ
                // consumers can fall through to UnitCost as the "total".
                double supply = 0, install = 0, vat = 0;
                try { var p = mat.LookupParameter("MAT_COST_SUPPLY_NR");  if (p != null && p.HasValue && p.StorageType == StorageType.Double) supply = p.AsDouble(); }
                catch (Exception ex) { StingLog.Warn($"Rfq supply '{mat.Name}': {ex.Message}"); }
                try { var p = mat.LookupParameter("MAT_COST_INSTALL_NR"); if (p != null && p.HasValue && p.StorageType == StorageType.Double) install = p.AsDouble(); }
                catch (Exception ex) { StingLog.Warn($"Rfq install '{mat.Name}': {ex.Message}"); }
                try { var p = mat.LookupParameter("MAT_VAT_PCT_NR");      if (p != null && p.HasValue && p.StorageType == StorageType.Double) vat = p.AsDouble(); }
                catch (Exception ex) { StingLog.Warn($"Rfq vat '{mat.Name}': {ex.Message}"); }
                if (vat <= 0) vat = 20.0; // UK default; project Region overrides via shared param
                int use = mat.Id != null && usage.TryGetValue(mat.Id.Value, out int u) ? u : 0;

                string epd = "";
                try
                {
                    var ep = mat.LookupParameter("STING_MAT_EPD_SRC_TXT");
                    if (ep != null && ep.StorageType == StorageType.String) epd = ep.AsString() ?? "";
                }
                catch (Exception ex) { StingLog.Warn($"Rfq epd '{mat.Name}': {ex.Message}"); }

                string notes = "";
                if (use == 0)   notes = "Note: zero usage in current model — verify need before quoting.";

                rows.Add(new RfqRow
                {
                    LineRef = $"RFQ-{line:D4}",
                    MaterialName = mat.Name ?? "",
                    Manufacturer = mfr ?? "",
                    Model = model ?? "",
                    Url = url ?? "",
                    UsageCount = use,
                    UnitCost = cost,
                    EstimatedTotal = cost * Math.Max(1, use),
                    Class = mat.MaterialClass ?? "",
                    EpdSource = epd,
                    Notes = notes,
                    SupplyCost = supply,
                    InstallCost = install,
                    VatPct = vat,
                });
                line++;
            }
            return rows;
        }

        public static string WriteCsv(Document doc, IReadOnlyList<RfqRow> rows)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            string projCode = doc?.ProjectInformation?.Number ?? "PRJ";
            string filePath = Path.Combine(outDir,
                $"STING_RFQ_{projCode}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("# STING Material RFQ — auto-generated from project materials with Manufacturer/Model/URL set.");
            sb.AppendLine($"# Project: {doc?.Title}");
            sb.AppendLine($"# Requested by: {Environment.UserName}");
            sb.AppendLine($"# Requested at: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            // A5/G29 — split columns appear after the legacy total so old
            // procurement parsers that read by position keep working.
            sb.AppendLine("LineRef,MaterialName,Manufacturer,Model,URL,UsageCount,UnitCost,EstimatedTotal,Class,EpdSource,Notes,SupplyCost,InstallCost,VatPct,VatAmount,GrossTotal");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",",
                    Q(r.LineRef), Q(r.MaterialName), Q(r.Manufacturer), Q(r.Model), Q(r.Url),
                    r.UsageCount.ToString(ci),
                    r.UnitCost.ToString("F2", ci),
                    r.EstimatedTotal.ToString("F2", ci),
                    Q(r.Class), Q(r.EpdSource), Q(r.Notes),
                    r.SupplyCost.ToString("F2", ci),
                    r.InstallCost.ToString("F2", ci),
                    r.VatPct.ToString("F2", ci),
                    r.VatAmount.ToString("F2", ci),
                    r.GrossTotal.ToString("F2", ci)));
            }
            File.WriteAllText(filePath, sb.ToString());
            StingLog.Info($"MaterialRfqGenerator: wrote {rows.Count} rows → {filePath}");
            return filePath;
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "''") + "\"";

        private static string Read(Material m, BuiltInParameter bip)
        {
            try
            {
                var p = m.get_Parameter(bip);
                if (p != null && p.HasValue && p.StorageType == StorageType.String) return p.AsString();
            }
            catch (Exception ex) { StingLog.Warn($"Rfq read {bip}: {ex.Message}"); }
            return null;
        }
    }
}
