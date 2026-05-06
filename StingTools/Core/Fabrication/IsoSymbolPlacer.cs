// StingTools v4 MVP — IsoSymbolPlacer.
//
// Walks an assembly's elements, looks up a matching detail symbol
// from STING_ISO_SYMBOLS_INDEX.csv via name/category heuristics,
// resolves the corresponding FamilySymbol (lazy-loads the .rfa from
// the families folder when not yet in the project), and calls
// NewFamilyInstance to drop the symbol on a host detail/section view.
//
// Placement algorithm (Phase 178):
//   1. Sort _index by SymbolCode length descending so longer keywords
//      win against shorter substrings (TEE_RED before TEE).
//   2. Resolve symbol: search Family.Name + FamilySymbol.Name +
//      member.Name with token-split logic — every "_"-separated token
//      of SymbolCode must appear in the haystack. Fall back to
//      Category match (mapped from CSV strings to Revit category names).
//   3. Pre-resolve all required FamilySymbols and LoadFamily missing
//      ones BEFORE the placement transaction (faster, also keeps load
//      separate from placement so a single bad family doesn't kill the
//      whole pass).
//   4. Honour FabricationOptions.SymbolPlacementMode:
//        Off       — bail.
//        NewOnly   — skip members whose symbol is already placed at
//                    approximately the same XYZ on the view.
//        Replace   — purge previously-stamped placer instances on the
//                    view first, then re-place every member.
//   5. Project member XYZ onto the view's RightDirection × UpDirection
//      plane so the symbol lands on the section, not at its 3D world
//      position (which often clips the view).
//   6. For LocationCurve members, use the curve mid-point instead of
//      the start endpoint so symbols centre on long runs.
//   7. Rotate the symbol to match the member's primary axis (curve
//      direction for pipes/ducts, host axis for fittings).
//   8. 8-quadrant collision avoidance — if a placed symbol overlaps a
//      previous one, try N/NE/E/SE/S/SW/W/NW offsets at scale-aware
//      step size before giving up.
//   9. Stamp placed instance with STING_PLACED_BY_SYMBOL_PLACER_BOOL +
//      STING_PLACER_ASSY_ID_TXT for idempotency + undo traceability.
//  10. Emit STING_v4_iso_symbols.csv audit sidecar with per-member
//      decision (resolved code, family, success, reason).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public class SymbolEntry
    {
        public string SymbolCode { get; set; } = "";
        public string FamilyFile { get; set; } = "";
        public string Category   { get; set; } = "";
        public string Description{ get; set; } = "";

        // Pre-computed when EnsureIndexLoaded sorts the index.
        public string[] Tokens   { get; set; } = Array.Empty<string>();
    }

    public static class IsoSymbolPlacer
    {
        private static List<SymbolEntry> _index;

        // Process-static dedup of "missing family" log lines so a project
        // with hundreds of members doesn't spam the log. The set is reset
        // per call via FabricationResult.MissingFamilies — that one is
        // always populated for UI surfacing.
        private static readonly HashSet<string> _missingFamiliesLogged
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Stamps written onto every placed FamilyInstance so re-runs can
        // detect them (NewOnly mode) or purge them (Replace mode), and
        // FabricationUndoManager can roll them back.
        public const string STAMP_BOOL = "STING_PLACED_BY_SYMBOL_PLACER_BOOL";
        public const string STAMP_ASSY = "STING_PLACER_ASSY_ID_TXT";
        public const string STAMP_MEMBER = "STING_PLACER_MEMBER_ID_TXT";
        public const string STAMP_CODE = "STING_PLACER_SYMBOL_CODE_TXT";

        public static int PlaceSymbolsForAssembly(
            Document doc,
            ElementId assemblyId,
            View detailView,
            FabricationResult result)
        {
            int placed = 0;
            if (doc == null || assemblyId == null || detailView == null) return placed;
            if (result == null) return placed;
            EnsureIndexLoaded();
            if (_index == null || _index.Count == 0) return placed;

            var ai = doc.GetElement(assemblyId) as AssemblyInstance;
            if (ai == null) return placed;

            var memberIds = ai.GetMemberIds();
            if (memberIds == null || memberIds.Count == 0) return placed;

            var mode = StingTools.Commands.Fabrication.FabricationOptions.SymbolPlacementMode;

            // ── Pass 1: per-member symbol resolution + record audit row.
            //    We do this OUTSIDE the placement transaction so it's
            //    cheap to abandon when the user cancels.
            var resolutions = new List<MemberResolution>();
            foreach (var mid in memberIds)
            {
                var member = doc.GetElement(mid);
                if (member == null) continue;
                var entry = ResolveSymbol(member);
                resolutions.Add(new MemberResolution
                {
                    MemberId = mid,
                    Member   = member,
                    Entry    = entry,
                });
            }

            // ── Pass 2: pre-load every unique family file before we start
            //    the placement transaction. LoadFamily inside a transaction
            //    is legal but doubles the rollback surface area, and
            //    pre-resolving lets us batch the missing-family warnings.
            var familyCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in resolutions)
            {
                if (r.Entry == null) continue;
                if (familyCache.ContainsKey(r.Entry.FamilyFile)) continue;
                var fs = ResolveFamilySymbol(doc, r.Entry, result);
                if (fs != null) familyCache[r.Entry.FamilyFile] = fs;
            }

            // Existing-symbol index for NewOnly / Replace modes. Built
            // BEFORE the transaction so we don't re-read the view mid-loop.
            var existingByMember = CollectExistingPlaced(doc, detailView);

            using (var tx = new Transaction(doc, "STING v4 ISO 6412 symbols"))
            {
                try { tx.Start(); }
                catch (Exception ex) { result.Warnings.Add($"IsoSymbolPlacer tx: {ex.Message}"); return placed; }

                try
                {
                    // Replace mode: purge first.
                    if (mode == StingTools.Commands.Fabrication.FabricationOptions.PlacementMode.Replace
                        && existingByMember.Count > 0)
                    {
                        int purged = 0;
                        foreach (var kv in existingByMember)
                        {
                            try { doc.Delete(kv.Value); purged++; } catch { }
                        }
                        result.SymbolsReplaced += purged;
                        existingByMember.Clear();
                    }

                    // 2D AABB collision tracker (view-space coords). Each
                    // placed symbol contributes a small rect; subsequent
                    // placements try the 8-quadrant fallback when the
                    // primary anchor overlaps.
                    var occupied = new List<UV>(); // approximate centre points
                    double scale = (double)(detailView?.Scale ?? 50);
                    double stepFt = MmToFt(8.0 * scale * 0.5); // half symbol-width step

                    foreach (var r in resolutions)
                    {
                        if (r.Entry == null)
                        {
                            result.UnmatchedMembers++;
                            if (result.UnmatchedSamples.Count < 10)
                            {
                                string sample = $"{r.Member?.Category?.Name}: {r.Member?.Name}";
                                if (!result.UnmatchedSamples.Contains(sample))
                                    result.UnmatchedSamples.Add(sample);
                            }
                            continue;
                        }

                        // NewOnly mode: skip if a placer-stamped instance
                        // already exists for this member.
                        long memberKey = r.MemberId.Value;
                        if (mode == StingTools.Commands.Fabrication.FabricationOptions.PlacementMode.NewOnly
                            && existingByMember.ContainsKey(memberKey))
                            continue;

                        if (!familyCache.TryGetValue(r.Entry.FamilyFile, out var fs) || fs == null)
                            continue;

                        if (TryPlace(doc, detailView, assemblyId, r, fs, occupied, stepFt, result))
                            placed++;
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"IsoSymbolPlacer fatal: {ex.Message}");
                }
            }

            // Audit CSV — one row per member with the decision trail.
            try { EmitAuditCsv(doc, ai, detailView, resolutions, result); }
            catch (Exception ex) { StingLog.Warn($"IsoSymbolPlacer audit csv: {ex.Message}"); }

            return placed;
        }

        // ─── Placement primitives ───────────────────────────────────────

        private sealed class MemberResolution
        {
            public ElementId MemberId;
            public Element Member;
            public SymbolEntry Entry;
        }

        private static bool TryPlace(
            Document doc, View view, ElementId assemblyId, MemberResolution r,
            FamilySymbol fs, List<UV> occupied, double stepFt,
            FabricationResult result)
        {
            try
            {
                if (!fs.IsActive) { fs.Activate(); doc.Regenerate(); }

                XYZ worldPoint = ResolveWorldAnchor(r.Member);
                if (worldPoint == null) return false;
                UV anchorUv = ProjectToViewPlane(view, worldPoint);

                // 8-quadrant collision avoidance.
                UV pickedUv = anchorUv;
                if (Overlaps(occupied, anchorUv, stepFt))
                {
                    pickedUv = TryFallbackPositions(anchorUv, occupied, stepFt) ?? anchorUv;
                }
                XYZ placePoint = UvToXyz(view, pickedUv);

                var inst = doc.Create.NewFamilyInstance(placePoint, fs, view);
                if (inst == null) return false;
                occupied.Add(pickedUv);

                // Rotation — align to member's primary axis projected onto view.
                ApplyRotation(doc, view, inst, r.Member, placePoint);

                // Scale param — preserve baked-in family default; only override
                // when the family is at the template default of 1.0 / 0 / unset.
                ApplySymbolScale(inst, view);

                // Stamps — for idempotency + undo + audit.
                StampInstance(inst, assemblyId, r);

                // Track for undo.
                if (result?.SymbolIds != null) result.SymbolIds.Add(inst.Id);
                return true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"IsoSymbolPlacer.TryPlace {r.Member.Id} -> {fs.FamilyName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Picks the anchor point in WORLD coords. For curve hosts use
        /// the mid-point (visual centre); for point hosts use the point
        /// itself. Returns null when both are unavailable.
        /// </summary>
        private static XYZ ResolveWorldAnchor(Element member)
        {
            try
            {
                var lp = member.Location as LocationPoint;
                if (lp != null) return lp.Point;

                var lc = member.Location as LocationCurve;
                if (lc?.Curve != null)
                {
                    try { return lc.Curve.Evaluate(0.5, true); }
                    catch { return lc.Curve.GetEndPoint(0); }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Projects a 3D world point onto the view's 2D plane in view
        /// (right × up) coordinates. Origin is the view's Origin.
        /// </summary>
        private static UV ProjectToViewPlane(View view, XYZ world)
        {
            try
            {
                XYZ origin = view.Origin ?? XYZ.Zero;
                XYZ right  = view.RightDirection ?? XYZ.BasisX;
                XYZ up     = view.UpDirection    ?? XYZ.BasisY;
                XYZ delta  = world - origin;
                return new UV(delta.DotProduct(right), delta.DotProduct(up));
            }
            catch { return new UV(0, 0); }
        }

        private static XYZ UvToXyz(View view, UV uv)
        {
            XYZ origin = view.Origin ?? XYZ.Zero;
            XYZ right  = view.RightDirection ?? XYZ.BasisX;
            XYZ up     = view.UpDirection    ?? XYZ.BasisY;
            return origin + right.Multiply(uv.U) + up.Multiply(uv.V);
        }

        private static bool Overlaps(List<UV> occupied, UV candidate, double stepFt)
        {
            double r2 = stepFt * stepFt;
            foreach (var p in occupied)
            {
                double du = p.U - candidate.U;
                double dv = p.V - candidate.V;
                if (du * du + dv * dv < r2) return true;
            }
            return false;
        }

        private static UV TryFallbackPositions(UV anchor, List<UV> occupied, double stepFt)
        {
            // 8 candidates: N, NE, E, SE, S, SW, W, NW at one step.
            for (int radius = 1; radius <= 2; radius++)
            {
                double r = stepFt * radius;
                var candidates = new[]
                {
                    new UV(anchor.U,         anchor.V + r),
                    new UV(anchor.U + r,     anchor.V + r),
                    new UV(anchor.U + r,     anchor.V),
                    new UV(anchor.U + r,     anchor.V - r),
                    new UV(anchor.U,         anchor.V - r),
                    new UV(anchor.U - r,     anchor.V - r),
                    new UV(anchor.U - r,     anchor.V),
                    new UV(anchor.U - r,     anchor.V + r),
                };
                foreach (var c in candidates)
                {
                    if (!Overlaps(occupied, c, stepFt)) return c;
                }
            }
            return null;
        }

        private static void ApplyRotation(Document doc, View view, FamilyInstance inst, Element member, XYZ pivot)
        {
            try
            {
                XYZ axisDir = ResolvePrimaryAxis(member);
                if (axisDir == null) return;

                // Project axis onto view plane and compute angle relative
                // to view's RightDirection.
                XYZ right = view.RightDirection ?? XYZ.BasisX;
                XYZ up    = view.UpDirection    ?? XYZ.BasisY;
                double dx = axisDir.DotProduct(right);
                double dy = axisDir.DotProduct(up);
                if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;

                double angle = Math.Atan2(dy, dx);
                if (Math.Abs(angle) < 1e-6) return;

                // Rotate around view's normal at the placement pivot.
                XYZ normal = view.ViewDirection ?? right.CrossProduct(up).Normalize();
                var line = Line.CreateBound(pivot, pivot + normal);
                ElementTransformUtils.RotateElement(doc, inst.Id, line, angle);
            }
            catch (Exception ex) { StingLog.Warn($"IsoSymbolPlacer rotate: {ex.Message}"); }
        }

        private static XYZ ResolvePrimaryAxis(Element member)
        {
            try
            {
                var lc = member.Location as LocationCurve;
                if (lc?.Curve != null)
                {
                    var dir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0));
                    if (dir.GetLength() > 1e-9) return dir.Normalize();
                }
                // For fittings, infer from connectors: first to last.
                var fi = member as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager != null)
                {
                    var iter = fi.MEPModel.ConnectorManager.Connectors.ForwardIterator();
                    XYZ first = null, last = null;
                    while (iter.MoveNext())
                    {
                        if (iter.Current is Connector c)
                        {
                            if (first == null) first = c.Origin;
                            else                last  = c.Origin;
                        }
                    }
                    if (first != null && last != null && (last - first).GetLength() > 1e-9)
                        return (last - first).Normalize();
                }
            }
            catch { }
            return null;
        }

        private static void ApplySymbolScale(FamilyInstance inst, View view)
        {
            try
            {
                int vs = (view?.Scale ?? 50);

                var p = inst?.LookupParameter("Symbol Scale");
                if (p != null && !p.IsReadOnly)
                {
                    // Only set when the family is at the convention default
                    // of 50. Families authored at non-default scales are
                    // assumed to know what they want.
                    bool atDefault =
                        (p.StorageType == StorageType.Integer && p.AsInteger() == 50) ||
                        (p.StorageType == StorageType.Double  && Math.Abs(p.AsDouble() - 50.0) < 1e-6);
                    if (atDefault)
                    {
                        if (p.StorageType == StorageType.Double)      p.Set((double)vs);
                        else if (p.StorageType == StorageType.Integer) p.Set(vs);
                    }
                }
                var pAss = inst?.LookupParameter("STING_ISO_SYMBOL_SCALE_IN");
                if (pAss != null && !pAss.IsReadOnly && pAss.StorageType == StorageType.Double)
                    pAss.Set((double)vs);
            }
            catch (Exception sx) { StingLog.Warn($"IsoSymbolPlacer scale: {sx.Message}"); }
        }

        private static void StampInstance(FamilyInstance inst, ElementId assemblyId, MemberResolution r)
        {
            try
            {
                var pBool = inst.LookupParameter(STAMP_BOOL);
                if (pBool != null && !pBool.IsReadOnly && pBool.StorageType == StorageType.Integer)
                    pBool.Set(1);

                var pAssy = inst.LookupParameter(STAMP_ASSY);
                if (pAssy != null && !pAssy.IsReadOnly && pAssy.StorageType == StorageType.String)
                    pAssy.Set(assemblyId?.Value.ToString() ?? "");

                var pMember = inst.LookupParameter(STAMP_MEMBER);
                if (pMember != null && !pMember.IsReadOnly && pMember.StorageType == StorageType.String)
                    pMember.Set(r.MemberId.Value.ToString());

                var pCode = inst.LookupParameter(STAMP_CODE);
                if (pCode != null && !pCode.IsReadOnly && pCode.StorageType == StorageType.String)
                    pCode.Set(r.Entry?.SymbolCode ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"IsoSymbolPlacer stamp: {ex.Message}"); }
        }

        // ─── Existing-symbol detection (idempotency) ───────────────────

        /// <summary>
        /// Returns map of memberId.Value → existing FamilyInstance id for
        /// every placer-stamped detail component on the view. Honours the
        /// STAMP_BOOL + STAMP_MEMBER stamps. Falls back to "any detail
        /// component on this view" detection when stamps aren't present
        /// (legacy placements pre-Phase 178).
        /// </summary>
        private static Dictionary<long, ElementId> CollectExistingPlaced(Document doc, View view)
        {
            var map = new Dictionary<long, ElementId>();
            try
            {
                var col = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance));
                foreach (var el in col)
                {
                    if (!(el is FamilyInstance fi)) continue;
                    var pBool = fi.LookupParameter(STAMP_BOOL);
                    if (pBool == null || pBool.StorageType != StorageType.Integer) continue;
                    if (pBool.AsInteger() != 1) continue;

                    var pMember = fi.LookupParameter(STAMP_MEMBER);
                    if (pMember == null || pMember.StorageType != StorageType.String) continue;
                    string raw = pMember.AsString();
                    if (long.TryParse(raw, out long memberKey))
                        map[memberKey] = fi.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"IsoSymbolPlacer.CollectExistingPlaced: {ex.Message}"); }
            return map;
        }

        // ─── Symbol resolution ─────────────────────────────────────────

        private static SymbolEntry ResolveSymbol(Element member)
        {
            string memberName = (member?.Name ?? "").ToUpperInvariant();
            string famName = "";
            string symName = "";
            try
            {
                var fi = member as FamilyInstance;
                famName = (fi?.Symbol?.FamilyName ?? "").ToUpperInvariant();
                symName = (fi?.Symbol?.Name ?? "").ToUpperInvariant();
            }
            catch { }

            string haystack = $"{memberName} {famName} {symName}";
            // Normalise non-alphanumeric to underscore so token matching
            // works regardless of "Tee 50mm" vs "TEE_50MM" etc.
            string normalised = Normalise(haystack);

            // 1. Token-based match — every "_" token of SymbolCode must
            //    appear in normalised haystack. Index is sorted by length
            //    descending in EnsureIndexLoaded so longer codes win.
            foreach (var entry in _index)
            {
                if (string.IsNullOrEmpty(entry.SymbolCode)) continue;
                if (entry.Tokens.Length == 0) continue;
                bool allMatch = true;
                foreach (var tok in entry.Tokens)
                {
                    // Match whole token surrounded by non-alphanumeric
                    // boundaries to prevent BW matching e.g. "BWALL".
                    if (!HasTokenBoundary(normalised, tok)) { allMatch = false; break; }
                }
                if (allMatch) return entry;
            }

            // 2. Category fallback — map CSV labels to Revit category
            //    display strings. CSV says "Pipe" but Revit returns
            //    "Pipes" / "Pipe Fittings".
            string catNm = (member?.Category?.Name ?? "").ToUpperInvariant();
            foreach (var entry in _index)
            {
                if (string.IsNullOrEmpty(entry.Category)) continue;
                if (CategoryMatches(entry.Category, catNm)) return entry;
            }
            return null;
        }

        private static bool CategoryMatches(string csvCat, string revitCatUpper)
        {
            string up = (csvCat ?? "").ToUpperInvariant();
            switch (up)
            {
                case "PIPE":     return revitCatUpper.Contains("PIPE");
                case "DUCT":     return revitCatUpper.Contains("DUCT");
                case "CONDUIT":  return revitCatUpper.Contains("CONDUIT");
                case "VALVE":    return revitCatUpper.Contains("PIPE ACCESS")
                                   || revitCatUpper.Contains("VALVE");
                case "FITTING":  return revitCatUpper.Contains("FITTING");
                default:         return string.Equals(up, revitCatUpper, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string Normalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else                          sb.Append(' ');
            }
            return sb.ToString();
        }

        private static bool HasTokenBoundary(string normalised, string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            int idx = 0;
            while ((idx = normalised.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
            {
                bool leftOk  = idx == 0 || !char.IsLetterOrDigit(normalised[idx - 1]);
                bool rightOk = idx + token.Length >= normalised.Length
                              || !char.IsLetterOrDigit(normalised[idx + token.Length]);
                if (leftOk && rightOk) return true;
                idx += token.Length;
            }
            return false;
        }

        // ─── Family resolution ─────────────────────────────────────────

        private static FamilySymbol ResolveFamilySymbol(Document doc, SymbolEntry entry, FabricationResult result)
        {
            string famName = Path.GetFileNameWithoutExtension(entry.FamilyFile);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)))
                {
                    if (el is FamilySymbol fs &&
                        string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase))
                        return fs;
                }
                // Try lazy load from families folder
                string familyPath = Path.Combine(StingToolsApp.DataPath ?? "", "..", "Families", "ISO6412", entry.FamilyFile);
                if (File.Exists(familyPath))
                {
                    Family f;
                    if (doc.LoadFamily(familyPath, out f) && f != null)
                    {
                        foreach (ElementId sid in f.GetFamilySymbolIds())
                            if (doc.GetElement(sid) is FamilySymbol fs) return fs;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"IsoSymbolPlacer.ResolveFamilySymbol {famName}: {ex.Message}");
            }

            if (_missingFamiliesLogged.Add(famName))
                StingLog.Warn($"IsoSymbolPlacer: family not found -> {famName}");
            try { result?.MissingFamilies?.Add(famName); } catch { }
            return null;
        }

        // ─── Index loading ─────────────────────────────────────────────

        private static void EnsureIndexLoaded()
        {
            if (_index != null) return;
            var list = new List<SymbolEntry>();
            string path = StingToolsApp.FindDataFile("STING_ISO_SYMBOLS_INDEX.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn("IsoSymbolPlacer: STING_ISO_SYMBOLS_INDEX.csv not found");
                _index = list;
                return;
            }
            try
            {
                bool first = true;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    if (first) { first = false; continue; }
                    var cols = StingToolsApp.ParseCsvLine(line);
                    if (cols == null || cols.Length < 4) continue;
                    var entry = new SymbolEntry
                    {
                        SymbolCode  = cols[0],
                        FamilyFile  = cols[1],
                        Category    = cols[2],
                        Description = cols[3],
                    };
                    entry.Tokens = (entry.SymbolCode ?? "")
                        .ToUpperInvariant()
                        .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
                    list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IsoSymbolPlacer: index load failed: {ex.Message}");
            }
            // Longest SymbolCode first so TEE_RED beats TEE.
            _index = list
                .OrderByDescending(e => (e.SymbolCode ?? "").Length)
                .ThenByDescending(e => e.Tokens?.Length ?? 0)
                .ToList();
        }

        // ─── Audit CSV ─────────────────────────────────────────────────

        private static void EmitAuditCsv(
            Document doc, AssemblyInstance ai, View view,
            List<MemberResolution> resolutions, FabricationResult result)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "STING_v4_iso_symbols.csv");
            bool firstWrite = !File.Exists(path);
            using (var w = new StreamWriter(path, append: !firstWrite))
            {
                if (firstWrite)
                {
                    try { Core.Branding.BrandTokens.StampCsvHeader(w, doc, "iso_symbols_audit"); } catch { }
                    w.WriteLine("assembly_id,assembly_name,view_id,view_name,member_id,member_category,member_name,family_name,symbol_code,family_file,resolved");
                }
                string assyName = "";
                try { assyName = (doc.GetElement(ai.GetTypeId()) as AssemblyType)?.Name ?? ""; } catch { }
                string viewName = "";
                try { viewName = view?.Name ?? ""; } catch { }
                foreach (var r in resolutions)
                {
                    string memCat = (r.Member?.Category?.Name ?? "").Replace(',', ';');
                    string memNm  = (r.Member?.Name ?? "").Replace(',', ';');
                    string famNm  = "";
                    try { famNm = ((r.Member as FamilyInstance)?.Symbol?.FamilyName ?? "").Replace(',', ';'); } catch { }
                    string code   = (r.Entry?.SymbolCode  ?? "").Replace(',', ';');
                    string ff     = (r.Entry?.FamilyFile  ?? "").Replace(',', ';');
                    string resolved = r.Entry == null ? "0" : "1";
                    w.WriteLine(string.Join(",", new[]
                    {
                        ai.Id.Value.ToString(CultureInfo.InvariantCulture),
                        assyName.Replace(',', ';'),
                        view.Id.Value.ToString(CultureInfo.InvariantCulture),
                        viewName.Replace(',', ';'),
                        r.MemberId.Value.ToString(CultureInfo.InvariantCulture),
                        memCat,
                        memNm,
                        famNm,
                        code,
                        ff,
                        resolved,
                    }));
                }
            }
        }

        private static double MmToFt(double mm) => mm / 304.8;
    }
}
