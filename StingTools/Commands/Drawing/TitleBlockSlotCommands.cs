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
                    $"No slot reference planes found in title-block family '{TitleBlockSlotUtils.GetFamilyName(doc, titleBlock)}'. "
                    + "The factory needs to have authored '<slotId>_TOP/BOT/LFT/RGT' planes — regenerate the family with TitleBlock_Create.");
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
            catch { }
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
                    try { newBim.Set(targetMode); } catch { }
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
            catch { }
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
            catch { }

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
                catch (Exception ex)
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
            catch { return null; }
        }

        public static string GetFamilyName(Document doc, Element titleBlock)
        {
            try
            {
                var sym = doc.GetElement(titleBlock.GetTypeId()) as FamilySymbol;
                return sym?.Family?.Name ?? "(unknown)";
            }
            catch { return "(unknown)"; }
        }

        /// <summary>Walk the title-block .rfa's reference planes, group by
        /// slot id (`<id>_TOP/BOT/LFT/RGT`), compute the bbox per slot.
        /// Reads from the family doc via Document.EditFamily — read-only,
        /// no transaction, family doc is closed at the end. Pulls
        /// SlotSpec metadata (purposeTag / viewportType / scaleHint) back
        /// from the JSON spec by family name so the auto-placer can apply
        /// them.</summary>
        public static Dictionary<string, SlotBounds> ReadSlotBoundsFromTitleBlock(Document doc, Element titleBlock)
        {
            var result = new Dictionary<string, SlotBounds>(StringComparer.OrdinalIgnoreCase);
            var familyName = GetFamilyName(doc, titleBlock);

            // 1. Open family doc and walk reference planes.
            try
            {
                var sym = doc.GetElement(titleBlock.GetTypeId()) as FamilySymbol;
                var family = sym?.Family;
                if (family == null) return result;
                var famDoc = doc.EditFamily(family);
                if (famDoc == null) return result;
                try
                {
                    // Group ref planes by slot id.
                    var byId = new Dictionary<string, List<(string suffix, XYZ p1, XYZ p2)>>(StringComparer.OrdinalIgnoreCase);
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
                            byId[slotId] = list = new List<(string, XYZ, XYZ)>();
                        list.Add((suffix, rp.BubbleEnd, rp.FreeEnd));
                    }

                    // Compute bbox per slot.
                    foreach (var kv in byId)
                    {
                        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
                        foreach (var (_, p1, p2) in kv.Value)
                        {
                            foreach (var p in new[] { p1, p2 })
                            {
                                if (p == null) continue;
                                if (p.X < minX) minX = p.X;
                                if (p.X > maxX) maxX = p.X;
                                if (p.Y < minY) minY = p.Y;
                                if (p.Y > maxY) maxY = p.Y;
                            }
                        }
                        if (double.IsInfinity(minX)) continue;
                        result[kv.Key] = new SlotBounds
                        {
                            Id   = kv.Key,
                            Bbox = new BoundingBoxXYZ
                            {
                                Min = new XYZ(minX, minY, 0),
                                Max = new XYZ(maxX, maxY, 0)
                            }
                        };
                    }
                }
                finally
                {
                    try { famDoc.Close(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ReadSlotBoundsFromTitleBlock: {ex.Message}");
            }

            // 2. Augment with SlotSpec metadata from the JSON spec, looked up
            // by family id. The id in the JSON matches the .rfa basename
            // (e.g. STING_TB_A1_BIM_v2.0).
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
                            if (string.IsNullOrEmpty(slot.Id)) continue;
                            if (!result.TryGetValue(slot.Id, out var bounds)) continue;
                            bounds.PurposeTag   = slot.PurposeTag;
                            bounds.ViewportType = slot.ViewportType;
                            bounds.ScaleHint    = slot.ScaleHint;
                        }
                    }
                }
            }
            catch { /* metadata is best-effort; bbox is the load-bearing part */ }
            return result;
        }
    }
}
