using Planscape.Core.Coordinates;

namespace Planscape.Tests;

/// <summary>
/// BLK-2 — proves the cross-host coordinate transform federates two models
/// authored in different world spaces into one shared space, with the axis
/// convention (Z-up, mm, rotation about Z, uniform scale) made explicit.
/// </summary>
public class ModelTransformMathTests
{
    private const int Precision = 6;   // mm, to 1e-6

    [Fact]
    public void IdentityTransform_LeavesPointUnchanged()
    {
        // The common single-model case: no transform → no movement.
        var p = ModelTransformMath.ApplyMm(0, 0, 0, rotationDeg: 0, scale: 1,
                                           x: 1234.5, y: -6789.0, z: 420.0);
        Assert.Equal(1234.5, p.X, Precision);
        Assert.Equal(-6789.0, p.Y, Precision);
        Assert.Equal(420.0, p.Z, Precision);
    }

    [Fact]
    public void KnownTransform_MatchesHandComputedWorldPoint()
    {
        // Local point (1000,0,500) mm, scale 1, rotate +90° about Z, then
        // translate by (10,20,30) mm. Rotating (1000,0) by +90° → (0,1000).
        // Z is untouched by the Z-rotation (Z-up convention).
        var p = ModelTransformMath.ApplyMm(
            tx: 10, ty: 20, tz: 30, rotationDeg: 90, scale: 1,
            x: 1000, y: 0, z: 500);

        Assert.Equal(10.0, p.X, Precision);     // 0 + 10
        Assert.Equal(1020.0, p.Y, Precision);   // 1000 + 20
        Assert.Equal(530.0, p.Z, Precision);    // 500 + 30 (no Z rotation)
    }

    [Fact]
    public void ScaleFactor_ConvertsMetreModelToMillimetreWorld()
    {
        // A model authored in metres (local point 5 = 5 m) with scale 1000
        // lands at 5000 mm — proves unit federation (m↔mm) via ScaleFactor.
        var p = ModelTransformMath.ApplyMm(0, 0, 0, rotationDeg: 0, scale: 1000,
                                           x: 5, y: 0, z: 0);
        Assert.Equal(5000.0, p.X, Precision);
    }

    [Fact]
    public void InverseMm_RoundTrips()
    {
        var local = ModelTransformMath.InverseMm(
            tx: -5000, ty: 8000, tz: 250, rotationDeg: -75, scale: 1.0,
            wx: 12345, wy: -6789, wz: 4200);
        var world = ModelTransformMath.ApplyMm(
            -5000, 8000, 250, -75, 1.0, local.X, local.Y, local.Z);
        Assert.Equal(12345.0, world.X, Precision);
        Assert.Equal(-6789.0, world.Y, Precision);
        Assert.Equal(4200.0, world.Z, Precision);
    }

    [Fact]
    public void TwoModelsInDifferentWorldSpaces_OverlayAfterTransform()
    {
        // One physical point shared by two federated models, in shared world (mm).
        const double worldX = 12345.0, worldY = -6789.0, worldZ = 4200.0;

        // Model A: local frame offset (1000,2000,0), rotated +30° about true-north,
        // authored in mm (scale 1).
        const double ax = 1000, ay = 2000, az = 0, aRot = 30, aScale = 1.0;
        // Model B: a completely different frame — offset (-5000,8000,250),
        // rotated -75°, authored in metres (scale 1000).
        const double bx = -5000, by = 8000, bz = 250, bRot = -75, bScale = 1000.0;

        // Where does the shared world point sit in each model's OWN local frame?
        var localA = ModelTransformMath.InverseMm(ax, ay, az, aRot, aScale, worldX, worldY, worldZ);
        var localB = ModelTransformMath.InverseMm(bx, by, bz, bRot, bScale, worldX, worldY, worldZ);

        // Apply each model's transform forward — both MUST land on the same world
        // coordinate. That equality is exactly "the two models overlay".
        var wA = ModelTransformMath.ApplyMm(ax, ay, az, aRot, aScale, localA.X, localA.Y, localA.Z);
        var wB = ModelTransformMath.ApplyMm(bx, by, bz, bRot, bScale, localB.X, localB.Y, localB.Z);

        Assert.Equal(worldX, wA.X, Precision);
        Assert.Equal(worldY, wA.Y, Precision);
        Assert.Equal(worldZ, wA.Z, Precision);

        Assert.Equal(wA.X, wB.X, Precision);
        Assert.Equal(wA.Y, wB.Y, Precision);
        Assert.Equal(wA.Z, wB.Z, Precision);

        // Sanity: the two models really were in different spaces (local coords differ).
        Assert.NotEqual(localA.X, localB.X, Precision);
    }
}
