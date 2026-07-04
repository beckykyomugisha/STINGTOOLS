using StingTools.Core;
// StingTools — SleeveConnectorEngine ("sleeve method").
//
// THE PROBLEM
//   After Seeds_SwapToManufacturer, vendor .rfa families almost never
//   carry a Domain.DomainCableTrayConduit connector. Market conduit tools
//   (ricaun EasyConduit, ConduiTool, Automatic Conduit, EVOLVE) all REQUIRE
//   a conduit connector and NONE author one, so a placed manufacturer
//   socket / switch / isolator / data outlet is un-routable: AutoConduitDrop
//   finds no free conduit connector and either falls back to the family
//   LocationPoint (imprecise) or skips it, leaving orphaned open connectors.
//
// THE FIX (owning connector authorship = the differentiator)
//   For every target fixture that lacks a free conduit connector, author a
//   short conduit STUB (Conduit.Create on a 20/25 mm conduit type) rising
//   out of the fixture face. A Conduit always exposes two real
//   Domain.DomainCableTrayConduit connectors; the OUTWARD (far) end is left
//   free, so AutoConduitDrop.FindBestFreeConnector() has a genuine,
//   correctly-oriented conduit terminal to extend to the nearest cable tray
//   / conduit. The engine that placed the fixtures now hands routing a
//   first-class conduit terminal instead of an insertion point.
//
// GUARANTEES (the hard requirements for this engine)
//   * Idempotent — a second run over the same fixtures places nothing new.
//     Detection is geometric (an existing conduit endpoint connector within
//     ProximityTolMm of the terminal) AND marker-based (ELC_CDT_INSTALL_
//     METHOD_TXT == SleeveMarker), so it survives even when the conduit
//     type does not carry the STING shared param.
//   * Dry-run / preview — Run(fixtures, dryRun:true) computes every terminal
//     point + count WITHOUT opening a transaction or touching geometry, so
//     the command can report "would place N stubs here" from the dev box
//     where it cannot be Revit-tested.
//   * Never author the wrong DOMAIN — the stub is a Conduit, whose
//     connectors are CableTrayConduit by construction. No Electrical (power)
//     connectors are minted where routing needs conduit.
//
// TUNABLE via MepSizingRegistry project override
//   (<project>/_BIM_COORD/mep_sizing_rules.json → conduit.sleeveStubSizeMm /
//   conduit.sleeveStubLengthMm).

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Mep
{
    /// <summary>One planned or placed sleeve-connector stub.</summary>
    public class SleeveConnectorItem
    {
        public ElementId FixtureId { get; set; }
        public string FixtureName { get; set; } = "";
        public XYZ TerminalFt { get; set; }
        public double StubSizeMm { get; set; }
        public double StubLengthMm { get; set; }
        public ElementId StubId { get; set; } = ElementId.InvalidElementId;
    }

    public class SleeveConnectorResult
    {
        public bool DryRun { get; set; }
        /// <summary>Fixtures that already carry a free conduit connector — no stub needed.</summary>
        public int AlreadyRoutable { get; set; }
        /// <summary>Fixtures skipped because a STING sleeve stub is already present (idempotency).</summary>
        public int AlreadySleeved { get; set; }
        /// <summary>Fixtures that got (or, in dry-run, would get) a new stub.</summary>
        public int Sleeved { get; set; }
        public int Failed { get; set; }
        public int Considered { get; set; }
        public List<SleeveConnectorItem> Items { get; } = new List<SleeveConnectorItem>();
        public List<ElementId> CreatedIds { get; } = new List<ElementId>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Authors conduit-connector stubs on connector-less fixtures. One
    /// instance per command invocation; not thread-safe.
    /// </summary>
    public class SleeveConnectorEngine
    {
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>Marker written to ELC_CDT_INSTALL_METHOD_TXT on stubs so
        /// re-runs and downstream QA recognise a STING-authored sleeve.</summary>
        public const string SleeveMarker = "STING_SLEEVE_STUB";
        private const string InstallMethodParam = "ELC_CDT_INSTALL_METHOD_TXT";
        private const string FabMethodParam = "ELC_CDT_FAB_METHOD_TXT";
        private const string DropOffsetParam = "FIXTURE_DROP_OFFSET_Z_MM";

        private readonly Document _doc;

        /// <summary>Stub diameter (mm). Seeded from MepSizingRegistry; overridable.</summary>
        public double StubSizeMm { get; set; }
        /// <summary>Stub length (mm). Seeded from MepSizingRegistry; overridable.</summary>
        public double StubLengthMm { get; set; }
        /// <summary>Idempotency / connector-coincidence tolerance (mm). 50 mm ≈ a
        /// conduit entry stub with locknut + bushing.</summary>
        public double ProximityTolMm { get; set; } = 50.0;
        /// <summary>Conduit type used for stubs. Auto-resolved when unset.</summary>
        public ElementId ConduitTypeId { get; set; } = ElementId.InvalidElementId;

        public SleeveConnectorEngine(Document doc)
        {
            _doc = doc;
            var rules = MepSizingRegistry.Get(doc);
            StubSizeMm   = rules?.ConduitSleeveStubSizeMm  > 0 ? rules.ConduitSleeveStubSizeMm  : 20.0;
            StubLengthMm = rules?.ConduitSleeveStubLengthMm > 0 ? rules.ConduitSleeveStubLengthMm : 150.0;
        }

        /// <summary>
        /// Plan (dryRun) or place conduit stubs for the supplied fixtures.
        /// Live runs open their own transaction; dry-runs touch nothing.
        /// </summary>
        public SleeveConnectorResult Run(IList<Element> fixtures, bool dryRun)
        {
            var result = new SleeveConnectorResult { DryRun = dryRun };
            if (fixtures == null || fixtures.Count == 0)
            {
                result.Warnings.Add("SleeveConnectorEngine: no fixtures supplied.");
                return result;
            }

            // Resolve a conduit type up-front (needed for live and to report a
            // meaningful dry-run — no type means nothing could be placed).
            ResolveConduitType(result);
            bool haveType = ConduitTypeId != null && ConduitTypeId != ElementId.InvalidElementId;
            if (!haveType && !dryRun)
            {
                result.Warnings.Add("SleeveConnectorEngine: no ConduitType found in project; cannot place stubs.");
                return result;
            }

            // Plan every fixture first (pure read). Live placement re-uses the plan.
            var plan = new List<SleeveConnectorItem>();
            foreach (var fx in fixtures)
            {
                if (fx == null) continue;
                result.Considered++;
                try
                {
                    if (HasFreeConduitConnector(fx)) { result.AlreadyRoutable++; continue; }

                    XYZ terminal = ComputeTerminal(fx);
                    if (terminal == null)
                    {
                        result.Warnings.Add($"Fixture {fx.Id}: no LocationPoint; cannot place a stub.");
                        result.Failed++;
                        continue;
                    }

                    if (HasConduitConnectorNear(terminal))
                    {
                        // A real conduit terminal or a prior STING stub is already here.
                        result.AlreadySleeved++;
                        continue;
                    }

                    plan.Add(new SleeveConnectorItem
                    {
                        FixtureId    = fx.Id,
                        FixtureName  = SafeName(fx),
                        TerminalFt   = terminal,
                        StubSizeMm   = StubSizeMm,
                        StubLengthMm = StubLengthMm,
                    });
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Warnings.Add($"Fixture {fx?.Id}: plan failed — {ex.Message}");
                }
            }

            if (dryRun)
            {
                result.Sleeved = plan.Count;
                result.Items.AddRange(plan);
                if (!haveType)
                    result.Warnings.Add("Dry-run: no ConduitType resolved — a live run would abort until one exists.");
                return result;
            }

            using (var tx = new Transaction(_doc, "STING Place Sleeve Connectors"))
            {
                try { tx.Start(); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Transaction start failed: {ex.Message}");
                    return result;
                }
                try
                {
                    foreach (var item in plan)
                    {
                        try
                        {
                            var id = PlaceStub(item, result);
                            if (id != null && id != ElementId.InvalidElementId)
                            {
                                item.StubId = id;
                                result.CreatedIds.Add(id);
                                result.Sleeved++;
                                result.Items.Add(item);
                            }
                            else result.Failed++;
                        }
                        catch (Exception ex)
                        {
                            result.Failed++;
                            result.Warnings.Add($"Fixture {item.FixtureId}: stub placement failed — {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"SleeveConnectorEngine fatal: {ex.Message}");
                }
            }
            return result;
        }

        // ---- planning helpers ---------------------------------------------------

        /// <summary>Terminal point = fixture LocationPoint, raised by
        /// FIXTURE_DROP_OFFSET_Z_MM when the (legacy connector-less) family
        /// carries that hint so the stub starts at back-box height.</summary>
        private XYZ ComputeTerminal(Element fx)
        {
            var lp = fx.Location as LocationPoint;
            if (lp?.Point == null) return null;
            XYZ p = lp.Point;
            double offMm = ReadDropOffsetMm(fx);
            if (Math.Abs(offMm) > 1e-6) p = new XYZ(p.X, p.Y, p.Z + offMm * MmToFt);
            return p;
        }

        private static double ReadDropOffsetMm(Element fx)
        {
            try
            {
                var p = fx.LookupParameter(DropOffsetParam);
                if (p == null || !p.HasValue) return 0.0;
                if (p.StorageType == StorageType.Double)
                    return p.AsDouble() / MmToFt;             // stored internal feet → mm
                if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double s))
                    return s;                                 // stored as plain mm text
            }
            catch (Exception ex) { StingLog.Warn($"SleeveConnectorEngine: read {DropOffsetParam} failed: {ex.Message}"); }
            return 0.0;
        }

        /// <summary>True when the element already exposes a FREE
        /// Domain.DomainCableTrayConduit connector — routing is already
        /// possible, so no stub is authored.</summary>
        private static bool HasFreeConduitConnector(Element el)
        {
            foreach (var c in GetConnectors(el))
            {
                bool connected;
                try { connected = c.IsConnected; } catch { continue; }
                if (connected) continue;
                Domain d;
                try { d = c.Domain; } catch { continue; }
                if (d == Domain.DomainCableTrayConduit) return true;
            }
            return false;
        }

        /// <summary>Idempotency probe — is there already a conduit connector
        /// (real terminal or prior STING stub) within ProximityTolMm of the
        /// terminal point?</summary>
        private bool HasConduitConnectorNear(XYZ terminal)
        {
            double tolFt = ProximityTolMm * MmToFt;
            try
            {
                var col = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    var mgr = (el as MEPCurve)?.ConnectorManager;
                    if (mgr == null) continue;
                    foreach (Connector c in mgr.Connectors)
                    {
                        if (c == null) continue;
                        try
                        {
                            if (c.ConnectorType != ConnectorType.End) continue;
                            if (c.Origin != null && c.Origin.DistanceTo(terminal) <= tolFt) return true;
                        }
                        catch { /* connector with no origin — skip */ }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SleeveConnectorEngine: idempotency scan failed: {ex.Message}"); }
            return false;
        }

        // ---- placement ----------------------------------------------------------

        private ElementId PlaceStub(SleeveConnectorItem item, SleeveConnectorResult result)
        {
            XYZ from = item.TerminalFt;
            XYZ to   = new XYZ(from.X, from.Y, from.Z + StubLengthMm * MmToFt); // rise +Z

            ElementId levelId = ResolveLevel(from, result);
            if (levelId == ElementId.InvalidElementId)
            {
                result.Warnings.Add($"Fixture {item.FixtureId}: no level for stub; skipped.");
                return ElementId.InvalidElementId;
            }

            var cdt = Conduit.Create(_doc, ConduitTypeId, from, to, levelId);
            if (cdt == null) return ElementId.InvalidElementId;

            // Set nominal diameter so the stub reads as a real 20/25 mm conduit
            // and its connectors size-match the project conduit size list.
            try
            {
                var dp = cdt.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
                if (dp != null && !dp.IsReadOnly) dp.Set(StubSizeMm * MmToFt);
            }
            catch (Exception ex) { result.Warnings.Add($"Fixture {item.FixtureId}: set stub diameter failed — {ex.Message}"); }

            TrySetString(cdt, InstallMethodParam, SleeveMarker);
            TrySetString(cdt, FabMethodParam, "SITE");
            return cdt.Id;
        }

        private void ResolveConduitType(SleeveConnectorResult result)
        {
            if (ConduitTypeId != null && ConduitTypeId != ElementId.InvalidElementId) return;
            try
            {
                foreach (var el in new FilteredElementCollector(_doc).OfClass(typeof(ConduitType)))
                    if (el is ConduitType ct) { ConduitTypeId = ct.Id; break; }
            }
            catch (Exception ex) { result.Warnings.Add($"Resolve ConduitType: {ex.Message}"); }
        }

        private ElementId ResolveLevel(XYZ origin, SleeveConnectorResult result)
        {
            ElementId levelId = ElementId.InvalidElementId;
            try
            {
                if (_doc.ActiveView != null)
                    levelId = _doc.ActiveView.GenLevel?.Id ?? ElementId.InvalidElementId;

                if (levelId == ElementId.InvalidElementId)
                {
                    Level nearest = null;
                    double nearestDelta = double.MaxValue;
                    foreach (var lvlEl in new FilteredElementCollector(_doc).OfClass(typeof(Level)))
                    {
                        if (!(lvlEl is Level lvl)) continue;
                        double delta = origin.Z - lvl.Elevation;
                        if (delta >= 0 && delta < nearestDelta) { nearestDelta = delta; nearest = lvl; }
                    }
                    // If the origin is below every level, take the lowest one.
                    if (nearest == null)
                    {
                        foreach (var lvlEl in new FilteredElementCollector(_doc).OfClass(typeof(Level)))
                        {
                            if (!(lvlEl is Level lvl)) continue;
                            if (nearest == null || lvl.Elevation < nearest.Elevation) nearest = lvl;
                        }
                    }
                    if (nearest != null) levelId = nearest.Id;
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Level resolve: {ex.Message}"); }
            return levelId;
        }

        // ---- shared low-level helpers -------------------------------------------

        private static IEnumerable<Connector> GetConnectors(Element el)
        {
            ConnectorManager cm = null;
            try
            {
                if (el is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
                else if (el is MEPCurve mc)  cm = mc.ConnectorManager;
            }
            catch { cm = null; }
            if (cm == null) yield break;
            ConnectorSet set;
            try { set = cm.Connectors; } catch { yield break; }
            if (set == null) yield break;
            foreach (Connector c in set) if (c != null) yield return c;
        }

        private static string SafeName(Element el)
        {
            try { return el?.Name ?? ""; } catch { return ""; }
        }

        private void TrySetString(Element el, string paramName, string value)
        {
            if (el == null || string.IsNullOrEmpty(paramName) || value == null) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"SleeveConnectorEngine: set {paramName} failed: {ex.Message}"); }
        }
    }
}
