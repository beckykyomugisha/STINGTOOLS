// ClashExportContext.cs — IExportContext implementation that accumulates tessellated
// geometry per element, with the full transform stack applied.
//
// Key design notes:
// - OnElementBegin / OnElementEnd bracket each element.
// - OnInstanceBegin / OnInstanceEnd bracket FamilyInstance symbols (push transform stack).
// - OnLinkBegin / OnLinkEnd bracket RevitLinkInstance geometry (push transform stack).
// - OnPolymesh is called for each tessellated face — we accumulate into per-element buffers.
// - The transform stack is maintained manually; the current combined transform is applied
//   to every point before storing.
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    internal sealed class ClashExportContext : IExportContext
    {
        private readonly Document _hostDoc;
        private readonly Stack<Transform> _transformStack = new Stack<Transform>();
        private readonly Stack<string> _docStack = new Stack<string>();
        private readonly Stack<int> _linkInstanceStack = new Stack<int>();

        private ElementId _currentElementId;
        private string _currentDocGuid;
        private int _currentLinkInstanceId = -1;
        private List<float> _currentVertices;
        private List<int> _currentIndices;
        private Dictionary<long, int> _vertexDedup;
        private string _currentCategory;
        private string _currentUniqueId;
        private string _currentIfcGuid;

        public Dictionary<ClashElementKey, ClashMeshBuffer> Buffers { get; } =
            new Dictionary<ClashElementKey, ClashMeshBuffer>();

        public ClashExportContext(Document hostDoc)
        {
            _hostDoc = hostDoc;
            _transformStack.Push(Transform.Identity);
            _docStack.Push(hostDoc.ProjectInformation?.UniqueId ?? hostDoc.PathName ?? "host");
        }

        public bool Start()
        {
            return true;
        }

        public void Finish() { }

        public bool IsCanceled() => false;

        public RenderNodeAction OnViewBegin(ViewNode node) => RenderNodeAction.Proceed;
        public void OnViewEnd(ElementId elementId) { }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            try
            {
                _currentElementId = elementId;
                _currentVertices = new List<float>(256);
                _currentIndices = new List<int>(256);
                _vertexDedup = new Dictionary<long, int>(256);

                var doc = _docStack.Count > 0 ? GetDocFromGuid(_docStack.Peek()) : _hostDoc;
                var element = doc?.GetElement(elementId);
                _currentCategory = element?.Category?.Name ?? "";
                _currentUniqueId = element?.UniqueId ?? "";
                _currentIfcGuid = TryGetIfcGuid(doc, elementId);
                _currentDocGuid = _docStack.Peek();
                _currentLinkInstanceId = _linkInstanceStack.Count > 0 ? _linkInstanceStack.Peek() : -1;
                return RenderNodeAction.Proceed;
            }
            catch (Exception)
            {
                return RenderNodeAction.Skip;
            }
        }

        public void OnElementEnd(ElementId elementId)
        {
            try
            {
                if (_currentVertices == null || _currentVertices.Count == 0 || _currentIndices.Count == 0)
                    return;

                var key = new ClashElementKey(
                    _currentDocGuid,
                    _currentLinkInstanceId,
                    (int)elementId.Value,
                    _currentUniqueId,
                    _currentIfcGuid);

                if (!Buffers.ContainsKey(key))
                {
                    var buf = new ClashMeshBuffer(
                        key,
                        _currentCategory,
                        _currentVertices.ToArray(),
                        _currentIndices.ToArray());
                    Buffers[key] = buf;
                }
            }
            catch (Exception) { /* swallow; geometry-error elements are dropped */ }
            finally
            {
                _currentVertices = null;
                _currentIndices = null;
                _vertexDedup = null;
            }
        }

        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            _transformStack.Push(_transformStack.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node)
        {
            if (_transformStack.Count > 1) _transformStack.Pop();
        }

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            _transformStack.Push(_transformStack.Peek().Multiply(node.GetTransform()));
            // Identify the linked document and link instance id.
            string guid = "link";
            int linkInstId = -1;
            try
            {
                var linkDoc = node.GetDocument();
                guid = linkDoc?.ProjectInformation?.UniqueId ?? linkDoc?.PathName ?? "link";
                // Revit 2025 removed LinkNode.GetSymbolId (and RevitLinkType
                // resolution moved to LinkNode.GetDocument().Title). We fall
                // back to a stable 32-bit hash of the link's file path so
                // the clash identity keys stay unique across sessions.
                linkInstId = (linkDoc?.PathName ?? guid).GetHashCode();
            }
            catch (Exception ex) { StingLog.Warn($"LinkNode resolve: {ex.Message}"); }
            _docStack.Push(guid);
            _linkInstanceStack.Push(linkInstId);
            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node)
        {
            if (_transformStack.Count > 1) _transformStack.Pop();
            if (_docStack.Count > 1) _docStack.Pop();
            if (_linkInstanceStack.Count > 0) _linkInstanceStack.Pop();
        }

        public void OnPolymesh(PolymeshTopology node)
        {
            if (_currentVertices == null) return;
            try
            {
                var transform = _transformStack.Peek();
                var points = node.GetPoints();
                int baseOffset = _currentVertices.Count / 3;
                var remap = new int[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    var p = transform.OfPoint(points[i]);
                    // Round to 0.5 mm for dedup (0.5mm = 0.00164 ft).
                    long key = Quantize(p);
                    if (!_vertexDedup.TryGetValue(key, out int idx))
                    {
                        idx = _currentVertices.Count / 3;
                        _currentVertices.Add((float)p.X);
                        _currentVertices.Add((float)p.Y);
                        _currentVertices.Add((float)p.Z);
                        _vertexDedup[key] = idx;
                    }
                    remap[i] = idx;
                }

                var facets = node.GetFacets();
                for (int i = 0; i < facets.Count; i++)
                {
                    var f = facets[i];
                    _currentIndices.Add(remap[f.V1]);
                    _currentIndices.Add(remap[f.V2]);
                    _currentIndices.Add(remap[f.V3]);
                }
            }
            catch { /* swallow geometry errors */ }
        }

        public void OnMaterial(MaterialNode node) { }
        public void OnLight(LightNode node) { }
        public void OnRPC(RPCNode node) { }
        // Revit 2025 removed the DaylightPortalNode type and the matching
        // IExportContext.OnDaylightPortal(DaylightPortalNode) interface
        // member (they were marked obsolete from Revit 2022). The override
        // used to live here — now dropped so the file builds on the 2025
        // API reference.
        public RenderNodeAction OnFaceBegin(FaceNode node) => RenderNodeAction.Proceed;
        public void OnFaceEnd(FaceNode node) { }

        private static long Quantize(XYZ p)
        {
            // 0.5 mm tolerance ~ 0.00164 ft; quantize to 0.001 ft (~0.3 mm) for safety.
            long x = (long)Math.Round(p.X * 1000.0);
            long y = (long)Math.Round(p.Y * 1000.0);
            long z = (long)Math.Round(p.Z * 1000.0);
            // Pack into a long via FNV-like mix.
            unchecked
            {
                long h = 1469598103934665603L;
                h = (h ^ x) * 1099511628211L;
                h = (h ^ y) * 1099511628211L;
                h = (h ^ z) * 1099511628211L;
                return h;
            }
        }

        private Document GetDocFromGuid(string guid)
        {
            if (guid == (_hostDoc.ProjectInformation?.UniqueId ?? _hostDoc.PathName ?? "host"))
                return _hostDoc;
            // Caller responsibility to resolve linked docs; we return host as safe fallback.
            return _hostDoc;
        }

        private static string TryGetIfcGuid(Document doc, ElementId id)
        {
            if (doc == null || id == null) return "";
            // ExporterIFCUtils lives in RevitAPIIFC.dll (not referenced).
            // Element.UniqueId is the same string Revit uses internally to
            // derive the IFC GUID — safe fallback for the clash-kernel's
            // per-element identity hash.
            try { return doc.GetElement(id)?.UniqueId ?? ""; }
            catch (Exception ex) { StingLog.Warn($"TryGetIfcGuid: {ex.Message}"); return ""; }
        }
    }
}
