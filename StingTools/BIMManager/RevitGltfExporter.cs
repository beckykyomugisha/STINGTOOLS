#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;   // Phase 2 — appearance assets (Asset / AssetProperty*)
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Newtonsoft.Json;

namespace StingTools.BIMManager
{
    /// <summary>
    /// Revit-native glTF 2.0 / GLB exporter built on <see cref="CustomExporter"/>.
    /// Walks a 3D view, flattens instance transforms, and writes a single .glb
    /// with one node per element, interleaved POSITION+NORMAL vertex buffers,
    /// 16-bit indices, and per-element extras carrying the Revit UniqueId.
    ///
    /// Element materials are simplified to a single PBR base colour derived from
    /// the element category's projection line colour. The output is intentionally
    /// minimal — clients (viewer.html, three.js) read element metadata from the
    /// element-map sidecar produced separately by <see cref="PublishModelCommand"/>.
    /// </summary>
    public class RevitGltfExporter : IExportContext
    {
        private readonly Document _doc;
        private readonly List<MeshNode> _nodes = new();
        private readonly Stack<Transform> _xformStack = new();

        private MeshNode? _current;
        private string? _currentUniqueId;
        private string? _currentName;
        private string? _currentCategory;
        private int[]? _currentRgb;

        // Phase 2 — real material textures. OFF by default: lean coordination /
        // low-bandwidth exports stay flat-colour (unchanged). ON for presentation /
        // as-built. Set via the static toggle or the Export() parameter
        // ("PlanscapeExportTextures" export option).
        public static bool ExportTextures { get; set; } = false;
        private readonly bool _exportTextures;
        // Per-material appearance cache (Revit material ElementId.Value → resolved def),
        // so the version-sensitive appearance read runs once per material, not per face.
        private readonly Dictionary<long, MaterialDef?> _appearanceCache = new();
        // Phase 2 — resolved texture-path cache (by lowercased filename) so the library
        // filesystem scan runs at most once per filename per export session.
        private static readonly Dictionary<string, string?> _texPathCache = new();

        private const double FeetToMm = 304.8;

        public RevitGltfExporter(Document doc, bool exportTextures = false)
        {
            _doc = doc;
            _exportTextures = exportTextures;
            _xformStack.Push(Transform.Identity);
        }

        public static ExportResult Export(Document doc, View3D view, string outputGlbPath, bool? exportTextures = null)
        {
            bool textures = exportTextures ?? ExportTextures;
            // S8.2.2 — span around the whole export so telemetry-on users see
            // p99 export latency vs scene size in their dashboards.
            return StingTools.Core.PluginTelemetry.Run(
                "RevitGltfExporter.export",
                () =>
                {
                    var ctx = new RevitGltfExporter(doc, textures);
                    var exporter = new CustomExporter(doc, ctx)
                    {
                        IncludeGeometricObjects = false,
                        ShouldStopOnError = false,
                        Export2DIncludingAnnotationObjects = false,
                        Export2DGeometricObjectsIncludingPatternLines = false,
                    };
                    exporter.Export(view);
                    var result = ctx.WriteGlb(outputGlbPath);
                    // Gap J — write coordinate sidecar alongside the GLB so the
                    // server can populate IfcAlignmentReport without re-parsing IFC.
                    ExportCoordinateSidecar(doc, outputGlbPath);
                    return result;
                },
                extras: new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["view"] = view.Name,
                    ["docTitle"] = doc.Title,
                });
        }

        // ── IExportContext ─────────────────────────────────────────────

        public bool Start() { return true; }
        public void Finish() { }
        public bool IsCanceled() => false;
        public RenderNodeAction OnViewBegin(ViewNode n) { n.LevelOfDetail = 4; return RenderNodeAction.Proceed; }
        public void OnViewEnd(ElementId id) { }

        public RenderNodeAction OnElementBegin(ElementId id)
        {
            var el = _doc.GetElement(id);
            if (el == null) return RenderNodeAction.Skip;
            _currentUniqueId = el.UniqueId;
            _currentName = el.Name ?? "";
            _currentCategory = el.Category?.Name ?? "";
            _currentRgb = ResolveCategoryColour(el);
            _current = new MeshNode
            {
                UniqueId = _currentUniqueId,
                Name = string.IsNullOrEmpty(_currentName) ? id.Value.ToString() : _currentName!,
                Category = _currentCategory ?? "",
                Rgb = _currentRgb,
            };
            return RenderNodeAction.Proceed;
        }

        public void OnElementEnd(ElementId id)
        {
            if (_current != null && _current.Positions.Count > 0) _nodes.Add(_current);
            _current = null;
            _currentUniqueId = null;
            _currentName = null;
            _currentRgb = null;
        }

        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            var t = _xformStack.Peek().Multiply(node.GetTransform());
            _xformStack.Push(t);
            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node)
        {
            if (_xformStack.Count > 1) _xformStack.Pop();
        }

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            var t = _xformStack.Peek().Multiply(node.GetTransform());
            _xformStack.Push(t);
            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node)
        {
            if (_xformStack.Count > 1) _xformStack.Pop();
        }

        public void OnRPC(RPCNode node) { }
        public void OnLight(LightNode node) { }
        // Revit 2025 dropped DaylightPortalNode / OnDaylightPortal and
        // OnFaceBegin1 from IExportContext (obsolete since 2022). Don't
        // declare them — see Clash/ClashExportContext.cs for the same
        // adjustment in another working impl.
        public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;
        public void OnFaceEnd(FaceNode node) { }
        private MaterialDef? _activeMat;   // Phase 2 — current face's resolved material (UV transform for OnPolymesh)

        public void OnMaterial(MaterialNode node)
        {
            try
            {
                var c = node.Color;
                if (c != null && _current != null)
                    _current.Rgb = new[] { (int)c.Red, (int)c.Green, (int)c.Blue };

                // Phase 2 — resolve the real appearance (diffuse bitmap + PBR factors) once
                // per material. Whole-element single-material model (last face wins), matching
                // the existing one-primitive-per-element design. Failure → null → flat colour.
                if (_exportTextures)
                {
                    var def = ResolveAppearance(node);
                    _activeMat = def;
                    if (def != null && _current != null) _current.Mat = def;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        // Phase 2 (hardened + self-diagnosing) — Material → AppearanceAsset → rendering asset
        // → diffuse bitmap + PBR. Records diag fields on the MaterialDef (logged at export end
        // by LogTextureDiagnostics) so a re-publish shows, per material, whether a bitmap was
        // found + whether its path resolved + why any was skipped. Never throws (failure → a
        // colour-only def). TODO-VERIFY-API: Visual.Asset schema is version-sensitive (2025/26/27).
        private MaterialDef? ResolveAppearance(MaterialNode node)
        {
            ElementId matId;
            try { matId = node.MaterialId; } catch { return null; }
            if (matId == null || matId == ElementId.InvalidElementId) return null;
            long key = matId.Value;
            if (_appearanceCache.TryGetValue(key, out var cached)) return cached;

            var def = new MaterialDef();
            try
            {
                var mat = _doc.GetElement(matId) as Material;
                def.MatName = mat?.Name ?? ("material " + key);
                try { var c = node.Color; if (c != null) def.DiffuseRgb = new[] { (int)c.Red, (int)c.Green, (int)c.Blue }; } catch { }
                try { def.Alpha = 1.0 - Clamp01(node.Transparency); } catch { }

                var assetElem = mat != null ? _doc.GetElement(mat.AppearanceAssetId) as AppearanceAssetElement : null;
                var asset = assetElem?.GetRenderingAsset();
                def.HadAsset = asset != null;
                if (asset == null) { def.Reason = "no-appearance-asset"; def.ComputeKey(); _appearanceCache[key] = def; return def; }

                var dc = ReadColor(asset, "generic_diffuse") ?? ReadColor(asset, "diffuse");
                if (dc != null) def.DiffuseRgb = dc;

                // Robust diffuse bitmap — walk the connected-asset graph, preferring
                // diffuse/colour/albedo branches, falling back to any UnifiedBitmap.
                var bmpAsset = FindBitmapAsset(asset, true);
                if (bmpAsset == null) { def.Reason = "no-bitmap"; }
                else
                {
                    var raw = GetBitmapRawPath(bmpAsset);
                    def.RawPath = raw ?? "";
                    def.BitmapFound = !string.IsNullOrWhiteSpace(raw);
                    if (!def.BitmapFound) def.Reason = "bitmap-prop-empty";
                    else
                    {
                        var resolved = ResolveTexturePath(raw!, def);
                        if (resolved != null) { def.DiffuseTexPath = resolved; ReadTextureTransform(bmpAsset, def); def.Reason = "ok"; }
                        else def.Reason = "path-missing";
                    }
                }

                // Optional normal/bump map.
                var bump = FindNamedBitmap(asset, new[] { "generic_bump_map", "bumpmap_Bitmap", "bump" });
                if (bump != null) { var bp = GetBitmapRawPath(bump); var br = string.IsNullOrWhiteSpace(bp) ? null : ResolveTexturePath(bp!, null); if (br != null) def.NormalTexPath = br; }

                // PBR factors.
                var gloss = ReadDouble(asset, "generic_glossiness"); if (gloss.HasValue) def.Roughness = 1.0 - Clamp01(gloss.Value);
                var metal = ReadDouble(asset, "generic_is_metal");   if (metal.HasValue) def.Metallic = metal.Value > 0.5 ? 1.0 : 0.0;
                var tr = ReadDouble(asset, "generic_transparency");  if (tr.HasValue) def.Alpha = 1.0 - Clamp01(tr.Value);
                def.ComputeKey();
            }
            catch (Exception ex) { def.Reason = "exception: " + ex.Message; StingLog.Warn($"[tex] appearance resolve failed for {def.MatName}: {ex.Message}"); }

            _appearanceCache[key] = def;
            return def;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        // Read a colour AssetProperty (AssetPropertyDoubleArray4d) → 0..255 rgb.
        private static int[]? ReadColor(Asset asset, string name)
        {
            try
            {
                var p = asset.FindByName(name);
                if (p is AssetPropertyDoubleArray4d col)
                {
                    var v = col.GetValueAsDoubles();
                    if (v != null && v.Count >= 3)
                        return new[] { Clamp255(v[0]), Clamp255(v[1]), Clamp255(v[2]) };
                }
            }
            catch { }
            return null;
        }
        private static int Clamp255(double d) { var i = (int)Math.Round(d * 255.0); return i < 0 ? 0 : (i > 255 ? 255 : i); }

        // Read a scalar AssetProperty across the types Revit uses (double / float / distance /
        // integer / boolean). texture_RealWorldScaleX etc. are AssetPropertyDistance.
        private static double? ReadDouble(Asset asset, string name)
        {
            try
            {
                var p = asset.FindByName(name);
                if (p is AssetPropertyDouble d) return d.Value;
                if (p is AssetPropertyFloat f) return f.Value;
                if (p is AssetPropertyDistance dist) return dist.Value;
                if (p is AssetPropertyInteger i) return i.Value;
                if (p is AssetPropertyBoolean b) return b.Value ? 1.0 : 0.0;
            }
            catch { }
            return null;
        }

        // ── robust bitmap discovery (recursive across connected assets) ────────────
        // Prefer a UnifiedBitmap reached via a diffuse/colour-named property; else any bitmap.
        private static Asset? FindBitmapAsset(Asset root, bool preferDiffuse)
        {
            if (preferDiffuse)
            {
                var hit = FindNamedBitmap(root, new[] { "generic_diffuse", "diffuse", "color_map", "surface_albedo", "base_color", "albedo" });
                if (hit != null) return hit;
            }
            return WalkForBitmap(root, 0);
        }
        // Bitmap connected under any of the named properties (recursing into the connected asset).
        private static Asset? FindNamedBitmap(Asset root, string[] names)
        {
            foreach (var nm in names)
            {
                try
                {
                    var p = root.FindByName(nm);
                    if (p == null) continue;
                    for (int i = 0; i < p.NumberOfConnectedProperties; i++)
                    {
                        if (p.GetConnectedProperty(i) is Asset c)
                        {
                            var b = WalkForBitmap(c, 0);
                            if (b != null) return b;
                        }
                    }
                }
                catch { }
            }
            return null;
        }
        // DFS for an Asset carrying a non-empty unifiedbitmap_Bitmap, across nested connected assets.
        private static Asset? WalkForBitmap(Asset? a, int depth)
        {
            if (a == null || depth > 6) return null;
            try
            {
                if (a.FindByName("unifiedbitmap_Bitmap") is AssetPropertyString bs && !string.IsNullOrWhiteSpace(bs.Value)) return a;
                for (int i = 0; i < a.Size; i++)
                {
                    var p = a.Get(i);
                    if (p == null) continue;
                    for (int j = 0; j < p.NumberOfConnectedProperties; j++)
                    {
                        if (p.GetConnectedProperty(j) is Asset c)
                        {
                            var r = WalkForBitmap(c, depth + 1);
                            if (r != null) return r;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
        private static string? GetBitmapRawPath(Asset bitmapAsset)
        {
            try { return (bitmapAsset.FindByName("unifiedbitmap_Bitmap") as AssetPropertyString)?.Value; }
            catch { return null; }
        }
        private static void ReadTextureTransform(Asset bmp, MaterialDef def)
        {
            def.UvScaleU = ReadDouble(bmp, "texture_RealWorldScaleX") ?? def.UvScaleU;
            def.UvScaleV = ReadDouble(bmp, "texture_RealWorldScaleY") ?? def.UvScaleV;
            def.UvOffU   = ReadDouble(bmp, "texture_UOffset") ?? def.UvOffU;
            def.UvOffV   = ReadDouble(bmp, "texture_VOffset") ?? def.UvOffV;
            def.UvAngle  = ReadDouble(bmp, "texture_WAngle") ?? def.UvAngle;
        }

        // ── path resolution (absolute → library dirs by filename, cached) ──────────
        // Revit stores absolute paths OR library tokens/relatives. Try each '|'-separated
        // candidate absolute; else search the material/texture library dirs by filename.
        private string? ResolveTexturePath(string raw, MaterialDef? def)
        {
            foreach (var cand0 in raw.Split('|'))
            {
                var cand = cand0.Trim();
                if (cand.Length == 0) continue;
                if (Path.IsPathRooted(cand) && File.Exists(cand)) return cand;
                var fn = Path.GetFileName(cand);
                if (string.IsNullOrEmpty(fn)) continue;
                var fkey = fn.ToLowerInvariant();
                if (_texPathCache.TryGetValue(fkey, out var hit)) { if (hit != null) return hit; continue; }
                var found = SearchLibraries(fn);
                _texPathCache[fkey] = found;
                if (found != null) return found;
            }
            if (def != null) def.AttemptedDirs = string.Join(";", TextureLibraryDirs());
            return null;
        }
        private string? SearchLibraries(string filename)
        {
            foreach (var dir in TextureLibraryDirs())
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var direct = Path.Combine(dir, filename);
                    if (File.Exists(direct)) return direct;
                    foreach (var f in Directory.EnumerateFiles(dir, filename, SearchOption.AllDirectories)) return f;
                }
                catch { /* permission / long-path — skip dir */ }
            }
            return null;
        }
        private IEnumerable<string> TextureLibraryDirs()
        {
            var dirs = new List<string>();
            try { var d = Path.GetDirectoryName(_doc.PathName); if (!string.IsNullOrEmpty(d)) dirs.Add(d!); } catch { }
            var cf86 = Environment.GetEnvironmentVariable("CommonProgramFiles(x86)") ?? @"C:\Program Files (x86)\Common Files";
            var cf   = Environment.GetEnvironmentVariable("CommonProgramFiles") ?? @"C:\Program Files\Common Files";
            dirs.Add(Path.Combine(cf86, "Autodesk Shared", "Materials", "Textures"));
            dirs.Add(Path.Combine(cf86, "Autodesk Shared", "Materials"));
            dirs.Add(Path.Combine(cf, "Autodesk Shared", "Materials", "Textures"));
            // Project-supplied / library overrides via env (';'-separated).
            foreach (var ev in new[] { "ADSK_MATERIAL_LIBRARY", "PLANSCAPE_TEXTURE_DIRS" })
            {
                var v = Environment.GetEnvironmentVariable(ev);
                if (!string.IsNullOrEmpty(v)) dirs.AddRange(v!.Split(';'));
            }
            return dirs.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct();
        }

        // Downscale a PNG/JPEG to maxDim (longest side) via System.Drawing; returns the
        // original bytes on no-resize-needed or any failure. Keeps the source codec.
        private static byte[] DownscaleImage(byte[] data, int maxDim, string ext)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var img = System.Drawing.Image.FromStream(ms);
                if (img.Width <= maxDim && img.Height <= maxDim) return data;
                double s = (double)maxDim / Math.Max(img.Width, img.Height);
                int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
                using var bmp = new System.Drawing.Bitmap(w, h);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, w, h);
                }
                using var outMs = new MemoryStream();
                var fmt = ext == ".png" ? System.Drawing.Imaging.ImageFormat.Png : System.Drawing.Imaging.ImageFormat.Jpeg;
                bmp.Save(outMs, fmt);
                var outBytes = outMs.ToArray();
                return outBytes.Length > 0 ? outBytes : data;
            }
            catch { return data; }
        }

        public void OnPolymesh(PolymeshTopology poly)
        {
            if (_current == null) return;
            var t = _xformStack.Peek();
            var pts = poly.GetPoints();
            var nrm = poly.GetNormals();
            var facets = poly.GetFacets();
            var distrib = poly.DistributionOfNormals;

            int baseIdx = _current.Positions.Count / 3;

            // Phase 2 — per-vertex UVs (TEXCOORD_0). Padded with (0,0) when a polymesh has
            // none so UVs stay aligned 1:1 with positions across multi-polymesh elements.
            // TODO-VERIFY-API: GetUVs()/NumberOfUVs return raw texture coords for textured
            // materials only; real-world scale/offset/rotation is applied via
            // KHR_texture_transform at material build (verify units in Revit).
            IList<UV>? uvs = null;
            if (_exportTextures)
            {
                try { if (poly.NumberOfUVs > 0) uvs = poly.GetUVs(); } catch { }
            }

            for (int i = 0; i < pts.Count; i++)
            {
                var w = t.OfPoint(pts[i]);
                _current.Positions.Add((float)(w.X * FeetToMm));
                _current.Positions.Add((float)(w.Y * FeetToMm));
                _current.Positions.Add((float)(w.Z * FeetToMm));

                XYZ n;
                if (distrib == DistributionOfNormals.AtEachPoint && i < nrm.Count)
                    n = t.OfVector(nrm[i]);
                else if (distrib == DistributionOfNormals.OnePerFace && nrm.Count > 0)
                    n = t.OfVector(nrm[0]);
                else
                    n = XYZ.BasisZ;
                var nl = n.GetLength();
                if (nl < 1e-9) n = XYZ.BasisZ; else n = n.Divide(nl);
                _current.Normals.Add((float)n.X);
                _current.Normals.Add((float)n.Y);
                _current.Normals.Add((float)n.Z);

                if (_exportTextures)
                {
                    if (uvs != null && i < uvs.Count) { _current.UVs.Add((float)uvs[i].U); _current.UVs.Add((float)uvs[i].V); }
                    else { _current.UVs.Add(0f); _current.UVs.Add(0f); }
                }
            }

            for (int f = 0; f < facets.Count; f++)
            {
                var tri = facets[f];
                _current.Indices.Add((uint)(baseIdx + tri.V1));
                _current.Indices.Add((uint)(baseIdx + tri.V2));
                _current.Indices.Add((uint)(baseIdx + tri.V3));
            }
        }

        // ── Gap J — Coordinate sidecar ─────────────────────────────────

        /// <summary>
        /// Gap J — Writes a JSON sidecar alongside the GLB with Revit coordinate metadata.
        /// The server reads this on upload to populate IfcAlignmentReport without re-parsing IFC.
        /// Output path: same as the GLB but with extension changed to .coord.json
        /// </summary>
        private static void ExportCoordinateSidecar(Document doc, string glbOutputPath)
        {
            try
            {
                var sidecarPath = Path.ChangeExtension(glbOutputPath, ".coord.json");

                // Project Base Point and Survey Point
                // TODO-VERIFY-API: FilteredElementCollector for BasePoint — confirmed available in Revit 2025 API.
                var pbpCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(BasePoint))
                    .Cast<BasePoint>()
                    .ToList();

                var projectBasePoint = pbpCollector.FirstOrDefault(bp => !bp.IsShared);
                var surveyPoint      = pbpCollector.FirstOrDefault(bp => bp.IsShared);

                // Project location (lat/lon/north angle)
                ProjectLocation? projectLocation = null;
                try { projectLocation = doc.ActiveProjectLocation; } catch { /* ignore */ }

                double? latitude = null, longitude = null, elevation = null, northAngleDeg = null;
                double? easting = null, northing = null;

                if (projectLocation != null)
                {
                    try
                    {
                        var pos = projectLocation.GetProjectPosition(XYZ.Zero);
                        // Latitude/Longitude moved to SiteLocation in Revit 2025+ API.
                        var site = doc.SiteLocation;
                        if (site != null)
                        {
                            latitude  = site.Latitude  * (180.0 / Math.PI);
                            longitude = site.Longitude * (180.0 / Math.PI);
                        }
                        northAngleDeg = pos.Angle     * (180.0 / Math.PI);
                        elevation     = pos.Elevation * FeetToMm;
                    }
                    catch { /* not all configurations carry site data */ }
                }

                // Survey point position via its coordinate system (feet → mm)
                // NOTE: BasePoint.GetCoordinateSystem() is not available in Revit 2025 public API.
                // Easting/northing will remain null until a supported API is identified.
                // if (surveyPoint != null) { ... surveyPoint.GetCoordinateSystem() ... }

                // Project base point coordinate system (feet → mm)
                double? pbpX = null, pbpY = null, pbpZ = null;
                // NOTE: BasePoint.GetCoordinateSystem() is not available in Revit 2025 public API.
                // pbpX/pbpY/pbpZ will remain null until a supported API is identified.

                var sidecar = new
                {
                    schemaVersion = "1.0",
                    generatedBy = "StingTools.RevitGltfExporter",
                    generatedAt = DateTime.UtcNow.ToString("O"),
                    exportMode = "ProjectInternal",  // TODO: detect shared coordinate export
                    projectBasePoint = (pbpX.HasValue) ? new
                    {
                        x    = pbpX.Value,
                        y    = pbpY!.Value,
                        z    = pbpZ!.Value,
                        unit = "mm",
                    } : (object?)null,
                    surveyPoint = (easting.HasValue && northing.HasValue) ? new
                    {
                        easting  = easting.Value,
                        northing = northing.Value,
                        unit     = "mm",
                    } : (object?)null,
                    geolocation = latitude.HasValue ? new
                    {
                        latitude          = latitude.Value,
                        longitude         = longitude!.Value,
                        elevation         = elevation!.Value,
                        trueNorthAngleDeg = northAngleDeg!.Value,
                    } : (object?)null,
                    lengthUnit = "mm",
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(sidecar, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(sidecarPath, json);
                StingLog.Info($"[RevitGltfExporter] Coordinate sidecar written: {sidecarPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"[RevitGltfExporter] Failed to write coordinate sidecar: {ex.Message}");
            }
        }

        // ── GLB writer ─────────────────────────────────────────────────

        private ExportResult WriteGlb(string path)
        {
            using var bin = new MemoryStream();
            using var binWriter = new BinaryWriter(bin);

            var meshes = new JArray();
            var nodes = new JArray();
            var accessors = new JArray();
            var bufferViews = new JArray();
            var materials = new JArray();
            var matKeyToIndex = new Dictionary<string, int>();
            // Phase 2 — texture graph (only populated when ExportTextures is on).
            var images = new JArray();
            var samplers = new JArray();
            var textures = new JArray();
            var imgCache = new Dictionary<string, int>();    // file path → images[] index (-1 = skipped)
            var texCache = new Dictionary<int, int>();       // image index → textures[] index
            var texMatCache = new Dictionary<string, int>(); // MaterialDef.Key → materials[] index
            int samplerIdx = -1;
            bool usedKhrTransform = false;

            static string MimeFor(string p)
            {
                var e = Path.GetExtension(p).ToLowerInvariant();
                return e is ".jpg" or ".jpeg" ? "image/jpeg" : ".png".Equals(e) ? "image/png" : "";
            }
            // Embed an image's bytes into the GLB buffer (dedup by path). glTF only renders
            // PNG/JPEG in-browser, so other formats (.bmp/.tif) are skipped (TODO: convert).
            int EmbedImage(string p)
            {
                if (imgCache.TryGetValue(p, out var ix)) return ix;
                var mime = MimeFor(p);
                if (mime.Length == 0) { StingLog.Warn($"[gltf] unsupported texture format, skipped: {p}"); imgCache[p] = -1; return -1; }
                byte[] bytes;
                try { bytes = File.ReadAllBytes(p); } catch { imgCache[p] = -1; return -1; }
                // Downscale > ~2k (longest side) via System.Drawing so the GLB stays lean;
                // dedup by path keeps repeats free. Final byte cap is a last-resort guard.
                bytes = DownscaleImage(bytes, 2048, Path.GetExtension(p).ToLowerInvariant());
                if (bytes.Length > 8 * 1024 * 1024) { StingLog.Warn($"[tex] texture still too large after downscale ({bytes.Length}B), skipped: {p}"); imgCache[p] = -1; return -1; }
                Pad4(bin, binWriter);
                int off = (int)bin.Position;
                binWriter.Write(bytes);
                int len = (int)bin.Position - off;
                Pad4(bin, binWriter);
                int bvi = bufferViews.Count;
                bufferViews.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = off, ["byteLength"] = len });
                int imi = images.Count;
                images.Add(new JObject { ["bufferView"] = bvi, ["mimeType"] = mime });
                imgCache[p] = imi;
                return imi;
            }
            int EnsureSampler()
            {
                if (samplerIdx < 0) { samplerIdx = samplers.Count; samplers.Add(new JObject { ["wrapS"] = 10497, ["wrapT"] = 10497 }); }
                return samplerIdx;
            }
            int EnsureTexture(int imageIndex)
            {
                if (texCache.TryGetValue(imageIndex, out var ti)) return ti;
                ti = textures.Count;
                textures.Add(new JObject { ["source"] = imageIndex, ["sampler"] = EnsureSampler() });
                texCache[imageIndex] = ti;
                return ti;
            }
            int ResolveTexturedMaterial(MaterialDef def)
            {
                if (texMatCache.TryGetValue(def.Key, out var mi)) return mi;
                int baseImg = string.IsNullOrEmpty(def.DiffuseTexPath) ? -1 : EmbedImage(def.DiffuseTexPath!);
                var pbr = new JObject { ["metallicFactor"] = def.Metallic, ["roughnessFactor"] = def.Roughness };
                if (baseImg >= 0)
                {
                    var bct = new JObject { ["index"] = EnsureTexture(baseImg) };
                    var tt = new JObject();
                    if (def.UvScaleU != 0 && def.UvScaleV != 0) tt["scale"] = new JArray { 1.0 / def.UvScaleU, 1.0 / def.UvScaleV };
                    if (def.UvOffU != 0 || def.UvOffV != 0) tt["offset"] = new JArray { def.UvOffU, def.UvOffV };
                    if (def.UvAngle != 0) tt["rotation"] = -def.UvAngle * Math.PI / 180.0;   // deg→rad (sign TODO-VERIFY)
                    if (tt.Count > 0) { bct["extensions"] = new JObject { ["KHR_texture_transform"] = tt }; usedKhrTransform = true; }
                    pbr["baseColorTexture"] = bct;
                    // Textured + translucent: white base factor carries the alpha.
                    if (def.Alpha < 0.999) pbr["baseColorFactor"] = new JArray { 1.0, 1.0, 1.0, def.Alpha };
                }
                else
                {
                    var rgb = def.DiffuseRgb ?? new[] { 200, 200, 200 };
                    pbr["baseColorFactor"] = new JArray { rgb[0] / 255.0, rgb[1] / 255.0, rgb[2] / 255.0, def.Alpha };
                }
                var m = new JObject { ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = true };
                if (!string.IsNullOrEmpty(def.NormalTexPath))
                {
                    int nimg = EmbedImage(def.NormalTexPath!);
                    if (nimg >= 0) m["normalTexture"] = new JObject { ["index"] = EnsureTexture(nimg) };
                }
                if (def.Alpha < 0.999) m["alphaMode"] = "BLEND";
                int idx = materials.Count; materials.Add(m); texMatCache[def.Key] = idx; return idx;
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            int totalElements = 0;
            int uvlessTextured = 0;   // textured material but mesh had no UVs (texture skipped)

            for (int n = 0; n < _nodes.Count; n++)
            {
                var node = _nodes[n];
                if (node.Positions.Count == 0 || node.Indices.Count == 0) continue;
                totalElements++;

                // Phase 2 — textured iff appearance resolved a usable diffuse bitmap AND we
                // captured a full set of UVs aligned to the vertices.
                bool textured = _exportTextures && node.Mat != null && !string.IsNullOrEmpty(node.Mat.DiffuseTexPath)
                                && node.UVs.Count == (node.Positions.Count / 3) * 2;
                if (_exportTextures && !textured && node.Mat != null && !string.IsNullOrEmpty(node.Mat.DiffuseTexPath))
                {
                    uvlessTextured++;
                    if (uvlessTextured <= 12) StingLog.Info($"[tex] '{node.Name}' has a textured material but no UVs — texture skipped (mesh carries no texture coords)");
                }
                int matIdx = textured ? ResolveTexturedMaterial(node.Mat!)
                                      : ResolveMaterial(materials, matKeyToIndex, node.Rgb);

                int idxByteOffset = (int)bin.Position;
                foreach (var i in node.Indices) binWriter.Write(i);
                int idxBytes = (int)bin.Position - idxByteOffset;
                Pad4(bin, binWriter);

                int posByteOffset = (int)bin.Position;
                float pMinX = float.MaxValue, pMinY = float.MaxValue, pMinZ = float.MaxValue;
                float pMaxX = float.MinValue, pMaxY = float.MinValue, pMaxZ = float.MinValue;
                for (int i = 0; i < node.Positions.Count; i += 3)
                {
                    float x = node.Positions[i], y = node.Positions[i + 1], z = node.Positions[i + 2];
                    binWriter.Write(x); binWriter.Write(y); binWriter.Write(z);
                    if (x < pMinX) pMinX = x; if (y < pMinY) pMinY = y; if (z < pMinZ) pMinZ = z;
                    if (x > pMaxX) pMaxX = x; if (y > pMaxY) pMaxY = y; if (z > pMaxZ) pMaxZ = z;
                }
                int posBytes = (int)bin.Position - posByteOffset;
                Pad4(bin, binWriter);

                if (pMinX < minX) minX = pMinX; if (pMinY < minY) minY = pMinY; if (pMinZ < minZ) minZ = pMinZ;
                if (pMaxX > maxX) maxX = pMaxX; if (pMaxY > maxY) maxY = pMaxY; if (pMaxZ > maxZ) maxZ = pMaxZ;

                int normByteOffset = (int)bin.Position;
                foreach (var v in node.Normals) binWriter.Write(v);
                int normBytes = (int)bin.Position - normByteOffset;
                Pad4(bin, binWriter);

                int uvByteOffset = 0, uvBytes = 0;
                if (textured)
                {
                    uvByteOffset = (int)bin.Position;
                    foreach (var v in node.UVs) binWriter.Write(v);
                    uvBytes = (int)bin.Position - uvByteOffset;
                    Pad4(bin, binWriter);
                }

                int idxBvIdx = bufferViews.Count;
                bufferViews.Add(new JObject {
                    ["buffer"] = 0, ["byteOffset"] = idxByteOffset, ["byteLength"] = idxBytes, ["target"] = 34963,
                });
                int posBvIdx = bufferViews.Count;
                bufferViews.Add(new JObject {
                    ["buffer"] = 0, ["byteOffset"] = posByteOffset, ["byteLength"] = posBytes,
                    ["byteStride"] = 12, ["target"] = 34962,
                });
                int normBvIdx = bufferViews.Count;
                bufferViews.Add(new JObject {
                    ["buffer"] = 0, ["byteOffset"] = normByteOffset, ["byteLength"] = normBytes,
                    ["byteStride"] = 12, ["target"] = 34962,
                });

                int idxAccIdx = accessors.Count;
                accessors.Add(new JObject {
                    ["bufferView"] = idxBvIdx, ["componentType"] = 5125,
                    ["count"] = node.Indices.Count, ["type"] = "SCALAR",
                });
                int posAccIdx = accessors.Count;
                accessors.Add(new JObject {
                    ["bufferView"] = posBvIdx, ["componentType"] = 5126,
                    ["count"] = node.Positions.Count / 3, ["type"] = "VEC3",
                    ["min"] = new JArray { pMinX, pMinY, pMinZ },
                    ["max"] = new JArray { pMaxX, pMaxY, pMaxZ },
                });
                int normAccIdx = accessors.Count;
                accessors.Add(new JObject {
                    ["bufferView"] = normBvIdx, ["componentType"] = 5126,
                    ["count"] = node.Normals.Count / 3, ["type"] = "VEC3",
                });

                int uvAccIdx = -1;
                if (textured)
                {
                    int uvBvIdx = bufferViews.Count;
                    bufferViews.Add(new JObject {
                        ["buffer"] = 0, ["byteOffset"] = uvByteOffset, ["byteLength"] = uvBytes,
                        ["byteStride"] = 8, ["target"] = 34962,
                    });
                    uvAccIdx = accessors.Count;
                    accessors.Add(new JObject {
                        ["bufferView"] = uvBvIdx, ["componentType"] = 5126,
                        ["count"] = node.UVs.Count / 2, ["type"] = "VEC2",
                    });
                }

                var attrs = new JObject { ["POSITION"] = posAccIdx, ["NORMAL"] = normAccIdx };
                if (uvAccIdx >= 0) attrs["TEXCOORD_0"] = uvAccIdx;

                int meshIdx = meshes.Count;
                meshes.Add(new JObject {
                    ["primitives"] = new JArray {
                        new JObject {
                            ["attributes"] = attrs,
                            ["indices"] = idxAccIdx,
                            ["material"] = matIdx,
                            ["mode"] = 4,
                        }
                    },
                });

                nodes.Add(new JObject {
                    ["name"] = node.Name,
                    ["mesh"] = meshIdx,
                    ["extras"] = new JObject {
                        ["uniqueId"] = node.UniqueId,
                        ["category"] = node.Category,
                    },
                });
            }

            // Phase 2 — per-material texture diagnostics + summary (the human reads these in
            // StingTools.log after a re-publish to see exactly what resolved + why any skipped).
            if (_exportTextures)
            {
                int withBitmap = 0, embedded = 0, noBitmap = 0, pathMissing = 0;
                foreach (var kv in _appearanceCache)
                {
                    var d = kv.Value; if (d == null) continue;
                    bool emb = !string.IsNullOrEmpty(d.DiffuseTexPath) && imgCache.TryGetValue(d.DiffuseTexPath!, out var ii) && ii >= 0;
                    if (d.BitmapFound) withBitmap++;
                    if (emb) embedded++;
                    if (!d.BitmapFound) noBitmap++;
                    else if (string.IsNullOrEmpty(d.DiffuseTexPath)) pathMissing++;
                    var line = $"[tex] '{d.MatName}': appearanceAsset={(d.HadAsset ? "yes" : "no")} " +
                               $"diffuseBitmapProp={(d.BitmapFound ? "found" : "none")} rawPath='{d.RawPath}' " +
                               $"resolved='{(string.IsNullOrEmpty(d.DiffuseTexPath) ? "MISSING" : d.DiffuseTexPath)}' " +
                               $"embedded={(emb ? "yes" : "no")} reason={d.Reason}";
                    if (string.IsNullOrEmpty(d.DiffuseTexPath) && d.BitmapFound && !string.IsNullOrEmpty(d.AttemptedDirs))
                        line += $" searchedDirs='{d.AttemptedDirs}'";
                    StingLog.Info(line);
                }
                StingLog.Info($"[tex] SUMMARY materials={_appearanceCache.Count} withBitmap={withBitmap} " +
                              $"embedded={embedded} skipped-noBitmap={noBitmap} skipped-pathMissing={pathMissing} " +
                              $"uvless-textured={uvlessTextured} images={images.Count}");
            }

            var rootNodeIndices = new JArray();
            for (int i = 0; i < nodes.Count; i++) rootNodeIndices.Add(i);

            var binBytes = bin.ToArray();
            var gltf = new JObject {
                ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "STING RevitGltfExporter" },
                ["scene"] = 0,
                ["scenes"] = new JArray { new JObject { ["nodes"] = rootNodeIndices } },
                ["nodes"] = nodes,
                ["meshes"] = meshes,
                ["materials"] = materials,
                ["accessors"] = accessors,
                ["bufferViews"] = bufferViews,
                ["buffers"] = new JArray { new JObject { ["byteLength"] = binBytes.Length } },
            };
            // Phase 2 — only attach the texture graph when something was actually embedded.
            if (images.Count > 0) gltf["images"] = images;
            if (samplers.Count > 0) gltf["samplers"] = samplers;
            if (textures.Count > 0) gltf["textures"] = textures;
            if (usedKhrTransform) gltf["extensionsUsed"] = new JArray { "KHR_texture_transform" };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(gltf.ToString(Newtonsoft.Json.Formatting.None));
            int jsonPad = (4 - (jsonBytes.Length & 3)) & 3;
            int binPad = (4 - (binBytes.Length & 3)) & 3;
            int totalLength = 12 + 8 + jsonBytes.Length + jsonPad + 8 + binBytes.Length + binPad;

            using var fs = File.Create(path);
            using var fw = new BinaryWriter(fs);
            fw.Write(0x46546C67);
            fw.Write(2);
            fw.Write(totalLength);

            fw.Write(jsonBytes.Length + jsonPad);
            fw.Write(0x4E4F534A);
            fw.Write(jsonBytes);
            for (int i = 0; i < jsonPad; i++) fw.Write((byte)0x20);

            fw.Write(binBytes.Length + binPad);
            fw.Write(0x004E4942);
            fw.Write(binBytes);
            for (int i = 0; i < binPad; i++) fw.Write((byte)0);

            return new ExportResult
            {
                Path = path,
                ElementCount = totalElements,
                BoundsMm = totalElements > 0
                    ? new[] { minX, minY, minZ, maxX, maxY, maxZ }
                    : new[] { 0d, 0d, 0d, 0d, 0d, 0d },
                FileSizeBytes = totalLength,
            };
        }

        private static int ResolveMaterial(JArray materials, Dictionary<string, int> cache, int[]? rgb)
        {
            int r = rgb != null && rgb.Length >= 3 ? rgb[0] : 200;
            int g = rgb != null && rgb.Length >= 3 ? rgb[1] : 200;
            int b = rgb != null && rgb.Length >= 3 ? rgb[2] : 200;
            string key = $"{r},{g},{b}";
            if (cache.TryGetValue(key, out var idx)) return idx;
            idx = materials.Count;
            materials.Add(new JObject
            {
                ["pbrMetallicRoughness"] = new JObject
                {
                    ["baseColorFactor"] = new JArray { r / 255.0, g / 255.0, b / 255.0, 1.0 },
                    ["metallicFactor"] = 0.0,
                    ["roughnessFactor"] = 0.85,
                },
                ["doubleSided"] = true,
            });
            cache[key] = idx;
            return idx;
        }

        private static int[]? ResolveCategoryColour(Element el)
        {
            try
            {
                var c = el.Category;
                if (c == null) return null;
                var col = c.LineColor;
                if (col == null) return null;
                int r = col.Red, g = col.Green, b = col.Blue;
                if (r == 0 && g == 0 && b == 0) return new[] { 200, 200, 200 };
                return new[] { r, g, b };
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static void Pad4(MemoryStream s, BinaryWriter w)
        {
            int pad = (int)((4 - (s.Position & 3)) & 3);
            for (int i = 0; i < pad; i++) w.Write((byte)0);
        }

        private class MeshNode
        {
            public string UniqueId = "";
            public string Name = "";
            public string Category = "";
            public int[]? Rgb;
            public List<float> Positions = new();
            public List<float> Normals = new();
            public List<uint> Indices = new();
            public List<float> UVs = new();   // Phase 2 — TEXCOORD_0 (2 floats/vertex), aligned to Positions
            public MaterialDef? Mat;          // Phase 2 — resolved appearance (textures + PBR)
        }

        // Phase 2 — resolved Revit appearance for one material.
        private class MaterialDef
        {
            public int[]? DiffuseRgb;          // fallback base colour (0..255)
            public double Alpha = 1.0;         // 1 = opaque
            public double Roughness = 0.85;
            public double Metallic = 0.0;
            public string? DiffuseTexPath;     // baseColorTexture source (resolved, existing file)
            public string? NormalTexPath;      // normalTexture source
            public double UvScaleU = 1.0, UvScaleV = 1.0;  // real-world tile size (feet) → KHR scale = 1/size
            public double UvOffU = 0.0, UvOffV = 0.0;
            public double UvAngle = 0.0;       // degrees (Revit texture_WAngle)
            public string Key = "";
            // ── self-diagnostics (logged at export end so a re-publish shows what happened) ──
            public string MatName = "";
            public bool HadAsset;              // material had an AppearanceAsset rendering asset
            public bool BitmapFound;           // a diffuse bitmap property with a non-empty path was found
            public string RawPath = "";        // the raw bitmap string from the asset (pre-resolve)
            public string Reason = "";         // ok | no-appearance-asset | no-bitmap | path-missing | exception:…
            public string AttemptedDirs = "";  // library dirs searched when the path didn't resolve
            public void ComputeKey()
            {
                var rgb = DiffuseRgb != null ? string.Join(",", DiffuseRgb) : "x";
                Key = $"{DiffuseTexPath}|{NormalTexPath}|{rgb}|{Alpha:F3}|{Roughness:F3}|{Metallic:F1}|{UvScaleU:F4}|{UvScaleV:F4}|{UvOffU:F4}|{UvOffV:F4}|{UvAngle:F2}";
            }
        }

        public class ExportResult
        {
            public string Path = "";
            public int ElementCount;
            public double[] BoundsMm = Array.Empty<double>();
            public long FileSizeBytes;
        }
    }
}
