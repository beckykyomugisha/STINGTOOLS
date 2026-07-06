// StingTools — Shared perimeter-envelope detector (WS A2).
//
// Single source of envelope-from-geometry truth: intersects a Space/Room
// boundary with exterior walls + their hosted windows, derives net wall + glazing
// area + orientation, and adds a roof segment on the top level. Falls back to a
// generic envelope ratio when the geometry doesn't yield (linked architectural
// model, no boundary). Extracted from HvacBlockLoadCommand so the annual-energy
// (sustainability) path reuses the EXACT same conduction + per-façade solar input
// the HVAC block-load uses — one detector, no fork.
//
// Revit-facing (touches geometry) — NOT in the test project. The per-façade solar
// MATH that consumes these segments (AnnualEnergyEstimator.VerticalSolarFactor /
// EstimateZone) is pure and unit-tested separately.

using System;
using System.Collections.Concurrent;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Hvac.Loads
{
    /// <summary>
    /// Accumulates how many envelope segments were built from model-derived
    /// thermal data vs the construction profile fallback (Tier 2 item 2.1).
    /// Surfaced in the block-load result panel so the engineer knows how much
    /// of the number is measured fabric vs assumed.
    /// </summary>
    public class EnvelopeBuildStats
    {
        /// <summary>Exterior walls whose U came from the wall type's CompoundStructure.</summary>
        public int WallModelDerived { get; set; }
        /// <summary>Exterior walls that fell back to the profile U (no thermal data).</summary>
        public int WallFallback { get; set; }
        /// <summary>Windows whose SHGC/U came from the family symbol.</summary>
        public int GlazingModelDerived { get; set; }
        /// <summary>Windows that fell back to the profile SHGC/U.</summary>
        public int GlazingFallback { get; set; }

        public int TotalModelDerived => WallModelDerived + GlazingModelDerived;
        public int TotalFallback     => WallFallback + GlazingFallback;
    }

    public static class EnvelopeDetector
    {
        // Per-document cache of the top level's id — re-resolving the highest Level
        // on every space gets expensive on large projects.
        private static readonly ConcurrentDictionary<string, ElementId> _topLevelCache
            = new ConcurrentDictionary<string, ElementId>();

        /// <summary>Drop the cached top-level lookup for a document (document-close hook).</summary>
        public static void InvalidateTopLevelCache(Document doc)
        {
            try { _topLevelCache.TryRemove(doc?.PathName ?? "<no-doc>", out _); } catch { }
        }

        /// <summary>
        /// Best-effort envelope detection by intersecting the room boundary with
        /// exterior walls + their hosted windows. When the geometry doesn't yield
        /// (linked architectural model, etc.) fall back to a generic envelope ratio
        /// so the calc still runs. Appends segments to <paramref name="z"/>.Envelope.
        ///
        /// <para>Tier-2 item 2.1 — wall U is read from each exterior wall's
        /// <see cref="WallType.GetCompoundStructure"/> (ΣR of layer width /
        /// material thermal conductivity + inside/outside air films → U = 1/ΣR)
        /// and area-weighted across the zone's exterior walls; window SHGC/U are
        /// read from the window <see cref="FamilySymbol"/>. Both fall back to the
        /// <paramref name="construction"/> profile when the type carries no usable
        /// thermal data, with a per-segment <see cref="StingLog"/> warning and a
        /// tally in <paramref name="stats"/> (when supplied).</para>
        /// </summary>
        public static void AddPerimeterEnvelope(SpatialElement spatial, LoadZone z,
            ConstructionProfile construction, EnvelopeBuildStats stats = null)
        {
            try
            {
                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Center
                };
                var segs = spatial.GetBoundarySegments(opts);
                if (segs == null || segs.Count == 0) goto Fallback;

                Document doc = spatial.Document;
                double extWallAreaM2 = 0;
                double glazingAreaM2 = 0;
                double avgOrient = 0; int orientN = 0;

                // Area-weighted accumulators for the model-derived properties.
                double wallUAreaSum = 0;   // Σ (U_wall · A_wall)
                double wallAreaForU = 0;   // Σ A_wall (walls that yielded a usable U)
                double glazUAreaSum = 0, glazShgcAreaSum = 0, glazAreaForProp = 0;

                bool anyWallModelU = false;   // did at least one wall yield a model U?
                bool anyGlazModelProp = false;

                foreach (var loop in segs)
                foreach (var seg in loop)
                {
                    var el = doc.GetElement(seg.ElementId);
                    if (el is not Wall w) continue;
                    if (w.WallType?.Function != WallFunction.Exterior) continue;
                    double lenM = UnitUtils.ConvertFromInternalUnits(seg.GetCurve()?.Length ?? 0, UnitTypeId.Meters);
                    double h = z.HeightM;
                    double area = lenM * h;
                    extWallAreaM2 += area;

                    // ── Model-derived wall U (Tier-2 2.1) ──────────────────
                    double? modelU = TryWallUFromModel(doc, w.WallType);
                    if (modelU.HasValue && modelU.Value > 0)
                    {
                        wallUAreaSum += modelU.Value * area;
                        wallAreaForU += area;
                        anyWallModelU = true;
                    }
                    else
                    {
                        StingLog.Info($"Envelope 2.1: wall type '{w.WallType?.Name}' " +
                                      $"({spatial?.Id}) → profile U {construction.WallUvalue:F2} (no model thermal data).");
                    }

                    // Glazing — sum hosted window areas if any, read model
                    // SHGC/U from the window symbol.
                    try
                    {
                        var hosted = w.FindInserts(true, false, false, false);
                        foreach (var ins in hosted)
                        {
                            if (doc.GetElement(ins) is FamilyInstance fi &&
                                fi.Category?.Id?.Value == (long)BuiltInCategory.OST_Windows)
                            {
                                var bb = fi.get_BoundingBox(null);
                                if (bb != null)
                                {
                                    double wFt = bb.Max.X - bb.Min.X;
                                    double hFt = bb.Max.Z - bb.Min.Z;
                                    double aM2 = UnitUtils.ConvertFromInternalUnits(wFt * hFt, UnitTypeId.SquareMeters);
                                    if (aM2 > 0.1)
                                    {
                                        glazingAreaM2 += aM2;

                                        var sym = doc.GetElement(fi.GetTypeId()) as FamilySymbol;
                                        double? gShgc = TryGlazingShgcFromSymbol(sym);
                                        double? gU    = TryGlazingUFromSymbol(sym);
                                        if (gShgc.HasValue || gU.HasValue)
                                        {
                                            glazShgcAreaSum += (gShgc ?? construction.WindowSHGC) * aM2;
                                            glazUAreaSum    += (gU    ?? construction.WindowUvalue) * aM2;
                                            glazAreaForProp += aM2;
                                            anyGlazModelProp = true;
                                        }
                                        else
                                        {
                                            StingLog.Info($"Envelope 2.1: window type '{sym?.Name}' " +
                                                          $"({spatial?.Id}) → profile SHGC {construction.WindowSHGC:F2} " +
                                                          $"/ U {construction.WindowUvalue:F2} (no analytic glazing data).");
                                        }
                                    }
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

                // Resolve the wall U: area-weighted model value when any wall
                // yielded one, else the profile fallback.
                double wallU = construction.WallUvalue;
                if (anyWallModelU && wallAreaForU > 0)
                    wallU = wallUAreaSum / wallAreaForU;

                // Resolve glazing SHGC + U similarly.
                double glazShgc = construction.WindowSHGC;
                double glazU    = construction.WindowUvalue;
                if (anyGlazModelProp && glazAreaForProp > 0)
                {
                    glazShgc = glazShgcAreaSum / glazAreaForProp;
                    glazU    = glazUAreaSum    / glazAreaForProp;
                }

                if (netWall > 0)
                {
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.ExteriorWall, AreaM2 = netWall,
                        UvalueWm2K = wallU, OrientationDeg = orientation
                    });
                    if (stats != null)
                    {
                        if (anyWallModelU) stats.WallModelDerived++;
                        else               stats.WallFallback++;
                    }
                }
                if (glazingAreaM2 > 0)
                {
                    z.Envelope.Add(new EnvelopeSegment
                    {
                        Kind = SegmentKind.Window, AreaM2 = glazingAreaM2,
                        UvalueWm2K = glazU,
                        SHGC = glazShgc,
                        ShadingFactor = construction.WindowShadingFactor,
                        OrientationDeg = orientation
                    });
                    if (stats != null)
                    {
                        if (anyGlazModelProp) stats.GlazingModelDerived++;
                        else                  stats.GlazingFallback++;
                    }
                }

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
                if (stats != null) { stats.WallFallback++; stats.GlazingFallback++; }
            }
            catch (Exception ex) { StingLog.Warn($"Envelope detect {spatial?.Id}: {ex.Message}"); }
        }

        // ── Tier-2 2.1 model-thermal-property readers ───────────────────

        // Standard combined inside + outside surface air-film resistances for a
        // vertical exterior wall, m²·K/W (ISO 6946 / CIBSE Guide A: Rsi 0.13 +
        // Rse 0.04). Added to the layer resistances before inverting to U.
        private const double SurfaceAirFilmRm2KW = 0.13 + 0.04;

        /// <summary>
        /// Derive a wall U-value (W/m²·K) from the wall type's compound
        /// structure: ΣR of (layer width / material thermal conductivity) plus
        /// the standard inside+outside air films, then U = 1/ΣR.
        ///
        /// Returns null when the type has no compound structure, no layers, or a
        /// layer with missing/zero conductivity (curtain wall, generic wall, or
        /// materials without a thermal asset) — the caller then falls back to the
        /// construction profile. Never throws.
        /// </summary>
        public static double? TryWallUFromModel(Document doc, WallType wallType)
        {
            try
            {
                if (doc == null || wallType == null) return null;
                var cs = wallType.GetCompoundStructure();
                if (cs == null) return null;
                var layers = cs.GetLayers();
                if (layers == null || layers.Count == 0) return null;

                double sumR = SurfaceAirFilmRm2KW;
                bool anyLayer = false;
                foreach (var layer in layers)
                {
                    // Membrane / zero-width layers contribute negligible R — skip.
                    double widthM = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Meters);
                    if (widthM <= 1e-6) continue;

                    double? k = TryMaterialConductivity(doc, layer.MaterialId);
                    if (!k.HasValue || k.Value <= 1e-6)
                        return null;   // a solid layer without usable k → abandon, use profile

                    sumR += widthM / k.Value;
                    anyLayer = true;
                }
                if (!anyLayer || sumR <= 1e-6) return null;
                return 1.0 / sumR;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryWallUFromModel '{wallType?.Name}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Thermal conductivity (W/m·K) of a material via its thermal asset.
        /// Material.ThermalAssetId → PropertySetElement.GetThermalAsset() →
        /// ThermalAsset.ThermalConductivity (Revit internal units → W/m·K).
        /// Returns null when the material has no thermal asset. Never throws.
        /// </summary>
        public static double? TryMaterialConductivity(Document doc, ElementId materialId)
        {
            try
            {
                if (doc == null || materialId == null || materialId == ElementId.InvalidElementId
                    || materialId.Value <= 0)
                    return null;
                if (doc.GetElement(materialId) is not Material mat) return null;
                var thermalId = mat.ThermalAssetId;
                if (thermalId == ElementId.InvalidElementId) return null;
                if (doc.GetElement(thermalId) is not PropertySetElement pse) return null;
                var asset = pse.GetThermalAsset();
                if (asset == null) return null;
                // ThermalAsset.ThermalConductivity is already SI (W/m·K) —
                // it is NOT subject to Revit's internal length-unit system.
                // Confirmed by the codebase's own writer (Temp/MaterialCommands.cs
                // sets ThermalConductivity from a raw W/m·K value with no
                // ConvertToInternalUnits). Applying ConvertFromInternalUnits here
                // would double-convert and understate U by ~45%.
                double kSi = asset.ThermalConductivity;
                return kSi > 0 ? kSi : (double?)null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryMaterialConductivity {materialId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read the glazing Solar Heat Gain Coefficient from the window family
        /// symbol — a STING <c>HVC_GLAZING_SHGC_NR</c> shared param first, then
        /// the Revit built-in analytic <c>ANALYTICAL_SOLAR_HEAT_GAIN_COEFFICIENT</c>.
        /// Returns null when neither is present/valid. Never throws.
        /// </summary>
        public static double? TryGlazingShgcFromSymbol(FamilySymbol sym)
        {
            if (sym == null) return null;
            double? v = TryReadDoubleParam(sym, "HVC_GLAZING_SHGC_NR");
            if (v.HasValue && v.Value > 0 && v.Value <= 1) return v;
            try
            {
                var p = sym.get_Parameter(BuiltInParameter.ANALYTICAL_SOLAR_HEAT_GAIN_COEFFICIENT);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double d = p.AsDouble();   // SHGC is dimensionless 0..1
                    if (d > 0 && d <= 1) return d;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Read the glazing U-value (W/m²·K) from the window family symbol — a
        /// STING <c>HVC_GLAZING_U_NR</c> shared param first, then the Revit
        /// built-in analytic <c>ANALYTICAL_HEAT_TRANSFER_COEFFICIENT</c> (converted
        /// from internal units). Returns null when neither is present/valid.
        /// Never throws.
        /// </summary>
        public static double? TryGlazingUFromSymbol(FamilySymbol sym)
        {
            if (sym == null) return null;
            double? v = TryReadDoubleParam(sym, "HVC_GLAZING_U_NR");
            if (v.HasValue && v.Value > 0) return v;
            try
            {
                var p = sym.get_Parameter(BuiltInParameter.ANALYTICAL_HEAT_TRANSFER_COEFFICIENT);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double internalU = p.AsDouble();
                    if (internalU > 0)
                    {
                        double uSi = UnitUtils.ConvertFromInternalUnits(
                            internalU, UnitTypeId.WattsPerSquareMeterKelvin);
                        if (uSi > 0) return uSi;
                    }
                }
            }
            catch { }
            return null;
        }

        private static double? TryReadDoubleParam(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return null;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                return null;
            }
            catch { return null; }
        }

        public static bool IsTopLevel(SpatialElement spatial)
        {
            try { return spatial != null && IsTopLevelId(spatial.Document, spatial.LevelId); }
            catch { return false; }
        }

        /// <summary>The highest-elevation Level id for a document (cached per path).</summary>
        public static ElementId TopLevelId(Document doc)
        {
            if (doc == null) return ElementId.InvalidElementId;
            return _topLevelCache.GetOrAdd(doc.PathName ?? "<no-doc>", _ =>
            {
                try
                {
                    var top = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderByDescending(l => l.Elevation).FirstOrDefault();
                    return top?.Id ?? ElementId.InvalidElementId;
                }
                catch { return ElementId.InvalidElementId; }
            });
        }

        /// <summary>True when <paramref name="levelId"/> is the document's top level.
        /// SpatialElement.LevelId is on the Element base; safer than `.Level`.</summary>
        public static bool IsTopLevelId(Document doc, ElementId levelId)
        {
            if (levelId == null || levelId == ElementId.InvalidElementId) return false;
            var topId = TopLevelId(doc);
            return topId != ElementId.InvalidElementId && topId == levelId;
        }
    }
}
