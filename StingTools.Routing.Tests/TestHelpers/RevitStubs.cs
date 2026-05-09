// RevitStubs.cs — minimal Autodesk.Revit.DB types so the routing math
// files compile without referencing the actual Revit API. Mirrors the
// pattern StingTools.Clash.Tests uses with System.Numerics shims.
//
// Only the public surface area touched by AStarSolver / AcoRefiner /
// VoxelGrid / ConduitRouteEngine is implemented. Everything else
// throws NotImplementedException so accidental drift surfaces loudly
// rather than silently breaking under a pure-logic test.

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
}
