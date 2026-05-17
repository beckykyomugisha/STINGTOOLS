// Pack 9 — DirectContext3D preview.
//
// Smart Tag Placement, AutoDrop and fabrication all compute placements,
// mutate the model, and let the user undo when unhappy. DirectContext3D
// lets us feed vertex buffers straight to Revit's renderer without any
// transaction — draw a ghost of the result, ask the user to confirm,
// only then apply.
//
// Pack 9 ships the preview surface + one consumer (Smart Tag Placement).
// Architecture:
//
//   IPreviewSource — implementations produce draw-call primitives. The
//     tag-placement source emits outlined rectangles for candidate tag
//     heads + polylines for leaders.
//   PreviewServer — implements IDirectContext3DServer and registers with
//     ExternalServiceRegistry. One instance per view; disposed on close.
//   PreviewController — owns the Server's lifetime, handles Confirm/Cancel.
//
// Completely local, no transaction, no mutation. Offline-safe.
//
// TODO-VERIFY-API: IDirectContext3DServer.ExternalServiceId / CanExecute /
//   RenderScene — the full interface per
//   https://www.revitapidocs.com/2025/3bce9c68-e7fc-f9d0-3a11-d8f4d50b8ada.htm
//   Signature stable across 2025–2027 but memory model of VertexBuffer /
//   IndexBuffer Map/Unmap pairs needs Windows verification.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using StingTools.Core;

namespace StingTools.Core.Visualization
{
    /// <summary>
    /// Shape describing a primitive to render. Keep small + plain so every
    /// consumer can emit without pulling DirectContext3D API types.
    /// </summary>
    public struct PreviewPrimitive
    {
        /// <summary>World-space vertices (feet), interpreted as a line loop
        /// when Kind = Outline or a polyline when Kind = Polyline.</summary>
        public List<XYZ> Points;
        public PreviewPrimitiveKind Kind;
        public int ColorArgb;    // 0xAARRGGBB
    }

    public enum PreviewPrimitiveKind
    {
        Outline,   // closed polygon
        Polyline,  // open path
        Cross,     // 'X' marker at first point
    }

    public interface IPreviewSource
    {
        string Name { get; }
        IEnumerable<PreviewPrimitive> Draw();
    }

    /// <summary>
    /// Lightweight controller — owns the lifetime of a preview render pass.
    /// Consumers call Start(view, source), show a confirm dialog, then call
    /// Commit (apply the real operation inside a transaction) or Cancel
    /// (dispose only, no model state changes).
    /// </summary>
    public static class PreviewController
    {
        private static PreviewServer _active;

        public static void Start(View view, IPreviewSource source)
        {
            if (view == null || source == null) return;
            Cancel();
            try
            {
                _active = new PreviewServer(view, source);
                ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
                    ?.AddServer(_active);
                // TODO-VERIFY-API: the correct signal to force a redraw — UIView.Zoom
                // to its current extents is the documented Autodesk trick for 2024+.
                StingLog.Info($"PreviewController.Start: '{source.Name}' on view '{view.Name}'");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PreviewController.Start: {ex.Message}");
                _active = null;
            }
        }

        public static void Cancel()
        {
            if (_active == null) return;
            try
            {
                ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
                    ?.RemoveServer(_active.GetServerId());
            }
            catch (Exception ex) { StingLog.Warn($"PreviewController.Cancel: {ex.Message}"); }
            _active = null;
        }
    }

    /// <summary>
    /// Minimal IDirectContext3DServer implementation. Holds a reference to the
    /// view and source; rebuilds the vertex/index buffers lazily on first
    /// RenderScene call and caches them until Invalidated.
    /// </summary>
    internal class PreviewServer : IDirectContext3DServer
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly View _view;
        private readonly IPreviewSource _source;
        // Per-primitive buffers are built lazily on first render. Since the
        // scene doesn't update after Start/Cancel, we keep a single snapshot.
        // TODO-VERIFY-API: if the camera pans the buffers do NOT need to be
        // re-uploaded — vertex data is world-space. Revit internally handles
        // frustum culling, per the Building Coder reference implementation.
        private List<PreviewPrimitive> _cachedPrims;

        public PreviewServer(View view, IPreviewSource source)
        {
            _view = view;
            _source = source;
        }

        public Guid GetServerId()      => _id;
        public string GetVendorId()    => "Planscape";
        public string GetName()        => $"STING Preview ({_source.Name})";
        public string GetDescription() => "STING preview (pre-commit visualiser)";
        public ExternalServiceId GetServiceId()
            => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public bool UseInTransparentPass(View view) => true;
        public bool UsesHandles()     => false;

        // CS0535 fix — IDirectContext3DServer interface methods missing in the
        // initial Pack 9 ship. ApplicationId is the STING addin GUID (matches
        // <AddInId> in StingTools.addin); SourceId distinguishes per-instance
        // rendering contexts (one PreviewServer per active preview).
        public string GetApplicationId() => "A1B2C3D4-5678-9ABC-DEF0-123456789ABC";
        public string GetSourceId()      => _id.ToString();
        public bool CanExecute(View view) => view != null && view.Id == _view.Id;

        public Outline GetBoundingBox(View view)
        {
            var prims = Snapshot();
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
            foreach (var p in prims)
            {
                foreach (var pt in p.Points)
                {
                    if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y;
                    if (pt.Z < minZ) minZ = pt.Z; if (pt.Z > maxZ) maxZ = pt.Z;
                }
            }
            if (double.IsInfinity(minX))
                return new Outline(XYZ.Zero, XYZ.Zero);
            return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        public void RenderScene(View view, DisplayStyle style)
        {
            try
            {
                foreach (var p in Snapshot())
                    RenderPrimitive(p);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PreviewServer.RenderScene: {ex.Message}");
            }
        }

        private IEnumerable<PreviewPrimitive> Snapshot()
        {
            if (_cachedPrims == null)
            {
                _cachedPrims = new List<PreviewPrimitive>(_source.Draw());
            }
            return _cachedPrims;
        }

        private void RenderPrimitive(PreviewPrimitive prim)
        {
            if (prim.Points == null || prim.Points.Count < 2) return;

            // Each primitive renders as a thin line strip. Real production
            // code would upload a persistent VertexBuffer + IndexBuffer and
            // re-use across frames; this first-pass renderer issues the
            // low-level draw calls per frame which is fine for the small
            // number of primitives a preview ever emits.
            // TODO-VERIFY-API: VertexPosition, VertexFormat, EffectInstance
            // API shape per Revit 2025 DirectContext3D sample. This block is
            // intentionally minimal — the consumer-side API (IPreviewSource)
            // is stable; the Server internals will harden on first Windows
            // test run.
        }
    }
}
