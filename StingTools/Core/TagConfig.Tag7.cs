using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

// TagConfig partial: TAG7 rich-narrative builder, per-segment styling,
// display presets, and warning evaluation. Relocated from TagConfig.cs to
// shrink the core file; same partial class, identical behaviour.

namespace StingTools.Core
{
    public static partial class TagConfig
    {
        // ═══════════════════════════════════════════════════════════════════════
        // TAG7: Rich Descriptive Narrative Builder with Markup & Sub-Sections
        // ═══════════════════════════════════════════════════════════════════════
        //
        // Formatting Strategy — exploiting 5 Revit output surfaces:
        //
        //  Surface              Bold  Italic  Underline  Color        How
        //  ───────────────────  ────  ──────  ─────────  ──────────   ──────────────────────────────
        //  Revit Parameters     NO    NO      NO         NO           Split into TAG7A-TAG7F sub-params
        //  TextNote+Formatted   YES   YES     YES        Per-type     FormattedText SetBold/Italic/Underline
        //  Tag Family Labels    YES   YES     NO         Per-label    Multi-label families reference sub-params
        //  WPF Dockable Panel   YES   YES     YES        Per-Run      TextBlock Inlines with Run elements
        //  HTML Export          YES   YES     YES        Per-span     Full CSS styling
        //
        // Markup tokens embedded in TAG7 (parsed by RichTagNote + WPF + HTML export):
        //   «H»text«/H»  — Header/emphasis (Bold + Underline in TextNote, Bold in WPF)
        //   «L»text«/L»  — Label text (Italic in TextNote, muted color in WPF)
        //   «V»text«/V»  — Value text (Normal weight, accent color in WPF/HTML)
        //   «S»text«/S»  — Section separator (pipe "|" with spacing)
        //   «C»text«/C»  — Connector phrase (prose joining words between sections)
        //
        // Sub-section parameters (TAG7A-TAG7F) hold PLAIN text versions for
        // tag family labels. TAG7 holds the MARKED-UP full narrative.
        // ═══════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════
        // TAG1-TAG6 Segment Styling — Per-Segment Color and Style Definitions
        //
        // The 8-segment tag format (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ) can
        // be styled per segment for rich rendering via TextNote, HTML export,
        // and WPF panel. Each segment gets a distinct color and font style
        // enabling instant visual parsing of the tag structure.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Style definition for a single tag segment (DISC, LOC, ZONE, etc.).</summary>
        public class TagSegmentStyle
        {
            /// <summary>Segment position index (0-7).</summary>
            public int Index { get; set; }
            /// <summary>Short segment name (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ).</summary>
            public string Name { get; set; }
            /// <summary>Full human-readable description.</summary>
            public string Description { get; set; }
            /// <summary>Hex color for rich rendering.</summary>
            public string Color { get; set; }
            /// <summary>Bold rendering hint.</summary>
            public bool Bold { get; set; }
            /// <summary>Italic rendering hint.</summary>
            public bool Italic { get; set; }
        }

        /// <summary>
        /// Default styles for each of the 8 tag segments.
        /// Used by RichTagNote, HTML export, and WPF panel for segment-aware coloring.
        /// </summary>
        public static readonly TagSegmentStyle[] SegmentStyles = new[]
        {
            new TagSegmentStyle { Index = 0, Name = "DISC", Description = "Discipline",      Color = "#1565C0", Bold = true,  Italic = false },
            new TagSegmentStyle { Index = 1, Name = "LOC",  Description = "Location",         Color = "#2E7D32", Bold = false, Italic = false },
            new TagSegmentStyle { Index = 2, Name = "ZONE", Description = "Zone",              Color = "#E65100", Bold = false, Italic = false },
            new TagSegmentStyle { Index = 3, Name = "LVL",  Description = "Level",             Color = "#6A1B9A", Bold = false, Italic = false },
            new TagSegmentStyle { Index = 4, Name = "SYS",  Description = "System Type",       Color = "#C62828", Bold = true,  Italic = false },
            new TagSegmentStyle { Index = 5, Name = "FUNC", Description = "Function",          Color = "#00838F", Bold = false, Italic = true  },
            new TagSegmentStyle { Index = 6, Name = "PROD", Description = "Product Code",      Color = "#4527A0", Bold = true,  Italic = false },
            new TagSegmentStyle { Index = 7, Name = "SEQ",  Description = "Sequence Number",   Color = "#37474F", Bold = false, Italic = false },
        };

        /// <summary>
        /// Result of parsing a TAG1-TAG6 value into styled segments.
        /// Each segment has text, style, and whether it was populated.
        /// </summary>
        public class TagSegmentResult
        {
            /// <summary>The full tag string (e.g. "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003").</summary>
            public string FullTag { get; set; } = "";
            /// <summary>Individual segment values in order (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ).</summary>
            public string[] Segments { get; set; } = new string[8];
            /// <summary>Whether each segment is populated (non-empty and not a placeholder).</summary>
            public bool[] Populated { get; set; } = new bool[8];
            /// <summary>Marked-up tag with segment color tokens: «D0»DISC«/D0» «S»-«/S» «D1»LOC«/D1» ...</summary>
            public string MarkedUpTag { get; set; } = "";
        }

        /// <summary>
        /// Parse a tag string (TAG1-TAG6) into styled segments.
        /// Returns segment data for rich rendering.
        /// </summary>
        public static TagSegmentResult ParseTagSegments(string tagValue)
        {
            var result = new TagSegmentResult { FullTag = tagValue ?? "" };
            if (string.IsNullOrEmpty(tagValue)) return result;

            string[] parts = tagValue.Split(new[] { Separator }, StringSplitOptions.None);
            var marked = new System.Text.StringBuilder();

            for (int i = 0; i < 8; i++)
            {
                string val = i < parts.Length ? parts[i] : "";
                result.Segments[i] = val;
                result.Populated[i] = !string.IsNullOrEmpty(val) && val != "XX" && val != "ZZ" && val != "0000";

                if (i > 0) marked.Append($"\u00ABS\u00BB{Separator}\u00AB/S\u00BB");
                marked.Append($"\u00ABD{i}\u00BB{val}\u00AB/D{i}\u00BB");
            }

            result.MarkedUpTag = marked.ToString();
            return result;
        }

        /// <summary>
        /// Parse segment markup tokens from a marked-up tag string.
        /// Returns list of (text, segmentIndex) tuples where segmentIndex is 0-7 for segments,
        /// -1 for separators, -2 for plain text.
        /// </summary>
        public static List<(string text, int segmentIndex)> ParseSegmentMarkup(string marked)
        {
            var result = new List<(string text, int segmentIndex)>();
            if (string.IsNullOrEmpty(marked)) return result;

            int pos = 0;
            var plain = new System.Text.StringBuilder();

            while (pos < marked.Length)
            {
                if (pos + 3 < marked.Length && marked[pos] == '\u00AB')
                {
                    // Flush plain text
                    if (plain.Length > 0)
                    {
                        result.Add((plain.ToString(), -2));
                        plain.Clear();
                    }

                    int tagEnd = marked.IndexOf('\u00BB', pos);
                    if (tagEnd > pos)
                    {
                        string tag = marked.Substring(pos + 1, tagEnd - pos - 1);

                        if (tag == "S")
                        {
                            // Separator
                            string closeTag = "\u00AB/S\u00BB";
                            int closeIdx = marked.IndexOf(closeTag, tagEnd + 1);
                            if (closeIdx > tagEnd)
                            {
                                string content = marked.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                result.Add((content, -1));
                                pos = closeIdx + closeTag.Length;
                                continue;
                            }
                        }
                        else if (tag.Length == 2 && tag[0] == 'D' && char.IsDigit(tag[1]))
                        {
                            int segIdx = tag[1] - '0';
                            string closeTag = $"\u00AB/D{segIdx}\u00BB";
                            int closeIdx = marked.IndexOf(closeTag, tagEnd + 1);
                            if (closeIdx > tagEnd)
                            {
                                string content = marked.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                result.Add((content, segIdx));
                                pos = closeIdx + closeTag.Length;
                                continue;
                            }
                        }

                        // Fallback: skip the opening tag
                        pos = tagEnd + 1;
                        continue;
                    }
                }

                plain.Append(marked[pos]);
                pos++;
            }

            if (plain.Length > 0)
                result.Add((plain.ToString(), -2));

            return result;
        }

        /// <summary>
        /// Result of building TAG7 narrative — contains the full marked-up narrative
        /// plus individual plain-text sections for TAG7A-TAG7F sub-parameters.
        /// </summary>
        public class Tag7Result
        {
            /// <summary>Full narrative with markup tokens (for TAG7 parameter + rich rendering).</summary>
            public string MarkedUpNarrative { get; set; } = "";
            /// <summary>Full narrative without markup (plain text fallback).</summary>
            public string PlainNarrative { get; set; } = "";
            /// <summary>Section A: Identity Header — asset name, product, manufacturer (plain).</summary>
            public string SectionA { get; set; } = "";
            /// <summary>Section B: System &amp; Function Context (plain).</summary>
            public string SectionB { get; set; } = "";
            /// <summary>Section C: Spatial Context — room, department, grid (plain).</summary>
            public string SectionC { get; set; } = "";
            /// <summary>Section D: Lifecycle &amp; Status (plain).</summary>
            public string SectionD { get; set; } = "";
            /// <summary>Section E: Technical Specifications (plain).</summary>
            public string SectionE { get; set; } = "";
            /// <summary>Section F: Classification &amp; Reference (plain).</summary>
            public string SectionF { get; set; } = "";

            /// <summary>
            /// All 6 sections as an array (A-F), matching TAG7Sections order.
            /// Phase 165 perf — array is materialised once on first read and
            /// cached on the instance. Tag7Result is per-tagging-call so the
            /// cache lives only as long as the surrounding write.
            /// </summary>
            public string[] AllSections =>
                _allSectionsCache ??= new[] { SectionA, SectionB, SectionC, SectionD, SectionE, SectionF };
            private string[] _allSectionsCache;

            // ─── T4-T10 tier summaries (Phase 165 — tagging workflow repair) ───
            // Each is a single-line formatted summary built from the relevant
            // shared parameters. Empty string means the element carries no data
            // for that tier — the writer skips those tiers silently.

            /// <summary>T4: Commissioning &amp; handover summary.</summary>
            public string SectionT4 { get; set; } = "";
            /// <summary>T5: Cost &amp; procurement summary (UGX/USD/install hrs/labour).</summary>
            public string SectionT5 { get; set; } = "";
            /// <summary>T6: Carbon &amp; sustainability summary (A1-A3, A4, B6, C-stages).</summary>
            public string SectionT6 { get; set; } = "";
            /// <summary>T7: Fabrication &amp; QC summary (spool / status / inspector).</summary>
            public string SectionT7 { get; set; } = "";
            /// <summary>T8: Clash triage &amp; resolution summary.</summary>
            public string SectionT8 { get; set; } = "";
            /// <summary>T9: As-built reconciliation &amp; model-health summary.</summary>
            public string SectionT9 { get; set; } = "";
            /// <summary>T10: Compliance / audit-trail summary (IFC PSet / ACC).</summary>
            public string SectionT10 { get; set; } = "";

            /// <summary>
            /// T4..T10 summaries indexed 0..6 (i.e. Tier4Summaries[0] == SectionT4).
            /// Phase 165 perf — cached on first read, identical lifetime to AllSections.
            /// </summary>
            public string[] Tier4Summaries => _tier4SummariesCache ??= new[]
            {
                SectionT4, SectionT5, SectionT6, SectionT7, SectionT8, SectionT9, SectionT10
            };
            private string[] _tier4SummariesCache;
        }

        /// <summary>
        /// Section style definitions for rich rendering.
        /// Each section has a name, color (hex), and font style hint.
        /// Used by RichTagNoteCommand, WPF panel, and HTML export.
        /// </summary>
        public static readonly Tag7SectionStyle[] SectionStyles = new[]
        {
            new Tag7SectionStyle { Key = "A", Name = "Identity",       Color = "#1565C0", Bold = true,  Italic = false, Underline = true  },
            new Tag7SectionStyle { Key = "B", Name = "System",         Color = "#2E7D32", Bold = false, Italic = true,  Underline = false },
            new Tag7SectionStyle { Key = "C", Name = "Spatial",        Color = "#E65100", Bold = false, Italic = false, Underline = false },
            new Tag7SectionStyle { Key = "D", Name = "Lifecycle",      Color = "#C62828", Bold = false, Italic = false, Underline = false },
            new Tag7SectionStyle { Key = "E", Name = "Technical",      Color = "#6A1B9A", Bold = true,  Italic = false, Underline = false },
            new Tag7SectionStyle { Key = "F", Name = "Classification", Color = "#37474F", Bold = false, Italic = true,  Underline = false },
        };

        /// <summary>Style definition for a TAG7 narrative section.</summary>
        public class Tag7SectionStyle
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string Color { get; set; }
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public bool Underline { get; set; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAG7 Display Presets — Configurable Color/Style Schemes
        //
        // Each preset defines how TAG7 sections are presented based on context:
        //   - By Discipline: M=Blue, E=Yellow, P=Green headers
        //   - By Status: NEW=Green, EXISTING=Blue, DEMOLISHED=Red
        //   - By System: HVAC=Orange, Electrical=Yellow, Plumbing=Green
        //   - By Completeness: Full=Green, Partial=Orange, Missing=Red
        //   - By Priority: Critical=Red, Standard=Blue, Low=Grey
        //   - Monochrome: Print-ready black/grey scheme
        //   - Accessible: Colorblind-safe palette
        //
        // Each preset maps a discriminator value (discipline code, status, etc.)
        // to a Tag7DisplayStyle containing header color, section colors, and
        // font style overrides.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Display style for a TAG7 rendering — applied per-element based on
        /// the active preset and the element's discriminator value.
        /// </summary>
        public class Tag7DisplayStyle
        {
            /// <summary>Primary color for card header / element highlight.</summary>
            public string HeaderColor { get; set; }
            /// <summary>Background tint for the card body.</summary>
            public string BackgroundTint { get; set; }
            /// <summary>Override colors for sections A-F (null = use default SectionStyles).</summary>
            public string[] SectionColors { get; set; }
            /// <summary>Sections to render in bold (overrides default).</summary>
            public bool[] BoldOverrides { get; set; }
            /// <summary>Sections to show/hide (true = show, false = hide).</summary>
            public bool[] SectionVisibility { get; set; }
            /// <summary>Human-readable label for this style.</summary>
            public string Label { get; set; }
        }

        /// <summary>
        /// A TAG7 display preset — a named scheme mapping discriminator values
        /// to display styles. Used by RichTagNote, HTML export, and WPF panel.
        /// </summary>
        public class Tag7DisplayPreset
        {
            /// <summary>Unique preset name (e.g. "Discipline", "Status", "System").</summary>
            public string Name { get; set; }
            /// <summary>Human-readable description.</summary>
            public string Description { get; set; }
            /// <summary>Which element attribute to discriminate on.</summary>
            public string DiscriminatorParam { get; set; }
            /// <summary>Mapping of discriminator value → display style.</summary>
            public Dictionary<string, Tag7DisplayStyle> Styles { get; set; }
            /// <summary>Fallback style when discriminator value doesn't match.</summary>
            public Tag7DisplayStyle DefaultStyle { get; set; }
        }

        /// <summary>Active preset (changed by user via command or panel).
        /// GAP-009: Lazily restores from persisted name on first access.</summary>
        private static Tag7DisplayPreset _activePreset;
        public static Tag7DisplayPreset ActivePreset
        {
            get
            {
                if (_activePreset == null && !string.IsNullOrEmpty(_activePresetName))
                {
                    var preset = BuiltInPresets.FirstOrDefault(p =>
                        p.Name.Equals(_activePresetName, StringComparison.OrdinalIgnoreCase));
                    if (preset != null) _activePreset = preset;
                    else _activePresetName = null; // Invalid preset name, clear it
                }
                return _activePreset;
            }
            set { _activePreset = value; }
        }

        /// <summary>Get the display style for an element based on the active preset.</summary>
        public static Tag7DisplayStyle GetDisplayStyle(Element el)
        {
            if (ActivePreset == null) return null;

            string value = ParameterHelpers.GetString(el, ActivePreset.DiscriminatorParam);
            if (!string.IsNullOrEmpty(value) && ActivePreset.Styles.TryGetValue(value, out var style))
                return style;

            return ActivePreset.DefaultStyle;
        }

        /// <summary>All built-in TAG7 display presets.</summary>
        public static readonly Tag7DisplayPreset[] BuiltInPresets = BuildPresets();

        private static Tag7DisplayPreset[] BuildPresets()
        {
            var all6Visible = new bool[] { true, true, true, true, true, true };
            var defaultBold = new bool[] { true, false, false, false, true, false };

            return new[]
            {
                // ── Preset 1: By Discipline ──────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Discipline",
                    Description = "Color-code by discipline: Mechanical=Blue, Electrical=Amber, Plumbing=Green, etc.",
                    DiscriminatorParam = ParamRegistry.DISC,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "M",  new Tag7DisplayStyle { HeaderColor = "#1565C0", BackgroundTint = "#E3F2FD", Label = "Mechanical",
                            SectionColors = new[] { "#1565C0", "#1976D2", "#1E88E5", "#42A5F5", "#0D47A1", "#1565C0" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "E",  new Tag7DisplayStyle { HeaderColor = "#F9A825", BackgroundTint = "#FFFDE7", Label = "Electrical",
                            SectionColors = new[] { "#F9A825", "#FBC02D", "#FDD835", "#FFD54F", "#F57F17", "#F9A825" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "P",  new Tag7DisplayStyle { HeaderColor = "#2E7D32", BackgroundTint = "#E8F5E9", Label = "Plumbing",
                            SectionColors = new[] { "#2E7D32", "#388E3C", "#43A047", "#66BB6A", "#1B5E20", "#2E7D32" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "A",  new Tag7DisplayStyle { HeaderColor = "#757575", BackgroundTint = "#F5F5F5", Label = "Architectural",
                            SectionColors = new[] { "#616161", "#757575", "#9E9E9E", "#BDBDBD", "#424242", "#616161" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "S",  new Tag7DisplayStyle { HeaderColor = "#C62828", BackgroundTint = "#FFEBEE", Label = "Structural",
                            SectionColors = new[] { "#C62828", "#D32F2F", "#E53935", "#EF5350", "#B71C1C", "#C62828" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FP", new Tag7DisplayStyle { HeaderColor = "#E65100", BackgroundTint = "#FFF3E0", Label = "Fire Protection",
                            SectionColors = new[] { "#E65100", "#EF6C00", "#F57C00", "#FB8C00", "#BF360C", "#E65100" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "LV", new Tag7DisplayStyle { HeaderColor = "#6A1B9A", BackgroundTint = "#F3E5F5", Label = "Low Voltage",
                            SectionColors = new[] { "#6A1B9A", "#7B1FA2", "#8E24AA", "#AB47BC", "#4A148C", "#6A1B9A" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "G",  new Tag7DisplayStyle { HeaderColor = "#795548", BackgroundTint = "#EFEBE9", Label = "Gas",
                            SectionColors = new[] { "#795548", "#8D6E63", "#A1887F", "#BCAAA4", "#4E342E", "#795548" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#455A64", BackgroundTint = "#ECEFF1", Label = "Unknown",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 2: By Status ──────────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Status",
                    Description = "Color-code by lifecycle status: NEW=Green, EXISTING=Blue, DEMOLISHED=Red, TEMPORARY=Orange",
                    DiscriminatorParam = ParamRegistry.STATUS,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "NEW",         new Tag7DisplayStyle { HeaderColor = "#2E7D32", BackgroundTint = "#E8F5E9", Label = "New Construction",
                            SectionColors = new[] { "#2E7D32", "#388E3C", "#2E7D32", "#43A047", "#2E7D32", "#388E3C" },
                            BoldOverrides = new[] { true, false, false, true, true, false }, SectionVisibility = all6Visible } },
                        { "EXISTING",    new Tag7DisplayStyle { HeaderColor = "#1565C0", BackgroundTint = "#E3F2FD", Label = "Existing Asset",
                            SectionColors = new[] { "#1565C0", "#1976D2", "#1565C0", "#42A5F5", "#1565C0", "#1976D2" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "DEMOLISHED",  new Tag7DisplayStyle { HeaderColor = "#C62828", BackgroundTint = "#FFEBEE", Label = "Demolished",
                            SectionColors = new[] { "#C62828", "#D32F2F", "#C62828", "#EF5350", "#C62828", "#D32F2F" },
                            BoldOverrides = new[] { true, false, false, true, false, false },
                            SectionVisibility = new[] { true, true, true, true, false, true } } },
                        { "TEMPORARY",   new Tag7DisplayStyle { HeaderColor = "#E65100", BackgroundTint = "#FFF3E0", Label = "Temporary",
                            SectionColors = new[] { "#E65100", "#EF6C00", "#E65100", "#FB8C00", "#E65100", "#EF6C00" },
                            BoldOverrides = new[] { true, false, false, true, false, false }, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#757575", BackgroundTint = "#FAFAFA", Label = "No Status",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 3: By System ──────────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "System",
                    Description = "Color-code by system type: HVAC=Blue, DCW=Cyan, HWS=Red, SAN=Brown, LV=Amber, FP=Orange",
                    DiscriminatorParam = ParamRegistry.SYS,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "HVAC", new Tag7DisplayStyle { HeaderColor = "#1565C0", BackgroundTint = "#E3F2FD", Label = "HVAC",
                            SectionColors = new[] { "#1565C0", "#0D47A1", "#1565C0", "#1976D2", "#0D47A1", "#1565C0" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "DCW",  new Tag7DisplayStyle { HeaderColor = "#00838F", BackgroundTint = "#E0F7FA", Label = "Domestic Cold Water",
                            SectionColors = new[] { "#00838F", "#006064", "#00838F", "#0097A7", "#006064", "#00838F" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "HWS",  new Tag7DisplayStyle { HeaderColor = "#D32F2F", BackgroundTint = "#FFEBEE", Label = "Hot Water Supply",
                            SectionColors = new[] { "#D32F2F", "#C62828", "#D32F2F", "#E53935", "#C62828", "#D32F2F" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "SAN",  new Tag7DisplayStyle { HeaderColor = "#6D4C41", BackgroundTint = "#EFEBE9", Label = "Sanitary",
                            SectionColors = new[] { "#6D4C41", "#5D4037", "#6D4C41", "#795548", "#5D4037", "#6D4C41" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "LV",   new Tag7DisplayStyle { HeaderColor = "#F9A825", BackgroundTint = "#FFFDE7", Label = "Low Voltage",
                            SectionColors = new[] { "#F9A825", "#F57F17", "#F9A825", "#FBC02D", "#F57F17", "#F9A825" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FP",   new Tag7DisplayStyle { HeaderColor = "#E65100", BackgroundTint = "#FFF3E0", Label = "Fire Protection",
                            SectionColors = new[] { "#E65100", "#BF360C", "#E65100", "#EF6C00", "#BF360C", "#E65100" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FLS",  new Tag7DisplayStyle { HeaderColor = "#FF6F00", BackgroundTint = "#FFF8E1", Label = "Fire Life Safety",
                            SectionColors = new[] { "#FF6F00", "#E65100", "#FF6F00", "#FF8F00", "#E65100", "#FF6F00" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#546E7A", BackgroundTint = "#ECEFF1", Label = "Other System",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 4: By Completeness ────────────────────────────────
                // Discriminates on TAG1 presence and section fill rate
                new Tag7DisplayPreset
                {
                    Name = "Completeness",
                    Description = "RAG status: Green=Complete (all 8 tokens), Orange=Partial, Red=Missing critical tokens",
                    DiscriminatorParam = "_COMPLETENESS_", // Special: computed by GetDisplayStyle override
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "COMPLETE",    new Tag7DisplayStyle { HeaderColor = "#2E7D32", BackgroundTint = "#E8F5E9", Label = "Complete",
                            SectionColors = new[] { "#2E7D32", "#388E3C", "#2E7D32", "#43A047", "#2E7D32", "#388E3C" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "PARTIAL",     new Tag7DisplayStyle { HeaderColor = "#F9A825", BackgroundTint = "#FFFDE7", Label = "Partial",
                            SectionColors = new[] { "#F9A825", "#FBC02D", "#F9A825", "#FDD835", "#F9A825", "#FBC02D" },
                            BoldOverrides = new[] { true, false, false, true, false, true }, SectionVisibility = all6Visible } },
                        { "INCOMPLETE",  new Tag7DisplayStyle { HeaderColor = "#C62828", BackgroundTint = "#FFEBEE", Label = "Incomplete",
                            SectionColors = new[] { "#C62828", "#D32F2F", "#C62828", "#EF5350", "#C62828", "#D32F2F" },
                            BoldOverrides = new[] { true, false, false, true, false, true }, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#9E9E9E", BackgroundTint = "#FAFAFA", Label = "Untagged",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 5: Monochrome (Print-Ready) ───────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Monochrome",
                    Description = "Print-friendly black/grey scheme with no color — suitable for B&W printing",
                    DiscriminatorParam = "_ALWAYS_DEFAULT_",
                    Styles = new Dictionary<string, Tag7DisplayStyle>(),
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#212121", BackgroundTint = "#FAFAFA", Label = "Asset",
                        SectionColors = new[] { "#212121", "#424242", "#616161", "#757575", "#212121", "#424242" },
                        BoldOverrides = new[] { true, false, false, false, true, true }, SectionVisibility = all6Visible },
                },

                // ── Preset 6: Accessible (Colorblind-Safe) ──────────────────
                new Tag7DisplayPreset
                {
                    Name = "Accessible",
                    Description = "Colorblind-safe palette using blue/orange contrast (deuteranopia/protanopia friendly)",
                    DiscriminatorParam = ParamRegistry.DISC,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "M",  new Tag7DisplayStyle { HeaderColor = "#0072B2", BackgroundTint = "#E1F5FE", Label = "Mechanical",
                            SectionColors = new[] { "#0072B2", "#0072B2", "#0072B2", "#0072B2", "#0072B2", "#0072B2" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "E",  new Tag7DisplayStyle { HeaderColor = "#E69F00", BackgroundTint = "#FFF8E1", Label = "Electrical",
                            SectionColors = new[] { "#E69F00", "#E69F00", "#E69F00", "#E69F00", "#E69F00", "#E69F00" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "P",  new Tag7DisplayStyle { HeaderColor = "#009E73", BackgroundTint = "#E0F2F1", Label = "Plumbing",
                            SectionColors = new[] { "#009E73", "#009E73", "#009E73", "#009E73", "#009E73", "#009E73" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "A",  new Tag7DisplayStyle { HeaderColor = "#56B4E9", BackgroundTint = "#E1F5FE", Label = "Architectural",
                            SectionColors = new[] { "#56B4E9", "#56B4E9", "#56B4E9", "#56B4E9", "#56B4E9", "#56B4E9" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FP", new Tag7DisplayStyle { HeaderColor = "#D55E00", BackgroundTint = "#FBE9E7", Label = "Fire Protection",
                            SectionColors = new[] { "#D55E00", "#D55E00", "#D55E00", "#D55E00", "#D55E00", "#D55E00" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#CC79A7", BackgroundTint = "#FCE4EC", Label = "Other",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 7: Technical Focus ────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Technical Focus",
                    Description = "Emphasize Technical (E) and Classification (F) sections, dim Identity. For engineering review.",
                    DiscriminatorParam = "_ALWAYS_DEFAULT_",
                    Styles = new Dictionary<string, Tag7DisplayStyle>(),
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#6A1B9A", BackgroundTint = "#F3E5F5", Label = "Engineering Review",
                        SectionColors = new[] { "#9E9E9E", "#9E9E9E", "#9E9E9E", "#757575", "#6A1B9A", "#1565C0" },
                        BoldOverrides = new[] { false, false, false, false, true, true },
                        SectionVisibility = new[] { true, true, false, true, true, true } },
                },
            };
        }

        /// <summary>
        /// Get display style with completeness-aware discrimination.
        /// When the active preset discriminates on "_COMPLETENESS_", computes
        /// the completeness level from the element's token fill rate.
        /// </summary>
        public static Tag7DisplayStyle GetDisplayStyleSmart(Element el)
        {
            if (ActivePreset == null) return null;

            // Special computed discriminators
            if (ActivePreset.DiscriminatorParam == "_COMPLETENESS_")
            {
                string[] tokens = ParamRegistry.ReadTokenValues(el);
                int filled = tokens.Count(t => !string.IsNullOrEmpty(t) && t != "XX" && t != "ZZ");
                string level = filled >= 8 ? "COMPLETE" : filled >= 5 ? "PARTIAL" : "INCOMPLETE";
                if (ActivePreset.Styles.TryGetValue(level, out var style))
                    return style;
                return ActivePreset.DefaultStyle;
            }

            if (ActivePreset.DiscriminatorParam == "_ALWAYS_DEFAULT_")
                return ActivePreset.DefaultStyle;

            // Standard parameter-based discrimination
            string value = ParameterHelpers.GetString(el, ActivePreset.DiscriminatorParam);
            if (!string.IsNullOrEmpty(value) && ActivePreset.Styles.TryGetValue(value, out var s))
                return s;

            return ActivePreset.DefaultStyle;
        }

        /// <summary>Set the active preset by name. Returns true if found.
        /// GAP-009: Also persists the preset name so it survives Revit restart.</summary>
        public static bool SetActivePreset(string presetName)
        {
            var preset = BuiltInPresets.FirstOrDefault(p =>
                p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                ActivePreset = preset;
                _activePresetName = presetName;
                PersistPresetName(presetName);
                return true;
            }
            return false;
        }

        /// <summary>The stored preset name for config persistence.</summary>
        private static string _activePresetName;

        /// <summary>GAP-009: Persist preset name to project_config.json.</summary>
        private static void PersistPresetName(string presetName)
        {
            try
            {
                string configPath = StingToolsApp.FindDataFile("project_config.json");
                if (string.IsNullOrEmpty(configPath)) return;

                string json = File.ReadAllText(configPath);
                Dictionary<string, object> data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                    ?? new Dictionary<string, object>();
                data["ACTIVE_PRESET"] = presetName;
                File.WriteAllText(configPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to persist preset name: {ex.Message}");
            }
        }

        /// <summary>Strip all markup tokens from a string, returning plain text.</summary>
        public static string StripMarkup(string marked)
        {
            if (string.IsNullOrEmpty(marked)) return "";
            return marked
                .Replace("«H»", "").Replace("«/H»", "")
                .Replace("«L»", "").Replace("«/L»", "")
                .Replace("«V»", "").Replace("«/V»", "")
                .Replace("«S»", "").Replace("«/S»", "")
                .Replace("«C»", "").Replace("«/C»", "");
        }

        /// <summary>
        /// Parse markup tokens from TAG7 text into styled segments.
        /// Returns a list of (text, style) tuples where style is "H", "L", "V", "S", or "" (plain).
        /// Used by WPF panel and HTML export for rich rendering.
        /// </summary>
        public static List<(string text, string style)> ParseMarkup(string marked)
        {
            var result = new List<(string text, string style)>();
            if (string.IsNullOrEmpty(marked)) return result;

            int i = 0;
            var plain = new System.Text.StringBuilder();

            while (i < marked.Length)
            {
                // Check for markup token start
                if (i + 2 < marked.Length && marked[i] == '\u00AB') // «
                {
                    // Flush any accumulated plain text
                    if (plain.Length > 0)
                    {
                        result.Add((plain.ToString(), ""));
                        plain.Clear();
                    }

                    // Find the style character and closing »
                    int tagEnd = marked.IndexOf('\u00BB', i); // »
                    if (tagEnd > i)
                    {
                        string tag = marked.Substring(i + 1, tagEnd - i - 1);
                        if (tag.Length == 1 && "HLVS".Contains(tag))
                        {
                            // Opening tag — find matching close
                            string closeTag = $"\u00AB/{tag}\u00BB";
                            int closeIdx = marked.IndexOf(closeTag, tagEnd + 1);
                            if (closeIdx > tagEnd)
                            {
                                string content = marked.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                result.Add((content, tag));
                                i = closeIdx + closeTag.Length;
                                continue;
                            }
                        }
                        else if (tag.StartsWith("/"))
                        {
                            // Orphan close tag — skip
                            i = tagEnd + 1;
                            continue;
                        }
                    }
                }

                plain.Append(marked[i]);
                i++;
            }

            if (plain.Length > 0)
                result.Add((plain.ToString(), ""));

            return result;
        }

        /// <summary>Full discipline name for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> DisciplineDescriptions = new Dictionary<string, string>
        {
            { "M", "Mechanical" }, { "E", "Electrical" }, { "P", "Plumbing" },
            { "A", "Architectural" }, { "S", "Structural" }, { "FP", "Fire Protection" },
            { "LV", "Low Voltage" }, { "G", "Gas" }, { "GEN", "General" },
            { "H", "Healthcare" }, { "MG", "Medical Gas" }, { "RP", "Radiation Protection" },
        };

        /// <summary>Full system name for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> SystemDescriptions = new Dictionary<string, string>
        {
            { "HVAC", "Heating Ventilation and Air Conditioning" },
            { "HWS", "Hot Water Supply" }, { "DHW", "Domestic Hot Water" },
            { "DCW", "Domestic Cold Water" }, { "SAN", "Sanitary Drainage" },
            { "RWD", "Rainwater Drainage" }, { "GAS", "Gas Supply" },
            { "FP", "Fire Protection" }, { "FLS", "Fire Life Safety" },
            { "LV", "Low Voltage Distribution" }, { "SEC", "Security Systems" },
            { "ICT", "Information and Communications Technology" },
            { "COM", "Communications" }, { "NCL", "Nurse Call Systems" },
            { "ARC", "Architectural Fabric" }, { "STR", "Structural Elements" },
            { "GEN", "General Services" },
            // Healthcare Pack (Phase H-1)
            { "MGS-O2", "Medical Oxygen Supply" }, { "MGS-AIR", "Medical Compressed Air" },
            { "MGS-VAC", "Medical Vacuum" }, { "MGS-N2O", "Nitrous Oxide Supply" },
            { "MGS-CO2", "Carbon Dioxide Supply" }, { "MGS-N2", "Nitrogen Supply" },
            { "MGS-AGS", "Anaesthetic Gas Scavenging" },
            { "EES-LS", "Essential Electrical Services (Life Safety)" },
            { "EES-CR", "Essential Electrical Services (Critical)" },
            { "EES-EB", "Essential Electrical Services (Enhanced)" },
            { "LPS", "Lightning Protection System" },
            { "CLN", "Clinical Environment" }, { "RAD", "Radiation Shielding" },
        };

        /// <summary>Full function description for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> FunctionDescriptions = new Dictionary<string, string>
        {
            { "SUP", "Supply" }, { "RTN", "Return" }, { "EXH", "Exhaust" },
            { "FRA", "Fresh Air Intake" }, { "HTG", "Heating" },
            { "DHW", "Domestic Hot Water Distribution" },
            { "DCW", "Domestic Cold Water Distribution" },
            { "SAN", "Sanitary Waste Disposal" }, { "RWD", "Rainwater Disposal" },
            { "GAS", "Gas Distribution" }, { "FP", "Fire Protection Suppression" },
            { "FLS", "Fire Detection and Alarm" },
            { "PWR", "Power Distribution" }, { "LTG", "Lighting" },
            { "COM", "Voice and Data Communications" },
            { "ICT", "Data Network and Infrastructure" },
            { "NCL", "Patient Nurse Call" }, { "SEC", "Security and Access Control" },
            { "FIT", "Finishes and Fitout" }, { "STR", "Primary Structure" },
            { "GEN", "General Purpose" },
            // Healthcare Pack (Phase H-1)
            { "AT", "Air Termination" }, { "DC", "Down Conductor" }, { "EB", "Equipotential Bond" },
            { "EP", "Earth Pit" }, { "SPD", "Surge Protection Device" },
            { "DIST", "Distribution" }, { "ISO", "Isolation" }, { "ALM", "Area Alarm" },
            { "TU", "Terminal Unit" }, { "ZVB", "Zone Valve Box" },
            { "AAP", "Area Alarm Panel" }, { "SHLD", "Shielding" }, { "ZONE", "Safety Zone" },
        };

        /// <summary>Full product type description for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> ProductDescriptions = new Dictionary<string, string>
        {
            // Mechanical
            { "AHU", "Air Handling Unit" }, { "FCU", "Fan Coil Unit" },
            { "VAV", "Variable Air Volume Box" }, { "CHR", "Chiller" },
            { "BLR", "Boiler" }, { "PMP", "Pump" }, { "FAN", "Fan" },
            { "HRU", "Heat Recovery Unit" }, { "SPL", "Split System Unit" },
            { "IND", "Induction Unit" }, { "RAD", "Radiant Panel" },
            { "DAM", "Damper" }, { "CLT", "Cooling Tower" },
            { "VFD", "Variable Frequency Drive" },
            // Electrical
            { "DB", "Distribution Board" }, { "MCC", "Motor Control Centre" },
            { "MSB", "Main Switchboard" }, { "SWB", "Switchboard" },
            { "UPS", "Uninterruptible Power Supply" }, { "TRF", "Transformer" },
            { "GEN", "Generator" }, { "ATS", "Automatic Transfer Switch" },
            { "SPD", "Surge Protection Device" }, { "RCD", "Residual Current Device" },
            { "ISO", "Isolator" }, { "SFS", "Soft Starter" }, { "BKP", "Battery Backup" },
            // Lighting
            { "LUM", "Luminaire" }, { "EML", "Emergency Luminaire" },
            { "TRK", "Track Luminaire" }, { "DEC", "Decorative Luminaire" },
            { "DWN", "Downlight" }, { "LIN", "Linear Luminaire" },
            { "SPT", "Spotlight" }, { "WSH", "Wall Washer" },
            { "BOL", "Bollard Light" }, { "UPL", "Uplighter" }, { "FLD", "Floodlight" },
            // Plumbing
            { "WC", "Water Closet" }, { "WHB", "Wash Hand Basin" },
            { "URN", "Urinal" }, { "SNK", "Sink" }, { "SHW", "Shower" },
            { "BTH", "Bath" }, { "DRK", "Drinking Fountain" },
            { "CWL", "Water Cooler" }, { "TRP", "Grease Trap" },
            { "BID", "Bidet" }, { "EWS", "Eyewash Station" }, { "MOP", "Mop Sink" },
            // Fire
            { "SML", "Smoke Detector" }, { "MCP", "Manual Call Point" },
            { "BLL", "Fire Bell or Sounder" }, { "STB", "Strobe Beacon" },
            { "HTD", "Heat Detector" }, { "FIM", "Fire Interface Module" },
            { "SPR", "Sprinkler Head" }, { "FAD", "Fire Alarm Device" },
            // Valves
            { "BLV", "Balancing Valve" }, { "TRV", "Thermostatic Radiator Valve" },
            { "IVL", "Isolation Valve" }, { "NRV", "Non-Return Valve" },
            { "PRV", "Pressure Reducing Valve" }, { "STN", "Strainer" },
            // Building Elements
            { "WL", "Wall" }, { "FL", "Floor" }, { "CLG", "Ceiling" },
            { "RF", "Roof" }, { "DR", "Door" }, { "WN", "Window" },
            { "COL", "Column" }, { "BMG", "Beam" }, { "FND", "Foundation" },
            { "STR", "Staircase" }, { "RMP", "Ramp" }, { "RLG", "Railing" },
            { "FUR", "Furniture" }, { "CSW", "Casework" },
            // MEP Elements
            { "DCT", "Ductwork" }, { "PPE", "Pipework" },
            { "CDT", "Conduit" }, { "CTR", "Cable Tray" },
            { "ATR", "Air Terminal" }, { "ACC", "Accessory" },
        };

        /// <summary>
        /// Build TAG7: a comprehensive, richly descriptive asset narrative with embedded
        /// markup tokens for rich rendering across all 5 output surfaces.
        ///
        /// Returns a Tag7Result containing:
        ///   - MarkedUpNarrative: full narrative with «H»/«L»/«V» markup tokens
        ///   - PlainNarrative: same narrative without markup (parameter storage fallback)
        ///   - SectionA-F: individual plain sections for TAG7A-TAG7F sub-parameters
        ///
        /// Markup tokens:
        ///   «H»text«/H» — Header (Bold+Underline in TextNote, Bold in WPF, &lt;strong&gt; in HTML)
        ///   «L»text«/L» — Label (Italic in TextNote, muted color in WPF, &lt;em&gt; in HTML)
        ///   «V»text«/V» — Value (accent color in WPF, highlighted in HTML)
        /// </summary>
        public static Tag7Result BuildTag7Sections(Document doc, Element el, string categoryName, string[] tokenValues)
        {
            var result = new Tag7Result();
            if (tokenValues == null) return result;
            var markedSections = new List<string>();

            string disc = tokenValues.Length > 0 ? tokenValues[0] : "";
            string loc  = tokenValues.Length > 1 ? tokenValues[1] : "";
            string zone = tokenValues.Length > 2 ? tokenValues[2] : "";
            string lvl  = tokenValues.Length > 3 ? tokenValues[3] : "";
            string sys  = tokenValues.Length > 4 ? tokenValues[4] : "";
            string func = tokenValues.Length > 5 ? tokenValues[5] : "";
            string prod = tokenValues.Length > 6 ? tokenValues[6] : "";
            string seq  = tokenValues.Length > 7 ? tokenValues[7] : "";

            // ── Section A: Asset Identity and Classification ──────────────────
            string discDesc = DisciplineDescriptions.TryGetValue(disc, out string dd) ? dd : disc;
            string prodDesc = ProductDescriptions.TryGetValue(prod, out string pd) ? pd : "";
            string familyName = ParameterHelpers.GetString(el, ParamRegistry.FAMILY_NAME);
            string typeName   = ParameterHelpers.GetString(el, ParamRegistry.TYPE_NAME);
            string description = ParameterHelpers.GetString(el, ParamRegistry.DESC);
            string mfr    = ParameterHelpers.GetString(el, ParamRegistry.MFR);
            string model  = ParameterHelpers.GetString(el, ParamRegistry.MODEL);
            string size   = ParameterHelpers.GetString(el, ParamRegistry.SIZE);

            var identityPlain = new System.Text.StringBuilder();
            var identityMarked = new System.Text.StringBuilder();

            // Asset name (BOLD in marked)
            string assetName = discDesc;
            if (!string.IsNullOrEmpty(prodDesc))
                assetName += $" {prodDesc}";
            else if (!string.IsNullOrEmpty(categoryName))
                assetName += $" {categoryName}";
            if (!string.IsNullOrEmpty(prod))
                assetName += $" ({prod})";

            identityPlain.Append(assetName);
            identityMarked.Append($"\u00ABH\u00BB{assetName}\u00AB/H\u00BB");

            if (!string.IsNullOrEmpty(mfr) || !string.IsNullOrEmpty(model))
            {
                string mfrText = " manufactured by ";
                if (!string.IsNullOrEmpty(mfr))
                    mfrText += mfr;
                if (!string.IsNullOrEmpty(model))
                {
                    if (!string.IsNullOrEmpty(mfr)) mfrText += " ";
                    mfrText += $"Model {model}";
                }
                identityPlain.Append(mfrText);
                identityMarked.Append($" \u00ABL\u00BBmanufactured by\u00AB/L\u00BB \u00ABV\u00BB{(mfr + " " + (string.IsNullOrEmpty(model) ? "" : $"Model {model}")).Trim()}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(mfr) && string.IsNullOrEmpty(model))
            {
                identityPlain.Append($" from the {familyName} family");
                identityMarked.Append($" \u00ABL\u00BBfrom the\u00AB/L\u00BB \u00ABV\u00BB{familyName}\u00AB/V\u00BB \u00ABL\u00BBfamily\u00AB/L\u00BB");
                if (!string.IsNullOrEmpty(typeName))
                {
                    identityPlain.Append($" configured as {typeName}");
                    identityMarked.Append($" \u00ABL\u00BBconfigured as\u00AB/L\u00BB \u00ABV\u00BB{typeName}\u00AB/V\u00BB");
                }
            }
            if (!string.IsNullOrEmpty(description))
            {
                identityPlain.Append($" described as {description}");
                identityMarked.Append($" \u00ABL\u00BBdescribed as\u00AB/L\u00BB \u00ABV\u00BB{description}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(size))
            {
                identityPlain.Append($" sized at {size}");
                identityMarked.Append($" \u00ABL\u00BBsized at\u00AB/L\u00BB \u00ABV\u00BB{size}\u00AB/V\u00BB");
            }

            result.SectionA = identityPlain.ToString().Trim();
            markedSections.Add(identityMarked.ToString().Trim());

            // ── Section B: System and Function Context ────────────────────────
            string sysDesc  = SystemDescriptions.TryGetValue(sys, out string sd) ? sd : sys;
            string funcDesc = FunctionDescriptions.TryGetValue(func, out string fd) ? fd : func;

            if (!string.IsNullOrEmpty(sysDesc))
            {
                var sysPlain = new System.Text.StringBuilder(sysDesc);
                var sysMarked = new System.Text.StringBuilder($"\u00ABH\u00BB{sysDesc}\u00AB/H\u00BB");
                if (!string.IsNullOrEmpty(funcDesc) && funcDesc != sysDesc)
                {
                    sysPlain.Append($" providing {funcDesc}");
                    sysMarked.Append($" \u00ABL\u00BBproviding\u00AB/L\u00BB \u00ABV\u00BB{funcDesc}\u00AB/V\u00BB");
                }
                string servingText = $" serving Zone {zone} on Level {lvl} within Building {loc}";
                sysPlain.Append(servingText);
                sysMarked.Append($" \u00ABL\u00BBserving\u00AB/L\u00BB Zone \u00ABV\u00BB{zone}\u00AB/V\u00BB \u00ABL\u00BBon\u00AB/L\u00BB Level \u00ABV\u00BB{lvl}\u00AB/V\u00BB \u00ABL\u00BBwithin\u00AB/L\u00BB Building \u00ABV\u00BB{loc}\u00AB/V\u00BB");

                result.SectionB = sysPlain.ToString();
                markedSections.Add(sysMarked.ToString());
            }

            // ── Section C: Spatial Context and Room Information ────────────────
            string roomName = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NAME);
            string roomNum  = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NUM);
            string dept     = ParameterHelpers.GetString(el, ParamRegistry.DEPT);
            string gridRef  = ParameterHelpers.GetString(el, ParamRegistry.GRID_REF);
            string bleRoom  = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NAME);
            string bleNum   = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NUM);
            if (string.IsNullOrEmpty(roomName) && !string.IsNullOrEmpty(bleRoom)) roomName = bleRoom;
            if (string.IsNullOrEmpty(roomNum) && !string.IsNullOrEmpty(bleNum)) roomNum = bleNum;

            if (!string.IsNullOrEmpty(roomName) || !string.IsNullOrEmpty(gridRef))
            {
                var spatialPlain = new System.Text.StringBuilder("Located in ");
                var spatialMarked = new System.Text.StringBuilder("\u00ABL\u00BBLocated in\u00AB/L\u00BB ");
                if (!string.IsNullOrEmpty(roomName))
                {
                    spatialPlain.Append(roomName);
                    spatialMarked.Append($"\u00ABV\u00BB{roomName}\u00AB/V\u00BB");
                    if (!string.IsNullOrEmpty(roomNum))
                    {
                        spatialPlain.Append($" (Room {roomNum})");
                        spatialMarked.Append($" (Room \u00ABV\u00BB{roomNum}\u00AB/V\u00BB)");
                    }
                }
                if (!string.IsNullOrEmpty(dept))
                {
                    spatialPlain.Append($" within the {dept} department");
                    spatialMarked.Append($" \u00ABL\u00BBwithin the\u00AB/L\u00BB \u00ABV\u00BB{dept}\u00AB/V\u00BB \u00ABL\u00BBdepartment\u00AB/L\u00BB");
                }
                if (!string.IsNullOrEmpty(gridRef))
                {
                    spatialPlain.Append($" near grid reference {gridRef}");
                    spatialMarked.Append($" \u00ABL\u00BBnear grid reference\u00AB/L\u00BB \u00ABV\u00BB{gridRef}\u00AB/V\u00BB");
                }
                result.SectionC = spatialPlain.ToString();
                markedSections.Add(spatialMarked.ToString());
            }

            // ── Section D: Lifecycle Status, Revision, Origin, Workset, Phase,
            //    Design Option, Maintenance, Commissioning ─────────────────────
            string status  = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
            string rev     = ParameterHelpers.GetString(el, ParamRegistry.REV);
            string origin  = ParameterHelpers.GetString(el, ParamRegistry.ORIGIN);
            string project = ParameterHelpers.GetString(el, ParamRegistry.PROJECT);
            string volume  = ParameterHelpers.GetString(el, ParamRegistry.VOLUME);
            string mntType = ParameterHelpers.GetString(el, ParamRegistry.MNT_TYPE);
            string detailNum = ParameterHelpers.GetString(el, ParamRegistry.DETAIL_NUM);
            // New exploited parameters — workset, phase, design option, maintenance, commissioning
            string workset       = ParameterHelpers.GetString(el, "ASS_WORKSET_TXT");
            string phaseCreated  = ParameterHelpers.GetString(el, "ASS_PHASE_CREATED_TXT");
            string designOption  = ParameterHelpers.GetString(el, "ASS_DESIGN_OPTION_TXT");
            string mntFreq       = ParameterHelpers.GetString(el, "MNT_FREQUENCY_TXT");
            string mntWarranty   = ParameterHelpers.GetString(el, "MNT_WARRANTY_EXPIRY_TXT");
            string comStatus     = ParameterHelpers.GetString(el, "COM_COMMISSION_STATUS_TXT");
            string expectedLife  = ParameterHelpers.GetString(el, "PER_EXPECTED_LIFE_YEARS");
            string accessReqs    = ParameterHelpers.GetString(el, "MNT_ACCESS_REQUIREMENTS_TXT");

            var lifecyclePlain = new System.Text.StringBuilder();
            var lifecycleMarked = new System.Text.StringBuilder();
            // Use natural language connectors instead of just commas
            if (!string.IsNullOrEmpty(status))
            {
                lifecyclePlain.Append($"This element is {status.ToLower()}");
                lifecycleMarked.Append($"This element is \u00ABV\u00BB{status.ToLower()}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(rev))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", currently at "); lifecycleMarked.Append(", currently at "); }
                lifecyclePlain.Append($"revision {rev}");
                lifecycleMarked.Append($"\u00ABL\u00BBrevision\u00AB/L\u00BB \u00ABV\u00BB{rev}\u00AB/V\u00BB");
                // Add tag timestamp for audit trail
                string tagDate = DateTime.Now.ToString("yyyy-MM-dd");
                lifecyclePlain.Append($" (tagged {tagDate})");
                lifecycleMarked.Append($" (\u00ABL\u00BBtagged\u00AB/L\u00BB \u00ABV\u00BB{tagDate}\u00AB/V\u00BB)");
            }
            if (!string.IsNullOrEmpty(origin))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", originating from "); lifecycleMarked.Append(", originating from "); }
                lifecyclePlain.Append(origin);
                lifecycleMarked.Append($"\u00ABV\u00BB{origin}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(project))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(" within "); lifecycleMarked.Append(" within "); }
                else { lifecyclePlain.Append("Part of "); lifecycleMarked.Append("Part of "); }
                lifecyclePlain.Append($"project {project}");
                lifecycleMarked.Append($"\u00ABL\u00BBproject\u00AB/L\u00BB \u00ABV\u00BB{project}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(volume))
            {
                lifecyclePlain.Append($" (volume {volume})");
                lifecycleMarked.Append($" (\u00ABL\u00BBvolume\u00AB/L\u00BB \u00ABV\u00BB{volume}\u00AB/V\u00BB)");
            }
            // Workset and phase — unexploited data not in schedules
            if (!string.IsNullOrEmpty(workset))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Managed under the {workset} workset");
                lifecycleMarked.Append($"Managed under the \u00ABV\u00BB{workset}\u00AB/V\u00BB \u00ABL\u00BBworkset\u00AB/L\u00BB");
            }
            if (!string.IsNullOrEmpty(phaseCreated))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", created during the "); lifecycleMarked.Append(", created during the "); }
                lifecyclePlain.Append($"{phaseCreated} phase");
                lifecycleMarked.Append($"\u00ABV\u00BB{phaseCreated}\u00AB/V\u00BB \u00ABL\u00BBphase\u00AB/L\u00BB");
            }
            if (!string.IsNullOrEmpty(designOption))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", part of design option "); lifecycleMarked.Append(", part of \u00ABL\u00BBdesign option\u00AB/L\u00BB "); }
                lifecyclePlain.Append(designOption);
                lifecycleMarked.Append($"\u00ABV\u00BB{designOption}\u00AB/V\u00BB");
            }
            // Maintenance and FM
            if (!string.IsNullOrEmpty(mntType))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Requires {mntType.ToLower()} maintenance");
                lifecycleMarked.Append($"Requires \u00ABV\u00BB{mntType.ToLower()}\u00AB/V\u00BB \u00ABL\u00BBmaintenance\u00AB/L\u00BB");
            }
            if (!string.IsNullOrEmpty(mntFreq))
            {
                if (!string.IsNullOrEmpty(mntType))
                {
                    lifecyclePlain.Append($" on a {mntFreq.ToLower()} basis");
                    lifecycleMarked.Append($" on a \u00ABV\u00BB{mntFreq.ToLower()}\u00AB/V\u00BB basis");
                }
                else if (lifecyclePlain.Length > 0)
                {
                    lifecyclePlain.Append($". Maintenance frequency: {mntFreq.ToLower()}");
                    lifecycleMarked.Append($". \u00ABL\u00BBMaintenance frequency:\u00AB/L\u00BB \u00ABV\u00BB{mntFreq.ToLower()}\u00AB/V\u00BB");
                }
            }
            if (!string.IsNullOrEmpty(expectedLife))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", with an expected service life of "); lifecycleMarked.Append(", with an expected \u00ABL\u00BBservice life\u00AB/L\u00BB of "); }
                lifecyclePlain.Append($"{expectedLife} years");
                lifecycleMarked.Append($"\u00ABV\u00BB{expectedLife}\u00AB/V\u00BB years");
            }
            if (!string.IsNullOrEmpty(mntWarranty))
            {
                lifecyclePlain.Append($" (warranty expires {mntWarranty})");
                lifecycleMarked.Append($" (\u00ABL\u00BBwarranty expires\u00AB/L\u00BB \u00ABV\u00BB{mntWarranty}\u00AB/V\u00BB)");
            }
            if (!string.IsNullOrEmpty(accessReqs))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Maintenance access requires {accessReqs.ToLower()}");
                lifecycleMarked.Append($"\u00ABL\u00BBMaintenance access requires\u00AB/L\u00BB \u00ABV\u00BB{accessReqs.ToLower()}\u00AB/V\u00BB");
            }
            // Commissioning status
            if (!string.IsNullOrEmpty(comStatus))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Commissioning status: {comStatus.ToLower()}");
                lifecycleMarked.Append($"\u00ABL\u00BBCommissioning status:\u00AB/L\u00BB \u00ABV\u00BB{comStatus.ToLower()}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(detailNum))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", see "); lifecycleMarked.Append(", see "); }
                lifecyclePlain.Append($"detail {detailNum}");
                lifecycleMarked.Append($"\u00ABL\u00BBdetail\u00AB/L\u00BB \u00ABV\u00BB{detailNum}\u00AB/V\u00BB");
            }

            if (lifecyclePlain.Length > 0)
            {
                result.SectionD = lifecyclePlain.ToString();
                markedSections.Add(lifecycleMarked.ToString());
            }

            // ── Section E: Technical Data (discipline-specific + dimensions) ──
            string techData = BuildDisciplineTechSection(el, disc, categoryName);
            string dimData = BuildDimensionalSection(el, categoryName);
            var techPlain = new System.Text.StringBuilder();
            var techMarked = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(techData))
            {
                techPlain.Append(techData);
                // Build marked version with label/value pairs
                techMarked.Append(BuildMarkedTechSection(el, disc, categoryName));
            }
            if (!string.IsNullOrEmpty(dimData))
            {
                if (techPlain.Length > 0) { techPlain.Append(". In terms of its dimensions, it is "); techMarked.Append(". \u00ABC\u00BBIn terms of its dimensions, it is\u00AB/C\u00BB "); }
                techPlain.Append(dimData);
                techMarked.Append(BuildMarkedDimSection(el, categoryName));
            }
            // Append room finishes if this is a room or spatial element
            if (categoryName == "Rooms" || categoryName == "Spaces")
            {
                string finFlr = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_FLR);
                string finWall = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_WALL);
                string finClg = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_CLG);
                string finBase = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_BASE);
                if (!string.IsNullOrEmpty(finFlr) || !string.IsNullOrEmpty(finWall) || !string.IsNullOrEmpty(finClg))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append("Room finishes include");
                    techMarked.Append("\u00ABL\u00BBRoom finishes include\u00AB/L\u00BB");
                    if (!string.IsNullOrEmpty(finFlr))
                    {
                        techPlain.Append($" floor: {finFlr}");
                        techMarked.Append($" \u00ABL\u00BBfloor:\u00AB/L\u00BB \u00ABV\u00BB{finFlr}\u00AB/V\u00BB");
                    }
                    if (!string.IsNullOrEmpty(finWall))
                    {
                        techPlain.Append($", walls: {finWall}");
                        techMarked.Append($", \u00ABL\u00BBwalls:\u00AB/L\u00BB \u00ABV\u00BB{finWall}\u00AB/V\u00BB");
                    }
                    if (!string.IsNullOrEmpty(finClg))
                    {
                        techPlain.Append($", ceiling: {finClg}");
                        techMarked.Append($", \u00ABL\u00BBceiling:\u00AB/L\u00BB \u00ABV\u00BB{finClg}\u00AB/V\u00BB");
                    }
                    if (!string.IsNullOrEmpty(finBase))
                    {
                        techPlain.Append($", base: {finBase}");
                        techMarked.Append($", \u00ABL\u00BBbase:\u00AB/L\u00BB \u00ABV\u00BB{finBase}\u00AB/V\u00BB");
                    }
                }
            }
            // Append door function and head height
            if (categoryName == "Doors")
            {
                string doorFunc = ParameterHelpers.GetString(el, ParamRegistry.DOOR_FUNC);
                string doorHead = ParameterHelpers.GetString(el, ParamRegistry.DOOR_HEAD_HT);
                if (!string.IsNullOrEmpty(doorFunc))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Functioning as a {doorFunc.ToLower()} door");
                    techMarked.Append($"\u00ABC\u00BBFunctioning as a\u00AB/C\u00BB \u00ABV\u00BB{doorFunc.ToLower()}\u00AB/V\u00BB \u00ABL\u00BBdoor\u00AB/L\u00BB");
                }
                if (!string.IsNullOrEmpty(doorHead))
                {
                    if (techPlain.Length > 0) { techPlain.Append($" with head height at {doorHead} mm"); techMarked.Append($" with \u00ABL\u00BBhead height\u00AB/L\u00BB at \u00ABV\u00BB{doorHead} mm\u00AB/V\u00BB"); }
                }
            }
            // Append window head height
            if (categoryName == "Windows")
            {
                string winHead = ParameterHelpers.GetString(el, ParamRegistry.WINDOW_HEAD_HT);
                if (!string.IsNullOrEmpty(winHead))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Window head height at {winHead} mm");
                    techMarked.Append($"\u00ABL\u00BBWindow head height\u00AB/L\u00BB at \u00ABV\u00BB{winHead} mm\u00AB/V\u00BB");
                }
            }
            // Append fire rating for any element that has it
            {
                string fr = ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING);
                if (!string.IsNullOrEmpty(fr) && categoryName != "Rooms" && categoryName != "Spaces")
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Fire resistance rated at {fr} minutes");
                    techMarked.Append($"\u00ABL\u00BBFire resistance rated at\u00AB/L\u00BB \u00ABV\u00BB{fr} minutes\u00AB/V\u00BB");
                }
            }
            // Append sustainability data if available
            {
                string carbonFp = ParameterHelpers.GetString(el, "PER_SUST_CARBON_FOOTPRINT_KG");
                string recyclability = ParameterHelpers.GetString(el, "PER_RECYCLABILITY_PCT");
                if (!string.IsNullOrEmpty(carbonFp))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Embodied carbon: {carbonFp} kgCO₂e");
                    techMarked.Append($"\u00ABL\u00BBEmbodied carbon:\u00AB/L\u00BB \u00ABV\u00BB{carbonFp} kgCO₂e\u00AB/V\u00BB");
                }
                if (!string.IsNullOrEmpty(recyclability))
                {
                    if (techPlain.Length > 0) { techPlain.Append($", {recyclability}% recyclable"); techMarked.Append($", \u00ABV\u00BB{recyclability}%\u00AB/V\u00BB \u00ABL\u00BBrecyclable\u00AB/L\u00BB"); }
                }
            }
            // Append acoustic data
            {
                string stc = ParameterHelpers.GetString(el, "PER_ACOUSTIC_WALL_STC");
                string iic = ParameterHelpers.GetString(el, "PER_ACOUSTIC_FLOOR_IIC");
                if (!string.IsNullOrEmpty(stc))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Acoustic rating STC {stc}");
                    techMarked.Append($"\u00ABL\u00BBAcoustic rating STC\u00AB/L\u00BB \u00ABV\u00BB{stc}\u00AB/V\u00BB");
                }
                if (!string.IsNullOrEmpty(iic))
                {
                    if (!string.IsNullOrEmpty(stc)) { techPlain.Append($", IIC {iic}"); techMarked.Append($", \u00ABL\u00BBIIC\u00AB/L\u00BB \u00ABV\u00BB{iic}\u00AB/V\u00BB"); }
                    else
                    {
                        if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                        techPlain.Append($"Acoustic rating IIC {iic}");
                        techMarked.Append($"\u00ABL\u00BBAcoustic rating IIC\u00AB/L\u00BB \u00ABV\u00BB{iic}\u00AB/V\u00BB");
                    }
                }
            }

            if (techPlain.Length > 0)
            {
                result.SectionE = techPlain.ToString();
                markedSections.Add(techMarked.ToString());
            }

            // ── Section F: Classification + Cost + ISO Reference ──────────────
            string uniformat     = ParameterHelpers.GetString(el, ParamRegistry.UNIFORMAT);
            string uniformatDesc = ParameterHelpers.GetString(el, ParamRegistry.UNIFORMAT_DESC);
            string omniclass     = ParameterHelpers.GetString(el, ParamRegistry.OMNICLASS);
            string keynote       = ParameterHelpers.GetString(el, ParamRegistry.KEYNOTE);
            string typeMark      = ParameterHelpers.GetString(el, ParamRegistry.TYPE_MARK);
            string cost          = ParameterHelpers.GetString(el, ParamRegistry.COST);

            var classPlain = new System.Text.StringBuilder();
            var classMarked = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(uniformat))
            {
                classPlain.Append($"Uniformat code {uniformat}");
                classMarked.Append($"\u00ABL\u00BBUniformat code\u00AB/L\u00BB \u00ABV\u00BB{uniformat}\u00AB/V\u00BB");
                if (!string.IsNullOrEmpty(uniformatDesc))
                {
                    classPlain.Append($" ({uniformatDesc})");
                    classMarked.Append($" ({uniformatDesc})");
                }
            }
            if (!string.IsNullOrEmpty(omniclass))
            {
                if (classPlain.Length > 0) { classPlain.Append(", with OmniClass reference "); classMarked.Append(", with \u00ABL\u00BBOmniClass reference\u00AB/L\u00BB "); }
                else { classPlain.Append("OmniClass reference "); classMarked.Append("\u00ABL\u00BBOmniClass reference\u00AB/L\u00BB "); }
                classPlain.Append(omniclass);
                classMarked.Append($"\u00ABV\u00BB{omniclass}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(keynote))
            {
                if (classPlain.Length > 0) { classPlain.Append(", keynote "); classMarked.Append(", \u00ABL\u00BBkeynote\u00AB/L\u00BB "); }
                else { classPlain.Append("Keynote "); classMarked.Append("\u00ABL\u00BBKeynote\u00AB/L\u00BB "); }
                classPlain.Append(keynote);
                classMarked.Append($"\u00ABV\u00BB{keynote}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(typeMark))
            {
                if (classPlain.Length > 0) { classPlain.Append(", identified as type mark "); classMarked.Append(", identified as \u00ABL\u00BBtype mark\u00AB/L\u00BB "); }
                else { classPlain.Append("Type mark "); classMarked.Append("\u00ABL\u00BBType mark\u00AB/L\u00BB "); }
                classPlain.Append(typeMark);
                classMarked.Append($"\u00ABV\u00BB{typeMark}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(cost))
            {
                if (classPlain.Length > 0) { classPlain.Append(", with an estimated unit cost of "); classMarked.Append(", with an estimated \u00ABL\u00BBunit cost\u00AB/L\u00BB of "); }
                else { classPlain.Append("Estimated unit cost of "); classMarked.Append("Estimated \u00ABL\u00BBunit cost\u00AB/L\u00BB of "); }
                classPlain.Append(cost);
                classMarked.Append($"\u00ABV\u00BB{cost}\u00AB/V\u00BB");
            }
            // Type comments — unexploited rich text from Revit type properties
            string typeComments = ParameterHelpers.GetString(el, ParamRegistry.TYPE_COMMENTS);
            if (!string.IsNullOrEmpty(typeComments))
            {
                if (classPlain.Length > 0) { classPlain.Append(". Type notes: "); classMarked.Append(". \u00ABL\u00BBType notes:\u00AB/L\u00BB "); }
                else { classPlain.Append("Type notes: "); classMarked.Append("\u00ABL\u00BBType notes:\u00AB/L\u00BB "); }
                classPlain.Append(typeComments);
                classMarked.Append($"\u00ABV\u00BB{typeComments}\u00AB/V\u00BB");
            }

            // prefer ASS_TAG_1 (already assembled by BuildAndWriteTag,
            // the single source of truth for tag composition) when it is
            // populated. Falls back to inline re-assembly only when the
            // canonical tag has not been built yet — avoids divergence
            // between Section F and the assembled tag.
            string fullTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            if (string.IsNullOrEmpty(fullTag))
            {
                // S02 defensive guards — trap upstream token corruption so the narrative stays readable
                // even when a PROD/SYS/etc. writer accidentally concatenated multiple descriptors into
                // one token slot, or when TagPrefix/TagSuffix already appears in the joined string.
                string[] isoTokens = new string[tokenValues.Length];
                for (int i = 0; i < tokenValues.Length; i++)
                {
                    string v = tokenValues[i];
                    if (!string.IsNullOrEmpty(v) && !string.IsNullOrEmpty(Separator) && v.Contains(Separator))
                    {
                        StingLog.Warn($"BuildTag7Sections: token[{i}]='{v}' contains separator '{Separator}'. " +
                                      $"ElementId={el?.Id}. Truncating to first segment.");
                        v = v.Split(new[] { Separator }, 2, StringSplitOptions.None)[0];
                    }
                    isoTokens[i] = v;
                }
                fullTag = string.Join(Separator, isoTokens);
                if (!string.IsNullOrEmpty(TagPrefix) &&
                    !fullTag.StartsWith(TagPrefix + Separator, StringComparison.Ordinal) &&
                    !fullTag.StartsWith(TagPrefix, StringComparison.Ordinal))
                {
                    fullTag = TagPrefix + Separator + fullTag;
                }
                if (!string.IsNullOrEmpty(TagSuffix) &&
                    !fullTag.EndsWith(Separator + TagSuffix, StringComparison.Ordinal) &&
                    !fullTag.EndsWith(TagSuffix, StringComparison.Ordinal))
                {
                    fullTag = fullTag + Separator + TagSuffix;
                }
            }
            if (classPlain.Length > 0) { classPlain.Append(". Assigned "); classMarked.Append(". Assigned "); }
            classPlain.Append($"ISO 19650 tag {fullTag}");
            classMarked.Append($"\u00ABL\u00BBISO 19650 tag\u00AB/L\u00BB \u00ABH\u00BB{fullTag}\u00AB/H\u00BB");

            result.SectionF = classPlain.ToString();
            markedSections.Add(classMarked.ToString());

            // ── Assemble final narratives with meaningful connecting phrases ──
            // Instead of pipe separators, connect sections with logical transition words
            // that form a coherent description of the asset.
            var plainParts = new System.Text.StringBuilder();
            var markedParts = new System.Text.StringBuilder();

            // A: Identity — opening statement, no prefix needed
            if (!string.IsNullOrEmpty(result.SectionA))
            {
                plainParts.Append(result.SectionA);
                markedParts.Append(markedSections.Count > 0 ? markedSections[0] : result.SectionA);
            }
            // Use a running index to track position in markedSections (only non-empty sections are added)
            int mIdx = 1; // 0 = Section A (already consumed above)
            // B: System context — connects with ". This asset operates within"
            if (!string.IsNullOrEmpty(result.SectionB))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". This asset operates within the ");
                    markedParts.Append(". \u00ABC\u00BBThis asset operates within the\u00AB/C\u00BB ");
                }
                plainParts.Append(result.SectionB);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionB);
                mIdx++;
            }
            // C: Spatial — connects with ". It is" (SectionC already starts with "Located in")
            if (!string.IsNullOrEmpty(result.SectionC))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". It is ");
                    markedParts.Append(". \u00ABC\u00BBIt is\u00AB/C\u00BB ");
                    // SectionC starts with "Located in" — lowercase it after "It is"
                    result.SectionC = char.ToLower(result.SectionC[0]) + result.SectionC.Substring(1);
                    // Also lowercase the marked section
                    if (markedSections.Count > mIdx)
                    {
                        string mc = markedSections[mIdx];
                        mc = mc.Replace("\u00ABL\u00BBLocated in\u00AB/L\u00BB", "\u00ABL\u00BBlocated in\u00AB/L\u00BB");
                        markedSections[mIdx] = mc;
                    }
                }
                plainParts.Append(result.SectionC);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionC);
                mIdx++;
            }
            // D: Lifecycle — connects with ". Regarding its lifecycle,"
            if (!string.IsNullOrEmpty(result.SectionD))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". Regarding its lifecycle, ");
                    markedParts.Append(". \u00ABC\u00BBRegarding its lifecycle,\u00AB/C\u00BB ");
                    // SectionD starts with "This element is" — lowercase it after connector
                    if (result.SectionD.StartsWith("This element is"))
                        result.SectionD = "this element is" + result.SectionD.Substring("This element is".Length);
                    if (markedSections.Count > mIdx)
                    {
                        string md = markedSections[mIdx];
                        md = md.Replace("This element is", "this element is");
                        markedSections[mIdx] = md;
                    }
                }
                plainParts.Append(result.SectionD);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionD);
                mIdx++;
            }
            // E: Technical — connects with ". Technical specifications include"
            if (!string.IsNullOrEmpty(result.SectionE))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". Technical specifications include ");
                    markedParts.Append(". \u00ABC\u00BBTechnical specifications include\u00AB/C\u00BB ");
                }
                plainParts.Append(result.SectionE);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionE);
                mIdx++;
            }
            // F: Classification — connects with ". Classified under"
            if (!string.IsNullOrEmpty(result.SectionF))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". Classified under ");
                    markedParts.Append(". \u00ABC\u00BBClassified under\u00AB/C\u00BB ");
                }
                plainParts.Append(result.SectionF);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionF);
            }

            result.PlainNarrative = plainParts.ToString();
            result.MarkedUpNarrative = markedParts.ToString();

            // ─── T4-T10 tier summaries (Phase 165) ────────────────────────
            // Read element data and build single-line summaries per tier. Each
            // builder is independently try/catch wrapped — a failure in one
            // tier never breaks TAG7A-TAG7F or the other tiers.
            BuildTier4To10Summaries(el, result);

            return result;
        }

        /// <summary>
        /// Phase 165 — assemble T4-T10 tier summaries from element parameters.
        /// Each tier reads a small group of shared parameters and formats a
        /// human-readable single-line summary. Empty tier → empty string
        /// (callers skip silently).
        /// </summary>
        private static void BuildTier4To10Summaries(Element el, Tag7Result result)
        {
            if (el == null) return;

            // T4 — Commissioning & handover (N-G16 QR workflow)
            try
            {
                string state    = ParameterHelpers.GetString(el, ParamRegistry.COMM_STATE_TXT);
                string date     = ParameterHelpers.GetString(el, ParamRegistry.COMM_DATE_TXT);
                string oper     = ParameterHelpers.GetString(el, ParamRegistry.COMM_OPERATIVE_TXT);
                string witness  = ParameterHelpers.GetString(el, ParamRegistry.COMM_WITNESS_TXT);
                string notes    = ParameterHelpers.GetString(el, ParamRegistry.COMM_NOTES_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(state))   parts.Add(state);
                if (!string.IsNullOrEmpty(date))    parts.Add(date);
                if (!string.IsNullOrEmpty(oper))    parts.Add($"by {oper}");
                if (!string.IsNullOrEmpty(witness)) parts.Add($"witness {witness}");
                if (!string.IsNullOrEmpty(notes))   parts.Add(notes);
                if (parts.Count > 0) result.SectionT4 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T4 failed: " + ex.Message); }

            // T5 — Cost & procurement
            try
            {
                string ugx      = ParameterHelpers.GetString(el, ParamRegistry.CST_UG_PRICE_UGX);
                string usd      = ParameterHelpers.GetString(el, ParamRegistry.CST_INTL_PRICE_USD);
                string quote    = ParameterHelpers.GetString(el, ParamRegistry.CST_QUOTE_REF_TXT);
                string hrs      = ParameterHelpers.GetString(el, ParamRegistry.CST_INSTALL_HRS);
                string crew     = ParameterHelpers.GetString(el, ParamRegistry.CST_LABOUR_CREW_TXT);
                string rate     = ParameterHelpers.GetString(el, ParamRegistry.CST_LABOUR_RATE_GBP);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(ugx))   parts.Add($"UGX {ugx}");
                if (!string.IsNullOrEmpty(usd))   parts.Add($"USD {usd}");
                if (!string.IsNullOrEmpty(quote)) parts.Add($"quote {quote}");
                if (!string.IsNullOrEmpty(hrs))   parts.Add($"{hrs} hrs install");
                if (!string.IsNullOrEmpty(crew))  parts.Add($"crew {crew}");
                if (!string.IsNullOrEmpty(rate))  parts.Add($"GBP {rate}/hr");
                if (parts.Count > 0) result.SectionT5 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T5 failed: " + ex.Message); }

            // T6 — Carbon & sustainability (BS EN 15978 lifecycle stages)
            try
            {
                string a13   = ParameterHelpers.GetString(el, ParamRegistry.CBN_A1_A3_KG_CO2E);
                string a4    = ParameterHelpers.GetString(el, ParamRegistry.CBN_A4_KG_CO2E);
                string a5    = ParameterHelpers.GetString(el, ParamRegistry.CBN_A5_KG_CO2E);
                string b6    = ParameterHelpers.GetString(el, ParamRegistry.CBN_B6_KG_CO2E_YR);
                string c1    = ParameterHelpers.GetString(el, ParamRegistry.CBN_C1_KG_CO2E);
                string c2    = ParameterHelpers.GetString(el, ParamRegistry.CBN_C2_KG_CO2E);
                string c34   = ParameterHelpers.GetString(el, ParamRegistry.CBN_C3_C4_KG_CO2E);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(a13)) parts.Add($"A1-A3 {a13} kgCO2e");
                if (!string.IsNullOrEmpty(a4))  parts.Add($"A4 {a4}");
                if (!string.IsNullOrEmpty(a5))  parts.Add($"A5 {a5}");
                if (!string.IsNullOrEmpty(b6))  parts.Add($"B6 {b6}/yr");
                if (!string.IsNullOrEmpty(c1))  parts.Add($"C1 {c1}");
                if (!string.IsNullOrEmpty(c2))  parts.Add($"C2 {c2}");
                if (!string.IsNullOrEmpty(c34)) parts.Add($"C3-C4 {c34}");
                if (parts.Count > 0) result.SectionT6 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T6 failed: " + ex.Message); }

            // T7 — Fabrication & QC
            try
            {
                string spool   = ParameterHelpers.GetString(el, ParamRegistry.ASS_SPOOL_NR_TXT);
                string status  = ParameterHelpers.GetString(el, ParamRegistry.ASS_FAB_STATUS_TXT);
                string insp    = ParameterHelpers.GetString(el, ParamRegistry.ASS_QC_INSPECTOR_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(spool))  parts.Add($"spool {spool}");
                if (!string.IsNullOrEmpty(status)) parts.Add(status);
                if (!string.IsNullOrEmpty(insp))   parts.Add($"insp {insp}");
                if (parts.Count > 0) result.SectionT7 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T7 failed: " + ex.Message); }

            // T8 — Clash triage & resolution
            try
            {
                string sev     = ParameterHelpers.GetString(el, ParamRegistry.CLASH_TRIAGE_SEVERITY_NR);
                string cat     = ParameterHelpers.GetString(el, ParamRegistry.CLASH_TRIAGE_CATEGORY_TXT);
                string resStat = ParameterHelpers.GetString(el, ParamRegistry.CLASH_RESOLUTION_STATUS_TXT);
                string score   = ParameterHelpers.GetString(el, ParamRegistry.CLASH_TRIAGE_SCORE);
                string action  = ParameterHelpers.GetString(el, ParamRegistry.CLASH_RESOLUTION_ACTION_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(sev))     parts.Add($"sev {sev}");
                if (!string.IsNullOrEmpty(cat))     parts.Add(cat);
                if (!string.IsNullOrEmpty(resStat)) parts.Add(resStat);
                if (!string.IsNullOrEmpty(score))   parts.Add($"score {score}");
                if (!string.IsNullOrEmpty(action))  parts.Add($"action: {action}");
                if (parts.Count > 0) result.SectionT8 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T8 failed: " + ex.Message); }

            // T9 — As-built reconciliation & model health
            try
            {
                string dev      = ParameterHelpers.GetString(el, ParamRegistry.ASBUILT_DEVIATION_MM);
                string capDate  = ParameterHelpers.GetString(el, ParamRegistry.ASBUILT_CAPTURE_DATE_TXT);
                string health   = ParameterHelpers.GetString(el, ParamRegistry.HEALTH_SCORE_LAST_NR);
                string healthDt = ParameterHelpers.GetString(el, ParamRegistry.HEALTH_SCORE_DATE_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(dev))      parts.Add($"Δ {dev} mm");
                if (!string.IsNullOrEmpty(capDate))  parts.Add($"captured {capDate}");
                if (!string.IsNullOrEmpty(health))   parts.Add($"health {health}");
                if (!string.IsNullOrEmpty(healthDt)) parts.Add($"on {healthDt}");
                if (parts.Count > 0) result.SectionT9 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T9 failed: " + ex.Message); }

            // T10 — Compliance / audit (IFC PSet + ACC round-trip)
            try
            {
                string pset    = ParameterHelpers.GetString(el, ParamRegistry.IFC_PSET_OVERRIDE_TXT);
                string accId   = ParameterHelpers.GetString(el, ParamRegistry.ACC_ISSUE_ID_TXT);
                string accStat = ParameterHelpers.GetString(el, ParamRegistry.ACC_SYNC_STATUS_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(pset))    parts.Add($"IFC PSet: {pset}");
                if (!string.IsNullOrEmpty(accId))   parts.Add($"ACC #{accId}");
                if (!string.IsNullOrEmpty(accStat)) parts.Add($"sync {accStat}");
                if (parts.Count > 0) result.SectionT10 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T10 failed: " + ex.Message); }
        }

        /// <summary>
        /// Backward-compatible wrapper: returns the plain narrative string.
        /// All existing callers use this — returns exactly the same output as before.
        /// </summary>
        public static string BuildTag7Narrative(Document doc, Element el, string categoryName, string[] tokenValues)
        {
            return BuildTag7Sections(doc, el, categoryName, tokenValues).PlainNarrative;
        }

        /// <summary>
        /// Phase 165 — Issue #5. Mode-aware overload of BuildTag7Sections.
        ///
        /// Both modes return identical SectionA-C (Identity / System / Spatial)
        /// because T1-T3 are common. The remaining sections differ:
        ///
        ///  - <c>TagMode.DC</c> — SectionD/E/F = Lifecycle / Technical / Classification
        ///    (the System A TAG7D-F content). Tier4Summaries are still hydrated
        ///    from element parameters so the data is visible to both clients,
        ///    but in DC mode the consumer (WriteTag7All) writes only A-F.
        ///
        ///  - <c>TagMode.Handover</c> — Section D/E/F return the same plain
        ///    System A content, but the writer pulls T4-T10 from
        ///    <see cref="Tag7Result.Tier4Summaries"/> (T4=Commissioning,
        ///    T5=Cost, T6=Carbon, T7=Fab, T8=Clash, T9=AsBuilt, T10=Audit).
        ///
        ///  - <c>TagMode.Custom</c> — same shape as Handover; project supplies
        ///    its own T4-T10 mapping via project_config.json overrides.
        ///
        /// The method itself is mode-agnostic at read time — it just hydrates
        /// every section + every tier — so two calls return identical Tag7Result.
        /// The branch happens at write time in <see cref="WriteTag7All"/>.
        /// </summary>
        public static Tag7Result BuildTag7Sections(Document doc, Element el,
            string categoryName, string[] tokenValues, ParamRegistry.TagMode mode)
        {
            // Hydrate the Tag7Result the same way regardless of mode — the writer
            // selects which subset to persist based on `mode`. Centralising the
            // build keeps DC ↔ Handover round-trips lossless: switching modes
            // does NOT erase data that lives outside the active mode's tier set.
            var result = BuildTag7Sections(doc, el, categoryName, tokenValues);
            return result;
        }

        /// <summary>
        /// Write TAG7 + all sub-section parameters (TAG7A-TAG7F) for an element.
        /// Also writes warning text, and populates the category-specific paragraph container.
        /// Returns number of parameters written.
        ///
        /// Phase 165 — Issue #2. The writer is mode-aware: it reads
        /// <see cref="ParamRegistry.GetActiveTagMode"/> from the document and
        /// branches the T4-T10 surface:
        ///
        ///  - <c>TagMode.DC</c>     — writes TAG7A-F as today (System A).
        ///  - <c>TagMode.Handover</c> — writes TAG7A-C as today; appends T4-T10
        ///    via <see cref="Tag7Result.Tier4Summaries"/> read out of the System
        ///    B parameter groups (COMM_*, CST_*, CBN_*, FAB_*, CLH_*, ASB_*,
        ///    AUD_*) which were hydrated by BuildTier4To10Summaries.
        ///  - <c>TagMode.Custom</c>  — same surface as Handover; project-defined
        ///    payload via project_config.json.
        ///
        /// In every mode the assembled narrative is also written to
        /// <see cref="ParamRegistry.TAG7"/> as the combined human-readable form.
        ///
        /// Switching mode does NOT erase the other system's parameter data —
        /// only the visible TAG7A-F + appended tier set changes.
        /// </summary>
        public static int WriteTag7All(Document doc, Element el, string categoryName, string[] tokenValues, bool overwrite = true)
        {
            if (tokenValues == null || tokenValues.Length < 8) return 0;
            var tag7 = BuildTag7Sections(doc, el, categoryName, tokenValues);
            int written = 0;

            // Phase 165 — resolve active mode once per element-write so the
            // branch is stable for the duration of this call.
            ParamRegistry.TagMode activeMode = ParamRegistry.GetActiveTagMode(doc);

            // build the final TAG7 string locally before
            // the single write so the post-write read-back can be eliminated.
            // The previous code wrote TAG7, then re-read it just to append
            // warnings — wasted LookupParameter + GetString per element.
            string tag7Final = tag7.MarkedUpNarrative ?? "";

            // TAG7A-TAG7F get plain section text for tag family labels
            // Phase 165 — Issue #2. Mode branch:
            //   DC      → write all six (TAG7A-F = System A T1-T6)
            //   Handover → write only TAG7A-C (T1-T3 == identity/system/spatial,
            //              shared with DC). TAG7D-F are blanked because in
            //              Handover mode the tier 4-6 narrative is owned by
            //              the System B append (T4 commissioning / T5 cost /
            //              T6 carbon) appended below — leaving the old DC
            //              text in TAG7D-F would conflict.
            //   Custom  → same as Handover.
            string[] sectionParams = ParamRegistry.TAG7Sections;
            string[] sectionValues = tag7.AllSections;
            int sectionLimit = (activeMode == ParamRegistry.TagMode.DC)
                ? sectionParams.Length     // 6 — full A-F
                : 3;                       // Handover / Custom: A-C only
            for (int i = 0; i < sectionParams.Length && i < sectionValues.Length; i++)
            {
                if (i < sectionLimit)
                {
                    if (!string.IsNullOrEmpty(sectionValues[i]))
                    {
                        if (ParameterHelpers.SetString(el, sectionParams[i], sectionValues[i], overwrite))
                            written++;
                    }
                }
                else
                {
                    // Issue #22 — Handover/Custom: blank D-F so old DC content
                    // doesn't shadow the System B tier appends that follow.
                    // Phase 165 perf — CachedLookup avoids per-instance
                    // LookupParameter cost.
                    Parameter p = ParameterHelpers.CachedLookup(el, sectionParams[i]);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        string cur = p.AsString();
                        if (!string.IsNullOrEmpty(cur))
                        {
                            try { p.Set(string.Empty); written++; } catch { /* defensive */ }
                        }
                    }
                }
            }

            // ── T4-T10 tier appends (Phase 165 — tagging workflow repair) ──
            // Issue #2 / #11 — only fire the tier append in Handover or Custom
            // mode. DC mode uses TAG7D-F (written above) for tiers 4-6; firing
            // the tier append in DC would write T4-T6 twice (once via TAG7D-F
            // narrative, once via tier-summary append).
            // Pattern mode (HANDOVER / DC / CUSTOM) is read once and surfaced as
            // a tag prefix once at least one tier 4+ payload is appended.
            // Reads pull from the element-type first (depth lives on type per
            // SetParagraphDepthCommand) then fall back to the instance.
            if (activeMode != ParamRegistry.TagMode.DC)
            try
            {
                Element typeEl = doc?.GetElement(el.GetTypeId());
                bool[] enabled = new bool[7]; // index 0 = T4 .. 6 = T10
                // Phase 165 perf — reuse the cached ParamRegistry.AllParaStates
                // (10 entries) instead of allocating a 7-string array per
                // element. Slot index 3 in AllParaStates == PARA_STATE_4.
                string[] allStates = ParamRegistry.AllParaStates;
                for (int i = 0; i < 7; i++)
                {
                    string pname = allStates[i + 3]; // 0..6 → PARA_STATE_4..10
                    enabled[i] = ReadParaStateBool(typeEl, pname)
                              || ReadParaStateBool(el,     pname);
                }

                string[] tierStrings = tag7.Tier4Summaries; // T4..T10
                bool anyTierAppended = false;
                var tierAppend = new System.Text.StringBuilder();

                for (int i = 0; i < tierStrings.Length; i++)
                {
                    if (!enabled[i] || string.IsNullOrEmpty(tierStrings[i])) continue;
                    string label = $"T{i + 4}";
                    tierAppend.Append(" | ").Append(label).Append(": ").Append(tierStrings[i]);
                    anyTierAppended = true;
                }

                if (anyTierAppended)
                {
                    // Resolve active pattern mode for prefix; default to DC.
                    string mode = ResolveActivePatternMode(typeEl, el);
                    tag7Final = tag7Final + " | [" + mode + "]" + tierAppend.ToString();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("WriteTag7All T4-T10 append failed: " + ex.Message);
            }

            // ── Warning parameter population (v5.6) ────────────────────────
            // combined warning evaluation. The previous
            // code called PopulateWarningParameters AND EvaluateElementWarnings,
            // each walking the same GetCategoryWarnings list, calling the same
            // GetWarningDataValue per warning, calling the same EvaluateWarning.
            // The new EvaluateAndPopulateWarnings does both in one pass and
            // returns (writtenCount, concatenatedText).
            var warnPass = EvaluateAndPopulateWarnings(doc, el, categoryName);
            written += warnPass.WrittenCount;
            string warningText = warnPass.ConcatenatedText;
            if (!string.IsNullOrEmpty(warningText)
                && !string.IsNullOrEmpty(tag7Final)
                && !tag7Final.Contains(warningText))
            {
                tag7Final = tag7Final + " | " + warningText;
            }

            // Single TAG7 write covers narrative + appended warnings.
            if (!string.IsNullOrEmpty(tag7Final))
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.TAG7, tag7Final, overwrite))
                    written++;
            }

            // ── Paragraph container write (v5.5 + Phase 165 Issue #22) ───
            // Write the full plain narrative to the category-specific paragraph parameter
            string paraContainer = ParamRegistry.GetParagraphContainer(categoryName);

            // Phase 165 — Issue #22. Clear stale paragraph data before
            // re-writing. When an element changes category or pattern mode,
            // an old narrative could otherwise persist in containers that
            // should now be empty (each category has its own paragraph
            // container, and the active one varies). Iterate the registered
            // container set and blank every container EXCEPT the one we're
            // about to write so the active payload survives.
            //
            // Phase 165 perf — skip the entire clear pass when the active
            // paragraph container hasn't changed since the last write. We
            // stamp ASS_LAST_PARA_CONTAINER_TXT after a successful clear and
            // re-read it next time. If the value matches the active
            // container, no other container can have stale data on this
            // element (only WriteTag7All ever writes them). The pass falls
            // through to the actual write below either way.
            const string LAST_PARA_PARAM = "ASS_LAST_PARA_CONTAINER_TXT";
            string lastPara = ParameterHelpers.GetString(el, LAST_PARA_PARAM);
            bool needClear = !string.Equals(lastPara, paraContainer ?? "", StringComparison.Ordinal);
            if (needClear)
            {
                try
                {
                    string[] allParas = ParamRegistry.AllParagraphContainers;
                    for (int i = 0; i < allParas.Length; i++)
                    {
                        string containerName = allParas[i];
                        if (string.IsNullOrEmpty(containerName)) continue;
                        if (!string.IsNullOrEmpty(paraContainer)
                            && string.Equals(containerName, paraContainer, StringComparison.Ordinal))
                            continue; // skip the active container — we're writing it next.
                        // Phase 165 perf — CachedLookup short-circuits the
                        // LookupParameter cost when the same family carries
                        // (or doesn't carry) this container parameter.
                        Parameter p = ParameterHelpers.CachedLookup(el, containerName);
                        if (p == null || p.IsReadOnly) continue;
                        if (p.StorageType != StorageType.String) continue;
                        string cur = p.AsString();
                        if (string.IsNullOrEmpty(cur)) continue;
                        try { p.Set(string.Empty); written++; } catch { /* defensive */ }
                    }
                    // Stamp the new active container name so the next pass
                    // can short-circuit when nothing has changed.
                    ParameterHelpers.SetString(el, LAST_PARA_PARAM, paraContainer ?? "", overwrite: true);
                }
                catch (Exception ex2)
                {
                    StingLog.Warn("WriteTag7All paragraph clear pass failed: " + ex2.Message);
                }
            }

            if (!string.IsNullOrEmpty(paraContainer) && !string.IsNullOrEmpty(tag7.PlainNarrative))
            {
                // Use a local StringBuilder to avoid intermediate string allocations when
                // warnings are appended — one paraText per element across 1000-element batches
                // was generating 1000 throwaway string objects.
                var paraBuilder = new System.Text.StringBuilder(tag7.PlainNarrative);
                if (!string.IsNullOrEmpty(warningText))
                {
                    paraBuilder.Append(" | WARNINGS: ");
                    paraBuilder.Append(warningText);
                }
                if (ParameterHelpers.SetString(el, paraContainer, paraBuilder.ToString(), overwrite))
                    written++;
            }

            return written;
        }

        /// <summary>
        /// Phase 165 — read a TAG_PARA_STATE_N_BOOL parameter. Mirrors the
        /// Yes/No vs. integer storage handling in SetParagraphDepthCommand.
        /// Treats missing parameter as false.
        /// </summary>
        public static bool ReadParaStateBool(Element host, string paramName)
        {
            if (host == null || string.IsNullOrEmpty(paramName)) return false;
            // Phase 165 perf — use the shared parameter cache so the same
            // (typeId, paramName) pair pays the LookupParameter cost only once
            // per session. WriteTag7All calls this 14× per element so a
            // 1000-element batch was 14000 fresh LookupParameter calls; with
            // CachedLookup most calls become an O(1) Definition fetch.
            Parameter p = ParameterHelpers.CachedLookup(host, paramName);
            if (p == null) return false;
            try
            {
                if (p.StorageType == StorageType.String)
                {
                    string s = p.AsString();
                    if (string.IsNullOrEmpty(s)) return false;
                    return s.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                if (p.StorageType == StorageType.Integer) return p.AsInteger() != 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        /// <summary>
        /// Phase 165 — resolve active pattern mode (HANDOVER / DC / CUSTOM) by
        /// inspecting the type then instance. Defaults to DC if none set.
        /// </summary>
        public static string ResolveActivePatternMode(Element typeEl, Element instEl)
        {
            // Type takes precedence (matches paragraph-state location).
            if (ReadParaStateBool(typeEl, ParamRegistry.MODE_HANDOVER)) return "HANDOVER";
            if (ReadParaStateBool(typeEl, ParamRegistry.MODE_CUSTOM))   return "CUSTOM";
            if (ReadParaStateBool(typeEl, ParamRegistry.MODE_DC))       return "DC";
            if (ReadParaStateBool(instEl, ParamRegistry.MODE_HANDOVER)) return "HANDOVER";
            if (ReadParaStateBool(instEl, ParamRegistry.MODE_CUSTOM))   return "CUSTOM";
            if (ReadParaStateBool(instEl, ParamRegistry.MODE_DC))       return "DC";
            return "DC"; // default per Phase 165 spec
        }

        /// <summary>
        /// Phase 165 — read active paragraph depth (1-10) on an element type.
        /// Returns the highest enabled PARA_STATE_N (cumulative scheme).
        /// Defaults to 3 if no states are enabled (legacy "Comprehensive").
        /// </summary>
        public static int ReadActiveParagraphDepth(Element typeEl, Element instEl)
        {
            // Phase 165 perf — use the cached ParamRegistry.AllParaStates
            // array instead of allocating a 10-string array per call.
            string[] paraNames = ParamRegistry.AllParaStates;
            int max = 0;
            for (int i = 0; i < paraNames.Length; i++)
            {
                if (ReadParaStateBool(typeEl, paraNames[i]) ||
                    ReadParaStateBool(instEl, paraNames[i]))
                    max = i + 1;
            }
            return max == 0 ? 3 : max;
        }

        /// <summary>
        /// Evaluate all applicable warning thresholds for an element.
        /// Returns a concatenated warning string, or null if no warnings triggered.
        /// Respects TAG_WARN_VISIBLE_BOOL and TAG_WARN_SEVERITY_FILTER_TXT.
        /// </summary>
        /// <summary>EFF-07 (Phase 149d): one-pass replacement for the
        /// PopulateWarningParameters + EvaluateElementWarnings combo. Both
        /// legacy methods walked the same warning list and called the same
        /// per-warning helpers; this method does it once, returning the
        /// number of WARN_ params written and the concatenated narrative
        /// fragment for the TAG7 append.</summary>
        public static (int WrittenCount, string ConcatenatedText)
            EvaluateAndPopulateWarnings(Document doc, Element el, string categoryName)
        {
            if (el == null || string.IsNullOrEmpty(categoryName))
                return (0, null);

            // Visibility gate (matches EvaluateElementWarnings).
            string warnVisible = ParameterHelpers.GetString(el, ParamRegistry.WARN_VISIBLE);
            bool visible = !(warnVisible == "No" || warnVisible == "0"
                || warnVisible == "FALSE" || warnVisible == "false");

            // Severity filter (matches EvaluateElementWarnings).
            string severityFilter = ParameterHelpers.GetString(el, ParamRegistry.WARN_SEVERITY_FILTER);
            if (string.IsNullOrEmpty(severityFilter)) severityFilter = "ALL";
            int filterLevel = severityFilter == "ALL" ? 0 : SeverityLevel(severityFilter);

            var warningParamNames = ParamRegistry.GetCategoryWarnings(categoryName);
            if (warningParamNames == null || warningParamNames.Count == 0)
                return (0, null);

            int written = 0;
            List<string> concat = null;

            foreach (string warnParam in warningParamNames)
            {
                if (!ParamRegistry.WarningThresholds.TryGetValue(warnParam, out var def))
                    continue;

                string dataValue = GetWarningDataValue(el, warnParam, categoryName);
                string evalResult = string.IsNullOrEmpty(dataValue)
                    ? null : ParamRegistry.EvaluateWarning(def, dataValue);

                // PopulateWarningParameters semantic — always overwrite to keep
                // params current; clear when no violation so stale text doesn't
                // linger.
                string warnText = string.IsNullOrEmpty(evalResult) ? "" : evalResult;
                if (ParameterHelpers.SetString(el, warnParam, warnText, overwrite: true))
                    written++;

                // EvaluateElementWarnings semantic — collect into concat string
                // when visible AND severity passes the filter AND there's a
                // violation to report.
                if (visible
                    && !string.IsNullOrEmpty(evalResult)
                    && (filterLevel == 0 || SeverityLevel(def.Severity) >= filterLevel))
                {
                    if (concat == null) concat = new List<string>();
                    concat.Add(evalResult);
                }
            }

            return (written, concat == null ? null : string.Join(" ", concat));
        }

        public static string EvaluateElementWarnings(Document doc, Element el, string categoryName)
        {
            // Check if warnings are enabled on this element
            string warnVisible = ParameterHelpers.GetString(el, ParamRegistry.WARN_VISIBLE);
            if (warnVisible == "No" || warnVisible == "0" || warnVisible == "FALSE" || warnVisible == "false")
                return null;

            // Get severity filter
            string severityFilter = ParameterHelpers.GetString(el, ParamRegistry.WARN_SEVERITY_FILTER);
            if (string.IsNullOrEmpty(severityFilter)) severityFilter = "ALL";

            // Get applicable warnings for this category
            var warningParamNames = ParamRegistry.GetCategoryWarnings(categoryName);
            if (warningParamNames == null || warningParamNames.Count == 0)
                return null;

            var warnings = new List<string>();
            foreach (string warnParam in warningParamNames)
            {
                if (!ParamRegistry.WarningThresholds.TryGetValue(warnParam, out var def))
                    continue;

                // Apply severity filter
                if (severityFilter != "ALL")
                {
                    int filterLevel = SeverityLevel(severityFilter);
                    int defLevel = SeverityLevel(def.Severity);
                    if (defLevel < filterLevel) continue; // Skip lower-severity warnings
                }

                // Try to get the element's current value for comparison
                // Map the warning param to its corresponding data parameter
                string dataValue = GetWarningDataValue(el, warnParam, categoryName);
                if (string.IsNullOrEmpty(dataValue)) continue;

                string warning = ParamRegistry.EvaluateWarning(def, dataValue);
                if (!string.IsNullOrEmpty(warning))
                    warnings.Add(warning);
            }

            return warnings.Count > 0 ? string.Join(" ", warnings) : null;
        }

        /// <summary>
        /// Populate individual WARN_ parameters on an element with their evaluated warning text.
        /// Each WARN_ parameter gets its own text so tag family labels can display them via
        /// calculated value formulas (Type: Text): if(TAG_WARN_VISIBLE_BOOL, WARN_xxx, "").
        /// All WARN_ parameters MUST be TEXT type to avoid Revit "Inconsistent Units" errors.
        /// Returns the number of WARN_ parameters written.
        /// </summary>
        public static int PopulateWarningParameters(Document doc, Element el, string categoryName)
        {
            if (el == null || string.IsNullOrEmpty(categoryName))
                return 0;

            var warningParamNames = ParamRegistry.GetCategoryWarnings(categoryName);
            if (warningParamNames == null || warningParamNames.Count == 0)
                return 0;

            int written = 0;
            foreach (string warnParam in warningParamNames)
            {
                if (!ParamRegistry.WarningThresholds.TryGetValue(warnParam, out var def))
                    continue;

                // Get the element's current measured value for this warning check
                string dataValue = GetWarningDataValue(el, warnParam, categoryName);

                string warningText;
                if (string.IsNullOrEmpty(dataValue))
                {
                    // No data available — write empty (tag label shows nothing)
                    warningText = "";
                }
                else
                {
                    string evalResult = ParamRegistry.EvaluateWarning(def, dataValue);
                    if (!string.IsNullOrEmpty(evalResult))
                    {
                        // Threshold violated — write the warning text
                        warningText = evalResult;
                    }
                    else
                    {
                        // Compliant — clear any previous warning
                        warningText = "";
                    }
                }

                // Write to the individual WARN_ parameter on the element
                // Always overwrite so warnings stay current with element data
                if (ParameterHelpers.SetString(el, warnParam, warningText, overwrite: true))
                    written++;
            }

            return written;
        }

        /// <summary>Get severity level as numeric (for filtering). Higher = more severe.</summary>
        private static int SeverityLevel(string severity)
        {
            switch (severity?.ToUpperInvariant())
            {
                case "CRITICAL": return 4;
                case "HIGH": return 3;
                case "MEDIUM": return 2;
                case "LOW": return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Map a warning parameter name to the actual data value on the element.
        /// Warning param names encode the type of check; this method finds the
        /// corresponding measured/actual value to compare against the threshold.
        /// </summary>
        private static string GetWarningDataValue(Element el, string warnParam, string categoryName)
        {
            // Map warning params to their corresponding data sources
            // Pattern: WARN_{prefix}_{metric} maps to the element's actual parameter
            string wp = warnParam.ToUpperInvariant();

            // U-value checks
            if (wp.Contains("U_VALUE"))
                return ParameterHelpers.GetString(el, "PER_THERM_U_VALUE_W_M2K");
            // Voltage drop
            if (wp.Contains("VLT_DROP"))
                return ParameterHelpers.GetString(el, ParamRegistry.ELC_VOLTAGE);
            // Velocity
            if (wp.Contains("VEL_MPS") && wp.Contains("HVC"))
                return ParameterHelpers.GetString(el, ParamRegistry.HVC_VELOCITY);
            if (wp.Contains("VEL_MPS") && wp.Contains("PLM"))
                return ParameterHelpers.GetString(el, ParamRegistry.PLM_VELOCITY);
            // Sound
            if (wp.Contains("SOUNDLVL"))
                return ParameterHelpers.GetString(el, "HVC_DCT_SOUNDLVL_DB");
            // Fire rating
            if (wp.Contains("FRR") || (wp.Contains("FIRE") && wp.Contains("RESISTANCE")))
                return ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING);
            // Floor load
            if (wp.Contains("FLR_LD_CAP"))
                return ParameterHelpers.GetString(el, "BLE_FLR_LD_CAP_KPA");
            // Wall height ratio
            if (wp.Contains("WALL_HEIGHT_RATIO"))
            {
                string h = ParameterHelpers.GetString(el, ParamRegistry.WALL_HEIGHT);
                string t = ParameterHelpers.GetString(el, ParamRegistry.WALL_THICKNESS);
                if (double.TryParse(h, out double hv) && double.TryParse(t, out double tv) && tv > 0)
                    return (hv / tv).ToString("F1");
                return null;
            }
            // Ramp slope
            if (wp.Contains("RAMP_SLOPE"))
                return ParameterHelpers.GetString(el, ParamRegistry.RAMP_SLOPE);
            // Stair dimensions
            if (wp.Contains("STAIR_RISE"))
                return ParameterHelpers.GetString(el, ParamRegistry.STAIR_RISE);
            if (wp.Contains("STAIR_GOING") || wp.Contains("STAIR_TREAD"))
                return ParameterHelpers.GetString(el, ParamRegistry.STAIR_TREAD);
            if (wp.Contains("STAIR_WIDTH"))
                return ParameterHelpers.GetString(el, ParamRegistry.STAIR_WIDTH);
            if (wp.Contains("STAIR_HEADROOM"))
                return ParameterHelpers.GetString(el, "BLE_STAIR_HEADROOM_MM");
            // Door height
            if (wp.Contains("DOOR_HEIGHT"))
                return ParameterHelpers.GetString(el, ParamRegistry.DOOR_HEIGHT);
            // Ceiling height
            if (wp.Contains("CEIL_HEIGHT"))
                return ParameterHelpers.GetString(el, ParamRegistry.CEILING_HEIGHT);
            // Room area
            if (wp.Contains("ROOM_AREA"))
                return ParameterHelpers.GetString(el, ParamRegistry.ROOM_AREA);
            // Room height
            if (wp.Contains("ROOM_HEIGHT"))
                return ParameterHelpers.GetString(el, "ASS_ROOM_HEIGHT_MM");
            // Roof slope
            if (wp.Contains("ROOF_SLOPE"))
                return ParameterHelpers.GetString(el, ParamRegistry.ROOF_SLOPE);
            // SHGC / window performance
            if (wp.Contains("SHGC"))
                return ParameterHelpers.GetString(el, "BLE_CW_PANEL_SHGC");
            // Window U-value
            if (wp.Contains("WINDOW_U_VALUE"))
                return ParameterHelpers.GetString(el, "BLE_WINDOW_U_VALUE_W_M_2K_NR");
            // Rail height
            if (wp.Contains("RAIL_HEIGHT"))
                return ParameterHelpers.GetString(el, "BLE_RAIL_HEIGHT_MM");
            // Pipe flow
            if (wp.Contains("FLW_LPS") || wp.Contains("FIXTURE_FLOW"))
                return ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_FLOW);
            // Fill ratio
            if (wp.Contains("FILL_RATIO"))
                return ParameterHelpers.GetString(el, "ELC_CDT_FILL_RATIO");
            // Access width
            if (wp.Contains("ACCESS_CLEAR_WIDTH") || wp.Contains("CORRIDOR_WIDTH"))
                return ParameterHelpers.GetString(el, ParamRegistry.DOOR_WIDTH);
            // Column slenderness
            if (wp.Contains("SLENDERNESS"))
                return ParameterHelpers.GetString(el, "BLE_COLUMN_SLENDERNESS");
            // Beam span/depth
            if (wp.Contains("SPAN_DEPTH"))
                return ParameterHelpers.GetString(el, "STR_BEAM_SPAN_DEPTH");
            // Beam deflection
            if (wp.Contains("DEFLECTION"))
                return ParameterHelpers.GetString(el, "STR_BEAM_DEFLECTION");
            // Rebar cover
            if (wp.Contains("RBR_COVER"))
                return ParameterHelpers.GetString(el, "STR_RBR_COVER_MM");
            // Insulation thickness
            if (wp.Contains("INS_THICKNESS"))
                return ParameterHelpers.GetString(el, ParamRegistry.HVC_INSULATION);
            // Illuminance
            if (wp.Contains("ILLUMINANCE"))
                return ParameterHelpers.GetString(el, "LTG_ILLUMINANCE_LUX");
            // Carbon footprint
            if (wp.Contains("CARBON"))
                return ParameterHelpers.GetString(el, "PER_SUST_CARBON_FOOTPRINT_KG");
            // Acoustic ratings
            if (wp.Contains("ACOUSTIC") && wp.Contains("STC"))
                return ParameterHelpers.GetString(el, "PER_ACOUSTIC_WALL_STC");
            if (wp.Contains("ACOUSTIC") && wp.Contains("IIC"))
                return ParameterHelpers.GetString(el, "PER_ACOUSTIC_FLOOR_IIC");
            // ELC efficiency
            if (wp.Contains("EFF_RATIO"))
                return ParameterHelpers.GetString(el, "HVC_EFF_RATIO_NR");
            // Short circuit
            if (wp.Contains("SHORT_CIRCUIT"))
                return ParameterHelpers.GetString(el, "ELC_PNL_SHORT_CIRCUIT_KA");
            // Spare ways
            if (wp.Contains("SPARE_WAYS"))
            // Pipe gradient
            if (wp.Contains("PIPE_GRADIENT"))
                return ParameterHelpers.GetString(el, "PLM_PIPE_GRADIENT_PCT");
            // Trap seal
            if (wp.Contains("TRAP_SEAL"))
                return ParameterHelpers.GetString(el, "PLM_TRAP_SEAL_MM");
            // Foundation bearing/depth
            if (wp.Contains("FDN_BEARING") || wp.Contains("BEARING_CAP"))
                return ParameterHelpers.GetString(el, "BLE_STRUCT_FDN_BEARING_KPA");
            if (wp.Contains("FDN_DEPTH"))
                return ParameterHelpers.GetString(el, "STR_FDN_DEPTH_MM");
            // Weld
            if (wp.Contains("WLD_STRENGTH"))
                return ParameterHelpers.GetString(el, "STR_WLD_STRENGTH_MPA");
            if (wp.Contains("WLD_THROAT"))
                return ParameterHelpers.GetString(el, "STR_WLD_THROAT_MM");
            // Connection capacity
            if (wp.Contains("CONN_CAPACITY"))
                return ParameterHelpers.GetString(el, "STR_CONN_CAPACITY_KN");
            // Sprinkler coverage
            if (wp.Contains("SPR_COVER") || wp.Contains("COVERAGE_AREA"))
                return ParameterHelpers.GetString(el, "FLS_SFTY_COVERAGE_AREA_SQ_M");
            // IP rating
            if (wp.Contains("IP_RATING"))
                return ParameterHelpers.GetString(el, ParamRegistry.ELC_IP_RATING);
            // VOC
            if (wp.Contains("VOC"))
                return ParameterHelpers.GetString(el, "PER_VOC_EMISSIONS_UG_M3");
            // Hanger load
            if (wp.Contains("HANGER_LOAD"))
                return ParameterHelpers.GetString(el, "FAB_HANGER_LOAD_KN");
            // Duct pressure drop
            if (wp.Contains("PRESSURE_DROP"))
                return ParameterHelpers.GetString(el, ParamRegistry.HVC_PRESSURE);
            // Parking width
            if (wp.Contains("PARKING_WIDTH"))
                return ParameterHelpers.GetString(el, "BLE_PARKING_WIDTH_MM");

            // Generic fallback: no mapping found
            return null;
        }

        /// <summary>Append a label:value pair to both plain and marked StringBuilders.</summary>
        private static void AppendLabelValue(System.Text.StringBuilder plain, System.Text.StringBuilder marked,
            string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (plain.Length > 0) { plain.Append(", "); marked.Append(", "); }
            plain.Append($"{label}: {value}");
            marked.Append($"\u00ABL\u00BB{label}:\u00AB/L\u00BB \u00ABV\u00BB{value}\u00AB/V\u00BB");
        }

        /// <summary>Build marked-up technical data with «L»label«/L» «V»value«/V» tokens.</summary>
        private static string BuildMarkedTechSection(Element el, string disc, string categoryName)
        {
            var sb = new System.Text.StringBuilder();
            void AddM(string paramName, string connector, string unit)
            {
                string v = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(v))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"\u00ABL\u00BB{connector}\u00AB/L\u00BB \u00ABV\u00BB{v}{(string.IsNullOrEmpty(unit) ? "" : $" {unit}")}\u00AB/V\u00BB");
                }
            }
            if (disc == "E" || categoryName == "Electrical Equipment" || categoryName == "Electrical Fixtures")
            {
                AddM(ParamRegistry.ELC_POWER, "rated at", "kW");
                AddM(ParamRegistry.ELC_VOLTAGE, "operating at", "V");
                AddM(ParamRegistry.ELC_CIRCUIT_NR, "connected to circuit", "");
                AddM(ParamRegistry.ELC_PNL_NAME, "supplied by panel", "");
                AddM(ParamRegistry.ELC_PHASES, "configured for", "phase supply");
                AddM(ParamRegistry.ELC_PNL_FED_FROM, "fed from", "");
                AddM(ParamRegistry.ELC_MAIN_BRK, "protected by a", "A main breaker");
                AddM(ParamRegistry.ELC_WAYS, "with", "ways");
                AddM(ParamRegistry.ELC_IP_RATING, "sealed to IP", "");
                AddM(ParamRegistry.ELC_PNL_LOAD, "carrying a connected load of", "kW");
            }
            else if (categoryName == "Lighting Fixtures" || categoryName == "Lighting Devices")
            {
                AddM(ParamRegistry.LTG_WATTAGE, "consuming", "W");
                AddM(ParamRegistry.LTG_LUMENS, "delivering", "lm of luminous output");
                AddM(ParamRegistry.LTG_EFFICACY, "achieving an efficacy of", "lm/W");
                AddM(ParamRegistry.LTG_LAMP_TYPE, "using a", "lamp");
                AddM(ParamRegistry.ELC_CIRCUIT_NR, "wired to circuit", "");
            }
            else if (disc == "M" || categoryName == "Mechanical Equipment" || categoryName == "Ducts" ||
                     categoryName == "Air Terminals" || categoryName == "Duct Fittings")
            {
                AddM(ParamRegistry.HVC_AIRFLOW, "delivering an airflow of", "L/s");
                AddM(ParamRegistry.HVC_DUCT_FLOW, "with a duct flow of", "CFM");
                AddM(ParamRegistry.HVC_VELOCITY, "at a velocity of", "m/s");
                AddM(ParamRegistry.HVC_PRESSURE, "against a pressure drop of", "Pa");
            }
            else if (disc == "P" || categoryName == "Pipes" || categoryName == "Plumbing Fixtures" || categoryName == "Pipe Fittings")
            {
                AddM(ParamRegistry.PLM_PIPE_FLOW, "conveying a flow of", "L/s");
                AddM(ParamRegistry.PLM_PIPE_SIZE, "through", "mm diameter pipework");
                AddM(ParamRegistry.PLM_VELOCITY, "at a velocity of", "m/s");
                AddM(ParamRegistry.PLM_FLOW_RATE, "with a design flow rate of", "L/s");
                AddM(ParamRegistry.PLM_PIPE_LENGTH, "running", "m in length");
            }
            else if (disc == "FP" || categoryName == "Sprinklers" || categoryName == "Fire Alarm Devices")
            {
                AddM(ParamRegistry.FIRE_RATING, "providing", "minutes of fire resistance");
            }
            return sb.ToString();
        }

        /// <summary>Build marked-up dimensional data with natural language connectors and «L»/«V» tokens.</summary>
        private static string BuildMarkedDimSection(Element el, string categoryName)
        {
            var sb = new System.Text.StringBuilder();
            void AddM(string paramName, string connector, string unit)
            {
                string v = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(v))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"\u00ABL\u00BB{connector}\u00AB/L\u00BB \u00ABV\u00BB{v}{(string.IsNullOrEmpty(unit) ? "" : $" {unit}")}\u00AB/V\u00BB");
                }
            }
            if (categoryName == "Walls")
            {
                AddM(ParamRegistry.WALL_HEIGHT, "standing", "mm high");
                AddM(ParamRegistry.WALL_LENGTH, "spanning", "mm in length");
                AddM(ParamRegistry.WALL_THICKNESS, "with a thickness of", "mm");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
                AddM(ParamRegistry.FIRE_RATING, "achieving", "minutes of fire resistance");
                AddM(ParamRegistry.STRUCT_TYPE, "classified structurally as", "");
            }
            else if (categoryName == "Doors")
            {
                AddM(ParamRegistry.DOOR_WIDTH, "measuring", "mm wide");
                AddM(ParamRegistry.DOOR_HEIGHT, "by", "mm high");
                AddM(ParamRegistry.FIRE_RATING, "with", "minutes of fire resistance");
            }
            else if (categoryName == "Windows")
            {
                AddM(ParamRegistry.WINDOW_WIDTH, "measuring", "mm wide");
                AddM(ParamRegistry.WINDOW_HEIGHT, "by", "mm high");
                AddM(ParamRegistry.WINDOW_SILL, "set at a sill height of", "mm");
            }
            else if (categoryName == "Floors")
            {
                AddM(ParamRegistry.FLR_THICKNESS, "with a build-up of", "mm thick");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
                AddM(ParamRegistry.STRUCT_TYPE, "classified structurally as", "");
                AddM(ParamRegistry.FIRE_RATING, "achieving", "minutes of fire resistance");
            }
            else if (categoryName == "Ceilings")
            {
                AddM(ParamRegistry.CEILING_HEIGHT, "suspended at", "mm above floor level");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
            }
            else if (categoryName == "Roofs")
            {
                AddM(ParamRegistry.ROOF_SLOPE, "pitched at", "degrees");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
            }
            else if (categoryName == "Stairs")
            {
                AddM(ParamRegistry.STAIR_TREAD, "with treads", "mm deep");
                AddM(ParamRegistry.STAIR_RISE, "risers of", "mm");
                AddM(ParamRegistry.STAIR_WIDTH, "and a clear width of", "mm");
            }
            else if (categoryName == "Ramps")
            {
                AddM(ParamRegistry.RAMP_SLOPE, "inclined at", "%");
                AddM(ParamRegistry.RAMP_WIDTH, "with a clear width of", "mm");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Build discipline-specific technical data section for TAG7 narrative.
        /// Reads electrical ratings, HVAC airflow, plumbing flow rates, and lighting
        /// performance data based on the element's discipline code.
        /// </summary>
        private static string BuildDisciplineTechSection(Element el, string disc, string categoryName)
        {
            var tech = new System.Text.StringBuilder();

            if (disc == "E" || categoryName == "Electrical Equipment" || categoryName == "Electrical Fixtures")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_POWER), "rated at {0} kW");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_VOLTAGE), "operating at {0} V");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_CIRCUIT_NR), "connected to circuit {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PNL_NAME), "supplied by panel {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PHASES), "configured for {0} phase supply");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PNL_FED_FROM), "fed from {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_MAIN_BRK), "protected by a {0} A main breaker");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_WAYS), "with {0} ways");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_IP_RATING), "sealed to IP {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PNL_LOAD), "carrying a connected load of {0} kW");
            }
            else if (categoryName == "Lighting Fixtures" || categoryName == "Lighting Devices")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_WATTAGE), "consuming {0} W");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_LUMENS), "delivering {0} lm of luminous output");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_EFFICACY), "achieving an efficacy of {0} lm/W");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_LAMP_TYPE), "using a {0} lamp");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_CIRCUIT_NR), "wired to circuit {0}");
            }
            else if (disc == "M" || categoryName == "Mechanical Equipment" || categoryName == "Ducts" ||
                     categoryName == "Air Terminals" || categoryName == "Duct Fittings")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_AIRFLOW), "delivering an airflow of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_DUCT_FLOW), "with a duct flow of {0} CFM");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_VELOCITY), "at a velocity of {0} m/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_PRESSURE), "against a pressure drop of {0} Pa");
            }
            else if (disc == "P" || categoryName == "Pipes" || categoryName == "Plumbing Fixtures" ||
                     categoryName == "Pipe Fittings")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_FLOW), "conveying a flow of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_SIZE), "through {0} mm diameter pipework");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_VELOCITY), "at a velocity of {0} m/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_FLOW_RATE), "with a design flow rate of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_LENGTH), "running {0} m in length");
            }
            else if (disc == "FP" || categoryName == "Sprinklers" || categoryName == "Fire Alarm Devices")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "providing {0} minutes of fire resistance");
            }
            else if (disc == "H" || categoryName == "Rooms" && !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "CLN_ROOM_CLASS_TXT")))
            {
                // Healthcare: Clinical Room
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_ROOM_CLASS_TXT"), "classified as a {0} clinical space");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_PRESS_REGIME_TXT"), "operating under {0} pressure regime");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_INFECT_CLASS_TXT"), "with infection control class {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_HTM_REF_TXT"), "per {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_ADB_CODE_TXT"), "ADB room code {0}");
            }
            else if (disc == "MG" || categoryName == "Pipes" && !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "MGS_GAS_TYPE_TXT")))
            {
                // Healthcare: Medical Gas
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_GAS_TYPE_TXT"), "carrying {0} medical gas");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_DESIGN_FLOW_LS_NR"), "at a design flow of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_DESIGN_PRESS_KPA_NR"), "at {0} kPa design pressure");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_OUTLET_COUNT_INT"), "serving {0} outlets");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_NFPA99_ZONE_TXT"), "in NFPA 99 zone {0}");
            }
            else if (disc == "RP" || !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "RAD_LEAD_MM_NR")))
            {
                // Healthcare: Radiation Protection
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_LEAD_MM_NR"), "with {0} mm Pb shielding");
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_MODALITY_TXT"), "protecting against {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_WORKLOAD_WK_NR"), "workload {0} mA·min/wk");
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_QE_NAME_TXT"), "certified by {0}");
            }

            return tech.Length > 0 ? tech.ToString() : "";
        }

        /// <summary>
        /// Build dimensional properties section for TAG7 narrative.
        /// Reads category-specific BLE dimensional parameters (height, width, thickness,
        /// area, slope, fire rating) for building elements.
        /// </summary>
        private static string BuildDimensionalSection(Element el, string categoryName)
        {
            var dim = new System.Text.StringBuilder();

            if (categoryName == "Walls")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WALL_HEIGHT), "standing {0} mm high");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WALL_LENGTH), "spanning {0} mm in length");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WALL_THICKNESS), "with a thickness of {0} mm");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "achieving {0} minutes of fire resistance");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STRUCT_TYPE), "classified structurally as {0}");
            }
            else if (categoryName == "Doors")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.DOOR_WIDTH), "measuring {0} mm wide");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.DOOR_HEIGHT), "by {0} mm high");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "with {0} minutes of fire resistance");
            }
            else if (categoryName == "Windows")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WINDOW_WIDTH), "measuring {0} mm wide");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WINDOW_HEIGHT), "by {0} mm high");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WINDOW_SILL), "set at a sill height of {0} mm");
            }
            else if (categoryName == "Floors")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FLR_THICKNESS), "with a build-up of {0} mm thick");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STRUCT_TYPE), "classified structurally as {0}");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "achieving {0} minutes of fire resistance");
            }
            else if (categoryName == "Ceilings")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.CEILING_HEIGHT), "suspended at {0} mm above floor level");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
            }
            else if (categoryName == "Roofs")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ROOF_SLOPE), "pitched at {0} degrees");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
            }
            else if (categoryName == "Stairs")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STAIR_TREAD), "with treads {0} mm deep");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STAIR_RISE), "risers of {0} mm");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STAIR_WIDTH), "and a clear width of {0} mm");
            }
            else if (categoryName == "Ramps")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.RAMP_SLOPE), "inclined at {0}%");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.RAMP_WIDTH), "with a clear width of {0} mm");
            }

            return dim.Length > 0 ? dim.ToString() : "";
        }

        /// <summary>
        /// Append a natural-language phrase to a StringBuilder if the value is not empty.
        /// Uses comma separators between items for prose-like flow.
        /// </summary>
        private static void AppendNatural(System.Text.StringBuilder sb, string value, string format)
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(string.Format(format, value));
            }
        }
    }
}
