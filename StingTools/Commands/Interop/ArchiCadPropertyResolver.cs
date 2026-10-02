// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Commands/Interop/ArchiCadPropertyResolver.cs
//
// Revit-free half of the ArchiCAD IFC property mapper: the mapping row as read from
// Data/IFC/ARCHICAD_IFC_MAPPING.json, and which rows supply a value for an element.
//
// Ordered fallback (DSCH-38 follow-up): several rows may name the same STING
// parameter. They are tried in FILE ORDER and the first row whose source is present
// on the element supplies the value; later rows for that parameter are fallbacks and
// do not overwrite it. This lets one parameter read the IFC4 / IFC4X3 buildingSMART
// property, then the IFC2X3 name of the same property, then a vendor-specific name,
// without guessing which schema or vendor wrote the file. The mapper logs which
// source matched, and a row carrying 'verify' (a source no primary document
// confirms) is reported when it is the one that matched.
//
// Before this change every row that had a value wrote it, so the LAST row won; the
// shipped map's overlapping groups were reordered in the same change so each group's
// winner is unchanged.
//
// Tested in StingTools.Tags.Tests (ArchiCadPropertyResolverTests).

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StingTools.Commands.Interop
{
    public sealed class AcIfcPropMapping
    {
        [JsonProperty("archicad_pset")]   public string ArchiCadPset  { get; set; } = "";
        [JsonProperty("archicad_prop")]   public string ArchiCadProp  { get; set; } = "";
        // DSCH round 3: Data/IFC/ARCHICAD_IFC_MAPPING.json (all 191 rows) and
        // docs/archicad-zone-mapping-guide.md spell these pset_name / property_name.
        // Only the two names above were bound, so Newtonsoft dropped both keys on every
        // row: ArchiCadProp was "", the scan looked for keys ending in "." and no
        // mapping ever wrote a value. Both spellings now bind.
        [JsonProperty("pset_name")]       private string PsetNameAlias { set => ArchiCadPset = value ?? ""; }
        [JsonProperty("property_name")]   private string PropertyNameAlias { set => ArchiCadProp = value ?? ""; }
        [JsonProperty("sting_param")]     public string StingParam    { get; set; } = "";
        [JsonProperty("revit_builtin")]   public string RevitBuiltIn  { get; set; } = "";
        [JsonProperty("notes")]           public string Notes         { get; set; } = "";
        /// <summary>Non-empty when no primary source confirms that this pset / property is
        /// written by the host it is meant for. Logged when the row is the one that matched.</summary>
        [JsonProperty("verify")]          public string Verify        { get; set; } = "";
        /// <summary>When non-empty, mapping only applies to IFC elements whose IfcType is in this list.</summary>
        [JsonProperty("element_types")]   public List<string> ElementTypes { get; set; } = new();
    }

    /// <summary>A mapping row that has a value on one element.</summary>
    public sealed class AcIfcPropCandidate
    {
        public AcIfcPropMapping Mapping { get; }
        public string Value { get; }
        /// <summary>The element property key the value came from ("Pset.Property").</summary>
        public string SourceKey { get; }
        /// <summary>What the row writes: the STING parameter, else the built-in.</summary>
        public string TargetKey => ArchiCadPropertyResolver.TargetKey(Mapping);

        public AcIfcPropCandidate(AcIfcPropMapping mapping, string value, string sourceKey)
        { Mapping = mapping; Value = value; SourceKey = sourceKey; }
    }

    public static class ArchiCadPropertyResolver
    {
        /// <summary>The target a row writes; rows with the same key are fallbacks for each other.</summary>
        public static string TargetKey(AcIfcPropMapping m) =>
            !string.IsNullOrEmpty(m.StingParam) ? m.StingParam : "builtin:" + m.RevitBuiltIn;

        public static bool AppliesTo(AcIfcPropMapping m, string ifcType)
        {
            if (m.ElementTypes == null || m.ElementTypes.Count == 0) return true;
            foreach (var t in m.ElementTypes)
                if (string.Equals(t, ifcType, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The row's value on this element, if its source is present and non-blank.
        /// An empty pset (or 'scan_all_psets' in notes) matches the property in any pset.</summary>
        public static bool TryRead(AcIfcPropMapping m, IReadOnlyDictionary<string, string> props,
                                   out string value, out string sourceKey)
        {
            value = null; sourceKey = null;
            if (string.IsNullOrEmpty(m.ArchiCadProp)) return false;
            bool scanAll = string.IsNullOrEmpty(m.ArchiCadPset)
                || (m.Notes ?? "").IndexOf("scan_all_psets", StringComparison.OrdinalIgnoreCase) >= 0;
            if (scanAll)
            {
                string suffix = "." + m.ArchiCadProp;
                foreach (var kv in props)
                    if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                    { value = kv.Value; sourceKey = kv.Key; return true; }
                return false;
            }
            string key = m.ArchiCadPset + "." + m.ArchiCadProp;
            if (props.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            { value = v; sourceKey = key; return true; }
            return false;
        }

        /// <summary>Every row that applies to the element and has a value, in file order.
        /// The caller writes the first candidate per <see cref="AcIfcPropCandidate.TargetKey"/>
        /// that it can, and treats the rest as unused fallbacks.</summary>
        public static IEnumerable<AcIfcPropCandidate> Candidates(
            IEnumerable<AcIfcPropMapping> mappings, string ifcType, IReadOnlyDictionary<string, string> props)
        {
            foreach (var m in mappings)
            {
                if (!AppliesTo(m, ifcType)) continue;
                if (TryRead(m, props, out var v, out var src))
                    yield return new AcIfcPropCandidate(m, v, src);
            }
        }

        /// <summary>The first candidate per target, in file order: the value each target
        /// gets when every write succeeds.</summary>
        public static List<AcIfcPropCandidate> FirstPerTarget(
            IEnumerable<AcIfcPropMapping> mappings, string ifcType, IReadOnlyDictionary<string, string> props)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<AcIfcPropCandidate>();
            foreach (var c in Candidates(mappings, ifcType, props))
                if (seen.Add(c.TargetKey)) list.Add(c);
            return list;
        }
    }
}
