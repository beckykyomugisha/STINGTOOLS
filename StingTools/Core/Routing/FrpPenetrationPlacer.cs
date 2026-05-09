// StingTools — FrpPenetrationPlacer.
//
// Closes the loop from SlabPenetrationDetector. For each
// PenetrationRecord the detector produces, this placer locates the
// loaded STING_SEED_SpecialityEquipment family (or any family whose
// STING_SEED_FAMILY_TXT marks it as a SpecialityEquipment seed) and
// drops a face-based instance at the crossing point, stamping every
// PEN_* parameter from the record onto the new instance.
//
// When the family is NOT loaded the placer warns once and stamps the
// member-side parameters only — same behaviour the detector already
// has. This means: shipping the JSON spec + the placer is enough for
// the parameter pipeline to work; the .rfa authoring is the visual
// finish, not a pre-condition for the code.
//
// After the user runs SwapToManufacturerCommand to replace the seed
// with a real Hilti / Promat / etc. family, every placed instance
// inherits the manufacturer geometry while keeping the PEN_* metadata
// the placer stamped — the Penetration Register schedule keeps
// rendering correctly.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace StingTools.Core.Routing
{
    public sealed class FrpPlacementResult
    {
        public int Placed { get; set; }
        public int Stamped { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class FrpPenetrationPlacer
    {
        // Fire-rating → type-name suffix used when looking up the right
        // type variant on the seed family. Matches the variants the
        // .rfa author is expected to create per the layman's guide.
        private static readonly Dictionary<string, string> _ratingToType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "FR30",  "FR30"  },
                { "FR60",  "FR60"  },
                { "FR90",  "FR90"  },
                { "FR120", "FR120" },
            };

        // Project-scoped sequence so penetration control numbers run
        // contiguously across multiple Detect → Place cycles. Lock-free
        // — at-most one user thread will be calling at a time.
        private static int _nextControlNumber = 1;

        /// <summary>
        /// Place an FRP family instance at every penetration record.
        /// Returns count + warnings; never throws unless doc is null.
        /// Caller wraps in a Transaction.
        /// </summary>
        public static FrpPlacementResult Place(Document doc, IList<PenetrationRecord> records)
        {
            var r = new FrpPlacementResult();
            if (doc == null)
            {
                r.Warnings.Add("FrpPenetrationPlacer: null document.");
                return r;
            }
            if (records == null || records.Count == 0) return r;

            var seedFamily = ResolveSeedFamily(doc);
            if (seedFamily == null)
            {
                r.Warnings.Add(
                    "STING_SEED_SpecialityEquipment family not loaded — penetrations stamped on " +
                    "members but no FRP instances placed. Run 'Build Seed Families' to scaffold the " +
                    ".rfa, finish per Families/Seeds/README.md, then load it into the project.");
                // Even without the family, stamp PEN_CONTROL_NUMBER_TXT
                // on every member so the register schedule still renders
                // useful output.
                foreach (var rec in records)
                {
                    if (StampMemberFallback(doc, rec)) r.Stamped++;
                }
                return r;
            }

            // Index types by rating once.
            var typesByRating = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var symId in seedFamily.GetFamilySymbolIds())
            {
                var sym = doc.GetElement(symId) as FamilySymbol;
                if (sym == null) continue;
                string nm = sym.Name ?? "";
                foreach (var kv in _ratingToType)
                {
                    if (nm.IndexOf(kv.Value, StringComparison.OrdinalIgnoreCase) >= 0)
                    { typesByRating[kv.Key] = sym; break; }
                }
                // Default fallback: first symbol seen if no rating match.
                if (!typesByRating.ContainsKey("__default__")) typesByRating["__default__"] = sym;
            }

            foreach (var rec in records)
            {
                try
                {
                    if (rec.HostFloorId == null || rec.HostFloorId == ElementId.InvalidElementId)
                    { r.Skipped++; continue; }

                    var floor = doc.GetElement(rec.HostFloorId) as Floor;
                    if (floor == null) { r.Skipped++; continue; }

                    string rating = string.IsNullOrEmpty(rec.FireRating) ? "FR60" : rec.FireRating;
                    if (!typesByRating.TryGetValue(rating, out var sym))
                    {
                        // Pick best-effort default; the .rfa might not yet
                        // ship every type variant.
                        if (!typesByRating.TryGetValue("__default__", out sym))
                        { r.Skipped++; continue; }
                        r.Warnings.Add($"FRP type variant '{rating}' missing — used default for member {rec.MemberId?.Value}.");
                    }

                    if (!sym.IsActive)
                    {
                        try { sym.Activate(); doc.Regenerate(); }
                        catch (Exception ex) { r.Warnings.Add($"Activate {sym.Name}: {ex.Message}"); }
                    }

                    // Face-based placement uses the floor's bottom
                    // reference. Falling back to a non-hosted point
                    // instance when face acquisition fails keeps the
                    // pipeline robust on legacy floor geometries.
                    FamilyInstance fi = null;
                    try
                    {
                        fi = TryFaceBasedPlace(doc, sym, floor, rec);
                    }
                    catch (Exception ex) { r.Warnings.Add($"Face-based place: {ex.Message}"); }

                    if (fi == null)
                    {
                        try
                        {
                            fi = doc.Create.NewFamilyInstance(rec.Location, sym, floor,
                                StructuralType.NonStructural);
                        }
                        catch (Exception ex)
                        {
                            r.Errors++;
                            r.Warnings.Add($"Place {sym.Name} at {rec.MemberId?.Value}: {ex.Message}");
                            continue;
                        }
                    }

                    StampInstance(fi, rec, MintControlNumber());
                    r.Placed++;
                }
                catch (Exception ex)
                {
                    r.Errors++;
                    StingLog.Warn($"FrpPenetrationPlacer record {rec?.MemberId?.Value}: {ex.Message}");
                }
            }

            return r;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static Family ResolveSeedFamily(Document doc)
        {
            try
            {
                // Primary: look for the canonical name.
                var byName = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name,
                        "STING_SEED_SpecialityEquipment", StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName;

                // Fallback: any family whose first symbol carries the seed
                // marker — covers the case where the .rfa was renamed.
                foreach (var fam in new FilteredElementCollector(doc)
                    .OfClass(typeof(Family)).Cast<Family>())
                {
                    foreach (var sid in fam.GetFamilySymbolIds())
                    {
                        var sym = doc.GetElement(sid) as FamilySymbol;
                        if (sym == null) continue;
                        string seedTag = ParameterHelpers.GetString(sym, "STING_SEED_FAMILY_TXT");
                        if (string.Equals(seedTag, "STING_SEED_SpecialityEquipment",
                            StringComparison.OrdinalIgnoreCase)) return fam;
                        break;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSeedFamily: {ex.Message}"); }
            return null;
        }

        private static FamilyInstance TryFaceBasedPlace(Document doc, FamilySymbol sym, Floor floor, PenetrationRecord rec)
        {
            // Resolve the floor's bottom face via Geometry.
            var opts = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false };
            var geo = floor.get_Geometry(opts);
            if (geo == null) return null;
            Reference bottomFaceRef = null;
            double bestZ = double.PositiveInfinity;
            foreach (GeometryObject go in geo)
            {
                if (!(go is Solid s) || s.Faces == null) continue;
                foreach (Face f in s.Faces)
                {
                    if (!(f is PlanarFace pf)) continue;
                    if (Math.Abs(pf.FaceNormal.Z + 1.0) > 0.05) continue; // bottom face = -Z normal
                    var bb = pf.GetBoundingBox();
                    if (bb == null) continue;
                    var pt = pf.Evaluate(new UV(0.5 * (bb.Min.U + bb.Max.U), 0.5 * (bb.Min.V + bb.Max.V)));
                    if (pt.Z < bestZ) { bestZ = pt.Z; bottomFaceRef = pf.Reference; }
                }
            }
            if (bottomFaceRef == null) return null;

            // Place the family instance face-hosted at the recorded XY,
            // with the up-direction set to +Y so the family's reference
            // plane orients consistently across drops.
            return doc.Create.NewFamilyInstance(bottomFaceRef, rec.Location, XYZ.BasisY, sym);
        }

        private static void StampInstance(FamilyInstance fi, PenetrationRecord rec, string controlNumber)
        {
            try
            {
                ParameterHelpers.SetString(fi, "PEN_FIRE_RATING_TXT",       rec.FireRating ?? "FR60", overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_OD_MM",                 rec.MemberDiameterMm.ToString("F0"), overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_HOST_REF_TXT",          $"FLR:{rec.HostFloorId?.Value}", overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_MEMBER_ID_TXT",         rec.MemberId?.Value.ToString() ?? "", overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_CONTROL_NUMBER_TXT",    controlNumber, overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_INSTALL_STATUS_TXT",    "DRAFT", overwrite: false);
                ParameterHelpers.SetString(fi, "STING_SEED_FAMILY_TXT",     "STING_SEED_SpecialityEquipment", overwrite: false);
            }
            catch (Exception ex) { StingLog.Warn($"StampInstance {fi?.Id}: {ex.Message}"); }
        }

        private static bool StampMemberFallback(Document doc, PenetrationRecord rec)
        {
            try
            {
                var member = doc.GetElement(rec.MemberId);
                if (member == null) return false;
                string n = MintControlNumber();
                ParameterHelpers.SetString(member, "STING_PENETRATION_REF_TXT",
                    $"FLR:{rec.HostFloorId?.Value}@{rec.FireRating}#{n}", overwrite: true);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"StampMemberFallback: {ex.Message}"); return false; }
        }

        private static string MintControlNumber()
        {
            int n = Interlocked.Increment(ref _nextControlNumber);
            return $"FRP-{n:D4}";
        }
    }
}
