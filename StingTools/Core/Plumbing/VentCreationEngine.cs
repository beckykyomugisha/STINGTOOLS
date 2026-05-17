// VentCreationEngine — creates Revit pipe elements from VentRequirement
// records produced by VentDesigner. Phase 179c.
//
// Each VentRequirement describes a drain pipe that needs a vent branch.
// The engine:
//   1. Resolves the drain pipe's highest connector (top of P-trap riser).
//   2. Finds or selects a vent pipe system type + pipe type matching the
//      recommended DN.
//   3. Calls Pipe.Create() for the new vent run.
//   4. Optionally places an AAV family instance at the termination.
//   5. Writes PLM_VENT_DN_MM and PLM_VENT_PIPE_ID back to the drain pipe.
//
// Must be called inside a Transaction (the caller owns the transaction).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class VentCreationOptions
    {
        /// <summary>Place AAV family instance when RequiresAav = true.</summary>
        public bool   PlaceAavs              { get; set; } = true;
        /// <summary>Minimum vent rise height above the drain pipe centreline, metres.
        /// BS EN 12056-2 §8.3: vent termination ≥ 200 mm above flood-level rim.
        /// 2.1 m provides safe clearance above WC cistern flood level.</summary>
        public double VentRiseM              { get; set; } = 2.1;
        /// <summary>Phase II option — attempt to connect vent to main stack connector.
        /// Not fully automated in Phase 179c; use with caution.</summary>
        public bool   ConnectToStack         { get; set; } = false;
        /// <summary>Preferred pipe type name fragment (e.g. "UPVC"). Empty = first match by DN.</summary>
        public string PreferredPipeTypeName  { get; set; } = "";
    }

    public class VentCreationResult
    {
        public int            VentsCreated { get; set; }
        public int            VentsSkipped { get; set; }
        public int            AavsPlaced   { get; set; }
        public List<string>   Warnings     { get; } = new List<string>();
        public List<ElementId> CreatedIds  { get; } = new List<ElementId>();
    }

    public static class VentCreationEngine
    {
        private const double FtToM  = 0.3048;
        private const double MToFt  = 1.0 / 0.3048;
        private const double MmToFt = 1.0 / 304.8;

        /// <summary>
        /// Create vent pipe elements for every VentRequirement in the list.
        /// Must be called within an active, started Transaction.
        /// </summary>
        public static VentCreationResult CreateVents(
            Document doc,
            List<VentRequirement> requirements,
            VentCreationOptions opts = null)
        {
            var result = new VentCreationResult();
            if (doc == null || requirements == null) return result;
            opts = opts ?? new VentCreationOptions();

            // Pre-resolve shared resources once
            ElementId ventSystemTypeId = ResolveVentSystemType(doc);
            Level      baseLevel       = ResolveBaseLevel(doc);

            foreach (var req in requirements)
            {
                try
                {
                    ProcessRequirement(doc, req, opts, ventSystemTypeId, baseLevel, result);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"VentCreate req {req.DrainPipeId?.Value}: {ex.Message}");
                    result.VentsSkipped++;
                }
            }

            StingLog.Info($"VentCreationEngine: created {result.VentsCreated}, " +
                          $"skipped {result.VentsSkipped}, AAVs {result.AavsPlaced}");
            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        // Core per-requirement logic
        // ──────────────────────────────────────────────────────────────────

        private static void ProcessRequirement(
            Document doc,
            VentRequirement req,
            VentCreationOptions opts,
            ElementId ventSystemTypeId,
            Level baseLevel,
            VentCreationResult result)
        {
            // 1. Get drain pipe
            Pipe drainPipe = null;
            try { drainPipe = doc.GetElement(req.DrainPipeId) as Pipe; } catch { }
            if (drainPipe == null)
            {
                result.Warnings.Add($"DrainPipe {req.DrainPipeId?.Value} not found — skipped.");
                result.VentsSkipped++;
                return;
            }

            // 2. Find drain pipe's top connector (highest Z endpoint)
            XYZ topPt = GetTopConnectorPoint(drainPipe);
            if (topPt == null)
            {
                result.Warnings.Add($"Pipe {req.DrainPipeId?.Value}: no usable connector — skipped.");
                result.VentsSkipped++;
                return;
            }

            // 3. Find appropriate pipe type for vent
            ElementId ventPipeTypeId = ResolvePipeType(doc, req.RecommendedVentDnMm, opts.PreferredPipeTypeName);
            if (ventPipeTypeId == null || ventPipeTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add($"No pipe type found for DN{req.RecommendedVentDnMm} — skipped pipe {req.DrainPipeId?.Value}.");
                result.VentsSkipped++;
                return;
            }

            // 4. Resolve or reuse level
            Level level = ResolveLevelForPoint(doc, topPt) ?? baseLevel;
            if (level == null)
            {
                result.Warnings.Add($"No level found for pipe {req.DrainPipeId?.Value} — skipped.");
                result.VentsSkipped++;
                return;
            }

            // 5. Calculate vent start and end points (Revit internal feet)
            // Offset vent start: 150 mm horizontally from drain centreline, 300 mm up
            double offsetHorizFt = 150.0 * MmToFt;   // 150 mm horizontal standoff
            double offsetUpFt    = 300.0 * MmToFt;   // 300 mm above top connector
            double riseHtFt      = opts.VentRiseM * MToFt;

            // Horizontal offset in +X direction (simple; production would use drain direction)
            XYZ ventStart = new XYZ(topPt.X + offsetHorizFt, topPt.Y, topPt.Z + offsetUpFt);

            // Limit rise to MaxVentLengthM
            double maxRiseFt = Math.Min(req.MaxVentLengthM, opts.VentRiseM + 1.0) * MToFt;
            XYZ ventEnd = new XYZ(ventStart.X, ventStart.Y, ventStart.Z + maxRiseFt);

            // 6. Create the vent pipe
            Pipe ventPipe = null;
            try
            {
                ventPipe = Pipe.Create(doc, ventSystemTypeId, ventPipeTypeId,
                                       level.Id, ventStart, ventEnd);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Pipe.Create failed for vent of {req.DrainPipeId?.Value}: {ex.Message}");
                result.VentsSkipped++;
                return;
            }

            if (ventPipe == null)
            {
                result.Warnings.Add($"Pipe.Create returned null for {req.DrainPipeId?.Value}.");
                result.VentsSkipped++;
                return;
            }

            // Set nominal diameter on the vent pipe
            try
            {
                double targetDiamFt = req.RecommendedVentDnMm * MmToFt;
                Parameter diamParam = ventPipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diamParam != null && !diamParam.IsReadOnly)
                    diamParam.Set(targetDiamFt);
            }
            catch { }

            result.CreatedIds.Add(ventPipe.Id);
            result.VentsCreated++;

            // 7. Write params back to drain pipe
            TryWriteInt(drainPipe, ParamRegistry.PLM_VENT_DN, req.RecommendedVentDnMm);
            TryWriteString(drainPipe, ParamRegistry.PLM_VENT_PIPE_ID, ventPipe.Id.Value.ToString());

            // 8. Place AAV if required
            if (req.RequiresAav && opts.PlaceAavs)
            {
                bool placed = TryPlaceAav(doc, ventEnd, level);
                if (placed) result.AavsPlaced++;
                else        result.Warnings.Add($"AAV family not found for pipe {req.DrainPipeId?.Value} — add family containing 'AAV' or 'Air Admittance'.");
            }

            // 9. Relief vent note
            if (req.RequiresReliefVent)
                result.Warnings.Add($"Pipe {req.DrainPipeId?.Value}: tall stack (DFU={req.Dfu:F1}) — relief vent required at stack top per BS EN 12056-2 §8.6.");
        }

        // ──────────────────────────────────────────────────────────────────
        // AAV placement
        // ──────────────────────────────────────────────────────────────────

        private static bool TryPlaceAav(Document doc, XYZ location, Level level)
        {
            try
            {
                FamilySymbol aavSymbol = FindAavSymbol(doc);
                if (aavSymbol == null) return false;
                if (!aavSymbol.IsActive) aavSymbol.Activate();
                doc.Create.NewFamilyInstance(location, aavSymbol, level,
                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TryPlaceAav: {ex.Message}");
                return false;
            }
        }

        private static FamilySymbol FindAavSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                {
                    string n = (fs.Family?.Name ?? "") + " " + (fs.Name ?? "");
                    return n.IndexOf("AAV", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Air Admittance", StringComparison.OrdinalIgnoreCase) >= 0;
                });
        }

        // ──────────────────────────────────────────────────────────────────
        // Resource resolution helpers
        // ──────────────────────────────────────────────────────────────────

        private static ElementId ResolveVentSystemType(Document doc)
        {
            try
            {
                // Prefer system types whose name contains "Vent"
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>()
                    .ToList();

                var ventSys = all.FirstOrDefault(s =>
                    s.Name.IndexOf("Vent", StringComparison.OrdinalIgnoreCase) >= 0);

                return (ventSys ?? all.FirstOrDefault())?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveVentSystemType: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static ElementId ResolvePipeType(Document doc, int targetDnMm, string preferredName)
        {
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>()
                    .ToList();

                if (!string.IsNullOrWhiteSpace(preferredName))
                {
                    var preferred = types.FirstOrDefault(t =>
                        t.Name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (preferred != null) return preferred.Id;
                }

                // Fall back to any pipe type — diameter is set after creation
                return types.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolvePipeType DN{targetDnMm}: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static Level ResolveBaseLevel(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static Level ResolveLevelForPoint(Document doc, XYZ ptFt)
        {
            try
            {
                // Find the level immediately below the point
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                Level best = levels.FirstOrDefault();
                foreach (var lv in levels)
                {
                    if (lv.Elevation <= ptFt.Z) best = lv;
                    else break;
                }
                return best;
            }
            catch { return null; }
        }

        private static XYZ GetTopConnectorPoint(Pipe pipe)
        {
            try
            {
                var lc = pipe.Location as LocationCurve;
                if (lc?.Curve == null) return null;
                var p0 = lc.Curve.GetEndPoint(0);
                var p1 = lc.Curve.GetEndPoint(1);
                return p0.Z >= p1.Z ? p0 : p1;
            }
            catch { return null; }
        }

        // ──────────────────────────────────────────────────────────────────
        // Parameter write helpers
        // ──────────────────────────────────────────────────────────────────

        private static bool TryWriteInt(Element el, string paramName, int value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Integer) { p.Set(value); return true; }
                if (p.StorageType == StorageType.Double)  { p.Set((double)value); return true; }
                if (p.StorageType == StorageType.String)  { p.Set(value.ToString()); return true; }
            }
            catch { }
            return false;
        }

        private static bool TryWriteString(Element el, string paramName, string value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.String) { p.Set(value); return true; }
            }
            catch { }
            return false;
        }
    }
}
