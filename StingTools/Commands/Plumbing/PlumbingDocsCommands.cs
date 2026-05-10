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
            var b = PlumbingBOQBuilder.Build(ctx.Doc);
            var pipeRows = b.Rows.Where(r => r.Unit == "m").ToList();

            var rows = pipeRows.Select(r =>
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
            string status = $"Pipe schedule · {b.PipesCounted} pipes · {pipeRows.Count} rows · "
                          + $"{pipeRows.Sum(r => r.Qty):F1} m total";

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetDocsPipeScheduleResult(rows, status);
                return Result.Succeeded;
            }

            var panel = StingResultPanel.Create("Plumbing Pipe Schedule");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes counted", b.PipesCounted.ToString())
                 .Metric("Distinct rows", pipeRows.Count.ToString())
                 .Metric("Total length (m)", pipeRows.Sum(r => r.Qty).ToString("F1"));
            if (pipeRows.Any())
            {
                panel.AddSection("ROWS (first 80)");
                foreach (var row in pipeRows.Take(80))
                    panel.Text($"{row.Code} · {row.Description,-50} · {row.Qty,8:F1} {row.Unit}");
            }
            panel.Show();
            return Result.Succeeded;
        }

        // BOQ description format established by PlumbingBOQBuilder is roughly
        // "{material} pipe DN{size} ({system N})". We split heuristically; if
        // the parse fails the caller falls back to the full description in the
        // Material column so no information is lost.
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
            // Looser fallback: just pull the DN if present so the column at
            // least shows the diameter on rows the regex didn't match.
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
            var b = PlumbingBOQBuilder.Build(ctx.Doc);

            var rows = b.Rows.Select(r => new DocsBoqRow
            {
                Item        = r.Code,
                Description = r.Description,
                Qty         = r.Qty,
                Unit        = r.Unit
            }).ToList();
            string status = $"BOQ · {b.PipesCounted} pipes · {b.FittingsCounted} fittings · "
                          + $"{b.AccessoriesCounted} accessories · {b.Rows.Count} rows";

            var inst = StingPlumbingPanel.Instance;
            if (inst != null)
            {
                inst.SetDocsBoqResult(rows, status);
                return Result.Succeeded;
            }

            var panel = StingResultPanel.Create("Plumbing BOQ");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes",        b.PipesCounted.ToString())
                 .Metric("Fittings",     b.FittingsCounted.ToString())
                 .Metric("Accessories",  b.AccessoriesCounted.ToString())
                 .Metric("Total rows",   b.Rows.Count.ToString());
            if (b.Rows.Any())
            {
                panel.AddSection("BOQ ROWS (first 100)");
                foreach (var r in b.Rows.Take(100))
                    panel.Text($"{r.Code} · {r.Description,-46} · {r.Qty,8:F2} {r.Unit}");
            }
            if (b.Warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in b.Warnings.Take(20)) panel.Text(w);
            }
            panel.Show();
            return Result.Succeeded;
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
                    var pIn  = el.LookupParameter(ParamRegistry.PLM_DRN_INV_US)?.AsDouble();
                    var pOut = el.LookupParameter(ParamRegistry.PLM_DRN_INV_DS)?.AsDouble();
                    if (pIn  != null) invIn  = pIn.Value  * 0.3048;  // ft → m
                    if (pOut != null) invOut = pOut.Value * 0.3048;
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
