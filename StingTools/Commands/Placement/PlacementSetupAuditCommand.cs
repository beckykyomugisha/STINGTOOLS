// Phase 139.6 — Placement setup audit.
//
// Walks the active document and reports every requirement listed in the
// Phase 139.6 family-authoring + project-setup table:
//   1. Required shared parameters bound to MEP / Lighting / Electrical /
//      Junction Box categories: STING_BOX_LOCATION_ID,
//      STING_NOGGIN_REQUIRED, STING_FIXTURE_VARIANT_TXT, MK_CATALOGUE_REF.
//   2. Critical families loaded: BESA round box, square outlet box,
//      MK Logic Plus / Grid Plus / Metal Clad, sleeves, smoke / heat
//      detector, sprinkler, air terminal.
//   3. Phases needed for two-phase routing: Construction, Handover.
//   4. Catalogue populated (entries > 0).
//   5. STING_PLACEMENT_RULES.json + override path discoverable.
//
// Output: TaskDialog with grouped findings + optional CSV export at
// OutputLocationHelper.GetOutputPath(doc, "PlacementSetupAudit").

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacementSetupAuditCommand : IExternalCommand
    {
        private static readonly string[] RequiredSharedParams = new[]
        {
            ParamRegistry.BOX_LOCATION_ID,
            ParamRegistry.NOGGIN_REQUIRED,
            "STING_FIXTURE_VARIANT_TXT",
            "MK_CATALOGUE_REF",
        };

        private static readonly string[] RequiredFamilyNamePatterns = new[]
        {
            "Conduit_BESA_Round",
            "Conduit_Square_Outlet",
            "MK_LogicPlus_1G_Flush",
            "MK_LogicPlus_2G_Flush",
            "MK_LogicPlus_3G_Flush",
            "MK_GridPlus_4M_Flush",
            "MK_MetalClad_1G_Surface",
            "MK_MetalClad_1G_Weatherproof",
            "MK_JunctionBox_Round",
            "STING_SLEEVE_ROUND",
        };

        private static readonly string[] RequiredCategoriesLoaded = new[]
        {
            "Sprinklers", "Fire Alarm Devices", "Air Terminals", "Lighting Fixtures",
        };

        private static readonly string[] RequiredPhases = new[]
        {
            "Construction", "Handover",
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var findings = new List<AuditFinding>();
            try
            {
                AuditSharedParameters(doc, findings);
                AuditFamilies(doc, findings);
                AuditCategories(doc, findings);
                AuditPhases(doc, findings);
                AuditCatalogue(findings);
                AuditRulePack(doc, findings);
                AuditViewStylePack(findings);
            }
            catch (Exception ex)
            {
                StingLog.Error($"PlacementSetupAudit: {ex.Message}", ex);
                message = ex.Message;
                return Result.Failed;
            }

            // Build report.
            int errs = findings.Count(f => f.Severity == AuditSeverity.Error);
            int warns = findings.Count(f => f.Severity == AuditSeverity.Warning);
            int info = findings.Count(f => f.Severity == AuditSeverity.Info);

            var sb = new StringBuilder();
            sb.AppendLine($"Errors:   {errs}");
            sb.AppendLine($"Warnings: {warns}");
            sb.AppendLine($"Info:     {info}");
            sb.AppendLine();
            foreach (var grp in findings.GroupBy(f => f.Severity).OrderBy(g => g.Key))
            {
                sb.AppendLine($"== {grp.Key.ToString().ToUpperInvariant()} ==");
                foreach (var f in grp.Take(40))
                    sb.AppendLine($"  [{f.Area}] {f.Item} — {f.Message}");
                if (grp.Count() > 40)
                    sb.AppendLine($"  + {grp.Count() - 40} more (see CSV)");
                sb.AppendLine();
            }

            // Always write CSV.
            string csvPath = "";
            try
            {
                string outDir = OutputLocationHelper.GetOutputPath(doc, "PlacementSetupAudit") ?? Path.GetTempPath();
                Directory.CreateDirectory(outDir);
                csvPath = Path.Combine(outDir, $"STING_PlacementSetupAudit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csv = new StringBuilder();
                csv.AppendLine("Severity,Area,Item,Message,Fix");
                foreach (var f in findings)
                    csv.AppendLine($"{f.Severity},{Quote(f.Area)},{Quote(f.Item)},{Quote(f.Message)},{Quote(f.Fix)}");
                File.WriteAllText(csvPath, csv.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"PlacementSetupAudit CSV: {ex.Message}"); }

            TaskDialog.Show("STING - Placement Setup Audit",
                sb.ToString() + (string.IsNullOrEmpty(csvPath) ? "" : $"\nCSV: {csvPath}"));
            return Result.Succeeded;
        }

        // ── Audit passes ───────────────────────────────────────────

        private static void AuditSharedParameters(Document doc, List<AuditFinding> findings)
        {
            var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)))
                    if (el is SharedParameterElement spe && !string.IsNullOrEmpty(spe.Name))
                        bound.Add(spe.Name);
            }
            catch { }

            foreach (var p in RequiredSharedParams)
            {
                if (!bound.Contains(p))
                    findings.Add(new AuditFinding(AuditSeverity.Error, "SharedParam", p,
                        "Not bound. Phase 139.x rules will fall to degraded mode for this parameter.",
                        $"Bind '{p}' via Manage > Shared Parameters or run the Tags > Load Shared Parameters command."));
            }
        }

        private static void AuditFamilies(Document doc, List<AuditFinding> findings)
        {
            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                    if (el is Family fam && !string.IsNullOrEmpty(fam.Name)) loaded.Add(fam.Name);
            }
            catch { }
            foreach (var pat in RequiredFamilyNamePatterns)
            {
                if (!loaded.Contains(pat))
                    findings.Add(new AuditFinding(AuditSeverity.Warning, "Family", pat,
                        "Not loaded. Rules referencing this family will warn 'no first-fix box family matched'.",
                        $"Load '{pat}.rfa' via Insert > Load Family or drop into Families/ directory."));
            }
        }

        private static void AuditCategories(Document doc, List<AuditFinding> findings)
        {
            try
            {
                var byName = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Sprinklers",         BuiltInCategory.OST_Sprinklers },
                    { "Fire Alarm Devices", BuiltInCategory.OST_FireAlarmDevices },
                    { "Air Terminals",      BuiltInCategory.OST_DuctTerminal },
                    { "Lighting Fixtures",  BuiltInCategory.OST_LightingFixtures },
                };
                foreach (var name in RequiredCategoriesLoaded)
                {
                    if (!byName.TryGetValue(name, out var bic)) continue;
                    bool any = false;
                    foreach (var el in new FilteredElementCollector(doc).OfCategory(bic).OfClass(typeof(FamilySymbol)))
                    { any = true; break; }
                    if (!any)
                        findings.Add(new AuditFinding(AuditSeverity.Warning, "Category", name,
                            "No FamilySymbol loaded for this category. Rules will warn 'No FamilySymbol found'.",
                            $"Load at least one '{name}' family before running Place Fixtures."));
                }
            }
            catch (Exception ex) { findings.Add(new AuditFinding(AuditSeverity.Info, "Category", "(scan)", ex.Message, "")); }
        }

        private static void AuditPhases(Document doc, List<AuditFinding> findings)
        {
            try
            {
                var phaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(Phase)))
                    if (el is Phase p && !string.IsNullOrEmpty(p.Name)) phaseNames.Add(p.Name);
                foreach (var ph in RequiredPhases)
                {
                    if (!phaseNames.Contains(ph))
                        findings.Add(new AuditFinding(AuditSeverity.Warning, "Phase", ph,
                            "Phase not present. Two-phase routing assumes 'Construction' (first-fix) + 'Handover' (second-fix).",
                            $"Manage > Phases > New: '{ph}'."));
                }
            }
            catch (Exception ex) { findings.Add(new AuditFinding(AuditSeverity.Info, "Phase", "(scan)", ex.Message, "")); }
        }

        private static void AuditCatalogue(List<AuditFinding> findings)
        {
            try
            {
                int n = ManufacturerCatalogueRegistry.All?.Count ?? 0;
                if (n == 0)
                    findings.Add(new AuditFinding(AuditSeverity.Warning, "Catalogue", "(empty)",
                        "ManufacturerCatalogueRegistry has zero entries. ScoreManufacturerResolution will return 0 for every rule.",
                        "Run Placement_AutoPopulateCatalogue or ship STING_MANUFACTURER_CATALOGUE.json with the deployment."));
                else
                    findings.Add(new AuditFinding(AuditSeverity.Info, "Catalogue", "(populated)",
                        $"{n} catalogue entries available.", ""));
            }
            catch (Exception ex) { findings.Add(new AuditFinding(AuditSeverity.Info, "Catalogue", "(scan)", ex.Message, "")); }
        }

        private static void AuditRulePack(Document doc, List<AuditFinding> findings)
        {
            try
            {
                var rules = PlacementRuleLoader.Load(doc.PathName);
                int n = rules?.Count ?? 0;
                findings.Add(new AuditFinding(
                    n > 0 ? AuditSeverity.Info : AuditSeverity.Error,
                    "RulePack", "(load)",
                    $"{n} rules loaded.",
                    n > 0 ? "" : "Place STING_PLACEMENT_RULES.json + per-discipline packs in the data folder."));
            }
            catch (Exception ex) { findings.Add(new AuditFinding(AuditSeverity.Error, "RulePack", "(load)", ex.Message, "")); }
        }

        private static void AuditViewStylePack(List<AuditFinding> findings)
        {
            try
            {
                string p = StingToolsApp.FindDataFile("STING_VIEW_STYLE_PACKS.json");
                if (string.IsNullOrEmpty(p) || !File.Exists(p))
                    findings.Add(new AuditFinding(AuditSeverity.Info, "ViewStyle", "STING_VIEW_STYLE_PACKS.json",
                        "Pack not found.",
                        "Optional. Required only if you use the Drawing Type / View Style Pack engine."));
                else
                    findings.Add(new AuditFinding(AuditSeverity.Info, "ViewStyle", "(present)",
                        $"View-style pack found at {p}.", ""));
            }
            catch { }
        }

        private static string Quote(string s)
        {
            if (s == null) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private enum AuditSeverity { Error, Warning, Info }

        private struct AuditFinding
        {
            public AuditSeverity Severity;
            public string Area, Item, Message, Fix;
            public AuditFinding(AuditSeverity s, string a, string i, string m, string fx)
            { Severity = s; Area = a; Item = i; Message = m; Fix = fx; }
        }
    }
}
