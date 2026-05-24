// Revit API CI Stubs — Autodesk.Revit.DB.Geometry (nested under DB)
using System;
using System.Collections.Generic;

namespace Autodesk.Revit.DB
{
    public class MEPCurve : Element
    {
        public ConnectorManager ConnectorManager { get; }
        public MEPSystem MEPSystem { get; }
        public double Diameter { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public Curve GetLocationCurve_MEP() => throw new NotImplementedException();
        public Curve Location_Curve { get; }
        public ElementId GetTypeId() => throw new NotImplementedException();
    }

    public class MEPSystem : Element { }

    public class ConnectorManager
    {
        public ConnectorSet Connectors { get; }
        public ISet<Connector> UnusedConnectors { get; }
    }
    public class ConnectorSet : IEnumerable<Connector>
    {
        public IEnumerator<Connector> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public int Size { get; }
        public bool IsEmpty { get; }
    }
    public class Connector
    {
        public ConnectorType ConnectorType { get; }
        public Domain Domain { get; }
        public bool IsConnected { get; }
        public ConnectorSet AllRefs { get; }
        public XYZ Origin { get; }
        public XYZ CoordinateSystem { get; }
        public Element Owner { get; }
        public MEPConnectorInfo GetMEPConnectorInfo() => throw new NotImplementedException();
    }
    public class MEPConnectorInfo { public string FlowDirection { get; } }
    public enum ConnectorType { Undefined, EndDefined, Curve, Physical, Logical }
    public enum Domain { Undefined, DomainHvac, DomainPiping, DomainElectrical, DomainCableTrayConduit }

    public class ConnectorElement : Element
    {
        public Domain Domain { get; }
        public ConnectorType ConnectorType { get; }
    }

    // Geometry sub-namespace
    public static class Geometry { }
}

namespace Autodesk.Revit.DB.Geometry
{
    // these are stub placeholders — Revit's geometry types live in DB but
    // the compiler sometimes sees them via the nested-namespace path.
}

// The actual geometry classes live in Autodesk.Revit.DB
namespace Autodesk.Revit.DB
{
    public class GeometryElement : IEnumerable<GeometryObject>
    {
        public IEnumerator<GeometryObject> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public GeometryElement GetTransformed(Transform transform) => throw new NotImplementedException();
    }

    public abstract class GeometryObject { public Visibility Visibility { get; } public GraphicsStyle GraphicsStyle { get; } public bool IsReadOnly { get; } }
    public enum Visibility { Visible, Invisible, Unknown }

    public class GeometryInstance : GeometryObject
    {
        public GeometryElement GetSymbolGeometry() => throw new NotImplementedException();
        public GeometryElement GetInstanceGeometry() => throw new NotImplementedException();
        public GeometryElement GetInstanceGeometry(Transform transform) => throw new NotImplementedException();
        public Transform Transform { get; }
    }

    public class Solid : GeometryObject
    {
        public FaceArray Faces { get; }
        public EdgeArray Edges { get; }
        public double Volume { get; }
        public double SurfaceArea { get; }
        public XYZ ComputeCentroid() => throw new NotImplementedException();
        public BoundingBoxXYZ GetBoundingBox() => throw new NotImplementedException();
    }

    public abstract class Face : GeometryObject
    {
        public Mesh Triangulate() => throw new NotImplementedException();
        public Mesh Triangulate(double levelOfDetail) => throw new NotImplementedException();
        public IList<CurveLoop> GetEdgesAsCurveLoops() => throw new NotImplementedException();
        public XYZ ComputeNormal(UV point) => throw new NotImplementedException();
        public XYZ Project(XYZ point) => throw new NotImplementedException();
        public BoundingBoxUV GetBoundingBox() => throw new NotImplementedException();
        public bool IsInside(UV uv) => throw new NotImplementedException();
    }
    public class PlanarFace : Face { public XYZ FaceNormal { get; } public XYZ Origin { get; } }
    public class CylindricalFace : Face { public XYZ Axis { get; } public double Radius { get; } }
    public class RevolvedFace : Face { }
    public class RuledFace : Face { }
    public class HermiteFace : Face { }
    public class ConicalFace : Face { }

    public class Mesh : GeometryObject
    {
        public int NumTriangles { get; }
        public MeshTriangle get_Triangle(int index) => throw new NotImplementedException();
        public IList<XYZ> Vertices { get; }
    }
    public class MeshTriangle { public XYZ get_Vertex(int index) => throw new NotImplementedException(); public int get_Index(int index) => throw new NotImplementedException(); }

    public class Edge : GeometryObject
    {
        public Curve AsCurve() => throw new NotImplementedException();
        public IList<Face> GetFaces() => throw new NotImplementedException();
    }

    public class FaceArray : IEnumerable<Face>
    {
        public IEnumerator<Face> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public int Size { get; }
        public Face get_Item(int index) => throw new NotImplementedException();
    }
    public class EdgeArray : IEnumerable<Edge>
    {
        public IEnumerator<Edge> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public int Size { get; }
    }
    public class BoundingBoxUV { public UV Min { get; } public UV Max { get; } }
}
