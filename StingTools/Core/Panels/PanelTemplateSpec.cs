// PanelTemplateSpec — Revit-free model of STING_PANEL_SCHEDULE_SPECS.json.
//
// A spec describes one Revit PanelScheduleTemplate: its schedule type and
// configuration, the header (board identification — ISO 19650 tag, location,
// supply), the circuit table columns (BS 7671 circuit data) and the footer
// (totals and notes). PanelTemplateBuilder turns a spec into a real template
// with PanelScheduleTemplate.Create + SetTableData; this file holds everything
// that can be checked without Revit, so a bad spec fails a unit test instead of
// producing a silently empty column in a live model.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Panels
{
    public sealed class PanelTemplateField
    {
        /// <summary>Header/footer: label text shown beside the value.</summary>
        [JsonProperty("label")]    public string Label { get; set; } = "";
        /// <summary>Body: column heading.</summary>
        [JsonProperty("heading")]  public string Heading { get; set; } = "";
        /// <summary>"bip:NAME" for a BuiltInParameter, otherwise a shared parameter name.</summary>
        [JsonProperty("param")]    public string Param { get; set; } = "";
        /// <summary>Optional category override, e.g. "ProjectInformation".</summary>
        [JsonProperty("category")] public string Category { get; set; }
        [JsonProperty("widthMm")]  public double WidthMm { get; set; }

        [JsonIgnore] public bool IsBuiltIn => (Param ?? "").StartsWith("bip:", StringComparison.OrdinalIgnoreCase);
        /// <summary>The BuiltInParameter name, or the shared parameter name.</summary>
        [JsonIgnore] public string ParamName => IsBuiltIn ? Param.Substring(4).Trim() : (Param ?? "").Trim();
        [JsonIgnore] public string Text => string.IsNullOrEmpty(Label) ? Heading : Label;
    }

    public sealed class PanelTemplateSpec
    {
        [JsonProperty("name")]          public string Name { get; set; } = "";
        /// <summary>Branch | Switchboard | Data (Revit PanelScheduleType).</summary>
        [JsonProperty("scheduleType")]  public string ScheduleType { get; set; } = "Branch";
        /// <summary>OneColumn | TwoColumnsCircuitsAcross | TwoColumnsCircuitsDown.</summary>
        [JsonProperty("configuration")] public string Configuration { get; set; } = "OneColumn";
        [JsonProperty("title")]         public string Title { get; set; } = "";
        [JsonProperty("header")]        public List<PanelTemplateField> Header { get; set; } = new List<PanelTemplateField>();
        [JsonProperty("body")]          public List<PanelTemplateField> Body { get; set; } = new List<PanelTemplateField>();
        [JsonProperty("footer")]        public List<PanelTemplateField> Footer { get; set; } = new List<PanelTemplateField>();
        [JsonProperty("notes")]         public List<string> Notes { get; set; } = new List<string>();
    }

    public sealed class PanelTemplateSpecSet
    {
        [JsonProperty("version")]   public string Version { get; set; } = "";
        [JsonProperty("templates")] public List<PanelTemplateSpec> Templates { get; set; } = new List<PanelTemplateSpec>();

        public static readonly string[] ScheduleTypes = { "Branch", "Switchboard", "Data" };
        public static readonly string[] Configurations = { "OneColumn", "TwoColumnsCircuitsAcross", "TwoColumnsCircuitsDown" };

        public static PanelTemplateSpecSet Parse(string json)
        {
            var set = JsonConvert.DeserializeObject<PanelTemplateSpecSet>(json ?? "")
                      ?? new PanelTemplateSpecSet();
            set.Templates = set.Templates ?? new List<PanelTemplateSpec>();
            foreach (var t in set.Templates)
            {
                t.Header = t.Header ?? new List<PanelTemplateField>();
                t.Body   = t.Body   ?? new List<PanelTemplateField>();
                t.Footer = t.Footer ?? new List<PanelTemplateField>();
                t.Notes  = t.Notes  ?? new List<string>();
            }
            return set;
        }

        /// <summary>
        /// Corporate set with a project set layered over it: a project template
        /// replaces the corporate one of the same name; new names are added.
        /// </summary>
        public static PanelTemplateSpecSet Merge(PanelTemplateSpecSet corporate, PanelTemplateSpecSet project)
        {
            var merged = new PanelTemplateSpecSet { Version = corporate?.Version ?? "" };
            var byName = new Dictionary<string, PanelTemplateSpec>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var src in new[] { corporate, project })
            {
                if (src?.Templates == null) continue;
                foreach (var t in src.Templates)
                {
                    if (string.IsNullOrWhiteSpace(t?.Name)) continue;
                    if (!byName.ContainsKey(t.Name)) order.Add(t.Name);
                    byName[t.Name] = t;
                }
            }
            merged.Templates = order.Select(n => byName[n]).ToList();
            return merged;
        }

        /// <summary>
        /// Structural problems that make a spec unbuildable. Parameter existence
        /// and binding are checked by the unit tests (against the shipped data)
        /// and by the builder at run time (against the open model).
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Templates.Count == 0) problems.Add("no templates defined");
            foreach (var dup in Templates.GroupBy(t => t.Name ?? "", StringComparer.OrdinalIgnoreCase)
                                         .Where(g => g.Count() > 1))
                problems.Add($"template name '{dup.Key}' defined {dup.Count()} times");

            foreach (var t in Templates)
            {
                string n = string.IsNullOrWhiteSpace(t.Name) ? "(unnamed)" : t.Name;
                if (string.IsNullOrWhiteSpace(t.Name)) problems.Add("a template has no name");
                if (!ScheduleTypes.Contains(t.ScheduleType ?? "", StringComparer.Ordinal))
                    problems.Add($"{n}: scheduleType '{t.ScheduleType}' is not one of {string.Join("/", ScheduleTypes)}");
                if (!Configurations.Contains(t.Configuration ?? "", StringComparer.Ordinal))
                    problems.Add($"{n}: configuration '{t.Configuration}' is not one of {string.Join("/", Configurations)}");
                if (t.Body.Count == 0) problems.Add($"{n}: body has no columns");

                foreach (var (section, fields) in new[] { ("header", t.Header), ("body", t.Body), ("footer", t.Footer) })
                    foreach (var f in fields)
                    {
                        if (string.IsNullOrWhiteSpace(f.ParamName))
                            problems.Add($"{n}/{section}: '{f.Text}' has no param");
                        if (string.IsNullOrWhiteSpace(f.Text))
                            problems.Add($"{n}/{section}: param '{f.Param}' has no label/heading");
                        if (f.WidthMm < 0 || f.WidthMm > 300)
                            problems.Add($"{n}/{section}: '{f.Text}' width {f.WidthMm} mm is out of range");
                    }

                foreach (var dupCol in t.Body.GroupBy(f => f.Param ?? "", StringComparer.OrdinalIgnoreCase)
                                             .Where(g => g.Count() > 1))
                    problems.Add($"{n}/body: param '{dupCol.Key}' used in {dupCol.Count()} columns");
            }
            return problems;
        }
    }
}
