// StingTools — Speech-privacy / NC cross-talk audit.
//
// Phase 187e — third deferred-list item. NC cross-talk happens when
// two rooms share a common duct path (open ceiling plenum return,
// shared trunk supplying both, etc). Sound from room A enters its
// terminal, travels up the duct, branches across, exits room B's
// terminal — generating an apparent leak with no direct opening
// between the two rooms. The dominant attenuation along that path is
// what determines speech privacy.
//
// Approach:
//   1. Collect every air terminal in the active view (or project).
//   2. Index each terminal's host space, family, plus the duct it's
//      connected to.
//   3. Pair every (A, B) terminal where:
//        - they're in *different* spaces (filtered by HVC_SPACE_ID
//          or Space.Id)
//        - their connector chains converge on a common duct/fitting
//          within MaxWalk hops
//   4. For each pair, compute the cumulative attenuation along the
//      shared path using NcPredictionEngine attenuation tables
//      (straight + elbow + tee + end-reflection × 2). The cross-talk
//      Lp at receiver B for a unit-gain talker in A is approximately:
//        Lp_B = Lp_A − attenAB − 6   (split at the tee)
//   5. Compare against a privacy floor. Rule-of-thumb from BB101,
//      Building Bulletin 93, ASHRAE Handbook A48 Tbl 11:
//        Confidential   < NC-30  cross-talk → flag
//        Normal speech  < NC-35  cross-talk → flag
//
// Output: ranked pairs (worst attenuation first), with the shared
// duct ids + estimated cross-talk Lp. CSV exported to <project>/
// _BIM_COORD/acoustic/crosstalk_<ts>.csv.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Acoustic;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacCrossTalkAuditCommand : IExternalCommand
    {
        private const int MaxWalk = 18;
        // Minimum cumulative attenuation between two terminals to call the
        // path "speech-private". 30 dB at 1 kHz is the conventional floor.
        private const double SpeechPrivacyFloorDb = 30.0;
        // Conservative talker reference: a person at 1 m, normal voice ≈ 60 dBA
        // (ANSI S3.5 / BB93 nominal). Single value → octave-band stand-in.
        private const double TalkerLpRefDb = 60.0;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                // 1. Collect terminals
                var terminals = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(t => t.Space != null)
                    .Select(t => new TerminalNode {
                        El = t, SpaceId = t.Space.Id.Value,
                        SpaceName = t.Space.Name ?? "",
                        Label = $"#{t.Id.Value} {t.Name}"
                    })
                    .ToList();
                if (terminals.Count < 2)
                {
                    TaskDialog.Show("STING HVAC — Cross-talk Audit",
                        "Need at least two air terminals with host Spaces. " +
                        $"Found {terminals.Count} terminal(s).");
                    return Result.Cancelled;
                }

                // 2. Build per-terminal upstream-duct walks (BFS) keyed on
                //    the elements on the path. Each path = ordered list of
                //    (elementId, elementCategory, lengthM) for attenuation
                //    accumulation when two paths converge.
                foreach (var t in terminals)
                    t.UpstreamPath = WalkUpstream(t.El);

                // 3. Pair check — O(n²) but n is typically <500 so OK.
                var flagged = new List<CrossTalkPair>();
                int paired = 0, sameSpace = 0, noShare = 0;
                for (int i = 0; i < terminals.Count; i++)
                for (int j = i + 1; j < terminals.Count; j++)
                {
                    var a = terminals[i]; var b = terminals[j];
                    if (a.SpaceId == b.SpaceId) { sameSpace++; continue; }
                    long shared = FirstSharedElement(a.UpstreamPath, b.UpstreamPath);
                    if (shared == 0) { noShare++; continue; }
                    paired++;

                    double attenDb = AccumulateAttenuationToShared(a, shared)
                                   + AccumulateAttenuationToShared(b, shared)
                                   + 6.0; // tee split
                    // Reference 1 kHz Lp at receiver.
                    double receiverLp = TalkerLpRefDb - attenDb;
                    bool flag = attenDb < SpeechPrivacyFloorDb;
                    if (flag)
                    {
                        flagged.Add(new CrossTalkPair
                        {
                            A = a, B = b, SharedDuctId = shared,
                            AttenDb = attenDb, ReceiverLpDb = receiverLp
                        });
                    }
                }

                // 4. Write CSV
                string csvPath = WriteCsv(doc, flagged);

                // 5. Result panel
                var panel = StingResultPanel.Create("HVAC — Cross-talk Audit");
                panel.SetSubtitle($"{terminals.Count} terminals · {paired} cross-space pairs · " +
                                  $"{flagged.Count} flagged ({SpeechPrivacyFloorDb:F0} dB privacy floor)");
                panel.AddSection("SUMMARY")
                     .Metric("Terminals scanned",       terminals.Count.ToString())
                     .Metric("Cross-space pairs",       paired.ToString())
                     .Metric("Same-space (skipped)",    sameSpace.ToString())
                     .Metric("No shared path",          noShare.ToString())
                     .Metric("Pairs flagged",           flagged.Count.ToString())
                     .Metric("CSV",                     csvPath ?? "(not written)");

                if (flagged.Count > 0)
                {
                    panel.AddSection("WORST CROSS-TALK (top 20)");
                    foreach (var p in flagged.OrderBy(x => x.AttenDb).Take(20))
                        panel.Text($"  '{p.A.SpaceName}' ↔ '{p.B.SpaceName}' · " +
                                   $"shared duct #{p.SharedDuctId} · atten {p.AttenDb:F0} dB · " +
                                   $"est. receiver Lp {p.ReceiverLpDb:F0} dB");
                }
                panel.Text("Method: walks each terminal's connector graph upstream, finds the " +
                           "shared duct between every (A, B) pair, accumulates ASHRAE A48 attenuation " +
                           "(straight + elbow + tee + end-reflection ×2) plus 6 dB tee split. " +
                           "Privacy floor = 30 dB at 1 kHz (BB93 / ASHRAE A48 conventional). " +
                           "Add cross-talk silencers or branch isolation where flagged.");
                panel.Show();

                try
                {
                    var pp = StingHvacPanel.Instance;
                    pp?.PushRunRow($"Cross-talk audit ({flagged.Count} flagged)",
                        flagged.Count > 0 ? "⬡" : "⬤");
                    if (pp != null)
                    {
                        foreach (var p in flagged.OrderBy(x => x.AttenDb).Take(10))
                            pp.IssueRows.Add(new HvacIssueRow
                            {
                                Severity = "⚠",
                                Element = $"'{p.A.SpaceName}' ↔ '{p.B.SpaceName}'",
                                Issue   = $"Cross-talk atten only {p.AttenDb:F0} dB (floor {SpeechPrivacyFloorDb:F0})",
                                Suggestion = "Add cross-talk silencer or branch isolation"
                            });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacCrossTalkAuditCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Path walk ───────────────────────────────────────────────

        /// <summary>
        /// Walk upstream from a terminal's host connector through the
        /// duct + fitting graph. Returns a dictionary keyed on element
        /// id mapped to attenuation contribution to that point. Skips
        /// other terminals and equipment (stops the walk).
        /// </summary>
        private static Dictionary<long, double> WalkUpstream(FamilyInstance terminal)
        {
            var map = new Dictionary<long, double>();
            try
            {
                var conns = terminal.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return map;
                // Terminal end-reflection at 1 kHz ≈ 3 dB (ASHRAE A48 Tbl 18).
                double atten = 3.0;
                foreach (Connector c in conns)
                    WalkUpstreamRec(c, atten, map, 0);
            }
            catch (Exception ex) { StingLog.Warn($"WalkUpstream {terminal?.Id}: {ex.Message}"); }
            return map;
        }

        private static void WalkUpstreamRec(Connector startC, double atten,
            Dictionary<long, double> map, int depth)
        {
            if (startC == null || depth > MaxWalk) return;
            try
            {
                var refs = startC.AllRefs;
                if (refs == null) return;
                foreach (Connector other in refs)
                {
                    var owner = other?.Owner;
                    if (owner == null) continue;
                    long id = owner.Id.Value;
                    if (map.ContainsKey(id)) continue;

                    if (owner.Category != null)
                    {
                        var bic = (BuiltInCategory)owner.Category.Id.Value;
                        if (bic == BuiltInCategory.OST_DuctTerminal) continue;
                        if (bic == BuiltInCategory.OST_MechanicalEquipment) continue;

                        // 1 kHz attenuation contribution at this hop.
                        double dHop;
                        if (bic == BuiltInCategory.OST_DuctCurves && owner is Duct d)
                        {
                            double lenM = 0;
                            if (d.Location is LocationCurve lc && lc.Curve != null)
                                lenM = UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters);
                            dHop = NcPredictionEngine.RectStraightUnlinedDbPerM.Hz1000 * lenM;
                        }
                        else if (bic == BuiltInCategory.OST_DuctFitting)
                        {
                            string nm = (owner.Name ?? "").ToLowerInvariant();
                            if (nm.Contains("elbow")) dHop = NcPredictionEngine.Elbow90UnlinedDb.Hz1000;
                            else if (nm.Contains("tee") || nm.Contains("branch"))
                                dHop = NcPredictionEngine.TeeBranchDb.Hz1000;
                            else dHop = 0.5;        // generic transition
                        }
                        else if (bic == BuiltInCategory.OST_DuctAccessory)
                        {
                            // Silencer in the path is the big win — credit its insertion loss.
                            string nm = (owner.Name ?? "").ToLowerInvariant();
                            if (nm.Contains("silencer") || nm.Contains("attenuator"))
                                dHop = 14.0;                 // generic mid-band; replace with registry lookup later
                            else dHop = 0.0;
                        }
                        else continue;

                        double cum = atten + dHop;
                        map[id] = cum;

                        // Recurse via the OTHER connector on this element.
                        var ownerConns = ConnectorsOf(owner);
                        if (ownerConns == null) continue;
                        foreach (Connector cm in ownerConns)
                        {
                            if (cm == null || cm.Id == other.Id) continue;
                            WalkUpstreamRec(cm, cum, map, depth + 1);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"WalkUpstreamRec: {ex.Message}"); }
        }

        private static long FirstSharedElement(Dictionary<long, double> a, Dictionary<long, double> b)
        {
            if (a == null || b == null) return 0;
            long best = 0;
            double bestSum = double.MaxValue;
            // Smaller dict in outer loop for speed.
            var (small, big) = a.Count < b.Count ? (a, b) : (b, a);
            foreach (var kv in small)
            {
                if (!big.TryGetValue(kv.Key, out double bAtten)) continue;
                double sum = kv.Value + bAtten;
                if (sum < bestSum) { bestSum = sum; best = kv.Key; }
            }
            return best;
        }

        private static double AccumulateAttenuationToShared(TerminalNode t, long sharedId)
            => t.UpstreamPath != null && t.UpstreamPath.TryGetValue(sharedId, out double v) ? v : 0;

        private static IEnumerable<Connector> ConnectorsOf(Element el)
        {
            try
            {
                if (el is MEPCurve mc) return ToList(mc.ConnectorManager?.Connectors);
                if (el is FamilyInstance fi) return ToList(fi.MEPModel?.ConnectorManager?.Connectors);
            }
            catch { }
            return null;
        }

        private static IList<Connector> ToList(ConnectorSet set)
        {
            var list = new List<Connector>();
            if (set == null) return list;
            foreach (Connector c in set) list.Add(c);
            return list;
        }

        private static string WriteCsv(Document doc, List<CrossTalkPair> flagged)
        {
            try
            {
                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projDir)) return null;
                string outDir = Path.Combine(projDir, "_BIM_COORD", "acoustic");
                Directory.CreateDirectory(outDir);
                string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string csv = Path.Combine(outDir, $"crosstalk_{ts}.csv");
                var sb = new StringBuilder();
                sb.AppendLine("TalkerSpace,ReceiverSpace,TalkerTerminal,ReceiverTerminal,SharedDuctId,AttenDb,ReceiverLpDb");
                foreach (var p in flagged.OrderBy(x => x.AttenDb))
                {
                    sb.AppendLine($"\"{p.A.SpaceName}\",\"{p.B.SpaceName}\",\"{p.A.Label}\",\"{p.B.Label}\",{p.SharedDuctId},{p.AttenDb:F1},{p.ReceiverLpDb:F1}");
                }
                File.WriteAllText(csv, sb.ToString());
                return csv;
            }
            catch (Exception ex) { StingLog.Warn($"WriteCsv: {ex.Message}"); return null; }
        }

        // ── DTOs ────────────────────────────────────────────────────

        private class TerminalNode
        {
            public FamilyInstance El;
            public long   SpaceId;
            public string SpaceName;
            public string Label;
            public Dictionary<long, double> UpstreamPath;
        }

        private class CrossTalkPair
        {
            public TerminalNode A;
            public TerminalNode B;
            public long   SharedDuctId;
            public double AttenDb;
            public double ReceiverLpDb;
        }
    }
}
