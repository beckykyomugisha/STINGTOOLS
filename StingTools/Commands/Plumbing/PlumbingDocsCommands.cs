// PlumbingDocsCommands — Phase 179f DOCS tab.
//
// Plumb_PipeSchedule    — pipe schedule grouped by system + DN.
// Plumb_BOQ             — full plumbing BOQ via PlumbingBOQBuilder.
// Plumb_ManholeSchedule — chamber schedule: PLM_DRN_INV_* stamps, else connector inverts.
// Plumb_Isometric       — per-system pipework isometric in a drafting view.
// Plumb_CommPack        — generates commissioning shell file index.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System.Text.RegularExpressions;
using StingTools.BOQ;
using StingTools.UI.Plumbing;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;
using StingTools.Core.Drawing;

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

            var opts = IlReportingOptions.Default;
            double datumM = PipeInvert.DatumOffsetM(ctx.Doc, opts.Datum);
            var rows = manholes.Select(el =>
            {
                // Null = unknown. A zero here used to mean "missing", but an invert
                // below the datum is a real negative number and was thrown away.
                double? invIn = null, invOut = null, cover = null, depth = null;
                try
                {
                    // Stamped invert params win when present — they're the
                    // authoritative QS-checked values. TEXT, metres, on the
                    // reporting datum (see InvertMath.ToParamText); the old
                    // AsDouble()*0.3048 read a text parameter as feet.
                    invIn  = InvertMath.ParseMetres(el.LookupParameter(ParamRegistry.PLM_DRN_INV_US)?.AsString());
                    invOut = InvertMath.ParseMetres(el.LookupParameter(ParamRegistry.PLM_DRN_INV_DS)?.AsString());

                    // When the stamps are missing (typical before
                    // Plumb_InvertLevels has been run), derive directly from
                    // the connected drainage pipes' connector Z. Each pipe
                    // connector's Z minus the pipe radius gives the invert at
                    // the chamber face — highest is US, lowest is DS. This
                    // means the schedule still produces meaningful Cover /
                    // Depth even on a brand-new model.
                    if (!invIn.HasValue && !invOut.HasValue)
                    {
                        var (cIn, cOut) = ResolveInvertsFromConnectors(el, datumM);
                        invIn  = cIn;
                        invOut = cOut;
                    }

                    // Cover level = the top of the chamber family, on the same datum
                    // as the inverts. It used to be the HOST LEVEL's elevation, which
                    // is where the chamber is hosted, not where its cover is.
                    var bb = el.get_BoundingBox(null);
                    if (bb != null) cover = bb.Max.Z * 0.3048 + datumM;

                    var lowest = new[] { invIn, invOut }.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(double.NaN).Min();
                    if (cover.HasValue && !double.IsNaN(lowest)) depth = cover.Value - lowest;
                }
                catch (Exception ex) { StingLog.Warn($"Manhole schedule {el.Id}: {ex.Message}"); }
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
                    try { inv = el.LookupParameter(ParamRegistry.PLM_DRN_INV_DS)?.AsString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
        // invert (lowest internal point of the bore) at the chamber face.
        //
        // Classification cascade (per connector):
        //   1. Connector.Direction set to In/Out — trust it.
        //   2. Bidirectional / unset — walk to the connected pipe and infer
        //      from its slope: opposite end higher than the chamber → water
        //      enters here (In/US); opposite end lower → water exits (Out/DS).
        //      Catches off-the-shelf manhole families that flag every
        //      connector Bidirectional.
        //   3. Still ambiguous — drop into max=US/min=DS bucket as a last
        //      resort so depth math still produces a sensible value.
        //
        // Returns (0,0) when the family has no piping connectors so the
        // caller can fall back to the stamped PLM_DRN_INV_* params.
        //
        // NOTE: Connector.Flow is a double (flow rate). Connector.Direction
        // is the FlowDirectionType enum we want here.
        private static (double? invInM, double? invOutM) ResolveInvertsFromConnectors(Element el, double datumM)
        {
            try
            {
                var fi = el as FamilyInstance;
                var mgr = fi?.MEPModel?.ConnectorManager;
                if (mgr == null) return (null, null);
                var inAll = new List<double>();
                var outAll = new List<double>();
                var anyAll = new List<double>();
                foreach (Connector c in mgr.Connectors)
                {
                    if (c?.Domain != Domain.DomainPiping) continue;
                    // Bore invert at the chamber face: the CONNECTED PIPE's internal
                    // diameter, not the connector radius (which is nominal).
                    double? idM = ConnectedPipeInnerDiameterM(c);
                    double? nomM = null;
                    try { nomM = c.Radius * 2 * 0.3048; } catch (Exception ex) { StingLog.Warn($"Connector radius: {ex.Message}"); }
                    var inv = InvertMath.Invert(c.Origin.Z * 0.3048 + datumM, idM, nomM, out _);
                    if (!inv.HasValue) continue;
                    double invertM = inv.Value;
                    anyAll.Add(invertM);

                    var dir = SafeDirection(c);
                    if (dir == FlowDirectionType.In)        { inAll.Add(invertM);  continue; }
                    if (dir == FlowDirectionType.Out)       { outAll.Add(invertM); continue; }

                    // Bidirectional / unset: ask the connected pipe.
                    var inferred = InferFlowFromConnectedPipe(c);
                    if (inferred == FlowDirectionType.In)  inAll.Add(invertM);
                    else if (inferred == FlowDirectionType.Out) outAll.Add(invertM);
                }
                if (anyAll.Count == 0) return (null, null);

                double us = inAll.Count  > 0 ? inAll.Max()  : anyAll.Max();
                double ds = outAll.Count > 0 ? outAll.Min() : anyAll.Min();
                return (us, ds);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Manhole connector inverts {el?.Id}: {ex.Message}");
                return (null, null);
            }
        }

        private static double? ConnectedPipeInnerDiameterM(Connector c)
        {
            try
            {
                foreach (Connector other in c.AllRefs)
                {
                    if (!(other?.Owner is Autodesk.Revit.DB.Plumbing.Pipe pipe)) continue;
                    var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM);
                    if (p != null && p.HasValue && p.AsDouble() > 0) return p.AsDouble() * 0.3048;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Connected pipe inner diameter: {ex.Message}"); }
            return null;
        }

        private static FlowDirectionType SafeDirection(Connector c)
        {
            try { return c.Direction; }
            catch { return FlowDirectionType.Bidirectional; }
        }

        // For a Bidirectional chamber connector, walk every connected pipe in
        // AllRefs and vote: each connected pipe contributes one In or Out
        // vote based on slope (far end higher than the chamber → In, far
        // end lower → Out, flat within ~1 mm tolerance abstains). Majority
        // wins. Ties or all-flat returns Bidirectional so the caller falls
        // through to the max/min bucket. Voting (rather than first-pipe-wins)
        // makes the inference robust to cap-then-extend reroutes that briefly
        // leave two pipes sharing a chamber connector.
        private static FlowDirectionType InferFlowFromConnectedPipe(Connector chamberConn)
        {
            int inVotes = 0, outVotes = 0;
            try
            {
                var refs = chamberConn.AllRefs;
                if (refs == null) return FlowDirectionType.Bidirectional;
                const double flatToleranceFt = 0.0033; // ≈1 mm
                foreach (Connector other in refs)
                {
                    if (other?.Owner == null) continue;
                    if (!(other.Owner is Pipe p)) continue;
                    var lc = p.Location as LocationCurve;
                    var curve = lc?.Curve;
                    if (curve == null) continue;
                    var pStart = curve.GetEndPoint(0);
                    var pEnd   = curve.GetEndPoint(1);
                    double dStart = pStart.DistanceTo(chamberConn.Origin);
                    double dEnd   = pEnd.DistanceTo(chamberConn.Origin);
                    var nearZ = dStart <= dEnd ? pStart.Z : pEnd.Z;
                    var farZ  = dStart <= dEnd ? pEnd.Z   : pStart.Z;
                    if (Math.Abs(farZ - nearZ) < flatToleranceFt) continue;
                    if (farZ > nearZ) inVotes++; else outVotes++;
                }
            }
            catch { }
            if (inVotes > outVotes) return FlowDirectionType.In;
            if (outVotes > inVotes) return FlowDirectionType.Out;
            return FlowDirectionType.Bidirectional;
        }
    }

    /// <summary>
    /// Plumb_Isometric — draws one pipework isometric per piping system into a
    /// drafting view ("STING ISO - &lt;system&gt;") via PlumbingIsometricGenerator.
    /// Scope: the selected pipes (plus every pipe on the systems of any selected
    /// pipe or fixture), else every pipe visible in the active view, else the
    /// whole model. Re-running redraws the same views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbIsometricCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string scope;
            var pipes = CollectScope(ctx, out scope);
            if (pipes.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Isometric",
                    "No pipes found. Select pipes (or a fixture on a piping system), or open a view that shows pipework.");
                return Result.Cancelled;
            }

            var opts = new PlumbingIsometricOptions();
            PlumbingIsometricResult result;
            using (var tx = new Transaction(doc, "STING Plumbing Isometric"))
            {
                tx.Start();
                result = PlumbingIsometricGenerator.Generate(doc, pipes, opts);
                if (result.Views.Count == 0)
                {
                    tx.RollBack();
                    TaskDialog.Show("STING Plumbing — Isometric",
                        "No isometric could be drawn.\n\n" + string.Join("\n", result.Warnings.Take(8)));
                    return Result.Failed;
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Plumbing Isometric");
            panel.SetSubtitle($"{result.Views.Count} view(s) · scope: {scope} · NOT TO SCALE, fitted to {opts.FitToPaperMm:F0} mm at 1:{opts.ViewScale}");
            panel.AddSection("SUMMARY")
                 .Metric("Pipes considered", result.PipesConsidered.ToString())
                 .Metric("Pipes skipped",    result.PipesSkipped.ToString())
                 .Metric("Views drawn",      result.Views.Count.ToString());
            panel.AddSection("VIEWS");
            foreach (var v in result.Views)
                panel.Text($"{v.ViewName}{(v.Reused ? " (redrawn)" : "")} — {v.PipesDrawn} pipes, " +
                           $"{v.Risers} risers/drops, {v.Labels} labels");
            if (result.Warnings.Any())
            {
                panel.AddSection("WARNINGS");
                foreach (var w in result.Warnings.Take(15)) panel.Text(w);
            }
            panel.AddSection("NEXT")
                 .Text("Place the view on a sheet from the SHEETS tab (drawing type 'plumb-drainage-A1-1to100').")
                 .Text("Labels show DN and, on graded runs, the fall as 1:N. Fittings are not drawn separately.");
            panel.Show();

            OpenView(ctx, result.Views[0].ViewId);
            return Result.Succeeded;
        }

        /// <summary>Open the first drawn view. Optional convenience: the views exist either way.</summary>
        private static void OpenView(StingCommandContext ctx, ElementId viewId)
        {
            try
            {
                if (ctx.Doc.GetElement(viewId) is View v && ctx.UIDoc != null) ctx.UIDoc.RequestViewChange(v);
            }
            catch (Exception ex) { StingLog.Warn($"PlumbIsometric: open view: {ex.Message}"); }
        }

        private static List<Pipe> CollectScope(StingCommandContext ctx, out string scope)
        {
            var doc = ctx.Doc;
            var picked = ctx.UIDoc?.Selection?.GetElementIds()?.Select(id => doc.GetElement(id))
                             .Where(e => e != null).ToList() ?? new List<Element>();
            if (picked.Count > 0)
            {
                // A selected pipe or fixture stands for its whole system.
                var systemIds = new HashSet<long>();
                var direct = new Dictionary<long, Pipe>();
                foreach (var e in picked)
                {
                    if (e is Pipe p)
                    {
                        direct[p.Id.Value] = p;
                        if (p.MEPSystem != null) systemIds.Add(p.MEPSystem.Id.Value);
                    }
                    else if (e is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                    {
                        foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                            if (c.MEPSystem != null) systemIds.Add(c.MEPSystem.Id.Value);
                    }
                }
                foreach (var p in new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>())
                    if (p.MEPSystem != null && systemIds.Contains(p.MEPSystem.Id.Value)) direct[p.Id.Value] = p;
                if (direct.Count > 0)
                {
                    scope = $"selection ({systemIds.Count} system(s))";
                    return direct.Values.ToList();
                }
            }

            if (ctx.HasGraphicalView && !(ctx.ActiveView is ViewDrafting))
            {
                var inView = new FilteredElementCollector(doc, ctx.ActiveView.Id).OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
                if (inView.Count > 0) { scope = $"active view '{ctx.ActiveView.Name}'"; return inView; }
            }

            scope = "whole model";
            return new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
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
                pack = StingPaths.MetaFile(ctx.Doc, "_BIM_COORD", "plumbing", "commissioning");
                try { Directory.CreateDirectory(pack); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
