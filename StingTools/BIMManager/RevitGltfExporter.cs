#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
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

        private const double FeetToMm = 304.8;

        public RevitGltfExporter(Document doc)
        {
            _doc = doc;
            _xformStack.Push(Transform.Identity);
        }

        public static ExportResult Export(Document doc, View3D view, string outputGlbPath)
        {
            // S8.2.2 — span around the whole export so telemetry-on users see
            // p99 export latency vs scene size in their dashboards.
            return StingTools.Core.PluginTelemetry.Run(
                "RevitGltfExporter.export",
                () =>
                {
                    var ctx = new RevitGltfExporter(doc);
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
        public void OnMaterial(MaterialNode node)
        {
            try
            {
                var c = node.Color;
                if (c != null && _current != null)
                    _current.Rgb = new[] { (int)c.Red, (int)c.Green, (int)c.Blue };
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
                        latitude      = pos.Latitude  * (180.0 / Math.PI);
                        longitude     = pos.Longitude * (180.0 / Math.PI);
                        northAngleDeg = pos.Angle     * (180.0 / Math.PI);
                        elevation     = pos.Elevation * FeetToMm;
                    }
                    catch { /* not all configurations carry site data */ }
                }

                // Survey point position via its coordinate system (feet → mm)
                // TODO-VERIFY-API: BasePoint.GetCoordinateSystem() — available on BasePoint in Revit 2025.
                if (surveyPoint != null)
                {
                    try
                    {
                        var sp = surveyPoint.GetCoordinateSystem();
                        easting  = sp.Origin.X * FeetToMm;
                        northing = sp.Origin.Y * FeetToMm;
                    }
                    catch { /* survey point may not be set */ }
                }

                // Project base point coordinate system (feet → mm)
                double? pbpX = null, pbpY = null, pbpZ = null;
                if (projectBasePoint != null)
                {
                    try
                    {
                        var pbpCs = projectBasePoint.GetCoordinateSystem();
                        pbpX = pbpCs.Origin.X * FeetToMm;
                        pbpY = pbpCs.Origin.Y * FeetToMm;
                        pbpZ = pbpCs.Origin.Z * FeetToMm;
                    }
                    catch { /* base point may not be accessible */ }
                }

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

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            int totalElements = 0;

            for (int n = 0; n < _nodes.Count; n++)
            {
                var node = _nodes[n];
                if (node.Positions.Count == 0 || node.Indices.Count == 0) continue;
                totalElements++;

                int matIdx = ResolveMaterial(materials, matKeyToIndex, node.Rgb);

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

                int meshIdx = meshes.Count;
                meshes.Add(new JObject {
                    ["primitives"] = new JArray {
                        new JObject {
                            ["attributes"] = new JObject {
                                ["POSITION"] = posAccIdx,
                                ["NORMAL"] = normAccIdx,
                            },
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
