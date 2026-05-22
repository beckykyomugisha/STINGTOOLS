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
        // Minimum cumulative attenuation at 1 kHz between two terminals to
        // call the path speech-private. 30 dB at 1 kHz is the conventional
        // BB93 / ASHRAE A48 floor. Used as a secondary "summary" flag only;
        // the primary classification is via NC at the receiver (Phase 187f).
        private const double SpeechPrivacyFloorDb = 30.0;
        // ANSI S3.5 normal-voice talker octave-band Lp at 1 m, dB re 20 μPa.
        // Used as the receiver-side reference for both the summary attenuation
        // metric and the per-octave Lp computation that drives NcCurves.Rate.
        private static readonly OctaveBand TalkerLpRefOct = OctaveBand.FromArray(
            new[] { 54.0, 57.0, 58.0, 60.0, 60.0, 56.0, 50.0, 44.0 });
        // Receiver target NC — flag pairs whose computed NC at the receiver
        // exceeds this. Defaults to office speech-privacy NC-35 per ASHRAE A48.
        private const int ReceiverNcTarget = 35;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                // 1. Collect terminals with their host-space acoustic geometry
                //    (volume + surface + absorption — needed for the receiver
                //    direct+reverberant Lp pass in Phase 187g).
                var terminals = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(t => t.Space != null)
                    .Select(t => new TerminalNode {
                        El = t, SpaceId = t.Space.Id.Value,
                        SpaceName = t.Space.Name ?? "",
                        Label = $"#{t.Id.Value} {t.Name}",
                        Receiver = BuildReceiverFromSpace(t.Space)
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
                    t.UpstreamPath = WalkUpstream(t.El, doc);

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

                    var attenA = AccumulateAttenuationToShared(a, shared);
                    var attenB = AccumulateAttenuationToShared(b, shared);
                    // Total path attenuation = upstream A + upstream B + 6 dB tee split.
                    var attenTotal = new OctaveBand();
                    for (int k = 0; k < 8; k++)
                        attenTotal[k] = attenA[k] + attenB[k] + 6.0;

                    // Phase 187g — proper room receiver model.
                    //
                    // 1. Talker injects a sound POWER (Lw) at terminal A's
                    //    inlet equal to ANSI S3.5 Lp reference + 11 dB
                    //    (Lw ≈ Lp at 1 m for a 4π talker — equivalent to a
                    //    point source with no directivity boost).
                    // 2. Attenuation along the shared path reduces it to Lw
                    //    at terminal B's outlet (receiver-room side).
                    // 3. The receiver Lp at the listener is then the
                    //    standard room-acoustic Lp = Lw + 10·log10(Q/4πr²
                    //    + 4/R) using the receiver's room volume / surface
                    //    / absorption / directivity (Phase 187g).
                    //
                    // This adds the room's reverberant decay on top of the
                    // duct-side attenuation — a large absorptive room is
                    // 5-8 dB more forgiving than a small reflective one.
                    var talkerLw = new OctaveBand();
                    for (int k = 0; k < 8; k++) talkerLw[k] = TalkerLpRefOct[k] + 11.0;
                    var receiverLw = talkerLw - attenTotal;
                    var receiverLp = NcPredictionEngine.RoomLwToLp(receiverLw, b.Receiver);
                    int receiverNc = NcCurves.Rate(receiverLp);

                    double atten1kDb = attenTotal.Hz1000;
                    bool flag = receiverNc > ReceiverNcTarget || atten1kDb < SpeechPrivacyFloorDb;
                    if (flag)
                    {
                        flagged.Add(new CrossTalkPair
                        {
                            A = a, B = b, SharedDuctId = shared,
                            AttenTotal = attenTotal, ReceiverLp = receiverLp,
                            ReceiverNc = receiverNc, Atten1kDb = atten1kDb
                        });
                    }
                }

                // 4. Write CSV
                string csvPath = WriteCsv(doc, flagged);

                // 5. Result panel
                var panel = StingResultPanel.Create("HVAC — Cross-talk Audit");
                panel.SetSubtitle($"{terminals.Count} terminals · {paired} cross-space pairs · " +
                                  $"{flagged.Count} flagged (NC > {ReceiverNcTarget} or 1k-atten < {SpeechPrivacyFloorDb:F0} dB)");
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
                    foreach (var p in flagged.OrderByDescending(x => x.ReceiverNc).Take(20))
                        panel.Text($"  '{p.A.SpaceName}' ↔ '{p.B.SpaceName}' · " +
                                   $"shared #{p.SharedDuctId} · NC {p.ReceiverNc} · " +
                                   $"atten@1kHz {p.Atten1kDb:F0} dB");
                }
                panel.Text("Method: walks each terminal's connector graph upstream tracking full " +
                           "octave-band attenuation (63 Hz → 8 kHz) per ASHRAE A48 (straight + elbow " +
                           "+ tee + end-reflection ×2 + silencer IL via AcousticDataRegistry) plus 6 dB " +
                           "tee split. Talker Lw = ANSI S3.5 normal voice at 1 m + 11 dB → Lw. " +
                           "Receiver Lp computed via the direct + reverberant room model (Phase 187g): " +
                           "Lp = Lw + 10·log10(Q/4πr² + 4/R), R = Sᾱ/(1-ᾱ), using each receiver-space's " +
                           "volume, surface, absorption + 1.5 m listener / Q=2 ceiling-mount defaults. " +
                           $"Rated against NC curves; flag when NC > {ReceiverNcTarget} or 1 kHz " +
                           $"attenuation < {SpeechPrivacyFloorDb:F0} dB. Add cross-talk silencers or " +
                           "branch isolation where flagged.");
                panel.Show();

                try
                {
                    var pp = StingHvacPanel.Instance;
                    pp?.PushRunRow($"Cross-talk audit ({flagged.Count} flagged)",
                        flagged.Count > 0 ? "⬡" : "⬤");
                    if (pp != null)
                    {
                        foreach (var p in flagged.OrderByDescending(x => x.ReceiverNc).Take(10))
                            pp.IssueRows.Add(new HvacIssueRow
                            {
                                Severity = "⚠",
                                Element = $"'{p.A.SpaceName}' ↔ '{p.B.SpaceName}'",
                                Issue   = $"Cross-talk NC {p.ReceiverNc} > target NC {ReceiverNcTarget} (1k atten {p.Atten1kDb:F0} dB)",
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
        /// Walk upstream from a terminal's host connector through the duct +
        /// fitting graph. Returns element-id → cumulative <see cref="OctaveBand"/>
        /// attenuation at that element (8 bands, 63 Hz → 8 kHz). Skips other
        /// terminals and equipment (stops the walk).
        /// </summary>
        private static Dictionary<long, OctaveBand> WalkUpstream(
            FamilyInstance terminal, Document doc)
        {
            var map = new Dictionary<long, OctaveBand>();
            try
            {
                var conns = terminal.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return map;
                // Terminal end-reflection per octave (ASHRAE A48 Tbl 18).
                var atten = NcPredictionEngine.TerminalEndReflectionDb;
                foreach (Connector c in conns)
                    WalkUpstreamRec(c, atten, map, 0, doc);
            }
            catch (Exception ex) { StingLog.Warn($"WalkUpstream {terminal?.Id}: {ex.Message}"); }
            return map;
        }

        private static void WalkUpstreamRec(Connector startC, OctaveBand atten,
            Dictionary<long, OctaveBand> map, int depth, Document doc)
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

                        // Per-octave attenuation contribution at this hop.
                        OctaveBand dHop = new OctaveBand();
                        if (bic == BuiltInCategory.OST_DuctCurves && owner is Duct d)
                        {
                            double lenM = 0;
                            if (d.Location is LocationCurve lc && lc.Curve != null)
                                lenM = UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters);
                            for (int i = 0; i < 8; i++)
                                dHop[i] = NcPredictionEngine.RectStraightUnlinedDbPerM[i] * lenM;
                        }
                        else if (bic == BuiltInCategory.OST_DuctFitting)
                        {
                            string nm = (owner.Name ?? "").ToLowerInvariant();
                            if (nm.Contains("elbow")) dHop = NcPredictionEngine.Elbow90UnlinedDb;
                            else if (nm.Contains("tee") || nm.Contains("branch"))
                                dHop = NcPredictionEngine.TeeBranchDb;
                            else for (int i = 0; i < 8; i++) dHop[i] = 0.5;   // generic transition
                        }
                        else if (bic == BuiltInCategory.OST_DuctAccessory)
                        {
                            // Silencer in the path is the big win. Prefer
                            // manufacturer IL from the registry; fall back to a
                            // generic spectrum if no match.
                            string nm = (owner.Name ?? "").ToLowerInvariant();
                            if (nm.Contains("silencer") || nm.Contains("attenuator"))
                            {
                                var ac = StingTools.Core.Acoustic.AcousticDataRegistry.Get(doc);
                                var match = ac?.FindSilencer(owner.Name);
                                dHop = match?.Il ?? OctaveBand.FromArray(
                                    new[] { 2.0, 4, 8, 12, 14, 12, 8, 5 });
                            }
                            // Other accessories → zero attenuation.
                        }
                        else continue;

                        var cum = atten + dHop;
                        map[id] = cum;

                        var ownerConns = ConnectorsOf(owner);
                        if (ownerConns == null) continue;
                        foreach (Connector cm in ownerConns)
                        {
                            if (cm == null || cm.Id == other.Id) continue;
                            WalkUpstreamRec(cm, cum, map, depth + 1, doc);
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"WalkUpstreamRec: {ex.Message}"); }
        }

        /// <summary>
        /// Find the shared element minimising 1 kHz summed attenuation —
        /// proxy for "the loudest cross-talk path". Equivalent to the prior
        /// behaviour but now operating on the octave-band map.
        /// </summary>
        private static long FirstSharedElement(
            Dictionary<long, OctaveBand> a, Dictionary<long, OctaveBand> b)
        {
            if (a == null || b == null) return 0;
            long best = 0;
            double bestSum1k = double.MaxValue;
            var (small, big) = a.Count < b.Count ? (a, b) : (b, a);
            foreach (var kv in small)
            {
                if (!big.TryGetValue(kv.Key, out var bAtten)) continue;
                double sum1k = kv.Value.Hz1000 + bAtten.Hz1000;
                if (sum1k < bestSum1k) { bestSum1k = sum1k; best = kv.Key; }
            }
            return best;
        }

        private static OctaveBand AccumulateAttenuationToShared(TerminalNode t, long sharedId)
            => t.UpstreamPath != null && t.UpstreamPath.TryGetValue(sharedId, out var v)
                ? v : new OctaveBand();

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
                sb.AppendLine("TalkerSpace,ReceiverSpace,TalkerTerminal,ReceiverTerminal,SharedDuctId," +
                              "ReceiverNc,Atten63,Atten125,Atten250,Atten500,Atten1k,Atten2k,Atten4k,Atten8k," +
                              "Lp63,Lp125,Lp250,Lp500,Lp1k,Lp2k,Lp4k,Lp8k");
                foreach (var p in flagged.OrderByDescending(x => x.ReceiverNc))
                {
                    var a = p.AttenTotal; var lp = p.ReceiverLp;
                    sb.Append($"\"{p.A.SpaceName}\",\"{p.B.SpaceName}\",\"{p.A.Label}\",\"{p.B.Label}\"," +
                              $"{p.SharedDuctId},{p.ReceiverNc},");
                    sb.Append($"{a.Hz63:F1},{a.Hz125:F1},{a.Hz250:F1},{a.Hz500:F1},{a.Hz1000:F1},{a.Hz2000:F1},{a.Hz4000:F1},{a.Hz8000:F1},");
                    sb.AppendLine($"{lp.Hz63:F1},{lp.Hz125:F1},{lp.Hz250:F1},{lp.Hz500:F1},{lp.Hz1000:F1},{lp.Hz2000:F1},{lp.Hz4000:F1},{lp.Hz8000:F1}");
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
            public long           SpaceId;
            public string         SpaceName;
            public string         Label;
            /// <summary>Element-id → cumulative <see cref="OctaveBand"/> attenuation
            /// from this terminal up to that point. Populated by <see cref="WalkUpstream"/>.</summary>
            public Dictionary<long, OctaveBand> UpstreamPath;
            /// <summary>Receiver-room acoustic model (volume + surface +
            /// absorption + listener distance + directivity). Used by the
            /// pairing loop to convert duct-side Lw into Lp at the listener
            /// via <see cref="NcPredictionEngine.RoomLwToLp"/>.</summary>
            public RoomReceiver   Receiver;
        }

        private static RoomReceiver BuildReceiverFromSpace(Space sp)
        {
            try
            {
                double areaM2  = UnitUtils.ConvertFromInternalUnits(sp.Area, UnitTypeId.SquareMeters);
                double heightM = UnitUtils.ConvertFromInternalUnits(sp.UnboundedHeight, UnitTypeId.Meters);
                if (heightM < 0.5) heightM = 3.0;
                double vol = areaM2 * heightM;
                // Surface area = floor + ceiling + perimeter walls. Approx
                // square-ish room: perimeter ≈ 4·√area.
                double perimM = 4.0 * Math.Sqrt(Math.Max(areaM2, 1.0));
                double surfM2 = 2 * areaM2 + perimM * heightM;
                // Absorption coefficient — heuristic by space type. Plant /
                // warehouse are highly reflective; classrooms / auditoria
                // are progressively more absorptive.
                double absorption = 0.20;
                string spType = sp.LookupParameter("HVC_SPACE_TYPE_TXT")?.AsString();
                if (!string.IsNullOrEmpty(spType))
                {
                    string lo = spType.ToLowerInvariant();
                    if      (lo.Contains("patient")  || lo.Contains("hospital"))   absorption = 0.25;
                    else if (lo.Contains("classroom")|| lo.Contains("conference") ||
                             lo.Contains("meeting"))                                absorption = 0.30;
                    else if (lo.Contains("auditorium")|| lo.Contains("theatre"))   absorption = 0.40;
                    else if (lo.Contains("plant")    || lo.Contains("warehouse"))  absorption = 0.10;
                    else if (lo.Contains("kitchen"))                                absorption = 0.10;
                }
                return new RoomReceiver
                {
                    Name              = sp.Name ?? "",
                    VolumeM3          = vol,
                    SurfaceAreaM2     = surfM2,
                    AvgAbsorption     = absorption,
                    ListenerDistanceM = 1.5,     // standard speech-privacy reference distance
                    Directivity       = 2.0      // ceiling-mounted terminal — Q=2 (hemispherical)
                };
            }
            catch (Exception ex) { StingLog.Warn($"BuildReceiverFromSpace {sp?.Id}: {ex.Message}"); }
            return new RoomReceiver {
                Name = sp?.Name ?? "", VolumeM3 = 100, SurfaceAreaM2 = 120,
                AvgAbsorption = 0.2, ListenerDistanceM = 1.5, Directivity = 2.0 };
        }

        private class CrossTalkPair
        {
            public TerminalNode A;
            public TerminalNode B;
            public long       SharedDuctId;
            public OctaveBand AttenTotal;
            public OctaveBand ReceiverLp;
            public int        ReceiverNc;
            public double     Atten1kDb;
        }
    }
}
