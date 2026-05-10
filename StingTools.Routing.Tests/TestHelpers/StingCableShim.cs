// Minimal StingCable POCO. Mirrors the public surface of the real
// type (StingTools/Core/Electrical/CableManifest.cs:20) that
// ConduitRouteEngine.SelectConduitDiameterMm reads. Defining it
// here lets the test project skip CableManifest.cs which depends on
// Revit's Document.

using System.Collections.Generic;

namespace StingTools.Core.Electrical
{
    public class StingCable
    {
        public string CircuitId         { get; set; } = "";
        public string PanelName         { get; set; } = "";
        public int    CoreCount         { get; set; } = 3;
        public double CsaMm2            { get; set; } = 2.5;
        public double OuterDiameterMm   { get; set; }
        public string SourceEquipmentId { get; set; } = "";
        public string SegregationClass  { get; set; } = "UTP";
        public List<long> RouteTrayIds  { get; set; } = new List<long>();
        public List<long> JunctionBoxIds { get; set; } = new List<long>();
    }
}
