// FlowTree — a Revit-free, source-rooted pipe tree for branch-network solvers.
//
// Every element on the route from the source to the terminals is one node:
// a pipe carries its length and bore, a fitting or valve carries an
// equivalent length at its bore, a terminal (sprinkler head, gas appliance)
// is a leaf with its own demand data. Flow through a node passes through
// that node's resistance; elevation is the node's downstream point, so the
// static head between a node and its child is ρg·(z_child − z_node).
//
// Built from a Revit connector graph by RevitFlowTreeBuilder; built by hand
// in tests. Solved by SprinklerHydraulics and GasPipeSizer.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Mep.Networks
{
    public enum FlowNodeKind { Source, Pipe, Fitting, Accessory, Terminal, Junction }

    public sealed class FlowNode
    {
        public string Id { get; set; } = "";
        public FlowNodeKind Kind { get; set; }
        public string Label { get; set; } = "";
        /// <summary>Straight length (pipes) in metres. 0 for fittings/terminals.</summary>
        public double LengthM { get; set; }
        /// <summary>Equivalent length of a fitting or valve, in metres, at <see cref="DiameterMm"/>.</summary>
        public double EquivLengthM { get; set; }
        /// <summary>Equivalent length expressed in bores, used when the node is resized. 0 = fixed <see cref="EquivLengthM"/>.</summary>
        public double EquivLengthDiameters { get; set; }
        /// <summary>Internal bore, mm. 0 = unknown (inherits from the parent pipe when solved).</summary>
        public double DiameterMm { get; set; }
        /// <summary>Elevation of the node's downstream point, m.</summary>
        public double ElevationM { get; set; }
        /// <summary>Hazen-Williams C for pipes (sprinkler solver).</summary>
        public double HazenWilliamsC { get; set; }

        // Terminal data (only one set is used, by the solver that reads it)
        /// <summary>Sprinkler K-factor, L/min/bar^0.5.</summary>
        public double KFactor { get; set; }
        /// <summary>Gas appliance heat input, kW (gross).</summary>
        public double LoadKw { get; set; }

        public FlowNode Parent { get; private set; }
        public List<FlowNode> Children { get; } = new List<FlowNode>();

        public FlowNode Add(FlowNode child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            child.Parent = this;
            Children.Add(child);
            return child;
        }

        public double TotalLengthM => LengthM + EquivLengthM;
        public bool IsTerminal => Kind == FlowNodeKind.Terminal;

        public IEnumerable<FlowNode> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }

        /// <summary>Nodes from the root down to this one, inclusive.</summary>
        public List<FlowNode> PathFromRoot()
        {
            var path = new List<FlowNode>();
            for (var n = this; n != null; n = n.Parent) path.Add(n);
            path.Reverse();
            return path;
        }
    }

    public static class FlowTreeUtil
    {
        /// <summary>
        /// Fittings and accessories with no bore of their own take the bore of
        /// the nearest pipe upstream (else downstream); their equivalent length
        /// is then recomputed from <see cref="FlowNode.EquivLengthDiameters"/>.
        /// </summary>
        public static void ResolveInheritedBores(FlowNode root)
        {
            foreach (var n in new[] { root }.Concat(root.Descendants()))
            {
                if (n.Kind == FlowNodeKind.Pipe || n.Kind == FlowNodeKind.Terminal || n.Kind == FlowNodeKind.Source) continue;
                double d = n.DiameterMm;
                if (d <= 0)
                {
                    for (var p = n.Parent; p != null && d <= 0; p = p.Parent)
                        if (p.Kind == FlowNodeKind.Pipe && p.DiameterMm > 0) d = p.DiameterMm;
                    if (d <= 0)
                        d = n.Descendants().FirstOrDefault(c => c.Kind == FlowNodeKind.Pipe && c.DiameterMm > 0)?.DiameterMm ?? 0;
                    n.DiameterMm = d;
                }
                if (n.EquivLengthDiameters > 0 && n.DiameterMm > 0)
                    n.EquivLengthM = n.EquivLengthDiameters * n.DiameterMm / 1000.0;
            }
        }
    }
}
