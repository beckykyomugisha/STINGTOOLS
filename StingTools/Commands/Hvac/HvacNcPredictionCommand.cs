// StingTools — NC prediction command.
//
// Walks an active duct selection from the active view (treating the
// upstream-most member as the fan source) and computes the predicted
// NC at the room downstream of the terminal diffuser. Uses
// NcPredictionEngine to accumulate attenuation + regenerated noise
// along the path and renders the breakdown in a StingResultPanel.
//
// Fan sound power comes from STING_FAN_SPECTRA.json (manufacturer octave
// spectra, matched by family name). Without a match it is approximated from
// the path flow and pressure (Madison's empirical formula):
//     Lw = 67 + 10·log10(Q) + 10·log10(ΔP)      Q in L/s, ΔP in Pa
// Every value the command has to assume (fan spectrum, silencer IL, fitting
// velocity, terminal, receiver room) is listed in a BASIS section, and the
// result is graded DESIGN BASIS only when none was needed. An NC figure built
// on defaults is indicative and says so.
//
// Path order follows the duct connectors (upstream = the end carrying the
// most air), not element-id order: regenerated noise is attenuated only by
// what lies downstream of it, so order changes the answer.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Acoustic;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacNcPredictionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                // Use the selection as the path. Order is the user-pick order
                // upstream→downstream; in absence of a user pick we sort by
                // length descending and treat the longest as the trunk.
                var ids = ctx.UIDoc?.Selection?.GetElementIds()?.ToList()
                          ?? new List<ElementId>();
                if (ids.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — NC Prediction",
                        "Pick a duct path from fan source → terminal first (Select tab → Mechanical), then re-run.");
                    return Result.Cancelled;
                }

                var assumptions = new List<string>();
                var path = BuildPathFromSelection(doc, ids, out double pathFlowLs, out double pathDpPa, assumptions);
                if (path.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — NC Prediction",
                        "Selection contains no duct curves or fittings.");
                    return Result.Cancelled;
                }

                // Try the manufacturer fan-spectra registry first by looking
                // up the first selected mechanical-equipment element's family
                // name (e.g. "AHU CL-001 — Trox AT 600"). Falls back to a
                // synthetic Lw derived from path Q + ΔP when no match.
                var acoustic = StingTools.Core.Acoustic.AcousticDataRegistry.Get(doc);
                OctaveBand fanSpectrum;
                string fanLabel;
                var fanFamilyName = FindFanFamilyName(doc, ids);
                var fanMatch = acoustic.FindFan(fanFamilyName);
                if (fanMatch != null)
                {
                    fanSpectrum = fanMatch.Lw;
                    fanLabel = $"{fanMatch.Label} (registry match: '{fanMatch.Match}')";
                }
                else
                {
                    double fanLwTotal = pathFlowLs > 0 && pathDpPa > 0
                        ? 67 + 10 * Math.Log10(pathFlowLs) + 10 * Math.Log10(pathDpPa)
                        : 80;
                    fanSpectrum = OctaveBand.FromArray(new[]
                    {
                        fanLwTotal - 7, fanLwTotal - 5, fanLwTotal - 4, fanLwTotal - 3,
                        fanLwTotal - 4, fanLwTotal - 6, fanLwTotal - 10, fanLwTotal - 14
                    });
                    fanLabel = $"Synthetic fan (Lw≈{fanLwTotal:F0} dB) — add manufacturer spectrum via STING_FAN_SPECTRA.json";
                    assumptions.Add(pathFlowLs > 0 && pathDpPa > 0
                        ? $"Fan Lw estimated from path Q {pathFlowLs:F0} L/s and ΔP {pathDpPa:F0} Pa — no STING_FAN_SPECTRA.json match" +
                          (string.IsNullOrEmpty(fanFamilyName) ? " (no fan in selection)" : $" for '{fanFamilyName}'")
                        : "Fan Lw set to 80 dB — no path flow/pressure and no STING_FAN_SPECTRA.json match");
                }

                path.Insert(0, new PathElement
                {
                    Kind = ElementKind.Fan,
                    Label = fanLabel,
                    SourceLw = fanSpectrum
                });

                // Resolve the receiver room from the actual Revit Space (or
                // Room) that contains the terminal/receiver in the selection.
                // Falls back to the legacy hardcoded 100 m³ / α=0.2 cube when
                // no space is resolvable. The path used is surfaced below.
                var room = ResolveRoomReceiver(doc, ids, out string roomSource);
                if (roomSource.StartsWith("fallback", StringComparison.OrdinalIgnoreCase))
                    assumptions.Add("Receiver room assumed (100 m³, α 0.20) — no Space/Room found at the terminal");
                else if (roomSource.Contains("default α"))
                    assumptions.Add("Room absorption defaulted — finishes carry no absorption data");

                var result = NcPredictionEngine.Compute(path, room);

                var panel = StingResultPanel.Create("HVAC — NC Prediction");
                panel.SetSubtitle($"path {path.Count - 1} segments · flow {pathFlowLs:F0} L/s · ΔP {pathDpPa:F0} Pa · room V={room.VolumeM3:F0} m³ ({roomSource})");
                string grade = assumptions.Count == 0
                    ? "DESIGN BASIS — manufacturer and model data throughout"
                    : $"INDICATIVE — {assumptions.Count} assumed input(s); see BASIS";
                panel.AddSection("RESULT")
                     .Metric("Predicted NC", NcCurves.ExceedsAll(result.RoomLp)
                         ? "above NC 65 (off the tabulated curves)" : $"NC {result.NcRating}")
                     .Metric("Confidence",   grade)
                     .Metric("Fan Lw (1 kHz)", $"{fanSpectrum.Hz1000:F0} dB")
                     .Metric("Room Lw (1 kHz)", $"{result.RoomLw.Hz1000:F0} dB")
                     .Metric("Room Lp (1 kHz)", $"{result.RoomLp.Hz1000:F0} dB");

                panel.AddSection("ROOM MODEL")
                     .Metric("Source",        roomSource)
                     .Metric("Name",          string.IsNullOrEmpty(room.Name) ? "(unnamed)" : room.Name)
                     .Metric("Volume",        $"{room.VolumeM3:F1} m³")
                     .Metric("Surface area",  $"{room.SurfaceAreaM2:F1} m²")
                     .Metric("Avg absorption α", $"{room.AvgAbsorption:F2}");

                panel.AddSection("OCTAVE-BAND Lp dB(A)");
                var bands = OctaveBand.CentreFrequencies;
                var lp = result.RoomLp.AsArray();
                for (int i = 0; i < bands.Length; i++)
                    panel.Text($"{bands[i]:F0} Hz: Lp = {lp[i]:F1} dB");

                panel.AddSection("BASIS");
                if (assumptions.Count == 0) panel.Text("No assumed inputs.");
                foreach (var a in assumptions) panel.Text("Assumed: " + a);
                panel.Text("Not modelled: duct breakout (sound through duct walls), crosstalk, " +
                           "and plant-room airborne paths. Treat NC as the duct-borne path only.");

                panel.AddSection("PER-ELEMENT BREAKDOWN");
                foreach (var pe in result.PerElement)
                {
                    string atten = string.Join("/", pe.AttenDb.AsArray().Select(d => d.ToString("F0")));
                    string regen = string.Join("/", pe.RegenLw.AsArray().Select(d => d.ToString("F0")));
                    panel.Text($"{pe.Element}: atten {atten} · regen {regen}");
                }

                panel.Text($"Method: VDI 2081 / ASHRAE A48 attenuation + Bullock regen + " +
                           $"direct + reverberant room model. Room from {roomSource} — " +
                           "volume + surface area + finish-derived absorption when a Revit " +
                           "Space/Room is resolvable, else the legacy 100 m³ / α=0.20 cube. " +
                           "Fan and silencer spectra from STING_FAN_SPECTRA.json / " +
                           "STING_SILENCER_DATA.json when matched.");
                panel.Show();

                try
                {
                    var p = StingHvacPanel.Instance;
                    if (p != null)
                    {
                        p.PushRunRow($"NC prediction → NC {result.NcRating}", "⬤");

                        // Phase 187b — surface as an issue when the predicted NC
                        // exceeds the office target (35) or healthcare target (30).
                        // Future: read the actual target from the room's HVC_NC_TARGET.
                        int target = 35;
                        if (result.NcRating > target)
                        {
                            p.IssueRows.Add(new HvacIssueRow
                            {
                                Severity   = "⚠",
                                Element    = path.Count > 1 ? path[1].Label : "(path)",
                                Issue      = $"Predicted NC {result.NcRating} exceeds target NC {target}",
                                Suggestion = "Add silencer / lower duct velocity / oversize terminal"
                            });
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacNcPredictionCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Convert a Revit selection of MEPCurves + fittings into an
        /// ordered list of PathElement. The first non-fan element is the
        /// upstream-most segment (by Revit ElementId order — Revit's
        /// pick order isn't preserved, so this is best-effort).
        /// </summary>
        private static List<PathElement> BuildPathFromSelection(
            Document doc, List<ElementId> ids,
            out double maxFlowLs, out double sumDpPa, List<string> assumptions)
        {
            var list = new List<PathElement>();
            maxFlowLs = 0;
            sumDpPa = 0;
            var ordered = OrderAlongConnectors(doc, ids, out bool connected);
            if (!connected && ordered.Count > 1)
                assumptions.Add("Selection is not one connected duct run — path order by element id");
            foreach (var id in ordered)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                var pe = TryToPathElement(el);
                if (pe == null) continue;
                if (pe.Kind == ElementKind.Silencer && pe.Label.EndsWith("(default IL spectrum)"))
                    assumptions.Add($"Silencer '{el.Name}' uses a generic IL spectrum — no STING_SILENCER_DATA.json match");

                if (pe.Kind == ElementKind.StraightDuct)
                {
                    double q = MepUnits.ReadAirFlowLs(el, "HVC_FLOW_LS");
                    if (q <= 0) q = MepUnits.ReadBuiltInFlowLs(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                    if (q > maxFlowLs) maxFlowLs = q;

                    double dpPaPerM = TryReadDouble(el, "HVC_PRESSURE_DROP_PA");
                    sumDpPa += dpPaPerM;
                }
                list.Add(pe);
            }
            // Fittings, dampers and terminals carry no flow of their own in
            // the model: give each the velocity of the nearest duct upstream
            // (else downstream). Only when the run has no duct velocity at
            // all does the old 5 m/s stand, and then it is reported.
            int defaulted = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!double.IsNaN(list[i].VelocityMs)) continue;
                double v = double.NaN;
                for (int j = i - 1; j >= 0 && double.IsNaN(v); j--)
                    if (list[j].Kind == ElementKind.StraightDuct) v = list[j].VelocityMs;
                for (int j = i + 1; j < list.Count && double.IsNaN(v); j++)
                    if (list[j].Kind == ElementKind.StraightDuct) v = list[j].VelocityMs;
                if (double.IsNaN(v))
                {
                    v = list[i].Kind == ElementKind.Diffuser ? 3.0 : 5.0;
                    // A straight duct's velocity feeds no correlation; only
                    // fittings, dampers and terminals regenerate noise from it.
                    if (list[i].Kind != ElementKind.StraightDuct) defaulted++;
                }
                list[i].VelocityMs = v;
            }
            if (defaulted > 0)
                assumptions.Add($"{defaulted} fitting/terminal velocit{(defaulted == 1 ? "y" : "ies")} defaulted (5 m/s fittings, 3 m/s terminals) — no duct velocity on the run");
            foreach (var pe in list.Where(p => p.Kind == ElementKind.Diffuser && p.AreaM2 == 0.05))
                assumptions.Add($"Terminal '{pe.Label}' face area 0.05 m² and 3 m/s face velocity assumed for end reflection");

            // If we got no terminal in the selection, add a synthetic one
            // so the result panel still includes end-reflection.
            if (!list.Any(p => p.Kind == ElementKind.Diffuser))
            {
                assumptions.Add("No terminal in the selection — a 0.05 m², 3 m/s terminal was assumed");
                list.Add(new PathElement
                {
                    Kind = ElementKind.Diffuser,
                    Label = "Synthetic terminal (no diffuser in selection)",
                    VelocityMs = 3.0,
                    AreaM2 = 0.05
                });
            }
            return list;
        }

        /// <summary>
        /// Order the selected elements along their shared connectors, starting
        /// from the end of the chain that carries the most air (the fan side).
        /// <paramref name="connected"/> is false when the selection is not a
        /// single chain; the id order is then kept for the unreached remainder.
        /// </summary>
        private static List<ElementId> OrderAlongConnectors(Document doc, List<ElementId> ids, out bool connected)
        {
            connected = false;
            var set = new HashSet<long>(ids.Select(i => i.Value));
            var adj = new Dictionary<long, HashSet<long>>();
            foreach (var id in ids)
            {
                var n = new HashSet<long>();
                try
                {
                    var el = doc.GetElement(id);
                    ConnectorSet cs = (el as MEPCurve)?.ConnectorManager?.Connectors
                                   ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
                    if (cs != null)
                        foreach (Connector c in cs)
                        {
                            if (!c.IsConnected) continue;
                            foreach (Connector r in c.AllRefs)
                            {
                                long other = r.Owner?.Id?.Value ?? -1;
                                if (other != id.Value && set.Contains(other)) n.Add(other);
                            }
                        }
                }
                catch (Exception ex) { StingLog.Warn($"NC path adjacency {id}: {ex.Message}"); }
                adj[id.Value] = n;
            }

            double Flow(long v)
            {
                try
                {
                    var el = doc.GetElement(new ElementId(v));
                    double q = MepUnits.ReadAirFlowLs(el, "HVC_FLOW_LS");
                    if (q <= 0) q = MepUnits.ReadBuiltInFlowLs(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                    return q;
                }
                catch { return 0; }   // optional read: fittings carry no flow parameter
            }

            var ends = adj.Where(kv => kv.Value.Count <= 1).Select(kv => kv.Key).ToList();
            if (ends.Count == 0) return ids.OrderBy(i => i.Value).ToList();
            bool IsPlant(long v) =>
                doc.GetElement(new ElementId(v))?.Category?.Id.Value == (long)BuiltInCategory.OST_MechanicalEquipment;
            bool IsTerminal(long v) =>
                doc.GetElement(new ElementId(v))?.Category?.Id.Value == (long)BuiltInCategory.OST_DuctTerminal;
            // Fan/AHU end first; never start at a terminal; then the end carrying most air.
            long start = ends.OrderByDescending(IsPlant).ThenBy(IsTerminal)
                             .ThenByDescending(Flow).ThenBy(v => v).First();

            var order = new List<long>();
            var seen = new HashSet<long>();
            long cur = start;
            while (seen.Add(cur))
            {
                order.Add(cur);
                long next = adj[cur].Where(v => !seen.Contains(v)).DefaultIfEmpty(-1).Min();
                if (next < 0) break;
                cur = next;
            }
            connected = order.Count == ids.Count;
            foreach (var id in ids.OrderBy(i => i.Value))
                if (!seen.Contains(id.Value)) order.Add(id.Value);
            return order.Select(v => new ElementId(v)).ToList();
        }

        private static PathElement TryToPathElement(Element el)
        {
            try
            {
                if (el.Category == null) return null;
                var bic = (BuiltInCategory)el.Category.Id.Value;
                if (bic == BuiltInCategory.OST_DuctCurves &&
                    el is Autodesk.Revit.DB.Mechanical.Duct duct)
                {
                    double dia = UnitUtils.ConvertFromInternalUnits(duct.Diameter, UnitTypeId.Millimeters);
                    double w   = UnitUtils.ConvertFromInternalUnits(duct.Width,    UnitTypeId.Millimeters);
                    double h   = UnitUtils.ConvertFromInternalUnits(duct.Height,   UnitTypeId.Millimeters);
                    double areaM2 = (dia > 0)
                        ? Math.PI * Math.Pow(dia * 1e-3, 2) * 0.25
                        : (w > 0 && h > 0 ? w * h * 1e-6 : 0.05);
                    double flowLs = MepUnits.ReadAirFlowLs(el, "HVC_FLOW_LS");
                    if (flowLs <= 0) flowLs = MepUnits.ReadBuiltInFlowLs(el, BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                    double v = (areaM2 > 0 && flowLs > 0) ? (flowLs * 1e-3) / areaM2 : double.NaN;
                    double len = 0;
                    if (duct.Location is LocationCurve lc && lc.Curve != null)
                        len = UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters);
                    return new PathElement
                    {
                        Kind = ElementKind.StraightDuct,
                        Label = double.IsNaN(v) ? $"Straight duct {len:F1} m (no flow)" : $"Straight duct {len:F1} m @ {v:F1} m/s",
                        LengthM = len, VelocityMs = v, AreaM2 = areaM2
                    };
                }
                if (bic == BuiltInCategory.OST_DuctFitting)
                {
                    // Heuristic: family name → elbow / tee / damper
                    string nm = (el.Name ?? "").ToLowerInvariant();
                    var kind = nm.Contains("elbow")  ? ElementKind.Elbow
                             : nm.Contains("tee")    ? ElementKind.Tee
                             : nm.Contains("damper") ? ElementKind.Damper
                             :                         ElementKind.Elbow;
                    return new PathElement { Kind = kind, Label = el.Name, VelocityMs = double.NaN };
                }
                if (bic == BuiltInCategory.OST_DuctAccessory)
                {
                    string nm = (el.Name ?? "").ToLowerInvariant();
                    if (nm.Contains("silencer") || nm.Contains("attenuator"))
                    {
                        // Look up the family name in the silencer IL registry.
                        // Falls back to a generic mid-band default when no match
                        // — see STING_SILENCER_DATA.json for the corporate pack.
                        var acoustic = StingTools.Core.Acoustic.AcousticDataRegistry.Get(el.Document);
                        var match = acoustic.FindSilencer(el.Name);
                        var il = match?.Il ?? OctaveBand.FromArray(new[] { 2.0, 4, 8, 12, 14, 12, 8, 5 });
                        return new PathElement
                        {
                            Kind = ElementKind.Silencer,
                            Label = match != null
                                ? $"{el.Name} → {match.Label}"
                                : el.Name + " (default IL spectrum)",
                            SilencerILdB = il
                        };
                    }
                    return new PathElement { Kind = ElementKind.Damper, Label = el.Name, VelocityMs = double.NaN };
                }
                if (bic == BuiltInCategory.OST_DuctTerminal)
                {
                    return new PathElement
                    {
                        Kind = ElementKind.Diffuser, Label = el.Name,
                        VelocityMs = 3.0, AreaM2 = 0.05
                    };
                }
            }
            catch (Exception ex) { StingLog.Warn($"NcPath element {el.Id}: {ex.Message}"); }
            return null;
        }

        private static double TryReadDouble(Element el, string p)
        {
            try { return el.LookupParameter(p)?.AsDouble() ?? 0; } catch { return 0; }
        }

        /// <summary>
        /// Build the receiver <see cref="RoomReceiver"/> from the actual Revit
        /// <see cref="Space"/> (or Room) that contains the terminal/receiver in
        /// the selection. Volume comes from <c>Space.Volume</c>; surface area is
        /// the enclosing-boundary walls (perimeter × height) plus floor + ceiling;
        /// average absorption is estimated from boundary-finish material names
        /// when discoverable, else a sensible 0.15 default. Falls back to the
        /// legacy hardcoded 100 m³ / α=0.2 cube when no space resolves. The
        /// <paramref name="source"/> string describes which path was taken.
        /// </summary>
        private static RoomReceiver ResolveRoomReceiver(Document doc, List<ElementId> ids, out string source)
        {
            source = "fallback cube (100 m³, α=0.20)";
            try
            {
                var spatial = FindReceiverSpatial(doc, ids);
                if (spatial != null)
                {
                    double volFt3 = (spatial as Space)?.Volume
                        ?? (spatial as Autodesk.Revit.DB.Architecture.Room)?.Volume ?? 0;
                    double volM3 = volFt3 > 0
                        ? UnitUtils.ConvertFromInternalUnits(volFt3, UnitTypeId.CubicMeters)
                        : 0;

                    // Floor / ceiling area from the space's plan area.
                    double areaFt2 = 0;
                    try { areaFt2 = spatial.Area; } catch { }
                    double floorM2 = areaFt2 > 0
                        ? UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters)
                        : 0;

                    // Wall area = boundary perimeter × height. Height from the
                    // space's unbounded height, else volume/area, else 3 m.
                    double heightM = 3.0;
                    try
                    {
                        double hp = spatial.get_Parameter(BuiltInParameter.ROOM_HEIGHT)?.AsDouble() ?? 0;
                        if (hp <= 0) hp = spatial.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET)?.AsDouble() ?? 0;
                        if (hp > 0) heightM = UnitUtils.ConvertFromInternalUnits(hp, UnitTypeId.Meters);
                        else if (volM3 > 0 && floorM2 > 0) heightM = volM3 / floorM2;
                    }
                    catch { if (volM3 > 0 && floorM2 > 0) heightM = volM3 / floorM2; }

                    double perimeterM = 0;
                    double absWeightedSum = 0, absAreaSum = 0;
                    try
                    {
                        var opts = new SpatialElementBoundaryOptions
                        {
                            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                        };
                        var segs = spatial.GetBoundarySegments(opts);
                        if (segs != null)
                        {
                            foreach (var loop in segs)
                            foreach (var seg in loop)
                            {
                                double lenM = UnitUtils.ConvertFromInternalUnits(
                                    seg.GetCurve()?.Length ?? 0, UnitTypeId.Meters);
                                perimeterM += lenM;
                                double wallArea = lenM * heightM;
                                double a = EstimateBoundaryAbsorption(doc, seg.ElementId);
                                if (a > 0) { absWeightedSum += a * wallArea; absAreaSum += wallArea; }
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"NC room boundary: {ex.Message}"); }

                    double wallM2 = perimeterM * heightM;
                    double surfaceM2 = wallM2 + 2 * floorM2;   // walls + floor + ceiling
                    if (surfaceM2 <= 0 && volM3 > 0)
                        surfaceM2 = 6 * Math.Pow(volM3, 2.0 / 3.0);   // cube approximation

                    // Absorption: area-weighted from boundary finishes, blended
                    // with a floor/ceiling default. When no finish resolvable,
                    // use a plastered-room default of 0.15.
                    double wallAlpha = absAreaSum > 0 ? absWeightedSum / absAreaSum : 0.10;
                    // Blend: ceiling often absorptive (acoustic tile ~0.6), floor
                    // hard (~0.05); use a conservative composite when area known.
                    double alpha;
                    if (surfaceM2 > 0)
                    {
                        double floorAlpha = 0.05, ceilAlpha = 0.30;
                        alpha = (wallAlpha * wallM2 + floorAlpha * floorM2 + ceilAlpha * floorM2)
                                / Math.Max(surfaceM2, 1e-6);
                    }
                    else alpha = 0.15;
                    alpha = Math.Max(0.05, Math.Min(0.60, alpha));

                    if (volM3 > 0)
                    {
                        source = absAreaSum > 0
                            ? $"Revit {(spatial is Space ? "Space" : "Room")} (finishes → α)"
                            : $"Revit {(spatial is Space ? "Space" : "Room")} (default α)";
                        return new RoomReceiver
                        {
                            Name = string.IsNullOrEmpty(spatial.Name) ? spatial.Id.ToString() : spatial.Name,
                            VolumeM3 = volM3,
                            SurfaceAreaM2 = surfaceM2,
                            AvgAbsorption = alpha,
                            Directivity = 2,
                            ListenerDistanceM = 1.5
                        };
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveRoomReceiver: {ex.Message}"); }

            // Legacy hardcoded cube fallback.
            return new RoomReceiver
            {
                Name = "Receiver",
                VolumeM3 = 100,
                SurfaceAreaM2 = 6 * Math.Pow(100, 2.0 / 3.0),
                AvgAbsorption = 0.2,
                Directivity = 2,
                ListenerDistanceM = 1.5
            };
        }

        /// <summary>
        /// Find the Space (or Room) containing the receiver. Prefers the
        /// terminal/diffuser in the selection; uses its location point to
        /// query <c>Document.GetSpaceAtPoint</c> (falling back to
        /// <c>GetRoomAtPoint</c>). Returns null when nothing resolves.
        /// </summary>
        private static SpatialElement FindReceiverSpatial(Document doc, List<ElementId> ids)
        {
            try
            {
                // Prefer the air-terminal element; else the last-picked element
                // that carries a location point.
                Element terminal = null;
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el?.Category == null) continue;
                    var bic = (BuiltInCategory)el.Category.Id.Value;
                    if (bic == BuiltInCategory.OST_DuctTerminal) { terminal = el; break; }
                    if (terminal == null && el.Location is LocationPoint) terminal = el;
                }
                if (terminal == null) return null;

                XYZ pt = null;
                if (terminal.Location is LocationPoint lp) pt = lp.Point;
                else if (terminal.Location is LocationCurve lc && lc.Curve != null)
                    pt = lc.Curve.Evaluate(0.5, true);
                if (pt == null) return null;

                // GetSpaceAtPoint / GetRoomAtPoint need a valid phase.
                Phase phase = null;
                try
                {
                    var pid = terminal.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId();
                    if (pid != null && pid != ElementId.InvalidElementId)
                        phase = doc.GetElement(pid) as Phase;
                }
                catch { }

                Space sp = null;
                try { sp = phase != null ? doc.GetSpaceAtPoint(pt, phase) : doc.GetSpaceAtPoint(pt); }
                catch { }
                if (sp != null && sp.Volume > 0) return sp;

                Autodesk.Revit.DB.Architecture.Room rm = null;
                try { rm = phase != null ? doc.GetRoomAtPoint(pt, phase) : doc.GetRoomAtPoint(pt); }
                catch { }
                if (rm != null) return rm;
            }
            catch (Exception ex) { StingLog.Warn($"FindReceiverSpatial: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Estimate a Sabine absorption coefficient (500 Hz-ish average) for a
        /// boundary element from its material / type name keywords. Returns 0
        /// when the element is not a wall or no keyword matches so the caller
        /// can exclude it from the area-weighted average.
        /// </summary>
        private static double EstimateBoundaryAbsorption(Document doc, ElementId boundaryId)
        {
            try
            {
                var el = doc.GetElement(boundaryId);
                if (el == null) return 0;
                string name = ($"{el.Name} {el.Category?.Name}").ToLowerInvariant();
                // Pull the wall type name / finish material name too when a Wall.
                if (el is Wall w)
                {
                    name += " " + (w.WallType?.Name ?? "").ToLowerInvariant();
                }
                if (name.Contains("glaz") || name.Contains("glass") || name.Contains("window"))
                    return 0.05;
                if (name.Contains("acoustic") || name.Contains("absorb") || name.Contains("perforat"))
                    return 0.60;
                if (name.Contains("carpet") || name.Contains("fabric") || name.Contains("curtain"))
                    return 0.35;
                if (name.Contains("plaster") || name.Contains("gypsum") || name.Contains("drywall")
                    || name.Contains("partition"))
                    return 0.10;
                if (name.Contains("concrete") || name.Contains("block") || name.Contains("masonry")
                    || name.Contains("brick") || name.Contains("tile"))
                    return 0.03;
                if (name.Contains("wall")) return 0.08;   // generic painted wall
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Look through the user's selection for a mechanical-equipment
        /// family. The first one found provides the family-name string
        /// used to look up a fan Lw spectrum in the registry.
        /// </summary>
        private static string FindFanFamilyName(Document doc, List<ElementId> ids)
        {
            try
            {
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null || el.Category == null) continue;
                    var bic = (BuiltInCategory)el.Category.Id.Value;
                    if (bic == BuiltInCategory.OST_MechanicalEquipment ||
                        bic == BuiltInCategory.OST_DuctAccessory)
                    {
                        if (el is FamilyInstance fi)
                        {
                            // Build a composite "Family — Type" string so the
                            // substring match catches both "AHU" family names
                            // and "Trox AT" type names.
                            return $"{fi.Symbol?.Family?.Name} {fi.Symbol?.Name} {fi.Name}".Trim();
                        }
                        return el.Name;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindFanFamilyName: {ex.Message}"); }
            return "";
        }
    }
}
