// StingTools — VRF REFNET joint registry + tree-sizer.
//
// Phase 187h. STING_REFNET_JOINTS.json holds the per-vendor catalogue
// of REFNET branch joints (Daikin KHRP, Mitsubishi CMY, Toshiba RBM-BY).
// Each joint carries the maximum downstream-connected-capacity it can
// handle plus the upstream / downstream gas + liquid pipe ODs.
//
// `RefnetTreeSizer.SizeTree(tree, vendor, catalogue)` walks the tree
// from the root (ODU) outward, computes the connected-downstream
// capacity at every node, and picks the smallest joint whose maxKw ≥
// downstream capacity. Returns per-node selections + total joint
// count. Tree topology is supplied by the caller (extracted from the
// Revit connector graph or built manually).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Refrigerant
{
    public class RefnetJoint
    {
        public string Id                 { get; set; } = "";
        public string VendorSeriesId     { get; set; } = "";
        public double MaxKw              { get; set; }
        public double UpstreamGasOdMm    { get; set; }
        public double DownstreamGasOdMm  { get; set; }
        public double UpstreamLiqOdMm    { get; set; }
        public double DownstreamLiqOdMm  { get; set; }
    }

    public class RefnetCatalogue
    {
        public List<RefnetJoint> Joints { get; } = new();
        public IEnumerable<RefnetJoint> ForVendor(string vendorSeriesId)
            => Joints.Where(j => string.Equals(j.VendorSeriesId, vendorSeriesId,
                                StringComparison.OrdinalIgnoreCase))
                     .OrderBy(j => j.MaxKw);
    }

    public static class RefnetRegistry
    {
        public const string DataFileName = "STING_REFNET_JOINTS.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/refnet_joints.json";

        private static readonly ConcurrentDictionary<string, RefnetCatalogue> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        public static RefnetCatalogue Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static RefnetCatalogue Load(Document doc)
        {
            var cat = new RefnetCatalogue();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), cat);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), cat);
                }
            }
            catch (Exception ex)
            { StingTools.Core.StingLog.Error("RefnetRegistry.Load", ex); }
            return cat;
        }

        private static void Apply(JObject j, RefnetCatalogue cat)
        {
            var arr = j["joints"] as JArray;
            if (arr == null) return;
            foreach (var jt in arr.OfType<JObject>())
            {
                cat.Joints.Add(new RefnetJoint
                {
                    Id                = (string)jt["id"] ?? "",
                    VendorSeriesId    = (string)jt["vendorSeriesId"] ?? "",
                    MaxKw             = (double?)jt["maxKw"] ?? 0,
                    UpstreamGasOdMm   = (double?)jt["upstreamGasOdMm"] ?? 0,
                    DownstreamGasOdMm = (double?)jt["downstreamGasOdMm"] ?? 0,
                    UpstreamLiqOdMm   = (double?)jt["upstreamLiqOdMm"] ?? 0,
                    DownstreamLiqOdMm = (double?)jt["downstreamLiqOdMm"] ?? 0
                });
            }
        }
    }

    /// <summary>
    /// Logical refrigerant-tree node. Either an IDU leaf (CapacityKw set,
    /// no children) or an internal branch (children populated). The
    /// builder is up to the caller — RefnetTreeSizer doesn't care HOW
    /// the tree was built so long as the topology is correct.
    /// </summary>
    public class RefrigTreeNode
    {
        public string Id          { get; set; } = "";
        public string Label       { get; set; } = "";
        public double CapacityKw  { get; set; }     // only set on leaves (IDU)
        public List<RefrigTreeNode> Children { get; } = new();
        /// <summary>Selected REFNET joint (internal nodes only). Null on leaves.</summary>
        public RefnetJoint SelectedJoint { get; set; }
        /// <summary>Sum of downstream IDU capacities — populated during sizing.</summary>
        public double DownstreamKw { get; set; }
    }

    public class RefnetSizingResult
    {
        public int    JointsSized  { get; set; }
        public int    JointsMissed { get; set; }     // no catalogue match
        public double TotalSystemKw{ get; set; }
        public List<string> Warnings { get; } = new();
        public List<(string NodeId, double DownstreamKw, string SelectedJointId)> Trace { get; }
            = new();
    }

    public static class RefnetTreeSizer
    {
        /// <summary>
        /// Walk the tree (depth-first post-order) populating every
        /// internal node's DownstreamKw + SelectedJoint. Returns
        /// summary stats. Leaves are unchanged.
        /// </summary>
        public static RefnetSizingResult SizeTree(
            RefrigTreeNode root, string vendorSeriesId, RefnetCatalogue cat)
        {
            var r = new RefnetSizingResult();
            if (root == null || cat == null) return r;
            var pool = cat.ForVendor(vendorSeriesId).ToList();
            if (pool.Count == 0)
            {
                r.Warnings.Add($"No REFNET joints in catalogue for vendor '{vendorSeriesId}'.");
                return r;
            }
            r.TotalSystemKw = Walk(root, pool, r);
            return r;
        }

        private static double Walk(RefrigTreeNode node, List<RefnetJoint> pool, RefnetSizingResult r)
        {
            if (node == null) return 0;
            if (node.Children.Count == 0)
            {
                // Leaf — return its capacity (no joint needed).
                node.DownstreamKw = node.CapacityKw;
                return node.CapacityKw;
            }
            double sum = 0;
            foreach (var child in node.Children) sum += Walk(child, pool, r);
            node.DownstreamKw = sum;

            // Pick smallest joint whose maxKw ≥ sum.
            var match = pool.FirstOrDefault(j => j.MaxKw >= sum);
            if (match == null)
            {
                r.JointsMissed++;
                r.Warnings.Add($"Node '{node.Label ?? node.Id}': downstream {sum:F1} kW " +
                               $"exceeds largest catalogue joint ({pool.Last().MaxKw:F1} kW).");
                r.Trace.Add((node.Id, sum, null));
            }
            else
            {
                node.SelectedJoint = match;
                r.JointsSized++;
                r.Trace.Add((node.Id, sum, match.Id));
            }
            return sum;
        }
    }
}
