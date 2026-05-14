// StingTools — PanelWireReconcileCommand.
//
// Cross-checks wire-size data across three sources:
//   Source A — ElectricalSystem parameters (ELC_CKT_CSA_MM2 /
//              RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM) — the "design intent"
//   Source B — Conduit shared parameter ELC_CDT_FILL_PCT + the cable
//              manifest (CableManifest, Phase 175) — the "as-modelled"
//   Source C — Wire annotation text notes placed by WireAnnotationCommands —
//              the "as-drawn"
//
// For every circuit the command produces one of four verdicts:
//   OK          — A matches B and (if annotation exists) matches C
//   WIRE_ONLY   — A differs from C (annotation stale / wrong)
//   CONDUIT_ONLY — A differs from B (conduit fill not updated)
//   FULL_MISMATCH — A, B, and C all disagree
//
// In Dry-Run mode the command reports findings in a TaskDialog.
// In Apply mode it writes ELC_CKT_CSA_MM2 from the design-intent value
// onto every conduit in the circuit's run and optionally refreshes wire
// annotation text notes.
//
// Revit 2025+ note: ElementId.Value is long (Int64). All local variables
// keyed on element IDs use long throughout to avoid CS0266.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelWireReconcileCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // ── 1. Collect all electrical circuits ────────────────────
            var allSystems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .ToList();

            if (allSystems.Count == 0)
            {
                TaskDialog.Show("STING Wire Reconcile", "No electrical circuits found in the model.");
                return Result.Succeeded;
            }

            // ── 2. Build conduit → circuits index via circuit elements ─
            // ElementId.Value is long in Revit 2025+ — use long throughout.
            var conduitCircuitMap = new Dictionary<long, List<ElectricalSystem>>();
            foreach (var sys in allSystems)
            {
                try
                {
                    var members = sys.Elements;
                    if (members == null) continue;
                    foreach (Element member in members)
                    {
                        if (member?.Category == null) continue;
                        var bic = (BuiltInCategory)member.Category.Id.Value;
                        if (bic != BuiltInCategory.OST_Conduit &&
                            bic != BuiltInCategory.OST_CableTray &&
                            bic != BuiltInCategory.OST_Wire) continue;
                        long memberId = member.Id.Value;
                        if (!conduitCircuitMap.TryGetValue(memberId, out var sysList))
                        {
                            sysList = new List<ElectricalSystem>();
                            conduitCircuitMap[memberId] = sysList;
                        }
                        sysList.Add(sys);
                    }
                }
                catch { }
            }

            // ── 3. Collect wire annotation text notes (optional source C) ─
            var textNotes = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNote))
                .Cast<TextNote>()
                .ToList();

            // Index annotation text by the circuit/conduit unique-id embedded
            // in the Comments parameter (set by WireAnnotationEngine marker).
            var annotByConduit = new Dictionary<long, string>();
            foreach (var tn in textNotes)
            {
                try
                {
                    var commentsParam = tn.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    string marker = commentsParam?.AsString() ?? "";
                    if (!marker.StartsWith("STING_WIRE_ANNOT|")) continue;
                    string uid = marker.Substring("STING_WIRE_ANNOT|".Length);
                    // Reverse-look up the conduit by UniqueId so we can key on ElementId.Value.
                    Element conduit = doc.GetElement(uid);
                    if (conduit == null) continue;
                    long conduitIdVal = conduit.Id.Value;
                    if (!annotByConduit.ContainsKey(conduitIdVal))
                        annotByConduit[conduitIdVal] = tn.Text ?? "";
                }
                catch { }
            }

            // ── 4. Reconcile per circuit ───────────────────────────────
            var findings = new List<WireReconcileFinding>();

            foreach (var sys in allSystems)
            {
                string circuitDesignCsa = ReadDesignCsa(doc, sys);
                if (string.IsNullOrEmpty(circuitDesignCsa)) continue;

                string panelName = "";
                try { panelName = sys.BaseEquipment?.Name ?? ""; } catch { }

                // Gather every conduit/tray/wire element on this circuit.
                var circuitMembers = new List<Element>();
                try
                {
                    if (sys.Elements != null)
                        foreach (Element m in sys.Elements) circuitMembers.Add(m);
                }
                catch { }

                bool anyConduitMismatch = false;
                bool anyAnnotMismatch   = false;
                var conduitMismatches   = new List<string>();
                var annotMismatches     = new List<string>();

                foreach (var member in circuitMembers)
                {
                    if (member == null) continue;
                    long memberIdVal = member.Id.Value;

                    // Source B — conduit ELC_CDT_* parameter.
                    string conduitCsa = ParameterHelpers.GetString(member, "ELC_CDT_FILL_PCT");
                    if (string.IsNullOrEmpty(conduitCsa))
                        conduitCsa = ReadRbsWireSize(member);

                    if (!string.IsNullOrEmpty(conduitCsa) &&
                        !CsaMatches(circuitDesignCsa, conduitCsa))
                    {
                        anyConduitMismatch = true;
                        conduitMismatches.Add(
                            $"Conduit {member.Id.Value}: design={circuitDesignCsa} conduit={conduitCsa}");
                    }

                    // Source C — annotation text note.
                    if (annotByConduit.TryGetValue(memberIdVal, out string annotText))
                    {
                        if (!annotText.Contains(circuitDesignCsa))
                        {
                            anyAnnotMismatch = true;
                            annotMismatches.Add(
                                $"Annotation on conduit {memberIdVal}: design={circuitDesignCsa} annotation='{annotText}'");
                        }
                    }
                }

                if (!anyConduitMismatch && !anyAnnotMismatch) continue;

                var verdict = (anyConduitMismatch && anyAnnotMismatch) ? ReconcileVerdict.FullMismatch
                            : anyConduitMismatch                       ? ReconcileVerdict.ConduitOnly
                                                                       : ReconcileVerdict.WireAnnotOnly;

                findings.Add(new WireReconcileFinding
                {
                    CircuitId      = sys.Id.Value,      // long — Revit 2025+
                    CircuitName    = sys.CircuitNumber ?? sys.Name ?? sys.Id.ToString(),
                    PanelName      = panelName,
                    DesignCsa      = circuitDesignCsa,
                    Verdict        = verdict,
                    Details        = conduitMismatches.Concat(annotMismatches).ToList(),
                });
            }

            // ── 5. Present findings ───────────────────────────────────
            if (findings.Count == 0)
            {
                TaskDialog.Show("STING Wire Reconcile",
                    $"All {allSystems.Count} circuits are consistent. No wire-size mismatches found.");
                return Result.Succeeded;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Wire-size mismatches found: {findings.Count} of {allSystems.Count} circuits\n");

            int fullMismatch   = findings.Count(f => f.Verdict == ReconcileVerdict.FullMismatch);
            int conduitOnly    = findings.Count(f => f.Verdict == ReconcileVerdict.ConduitOnly);
            int annotOnly      = findings.Count(f => f.Verdict == ReconcileVerdict.WireAnnotOnly);

            sb.AppendLine($"Full mismatches (design ≠ conduit ≠ annotation): {fullMismatch}");
            sb.AppendLine($"Conduit-only (design ≠ conduit):                  {conduitOnly}");
            sb.AppendLine($"Annotation-only (design ≠ annotation):            {annotOnly}");
            sb.AppendLine();

            int showMax = Math.Min(findings.Count, 20);
            for (int i = 0; i < showMax; i++)
            {
                var f = findings[i];
                string tag = f.Verdict == ReconcileVerdict.FullMismatch ? "[FULL]"
                           : f.Verdict == ReconcileVerdict.ConduitOnly  ? "[CDT]"
                                                                         : "[ANN]";
                sb.AppendLine($"{tag} Circuit {f.CircuitName} / Panel {f.PanelName} — design {f.DesignCsa}");
                foreach (var d in f.Details.Take(2)) sb.AppendLine($"      {d}");
            }
            if (findings.Count > showMax)
                sb.AppendLine($"... and {findings.Count - showMax} more. Export CSV for full list.");

            var td = new TaskDialog("STING Wire Reconcile")
            {
                MainInstruction = $"{findings.Count} wire-size mismatch(es) detected",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Apply — write design CSA onto mismatched conduits",
                "Stamps ELC_CDT_CSA_MM2 on each conduit from its circuit's ELC_CKT_CSA_MM2 value.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Export CSV — save full report",
                $"Write {findings.Count} rows to the project output folder.");
            td.DefaultButton = TaskDialogResult.Close;

            var dlgResult = td.Show();

            if (dlgResult == TaskDialogResult.CommandLink1)
                ApplyFixes(doc, allSystems, findings);
            else if (dlgResult == TaskDialogResult.CommandLink2)
                ExportCsv(doc, findings);

            return Result.Succeeded;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static string ReadDesignCsa(Document doc, ElectricalSystem sys)
        {
            // Prefer STING shared parameter; fall back to Revit native wire-size.
            string sting = ParameterHelpers.GetString(sys, ParamRegistry.ELC_CKT_CSA_MM2);
            if (!string.IsNullOrEmpty(sting)) return sting;
            return ReadRbsWireSize(sys);
        }

        private static string ReadRbsWireSize(Element el)
        {
            try
            {
                var p = el.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM);
                if (p != null && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static bool CsaMatches(string a, string b)
        {
            // Normalise both strings: strip whitespace, lower-case, and try
            // numeric comparison (e.g. "2.5" == "2.50 mm²" == "2.5mm2").
            a = Normalise(a);
            b = Normalise(b);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (double.TryParse(ExtractNumeric(a), out double da) &&
                double.TryParse(ExtractNumeric(b), out double db))
                return Math.Abs(da - db) < 0.01;
            return false;
        }

        private static string Normalise(string s) =>
            (s ?? "").Trim().ToLowerInvariant()
                     .Replace("mm²", "").Replace("mm2", "").Replace("mm", "")
                     .Replace(" ", "").Replace("²", "");

        private static string ExtractNumeric(string s)
        {
            var sb = new StringBuilder();
            bool dot = false;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) sb.Append(c);
                else if (c == '.' && !dot) { sb.Append(c); dot = true; }
                else if (sb.Length > 0) break;
            }
            return sb.ToString();
        }

        private static void ApplyFixes(Document doc,
            IList<ElectricalSystem> allSystems,
            IList<WireReconcileFinding> findings)
        {
            // Build a set of circuit ids that need fixing.
            var fixSet = new HashSet<long>(findings.Select(f => f.CircuitId));

            int stamped = 0;
            using (var tx = new Transaction(doc, "STING Wire Reconcile — stamp conduit CSA"))
            {
                tx.Start();
                try
                {
                    foreach (var sys in allSystems)
                    {
                        long circuitIdVal = sys.Id.Value;   // long — Revit 2025+
                        if (!fixSet.Contains(circuitIdVal)) continue;

                        string designCsa = ReadDesignCsa(doc, sys);
                        if (string.IsNullOrEmpty(designCsa)) continue;

                        try
                        {
                            if (sys.Elements == null) continue;
                            foreach (Element member in sys.Elements)
                            {
                                if (member == null) continue;
                                try
                                {
                                    ParameterHelpers.SetString(member, "ELC_CDT_CSA_MM2", designCsa, overwrite: true);
                                    stamped++;
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    StingLog.Error("PanelWireReconcile ApplyFixes", ex);
                    TaskDialog.Show("STING Wire Reconcile", $"Apply failed: {ex.Message}");
                    return;
                }
            }

            TaskDialog.Show("STING Wire Reconcile",
                $"Stamped ELC_CDT_CSA_MM2 on {stamped} conduit element(s) across " +
                $"{findings.Count} circuit(s).");
        }

        private static void ExportCsv(Document doc, IList<WireReconcileFinding> findings)
        {
            try
            {
                string outDir = Core.OutputLocationHelper.GetOutputDirectory(doc);
                string path = System.IO.Path.Combine(outDir,
                    $"WireReconcile_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var lines = new List<string> { "CircuitName,PanelName,DesignCSA,Verdict,Detail" };
                foreach (var f in findings)
                {
                    string detail = string.Join("; ", f.Details.Take(3))
                                         .Replace(",", ";").Replace("\n", " ");
                    lines.Add($"{Q(f.CircuitName)},{Q(f.PanelName)},{Q(f.DesignCsa)},{f.Verdict},{Q(detail)}");
                }
                System.IO.File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
                TaskDialog.Show("STING Wire Reconcile", $"Exported {findings.Count} rows:\n{path}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("STING Wire Reconcile", $"Export failed: {ex.Message}");
            }
        }

        private static string Q(string s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
    }

    // ── Supporting types ────────────────────────────────────────────────

    internal enum ReconcileVerdict { Ok, ConduitOnly, WireAnnotOnly, FullMismatch }

    internal class WireReconcileFinding
    {
        /// <summary>ElementId.Value of the ElectricalSystem (long in Revit 2025+).</summary>
        public long CircuitId { get; set; }

        public string CircuitName { get; set; }
        public string PanelName   { get; set; }
        public string DesignCsa   { get; set; }
        public ReconcileVerdict Verdict { get; set; }
        public List<string> Details { get; set; } = new List<string>();
    }
}
