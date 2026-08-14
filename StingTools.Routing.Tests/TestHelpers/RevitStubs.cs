// RevitStubs.cs — minimal Autodesk.Revit.DB types so the routing math
// files compile without referencing the actual Revit API. Mirrors the
// pattern StingTools.Clash.Tests uses with System.Numerics shims.
//
// Only the public surface area touched by AStarSolver / AcoRefiner /
// VoxelGrid / ConduitRouteEngine is implemented.
//
// A correction to the note that used to sit here (#553)
// -----------------------------------------------------
// The original comment said "everything else throws NotImplementedException so
// accidental drift surfaces loudly rather than silently breaking under a pure-logic
// test". That reasoning is sound for drift inside a METHOD BODY — a
// NotImplementedException at runtime is loud. It does not hold for drift in a TYPE
// or a NAMESPACE, which fails at COMPILE time and takes the whole project's 41
// tests offline in silence. That is exactly what happened: 3e43f16e1 gave
// ConduitRouteEngine a `using Autodesk.Revit.DB.Structure` and a Document
// parameter, this shim did not grow with it, and the suite reported nothing at all
// from 2026-05-13 until now. The mechanism designed to make drift loud is the one
// that made it silent.
//
// So the shim is not the safety net and cannot be. The safety net is the CI job
// that builds every test project and fails on any that will not compile.
//
// Design rule for anything added below
// ------------------------------------
// Types exist so the code compiles; behaviour THROWS. Never return an empty
// collection or a default — see the ComputeRouteAdvanced note on
// FilteredElementCollector for what that would cost.

using System;

namespace Autodesk.Revit.DB
{
    public sealed class XYZ
    {
        public static readonly XYZ Zero   = new XYZ(0, 0, 0);
        public static readonly XYZ BasisX = new XYZ(1, 0, 0);
        public static readonly XYZ BasisY = new XYZ(0, 1, 0);
        public static readonly XYZ BasisZ = new XYZ(0, 0, 1);

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public XYZ(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double DistanceTo(XYZ other)
        {
            double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public double GetLength() => Math.Sqrt(X * X + Y * Y + Z * Z);
        public XYZ Normalize()
        {
            double l = GetLength();
            return l < 1e-12 ? Zero : new XYZ(X / l, Y / l, Z / l);
        }
        public double DotProduct(XYZ other) => X * other.X + Y * other.Y + Z * other.Z;
        public XYZ CrossProduct(XYZ o) =>
            new XYZ(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

        public static XYZ operator +(XYZ a, XYZ b) => new XYZ(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static XYZ operator -(XYZ a, XYZ b) => new XYZ(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static XYZ operator *(XYZ a, double s) => new XYZ(a.X * s, a.Y * s, a.Z * s);
        public static XYZ operator *(double s, XYZ a) => new XYZ(a.X * s, a.Y * s, a.Z * s);
        public static XYZ operator /(XYZ a, double s) => new XYZ(a.X / s, a.Y / s, a.Z / s);

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    public sealed class BoundingBoxXYZ
    {
        public XYZ Min { get; set; } = XYZ.Zero;
        public XYZ Max { get; set; } = XYZ.Zero;
    }

    public sealed class Outline
    {
        public XYZ MinimumPoint { get; }
        public XYZ MaximumPoint { get; }
        public Outline(XYZ min, XYZ max) { MinimumPoint = min; MaximumPoint = max; }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Below: the surface ConduitRouteEngine.ComputeRouteAdvanced needs in
    //  order to COMPILE. Nothing here is exercised by any test.
    //
    //  ComputeRouteAdvanced is the one genuinely Revit-bound method in the
    //  linked file — it collects structural obstacles through a
    //  FilteredElementCollector. The five members the routing tests actually
    //  call (ComputeRoute, CountBends, EstimateCableOdMm,
    //  SelectConduitDiameterMm, SplitAtBendCap) are pure math and touch none
    //  of it. Splitting that one method into its own file would be the
    //  cleaner fix, but it is a change to shipping plugin source made for the
    //  convenience of a test project, so it is left for a deliberate decision
    //  rather than smuggled in here.
    //
    //  READ THIS BEFORE MAKING ANY OF IT "WORK":
    //  ComputeRouteAdvanced wraps its collector block in try/catch and falls
    //  back to the rectilinear ComputeRoute path on failure. So a stub that
    //  returned an EMPTY element set would not fail — it would route through
    //  a world containing no obstacles and return a confident, plausible,
    //  wrong path. And a stub that throws is caught by that same catch and
    //  quietly degrades to the fallback. NEITHER is a usable test of
    //  ComputeRouteAdvanced. It is not testable in this project by
    //  construction, and pretending otherwise is worse than leaving it
    //  untested. Testing it needs a real Revit reference.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Opaque compile-time stand-in for the Revit document handle.</summary>
    public sealed class Document
    {
        internal Document() => throw new NotSupportedException(StubMessage.For("Document"));
    }

    /// <summary>Compile-time stand-in for a Revit view (only ever passed as null here).</summary>
    public sealed class View
    {
        internal View() => throw new NotSupportedException(StubMessage.For("View"));
    }

    public abstract class Element
    {
        protected Element() => throw new NotSupportedException(StubMessage.For("Element"));
        public BoundingBoxXYZ get_BoundingBox(View view) => throw new NotSupportedException(StubMessage.For("Element.get_BoundingBox"));
    }

    public class Floor : Element { }
    public class FamilyInstance : Element { }

    public enum BuiltInCategory
    {
        OST_StructuralColumns,
        OST_StructuralFraming,
    }

    /// <summary>
    /// Compile-time stand-in only. Every member throws — see the block comment
    /// above for why an empty result would be actively dangerous here.
    /// </summary>
    /// <remarks>
    /// Implements <c>IEnumerable&lt;Element&gt;</c> because the real one does, and
    /// ConduitRouteEngine chains <c>.Cast&lt;Floor&gt;()</c> straight off it. The
    /// enumerator THROWS rather than yielding nothing — an empty enumeration is the
    /// single most dangerous thing this stub could do (see the block comment above).
    /// </remarks>
    public sealed class FilteredElementCollector : System.Collections.Generic.IEnumerable<Element>
    {
        public FilteredElementCollector(Document doc) => throw new NotSupportedException(StubMessage.For("FilteredElementCollector"));
        public FilteredElementCollector OfClass(Type type) => throw new NotSupportedException(StubMessage.For("FilteredElementCollector.OfClass"));
        public FilteredElementCollector OfCategory(BuiltInCategory category) => throw new NotSupportedException(StubMessage.For("FilteredElementCollector.OfCategory"));
        public FilteredElementCollector WhereElementIsNotElementType() => throw new NotSupportedException(StubMessage.For("FilteredElementCollector.WhereElementIsNotElementType"));
        public System.Collections.Generic.IEnumerable<Element> ToElements() => throw new NotSupportedException(StubMessage.For("FilteredElementCollector.ToElements"));

        public System.Collections.Generic.IEnumerator<Element> GetEnumerator()
            => throw new NotSupportedException(StubMessage.For("FilteredElementCollector.GetEnumerator"));
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => throw new NotSupportedException(StubMessage.For("FilteredElementCollector.GetEnumerator"));
    }

    internal static class StubMessage
    {
        internal static string For(string member) =>
            $"Autodesk.Revit.DB.{member} is a COMPILE-TIME STUB in StingTools.Routing.Tests. " +
            "It exists so ConduitRouteEngine.ComputeRouteAdvanced compiles, not so it runs. " +
            "ComputeRouteAdvanced swallows failures here and degrades to the rectilinear " +
            "fallback, so neither throwing nor returning empty gives a meaningful test — " +
            "it needs a real Revit API reference.";
    }
}

// ConduitRouteEngine.cs imports Autodesk.Revit.DB.Structure but uses nothing from
// it; the namespace only has to exist for the using directive to resolve. Left
// deliberately empty rather than populated with types nothing references.
namespace Autodesk.Revit.DB.Structure
{
    internal static class NamespaceAnchor { }
}
