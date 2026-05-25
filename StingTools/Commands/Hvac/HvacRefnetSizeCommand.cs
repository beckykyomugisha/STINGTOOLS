// StingTools — REFNET joint sizer.
//
// Phase 187h. Walks every refrigerant IDU in the project (filtered
// by HVC_CAPACITY_KW + family-name "IDU" / "FCU" / "indoor" / "ducted"),
// builds a one-trunk-many-branches tree against the active vendor's
// REFNET joint catalogue, and reports the per-branch joint selection.
//
// Tree topology shipped here is the simple "trunk → joint per IDU →
// IDU" pattern — the dominant VRF arrangement. Multi-level branching
// (joint → sub-joint → IDUs) requires extracting the actual connector
// graph; left as a future enhancement.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Refrigerant;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRefnetSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;
                var pi = doc.ProjectInformation;
                string vendorSeries = pi?.LookupParameter("PRJ_REFRIG_VENDOR_SERIES_TXT")?.AsString();
                if (string.IsNullOrWhiteSpace(vendorSeries))
                {
                    TaskDialog.Show("STING HVAC — REFNET Size",
                        "Set PRJ_REFRIG_VENDOR_SERIES_TXT on Project Information first.");
                    return Result.Cancelled;
                }
                var cat = RefnetRegistry.Get(doc);
                if (cat.Joints.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — REFNET Size",
                        "No REFNET joints in STING_REFNET_JOINTS.json.");
                    return Result.Cancelled;
                }

                var ius = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(IsRefrigIdu)
                    .ToList();
                if (ius.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — REFNET Size",
                        "No refrigerant indoor units found (filter: IDU / FCU / Indoor / Ducted / Cassette).");
                    return Result.Cancelled;
                }

                // Build a synthetic single-trunk tree: root (ODU) → one
                // joint per IDU → IDU leaf. Real connector-graph extraction
                // would build a deeper tree; this is the dominant
                // small-to-medium-system layout.
                var root = new RefrigTreeNode { Id = "ODU", Label = "Outdoor Unit" };
                int idx = 0;
                foreach (var idu in ius)
                {
                    double capKw = ReadDouble(idu, "HVC_CAPACITY_KW");
                    if (capKw <= 0) continue;
                    var leaf = new RefrigTreeNode
                    {
                        Id         = $"IDU-{idx++}",
                        Label      = idu.LookupParameter("ASS_TAG_1")?.AsString()
                                     ?? $"#{idu.Id.Value} {idu.Symbol?.Family?.Name}",
                        CapacityKw = capKw
                    };
                    // One-joint-per-IDU wrapper so the sizer picks a joint
                    // for each branch (matches Daikin REFNET joint-per-IDU
                    // installation guide for systems with shared trunk).
                    var branchNode = new RefrigTreeNode
                    {
                        Id    = $"J-{idx-1}",
                        Label = $"Branch joint to {leaf.Label}"
                    };
                    branchNode.Children.Add(leaf);
                    root.Children.Add(branchNode);
                }

                var result = RefnetTreeSizer.SizeTree(root, vendorSeries, cat);

                var panel = StingResultPanel.Create("HVAC — REFNET Branch Sizing");
                panel.SetSubtitle($"vendor={vendorSeries} · {ius.Count} IDUs · system {result.TotalSystemKw:F1} kW");
                panel.AddSection("SUMMARY")
                     .Metric("Joints sized",        result.JointsSized.ToString())
                     .Metric("Joints over-capacity",result.JointsMissed.ToString())
                     .Metric("System total",        $"{result.TotalSystemKw:F1} kW");

                if (result.Warnings.Count > 0)
                {
                    panel.AddSection("WARNINGS");
                    foreach (var w in result.Warnings) panel.Text("⚠ " + w);
                }

                panel.AddSection("PER-BRANCH SELECTION");
                foreach (var (id, kw, jointId) in result.Trace.Take(60))
                    panel.Text($"  {id}: downstream {kw,5:F1} kW → {jointId ?? "(NO MATCH)"}");

                panel.Text("Synthetic single-trunk-many-branches topology. " +
                           "Full multi-level extraction from the Revit connector graph " +
                           "is a future enhancement. Joint sizing per vendor REFNET " +
                           "table — first joint whose maxKw ≥ downstream-connected wins.");
                panel.Show();
                try { StingHvacPanel.Instance?.PushRunRow($"REFNET sizing ({result.JointsSized} joints)", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRefnetSizeCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool IsRefrigIdu(FamilyInstance fi)
        {
            string s = ($"{fi.Symbol?.Family?.Name} {fi.Symbol?.Name} {fi.Name}").ToLowerInvariant();
            return s.Contains("idu") || s.Contains("indoor") || s.Contains("fcu") ||
                   s.Contains("ducted") || s.Contains("cassette") || s.Contains("vrv") || s.Contains("vrf");
        }

        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
