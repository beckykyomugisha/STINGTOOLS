// BcfViewpoint.cs — build a full BCF viewpoint from a clash record.
// Writes valid BCF 2.1 (.bcfv XML); BCF 3.0 uses the same schema for our purposes.
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace StingTools.Core.Clash
{
    public sealed class BcfViewpointCamera
    {
        public Vector3 ViewPoint;
        public Vector3 Direction;
        public Vector3 Up;
        public float FieldOfView = 45f;
        public float AspectRatio = 1.33f;
    }

    public sealed class BcfClippingPlane
    {
        public Vector3 Location;
        public Vector3 Direction;
    }

    public sealed class BcfComponent
    {
        public string IfcGuid;
        public string AuthoringTool;
        public string OriginatingSystem;
    }

    public sealed class BcfViewpointBuilder
    {
        public BcfViewpointCamera Camera;
        public List<BcfClippingPlane> ClippingPlanes = new List<BcfClippingPlane>();
        public List<BcfComponent> Selection = new List<BcfComponent>();
        public List<(string IfcGuid, string ColorHex)> Colors = new List<(string, string)>();
        public string SnapshotFileName;    // e.g. "snapshot.png"

        // rec-14: BCF 2.1 / 3.0 viewpoints are SPECIFIED IN METRES
        // (https://github.com/buildingSMART/BCF-XML/tree/release_2_1/Documentation).
        // Revit internal coordinates are feet. All ClashRecord coord fields
        // (AabbMin/AabbMax/Centroid) are also feet because they come from
        // System.Numerics.Vector3 copied straight out of the Revit geometry
        // pipeline. Convert once on construction of the builder so every
        // downstream write (camera, clipping planes, snapshot pad) is in
        // metres without per-write scaling.
        private const float FeetToMetres = 0.3048f;

        public static BcfViewpointBuilder FromClash(ClashRecord c)
        {
            var b = new BcfViewpointBuilder();
            // feet → metres for all coords.
            var cen = new Vector3(
                c.Centroid[0] * FeetToMetres,
                c.Centroid[1] * FeetToMetres,
                c.Centroid[2] * FeetToMetres);
            var size = new Vector3(
                (c.AabbMax[0] - c.AabbMin[0]) * FeetToMetres,
                (c.AabbMax[1] - c.AabbMin[1]) * FeetToMetres,
                (c.AabbMax[2] - c.AabbMin[2]) * FeetToMetres);
            float diag = size.Length();
            // Offset is in metres now; 2 m fallback when diag is tiny (sub-metre clashes).
            var offset = new Vector3(1, 1, 0.5f) * (diag * 1.3f + 2f);
            b.Camera = new BcfViewpointCamera
            {
                ViewPoint = cen + offset,
                Direction = Vector3.Normalize(cen - (cen + offset)),
                Up = new Vector3(0, 0, 1),
                FieldOfView = 45f,
                AspectRatio = 1.33f
            };
            // Six clipping planes around a 500 mm-padded section box (all in metres).
            float pad = 0.5f;   // 0.5 metre pad = 500 mm — correct BCF unit
            var mn = new Vector3(
                c.AabbMin[0] * FeetToMetres - pad,
                c.AabbMin[1] * FeetToMetres - pad,
                c.AabbMin[2] * FeetToMetres - pad);
            var mx = new Vector3(
                c.AabbMax[0] * FeetToMetres + pad,
                c.AabbMax[1] * FeetToMetres + pad,
                c.AabbMax[2] * FeetToMetres + pad);
            b.ClippingPlanes.Add(new BcfClippingPlane { Location = mn, Direction = new Vector3(-1, 0, 0) });
            b.ClippingPlanes.Add(new BcfClippingPlane { Location = mx, Direction = new Vector3( 1, 0, 0) });
            b.ClippingPlanes.Add(new BcfClippingPlane { Location = mn, Direction = new Vector3( 0,-1, 0) });
            b.ClippingPlanes.Add(new BcfClippingPlane { Location = mx, Direction = new Vector3( 0, 1, 0) });
            b.ClippingPlanes.Add(new BcfClippingPlane { Location = mn, Direction = new Vector3( 0, 0,-1) });
            b.ClippingPlanes.Add(new BcfClippingPlane { Location = mx, Direction = new Vector3( 0, 0, 1) });

            if (!string.IsNullOrEmpty(c.ElementA?.IfcGuid))
                b.Selection.Add(new BcfComponent { IfcGuid = c.ElementA.IfcGuid, AuthoringTool = "Revit", OriginatingSystem = "STING" });
            if (!string.IsNullOrEmpty(c.ElementB?.IfcGuid))
                b.Selection.Add(new BcfComponent { IfcGuid = c.ElementB.IfcGuid, AuthoringTool = "Revit", OriginatingSystem = "STING" });

            if (c.ElementA != null) b.Colors.Add((c.ElementA.IfcGuid, "#FF3030"));
            if (c.ElementB != null) b.Colors.Add((c.ElementB.IfcGuid, "#FFC000"));
            return b;
        }

        public string BuildBcfv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<VisualizationInfo xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
            if (Selection.Count > 0)
            {
                sb.AppendLine("  <Components>");
                sb.AppendLine("    <Selection>");
                foreach (var c in Selection)
                {
                    sb.AppendLine($"      <Component IfcGuid=\"{XmlEsc(c.IfcGuid)}\" OriginatingSystem=\"{XmlEsc(c.OriginatingSystem)}\" />");
                }
                sb.AppendLine("    </Selection>");
                sb.AppendLine("  </Components>");
            }
            if (Camera != null)
            {
                sb.AppendLine("  <PerspectiveCamera>");
                sb.AppendLine($"    <CameraViewPoint><X>{Camera.ViewPoint.X:F3}</X><Y>{Camera.ViewPoint.Y:F3}</Y><Z>{Camera.ViewPoint.Z:F3}</Z></CameraViewPoint>");
                sb.AppendLine($"    <CameraDirection><X>{Camera.Direction.X:F3}</X><Y>{Camera.Direction.Y:F3}</Y><Z>{Camera.Direction.Z:F3}</Z></CameraDirection>");
                sb.AppendLine($"    <CameraUpVector><X>{Camera.Up.X:F3}</X><Y>{Camera.Up.Y:F3}</Y><Z>{Camera.Up.Z:F3}</Z></CameraUpVector>");
                sb.AppendLine($"    <FieldOfView>{Camera.FieldOfView:F1}</FieldOfView>");
                sb.AppendLine($"    <AspectRatio>{Camera.AspectRatio:F3}</AspectRatio>");
                sb.AppendLine("  </PerspectiveCamera>");
            }
            if (ClippingPlanes.Count > 0)
            {
                sb.AppendLine("  <ClippingPlanes>");
                foreach (var p in ClippingPlanes)
                {
                    sb.AppendLine("    <ClippingPlane>");
                    sb.AppendLine($"      <Location><X>{p.Location.X:F3}</X><Y>{p.Location.Y:F3}</Y><Z>{p.Location.Z:F3}</Z></Location>");
                    sb.AppendLine($"      <Direction><X>{p.Direction.X:F3}</X><Y>{p.Direction.Y:F3}</Y><Z>{p.Direction.Z:F3}</Z></Direction>");
                    sb.AppendLine("    </ClippingPlane>");
                }
                sb.AppendLine("  </ClippingPlanes>");
            }
            sb.AppendLine("</VisualizationInfo>");
            return sb.ToString();
        }

        private static string XmlEsc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
