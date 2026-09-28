// StingTools — Scope-box planner · colours and defaults
//
// A scope box has no parameter to hold a colour, and Revit gives scope boxes
// no subcategories, so a colour cannot be STORED on one. It is DERIVED: from
// the box's name (its kind, size class, building, level) or, for a
// STING::<drawing-type> box, from that type's discipline. Nothing is recorded
// that could drift from the box — change the mode and every box recolours from
// what it already is.
//
// Corporate values: Data/STING_SCOPE_BOX_STYLE.json. A project file at
// _BIM_COORD/scope_box_style.json overrides any key it carries.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Drawing
{
    public enum ScopeBoxColourMode
    {
        /// <summary>Remove STING's colours.</summary>
        Off,
        /// <summary>By size class (A1-100, A1-50 …) — which boxes belong together.</summary>
        SizeClass,
        /// <summary>By discipline — for STING::&lt;type&gt; boxes the type's; for area boxes their class's first discipline.</summary>
        Discipline,
        /// <summary>By building code.</summary>
        Building,
        /// <summary>By level code (level-less boxes share one colour).</summary>
        Level,
        /// <summary>By kind: drawing-type / area / building / seed / plain.</summary>
        Kind,
    }

    public sealed class ScopeBoxStyle
    {
        [JsonProperty("defaults")]          public Defaults Values { get; set; } = new Defaults();
        [JsonProperty("disciplineColours")] public Dictionary<string, string> DisciplineColours { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("palette")]           public List<string> Palette { get; set; } = new List<string>();

        public sealed class Defaults
        {
            [JsonProperty("fitFactor")]  public double FitFactor { get; set; } = ScopeBoxSizing.DefaultFitFactor;
            [JsonProperty("overlapM")]   public double OverlapM { get; set; } = 2.0;
            [JsonProperty("paddingM")]   public double PaddingM { get; set; } = 1.0;
            [JsonProperty("lineWeight")] public int LineWeight { get; set; } = 6;
        }

        /// <summary>Corporate JSON with the project JSON laid over it key by key. Either may be null.</summary>
        public static ScopeBoxStyle Load(string corporateJson, string projectJson)
        {
            var merged = string.IsNullOrWhiteSpace(corporateJson) ? new JObject() : JObject.Parse(corporateJson);
            if (!string.IsNullOrWhiteSpace(projectJson))
                merged.Merge(JObject.Parse(projectJson), new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Replace,
                    MergeNullValueHandling = MergeNullValueHandling.Ignore,
                });
            var style = merged.ToObject<ScopeBoxStyle>() ?? new ScopeBoxStyle();
            style.DisciplineColours = new Dictionary<string, string>(style.DisciplineColours ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return style;
        }

        /// <summary>#RRGGBB → bytes. False for anything else, so a typo is reported rather than drawn black.</summary>
        public static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            var h = (hex ?? "").Trim().TrimStart('#');
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            r = (byte)(v >> 16); g = (byte)(v >> 8); b = (byte)v;
            return true;
        }

        /// <summary>
        /// A colour per key. Discipline codes use their own colour; everything else takes
        /// palette colours in sorted key order, so the same set of keys always gets the same
        /// colours and no two keys share one until the palette runs out.
        /// </summary>
        public Dictionary<string, string> Assign(ScopeBoxColourMode mode, IEnumerable<string> keys)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var distinct = (keys ?? Enumerable.Empty<string>())
                .Select(k => k ?? "").Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            int next = 0;
            foreach (var k in distinct)
            {
                if (mode == ScopeBoxColourMode.Discipline && DisciplineColours.TryGetValue(k, out var dc)) { result[k] = dc; continue; }
                if (Palette == null || Palette.Count == 0) continue;
                result[k] = Palette[next++ % Palette.Count];
            }
            return result;
        }
    }

    /// <summary>What a box is, as far as colouring needs to know. Built from its name.</summary>
    public sealed class ScopeBoxColourSubject
    {
        public string Name { get; set; }
        /// <summary>For STING::&lt;type&gt; boxes: that type's discipline. For area boxes: the class's first discipline.</summary>
        public string Discipline { get; set; }
        /// <summary>For area boxes: the size class from the saved plan.</summary>
        public string SizeClass { get; set; }
        /// <summary>For area boxes: the building code from the saved plan; null for model-wide boxes.</summary>
        public string Loc { get; set; }

        /// <summary>The key this box is coloured by under <paramref name="mode"/>; null means "leave it alone".</summary>
        public string KeyFor(ScopeBoxColourMode mode)
        {
            var kind = ScopeBoxNames.Classify(Name);
            switch (mode)
            {
                case ScopeBoxColourMode.Off: return null;
                case ScopeBoxColourMode.Kind: return kind.ToString();
                case ScopeBoxColourMode.Discipline: return Discipline;
                case ScopeBoxColourMode.SizeClass: return kind == ScopeBoxKind.Area ? SizeClass : null;
                case ScopeBoxColourMode.Building:
                    if (kind == ScopeBoxKind.Building) return Name.Substring(ScopeBoxNames.LocPrefix.Length);
                    // The building of an area box comes from the saved plan, not from
                    // parsing its code: "A1-100-03" and "ANNEX-A1-100-03" cannot be told
                    // apart by shape alone.
                    if (kind == ScopeBoxKind.Area) return string.IsNullOrWhiteSpace(Loc) ? "(model)" : Loc;
                    return null;
                case ScopeBoxColourMode.Level:
                    if (kind == ScopeBoxKind.Area && ScopeBoxNames.TryParseArea(Name, out _, out var lvl, out _)) return lvl ?? "(all levels)";
                    return null;
                default: return null;
            }
        }
    }
}
