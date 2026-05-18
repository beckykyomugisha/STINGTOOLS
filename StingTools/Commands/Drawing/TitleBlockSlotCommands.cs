// StingTools — Phase 171 · Title-block slot automation commands
//
// Two operator-facing commands that pair the two-family BIM
// architecture (Phase 170 revision) with the slot system from
// STING_TITLE_BLOCKS.json:
//
//   TitleBlock_AutoPlaceViewports — for each selected view, look up
//     the view's purpose tag from STING_VIEWPORT_PLACEMENT_RULES.json,
//     find the slot on the active sheet's title-block .rfa with the
//     matching tag (with alias chain fallback), and create a Viewport
//     at the slot's centre. Applies the slot's scaleHint + viewportType
//     when set. Reports per-view what slot it landed in.
//
//   TitleBlock_ToggleBIMMode — read STING_SHEET_BIM_MODE_TXT on the
//     active sheet, swap the title-block family between *_BIM_* and
//     *_NONBIM_* variants, transfer existing viewports onto the new
//     family (positions transfer 1:1 since slot ids are stable across
//     modes). Auto-loads the target .rfa from Families/TitleBlocks/
//     when not already present in the project.
//
// Slot bounds are derived from the named reference planes the factory
// authors inside each title-block family (`<id>_TOP/BOT/LFT/RGT`),
// read back by opening the family doc in-memory via Document.EditFamily.
// The named-ref-plane convention means the .rfa is self-describing —
// no JSON parsing at runtime, no metadata duplication on the sheet.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockAutoPlaceViewportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp?.ActiveUIDocument == null)
            { TaskDialog.Show("STING — Auto-place Viewports", "No active document."); return Result.Failed; }
            var doc = uiApp.ActiveUIDocument.Document;

            // 1. Pre-flight — must be standing on a sheet.
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "Open the target sheet view first. The auto-placer reads slots from the sheet's title-block family.");
                return Result.Cancelled;
            }

            var rules = ViewportPlacementRules.Load();
            if (rules == null)
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "STING_VIEWPORT_PLACEMENT_RULES.json not found in data/. Reinstall the addin or restore the data folder.");
                return Result.Failed;
            }

            // 2. Resolve the title-block family + read slot bounds.
            var titleBlock = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
            if (titleBlock == null)
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "Active sheet has no title block placed. Add a STING title block first.");
                return Result.Failed;
            }
            var slotMap = TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, titleBlock);
            if (slotMap.Count == 0)
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    $"No slots resolved for title-block family '{TitleBlockSlotUtils.GetFamilyName(doc, titleBlock)}'.\n\n"
                    + "Slot bounds are read from STING_TITLE_BLOCKS.json by family id "
                    + "(extends-resolved). Either:\n"
                    + "  - The family id doesn't match any spec entry, or\n"
                    + "  - The matched spec has an empty slots[] array.\n\n"
                    + "Run TitleBlock_CreateAll to regenerate the family from a current spec.");
                return Result.Failed;
            }
            // The slot bounds come from the family doc (origin = bottom-left
            // corner of the paper). On the sheet, the title-block instance's
            // location is the same origin, so slot bounds map 1:1.
            // (Title-block instances in Revit are anchored at the family
            // origin which by convention sits at the sheet's bottom-left.)

            // 3. Survey selected views (or every unplaced view if none selected).
            var selectedIds = uiApp.ActiveUIDocument.Selection.GetElementIds();
            var candidateViews = new List<View>();
            foreach (var id in selectedIds)
            {
                if (doc.GetElement(id) is View v
                    && v.ViewType != ViewType.DrawingSheet
                    && v.ViewType != ViewType.ProjectBrowser
                    && v.ViewType != ViewType.SystemBrowser
                    && !v.IsTemplate
                    && Viewport.CanAddViewToSheet(doc, sheet.Id, id))
                {
                    candidateViews.Add(v);
                }
            }
            if (candidateViews.Count == 0)
            {
                TaskDialog.Show("STING — Auto-place Viewports",
                    "Select at least one placeable view in the project browser before running this command. "
                    + "(Views already on a sheet, templates, and unplaceable view types are filtered out.)");
                return Result.Cancelled;
            }

            // 4. Place each view in a single Transaction.
            var placed   = new List<string>();
            var skipped  = new List<string>();
            using (var tx = new Transaction(doc, "STING Auto-place Viewports"))
            {
                tx.Start();
                foreach (var v in candidateViews)
                {
                    try
                    {
                        var tag      = TitleBlockAutoPlaceViewportsCommand.ResolvePurposeTag(rules, v);
                        var slotId   = ResolveSlotForTag(slotMap, tag, rules);
                        if (string.IsNullOrEmpty(slotId))
                        {
                            // Fall back to defaultPurposeTag's slot.
                            slotId = ResolveSlotForTag(slotMap, rules.DefaultPurposeTag ?? "main-plan", rules);
                        }
                        if (string.IsNullOrEmpty(slotId))
                        {
                            skipped.Add($"{v.ViewType} '{v.Name}' — no slot for tag '{tag}' on this title block");
                            continue;
                        }
                        var bbox = slotMap[slotId];
                        var centre = new XYZ((bbox.Min.X + bbox.Max.X) / 2.0,
                                             (bbox.Min.Y + bbox.Max.Y) / 2.0, 0);

                        // Apply scaleHint BEFORE placing — so the viewport
                        // adopts the right scale at creation time. Wrap in
                        // try/catch since some view types (Schedule / Legend
                        // / Drafting at fixed scale) reject Scale assignment.
                        var slotMeta = slotMap[slotId];
                        if (slotMeta.ScaleHint.HasValue && v.Scale != slotMeta.ScaleHint.Value)
                        {
                            try { v.Scale = slotMeta.ScaleHint.Value; } catch { /* view type doesn't support scale */ }
                        }

                        var vp = Viewport.Create(doc, sheet.Id, v.Id, centre);
                        if (vp == null)
                        {
                            skipped.Add($"{v.ViewType} '{v.Name}' — Viewport.Create returned null");
                            continue;
                        }

                        // Apply viewportType when the slot specifies one.
                        if (!string.IsNullOrEmpty(slotMeta.ViewportType))
                        {
                            var vpTypeId = ResolveViewportTypeId(doc, slotMeta.ViewportType);
                            if (vpTypeId != ElementId.InvalidElementId)
                            {
                                try { vp.ChangeTypeId(vpTypeId); } catch (Exception viewTypeEx) { skipped.Add($"{v.Name} — viewport type '{slotMeta.ViewportType}' not applied: {viewTypeEx.Message}"); }
                            }
                        }
                        placed.Add($"{v.ViewType,-12} '{v.Name}'  →  slot {slotId}  ({tag})");
                    }
                    catch (Exception ex)
                    {
                        skipped.Add($"{v.ViewType} '{v.Name}' — {ex.Message}");
                    }
                }
                tx.Commit();
            }

            // 5. Report
            var sb = new StringBuilder();
            sb.AppendLine($"STING — Auto-place Viewports");
            sb.AppendLine();
            sb.AppendLine($"Sheet           : {sheet.SheetNumber} — {sheet.Name}");
            sb.AppendLine($"Title block     : {TitleBlockSlotUtils.GetFamilyName(doc, titleBlock)}");
            sb.AppendLine($"Slots discovered: {slotMap.Count}  ({string.Join(", ", slotMap.Keys.Take(8))}{(slotMap.Count > 8 ? ", …" : "")})");
            sb.AppendLine();
            sb.AppendLine($"Placed: {placed.Count}");
            foreach (var p in placed.Take(15)) sb.AppendLine("  ✓ " + p);
            if (placed.Count > 15) sb.AppendLine($"  … +{placed.Count - 15} more");
            if (skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Skipped: {skipped.Count}");
                foreach (var s in skipped.Take(10)) sb.AppendLine("  ! " + s);
                if (skipped.Count > 10) sb.AppendLine($"  … +{skipped.Count - 10} more");
            }
            TaskDialog.Show("STING — Auto-place Viewports", sb.ToString());
            return placed.Count > 0 ? Result.Succeeded : Result.Failed;
        }

        internal static string ResolvePurposeTag(ViewportPlacementRules rules, View v)
        {
            if (rules?.Rules == null) return rules?.DefaultPurposeTag ?? "main-plan";
            string vtName = v.ViewType.ToString();
            string vName  = v.Name ?? "";
            foreach (var rule in rules.Rules)
            {
                if (!string.Equals(rule.ViewType, vtName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!GlobMatch(rule.NamePattern, vName)) continue;
                return rule.PurposeTag;
            }
            return rules.DefaultPurposeTag ?? "main-plan";
        }

        /// <summary>Find a slot whose <see cref="SlotBounds.PurposeTag"/> matches
        /// the requested tag. Walk <see cref="ViewportPlacementRules.PurposeTagAliases"/>
        /// for graceful fallback (a "main-plan-half-left"-tagged view lands in
        /// a "main-plan" slot when the half-left slot isn't present).
        /// Returns the slot id, or null if nothing matches even via aliases.</summary>
        private static string ResolveSlotForTag(Dictionary<string, SlotBounds> slotMap,
            string tag, ViewportPlacementRules rules)
        {
            if (slotMap == null || string.IsNullOrEmpty(tag)) return null;
            // Direct hit: any slot whose purposeTag matches.
            foreach (var kv in slotMap)
            {
                if (string.Equals(kv.Value.PurposeTag, tag, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            // Alias chain.
            if (rules?.PurposeTagAliases != null
                && rules.PurposeTagAliases.TryGetValue(tag, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    foreach (var kv in slotMap)
                    {
                        if (string.Equals(kv.Value.PurposeTag, alias, StringComparison.OrdinalIgnoreCase))
                            return kv.Key;
                    }
                }
            }
            return null;
        }

        private static bool GlobMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? "", rx, RegexOptions.IgnoreCase);
        }

        private static ElementId ResolveViewportTypeId(Document doc, string typeName)
        {
            try
            {
                foreach (var et in new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType))
                    .OfCategory(BuiltInCategory.OST_Viewports))
                {
                    if (string.Equals(et.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        return et.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockToggleBIMModeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp?.ActiveUIDocument == null)
            { TaskDialog.Show("STING — Toggle BIM Mode", "No active document."); return Result.Failed; }
            var doc = uiApp.ActiveUIDocument.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING — Toggle BIM Mode", "Open the target sheet first.");
                return Result.Cancelled;
            }

            var titleBlock = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
            if (titleBlock == null)
            {
                TaskDialog.Show("STING — Toggle BIM Mode", "Active sheet has no title block.");
                return Result.Cancelled;
            }
            var sym = doc.GetElement(titleBlock.GetTypeId()) as FamilySymbol;
            var currentName = sym?.Family?.Name ?? "";
            var bimModeParam = titleBlock.LookupParameter("STING_SHEET_BIM_MODE_TXT");
            var currentMode  = bimModeParam?.AsString();
            if (string.IsNullOrEmpty(currentMode)) currentMode = GuessModeFromName(currentName);
            string targetMode = string.Equals(currentMode, "BIM", StringComparison.OrdinalIgnoreCase)
                ? "NONBIM" : "BIM";
            string targetFamilyName = ComputeTargetFamilyName(currentName, targetMode);
            if (string.IsNullOrEmpty(targetFamilyName) || string.Equals(targetFamilyName, currentName, StringComparison.OrdinalIgnoreCase))
            {
                TaskDialog.Show("STING — Toggle BIM Mode",
                    $"Couldn't compute a target family name from '{currentName}'.\n"
                    + "Current name doesn't follow the STING_TB_<SIZE>_<MODE>_v<N> convention.\n"
                    + "Rename the family first or load a STING title block.");
                return Result.Cancelled;
            }

            // Find or load the target family.
            var targetFamily = FindLoadedFamily(doc, targetFamilyName)
                            ?? LoadFamilyFromDisk(doc, targetFamilyName);
            if (targetFamily == null)
            {
                TaskDialog.Show("STING — Toggle BIM Mode",
                    $"Target family '{targetFamilyName}' is not loaded and was not found at "
                    + $"Families/TitleBlocks/{targetFamilyName}.rfa or alongside the addin.\n\n"
                    + $"Run TitleBlock_CreateAll first, or copy the .rfa into the project's Families/TitleBlocks/ folder.");
                return Result.Failed;
            }

            // Pick the first FamilySymbol of the target family.
            FamilySymbol targetSym = null;
            foreach (var symId in targetFamily.GetFamilySymbolIds())
            {
                targetSym = doc.GetElement(symId) as FamilySymbol;
                if (targetSym != null) break;
            }
            if (targetSym == null)
            {
                TaskDialog.Show("STING — Toggle BIM Mode",
                    $"Target family '{targetFamilyName}' is loaded but has no types — re-author the family.");
                return Result.Failed;
            }

            // Swap.
            using (var tx = new Transaction(doc, "STING Toggle BIM Mode"))
            {
                tx.Start();
                if (!targetSym.IsActive) targetSym.Activate();
                doc.Regenerate();

                var fi = titleBlock as FamilyInstance;
                if (fi != null) fi.Symbol = targetSym;

                // Update the BIM mode marker on the new instance.
                var newBim = titleBlock.LookupParameter("STING_SHEET_BIM_MODE_TXT");
                if (newBim != null && !newBim.IsReadOnly)
                {
                    try { newBim.Set(targetMode); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                tx.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine("STING — Toggle BIM Mode");
            sb.AppendLine();
            sb.AppendLine($"Sheet         : {sheet.SheetNumber} — {sheet.Name}");
            sb.AppendLine($"Was           : {currentName}  ({currentMode})");
            sb.AppendLine($"Now           : {targetFamilyName}  ({targetMode})");
            sb.AppendLine();
            sb.AppendLine("Viewports: positions transferred 1:1 (slot ids are stable across BIM/NONBIM variants).");
            TaskDialog.Show("STING — Toggle BIM Mode", sb.ToString());
            return Result.Succeeded;
        }

        private static string ComputeTargetFamilyName(string currentName, string targetMode)
        {
            if (string.IsNullOrEmpty(currentName)) return null;
            if (currentName.IndexOf("_BIM_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return targetMode == "BIM" ? currentName
                                           : Replace(currentName, "_BIM_", "_NONBIM_");
            }
            if (currentName.IndexOf("_NONBIM_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return targetMode == "NONBIM" ? currentName
                                              : Replace(currentName, "_NONBIM_", "_BIM_");
            }
            return null;
        }

        private static string Replace(string s, string oldVal, string newVal)
        {
            int i = s.IndexOf(oldVal, StringComparison.OrdinalIgnoreCase);
            return i < 0 ? s : s.Substring(0, i) + newVal + s.Substring(i + oldVal.Length);
        }

        private static Family FindLoadedFamily(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .OfType<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static Family LoadFamilyFromDisk(Document doc, string name)
        {
            // Search candidates: project's Families/TitleBlocks, addin DLL dir.
            var candidates = new List<string>();
            try
            {
                var prjPath = doc.PathName;
                if (!string.IsNullOrEmpty(prjPath))
                {
                    var prjDir = Path.GetDirectoryName(prjPath);
                    if (!string.IsNullOrEmpty(prjDir))
                        candidates.Add(Path.Combine(prjDir, "Families", "TitleBlocks", name + ".rfa"));
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try
            {
                var asm = StingToolsApp.AssemblyPath;
                if (!string.IsNullOrEmpty(asm))
                {
                    var asmDir = Path.GetDirectoryName(asm);
                    if (!string.IsNullOrEmpty(asmDir))
                        candidates.Add(Path.Combine(asmDir, "Families", "TitleBlocks", name + ".rfa"));
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            foreach (var c in candidates)
            {
                if (!File.Exists(c)) continue;
                try
                {
                    using (var tx = new Transaction(doc, $"STING Load {name}"))
                    {
                        tx.Start();
                        var ok = doc.LoadFamily(c, new TitleBlockFamilyLoadOptions(), out Family fam);
                        tx.Commit();
                        if (ok && fam != null) return fam;
                        if (fam != null) return fam;  // already-loaded short-circuit
                    }
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"LoadFamilyFromDisk '{name}': {ex.Message}");
                }
            }
            return null;
        }

        private static string GuessModeFromName(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return "(unknown)";
            if (familyName.IndexOf("_BIM_",    StringComparison.OrdinalIgnoreCase) >= 0) return "BIM";
            if (familyName.IndexOf("_NONBIM_", StringComparison.OrdinalIgnoreCase) >= 0) return "NONBIM";
            return "(unknown)";
        }
    }

    /// <summary>Standard `IFamilyLoadOptions` — accept and overwrite. Same
    /// pattern as TagFamilyLoadOptions in TagFamilyCreatorCommand.cs.</summary>
    internal class TitleBlockFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        { overwriteParameterValues = true; return true; }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        { source = FamilySource.Family; overwriteParameterValues = true; return true; }
    }

    /// <summary>POCO mirror of STING_VIEWPORT_PLACEMENT_RULES.json.</summary>
    public sealed class ViewportPlacementRules
    {
        [JsonProperty("schemaVersion")]      public int    SchemaVersion { get; set; } = 1;
        [JsonProperty("name")]               public string Name { get; set; }
        [JsonProperty("description")]        public string Description { get; set; }
        [JsonProperty("defaultPurposeTag")]  public string DefaultPurposeTag { get; set; } = "main-plan";
        [JsonProperty("noMatchBehaviour")]   public string NoMatchBehaviour { get; set; } = "place-at-default-with-warning";
        [JsonProperty("rules")]              public List<ViewportPlacementRule> Rules { get; set; }
            = new List<ViewportPlacementRule>();
        [JsonProperty("purposeTagAliases")]  public Dictionary<string, List<string>> PurposeTagAliases { get; set; }
            = new Dictionary<string, List<string>>();

        public static ViewportPlacementRules Load()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_VIEWPORT_PLACEMENT_RULES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("ViewportPlacementRules: STING_VIEWPORT_PLACEMENT_RULES.json not found");
                    return null;
                }
                return JsonConvert.DeserializeObject<ViewportPlacementRules>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                StingLog.Error("ViewportPlacementRules.Load", ex);
                return null;
            }
        }
    }

    public sealed class ViewportPlacementRule
    {
        [JsonProperty("viewType")]    public string ViewType { get; set; }
        [JsonProperty("namePattern")] public string NamePattern { get; set; } = "*";
        [JsonProperty("purposeTag")]  public string PurposeTag { get; set; }
    }

    /// <summary>Slot bbox + metadata read back from a title-block .rfa via
    /// the named reference planes the factory authored.</summary>
    public sealed class SlotBounds
    {
        public string Id { get; set; }
        public BoundingBoxXYZ Bbox { get; set; }
        public XYZ Min => Bbox?.Min;
        public XYZ Max => Bbox?.Max;
        public string PurposeTag { get; set; }
        public string ViewportType { get; set; }
        public int? ScaleHint { get; set; }
    }

    /// <summary>Shared helpers used by both slot commands.</summary>
    internal static class TitleBlockSlotUtils
    {
        public static Element FindTitleBlockOnSheet(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        public static string GetFamilyName(Document doc, Element titleBlock)
        {
            try
            {
                var sym = doc.GetElement(titleBlock.GetTypeId()) as FamilySymbol;
                return sym?.Family?.Name ?? "(unknown)";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return "(unknown)"; }
        }

        /// <summary>Resolve slot bounds for the title block on the active
        /// sheet. JSON-first strategy:
        ///
        ///   1. Look up the family by id in STING_TITLE_BLOCKS.json
        ///      (extends-resolved). The JSON declares slots in mm relative
        ///      to the sheet's bottom-left, with PurposeTag / ViewportType
        ///      / ScaleHint metadata.
        ///   2. If the family ALSO has named reference planes
        ///      (`<id>_TOP/BOT/LFT/RGT`) inside its .rfa, we override the
        ///      JSON-declared bounds with the actual planes — useful when
        ///      an operator has manually adjusted slots in Family Editor.
        ///      Title-block families on Revit 2025 reject ref-plane
        ///      creation, so this step usually no-ops; legacy families
        ///      or migrated layouts may still have them.
        ///
        /// JSON is the authoritative source.</summary>
        public static Dictionary<string, SlotBounds> ReadSlotBoundsFromTitleBlock(Document doc, Element titleBlock)
        {
            var result = new Dictionary<string, SlotBounds>(StringComparer.OrdinalIgnoreCase);
            var familyName = GetFamilyName(doc, titleBlock);

            // 1. Primary source — STING_TITLE_BLOCKS.json (extends-resolved).
            try
            {
                var lib = Core.Drawing.TitleBlockSpecRegistry.Load();
                if (lib != null)
                {
                    var spec = lib.Families
                        .FirstOrDefault(f => string.Equals(f.Id, familyName, StringComparison.OrdinalIgnoreCase));
                    if (spec != null)
                    {
                        spec = Core.Drawing.TitleBlockSpecRegistry.Resolve(lib, spec);
                        foreach (var slot in spec.Slots ?? Enumerable.Empty<Core.Drawing.SlotSpec>())
                        {
                            if (string.IsNullOrEmpty(slot.Id)
                                || slot.Anchor == null || slot.Anchor.Length < 2
                                || slot.Size   == null || slot.Size.Length   < 2) continue;
                            // Convert the spec's mm coords to feet (Revit
                            // internal length unit), origin at sheet's
                            // bottom-left.
                            double xFt = MmToFt(slot.Anchor[0]);
                            double yFt = MmToFt(slot.Anchor[1]);
                            double wFt = MmToFt(slot.Size[0]);
                            double hFt = MmToFt(slot.Size[1]);
                            result[slot.Id] = new SlotBounds
                            {
                                Id           = slot.Id,
                                Bbox         = new BoundingBoxXYZ
                                {
                                    Min = new XYZ(xFt,        yFt,        0),
                                    Max = new XYZ(xFt + wFt,  yFt + hFt,  0),
                                },
                                PurposeTag   = slot.PurposeTag,
                                ViewportType = slot.ViewportType,
                                ScaleHint    = slot.ScaleHint,
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReadSlotBoundsFromTitleBlock (JSON): {ex.Message}");
            }

            // 2. Optional override — if the family has named reference planes,
            // they take precedence (the operator may have manually nudged
            // slots in Family Editor and we want to honour that).
            try
            {
                var sym = doc.GetElement(titleBlock.GetTypeId()) as FamilySymbol;
                var family = sym?.Family;
                if (family == null) return result;
                var famDoc = doc.EditFamily(family);
                if (famDoc == null) return result;
                try
                {
                    var byId = new Dictionary<string, List<XYZ>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rp in new FilteredElementCollector(famDoc)
                        .OfClass(typeof(ReferencePlane))
                        .OfType<ReferencePlane>())
                    {
                        var name = rp.Name ?? "";
                        var i = name.LastIndexOf('_');
                        if (i <= 0) continue;
                        var suffix = name.Substring(i + 1).ToUpperInvariant();
                        if (suffix != "TOP" && suffix != "BOT" && suffix != "LFT" && suffix != "RGT") continue;
                        var slotId = name.Substring(0, i);
                        if (!byId.TryGetValue(slotId, out var list))
                            byId[slotId] = list = new List<XYZ>();
                        if (rp.BubbleEnd != null) list.Add(rp.BubbleEnd);
                        if (rp.FreeEnd   != null) list.Add(rp.FreeEnd);
                    }
                    foreach (var kv in byId)
                    {
                        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
                        foreach (var p in kv.Value)
                        {
                            if (p.X < minX) minX = p.X;
                            if (p.X > maxX) maxX = p.X;
                            if (p.Y < minY) minY = p.Y;
                            if (p.Y > maxY) maxY = p.Y;
                        }
                        if (double.IsInfinity(minX)) continue;
                        // Override (or add) the slot — preserve metadata
                        // from the JSON-derived entry if present.
                        if (!result.TryGetValue(kv.Key, out var existing))
                            existing = new SlotBounds { Id = kv.Key };
                        existing.Bbox = new BoundingBoxXYZ
                        {
                            Min = new XYZ(minX, minY, 0),
                            Max = new XYZ(maxX, maxY, 0),
                        };
                        result[kv.Key] = existing;
                    }
                }
                finally
                {
                    try { famDoc.Close(false); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReadSlotBoundsFromTitleBlock (ref planes): {ex.Message}");
            }
            return result;
        }

        private const double MmPerFoot = 304.8;
        private static double MmToFt(double mm) => mm / MmPerFoot;
    }
}
