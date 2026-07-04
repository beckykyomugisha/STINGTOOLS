// ══════════════════════════════════════════════════════════════════════════
//  BoqErpExporter.cs — Phase 2E. Flat, import-ready ERP cost export.
//
//  Writes the bill in the union shape most ERP / accounting importers accept
//  (SAP PS / Oracle Primavera Unifier / QuickBooks / Sage): one flat CSV row
//  per line with WBS · CBS · cost code · description · qty · unit · rate · total
//  · currency · level · location · source · element/IFC id. Optionally also a
//  Primavera P6-style activity-cost XML (one activity per WBS group, with an
//  expense item carrying the cost). Pure — no Revit API — so it runs on the WPF
//  thread straight off the in-memory BOQDocument.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using StingTools.Core;

namespace StingTools.BOQ
{
    internal static class BoqErpExporter
    {
        /// <summary>Flat ERP cost-import CSV. Returns the path written.</summary>
        public static string ExportCsv(BOQDocument boq, string path)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", new[]
            {
                "WBS", "CBS", "CostCode", "Description", "Qty", "Unit", "UnitRate",
                "Total", "Currency", "Level", "Location", "Source", "ElementId"
            }));

            foreach (var i in boq.AllItems.OrderBy(x => x.WbsCode ?? "", StringComparer.OrdinalIgnoreCase)
                                          .ThenBy(x => x.BOQLineRef ?? "", StringComparer.OrdinalIgnoreCase))
            {
                string elemRef = !string.IsNullOrEmpty(i.IfcGlobalId)
                    ? i.IfcGlobalId
                    : (i.RevitElementId > 0 ? i.RevitElementId.ToString(CultureInfo.InvariantCulture) : "");
                string desc = !string.IsNullOrEmpty(i.ResolvedNRM2Paragraph) ? i.ResolvedNRM2Paragraph : (i.ItemName ?? "");
                string source = SourceLabel(i);
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(i.WbsCode), Q(i.CbsCode), Q(i.NRM2Section), Q(desc),
                    i.Quantity.ToString("0.######", CultureInfo.InvariantCulture),
                    Q(i.Unit),
                    i.RateUGX.ToString("0.##", CultureInfo.InvariantCulture),
                    i.TotalUGX.ToString("0.##", CultureInfo.InvariantCulture),
                    "UGX", Q(i.Level), Q(i.Location), Q(source), Q(elemRef)
                }));
            }

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"BOQ ERP CSV exported: {Path.GetFileName(path)} ({boq.AllItems.Count} rows).");
            return path;
        }

        /// <summary>
        /// Primavera P6-style activity-cost XML — one WBS node + one Activity per
        /// distinct WBS code (lines with no WBS fall under "UNASSIGNED"), each
        /// with an ExpenseItem carrying the grouped cost. Returns the path.
        /// </summary>
        public static string ExportP6Xml(BOQDocument boq, string path)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));

            var groups = boq.AllItems
                .GroupBy(i => string.IsNullOrEmpty(i.WbsCode) ? "UNASSIGNED" : i.WbsCode)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string projCode = string.IsNullOrEmpty(boq.ProjectName) ? "PROJECT" : boq.ProjectName;
            var root = new XElement("APIBusinessObjects");
            var project = new XElement("Project",
                new XElement("Id", Sanitise(projCode)),
                new XElement("Name", projCode),
                new XElement("Currency", "UGX"));

            int actSeq = 1;
            foreach (var g in groups)
            {
                string wbs = g.Key;
                double total = g.Sum(i => i.TotalUGX);
                project.Add(new XElement("WBS",
                    new XElement("Code", wbs),
                    new XElement("Name", wbs)));
                project.Add(new XElement("Activity",
                    new XElement("Id", $"A{actSeq:D4}"),
                    new XElement("Name", $"WBS {wbs}"),
                    new XElement("WBSCode", wbs),
                    new XElement("ExpenseItem",
                        new XElement("ExpenseCategory", "Construction"),
                        new XElement("PricePerUnit", total.ToString("0.##", CultureInfo.InvariantCulture)),
                        new XElement("PlannedUnits", "1"),
                        new XElement("PlannedCost", total.ToString("0.##", CultureInfo.InvariantCulture)),
                        new XElement("Currency", "UGX"))));
                actSeq++;
            }
            root.Add(project);

            new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(path);
            StingLog.Info($"BOQ P6 XML exported: {Path.GetFileName(path)} ({groups.Count} WBS activity group(s)).");
            return path;
        }

        /// <summary>Project the bill into accounting posting rows — one per BOQ
        /// line, the cost account taken from the NRM2 section, the class from the
        /// WBS/CBS, the memo from the description.</summary>
        private static List<ErpRow> ToErpRows(BOQDocument boq)
        {
            var rows = new List<ErpRow>();
            foreach (var i in boq.AllItems)
            {
                if (Math.Abs(i.TotalUGX) < 0.005) continue;
                string desc = !string.IsNullOrEmpty(i.ResolvedNRM2Paragraph) ? i.ResolvedNRM2Paragraph : (i.ItemName ?? "");
                rows.Add(new ErpRow
                {
                    CostAccount = string.IsNullOrEmpty(i.NRM2Section) ? "Construction Costs" : $"Construction:{i.NRM2Section}",
                    ClassName = !string.IsNullOrEmpty(i.CbsCode) ? i.CbsCode : (i.WbsCode ?? ""),
                    Memo = desc,
                    Amount = i.TotalUGX,
                    DocNum = i.BOQLineRef ?? "",
                });
            }
            return rows;
        }

        /// <summary>QuickBooks IIF general journal of the bill. Returns the path.</summary>
        public static string ExportIif(BOQDocument boq, string path, DateTime date)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            File.WriteAllText(path, ErpFormats.BuildIif(ToErpRows(boq), date,
                docNum: string.IsNullOrEmpty(boq.ProjectName) ? "BOQ" : boq.ProjectName));
            StingLog.Info($"BOQ QuickBooks IIF exported: {Path.GetFileName(path)}.");
            return path;
        }

        /// <summary>Sage 50 nominal-journal CSV of the bill. Returns the path.</summary>
        public static string ExportSageCsv(BOQDocument boq, string path, DateTime date)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            File.WriteAllText(path, ErpFormats.BuildSageCsv(ToErpRows(boq), date,
                reference: string.IsNullOrEmpty(boq.ProjectName) ? "BOQ" : boq.ProjectName));
            StingLog.Info($"BOQ Sage CSV exported: {Path.GetFileName(path)}.");
            return path;
        }

        private static string SourceLabel(BOQLineItem i)
        {
            string s = BoqSourceUtil.Label(i.Source);
            return string.IsNullOrWhiteSpace(i.SourceModel) ? s : $"{s} [{i.SourceModel}]";
        }

        // CSV field quote/escape.
        private static string Q(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "PROJECT";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
