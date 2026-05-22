// StingTools — NC prediction command.
//
// Walks an active duct selection from the active view (treating the
// upstream-most member as the fan source) and computes the predicted
// NC at the room downstream of the terminal diffuser. Uses
// NcPredictionEngine to accumulate attenuation + regenerated noise
// along the path and renders the breakdown in a StingResultPanel.
//
// For now the fan-source sound power is approximated from the
// upstream duct's velocity (Madison's fan-noise empirical formula):
//     Lw = 67 + 10·log10(Q) + 10·log10(ΔP)
// where Q in L/s, ΔP in Pa. A future phase will read the actual
// manufacturer Lw spectrum from a fan-curve sidecar.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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

                var path = BuildPathFromSelection(doc, ids, out double pathFlowLs, out double pathDpPa);
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
                }

                path.Insert(0, new PathElement
                {
                    Kind = ElementKind.Fan,
                    Label = fanLabel,
                    SourceLw = fanSpectrum
                });

                var room = new RoomReceiver
                {
                    Name = "Receiver",
                    VolumeM3 = 100,
                    SurfaceAreaM2 = 6 * Math.Pow(100, 2.0 / 3.0),
                    AvgAbsorption = 0.2,
                    Directivity = 2,
                    ListenerDistanceM = 1.5
                };

                var result = NcPredictionEngine.Compute(path, room);

                var panel = StingResultPanel.Create("HVAC — NC Prediction");
                panel.SetSubtitle($"path {path.Count - 1} segments · flow {pathFlowLs:F0} L/s · ΔP {pathDpPa:F0} Pa · room V={room.VolumeM3:F0} m³");
                panel.AddSection("RESULT")
                     .Metric("Predicted NC", $"NC {result.NcRating}")
                     .Metric("Fan Lw (1 kHz)", $"{fanSpectrum.Hz1000:F0} dB")
                     .Metric("Room Lw (1 kHz)", $"{result.RoomLw.Hz1000:F0} dB")
                     .Metric("Room Lp (1 kHz)", $"{result.RoomLp.Hz1000:F0} dB");

                panel.AddSection("OCTAVE-BAND Lp dB(A)");
                var bands = OctaveBand.CentreFrequencies;
                var lp = result.RoomLp.AsArray();
                for (int i = 0; i < bands.Length; i++)
                    panel.Text($"{bands[i]:F0} Hz: Lp = {lp[i]:F1} dB");

                panel.AddSection("PER-ELEMENT BREAKDOWN");
                foreach (var pe in result.PerElement)
                {
                    string atten = string.Join("/", pe.AttenDb.AsArray().Select(d => d.ToString("F0")));
                    string regen = string.Join("/", pe.RegenLw.AsArray().Select(d => d.ToString("F0")));
                    panel.Text($"{pe.Element}: atten {atten} · regen {regen}");
                }

                panel.Text("Method: VDI 2081 / ASHRAE A48 attenuation + Bullock regen + " +
                           "direct + reverberant room model. Synthetic fan Lw " +
                           "derived from path Q+ΔP — replace with manufacturer spectrum for definitive NC.");
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
            out double maxFlowLs, out double sumDpPa)
        {
            var list = new List<PathElement>();
            maxFlowLs = 0;
            sumDpPa = 0;
            foreach (var id in ids.OrderBy(i => i.Value))
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                var pe = TryToPathElement(el);
                if (pe == null) continue;

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
            // If we got no terminal in the selection, add a synthetic one
            // so the result panel still includes end-reflection.
            if (!list.Any(p => p.Kind == ElementKind.Diffuser))
            {
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
                    double v = (areaM2 > 0 && flowLs > 0) ? (flowLs * 1e-3) / areaM2 : 3.0;
                    double len = 0;
                    if (duct.Location is LocationCurve lc && lc.Curve != null)
                        len = UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters);
                    return new PathElement
                    {
                        Kind = ElementKind.StraightDuct,
                        Label = $"Straight duct {len:F1} m @ {v:F1} m/s",
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
                    return new PathElement { Kind = kind, Label = el.Name, VelocityMs = 5.0 };
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
                    return new PathElement { Kind = ElementKind.Damper, Label = el.Name, VelocityMs = 5.0 };
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
