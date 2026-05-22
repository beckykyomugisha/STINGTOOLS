// StingTools — Block-load command.
//
// Drives BlockLoadEngine against Revit Spaces (or Rooms when Spaces
// aren't bound). Honours the HVAC panel scope radio and writes per-
// space peak loads back as STING shared parameters so downstream
// commands (equipment selection, schedules, sustainability) can read
// them.
//
// What this gives the user over Revit's native loads:
//   - peak-picks at the SYSTEM level rather than summing per-zone peaks
//   - reports the diversity factor explicitly
//   - uses a location-aware climate site rather than a hardcoded design day
//
// Falls back to sensible defaults when a Space lacks envelope geometry —
// the result panel surfaces every skipped zone so users know exactly
// what data is missing before trusting the number.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Climate;
using StingTools.Core.Hvac.Loads;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacBlockLoadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var site = ClimateRegistry.ActiveSite(doc);
                // Snapshot the header context atomically (Phase 187c).
                var snap = StingHvacCommandHandler.Snapshot();
                bool cooling = string.IsNullOrEmpty(snap.LoadCode)
                            || !snap.LoadCode.ToLowerInvariant().Contains("heat");
                string scope = snap.Scope ?? "Project";

                var (zones, skipped) = CollectZones(ctx, scope);
                if (zones.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Block Load",
                        $"No spaces/rooms in scope ({scope}). Place at least one Revit Space first.");
                    return Result.Cancelled;
                }

                // RTS class — resolved from PRJ_RTS_CLASS_TXT on Project Info
                // (Reactive / Light / Medium / Heavy). Defaults to Reactive
                // when unset so legacy projects see the same number.
                var rts = StingTools.Core.Hvac.Loads.RtsConstructionClass.Reactive;
                try
                {
                    string rtsTxt = doc.ProjectInformation?.LookupParameter("PRJ_RTS_CLASS_TXT")?.AsString();
                    if (!string.IsNullOrWhiteSpace(rtsTxt) &&
                        Enum.TryParse(rtsTxt, true, out StingTools.Core.Hvac.Loads.RtsConstructionClass r))
                        rts = r;
                }
                catch { }

                var results = BlockLoadEngine.Run(zones, site, cooling, rts);
                double grand = results.Sum(r => r.BlockSensibleW);
                double sumPeaks = results.Sum(r => r.SumOfPeaksSensibleW);
                double diversity = sumPeaks > 0 ? grand / sumPeaks : 1.0;

                // Stamp per-space peaks back onto Revit so schedules see them.
                int stamped = 0;
                int stampCandidates = results.Sum(s => s.Zones.Count);
                if (stampCandidates > 0)
                {
                    using (var tx = new Transaction(doc, "STING Block-load Stamp"))
                    {
                        tx.Start();
                        foreach (var sys in results)
                        foreach (var z in sys.Zones)
                        {
                            var el = doc.GetElement(new ElementId(ParseLong(z.ZoneId)));
                            if (el == null) continue;
                            try
                            {
                                if (ParameterHelpers.SetString(el, "HVC_PEAK_SENS_W",
                                    $"{z.PeakSensibleW:F0}", overwrite: true)) stamped++;
                                ParameterHelpers.SetString(el, "HVC_PEAK_LAT_W",
                                    $"{z.PeakLatentW:F0}", overwrite: true);
                                ParameterHelpers.SetString(el, "HVC_PEAK_HOUR",
                                    $"{z.PeakHour:D2}:00", overwrite: true);
                                ParameterHelpers.SetString(el, "HVC_OA_LS",
                                    $"{z.OaLs:F1}", overwrite: true);
                                // Phase 187f — clear the stale flag that the
                                // envelope IUpdater may have set since the
                                // previous BlockLoad run.
                                ParameterHelpers.SetInt(el, "HVC_LOAD_STALE_BOOL", 0, overwrite: true);
                                ParameterHelpers.SetString(el, "HVC_LOAD_STALE_REASON_TXT", "", overwrite: true);
                            }
                            catch (Exception ex) { StingLog.Warn($"Block-load stamp {el.Id}: {ex.Message}"); }
                        }
                        tx.Commit();
                    }
                }

                // Result panel
                var panel = StingResultPanel.Create("HVAC — Block Load");
                panel.SetSubtitle($"site={site.Label} ({site.Cooling996DbC:F1}/{site.Cooling996McwbC:F1} °C) · " +
                                  $"ρ={site.AirDensityCoolingKgM3():F3} kg/m³ · " +
                                  $"{(cooling ? "Cooling" : "Heating")} · RTS={rts} · scope={scope}");
                panel.AddSection("BUILDING TOTAL")
                     .Metric("Block (peak) sensible", $"{grand / 1000:F1} kW")
                     .Metric("Σ per-zone peaks",     $"{sumPeaks / 1000:F1} kW")
                     .Metric("Diversity factor",     $"{diversity:F2}")
                     .Metric("Spaces sized",         zones.Count.ToString())
                     .Metric("Skipped (no data)",    skipped.ToString())
                     .Metric("Stamped HVC_PEAK_*",   stamped.ToString());

                panel.AddSection("PER SYSTEM");
                foreach (var s in results.OrderByDescending(r => r.BlockSensibleW))
                {
                    panel.Text($"{s.SystemId} · block {s.BlockSensibleW / 1000:F1} kW @ {s.BlockHour:D2}:00 · " +
                               $"diversity {s.DiversityFactor:F2} · {s.Zones.Count} zones");
                }

                panel.AddSection("TOP-10 ZONES BY PEAK");
                foreach (var z in results.SelectMany(r => r.Zones)
                                         .OrderByDescending(r => r.PeakSensibleW).Take(10))
                {
                    panel.Text($"{z.ZoneName} · {z.PeakSensibleW / 1000:F1} kW @ {z.PeakHour:D2}:00 · " +
                               $"{z.AreaM2:F0} m² · OA {z.OaLs:F0} L/s");
                }

                panel.Text("Block load = max over 24 hourly system totals. " +
                           "Diversity < 1 means zone peaks don't coincide; size plant to BLOCK, not Σpeaks.");
                panel.Show();

                try
                {
                    var p = StingHvacPanel.Instance;
                    if (p != null)
                    {
                        p.PushRunRow($"Block-load ({grand / 1000:F0} kW, div {diversity:F2})", "⬤");

                        // Phase 187b — populate the previously-empty LoadsTab grid.
                        // Replace the contents wholesale so re-runs reflect the latest pass.
                        p.SpaceLoadRows.Clear();
                        foreach (var z in results.SelectMany(r => r.Zones)
                                                .OrderByDescending(r => r.PeakSensibleW))
                        {
                            string warn = "";
                            if (z.PeakSensibleW <= 0) warn = "no load";
                            else if (z.OaLs <= 0)     warn = "no OA";
                            p.SpaceLoadRows.Add(new HvacSpaceLoadRow
                            {
                                SpaceName = z.ZoneName,
                                SpaceType = z.SystemId,
                                AreaM2    = z.AreaM2,
                                People    = 0,                                  // populated in F-3 phase
                                HeatingKw = 0,                                  // cooling-only pass for now
                                CoolingKw = z.PeakSensibleW / 1000.0,
                                OAls      = z.OaLs,
                                Warning   = warn
                            });
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacBlockLoadCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Zone collection ─────────────────────────────────────────

        private static (List<LoadZone> zones, int skipped) CollectZones(StingCommandContext ctx, string scope)
        {
            var doc = ctx.Doc;
            var zones = new List<LoadZone>();
            int skipped = 0;
            // Phase 187b — per-space-type schedules + densities.
            var profileLib = StingTools.Core.Hvac.Loads.LoadProfileRegistry.Get(doc);
            // Phase 187c — construction profile (U-values, SHGC) per project.
            var construction = StingTools.Core.Hvac.Loads.ConstructionProfileRegistry.Active(doc);

            // Prefer MEP Spaces (have ventilation rates + design temps).
            var spaces = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .Where(s => s.Area > 0)
                .ToList();

            if (spaces.Count == 0)
            {
                // Fall back to architectural Rooms with default vent rates.
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 0)
                    .ToList();
                foreach (var r in rooms)
                {
                    var z = ZoneFromRoom(r, profileLib, construction);
                    if (z != null) zones.Add(z); else skipped++;
                }
            }
            else
            {
                foreach (var s in spaces)
                {
                    var z = ZoneFromSpace(s, profileLib, construction);
                    if (z != null) zones.Add(z); else skipped++;
                }
            }

            return (zones, skipped);
        }

        private static LoadZone ZoneFromSpace(Space s,
            StingTools.Core.Hvac.Loads.LoadProfileLibrary profileLib,
            StingTools.Core.Hvac.Loads.ConstructionProfile construction)
        {
            try
            {
                double areaM2 = UnitUtils.ConvertFromInternalUnits(s.Area, UnitTypeId.SquareMeters);
                double heightM = UnitUtils.ConvertFromInternalUnits(s.UnboundedHeight, UnitTypeId.Meters);
                if (heightM <= 0.1) heightM = 3.0; // sane default

                // Resolve space-type id: STING shared param first, then Revit
                // Space Type name. Profile library applies all of:
                //   occupant + lighting + equipment density
                //   schedules (24h arrays)
                //   OA per-person + per-m²
                //   setpoints, infiltration
                string spaceTypeId = s.LookupParameter("HVC_SPACE_TYPE_TXT")?.AsString();
                if (string.IsNullOrWhiteSpace(spaceTypeId))
                {
                    try
                    {
                        var st = s.SpaceType;
                        if (st != SpaceType.NoSpaceType) spaceTypeId = st.ToString();
                    }
                    catch { }
                }
                var profile = profileLib?.Get(spaceTypeId) ?? new StingTools.Core.Hvac.Loads.LoadProfile();

                var z = new LoadZone
                {
                    Id          = s.Id.Value.ToString(),
                    Name        = string.IsNullOrEmpty(s.Name) ? $"Space {s.Id}" : s.Name,
                    SystemId    = ResolveSystemId(s),
                    SpaceTypeId = string.IsNullOrEmpty(profile.Id) ? "Office" : profile.Id,
                    FloorAreaM2 = areaM2,
                    HeightM     = heightM
                };
                profile.ApplyTo(z);

                // Honour explicit Revit Space airflow if the user has set
                // "Specified Supply Airflow per area" — overrides the profile.
                double designOa = TryReadFlowByName(s, "Specified Supply Airflow per area");
                if (designOa <= 0) designOa = TryReadFlowByName(s, "Calculated Supply Airflow per area");
                if (designOa > 0) z.OaLpsPerM2 = designOa;

                // Occupant count: prefer explicit Revit param, else derive from
                // the profile's density (e.g. Kitchen = 5 m²/person → 12 ppl in
                // a 60 m² space, vs Office's 10 → 6 ppl).
                int occ = TryReadIntByName(s, "Number of People");
                if (occ <= 0) occ = profile.OccupantCountFor(areaM2);
                z.OccupantCount = occ;

                AddPerimeterEnvelope(s, z, construction);
                return z;
            }
            catch (Exception ex) { StingLog.Warn($"ZoneFromSpace {s.Id}: {ex.Message}"); return null; }
        }

        private static LoadZone ZoneFromRoom(Autodesk.Revit.DB.Architecture.Room r,
            StingTools.Core.Hvac.Loads.LoadProfileLibrary profileLib,
            StingTools.Core.Hvac.Loads.ConstructionProfile construction)
        {
            try
            {
                double areaM2 = UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters);
                double heightM = UnitUtils.ConvertFromInternalUnits(r.UnboundedHeight, UnitTypeId.Meters);
                if (heightM <= 0.1) heightM = 3.0;

                // Room-only fallback path: no Space Type binding, use Department
                // name (if filled) as the profile key. Default to Office.
                string spaceTypeId = r.LookupParameter("Department")?.AsString();
                if (string.IsNullOrWhiteSpace(spaceTypeId))
                    spaceTypeId = r.LookupParameter("HVC_SPACE_TYPE_TXT")?.AsString() ?? "Office";
                var profile = profileLib?.Get(spaceTypeId) ?? new StingTools.Core.Hvac.Loads.LoadProfile();

                var z = new LoadZone
                {
                    Id          = r.Id.Value.ToString(),
                    Name        = string.IsNullOrEmpty(r.Name) ? $"Room {r.Id}" : r.Name,
                    SystemId    = "(default)",
                    SpaceTypeId = string.IsNullOrEmpty(profile.Id) ? "Office" : profile.Id,
                    FloorAreaM2 = areaM2,
                    HeightM     = heightM,
                    OccupantCount = profile.OccupantCountFor(areaM2)
                };
                profile.ApplyTo(z);
                AddPerimeterEnvelope(r, z, construction);
                return z;
            }
            catch (Exception ex) { StingLog.Warn($"ZoneFromRoom {r.Id}: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Best-effort envelope detection by intersecting the room boundary
        /// with exterior walls + their hosted windows. When the geometry
        /// doesn't yield (linked architectural model, etc.) fall back to a
        /// generic envelope ratio so the calc still runs.
        /// </summary>
        private static void AddPerimeterEnvelope(SpatialElement spatial, LoadZone z,
            StingTools.Core.Hvac.Loads.ConstructionProfile construction)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center
                };
                var segs = spatial.GetBoundarySegments(opts);
                if (segs == null || segs.Count == 0) goto Fallback;

                double extWallAreaM2 = 0;
                double glazingAreaM2 = 0;
                double avgOrient = 0; int orientN = 0;
                foreach (var loop in segs)
                foreach (var seg in loop)
                {
                    var el = spatial.Document.GetElement(seg.ElementId);
                    if (el is not Wall w) continue;
                    if (w.WallType?.Function != WallFunction.Exterior) continue;
                    double lenM = UnitUtils.ConvertFromInternalUnits(seg.GetCurve()?.Length ?? 0, UnitTypeId.Meters);
                    double h = z.HeightM;
                    double area = lenM * h;
                    extWallAreaM2 += area;

                    // Glazing — sum hosted window areas if any
                    try
                    {
                        var hosted = w.FindInserts(true, false, false, false);
                        foreach (var ins in hosted)
                        {
                            if (w.Document.GetElement(ins) is FamilyInstance fi &&
                                fi.Category?.Id?.Value == (long)BuiltInCategory.OST_Windows)
                            {
                                var bb = fi.get_BoundingBox(null);
                                if (bb != null)
                                {
                                    double wFt = bb.Max.X - bb.Min.X;
                                    double hFt = bb.Max.Z - bb.Min.Z;
                                    double aM2 = UnitUtils.ConvertFromInternalUnits(wFt * hFt, UnitTypeId.SquareMeters);
                                    if (aM2 > 0.1) glazingAreaM2 += aM2;
                                }
                            }
                        }
                    }
                    catch { /* swallow per-window failures */ }

                    // Crude orientation: wall facing vector
                    try
                    {
                        var dir = w.Orientation;
                        double deg = Math.Atan2(dir.X, dir.Y) * 180 / Math.PI;
                        if (deg < 0) deg += 360;
                        avgOrient += deg; orientN++;
                    }
                    catch { }
                }
                double orientation = orientN > 0 ? avgOrient / orientN : 180;
                double netWall = Math.Max(0, extWallAreaM2 - glazingAreaM2);

                if (netWall > 0)
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.ExteriorWall, AreaM2 = netWall,
                        UvalueWm2K = construction.WallUvalue, OrientationDeg = orientation
                    });
                if (glazingAreaM2 > 0)
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.Window, AreaM2 = glazingAreaM2,
                        UvalueWm2K = construction.WindowUvalue,
                        SHGC = construction.WindowSHGC,
                        ShadingFactor = construction.WindowShadingFactor,
                        OrientationDeg = orientation
                    });

                // Roof segment only when the zone is on the top level.
                if (IsTopLevel(spatial))
                {
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.Roof, AreaM2 = z.FloorAreaM2,
                        UvalueWm2K = construction.RoofUvalue, OrientationDeg = 0
                    });
                }
                return;

                Fallback:
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.ExteriorWall, AreaM2 = Math.Max(z.FloorAreaM2 * 0.6, 8),
                    UvalueWm2K = construction.WallUvalue, OrientationDeg = 180
                });
                z.Envelope.Add(new EnvelopeSegment
                {
                    Kind = SegmentKind.Window, AreaM2 = Math.Max(z.FloorAreaM2 * 0.15, 2),
                    UvalueWm2K = construction.WindowUvalue,
                    SHGC = construction.WindowSHGC,
                    ShadingFactor = construction.WindowShadingFactor,
                    OrientationDeg = 180
                });
            }
            catch (Exception ex) { StingLog.Warn($"Envelope detect {spatial.Id}: {ex.Message}"); }
        }

        // Per-document cache of the top level's id — re-resolving the highest
        // Level on every space gets expensive on large projects.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ElementId> _topLevelCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, ElementId>();

        /// <summary>
        /// Drop the cached top-level lookup for a document. Called by
        /// <see cref="StingTools.Core.StingToolsApp"/>'s document-closing hook so
        /// the cache doesn't outlive its source document.
        /// </summary>
        public static void InvalidateTopLevelCache(Document doc)
        {
            try { _topLevelCache.TryRemove(doc?.PathName ?? "<no-doc>", out _); } catch { }
        }

        private static bool IsTopLevel(SpatialElement spatial)
        {
            try
            {
                var doc = spatial.Document;
                // SpatialElement.LevelId is on the Element base; safer than
                // `.Level` which lives on Room/Space individually.
                var lvlId = spatial.LevelId;
                if (lvlId == ElementId.InvalidElementId) return false;
                string key = doc.PathName ?? "<no-doc>";
                var topId = _topLevelCache.GetOrAdd(key, _ =>
                {
                    var top = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderByDescending(l => l.Elevation)
                        .FirstOrDefault();
                    return top?.Id ?? ElementId.InvalidElementId;
                });
                return topId != ElementId.InvalidElementId && topId == lvlId;
            }
            catch { return false; }
        }

        private static string ResolveSystemId(Space s)
        {
            try
            {
                string sysId = s.LookupParameter("HVC_SYSTEM_ID_TXT")?.AsString();
                if (!string.IsNullOrWhiteSpace(sysId)) return sysId;
                // Group by Zone if available — `Space.Zone` returns the Revit
                // HVAC Zone the space is assigned to. Wrapped because not
                // every project binds Zones.
                try
                {
                    var zone = s.Zone;
                    if (zone != null && !string.IsNullOrEmpty(zone.Name)) return zone.Name;
                }
                catch { }
                return "(default)";
            }
            catch { return "(default)"; }
        }

        private static int TryReadIntByName(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.Double)  return (int)Math.Round(p.AsDouble());
                return 0;
            }
            catch { return 0; }
        }

        private static double TryReadFlowByName(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || p.StorageType != StorageType.Double) return 0;
                return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.LitersPerSecond);
            }
            catch { return 0; }
        }

        private static long ParseLong(string s)
        {
            return long.TryParse(s, out long v) ? v : -1;
        }
    }
}
