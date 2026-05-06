// STING Tools — Standards compliance command.
//
// Bridges Revit elements to the StandardsComplianceEngine. Rooms +
// doors + corridors are converted to DesignElement records and run
// through the engine's CheckBatchCompliance against the rules loaded
// from data\AEC_COMPLIANCE_RULES.csv (optional). Results are
// rendered through StingResultPanel grouped by violation severity.
//
// The engine is region-aware via ProjectStandardsManager — only rules
// whose Region matches the active project region (or are global) run.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Standards;
using StingTools.Standards.Compliance;
using StingTools.UI;

namespace StingTools.Commands.StandardsExt
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunStandardsComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var engine = new StandardsComplianceEngine();

            // Optional CSV — projects can drop their rule pack alongside the DLL
            // (data\AEC_COMPLIANCE_RULES.csv). When absent, we bootstrap a small
            // demo pack so the command produces a meaningful result on first install.
            try
            {
                var csv = StingToolsApp.FindDataFile("AEC_COMPLIANCE_RULES.csv");
                if (!string.IsNullOrEmpty(csv) && File.Exists(csv))
                    engine.LoadFromCsv(csv);
                else
                    SeedDefaultRules(engine);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Compliance rule load failed, seeding defaults: {ex.Message}");
                SeedDefaultRules(engine);
            }

            string region = ProjectStandardsManager.Instance.Region ?? "International";
            List<DesignElement> bag;
            try { bag = CollectElements(ctx.Doc); }
            catch (Exception ex) { StingLog.Error("Compliance collect", ex); message = ex.Message; return Result.Failed; }

            if (bag.Count == 0)
            {
                TaskDialog.Show("STING Standards Compliance",
                    "No rooms or doors found in the active document — nothing to validate.");
                return Result.Cancelled;
            }

            BatchComplianceResult batch;
            try { batch = engine.CheckBatchCompliance(bag, region); }
            catch (Exception ex) { StingLog.Error("Compliance run", ex); message = ex.Message; return Result.Failed; }

            // Render — overall RAG bar + violations + warnings
            int violations = batch.ElementResults.Sum(r => r.Violations.Count);
            int warnings   = batch.ElementResults.Sum(r => r.Warnings.Count);
            double scorePct = batch.OverallComplianceScore * 100.0;

            var panel = StingResultPanel.Create("Standards compliance audit")
                .SetSubtitle($"{region} · {batch.TotalElements} elements · {engine.GetEnabledStandards().Count()} standards");
            panel.SetOverallPct(scorePct);

            panel.AddSection("SUMMARY")
                 .Metric("Compliant elements", $"{batch.CompliantElements} / {batch.TotalElements}")
                 .Metric("Overall score",      $"{scorePct:F1}%")
                 .Metric("Critical violations", violations.ToString())
                 .Metric("Warnings",            warnings.ToString())
                 .Metric("Region",              region);

            if (violations > 0)
            {
                panel.AddSection("VIOLATIONS");
                foreach (var er in batch.ElementResults.Where(r => r.Violations.Any()).Take(40))
                {
                    foreach (var v in er.Violations)
                        panel.MetricError(
                            $"{er.ElementType} {er.ElementId}",
                            v.RuleName,
                            $"{v.StandardCode}: {v.Message}");
                }
            }

            if (warnings > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var er in batch.ElementResults.Where(r => r.Warnings.Any()).Take(40))
                {
                    foreach (var w in er.Warnings)
                        panel.MetricWarn(
                            $"{er.ElementType} {er.ElementId}",
                            w.RuleName,
                            $"{w.StandardCode}: {w.Message}");
                }
            }

            panel.Show();
            return Result.Succeeded;
        }

        // Builds DesignElement records for rooms (area, occupancy) and doors
        // (clear width, fire rating). Extend by category as more rule packs land.
        private static List<DesignElement> CollectElements(Document doc)
        {
            var bag = new List<DesignElement>();

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r.Area > 0);

            foreach (var r in rooms)
            {
                var de = new DesignElement
                {
                    Id = r.Id.ToString(),
                    Type = "Room",
                    Category = "Spatial",
                    Properties = new Dictionary<string, object>
                    {
                        ["Name"]      = r.Name ?? string.Empty,
                        ["AreaM2"]    = r.Area * 0.092903,         // ft² → m²
                        ["AreaSqFt"]  = r.Area,
                        ["Number"]    = r.Number ?? string.Empty,
                        ["Department"]= ParameterHelpers.GetString(r, "Department") ?? string.Empty,
                    }
                };
                bag.Add(de);
            }

            var doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>();

            foreach (var d in doors)
            {
                // Door Width / Height are normally TYPE parameters. LookupParameter
                // on an instance does NOT walk through to the family symbol, so
                // we fall back to the symbol when the instance returns null/0.
                double widthFt = ReadDoubleWithTypeFallback(doc, d, "Width");
                double heightFt = ReadDoubleWithTypeFallback(doc, d, "Height");
                string fire = ParameterHelpers.GetString(d, "Fire Rating");
                if (string.IsNullOrEmpty(fire))
                {
                    var symbol = doc.GetElement(d.GetTypeId()) as ElementType;
                    if (symbol != null) fire = ParameterHelpers.GetString(symbol, "Fire Rating");
                }
                bag.Add(new DesignElement
                {
                    Id = d.Id.ToString(),
                    Type = "Door",
                    Category = "Egress",
                    Properties = new Dictionary<string, object>
                    {
                        ["WidthMM"]    = widthFt * 304.8,
                        ["HeightMM"]   = heightFt * 304.8,
                        ["FireRating"] = fire ?? string.Empty
                    }
                });
            }

            return bag;
        }

        // Read a numeric parameter from an instance, falling back to the
        // associated FamilySymbol when the instance value is null or zero.
        // Door / window dimensions are nearly always type-bound.
        private static double ReadDoubleWithTypeFallback(Document doc, FamilyInstance fi, string name)
        {
            double v = fi.LookupParameter(name)?.AsDouble() ?? 0.0;
            if (v > 0) return v;
            var symbol = doc.GetElement(fi.GetTypeId()) as ElementType;
            return symbol?.LookupParameter(name)?.AsDouble() ?? 0.0;
        }

        // Default rule pack — fires when no CSV is shipped. Mirrors a minimal
        // BS 8300 + Approved Doc M + ISO 19650 baseline. Replace by dropping
        // a real AEC_COMPLIANCE_RULES.csv next to the DLL.
        private static void SeedDefaultRules(StandardsComplianceEngine engine)
        {
            void AddRule(string id, string code, string name, RuleSeverity sev,
                         string applicableType, string condition, string required, string unit)
            {
                var rule = new ComplianceRule
                {
                    Id = id,
                    StandardCode = code,
                    Category = applicableType,
                    Name = name,
                    Description = name,
                    Condition = condition,
                    RequiredValue = required,
                    Unit = unit,
                    Severity = sev,
                    Region = "International"
                };
                rule.ApplicableTypes.Add(applicableType);
                engine.AddRule(rule);
            }

            AddRule("BS8300-DOOR-WIDTH",   "BS8300",      "Door clear width ≥ 800 mm",     RuleSeverity.Critical, "Door", "WidthMM>=800",  "800",  "mm");
            AddRule("ADM-DOOR-WIDTH",      "ApprovedDocM","Door clear width ≥ 850 mm (ADA)", RuleSeverity.Major,    "Door", "WidthMM>=850",  "850",  "mm");
            AddRule("BS8300-DOOR-HEIGHT",  "BS8300",      "Door clear height ≥ 2000 mm",   RuleSeverity.Major,    "Door", "HeightMM>=2000", "2000", "mm");
            AddRule("ROOM-AREA-MIN",       "BS6465",      "Room area > 0",                  RuleSeverity.Critical, "Room", "AreaM2>0",       ">0",   "m²");

            engine.EnableStandard("BS8300");
            engine.EnableStandard("ApprovedDocM");
            engine.EnableStandard("BS6465");
        }
    }
}
