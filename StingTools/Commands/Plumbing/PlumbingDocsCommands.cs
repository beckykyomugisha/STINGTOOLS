// PlumbingDocsCommands — Phase 179f DOCS tab.
//
// Plumb_PipeSchedule    — pipe schedule grouped by system + DN.
// Plumb_BOQ             — full plumbing BOQ via PlumbingBOQBuilder.
// Plumb_ManholeSchedule — placeholder schedule from PLM_DRN_INV_* params.
// Plumb_Isometric       — drafting-view stub: notes drawing-type routing.
// Plumb_CommPack        — generates commissioning shell file index.

using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.RegularExpressions;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;
using StingTools.UI.Plumbing;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbPipeScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Same delegation as Plumb_BOQ — main NRM2 pipeline first, fall
            // back to PlumbingBOQBuilder when it can't run.
            BOQDocument boq = null;
            try { boq = BOQCostManager.BuildBOQDocument(ctx.Doc); }
            catch (Exception ex) { StingLog.Warn("PlumbPipeSchedule: main BOQ build failed, falling back: " + ex.Message); }

            // Pipe schedule deliberately excludes the enricher's sleeve/hanger
            // rows (those aren't pipe runs). Insulation rows are also excluded
            // by IsPipeRun's "no INSULATION in category" rule, so calling the
            // enricher here is unnecessary — leave it to PlumbBOQCommand.

            List<BOQLineItem> pipeItems = null;
            if (boq != null)
            {
                pipeItems = boq.AllItems
                    .Where(PlumbBOQCommand.IsPlumbingItem)
                    .Where(IsPipeRun)
                    .ToList();
            }

            var inst = StingPlumbingPanel.Instance;
            if (pipeItems != null && pipeItems.Count > 0)
            {
                var rows = pipeItems
                    .OrderBy(i => i.NRM2Section).ThenBy(i => i.SortOrder)
                    .Select(i => new DocsPipeScheduleRow
                    {
                        System   = ExtractSystem(i),
                        Dn       = ExtractDn(i),
                        Material = string.IsNullOrEmpty(i.FamilyName) ? PlumbBOQCommand.ComposeDescription(i) : i.FamilyName,
                        LengthM  = i.Quantity
                    }).ToList();
                double totalLength = pipeItems.Sum(i => i.Quantity);
                string status = $"Pipe schedule · NRM2 · {pipeItems.Count} runs · {totalLength:F1} m total";
                if (inst != null) { inst.SetDocsPipeScheduleResult(rows, status); return Result.Succeeded; }

                var panel = StingResultPanel.Create("Plumbing Pipe Schedule (NRM2)");
                panel.AddSection("SUMMARY")
                     .Metric("Runs",             pipeItems.Count.ToString())
                     .Metric("Total length (m)", totalLength.ToString("F1"));
                panel.AddSection("ROWS (first 80)");
                foreach (var i in pipeItems.Take(80))
                    panel.Text($"{i.BOQLineRef ?? i.NRM2Section} · {PlumbBOQCommand.ComposeDescription(i),-50} · {i.Quantity,8:F1} m");
                panel.Show();
                return Result.Succeeded;
            }

            // Fallback to the legacy plumbing-only builder.
            var b = PlumbingBOQBuilder.Build(ctx.Doc);
            var pipeRows = b.Rows.Where(r => r.Unit == "m").ToList();
            var fbRows = pipeRows.Select(r =>
            {
                ParseDescription(r.Description, out var system, out var dn, out var material);
                return new DocsPipeScheduleRow
                {
                    System   = system,
                    Dn       = dn,
                    Material = string.IsNullOrEmpty(material) ? r.Description : material,
                    LengthM  = r.Qty
                };
            }).ToList();
            string fbStatus = $"Pipe schedule (legacy) · {b.PipesCounted} pipes · "
                            + $"{pipeRows.Count} rows · {pipeRows.Sum(r => r.Qty):F1} m total";
            if (inst != null) { inst.SetDocsPipeScheduleResult(fbRows, fbStatus); return Result.Succeeded; }

            var fbPanel = StingResultPanel.Create("Plumbing Pipe Schedule (legacy fallback)");
            fbPanel.AddSection("SUMMARY")
                 .Metric("Pipes counted", b.PipesCounted.ToString())
                 .Metric("Distinct rows", pipeRows.Count.ToString())
                 .Metric("Total length (m)", pipeRows.Sum(r => r.Qty).ToString("F1"));
            if (pipeRows.Any())
            {
                fbPanel.AddSection("ROWS (first 80)");
                foreach (var row in pipeRows.Take(80))
                    fbPanel.Text($"{row.Code} · {row.Description,-50} · {row.Qty,8:F1} {row.Unit}");
            }
            fbPanel.Show();
            return Result.Succeeded;
        }

        private static bool IsPipeRun(BOQLineItem i)
        {
            if (i == null) return false;
            if (i.Unit != "m") return false;                          // pipe lengths only
            var c = (i.Category ?? "").ToUpperInvariant();
            return c.Contains("PIPE")
                && !c.Contains("FITTING")
                && !c.Contains("ACCESSORY")
                && !c.Contains("INSULATION");
        }

        // System code comes from BOQLineItem.Location (room / spatial code)
        // when set; fall back to parens parsed out of TypeName / ItemName.
        private static string ExtractSystem(BOQLineItem i)
        {
            if (!string.IsNullOrWhiteSpace(i.Location)) return i.Location;
            var src = i.TypeName ?? i.ItemName ?? "";
            var m = Regex.Match(src, @"\((?<sys>[^)]+)\)\s*$");
            return m.Success ? m.Groups["sys"].Value.Trim() : "";
        }

        // DN parsed from TypeName / ItemName / NRM2 paragraph: any of those may
        // carry "DN{n}" depending on the rate source.
        private static int ExtractDn(BOQLineItem i)
        {
            foreach (var src in new[] { i.TypeName, i.ItemName, i.ResolvedNRM2Paragraph })
            {
                if (string.IsNullOrEmpty(src)) continue;
                var m = Regex.Match(src, @"DN\s*(?<dn>\d+)", RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups["dn"].Value, out var n)) return n;
            }
            return 0;
        }

        // Legacy fallback parser for PlumbingBOQBuilder description shape:
        // "{material} pipe DN{size} ({system N})".
        private static void ParseDescription(string desc, out string system, out int dn, out string material)
        {
            system = ""; dn = 0; material = "";
            if (string.IsNullOrEmpty(desc)) return;
            var m = Regex.Match(desc, @"^(?<mat>.*?)\s*pipe\s*DN(?<dn>\d+)\s*\((?<sys>[^)]+)\)\s*$",
                                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                material = m.Groups["mat"].Value.Trim();
                int.TryParse(m.Groups["dn"].Value, out dn);
                system   = m.Groups["sys"].Value.Trim();
                return;
            }
            var m2 = Regex.Match(desc, @"DN(?<dn>\d+)", RegexOptions.IgnoreCase);
            if (m2.Success) int.TryParse(m2.Groups["dn"].Value, out dn);
            material = desc;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbBOQCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Delegate to the main NRM2 BOQ pipeline so plumbing rows carry
            // NRM2 section + paragraph, rate-source classification and snapshot
            // continuity. The plumbing-specific PlumbingBOQBuilder remains as a
            // fallback when the main pipeline can't run (e.g. missing rate
            // tables) so the panel always shows something.
            BOQDocument boq = null;
            try { boq = BOQCostManager.BuildBOQDocument(ctx.Doc); }
            catch (Exception ex) { StingLog.Warn("PlumbBOQ: main BOQ build failed, falling back: " + ex.Message); }

            List<BOQLineItem> plumbingItems = null;
            if (boq != null)
            {
                plumbingItems = boq.AllItems.Where(IsPlumbingItem).ToList();
            }
            if (plumbingItems == null) plumbingItems = new List<BOQLineItem>();

            // Supplemental rows the main pipeline doesn't cover for plumbing
            // scope (insulation, sleeves, hangers). The exclude-set carries
            // every Revit id already counted by the main BOQ so projects that
            // model sleeves under OST_PipeAccessory (which the main pipeline
            // does collect) don't get double-rated by the enricher.
            var excludeIds = new HashSet<long>(plumbingItems
                .Where(i => i.RevitElementId > 0)
                .Select(i => i.RevitElementId));
            plumbingItems.AddRange(PlumbingBOQEnricher.Build(ctx.Doc, excludeIds));

            var inst = StingPlumbingPanel.Instance;
            if (plumbingItems.Count > 0)
            {
                var rows = plumbingItems
                    .OrderBy(i => i.NRM2Section).ThenBy(i => i.SortOrder)
                    .Select(i => new DocsBoqRow
                    {
                        Item        = string.IsNullOrEmpty(i.BOQLineRef) ? i.NRM2Section : i.BOQLineRef,
                        Description = ComposeDescription(i),
                        Qty         = i.Quantity,
                        Unit        = i.Unit
                    }).ToList();
                int sections   = plumbingItems.Select(i => i.NRM2Section).Distinct().Count();
                double totalUgx = plumbingItems.Sum(i => i.TotalUGX);
                string status   = $"BOQ · NRM2 · {plumbingItems.Count} rows across {sections} sections · "
                                + $"UGX {totalUgx:N0}";
                if (inst != null) { inst.SetDocsBoqResult(rows, status); return Result.Succeeded; }

                var panel = StingResultPanel.Create("Plumbing BOQ (NRM2)");
                panel.SetSubtitle($"Source: BOQCostManager · {sections} sections · {plumbingItems.Count} items");
                panel.AddSection("SUMMARY")
                     .Metric("Items",          plumbingItems.Count.ToString())
                     .Metric("Sections",       sections.ToString())
                     .Metric("Total UGX",      totalUgx.ToString("N0"))
                     .Metric("Total USD",      plumbingItems.Sum(i => i.TotalUSD).ToString("N0"));
                panel.AddSection("ITEMS (first 100)");
                foreach (var i in plumbingItems.Take(100))
                    panel.Text($"{i.BOQLineRef ?? i.NRM2Section} · {ComposeDescription(i),-50} · {i.Quantity,8:F2} {i.Unit}");
                panel.Show();
                return Result.Succeeded;
            }

            // Fallback: legacy PlumbingBOQBuilder (no NRM2 phrasing). Used when
            // BOQCostManager couldn't build (e.g. missing cost tables on a fresh
            // project) so the panel still shows the plumbing inventory.
            var b = PlumbingBOQBuilder.Build(ctx.Doc);
            var fbRows = b.Rows.Select(r => new DocsBoqRow
            {
                Item        = r.Code,
                Description = r.Description,
                Qty         = r.Qty,
                Unit        = r.Unit
            }).ToList();
            string fbStatus = $"BOQ (legacy) · {b.PipesCounted} pipes · {b.FittingsCounted} fittings · "
                            + $"{b.AccessoriesCounted} accessories · {b.Rows.Count} rows";
            if (inst != null) { inst.SetDocsBoqResult(fbRows, fbStatus); return Result.Succeeded; }

            var fbPanel = StingResultPanel.Create("Plumbing BOQ (legacy fallback)");
            fbPanel.AddSection("SUMMARY")
                 .Metric("Pipes",        b.PipesCounted.ToString())
                 .Metric("Fittings",     b.FittingsCounted.ToString())
                 .Metric("Accessories",  b.AccessoriesCounted.ToString())
                 .Metric("Total rows",   b.Rows.Count.ToString());
            if (b.Rows.Any())
            {
                fbPanel.AddSection("BOQ ROWS (first 100)");
                foreach (var r in b.Rows.Take(100))
                    fbPanel.Text($"{r.Code} · {r.Description,-46} · {r.Qty,8:F2} {r.Unit}");
            }
            if (b.Warnings.Any())
            {
                fbPanel.AddSection("WARNINGS");
                foreach (var w in b.Warnings.Take(20)) fbPanel.Text(w);
            }
            fbPanel.Show();
            return Result.Succeeded;
        }

        // ── Plumbing slice of the main BOQ ─────────────────────────────
        // We trust the Discipline field first (BOQCostManager assigns "P" for
        // plumbing); fall back to the Revit category names so legacy snapshots
        // and partially-classified models still surface here. STING-emitted
        // sleeves and hangers usually live in OST_GenericModel — catch them by
        // family-name prefix so they appear alongside the rest of the plumbing
        // scope when the Phase 179f BOQ Enricher hasn't already injected them.
        internal static bool IsPlumbingItem(BOQLineItem i)
        {
            if (i == null) return false;
            if (string.Equals(i.Discipline, "P", StringComparison.OrdinalIgnoreCase)) return true;
            var c = (i.Category ?? "").ToUpperInvariant();
            if (c.Contains("PIPE") || c.Contains("PLUMBING") || c.Contains("INSULATION")) return true;
            var f = (i.FamilyName ?? "").ToUpperInvariant();
            if (f.StartsWith("STING_SLEEVE_")  ||
                f.StartsWith("STING_HANGER_")  ||
                f.StartsWith("STING_TRAPEZE_") ||
                f.StartsWith("STING_PROVISION_VOID")) return true;
            return false;
        }

        internal static string ComposeDescription(BOQLineItem i)
        {
            if (!string.IsNullOrWhiteSpace(i.ResolvedNRM2Paragraph)) return i.ResolvedNRM2Paragraph;
            if (!string.IsNullOrWhiteSpace(i.ItemName))
                return string.IsNullOrWhiteSpace(i.TypeName) ? i.ItemName : $"{i.ItemName} — {i.TypeName}";
            return i.FamilyName ?? "";
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbManholeScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Manhole schedule: scan plumbing equipment (manhole / inspection chamber)
            // and pipe-accessory categories. Reads PLM_DRN_INV_* if populated.
            var manholes = new FilteredElementCollector(ctx.Doc)
                .OfCategory(BuiltInCategory.OST_PlumbingEquipment)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(el =>
                {
                    var n = (el.Name ?? "").ToUpperInvariant();
                    return n.Contains("MANHOLE") || n.Contains("INSPECTION") || n.Contains("CHAMBER") || n.Contains("MH");
                })
                .ToList();

            var rows = manholes.Select(el =>
            {
                double invIn = 0, invOut = 0, cover = 0, depth = 0;
                try
                {
                    // Stamped invert params win when present — they're the
                    // authoritative QS-checked values.
                    var pIn  = el.LookupParameter(ParamRegistry.PLM_DRN_INV_US)?.AsDouble();
                    var pOut = el.LookupParameter(ParamRegistry.PLM_DRN_INV_DS)?.AsDouble();
                    if (pIn  != null) invIn  = pIn.Value  * 0.3048;
                    if (pOut != null) invOut = pOut.Value * 0.3048;

                    // When the stamps are missing (typical before
                    // Plumb_InvertLevels has been run), derive directly from
                    // the connected drainage pipes' connector Z. Each pipe
                    // connector's Z minus the pipe radius gives the invert at
                    // the chamber face — highest is US, lowest is DS. This
                    // means the schedule still produces meaningful Cover /
                    // Depth even on a brand-new model.
                    if (invIn <= 0 && invOut <= 0)
                    {
                        var (cIn, cOut) = ResolveInvertsFromConnectors(el);
                        if (invIn  <= 0) invIn  = cIn;
                        if (invOut <= 0) invOut = cOut;
                    }

                    // Cover = chamber's host-level elevation (project zero).
                    // Depth = Cover − lowest invert. Both internally consistent
                    // even when the project elevation isn't mAOD-aligned.
                    if (el.LevelId != null && el.LevelId.Value > 0)
                    {
                        var lvl = ctx.Doc.GetElement(el.LevelId) as Level;
                        if (lvl != null) cover = lvl.Elevation * 0.3048;
                    }
                    double lowestInv = (invIn > 0 || invOut > 0)
                        ? Math.Min(invIn  > 0 ? invIn  : double.MaxValue,
                                   invOut > 0 ? invOut : double.MaxValue)
                        : 0;
                    if (cover > 0 && lowestInv > 0 && lowestInv != double.MaxValue)
                        depth = cover - lowestInv;
                }
                catch { }
                return new DocsManholeRow
                {
                    Ref     = $"{el.Id.Value} {el.Name}",
                    InvInM  = invIn,
                    InvOutM = invOut,
                    CoverM  = cover,
                    DepthM  = depth
                };
            }).ToList();
            string status = $"Manholes · {manholes.Count} chambers";

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetDocsManholeResult(rows, status);
                return Result.Succeeded;
            }

            var panel = StingResultPanel.Create("Manhole / Access Chamber Schedule");
            panel.AddSection("SUMMARY")
                 .Metric("Chambers found", manholes.Count.ToString());
            if (manholes.Any())
            {
                panel.AddSection("ROWS (first 80)");
                foreach (var el in manholes.Take(80))
                {
                    string lvl = el.LevelId == ElementId.InvalidElementId ? "" : ctx.Doc.GetElement(el.LevelId)?.Name ?? "";
                    string inv = "";
                    try { inv = el.LookupParameter(ParamRegistry.PLM_DRN_INV_DS)?.AsValueString() ?? ""; } catch { }
                    panel.Text($"{el.Id.Value} · {el.Name} · level {lvl} · invert {inv}");
                }
            }
            else
            {
                panel.Text("No manhole / inspection chamber families found in the model. Place chambers (named 'MANHOLE', 'INSPECTION', or 'CHAMBER') under OST_PlumbingEquipment to populate this schedule.");
            }
            panel.Show();
            return Result.Succeeded;
        }

        // Walks the chamber's piping connectors and returns (US, DS) inverts
        // in metres relative to project zero. Z minus pipe radius gives the
        // invert (lowest internal point of the bore) at the chamber face;
        // highest of all connections is US, lowest is DS. Returns (0,0) when
        // the family has no piping connectors so the caller can fall back to
        // the stamped params.
        private static (double invInM, double invOutM) ResolveInvertsFromConnectors(Element el)
        {
            try
            {
                var fi = el as FamilyInstance;
                var mgr = fi?.MEPModel?.ConnectorManager;
                if (mgr == null) return (0, 0);
                var inverts = new List<double>();
                foreach (Connector c in mgr.Connectors)
                {
                    if (c?.Domain != Domain.DomainPiping) continue;
                    double radiusM = 0;
                    try { radiusM = c.Radius * 0.3048; } catch { }
                    inverts.Add(c.Origin.Z * 0.3048 - radiusM);
                }
                if (inverts.Count == 0) return (0, 0);
                return (inverts.Max(), inverts.Min());
            }
            catch { return (0, 0); }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbIsometricCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var panel = StingResultPanel.Create("Plumbing Isometric");
            panel.SetSubtitle("Routes through DrawingTypeRegistry (plumb-drainage-A1-1to100 / plumb-supply-A1-1to100)");
            panel.AddSection("STATUS")
                 .Metric("Drawing-type routing","Active")
                 .Metric("Default profile",     "plumb-drainage-A1-1to100");
            panel.AddSection("USAGE")
                 .Text("1. Run Plumb_FullAudit to refresh PLM_ params.")
                 .Text("2. Select pipes belonging to one system.")
                 .Text("3. Use the SHEETS tab → Create From Template → 'plumb-drainage-A1-1to100'.")
                 .Text("4. The SheetTemplateEngine will stamp invert levels and apply the corporate title block.");
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbCommPackCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Phase 179f ships a folder-listing of the planned commissioning artefacts.
            // Real DOCX/XLSX templates land in the template engine v1.1 _template_sources tree.
            var dir = Path.GetDirectoryName(ctx.Doc.PathName ?? "");
            string pack = "";
            if (!string.IsNullOrEmpty(dir))
            {
                pack = Path.Combine(dir, "_BIM_COORD", "plumbing", "commissioning");
                try { Directory.CreateDirectory(pack); } catch { }
            }

            var panel = StingResultPanel.Create("Plumbing Commissioning Pack");
            panel.SetSubtitle(string.IsNullOrEmpty(pack) ? "Project not saved — pack not staged" : pack);
            panel.AddSection("PLANNED ARTEFACTS")
                 .Text("plumbing_commissioning.docx — flushing + chlorination + pressure test record")
                 .Text("tmv_test_schedule.xlsx — annual TMV test record (NHSScotland HTM 04-01 format)")
                 .Text("legionella_risk_assessment_shell.docx — L8 ACOP RA skeleton")
                 .Text("drainage_cctv_schedule.xlsx — pre-handover CCTV survey schedule");
            panel.AddSection("NEXT STEP")
                 .Text("Place finalised templates into Docs/_template_sources/ and they will auto-extract to the per-project _BIM_COORD/templates/ on next document open.");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
