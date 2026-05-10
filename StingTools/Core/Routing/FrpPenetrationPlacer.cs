// StingTools — FrpPenetrationPlacer.
//
// Closes the loop from the penetration detectors. For each
// PenetrationRecord (slab / wall / beam) this placer locates the
// loaded STING_SEED_SpecialityEquipment family (or any family whose
// STING_SEED_FAMILY_TXT marks it as a SpecialityEquipment seed) and
// drops a face-based instance at the crossing point, stamping every
// PEN_* parameter from the record onto the new instance.
//
// Multi-host support added in Phase 178d: the placer dispatches to
// floor / wall / beam face resolution depending on rec.HostKind. Each
// host class has its own face-acquisition strategy; the parameter
// stamping schema is identical so downstream Penetration Register
// schedules and the coverage validator don't care about host class.
//
// Idempotence: every placed instance carries PEN_PFV_UUID_TXT keyed
// on UUIDv5(host-uniqueId, member-uniqueId). Re-running the placer
// against the same set finds the existing instance and updates its
// metadata in place rather than creating a duplicate. This UUID is
// the SAME schema the SleeveEngine uses, so a sleeve and an FRP
// instance for the same physical hole share one identity.
//
// When the family is NOT loaded the placer warns once and stamps the
// member-side parameters only.
//
// After the user runs SwapToManufacturerCommand to replace the seed
// with a real Hilti / Promat / etc. family, every placed instance
// inherits the manufacturer geometry while keeping the PEN_* metadata
// the placer stamped — the Penetration Register schedule keeps
// rendering correctly.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        // SLEEVE_GENERIC handles non-rated penetrations (beam holes for
        // service routing where the host carries no fire compartment).
        private static readonly Dictionary<string, string> _ratingToType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "FR30",  "FR30"  },
                { "FR60",  "FR60"  },
                { "FR90",  "FR90"  },
                { "FR120", "FR120" },
                { "",      "SLEEVE_GENERIC" },
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

            // Index types by rating on the firestop seed (back-compat
            // path used when PenetrationProductSelector chooses
            // PenetrationProductKind.Firestop or SleeveGeneric).
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
                if (!typesByRating.ContainsKey("__default__")) typesByRating["__default__"] = sym;
            }

            // Phase 178f — pre-resolve the fire-damper and acoustic-
            // seal seeds. Non-fatal when missing — selector falls back
            // to firestop and a warning lands in the result panel.
            var damperFamily   = ResolveSeedFamilyByName(doc, "STING_SEED_FireDamper");
            var acousticFamily = ResolveSeedFamilyByName(doc, "STING_SEED_AcousticSeal");
            var damperTypes    = IndexFamilySymbols(doc, damperFamily);
            var acousticTypes  = IndexFamilySymbols(doc, acousticFamily);

            // Build idempotence index — every existing penetration
            // instance keyed by its PEN_PFV_UUID_TXT. Re-runs of the
            // placer update in place rather than duplicate.
            var existingByUuid = BuildExistingIndex(doc, seedFamily);

            foreach (var rec in records)
            {
                try
                {
                    if (rec.HostId == null || rec.HostId == ElementId.InvalidElementId)
                    { r.Skipped++; continue; }

                    var host = doc.GetElement(rec.HostId);
                    if (host == null) { r.Skipped++; continue; }

                    // Compute the deterministic UUID first — it drives
                    // both idempotence and cross-pipeline pairing with
                    // SleeveEngine.
                    string pfvUuid = MakePfvUuid(host, doc.GetElement(rec.MemberId));

                    // Already placed? Update the metadata (rating,
                    // structural flag, OD might have changed since the
                    // last sweep) and skip the create-call.
                    if (existingByUuid.TryGetValue(pfvUuid, out var existingFi) && existingFi != null)
                    {
                        StampInstance(existingFi, rec, ReadOrMintControlNumber(existingFi), pfvUuid);
                        r.Stamped++;
                        continue;
                    }

                    // Phase 178f — dispatch via PenetrationProductSelector.
                    // Picks the right family (firestop / fire damper /
                    // acoustic seal / generic sleeve) for this record's
                    // member-class × host-rating combo. Falls back to
                    // the legacy firestop seed when the chosen family
                    // isn't loaded.
                    var choice = PenetrationProductSelector.Select(doc, rec);
                    Dictionary<string, FamilySymbol> chosenIndex = typesByRating;
                    if (choice.Kind == PenetrationProductKind.FireDamper && damperTypes != null && damperTypes.Count > 0)
                        chosenIndex = damperTypes;
                    else if (choice.Kind == PenetrationProductKind.AcousticSeal && acousticTypes != null && acousticTypes.Count > 0)
                        chosenIndex = acousticTypes;
                    else if (choice.Kind == PenetrationProductKind.FireDamper)
                        r.Warnings.Add($"Duct penetration of {rec.FireRating} barrier needs a fire damper but STING_SEED_FireDamper isn't loaded — falling back to firestop. Run BuildSeedFamilies.");
                    else if (choice.Kind == PenetrationProductKind.AcousticSeal)
                        r.Warnings.Add("Acoustic-only host but STING_SEED_AcousticSeal isn't loaded — falling back to firestop. Run BuildSeedFamilies.");

                    string variantHint = choice.TypeVariantHint ?? "";
                    string rating = rec.FireRating ?? "";
                    if (rec.HostKind == PenetrationHostKind.Beam && string.IsNullOrEmpty(rating))
                        rating = "";
                    else if (string.IsNullOrEmpty(rating))
                        rating = "FR60";

                    FamilySymbol sym = null;
                    // Prefer the variant hint chosen by the selector.
                    if (!string.IsNullOrEmpty(variantHint))
                        chosenIndex.TryGetValue(variantHint, out sym);
                    if (sym == null)
                        chosenIndex.TryGetValue(rating, out sym);
                    if (sym == null && !chosenIndex.TryGetValue("__default__", out sym))
                    { r.Skipped++; continue; }
                    if (variantHint != null && sym?.Name?.IndexOf(variantHint, StringComparison.OrdinalIgnoreCase) < 0)
                        r.Warnings.Add($"Penetration product variant '{variantHint}' missing on family — used '{sym.Name}' for member {rec.MemberId?.Value}.");

                    if (!sym.IsActive)
                    {
                        try { sym.Activate(); doc.Regenerate(); }
                        catch (Exception ex) { r.Warnings.Add($"Activate {sym.Name}: {ex.Message}"); }
                    }

                    FamilyInstance fi = null;
                    try
                    {
                        fi = PlaceOnHost(doc, sym, host, rec);
                    }
                    catch (Exception ex) { r.Warnings.Add($"Face-based place: {ex.Message}"); }

                    if (fi == null)
                    {
                        try
                        {
                            fi = doc.Create.NewFamilyInstance(rec.Location, sym, host,
                                StructuralType.NonStructural);
                        }
                        catch (Exception ex)
                        {
                            r.Errors++;
                            r.Warnings.Add($"Place {sym.Name} at {rec.MemberId?.Value}: {ex.Message}");
                            continue;
                        }
                    }

                    StampInstance(fi, rec, MintControlNumber(), pfvUuid);
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

        // ── Multi-host placement dispatcher ─────────────────────────────

        private static FamilyInstance PlaceOnHost(Document doc, FamilySymbol sym, Element host,
            PenetrationRecord rec)
        {
            switch (rec.HostKind)
            {
                case PenetrationHostKind.Floor:
                    if (host is Floor floor) return TryFaceBasedPlace(doc, sym, floor, rec);
                    break;
                case PenetrationHostKind.Wall:
                    if (host is Wall wall) return TryFaceBasedPlaceOnWall(doc, sym, wall, rec);
                    break;
                case PenetrationHostKind.Beam:
                    if (host is FamilyInstance fi) return TryFaceBasedPlaceOnBeam(doc, sym, fi, rec);
                    break;
            }
            return null;
        }

        private static FamilyInstance TryFaceBasedPlaceOnWall(Document doc, FamilySymbol sym, Wall wall,
            PenetrationRecord rec)
        {
            var opts = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false };
            var geo = wall.get_Geometry(opts);
            if (geo == null) return null;

            // Find the wall face whose normal is most aligned with the
            // pipe's incoming direction. Walls have two main faces +
            // edges; pick the side closer to the run's start.
            Reference faceRef = null;
            double bestScore = double.NegativeInfinity;
            foreach (GeometryObject go in geo)
            {
                if (!(go is Solid s) || s.Faces == null) continue;
                foreach (Face f in s.Faces)
                {
                    if (!(f is PlanarFace pf)) continue;
                    if (Math.Abs(pf.FaceNormal.Z) > 0.5) continue; // skip top/bottom edges
                    var bb = pf.GetBoundingBox();
                    if (bb == null) continue;
                    var pt = pf.Evaluate(new UV(0.5 * (bb.Min.U + bb.Max.U), 0.5 * (bb.Min.V + bb.Max.V)));
                    double score = -pt.DistanceTo(rec.Location);
                    if (score > bestScore) { bestScore = score; faceRef = pf.Reference; }
                }
            }
            if (faceRef == null) return null;

            // Up-vector along the wall's vertical so the family's
            // reference plane stays plumb on the wall.
            return doc.Create.NewFamilyInstance(faceRef, rec.Location, XYZ.BasisZ, sym);
        }

        private static FamilyInstance TryFaceBasedPlaceOnBeam(Document doc, FamilySymbol sym,
            FamilyInstance beam, PenetrationRecord rec)
        {
            // Beams are typically face-based hosting candidates only via
            // their generated solid. We pick the face whose centre is
            // closest to the recorded crossing.
            var opts = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false };
            var geo = beam.get_Geometry(opts);
            if (geo == null) return null;
            Reference faceRef = null;
            double bestDist = double.MaxValue;
            foreach (GeometryObject go in geo)
            {
                Solid s = go as Solid;
                if (s == null && go is GeometryInstance gi)
                {
                    foreach (var inner in gi.GetInstanceGeometry())
                        if (inner is Solid sx && sx.Volume > 0) { s = sx; break; }
                }
                if (s == null || s.Faces == null) continue;
                foreach (Face f in s.Faces)
                {
                    if (!(f is PlanarFace pf)) continue;
                    var bb = pf.GetBoundingBox();
                    if (bb == null) continue;
                    var pt = pf.Evaluate(new UV(0.5 * (bb.Min.U + bb.Max.U), 0.5 * (bb.Min.V + bb.Max.V)));
                    double d = pt.DistanceTo(rec.Location);
                    if (d < bestDist) { bestDist = d; faceRef = pf.Reference; }
                }
            }
            if (faceRef == null) return null;
            return doc.Create.NewFamilyInstance(faceRef, rec.Location, XYZ.BasisZ, sym);
        }

        private static Dictionary<string, FamilyInstance> BuildExistingIndex(Document doc, Family seedFamily)
        {
            var idx = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var symId in seedFamily.GetFamilySymbolIds())
                {
                    var col = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .OfCategoryId(new ElementId(BuiltInCategory.OST_SpecialityEquipment));
                    foreach (var el in col)
                    {
                        if (!(el is FamilyInstance fi)) continue;
                        if (fi.Symbol?.Family?.Id != seedFamily.Id) continue;
                        string uuid = ParameterHelpers.GetString(fi, "PEN_PFV_UUID_TXT");
                        if (!string.IsNullOrEmpty(uuid)) idx[uuid] = fi;
                    }
                    break; // collector is family-wide; iterate once
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildExistingIndex: {ex.Message}"); }
            return idx;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Phase 178f — locate any STING seed family by exact name.
        /// Used to find the fire-damper and acoustic-seal seeds the
        /// PenetrationProductSelector dispatches to.
        /// </summary>
        private static Family ResolveSeedFamilyByName(Document doc, string name)
        {
            if (doc == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSeedFamilyByName({name}): {ex.Message}"); return null; }
        }

        /// <summary>
        /// Index a family's symbols by name + add a "__default__" key
        /// so the placer can pick a fallback when no variant matches.
        /// </summary>
        private static Dictionary<string, FamilySymbol> IndexFamilySymbols(Document doc, Family fam)
        {
            var idx = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || fam == null) return idx;
            try
            {
                foreach (var symId in fam.GetFamilySymbolIds())
                {
                    var sym = doc.GetElement(symId) as FamilySymbol;
                    if (sym == null) continue;
                    string nm = sym.Name ?? "";
                    if (!idx.ContainsKey(nm)) idx[nm] = sym;
                    if (!idx.ContainsKey("__default__")) idx["__default__"] = sym;
                }
            }
            catch (Exception ex) { StingLog.Warn($"IndexFamilySymbols({fam?.Name}): {ex.Message}"); }
            return idx;
        }

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

        private static void StampInstance(FamilyInstance fi, PenetrationRecord rec,
            string controlNumber, string pfvUuid)
        {
            try
            {
                ParameterHelpers.SetString(fi, "PEN_FIRE_RATING_TXT",       rec.FireRating ?? "", overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_OD_MM",                 rec.MemberDiameterMm.ToString("F0"), overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_HOST_REF_TXT",          HostRefString(rec), overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_HOST_TYPE_TXT",         rec.HostKind.ToString().ToUpperInvariant(), overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_MEMBER_ID_TXT",         rec.MemberId?.Value.ToString() ?? "", overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_CONTROL_NUMBER_TXT",    controlNumber, overwrite: true);
                ParameterHelpers.SetString(fi, "PEN_INSTALL_STATUS_TXT",    "DRAFT", overwrite: false);
                ParameterHelpers.SetString(fi, "PEN_PFV_UUID_TXT",          pfvUuid ?? "", overwrite: true);
                // Stamp the family identity from the actual placed
                // symbol — fire dampers and acoustic seals carry their
                // own seed name (Phase 178f) so the swap registry and
                // coverage validator can tell them apart from
                // firestops.
                string seedName = fi.Symbol?.Family?.Name;
                if (string.IsNullOrEmpty(seedName)) seedName = "STING_SEED_SpecialityEquipment";
                ParameterHelpers.SetString(fi, "STING_SEED_FAMILY_TXT", seedName, overwrite: false);

                // Beam-only fields. Empty strings on slab / wall hosts.
                if (rec.HostKind == PenetrationHostKind.Beam)
                {
                    if (rec.BeamSpanMm > 0)
                    {
                        double pct = rec.DistanceFromSupportMm / rec.BeamSpanMm * 100.0;
                        ParameterHelpers.SetString(fi, "PEN_BEAM_OFFSET_PCT", pct.ToString("F1"), overwrite: true);
                    }
                    if (rec.BeamDepthMm > 0)
                    {
                        double ratio = rec.MemberDiameterMm / rec.BeamDepthMm;
                        ParameterHelpers.SetString(fi, "PEN_BEAM_DEPTH_RATIO", ratio.ToString("F2"), overwrite: true);
                    }
                    if (!string.IsNullOrEmpty(rec.StructuralFlag))
                        ParameterHelpers.SetString(fi, "PEN_STRUCTURAL_FLAG_TXT", rec.StructuralFlag, overwrite: true);
                }
            }
            catch (Exception ex) { StingLog.Warn($"StampInstance {fi?.Id}: {ex.Message}"); }
        }

        private static string HostRefString(PenetrationRecord rec)
        {
            string prefix = rec.HostKind switch
            {
                PenetrationHostKind.Floor   => "FLR",
                PenetrationHostKind.Wall    => "WAL",
                PenetrationHostKind.Beam    => "BEM",
                PenetrationHostKind.Ceiling => "CLG",
                PenetrationHostKind.Roof    => "RFM",
                _ => "HST"
            };
            return $"{prefix}:{rec.HostId?.Value}";
        }

        private static string ReadOrMintControlNumber(FamilyInstance existing)
        {
            try
            {
                var s = ParameterHelpers.GetString(existing, "PEN_CONTROL_NUMBER_TXT");
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return MintControlNumber();
        }

        /// <summary>
        /// Deterministic UUIDv5(host, member). Same scheme used by
        /// SleeveEngine — pairs an FRP instance with its sleeve when
        /// both pipelines target the same physical hole.
        /// </summary>
        public static string MakePfvUuid(Element host, Element member)
        {
            try
            {
                string seed = $"{host?.UniqueId}|{member?.UniqueId}";
                using var sha = SHA1.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("STING_PFV|" + seed));
                var g = new byte[16];
                Array.Copy(bytes, g, 16);
                g[6] = (byte)((g[6] & 0x0F) | 0x50);
                g[8] = (byte)((g[8] & 0x3F) | 0x80);
                return new Guid(g).ToString();
            }
            catch { return Guid.NewGuid().ToString(); }
        }

        private static bool StampMemberFallback(Document doc, PenetrationRecord rec)
        {
            try
            {
                var member = doc.GetElement(rec.MemberId);
                if (member == null) return false;
                string n = MintControlNumber();
                string prefix = rec.HostKind switch
                {
                    PenetrationHostKind.Wall => "WAL",
                    PenetrationHostKind.Beam => "BEM",
                    _ => "FLR",
                };
                ParameterHelpers.SetString(member, "STING_PENETRATION_REF_TXT",
                    $"{prefix}:{rec.HostId?.Value}@{rec.FireRating}#{n}", overwrite: true);
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
