using StingTools.Core;
// StingTools — Drawing Template Manager · Title-block factory POCOs
//
// Spec model loaded from Data/STING_TITLE_BLOCKS.json. Each
// TitleBlockSpec describes one .rfa family the generator will mint:
// the .rft template to start from, the shared parameters to add,
// the geometry (lines / labels / static text / filled regions) to
// draw, and the viewport slots the Drawing-Type / Sheet-Manager
// system uses to auto-place viewports.
//
// Phase 170 Revision (Two-Family BIM Architecture):
//   The Phase 170 original used a hybrid BIM-mode toggle
//   (Strategy A `Visible`-bind + Strategy B reflow-groups +
//   two-label trick for SHEET_FULL_REF). All three are removed.
//   Each paper size now ships TWO families — `STING_TB_<SIZE>_BIM_*`
//   and `STING_TB_<SIZE>_NONBIM_*` — declared independently in JSON
//   with optional `extends` inheritance from a shared abstract base
//   so duplication stays low. See docs/TITLE_BLOCK_FAMILY_DESIGN.md
//   §3.1 for the rationale.
//
// All coordinates in mm relative to the title-block sheet's
// bottom-left (consistent with Revit family-doc convention). The
// generator converts to internal feet at place time.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>Root JSON document — list of families to mint.</summary>
    public sealed class TitleBlockLibrary
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 2;
        [JsonProperty("name")]          public string Name { get; set; }
        [JsonProperty("description")]   public string Description { get; set; }
        [JsonProperty("lastUpdated")]   public string LastUpdated { get; set; }
        [JsonProperty("families")]      public List<TitleBlockSpec> Families { get; set; }
            = new List<TitleBlockSpec>();
    }

    /// <summary>Single .rfa family — OR an abstract base that other
    /// families extend (set <see cref="Abstract"/> = true to skip the
    /// .rfa generation step but still allow the spec to be referenced
    /// via <see cref="Extends"/>).</summary>
    public sealed class TitleBlockSpec
    {
        [JsonProperty("id")]                   public string Id { get; set; }
        [JsonProperty("description")]          public string Description { get; set; }

        /// <summary>Optional id of a parent spec to merge from. When set,
        /// the parent's parameters / lines / staticText / labels /
        /// filledRegions / slots collections are concatenated under this
        /// child's, and the parent's scalar fields (templateRft,
        /// category, mode) are inherited if this child leaves them
        /// blank. Children win on collision (same parameter name,
        /// same slot id). Loop detection in TitleBlockSpecRegistry.</summary>
        [JsonProperty("extends")]              public string Extends { get; set; }

        /// <summary>If true, the factory does NOT mint a .rfa for this
        /// spec — it exists purely as a base for other families to
        /// extend. Use for `A1_common_v2.0` / `A0_common_v2.0` etc.</summary>
        [JsonProperty("abstract")]             public bool   Abstract { get; set; }

        /// <summary>"BIM" or "NONBIM" — written into the family's
        /// STING_SHEET_BIM_MODE_TXT shared parameter as the default
        /// value, so each sheet inherits the right marker. Empty for
        /// abstract bases and for specialty families that aren't
        /// BIM-mode-relevant (cover sheet, transmittal cover, etc.).</summary>
        [JsonProperty("mode")]                 public string Mode { get; set; }

        /// <summary>Path to the .rft Revit family template, relative to
        /// Revit's family-template root, OR absolute. Generator searches
        /// both paths; first existing wins.</summary>
        [JsonProperty("templateRft")]          public string TemplateRft { get; set; }

        /// <summary>Output path for the .rfa, relative to the project
        /// directory or absolute. Generator creates directories as
        /// needed. Required for non-abstract specs.</summary>
        [JsonProperty("saveAs")]               public string SaveAs { get; set; }

        [JsonProperty("category")]             public string Category { get; set; }
            = "OST_TitleBlocks";

        [JsonProperty("parameters")]   public List<ParamSpec>        Parameters    { get; set; } = new List<ParamSpec>();
        [JsonProperty("lines")]        public List<LineSpec>         Lines         { get; set; } = new List<LineSpec>();
        [JsonProperty("staticText")]   public List<StaticTextSpec>   StaticText    { get; set; } = new List<StaticTextSpec>();
        [JsonProperty("labels")]       public List<LabelSpec>        Labels        { get; set; } = new List<LabelSpec>();
        [JsonProperty("filledRegions")]public List<FilledRegionSpec> FilledRegions { get; set; } = new List<FilledRegionSpec>();

        /// <summary>Viewport slots for the Drawing-Type / Sheet Manager
        /// system. Each slot defines a placement zone the consumer can
        /// drop a viewport into — the factory authors reference planes
        /// at the slot bounds, places a small slot-number marker at the
        /// top-left corner, and the slot definitions are echoed back to
        /// the build report so the operator can inspect the layout.</summary>
        [JsonProperty("slots")]        public List<SlotSpec>         Slots         { get; set; } = new List<SlotSpec>();
    }

    /// <summary>One shared / family parameter to add via FamilyManager.</summary>
    public sealed class ParamSpec
    {
        [JsonProperty("name")]      public string Name { get; set; }
        [JsonProperty("instance")]  public bool   Instance { get; set; } = true;

        /// <summary>"shared" pulls from the shared parameter file by
        /// name; "internal" creates a family-internal parameter. Default
        /// "shared".</summary>
        [JsonProperty("kind")]      public string Kind { get; set; } = "shared";

        /// <summary>Used only when kind = "internal". One of
        /// "Text", "YesNo", "Length", "Number", "Integer". Default
        /// "Text".</summary>
        [JsonProperty("type")]      public string Type { get; set; } = "Text";

        /// <summary>Parameter group — "IdentityData", "General",
        /// "Geometry", "Constraints". Default "IdentityData".</summary>
        [JsonProperty("group")]     public string Group { get; set; } = "IdentityData";

        /// <summary>Optional formula for family-internal calculated
        /// parameters. Subject to Revit's family-formula parser
        /// constraints (see docs/TITLE_BLOCK_FAMILY_DESIGN.md §3.3).</summary>
        [JsonProperty("formula")]   public string Formula { get; set; }

        /// <summary>Optional default value (used when no formula).</summary>
        [JsonProperty("default")]   public string Default { get; set; }
    }

    /// <summary>Detail line on the title-block view.</summary>
    public sealed class LineSpec
    {
        [JsonProperty("from")]    public double[] From { get; set; }   // [x, y] mm
        [JsonProperty("to")]      public double[] To   { get; set; }
        [JsonProperty("style")]   public string   Style { get; set; } = "Medium Lines";
    }

    /// <summary>Static text — cell label like "CLIENT", not bound
    /// to a parameter.</summary>
    public sealed class StaticTextSpec
    {
        [JsonProperty("text")]    public string   Text { get; set; }
        [JsonProperty("anchor")]  public double[] Anchor { get; set; }   // [x, y] mm
        [JsonProperty("size")]    public double   Size { get; set; } = 1.8;  // mm text height
        [JsonProperty("hAlign")]  public string   HAlign { get; set; } = "Left";
        [JsonProperty("vAlign")]  public string   VAlign { get; set; } = "Middle";
        [JsonProperty("textTypeName")] public string TextTypeName { get; set; }
        [JsonProperty("visibility")] public string Visibility { get; set; }
    }

    /// <summary>Label bound to a single family parameter.</summary>
    public sealed class LabelSpec
    {
        [JsonProperty("param")]   public string   Param { get; set; }
        [JsonProperty("anchor")]  public double[] Anchor { get; set; }
        [JsonProperty("size")]    public double   Size { get; set; } = 2.5;
        [JsonProperty("hAlign")]  public string   HAlign { get; set; } = "Left";
        [JsonProperty("vAlign")]  public string   VAlign { get; set; } = "Middle";
        [JsonProperty("prefix")]  public string   Prefix { get; set; } = "";
        [JsonProperty("suffix")]  public string   Suffix { get; set; } = "";
    }

    /// <summary>Filled region — typically the suitability chip / status
    /// band background. fill = solid colour name; for solid fills the
    /// generator picks the project's "Solid Fill" pattern.</summary>
    public sealed class FilledRegionSpec
    {
        [JsonProperty("topLeft")]     public double[] TopLeft { get; set; }      // [x, y] mm
        [JsonProperty("bottomRight")] public double[] BottomRight { get; set; }
        [JsonProperty("fillTypeName")] public string  FillTypeName { get; set; } = "Solid fill - Light Grey";
        [JsonProperty("color")]       public string   Color { get; set; }        // "#RRGGBB", optional
    }

    /// <summary>Viewport slot. Coordinates are mm relative to the
    /// title-block sheet bottom-left, same as every other coord in this
    /// spec. The Drawing-Type / Sheet-Manager system reads slot bounds
    /// back from the .rfa via the named reference planes
    /// (`<id>_TOP/BOT/LFT/RGT`) and routes viewports here based on
    /// <see cref="PurposeTag"/>.</summary>
    public sealed class SlotSpec
    {
        [JsonProperty("id")]          public string   Id { get; set; }            // "S01" / "S02" / "MAIN" — used for the corner marker label
        [JsonProperty("anchor")]      public double[] Anchor { get; set; }        // [x, y] mm — bottom-left corner of the slot
        [JsonProperty("size")]        public double[] Size   { get; set; }        // [w, h] mm
        [JsonProperty("description")] public string   Description { get; set; }   // human-readable purpose

        /// <summary>Routing key used by TitleBlock_AutoPlaceViewports —
        /// `STING_VIEWPORT_PLACEMENT_RULES.json` maps a view's
        /// (ViewType, name pattern) to a purpose tag, the auto-placer
        /// then scans the active sheet's title-block .rfa for the slot
        /// with this tag. See docs/title_blocks/SLOT_TAXONOMY.md for the
        /// canonical vocabulary.</summary>
        [JsonProperty("purposeTag")]  public string   PurposeTag { get; set; }

        /// <summary>Slot category, controls visual treatment in previews
        /// and auto-placer behaviour:
        ///   "primary"   — main drawable area (full / half / quad)
        ///   "auxiliary" — content slot for key-plans / notes / legends
        ///                 / schedules / revision-history etc.
        ///   "symbol"    — small-graphic slot for north arrow / scale bar
        ///                 / discipline colour chip / QR code
        ///   "overlay"   — sits on top of another slot (e.g. caption over
        ///                 a render, RFI markup over a base plan)
        /// </summary>
        [JsonProperty("category")]    public string   Category { get; set; } = "primary";

        /// <summary>Optional viewport type to apply when placing a view
        /// into this slot (e.g. "Title w/ Line", "No Title"). Resolved
        /// at placement time against the project's loaded ElementType
        /// of class Viewport.</summary>
        [JsonProperty("viewportType")] public string  ViewportType { get; set; }

        /// <summary>Optional default scale denominator (e.g. 100 for
        /// 1:100). Auto-placer applies this when the view's scale is
        /// "Use view scale" and the slot has a fixed expectation.</summary>
        [JsonProperty("scaleHint")]   public int?     ScaleHint { get; set; }

        /// <summary>Optional rotation (degrees, 0 / 90 / 180 / 270). Used
        /// by the auto-placer when a slot has a fixed orientation
        /// (e.g. a vertical revision-history strip on a landscape sheet
        /// hosts a 90°-rotated schedule view).</summary>
        [JsonProperty("rotation")]    public int      Rotation { get; set; } = 0;

        /// <summary>If true, the auto-placer respects the view's natural
        /// extents instead of stretching to fill the slot. Use for
        /// presentation slots where aspect-ratio matters.</summary>
        [JsonProperty("aspectLock")]  public bool     AspectLock { get; set; } = false;

        /// <summary>Optional explicit hex colour for SVG previews and the
        /// dock-panel slot-overview UI. When null, the renderer picks a
        /// canonical colour from the purposeTag → palette map (see
        /// tools/generate_title_block_previews.py PURPOSE_PALETTE).</summary>
        [JsonProperty("previewColor")] public string  PreviewColor { get; set; }

        /// <summary>Optional STING command tag that knows how to populate
        /// this slot when no view matches the purposeTag — e.g. an
        /// empty notes slot can route to `Legend_BuildNotes` to mint a
        /// project-default notes legend on demand.</summary>
        [JsonProperty("automationHook")] public string AutomationHook { get; set; }

        /// <summary>If true, the operator's `TB_SHOW_*_BOOL` toggle on
        /// the title-block instance can hide this slot's contents at
        /// sheet level. Mapping by purposeTag:
        ///   key-plan         → TB_SHOW_KEY_PLAN_BOOL
        ///   north-arrow      → TB_SHOW_NORTH_ARROW_BOOL
        ///   scale-bar        → TB_SHOW_SCALEBAR_BOOL
        ///   revision-history → TB_SHOW_REV_TABLE_BOOL
        ///   notes            → (no toggle — always visible)
        ///   discipline-band  → TB_SHOW_DISCIPLINE_COLOR_STRIP_BOOL
        ///   qr-code          → TB_SHOW_QR_CODE_BOOL
        /// </summary>
        [JsonProperty("respectShowToggle")] public bool RespectShowToggle { get; set; } = false;

        /// <summary>If true (default), the factory authors 4 reference
        /// planes (top / bottom / left / right) at the slot bounds so
        /// the user can dimension off them and drag attached viewports.
        /// Set false for slots that are markup-only.</summary>
        [JsonProperty("createReferencePlanes")] public bool CreateReferencePlanes { get; set; } = true;

        /// <summary>If true (default), the factory drops a small
        /// text-note slot-id marker at the top-left corner of the
        /// slot so the operator can see slot identifiers when authoring
        /// the title block.</summary>
        [JsonProperty("showCornerMarker")]      public bool ShowCornerMarker      { get; set; } = true;
    }

    /// <summary>Loader — corporate baseline at Data/STING_TITLE_BLOCKS.json,
    /// no project override yet (Phase 171). Resolves <see cref="TitleBlockSpec.Extends"/>
    /// inheritance via <see cref="TitleBlockLibrary"/>.Resolve.</summary>
    public static class TitleBlockSpecRegistry
    {
        public static TitleBlockLibrary Load()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_TITLE_BLOCKS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("TitleBlockSpecRegistry: STING_TITLE_BLOCKS.json not found");
                    return null;
                }
                return JsonConvert.DeserializeObject<TitleBlockLibrary>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Error("TitleBlockSpecRegistry.Load", ex);
                return null;
            }
        }

        /// <summary>Walk the <see cref="TitleBlockSpec.Extends"/> chain
        /// and return a flattened spec — parent-first concatenation for
        /// list fields, child-wins for scalar fields. Loop-safe via a
        /// visited set. Returns the input unchanged when there's no
        /// parent.</summary>
        public static TitleBlockSpec Resolve(TitleBlockLibrary lib, TitleBlockSpec spec)
        {
            if (lib == null || spec == null || string.IsNullOrEmpty(spec.Extends))
                return spec;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chain   = new List<TitleBlockSpec>();
            var cur     = spec;
            while (cur != null && !string.IsNullOrEmpty(cur.Extends))
            {
                if (!visited.Add(cur.Id ?? "(anon)"))
                {
                    StingLog.Warn($"TitleBlockSpecRegistry.Resolve: extends loop detected at '{cur.Id}'");
                    break;
                }
                var parent = FindById(lib, cur.Extends);
                if (parent == null)
                {
                    StingLog.Warn($"TitleBlockSpecRegistry.Resolve: extends '{cur.Extends}' (referenced by '{cur.Id}') not found");
                    break;
                }
                chain.Add(parent);
                cur = parent;
            }
            // Walk parents oldest → newest, fold into a fresh accumulator,
            // then layer the original child on top.
            chain.Reverse();
            var merged = new TitleBlockSpec
            {
                Id          = spec.Id,
                Description = spec.Description,
                Abstract    = false, // resolved specs are concrete
                Extends     = null,
                Mode        = spec.Mode,
                TemplateRft = spec.TemplateRft,
                SaveAs      = spec.SaveAs,
                Category    = spec.Category,
            };
            foreach (var p in chain) MergeInto(merged, p);
            MergeInto(merged, spec);
            return merged;
        }

        private static TitleBlockSpec FindById(TitleBlockLibrary lib, string id)
        {
            if (lib?.Families == null) return null;
            foreach (var f in lib.Families)
            {
                if (string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private static void MergeInto(TitleBlockSpec into, TitleBlockSpec from)
        {
            if (from == null) return;
            // Scalars — child wins, but only when child left it blank.
            if (string.IsNullOrEmpty(into.TemplateRft)) into.TemplateRft = from.TemplateRft;
            if (string.IsNullOrEmpty(into.Mode))        into.Mode        = from.Mode;
            if (string.IsNullOrEmpty(into.Category))    into.Category    = from.Category;
            // Lists — append parent contents under child contents,
            // de-duplicate parameters and slots by id.
            into.Parameters    = MergeParams(into.Parameters,       from.Parameters);
            into.Slots         = MergeSlots (into.Slots,            from.Slots);
            // Lines / static text / labels / filled regions don't have
            // natural ids, so just concatenate. The generator handles
            // duplicates via spatial collision (rare in practice).
            into.Lines         = ConcatList(into.Lines,             from.Lines);
            into.StaticText    = ConcatList(into.StaticText,        from.StaticText);
            into.Labels        = ConcatList(into.Labels,            from.Labels);
            into.FilledRegions = ConcatList(into.FilledRegions,     from.FilledRegions);
        }

        private static List<T> ConcatList<T>(List<T> a, List<T> b)
        {
            var r = new List<T>();
            if (b != null) r.AddRange(b);   // parent first
            if (a != null) r.AddRange(a);   // child overrides on top
            return r;
        }

        private static List<ParamSpec> MergeParams(List<ParamSpec> child, List<ParamSpec> parent)
        {
            var byName = new Dictionary<string, ParamSpec>(StringComparer.OrdinalIgnoreCase);
            if (parent != null)
                foreach (var p in parent)
                    if (!string.IsNullOrEmpty(p?.Name)) byName[p.Name] = p;
            if (child != null)
                foreach (var p in child)
                    if (!string.IsNullOrEmpty(p?.Name)) byName[p.Name] = p; // child wins
            return new List<ParamSpec>(byName.Values);
        }

        private static List<SlotSpec> MergeSlots(List<SlotSpec> child, List<SlotSpec> parent)
        {
            var byId = new Dictionary<string, SlotSpec>(StringComparer.OrdinalIgnoreCase);
            if (parent != null)
                foreach (var s in parent)
                    if (!string.IsNullOrEmpty(s?.Id)) byId[s.Id] = s;
            if (child != null)
                foreach (var s in child)
                    if (!string.IsNullOrEmpty(s?.Id)) byId[s.Id] = s; // child wins
            return new List<SlotSpec>(byId.Values);
        }
    }
}
