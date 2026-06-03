namespace Planscape.Core.Coordinates;

/// <summary>
/// BLK-2 — the ONE canonical cross-host coordinate transform used to federate
/// models authored in different hosts (Revit / ArchiCAD / Tekla-via-IFC) into a
/// single shared world space.
///
/// Axis convention (explicit — matches Revit/IFC and the viewer's apply path):
///   • Z-UP world. <c>RotationDeg</c> is rotation about the vertical world Z
///     axis only (i.e. project true-north alignment); Z is never rotated.
///     A model exported Y-up must be re-based to Z-up at GLB build time before
///     this transform is applied — the transform itself assumes Z-up.
///   • Translation (Tx,Ty,Tz) is in MILLIMETRES — the unit
///     <see cref="Entities.ProjectModelTransform"/> stores. Callers that work in
///     metres (e.g. the web viewer's GLB) divide the translation by 1000.
///   • <c>ScaleFactor</c> is uniform and absorbs per-model unit mismatch
///     (e.g. a model authored in metres vs one in millimetres).
///   • Composition is T·R·S: world = T + R_z(scale · local).
///
/// Two models that share a physical point each carry the transform that maps
/// their own local frame onto the common project survey origin; applying each
/// model's transform to that shared point yields the SAME world coordinate, so
/// the models overlay. Proven by ModelTransformMathTests.
/// </summary>
public static class ModelTransformMath
{
    public readonly record struct Vec3(double X, double Y, double Z);

    /// <summary>Map a local-space point (mm) into shared world space (mm).</summary>
    public static Vec3 ApplyMm(
        double tx, double ty, double tz,
        double rotationDeg, double scale,
        double x, double y, double z)
    {
        var rad = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        var sx = x * scale;
        var sy = y * scale;
        var sz = z * scale;

        return new Vec3(
            cos * sx - sin * sy + tx,
            sin * sx + cos * sy + ty,
            sz + tz);
    }

    /// <summary>
    /// Inverse of <see cref="ApplyMm"/>: map a shared-world point (mm) back into
    /// a model's local frame. Used to derive what local coordinate a known world
    /// point occupies in a given model — the basis of the overlay proof.
    /// </summary>
    public static Vec3 InverseMm(
        double tx, double ty, double tz,
        double rotationDeg, double scale,
        double wx, double wy, double wz)
    {
        if (scale == 0) scale = 1;
        var dx = wx - tx;
        var dy = wy - ty;
        var dz = wz - tz;

        var rad = -rotationDeg * Math.PI / 180.0;   // inverse rotation
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        return new Vec3(
            (cos * dx - sin * dy) / scale,
            (sin * dx + cos * dy) / scale,
            dz / scale);
    }
}
