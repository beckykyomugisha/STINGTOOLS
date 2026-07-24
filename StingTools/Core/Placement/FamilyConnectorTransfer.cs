// FamilyConnectorTransfer — preserve MEP connectors across a P2 template rebuild.
//
// WHY THIS EXISTS
// ElementTransformUtils.CopyElements does not carry ConnectorElements between
// family documents. Before this engine the converter merely COUNTED the source
// connectors and told the user to re-add them by hand, which made a P2 rebuild
// effectively unusable for MEP content — a converted AHU / luminaire / socket
// had no connectors and so could not join a system.
//
// TWO TIERS
//   Tier 1 — STING seed families: the connector set is already declared in
//            Data/Seeds/STING_SEED_*.json. Re-mint it declaratively through
//            SymbolLibraryCreator.AddConnectors — exact, no geometric guessing.
//   Tier 2 — ANY family (vendor / legacy): harvest every ConnectorElement from
//            the source family doc, then re-create each one on the rebuilt
//            geometry in the target doc.
//
// TIER 2 PLACEMENT STRATEGY (three attempts, best fidelity first)
//   A. Face match — find the copied PlanarFace that hosted the connector and
//      create on face.Reference. This is the highest-fidelity result: the
//      connector stays bound to real geometry and moves with it.
//   B. Reference-line fallback — mint a short ModelCurve at the harvested
//      origin/normal and create on its endpoint reference. This is the same
//      technique SymbolLibraryCreator.AddConnectorList uses to author seed
//      connectors, so it is proven in this codebase. It recovers connectors
//      that were hosted on a reference plane, or whose host face did not copy.
//      DEVIATION FROM SPEC: the addendum went straight from "no face match" to
//      "report as manual". Attempt B recovers most of that population instead,
//      and only genuine failures fall through to C.
//   C. Manual — record the spec with exact origin / normal / domain / shape /
//      size so a hand re-add is a two-minute job, never a forensic exercise.
//
// A connector is NEVER silently lost: every harvested spec ends up counted in
// exactly one of Recreated or Manual, and Manual entries carry full coordinates.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Everything needed to re-create one ConnectorElement in another family
    /// document. Captured from the source family before it is closed.
    /// </summary>
    public sealed class ConnectorSpec
    {
        public Domain Domain { get; set; } = Domain.DomainUndefined;

        /// <summary>
        /// Discriminates the two occupants of Domain.DomainCableTrayConduit.
        /// "Conduit" | "CableTray" | "" (unknown → treated as Conduit).
        /// </summary>
        public string CableTrayConduitKind { get; set; } = "";

        public MEPSystemClassification Classification { get; set; }
            = MEPSystemClassification.UndefinedSystemClassification;

        public XYZ Origin { get; set; } = XYZ.Zero;
        public XYZ BasisX { get; set; } = XYZ.BasisX;
        public XYZ BasisY { get; set; } = XYZ.BasisY;
        /// <summary>Outward normal — the direction the connector faces.</summary>
        public XYZ BasisZ { get; set; } = XYZ.BasisZ;

        public ConnectorProfileType Shape { get; set; } = ConnectorProfileType.Round;
        /// <summary>Feet. Round profiles only.</summary>
        public double Radius { get; set; }
        /// <summary>Feet. Rectangular / oval profiles only.</summary>
        public double Width { get; set; }
        /// <summary>Feet. Rectangular / oval profiles only.</summary>
        public double Height { get; set; }

        public bool IsPrimary { get; set; }
        public string Description { get; set; } = "";

        /// <summary>
        /// Writable non-dimensional parameters harvested by name, replayed after
        /// creation. Values are the raw storage values (double / int / string).
        /// </summary>
        public Dictionary<string, object> Params { get; } =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Human-readable one-liner for the manual re-add report.</summary>
        public string Describe()
        {
            string size = Shape == ConnectorProfileType.Round
                ? $"Ø{FtToMm(Radius * 2.0):F0}mm"
                : $"{FtToMm(Width):F0}×{FtToMm(Height):F0}mm";
            string dom = Domain == Domain.DomainCableTrayConduit && !string.IsNullOrEmpty(CableTrayConduitKind)
                ? CableTrayConduitKind
                : Domain.ToString().Replace("Domain", "");
            return $"{dom} / {Classification} · {Shape} {size} · " +
                   $"origin ({FtToMm(Origin.X):F1}, {FtToMm(Origin.Y):F1}, {FtToMm(Origin.Z):F1})mm · " +
                   $"normal ({BasisZ.X:F3}, {BasisZ.Y:F3}, {BasisZ.Z:F3})" +
                   (IsPrimary ? " · PRIMARY" : "");
        }

        private static double FtToMm(double ft) => ft * 304.8;
    }

    /// <summary>Per-family outcome of a connector transfer.</summary>
    public sealed class ConnectorTransferResult
    {
        /// <summary>"Tier1_SeedRemint" | "Tier2_Harvest" | "None".</summary>
        public string Tier { get; set; } = "None";
        public int Harvested { get; set; }
        public int Recreated { get; set; }
        /// <summary>Re-created bound to a real copied face (highest fidelity).</summary>
        public int OnFace { get; set; }
        /// <summary>Re-created on a minted reference line (geometry-independent).</summary>
        public int OnReferenceLine { get; set; }
        public int Manual => Math.Max(0, Harvested - Recreated);

        public List<string> ManualDetails { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>The single headline line for the converter report.</summary>
        public string Summary()
        {
            if (Harvested == 0 && Tier == "None") return "Connectors: none on the source family.";
            if (Tier == "Tier1_SeedRemint")
                return $"Connectors: {Recreated} re-minted from the STING seed spec (exact — declarative rebuild).";
            return $"Connectors: {Harvested} harvested · {Recreated} re-created " +
                   $"({OnFace} on copied faces, {OnReferenceLine} on reference lines) · {Manual} need manual re-add";
        }
    }

    /// <summary>
    /// Stateless connector preservation engine used by FamilyHostConverter's P2
    /// rebuild path. No UI, no TaskDialog — warnings surface through the result.
    /// </summary>
    public static class FamilyConnectorTransfer
    {
        /// <summary>Face-plane coincidence tolerance — 0.5 mm expressed in feet.</summary>
        private const double PlaneTolFt = 0.5 / 304.8;

        /// <summary>Normal alignment threshold (dot product of unit vectors).</summary>
        private const double NormalDot = 0.999;

        private static double MmToFt(double mm) => mm / 304.8;

        // ── entry point ─────────────────────────────────────────────────────

        /// <summary>
        /// Transfer connectors from <paramref name="src"/> to <paramref name="tgt"/>.
        /// Both must be open family documents. Opens its own transaction on
        /// <paramref name="tgt"/> — must NOT be called inside one.
        /// </summary>
        public static ConnectorTransferResult Transfer(Document src, Document tgt, string familyName)
        {
            var res = new ConnectorTransferResult();
            if (src == null || tgt == null || !src.IsFamilyDocument || !tgt.IsFamilyDocument)
            {
                res.Warnings.Add("Connector transfer skipped — source or target is not an open family document.");
                return res;
            }

            // Tier 1 — declarative re-mint for STING seed families.
            try
            {
                var seedDef = ResolveSeedDefinition(familyName, src);
                if (seedDef != null && seedDef.Connectors != null && seedDef.Connectors.Count > 0)
                {
                    var scr = new StingTools.Core.Symbols.SymbolCreationResult();
                    using (var t = new Transaction(tgt, "STING Rebuild Connectors (seed)"))
                    {
                        t.Start();
                        StingTools.Core.Symbols.SymbolLibraryCreator.AddConnectors(tgt, seedDef, scr);
                        t.Commit();
                    }
                    foreach (var w in scr.Warnings) res.Warnings.Add(w);

                    int made = CountConnectors(tgt);
                    res.Tier = "Tier1_SeedRemint";
                    res.Harvested = seedDef.Connectors.Count;
                    res.Recreated = made;
                    StingLog.Info($"ConnectorTransfer Tier1 '{familyName}': {made} re-minted from seed '{seedDef.Id}'.");
                    if (made > 0) return res;

                    // Seed re-mint produced nothing — fall through to Tier 2
                    // rather than reporting a false success.
                    res.Warnings.Add($"Seed re-mint for '{seedDef.Id}' created no connectors — falling back to harvest/recreate.");
                    res.Harvested = 0;
                    res.Recreated = 0;
                }
            }
            catch (Exception ex)
            {
                res.Warnings.Add($"Seed connector re-mint failed ({ex.Message}) — falling back to harvest/recreate.");
                StingLog.Warn($"ConnectorTransfer Tier1 '{familyName}': {ex.Message}");
            }

            // Tier 2 — harvest → match → recreate.
            var specs = Harvest(src, res);
            res.Harvested = specs.Count;
            if (specs.Count == 0)
            {
                if (res.Tier == "None") res.Tier = "Tier2_Harvest";
                return res;
            }
            res.Tier = "Tier2_Harvest";

            Recreate(tgt, specs, res);
            StingLog.Info($"ConnectorTransfer Tier2 '{familyName}': {res.Harvested} harvested, " +
                          $"{res.Recreated} re-created ({res.OnFace} face / {res.OnReferenceLine} refline), {res.Manual} manual.");
            return res;
        }

        // ── step 1: harvest ─────────────────────────────────────────────────

        /// <summary>
        /// Capture every ConnectorElement in the source family document.
        /// Read-only — safe to call before the source doc is closed.
        /// </summary>
        public static List<ConnectorSpec> Harvest(Document src, ConnectorTransferResult res)
        {
            var specs = new List<ConnectorSpec>();
            if (src == null || !src.IsFamilyDocument) return specs;

            IList<Element> found;
            try
            {
                found = new FilteredElementCollector(src)
                    .OfClass(typeof(ConnectorElement))
                    .ToElements();
            }
            catch (Exception ex)
            {
                res?.Warnings.Add($"Could not collect connectors from the source family: {ex.Message}");
                StingLog.Warn($"ConnectorTransfer harvest collect: {ex.Message}");
                return specs;
            }

            foreach (var el in found)
            {
                if (!(el is ConnectorElement ce)) continue;
                try
                {
                    var spec = new ConnectorSpec();

                    try { spec.Domain = ce.Domain; } catch { }
                    try { spec.Classification = ce.SystemClassification; } catch { }
                    try { spec.Shape = ce.Shape; } catch { }
                    try { spec.IsPrimary = ce.IsPrimary; } catch { }

                    // Origin + orientation. CoordinateSystem.BasisZ is the outward
                    // normal; BasisX fixes the rotation about it.
                    try
                    {
                        Transform cs = ce.CoordinateSystem;
                        if (cs != null)
                        {
                            spec.Origin = cs.Origin;
                            spec.BasisX = cs.BasisX;
                            spec.BasisY = cs.BasisY;
                            spec.BasisZ = cs.BasisZ;
                        }
                    }
                    catch (Exception ex) { res?.Warnings.Add($"Connector coordinate system unreadable: {ex.Message}"); }

                    // Origin property is the authoritative point when available.
                    try { if (ce.Origin != null) spec.Origin = ce.Origin; } catch { }

                    // Dimensions — read through parameters, which are reliable in a
                    // family doc even when the typed properties throw for the
                    // non-applicable profile.
                    spec.Radius = ReadLength(ce, BuiltInParameter.CONNECTOR_RADIUS);
                    if (spec.Radius <= 0)
                    {
                        double dia = ReadLength(ce, BuiltInParameter.CONNECTOR_DIAMETER);
                        if (dia > 0) spec.Radius = dia / 2.0;
                    }
                    if (spec.Radius <= 0) { try { spec.Radius = ce.Radius; } catch { } }
                    spec.Width = ReadLength(ce, BuiltInParameter.CONNECTOR_WIDTH);
                    if (spec.Width <= 0) { try { spec.Width = ce.Width; } catch { } }
                    spec.Height = ReadLength(ce, BuiltInParameter.CONNECTOR_HEIGHT);
                    if (spec.Height <= 0) { try { spec.Height = ce.Height; } catch { } }

                    try
                    {
                        var pd = ce.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION);
                        if (pd != null && pd.StorageType == StorageType.String)
                            spec.Description = pd.AsString() ?? "";
                    }
                    catch { }

                    // Conduit vs cable tray share one Domain value.
                    if (spec.Domain == Domain.DomainCableTrayConduit)
                        spec.CableTrayConduitKind = ResolveCableTrayConduitKind(ce);

                    HarvestWritableParams(ce, spec);
                    specs.Add(spec);
                }
                catch (Exception ex)
                {
                    res?.Warnings.Add($"A connector could not be harvested and will be lost: {ex.Message}");
                    StingLog.Warn($"ConnectorTransfer harvest one: {ex.Message}");
                }
            }
            return specs;
        }

        /// <summary>
        /// Capture writable, non-dimensional parameters by name so domain data
        /// (flow, pressure drop, load classification, voltage, poles …) survives.
        /// Dimensions and identity are replayed separately.
        /// </summary>
        private static void HarvestWritableParams(ConnectorElement ce, ConnectorSpec spec)
        {
            try
            {
                foreach (Parameter p in ce.Parameters)
                {
                    if (p == null || p.IsReadOnly) continue;
                    string name = p.Definition?.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (_dimensionParamNames.Contains(name)) continue; // replayed explicitly

                    switch (p.StorageType)
                    {
                        case StorageType.Double: spec.Params[name] = p.AsDouble(); break;
                        case StorageType.Integer: spec.Params[name] = p.AsInteger(); break;
                        case StorageType.String:
                            string s = p.AsString();
                            if (s != null) spec.Params[name] = s;
                            break;
                        // ElementId values do not map across documents — skipped.
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ConnectorTransfer harvest params: {ex.Message}"); }
        }

        private static readonly HashSet<string> _dimensionParamNames = new HashSet<string>(
            new[] { "Radius", "Diameter", "Width", "Height", "Description" }, StringComparer.OrdinalIgnoreCase);

        private static double ReadLength(ConnectorElement ce, BuiltInParameter bip)
        {
            try
            {
                var p = ce.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }
            return 0.0;
        }

        private static string ResolveCableTrayConduitKind(ConnectorElement ce)
        {
            try
            {
                var p = ce.get_Parameter(BuiltInParameter.RBS_CABLETRAYCONDUIT_CONNECTORELEM_TYPE);
                if (p != null && p.StorageType == StorageType.Integer)
                {
                    // 0 / 1 ordering is not documented as stable, so prefer the
                    // display string when Revit gives us one.
                    string disp = null;
                    try { disp = p.AsValueString(); } catch { }
                    if (!string.IsNullOrEmpty(disp))
                        return disp.IndexOf("tray", StringComparison.OrdinalIgnoreCase) >= 0 ? "CableTray" : "Conduit";
                }
            }
            catch { }
            return "";
        }

        // ── step 2+3: match + create ────────────────────────────────────────

        private static void Recreate(Document tgt, List<ConnectorSpec> specs, ConnectorTransferResult res)
        {
            // ComputeReferences = true is MANDATORY. Without it face.Reference is
            // null and ConnectorElement.Create* silently fails to bind.
            var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

            using var t = new Transaction(tgt, "STING Rebuild Connectors");
            t.Start();

            List<PlanarFace> faces;
            try { faces = CollectPlanarFaces(tgt, opt); }
            catch (Exception ex)
            {
                faces = new List<PlanarFace>();
                res.Warnings.Add($"Target geometry could not be read for face matching ({ex.Message}) — " +
                                 "all connectors will use the reference-line fallback.");
                StingLog.Warn($"ConnectorTransfer CollectPlanarFaces: {ex.Message}");
            }

            foreach (var spec in specs)
            {
                ConnectorElement ce = null;
                bool viaFace = false;

                // Attempt A — bind to the copied face that hosted it.
                try
                {
                    PlanarFace face = MatchFace(faces, spec);
                    if (face?.Reference != null)
                    {
                        ce = Create(tgt, spec, face.Reference);
                        viaFace = ce != null;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ConnectorTransfer face create: {ex.Message}");
                }

                // Attempt B — mint a reference line at the harvested location.
                if (ce == null)
                {
                    try
                    {
                        Reference lineRef = MintReferenceLine(tgt, spec);
                        if (lineRef != null) ce = Create(tgt, spec, lineRef);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ConnectorTransfer refline create: {ex.Message}");
                    }
                }

                // Attempt C — report with full detail; never silently dropped.
                if (ce == null)
                {
                    res.ManualDetails.Add(spec.Describe());
                    continue;
                }

                res.Recreated++;
                if (viaFace) res.OnFace++; else res.OnReferenceLine++;
                ReplayParameters(ce, spec, res);
            }

            t.Commit();
        }

        private static List<PlanarFace> CollectPlanarFaces(Document tgt, Options opt)
        {
            var faces = new List<PlanarFace>();
            var elems = new FilteredElementCollector(tgt)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var e in elems)
            {
                GeometryElement ge;
                try { ge = e.get_Geometry(opt); }
                catch { continue; }
                if (ge == null) continue;
                CollectFacesRecursive(ge, faces);
            }
            return faces;
        }

        private static void CollectFacesRecursive(GeometryElement ge, List<PlanarFace> faces)
        {
            foreach (GeometryObject go in ge)
            {
                if (go is Solid solid)
                {
                    if (solid.Faces == null) continue;
                    foreach (Face f in solid.Faces)
                        if (f is PlanarFace pf) faces.Add(pf);
                }
                else if (go is GeometryInstance gi)
                {
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    if (inner != null) CollectFacesRecursive(inner, faces);
                }
            }
        }

        /// <summary>
        /// Accept a face only when all three hold: the face plane contains the
        /// connector origin, the normals align, and the projected point actually
        /// lands on the face (not merely on its infinite plane).
        /// </summary>
        private static PlanarFace MatchFace(List<PlanarFace> faces, ConnectorSpec spec)
        {
            if (faces == null || faces.Count == 0 || spec.Origin == null) return null;

            PlanarFace best = null;
            double bestDist = double.MaxValue;

            foreach (var f in faces)
            {
                try
                {
                    XYZ n = f.FaceNormal;
                    if (n == null) continue;

                    // 2. normals align
                    if (n.DotProduct(spec.BasisZ) <= NormalDot) continue;

                    // 1. face plane contains the origin
                    double planeDist = Math.Abs((spec.Origin - f.Origin).DotProduct(n));
                    if (planeDist >= PlaneTolFt) continue;

                    // 3. the projected point is genuinely on the face
                    IntersectionResult ir = f.Project(spec.Origin);
                    if (ir == null) continue;

                    double d = ir.Distance;
                    if (d < bestDist) { bestDist = d; best = f; }
                }
                catch { /* a degenerate face must not abort the sweep */ }
            }
            return best;
        }

        /// <summary>
        /// Fallback host: a short model curve at the connector origin lying in the
        /// connector's own plane. Its endpoint reference is a valid connector host
        /// and is independent of whether the source geometry copied cleanly.
        /// This mirrors SymbolLibraryCreator.AddConnectorList's authoring technique.
        /// </summary>
        private static Reference MintReferenceLine(Document tgt, ConnectorSpec spec)
        {
            XYZ origin = spec.Origin ?? XYZ.Zero;
            XYZ normal = spec.BasisZ;
            if (normal == null || normal.IsZeroLength()) normal = XYZ.BasisZ;
            normal = normal.Normalize();

            Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);
            SketchPlane sp = SketchPlane.Create(tgt, plane);

            // A direction lying in the connector plane.
            XYZ dir = spec.BasisX;
            if (dir == null || dir.IsZeroLength() || Math.Abs(dir.DotProduct(normal)) > 0.99)
                dir = Math.Abs(normal.DotProduct(XYZ.BasisZ)) > 0.99 ? XYZ.BasisX : normal.CrossProduct(XYZ.BasisZ);
            if (dir.IsZeroLength()) dir = XYZ.BasisX;
            dir = dir.Normalize();

            XYZ p2 = origin.Add(dir.Multiply(MmToFt(10)));
            if (p2.DistanceTo(origin) < 1e-6) return null;

            ModelCurve mc = tgt.FamilyCreate.NewModelCurve(Line.CreateBound(origin, p2), sp);
            return mc?.GeometryCurve?.GetEndPointReference(0);
        }

        /// <summary>
        /// Create the connector for this spec's domain on the supplied host
        /// reference. Signatures verified against Revit 2025 RevitAPI.dll.
        /// </summary>
        private static ConnectorElement Create(Document tgt, ConnectorSpec spec, Reference hostRef)
        {
            if (hostRef == null) return null;

            switch (spec.Domain)
            {
                case Domain.DomainHvac:
                {
                    DuctSystemType st = MapDuctSystemType(spec.Classification);
                    ConnectorProfileType shape = spec.Shape == ConnectorProfileType.Invalid
                        ? ConnectorProfileType.Round : spec.Shape;
                    return ConnectorElement.CreateDuctConnector(tgt, st, shape, hostRef);
                }

                case Domain.DomainPiping:
                {
                    PipeSystemType st = MapPipeSystemType(spec.Classification);
                    return ConnectorElement.CreatePipeConnector(tgt, st, hostRef);
                }

                case Domain.DomainElectrical:
                {
                    ElectricalSystemType st = MapElectricalSystemType(spec.Classification);
                    return ConnectorElement.CreateElectricalConnector(tgt, st, hostRef);
                }

                case Domain.DomainCableTrayConduit:
                    return string.Equals(spec.CableTrayConduitKind, "CableTray", StringComparison.OrdinalIgnoreCase)
                        ? ConnectorElement.CreateCableTrayConnector(tgt, hostRef)
                        : ConnectorElement.CreateConduitConnector(tgt, hostRef);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Replay dimensions, primary flag, description and the harvested
        /// writable parameters onto the freshly created connector.
        /// </summary>
        private static void ReplayParameters(ConnectorElement ce, ConnectorSpec spec, ConnectorTransferResult res)
        {
            // Dimensions first — a wrong-sized connector will not mate.
            if (spec.Shape == ConnectorProfileType.Round)
            {
                if (spec.Radius > 0)
                {
                    // Some templates expose radius, others diameter.
                    if (!TrySetLength(ce, BuiltInParameter.CONNECTOR_RADIUS, spec.Radius))
                        TrySetLength(ce, BuiltInParameter.CONNECTOR_DIAMETER, spec.Radius * 2.0);
                }
            }
            else
            {
                if (spec.Width > 0) TrySetLength(ce, BuiltInParameter.CONNECTOR_WIDTH, spec.Width);
                if (spec.Height > 0) TrySetLength(ce, BuiltInParameter.CONNECTOR_HEIGHT, spec.Height);
            }

            if (spec.IsPrimary)
            {
                // ConnectorElement.IsPrimary is READ-ONLY (getter only), so the old
                // path — get_Parameter(RBS_CONNECTOR_ISPRIMARY) then p.Set(1) guarded
                // by !p.IsReadOnly — silently did nothing (the parameter is read-only,
                // so the guard skipped the write and the connector was never actually
                // made primary). AssignAsPrimary() is the documented API for this.
                try
                {
                    ce.AssignAsPrimary();
                }
                catch (Exception ex) { StingLog.Warn($"ConnectorTransfer set primary: {ex.Message}"); }
            }

            if (!string.IsNullOrEmpty(spec.Description))
            {
                try
                {
                    var p = ce.get_Parameter(BuiltInParameter.RBS_CONNECTOR_DESCRIPTION);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(spec.Description);
                }
                catch (Exception ex) { StingLog.Warn($"ConnectorTransfer set description: {ex.Message}"); }
            }

            foreach (var kv in spec.Params)
            {
                try
                {
                    Parameter p = ce.LookupParameter(kv.Key);
                    if (p == null || p.IsReadOnly) continue;
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            if (kv.Value is double d) p.Set(d);
                            break;
                        case StorageType.Integer:
                            if (kv.Value is int i) p.Set(i);
                            break;
                        case StorageType.String:
                            if (kv.Value is string s) p.Set(s);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    res.Warnings.Add($"Connector parameter '{kv.Key}' not replayed: {ex.Message}");
                    StingLog.Warn($"ConnectorTransfer replay '{kv.Key}': {ex.Message}");
                }
            }
        }

        private static bool TrySetLength(ConnectorElement ce, BuiltInParameter bip, double ft)
        {
            try
            {
                var p = ce.get_Parameter(bip);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
                p.Set(ft);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConnectorTransfer set {bip}: {ex.Message}");
                return false;
            }
        }

        // ── system-type mapping ─────────────────────────────────────────────
        //
        // MEPSystemClassification member names line up 1:1 with the per-domain
        // enums apart from Electrical (DataCircuit → Data), so parse by name and
        // fall back to the domain's generic type. The factories reject
        // UndefinedSystemType, so a concrete fallback is always supplied.

        private static DuctSystemType MapDuctSystemType(MEPSystemClassification c)
        {
            if (Enum.TryParse(c.ToString(), out DuctSystemType st) && st != DuctSystemType.UndefinedSystemType)
                return st;
            return DuctSystemType.SupplyAir;
        }

        private static PipeSystemType MapPipeSystemType(MEPSystemClassification c)
        {
            if (Enum.TryParse(c.ToString(), out PipeSystemType st) && st != PipeSystemType.UndefinedSystemType)
                return st;
            return PipeSystemType.SupplyHydronic;
        }

        private static ElectricalSystemType MapElectricalSystemType(MEPSystemClassification c)
        {
            string name = c.ToString();
            // Revit 2025 electrical enum uses the short name.
            if (string.Equals(name, "DataCircuit", StringComparison.Ordinal)) name = "Data";
            if (Enum.TryParse(name, out ElectricalSystemType st) && st != ElectricalSystemType.UndefinedSystemType)
                return st;
            return ElectricalSystemType.PowerCircuit;
        }

        // ── Tier 1 seed resolution ──────────────────────────────────────────

        /// <summary>
        /// Resolve the STING seed SymbolDefinition backing this family, if any.
        /// Matches on the seed id stamped into the family name (seed families are
        /// created as "STING Seed — X" / "STING_SEED_X"), then on the definition
        /// name. Returns null for any non-STING family — that is the normal case
        /// and drives the Tier-2 path.
        /// </summary>
        private static StingTools.Core.Symbols.SymbolDefinition ResolveSeedDefinition(string familyName, Document src)
        {
            if (string.IsNullOrWhiteSpace(familyName)) return null;

            string norm = Normalize(familyName);
            foreach (var path in EnumerateSeedSpecs())
            {
                StingTools.Core.Symbols.SymbolLibrary lib;
                try
                {
                    lib = Newtonsoft.Json.JsonConvert.DeserializeObject<StingTools.Core.Symbols.SymbolLibrary>(
                        File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ConnectorTransfer seed parse '{Path.GetFileName(path)}': {ex.Message}");
                    continue;
                }
                if (lib?.Symbols == null) continue;

                foreach (var sym in lib.Symbols)
                {
                    if (sym == null) continue;
                    if (!string.IsNullOrEmpty(sym.Id) && Normalize(sym.Id) == norm) return sym;
                    if (!string.IsNullOrEmpty(sym.Name) && Normalize(sym.Name) == norm) return sym;

                    if (sym.TypeVariants == null) continue;
                    foreach (var v in sym.TypeVariants)
                    {
                        if (v?.Name == null) continue;
                        // A built variant family is named "<seed name> - <variant>".
                        if (Normalize(v.Name) == norm) return sym;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<string> EnumerateSeedSpecs()
        {
            var list = new List<string>();
            try
            {
                string dataPath = StingToolsApp.DataPath;
                if (string.IsNullOrEmpty(dataPath)) return list;
                string seedDir = Path.Combine(dataPath, "Seeds");
                if (!Directory.Exists(seedDir)) return list;
                list.AddRange(Directory.GetFiles(seedDir, "STING_SEED_*.json"));
            }
            catch (Exception ex) { StingLog.Warn($"ConnectorTransfer seed scan: {ex.Message}"); }
            return list;
        }

        /// <summary>Case/'-'/'_'/space/em-dash-insensitive comparison key.</summary>
        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        private static int CountConnectors(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ConnectorElement)).GetElementCount();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ConnectorTransfer count: {ex.Message}");
                return 0;
            }
        }
    }
}
