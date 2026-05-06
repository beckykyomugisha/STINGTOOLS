// StingTools — MEP/FP/SLD Symbol Library creator (Phase 175)
//
// Iterates a SymbolLibrary loaded from JSON, opens the appropriate family
// template per FamilyType, draws normalised geometry into the family
// document, mints connectors / parameters, saves the resulting .rfa
// alongside the project, and loads it back into the active document.
//
// All Revit API calls target the 2025/2026/2027 signatures. The build
// environment has no Revit assemblies, so spots that depend on overload
// resolution are tagged // TODO-VERIFY-API for in-Revit verification.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Symbols
{
    /// <summary>Aggregate result of a CreateAllFromFile run.</summary>
    public sealed class SymbolCreationResult
    {
        public int Created { get; set; }
        public int Existed { get; set; }
        public int Failed { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> CreatedRfaPaths { get; } = new List<string>();
    }

    internal static class SymbolLibraryCreator
    {
        // 1 ft = 304.8 mm — Revit internal length unit is decimal feet.
        private const double MmPerFoot = 304.8;

        /// <summary>Convert millimetres to Revit decimal feet.</summary>
        private static double MmToFt(double mm) => mm / MmPerFoot;

        /// <summary>Scale a normalised coordinate (-0.5..+0.5) to internal feet using symbolSize mm.</summary>
        private static double Scale(double normCoord, double symbolSizeMm)
            => normCoord * MmToFt(symbolSizeMm);

        // ─────────────────────────────────────────────────────────────────
        // Library entry point
        // ─────────────────────────────────────────────────────────────────

        public static SymbolCreationResult CreateAllFromFile(
            Document hostDoc,
            string jsonPath,
            string outputFolder,
            bool loadIntoProject)
        {
            var result = new SymbolCreationResult();
            if (!File.Exists(jsonPath))
            {
                result.Errors.Add($"JSON not found: {jsonPath}");
                return result;
            }

            SymbolLibrary lib;
            try
            {
                lib = JsonConvert.DeserializeObject<SymbolLibrary>(File.ReadAllText(jsonPath));
            }
            catch (Exception ex)
            {
                result.Errors.Add($"JSON parse failed: {ex.Message}");
                return result;
            }
            if (lib?.Symbols == null || lib.Symbols.Count == 0)
            {
                result.Errors.Add("No symbols in library.");
                return result;
            }

            Directory.CreateDirectory(outputFolder);
            var app = hostDoc.Application;
            var templateFolder = ResolveTemplateFolder(app);

            foreach (var def in lib.Symbols)
            {
                if (string.IsNullOrWhiteSpace(def?.Id))
                {
                    result.Failed++;
                    result.Errors.Add("Symbol with empty id skipped.");
                    continue;
                }

                var rfaPath = Path.Combine(outputFolder, def.Id + ".rfa");
                if (File.Exists(rfaPath))
                {
                    result.Existed++;
                    result.CreatedRfaPaths.Add(rfaPath);
                    if (loadIntoProject) TryLoadFamily(hostDoc, rfaPath, result);
                    continue;
                }

                try
                {
                    string built = BuildOne(app, def, outputFolder, templateFolder, result);
                    if (!string.IsNullOrEmpty(built))
                    {
                        result.Created++;
                        result.CreatedRfaPaths.Add(built);
                        if (loadIntoProject) TryLoadFamily(hostDoc, built, result);
                    }
                    else
                    {
                        result.Failed++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{def.Id}: {ex.Message}");
                    StingLog.Error($"SymbolLibraryCreator: {def.Id} failed", ex);
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Per-symbol routing
        // ─────────────────────────────────────────────────────────────────

        private static string BuildOne(Application app, SymbolDefinition def,
            string outputFolder, string templateFolder, SymbolCreationResult result)
        {
            string templateFile = ResolveTemplateFile(def, templateFolder, result);
            if (string.IsNullOrEmpty(templateFile))
            {
                result.Warnings.Add($"{def.Id}: no family template found for {def.FamilyType}/{def.Discipline}");
                return null;
            }

            Document fdoc;
            try
            {
                fdoc = app.NewFamilyDocument(templateFile);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{def.Id}: NewFamilyDocument failed — {ex.Message}");
                return null;
            }
            if (fdoc == null)
            {
                result.Errors.Add($"{def.Id}: NewFamilyDocument returned null.");
                return null;
            }

            try
            {
                using (var tx = new Transaction(fdoc, "STING Create Symbol"))
                {
                    tx.Start();
                    DrawGeometry(fdoc, def, result);
                    AddParameters(fdoc, def, result);
                    if (def.Connectors != null && def.Connectors.Count > 0
                        && !string.Equals(def.FamilyType, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
                    {
                        AddConnectors(fdoc, def, result);
                    }
                    if (def.Solid3D != null
                        && !string.Equals(def.FamilyType, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
                    {
                        AddSolid3D(fdoc, def, result);
                    }
                    tx.Commit();
                }

                var saveAs = new SaveAsOptions { OverwriteExistingFile = true };
                string outPath = Path.Combine(outputFolder, def.Id + ".rfa");
                fdoc.SaveAs(outPath, saveAs);
                fdoc.Close(false);
                return outPath;
            }
            catch (Exception ex)
            {
                try { fdoc.Close(false); } catch (Exception inner) { StingLog.Warn($"SymbolLibraryCreator: close after error — {inner.Message}"); }
                result.Errors.Add($"{def.Id}: {ex.Message}");
                StingLog.Error($"SymbolLibraryCreator.BuildOne {def.Id}", ex);
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Geometry
        // ─────────────────────────────────────────────────────────────────

        private static void DrawGeometry(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            var geo = def.Geometry;
            if (geo == null) return;

            View planView = ResolvePlanView(fdoc);
            if (planView == null)
            {
                result.Warnings.Add($"{def.Id}: no plan view in family template; geometry skipped.");
                return;
            }
            SketchPlane sketch = SketchPlane.Create(fdoc,
                Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));

            double s = def.SymbolSize > 0 ? def.SymbolSize : 3.0;

            if (geo.Lines != null)
                foreach (var l in geo.Lines)
                    DrawLine(fdoc, planView, sketch, l, s, result, def.Id);

            if (geo.ConnectionLines != null)
                foreach (var l in geo.ConnectionLines)
                    DrawLine(fdoc, planView, sketch, l, s, result, def.Id);

            if (geo.Arcs != null)
                foreach (var a in geo.Arcs)
                    DrawArc(fdoc, planView, sketch, a, s, result, def.Id);

            if (geo.FilledRegions != null)
                foreach (var fr in geo.FilledRegions)
                    DrawFilledRegion(fdoc, planView, fr, s, result, def.Id);

            if (geo.Text != null)
                foreach (var t in geo.Text)
                    DrawText(fdoc, planView, t, s, result, def.Id);
        }

        private static void DrawLine(Document fdoc, View view, SketchPlane sketch,
            LineDefinition l, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                XYZ p1 = new XYZ(Scale(l.X1, symMm), Scale(l.Y1, symMm), 0);
                XYZ p2 = new XYZ(Scale(l.X2, symMm), Scale(l.Y2, symMm), 0);
                if (p1.DistanceTo(p2) < 1e-6) return;
                Line line = Line.CreateBound(p1, p2);
                if (fdoc.IsFamilyDocument)
                {
                    // TODO-VERIFY-API: NewSymbolicCurve for family annotation; falls back to NewDetailCurve.
                    fdoc.FamilyCreate.NewDetailCurve(view, line);
                }
                else
                {
                    fdoc.Create.NewDetailCurve(view, line);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: line draw failed — {ex.Message}");
            }
        }

        private static void DrawArc(Document fdoc, View view, SketchPlane sketch,
            ArcDefinition a, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                XYZ centre = new XYZ(Scale(a.Cx, symMm), Scale(a.Cy, symMm), 0);
                double r = Scale(a.R, symMm);
                if (r < 1e-6) return;

                double startRad = a.StartDeg * Math.PI / 180.0;
                double endRad   = a.EndDeg   * Math.PI / 180.0;

                Curve curve;
                if (Math.Abs(a.EndDeg - a.StartDeg) >= 360.0 - 1e-3)
                {
                    // Full circle
                    curve = Arc.Create(centre, r, 0, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
                }
                else
                {
                    curve = Arc.Create(centre, r, startRad, endRad, XYZ.BasisX, XYZ.BasisY);
                }

                if (fdoc.IsFamilyDocument)
                    fdoc.FamilyCreate.NewDetailCurve(view, curve);
                else
                    fdoc.Create.NewDetailCurve(view, curve);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: arc draw failed — {ex.Message}");
            }
        }

        private static void DrawFilledRegion(Document fdoc, View view,
            FilledRegionDefinition fr, double symMm, SymbolCreationResult result, string id)
        {
            try
            {
                if (fr.Boundary == null || fr.Boundary.Count < 3) return;

                var pts = fr.Boundary.Select(p => new XYZ(Scale(p.X, symMm), Scale(p.Y, symMm), 0)).ToList();

                var curves = new List<Curve>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    if (a.DistanceTo(b) < 1e-6) continue;
                    curves.Add(Line.CreateBound(a, b));
                }
                if (curves.Count < 3) return;

                var loop = CurveLoop.Create(curves);
                ElementId frTypeId = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(FilledRegionType))
                    .FirstElementId();
                if (frTypeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add($"{id}: no FilledRegionType in template.");
                    return;
                }
                FilledRegion.Create(fdoc, frTypeId, view.Id, new List<CurveLoop> { loop });
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: filled region failed — {ex.Message}");
            }
        }

        private static void DrawText(Document fdoc, View view,
            TextDefinition t, double symMm, SymbolCreationResult result, string id)
        {
            if (string.IsNullOrEmpty(t?.Value)) return;
            try
            {
                XYZ origin = new XYZ(Scale(t.X, symMm), Scale(t.Y, symMm), 0);
                ElementId textTypeId = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (textTypeId == ElementId.InvalidElementId)
                {
                    result.Warnings.Add($"{id}: no TextNoteType in template.");
                    return;
                }
                var opts = new TextNoteOptions(textTypeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    Rotation = 0
                };
                TextNote.Create(fdoc, view.Id, origin, t.Value, opts);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{id}: text draw failed — {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Parameters
        // ─────────────────────────────────────────────────────────────────

        // Universal symbol-system parameters — stamped on every family the
        // library creator emits so the runtime can identify symbols, track
        // their host element, find their adjacent label TextNote, and
        // override the resolved standard per-instance. Defined as static
        // text instance params; same names as ParamRegistry.SYMBOL_*.
        private static readonly string[] _universalStamps = new[]
        {
            "STING_SYMBOL_ID",
            "STING_SYMBOL_STANDARD",
            "STING_HOST_ELEMENT_ID",
            "STING_SYMBOL_LABEL_ID",
            "STING_SYMBOL_OVERRIDE",
            "STING_COMPOUND_PARENT_ID",
        };

        private static void AddParameters(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            var fm = fdoc.FamilyManager;

            // Always-on system stamps. These light up overlay placement,
            // SLD label-id fast path, drift detection, and per-instance
            // standard override.
            foreach (var name in _universalStamps)
            {
                try
                {
                    if (fm.get_Parameter(name) != null) continue;
                    fm.AddParameter(name, GroupTypeId.IdentityData,
                        SpecTypeId.String.Text, isInstance: true);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: universal stamp '{name}' add failed — {ex.Message}");
                }
            }

            if (def.Parameters == null || def.Parameters.Count == 0) return;
            foreach (var p in def.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p?.Name)) continue;
                try
                {
                    if (fm.get_Parameter(p.Name) != null) continue; // already exists

                    var groupTypeId = GroupTypeId.IdentityData;
                    var specTypeId  = ResolveSpecTypeId(p.Type);
                    fm.AddParameter(p.Name, groupTypeId, specTypeId, p.IsInstance);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: param '{p.Name}' add failed — {ex.Message}");
                }
            }
        }

        private static ForgeTypeId ResolveSpecTypeId(string type)
        {
            // TODO-VERIFY-API: Spec type IDs in Revit 2025 forge namespace.
            switch ((type ?? "Text").Trim())
            {
                case "Integer": return SpecTypeId.Int.Integer;
                case "Number":  return SpecTypeId.Number;
                case "Length":  return SpecTypeId.Length;
                case "YesNo":   return SpecTypeId.Boolean.YesNo;
                case "Material": return SpecTypeId.Reference.Material;
                case "Text":
                default:        return SpecTypeId.String.Text;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Connectors
        // ─────────────────────────────────────────────────────────────────

        private static void AddConnectors(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            double s = def.SymbolSize > 0 ? def.SymbolSize : 3.0;

            foreach (var c in def.Connectors)
            {
                if (c == null) continue;
                try
                {
                    XYZ origin = new XYZ(Scale(c.OffsetX, s), Scale(c.OffsetY, s), Scale(c.OffsetZ, s));
                    XYZ facing = ParseFacing(c.Facing);

                    Plane plane = Plane.CreateByNormalAndOrigin(facing, origin);
                    SketchPlane sp = SketchPlane.Create(fdoc, plane);

                    Domain domain = ResolveDomain(c.Domain);
                    // TODO-VERIFY-API: Connector creation requires reference geometry in 2025.
                    // We create a small reference line at the connector origin perpendicular
                    // to the facing direction, then mint the connector against its endpoint.
                    XYZ p2 = origin.Add(facing.CrossProduct(XYZ.BasisZ).Normalize().Multiply(MmToFt(10)));
                    if (p2.DistanceTo(origin) < 1e-6)
                        p2 = origin.Add(XYZ.BasisX.Multiply(MmToFt(10)));
                    var refLine = fdoc.FamilyCreate.NewModelCurve(Line.CreateBound(origin, p2), sp);

                    ConnectorElement ce = null;
                    switch (domain)
                    {
                        case Domain.DomainHvac:
                            ce = ConnectorElement.CreateDuctConnector(
                                fdoc,
                                ResolveDuctSystemType(c.SystemType),
                                ResolveProfileType(c.Shape),
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        case Domain.DomainPiping:
                            ce = ConnectorElement.CreatePipeConnector(
                                fdoc,
                                ResolvePipeSystemType(c.SystemType),
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        case Domain.DomainElectrical:
                            ce = ConnectorElement.CreateElectricalConnector(
                                fdoc,
                                ResolveElectricalSystemType(c.SystemType),
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        case Domain.DomainCableTrayConduit:
                            ce = ConnectorElement.CreateConduitConnector(
                                fdoc,
                                refLine.GeometryCurve.GetEndPointReference(0));
                            break;
                        default:
                            result.Warnings.Add($"{def.Id}: unsupported connector domain '{c.Domain}'");
                            break;
                    }

                    if (ce != null)
                    {
                        SetConnectorSize(ce, c);
                        SetConnectorDirection(ce, c.Direction);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{def.Id}: connector ({c.Domain}/{c.SystemType}) failed — {ex.Message}");
                }
            }
        }

        private static void SetConnectorSize(ConnectorElement ce, ConnectorDefinition c)
        {
            try
            {
                if (string.Equals(c.Shape, "Round", StringComparison.OrdinalIgnoreCase))
                {
                    var p = ce.get_Parameter(BuiltInParameter.CONNECTOR_DIAMETER);
                    if (p != null && !p.IsReadOnly && c.SizeMm > 0) p.Set(MmToFt(c.SizeMm));
                }
                else
                {
                    var pw = ce.get_Parameter(BuiltInParameter.CONNECTOR_WIDTH);
                    var ph = ce.get_Parameter(BuiltInParameter.CONNECTOR_HEIGHT);
                    if (pw != null && !pw.IsReadOnly && c.WidthMm  > 0) pw.Set(MmToFt(c.WidthMm));
                    if (ph != null && !ph.IsReadOnly && c.HeightMm > 0) ph.Set(MmToFt(c.HeightMm));
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetConnectorSize: {ex.Message}"); }
        }

        private static void SetConnectorDirection(ConnectorElement ce, string direction)
        {
            // ConnectorElement (family edit) doesn't expose flow direction
            // directly in Revit 2025 — the runtime Connector.Direction is
            // a get-only mirror of the connector's intrinsic type, and
            // BuiltInParameter.CONNECTOR_FLOW_DIRECTION was retired.
            // Symbol families default to Bidirectional which is fine for
            // schematic / tag content; users can override in the family
            // editor when fabrication-grade direction is required.
            if (string.IsNullOrEmpty(direction)
                || direction.Equals("Bidirectional", StringComparison.OrdinalIgnoreCase))
                return;
            StingLog.Info($"SetConnectorDirection: '{direction}' requested but ConnectorElement "
                + "doesn't expose flow direction in 2025 API — left at family default.");
        }

        private static XYZ ParseFacing(string facing)
        {
            switch ((facing ?? "-X").Trim())
            {
                case "+X": return XYZ.BasisX;
                case "-X": return XYZ.BasisX.Negate();
                case "+Y": return XYZ.BasisY;
                case "-Y": return XYZ.BasisY.Negate();
                case "+Z": return XYZ.BasisZ;
                case "-Z": return XYZ.BasisZ.Negate();
                default:   return XYZ.BasisX.Negate();
            }
        }

        private static Domain ResolveDomain(string domain)
        {
            switch ((domain ?? "").Trim())
            {
                case "HVAC":         return Domain.DomainHvac;
                case "Piping":       return Domain.DomainPiping;
                case "Electrical":   return Domain.DomainElectrical;
                case "Conduit":
                case "CableTray":    return Domain.DomainCableTrayConduit;
                default:             return Domain.DomainUndefined;
            }
        }

        // TODO-VERIFY-API: System type names mapped to MEPSystemType / DuctSystemType / PipeSystemType
        // enums in Revit 2025. Default fallbacks chosen for safety.
        private static DuctSystemType ResolveDuctSystemType(string s)
        {
            switch ((s ?? "").Trim())
            {
                case "SupplyAir":  return DuctSystemType.SupplyAir;
                case "ReturnAir":  return DuctSystemType.ReturnAir;
                case "ExhaustAir": return DuctSystemType.ExhaustAir;
                default:           return DuctSystemType.UndefinedSystemType;
            }
        }

        private static PipeSystemType ResolvePipeSystemType(string s)
        {
            switch ((s ?? "").Trim())
            {
                case "DomesticColdWater":   return PipeSystemType.DomesticColdWater;
                case "DomesticHotWater":    return PipeSystemType.DomesticHotWater;
                case "Sanitary":            return PipeSystemType.Sanitary;
                case "FireProtectionWet":   return PipeSystemType.FireProtectWet;
                case "FireProtectionDry":   return PipeSystemType.FireProtectDry;
                case "FireProtectionPreaction": return PipeSystemType.FireProtectPreaction;
                case "ChilledWaterSupply":  return PipeSystemType.SupplyHydronic;
                case "ChilledWaterReturn":  return PipeSystemType.ReturnHydronic;
                case "HotWaterSupply":      return PipeSystemType.SupplyHydronic;
                case "HotWaterReturn":      return PipeSystemType.ReturnHydronic;
                case "Hydronic":            return PipeSystemType.SupplyHydronic;
                default:                    return PipeSystemType.UndefinedSystemType;
            }
        }

        private static ElectricalSystemType ResolveElectricalSystemType(string s)
        {
            // Revit 2025 enum names are short ("Data" not "DataCircuit").
            // PowerBalanced / PowerUnBalanced are the only "Power…" variants;
            // "Power" maps to PowerCircuit which is the generic power flag.
            switch ((s ?? "").Trim())
            {
                case "Power":         return ElectricalSystemType.PowerCircuit;
                case "Lighting":      return ElectricalSystemType.PowerCircuit;
                case "Data":          return ElectricalSystemType.Data;
                case "Telephone":     return ElectricalSystemType.Telephone;
                case "FireAlarm":     return ElectricalSystemType.FireAlarm;
                case "Security":      return ElectricalSystemType.Security;
                case "NurseCall":     return ElectricalSystemType.NurseCall;
                case "Communication": return ElectricalSystemType.Communication;
                default:              return ElectricalSystemType.UndefinedSystemType;
            }
        }

        private static ConnectorProfileType ResolveProfileType(string shape)
        {
            switch ((shape ?? "Round").Trim())
            {
                case "Round":       return ConnectorProfileType.Round;
                case "Rectangular": return ConnectorProfileType.Rectangular;
                case "Oval":        return ConnectorProfileType.Oval;
                default:            return ConnectorProfileType.Round;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3D solid (best-effort placeholder mass)
        // ─────────────────────────────────────────────────────────────────

        private static void AddSolid3D(Document fdoc, SymbolDefinition def, SymbolCreationResult result)
        {
            if (!fdoc.IsFamilyDocument) return;
            var s3 = def.Solid3D;
            if (s3 == null) return;

            try
            {
                double w = s3.WidthMm  > 0 ? s3.WidthMm  : (s3.DiameterMm > 0 ? s3.DiameterMm : 200);
                double d = s3.DepthMm  > 0 ? s3.DepthMm  : (s3.DiameterMm > 0 ? s3.DiameterMm : 200);
                double h = s3.HeightMm > 0 ? s3.HeightMm : 100;

                CurveArray profile = new CurveArray();
                if (string.Equals(s3.Type, "Cylinder", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s3.Type, "Revolution", StringComparison.OrdinalIgnoreCase))
                {
                    double r = MmToFt((s3.DiameterMm > 0 ? s3.DiameterMm : Math.Max(w, d)) * 0.5);
                    profile.Append(Arc.Create(XYZ.Zero, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                    profile.Append(Arc.Create(XYZ.Zero, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                }
                else
                {
                    double hw = MmToFt(w * 0.5);
                    double hd = MmToFt(d * 0.5);
                    profile.Append(Line.CreateBound(new XYZ(-hw, -hd, 0), new XYZ( hw, -hd, 0)));
                    profile.Append(Line.CreateBound(new XYZ( hw, -hd, 0), new XYZ( hw,  hd, 0)));
                    profile.Append(Line.CreateBound(new XYZ( hw,  hd, 0), new XYZ(-hw,  hd, 0)));
                    profile.Append(Line.CreateBound(new XYZ(-hw,  hd, 0), new XYZ(-hw, -hd, 0)));
                }

                CurveArrArray prof = new CurveArrArray();
                prof.Append(profile);

                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                SketchPlane sp = SketchPlane.Create(fdoc, plane);
                // TODO-VERIFY-API: NewExtrusion signature in 2025.
                fdoc.FamilyCreate.NewExtrusion(true, prof, sp, MmToFt(h));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{def.Id}: 3D solid skipped — {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Family load
        // ─────────────────────────────────────────────────────────────────

        private static void TryLoadFamily(Document hostDoc, string rfaPath, SymbolCreationResult result)
        {
            try
            {
                using (var tx = new Transaction(hostDoc, "STING Load Symbol Family"))
                {
                    tx.Start();
                    Family fam;
                    bool loaded = hostDoc.LoadFamily(rfaPath, new FamilyLoadOpts(), out fam);
                    tx.Commit();
                    if (!loaded) result.Warnings.Add($"LoadFamily returned false for {Path.GetFileName(rfaPath)}");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"LoadFamily {Path.GetFileName(rfaPath)} failed — {ex.Message}");
            }
        }

        private sealed class FamilyLoadOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Template resolution
        // ─────────────────────────────────────────────────────────────────

        public static string ResolveTemplateFolder(Application app)
        {
            try
            {
                if (app != null)
                {
                    string p = app.FamilyTemplatePath;
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTemplateFolder: {ex.Message}"); }

            string[] fallbacks =
            {
                @"C:\ProgramData\Autodesk\RVT 2027\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2026\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates\English-Imperial",
            };
            foreach (var f in fallbacks)
                if (Directory.Exists(f)) return f;
            return null;
        }

        private static string ResolveTemplateFile(SymbolDefinition def, string folder, SymbolCreationResult result)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            string[] candidates = CandidateTemplateNames(def);
            foreach (var name in candidates)
            {
                try
                {
                    var hits = Directory.GetFiles(folder, name, SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
                catch (Exception ex) { StingLog.Warn($"ResolveTemplateFile {name}: {ex.Message}"); }
            }
            return null;
        }

        private static string[] CandidateTemplateNames(SymbolDefinition def)
        {
            string ft = (def.FamilyType ?? "").Trim();
            string disc = (def.Discipline ?? "").Trim();

            if (string.Equals(ft, "GenericAnnotation", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "Generic Annotation.rft", "Metric Generic Annotation.rft" };
            }
            if (string.Equals(ft, "MEPAccessory", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(disc, "Mechanical", StringComparison.OrdinalIgnoreCase)
                    && def.Connectors != null && def.Connectors.Any(c => string.Equals(c?.Domain, "HVAC", StringComparison.OrdinalIgnoreCase)))
                    return new[] { "Metric Duct Accessory.rft", "Duct Accessory.rft" };
                return new[] { "Metric Pipe Accessory.rft", "Pipe Accessory.rft" };
            }
            if (string.Equals(ft, "MEPEquipment", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(disc, "Electrical", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(disc, "Lighting",   StringComparison.OrdinalIgnoreCase))
                    return new[] { "Metric Electrical Fixture.rft", "Electrical Fixture.rft",
                                   "Metric Lighting Fixture.rft", "Lighting Fixture.rft",
                                   "Metric Generic Model.rft", "Generic Model.rft" };
                if (string.Equals(disc, "Plumbing", StringComparison.OrdinalIgnoreCase))
                    return new[] { "Metric Plumbing Fixture.rft", "Plumbing Fixture.rft",
                                   "Metric Generic Model.rft" };
                if (string.Equals(disc, "FireProtection", StringComparison.OrdinalIgnoreCase))
                    return new[] { "Metric Fire Alarm Device.rft",
                                   "Metric Sprinkler.rft",
                                   "Metric Pipe Accessory.rft",
                                   "Metric Generic Model.rft" };
                return new[] { "Metric Mechanical Equipment.rft", "Mechanical Equipment.rft",
                               "Metric Generic Model.rft" };
            }
            return new[] { "Metric Generic Model.rft", "Generic Model.rft" };
        }

        private static View ResolvePlanView(Document fdoc)
        {
            try
            {
                var views = new FilteredElementCollector(fdoc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .ToList();
                return views.FirstOrDefault(v => v.ViewType == ViewType.FloorPlan)
                    ?? views.FirstOrDefault(v => v.ViewType == ViewType.CeilingPlan)
                    ?? views.FirstOrDefault(v => v.ViewType == ViewType.DraftingView)
                    ?? views.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolvePlanView: {ex.Message}");
                return null;
            }
        }
    }
}
