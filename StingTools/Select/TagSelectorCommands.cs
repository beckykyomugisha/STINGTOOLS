using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Select
{
    // ══════════════════════════════════════════════════════════════════
    //  Tag Selector — multi-criteria annotation tag selection tool
    //  Selects IndependentTag elements by: tag text, text size,
    //  arrowhead style, leader line weight, leader state, elbow angle,
    //  tag family, host category, orientation, discipline, and more.
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Main entry point for the multi-criteria tag selector.
    /// Page 1 lets user pick a filter criterion, page 2 shows discovered
    /// values with counts, page 3 sets selection.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagSelectorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null)
            {
                TaskDialog.Show("STING Tools", "No active view.");
                return Result.Failed;
            }

            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            // Collect all IndependentTag instances in active view
            var allTags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (allTags.Count == 0)
            {
                TaskDialog.Show("Tag Selector", "No annotation tags found in the active view.");
                return Result.Succeeded;
            }

            // ── Page 1: Choose filter criterion (4 pages of criteria) ──
            int page = 0;
            while (true)
            {
                var criteria = TagSelectorEngine.AllCriteria;
                int start = page * 4;
                int remaining = criteria.Length - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = criteria.Length; }

                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("Tag Selector");
                td.MainInstruction = $"Select tags by property ({allTags.Count} tags in view)";
                td.MainContent = $"Choose a filter criterion (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        criteria[start + i].Name,
                        criteria[start + i].Description);
                }
                if (hasMore)
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More criteria →",
                        $"{remaining - show} more criteria available");
                }
                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = td.Show();
                int idx = -1;
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show)
                {
                    page++;
                    continue;
                }

                if (idx < 0 || idx >= show) return Result.Cancelled;

                var chosen = criteria[start + idx];
                return TagSelectorEngine.ExecuteCriterion(uidoc, doc, view, allTags, chosen);
            }
        }
    }

    /// <summary>
    /// Quick select: tags by their displayed text content (tag value).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByTextCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "TagText"));
        }
    }

    /// <summary>
    /// Quick select: tags by text size.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByTextSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "TextSize"));
        }
    }

    /// <summary>
    /// Quick select: tags by arrowhead style.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByArrowheadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "Arrowhead"));
        }
    }

    /// <summary>
    /// Quick select: tags by leader line weight.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByLineWeightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "LineWeight"));
        }
    }

    /// <summary>
    /// Quick select: tags by leader elbow angle.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByElbowAngleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "ElbowAngle"));
        }
    }

    /// <summary>
    /// Quick select: tags by tag family/type name.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByFamilyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "TagFamily"));
        }
    }

    /// <summary>
    /// Quick select: tags by host element category.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByHostCategoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "HostCategory"));
        }
    }

    /// <summary>
    /// Quick select: tags by leader state (with/without/free end).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByLeaderStateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "LeaderState"));
        }
    }

    /// <summary>
    /// Quick select: tags by orientation (horizontal/vertical).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByOrientationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "Orientation"));
        }
    }

    /// <summary>
    /// Quick select: tags by host element discipline code.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByDisciplineCodeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "DisciplineCode"));
        }
    }

    /// <summary>
    /// Quick select: tags by STING token segment value (any of the 8 segments).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectTagsByTokenCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "TokenSegment"));
        }
    }

    /// <summary>
    /// Quick select: tags with overlapping bounding boxes.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SelectOverlappingTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            var allTags = TagSelectorEngine.CollectTags(ctx.Doc, ctx.ActiveView);
            if (allTags.Count == 0) { TaskDialog.Show("Tag Selector", "No annotation tags in view."); return Result.Succeeded; }
            return TagSelectorEngine.ExecuteCriterion(ctx.UIDoc, ctx.Doc, ctx.ActiveView, allTags,
                TagSelectorEngine.AllCriteria.First(c => c.Key == "Overlapping"));
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Tag Selector Engine — shared logic for all criteria
    // ══════════════════════════════════════════════════════════════════

    internal static class TagSelectorEngine
    {
        internal class CriterionDef
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
        }

        internal static readonly CriterionDef[] AllCriteria = new[]
        {
            new CriterionDef { Key = "TagText",        Name = "By Tag Text",           Description = "Select tags displaying a specific text value" },
            new CriterionDef { Key = "TextSize",       Name = "By Text Size",           Description = "Select tags by their text height (mm)" },
            new CriterionDef { Key = "Arrowhead",      Name = "By Arrowhead Style",     Description = "Select tags by leader arrowhead type" },
            new CriterionDef { Key = "LineWeight",     Name = "By Leader Line Weight",  Description = "Select tags by leader line weight (pen 1-16)" },
            new CriterionDef { Key = "ElbowAngle",     Name = "By Leader Elbow Angle",  Description = "Select tags by leader elbow angle (0°, 45°, 90°, other)" },
            new CriterionDef { Key = "TagFamily",      Name = "By Tag Family",          Description = "Select tags by tag family/type name" },
            new CriterionDef { Key = "HostCategory",   Name = "By Host Category",       Description = "Select tags by the category of their host element" },
            new CriterionDef { Key = "LeaderState",    Name = "By Leader State",        Description = "Select tags with/without leaders or by end condition" },
            new CriterionDef { Key = "Orientation",    Name = "By Tag Orientation",     Description = "Select horizontal or vertical tags" },
            new CriterionDef { Key = "DisciplineCode", Name = "By Discipline Code",     Description = "Select tags by host element DISC token (M, E, P, A, ...)" },
            new CriterionDef { Key = "TokenSegment",   Name = "By Tag Token",           Description = "Select by any ISO 19650 tag segment (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD)" },
            new CriterionDef { Key = "Overlapping",    Name = "Overlapping Tags",       Description = "Select tags whose bounding boxes overlap with other tags" },
        };

        internal static List<IndependentTag> CollectTags(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
        }

        internal static Result ExecuteCriterion(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags, CriterionDef criterion)
        {
            switch (criterion.Key)
            {
                case "TagText":        return SelectByTagText(uidoc, doc, view, allTags);
                case "TextSize":       return SelectByTextSize(uidoc, doc, view, allTags);
                case "Arrowhead":      return SelectByArrowhead(uidoc, doc, view, allTags);
                case "LineWeight":     return SelectByLineWeight(uidoc, doc, view, allTags);
                case "ElbowAngle":     return SelectByElbowAngle(uidoc, doc, view, allTags);
                case "TagFamily":      return SelectByTagFamily(uidoc, doc, view, allTags);
                case "HostCategory":   return SelectByHostCategory(uidoc, doc, view, allTags);
                case "LeaderState":    return SelectByLeaderState(uidoc, doc, view, allTags);
                case "Orientation":    return SelectByOrientation(uidoc, doc, view, allTags);
                case "DisciplineCode": return SelectByDisciplineCode(uidoc, doc, view, allTags);
                case "TokenSegment":   return SelectByTokenSegment(uidoc, doc, view, allTags);
                case "Overlapping":    return SelectOverlapping(uidoc, doc, view, allTags);
                default:
                    TaskDialog.Show("Tag Selector", $"Unknown criterion: {criterion.Key}");
                    return Result.Failed;
            }
        }

        // ── By Tag Text ────────────────────────────────────────────────

        private static Result SelectByTagText(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            // Group tags by their displayed text value
            var groups = new Dictionary<string, List<ElementId>>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in allTags)
            {
                string text = SafeGetTagText(tag);
                if (!groups.ContainsKey(text))
                    groups[text] = new List<ElementId>();
                groups[text].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Tag Text",
                $"{groups.Count} unique tag texts found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: TruncateText(g.Key, 40), Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Text Size ───────────────────────────────────────────────

        private static Result SelectByTextSize(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                double sizeMm = GetTagTextSizeMm(doc, tag);
                string key = sizeMm > 0 ? $"{sizeMm:F1} mm" : "<Unknown>";

                if (!groups.ContainsKey(key))
                    groups[key] = new List<ElementId>();
                groups[key].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Text Size",
                $"{groups.Count} text sizes found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Arrowhead Style ─────────────────────────────────────────

        private static Result SelectByArrowhead(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string arrowName = GetArrowheadName(doc, tag);
                if (!groups.ContainsKey(arrowName))
                    groups[arrowName] = new List<ElementId>();
                groups[arrowName].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Arrowhead Style",
                $"{groups.Count} arrowhead styles found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Leader Line Weight ──────────────────────────────────────

        private static Result SelectByLineWeight(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                int weight = GetLeaderLineWeight(doc, view, tag);
                string key = weight > 0 ? $"Pen {weight}" : "<Default>";

                if (!groups.ContainsKey(key))
                    groups[key] = new List<ElementId>();
                groups[key].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Leader Line Weight",
                $"{groups.Count} line weights found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Leader Elbow Angle ──────────────────────────────────────

        private static Result SelectByElbowAngle(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string angle = GetElbowAngleCategory(doc, tag, view);
                if (!groups.ContainsKey(angle))
                    groups[angle] = new List<ElementId>();
                groups[angle].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Elbow Angle",
                $"{groups.Count} angle categories found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Tag Family ──────────────────────────────────────────────

        private static Result SelectByTagFamily(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string familyName = GetTagFamilyName(doc, tag);
                if (!groups.ContainsKey(familyName))
                    groups[familyName] = new List<ElementId>();
                groups[familyName].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Tag Family",
                $"{groups.Count} tag families found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Host Category ───────────────────────────────────────────

        private static Result SelectByHostCategory(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string hostCat = GetHostCategoryName(doc, tag);
                if (!groups.ContainsKey(hostCat))
                    groups[hostCat] = new List<ElementId>();
                groups[hostCat].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Host Category",
                $"{groups.Count} host categories found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Leader State ────────────────────────────────────────────

        private static Result SelectByLeaderState(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string state = GetLeaderStateName(tag);
                if (!groups.ContainsKey(state))
                    groups[state] = new List<ElementId>();
                groups[state].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Leader State",
                $"{groups.Count} leader states found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Orientation ─────────────────────────────────────────────

        private static Result SelectByOrientation(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string orient;
                try { orient = tag.TagOrientation == TagOrientation.Horizontal ? "Horizontal" : "Vertical"; }
                catch { orient = "<Unknown>"; }

                if (!groups.ContainsKey(orient))
                    groups[orient] = new List<ElementId>();
                groups[orient].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Orientation",
                $"{groups.Count} orientations found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Discipline Code ─────────────────────────────────────────

        private static Result SelectByDisciplineCode(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var groups = new Dictionary<string, List<ElementId>>();

            foreach (var tag in allTags)
            {
                string disc = GetHostDiscipline(doc, tag);
                if (!groups.ContainsKey(disc))
                    groups[disc] = new List<ElementId>();
                groups[disc].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, "Select by Discipline Code",
                $"{groups.Count} discipline codes found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── By Token Segment ───────────────────────────────────────────

        private static Result SelectByTokenSegment(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            // First: let user pick which token to filter on
            TaskDialog tokenDlg = new TaskDialog("Select Token");
            tokenDlg.MainInstruction = "Filter by which tag token?";
            tokenDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "DISC — Discipline", "M, E, P, A, S, FP, LV, G");
            tokenDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "SYS — System Type", "HVAC, DCW, HWS, SAN, LV, FLS, ...");
            tokenDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "LOC — Location", "BLD1, BLD2, BLD3, EXT");
            tokenDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "More tokens →");
            tokenDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string tokenParam = null;
            string tokenLabel = null;

            switch (tokenDlg.Show())
            {
                case TaskDialogResult.CommandLink1: tokenParam = ParamRegistry.DISC; tokenLabel = "DISC"; break;
                case TaskDialogResult.CommandLink2: tokenParam = ParamRegistry.SYS; tokenLabel = "SYS"; break;
                case TaskDialogResult.CommandLink3: tokenParam = ParamRegistry.LOC; tokenLabel = "LOC"; break;
                case TaskDialogResult.CommandLink4:
                {
                    TaskDialog moreDlg = new TaskDialog("Select Token");
                    moreDlg.MainInstruction = "Filter by which tag token?";
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "ZONE — Zone", "Z01, Z02, Z03, Z04");
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "LVL — Level Code", "L01, GF, B1, RF");
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "FUNC — Function", "SUP, HTG, DCW, PWR, ...");
                    moreDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "PROD — Product Code", "AHU, DB, DR, ...");
                    moreDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    switch (moreDlg.Show())
                    {
                        case TaskDialogResult.CommandLink1: tokenParam = ParamRegistry.ZONE; tokenLabel = "ZONE"; break;
                        case TaskDialogResult.CommandLink2: tokenParam = ParamRegistry.LVL; tokenLabel = "LVL"; break;
                        case TaskDialogResult.CommandLink3: tokenParam = ParamRegistry.FUNC; tokenLabel = "FUNC"; break;
                        case TaskDialogResult.CommandLink4: tokenParam = ParamRegistry.PROD; tokenLabel = "PROD"; break;
                        default: return Result.Cancelled;
                    }
                    break;
                }
                default: return Result.Cancelled;
            }

            // Group tags by host element's token value
            var groups = new Dictionary<string, List<ElementId>>();
            foreach (var tag in allTags)
            {
                Element host = GetHostElement(doc, tag);
                string val = host != null
                    ? ParameterHelpers.GetString(host, tokenParam)
                    : "";
                if (string.IsNullOrEmpty(val)) val = "<Empty>";

                if (!groups.ContainsKey(val))
                    groups[val] = new List<ElementId>();
                groups[val].Add(tag.Id);
            }

            return ShowPagedPicker(uidoc, $"Select by {tokenLabel}",
                $"{groups.Count} {tokenLabel} values found",
                groups.OrderByDescending(g => g.Value.Count)
                    .Select(g => (Label: g.Key, Count: g.Value.Count, Ids: g.Value))
                    .ToList());
        }

        // ── Overlapping Tags ───────────────────────────────────────────

        private static Result SelectOverlapping(UIDocument uidoc, Document doc, View view,
            List<IndependentTag> allTags)
        {
            var boxes = new List<(ElementId id, double minX, double minY, double maxX, double maxY)>();
            foreach (var tag in allTags)
            {
                try
                {
                    BoundingBoxXYZ bb = tag.get_BoundingBox(view);
                    if (bb != null)
                        boxes.Add((tag.Id, bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y));
                }
                catch { }
            }

            var overlapping = new HashSet<ElementId>();
            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (BoxesOverlap(boxes[i], boxes[j]))
                    {
                        overlapping.Add(boxes[i].id);
                        overlapping.Add(boxes[j].id);
                    }
                }
            }

            if (overlapping.Count == 0)
            {
                TaskDialog.Show("Overlapping Tags", "No overlapping tags found in this view.");
                return Result.Succeeded;
            }

            uidoc.Selection.SetElementIds(overlapping.ToList());
            TaskDialog.Show("Overlapping Tags",
                $"Selected {overlapping.Count} tags with bounding box overlaps\n" +
                $"(from {boxes.Count} total tags in view).");
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  Paged value picker — shows discovered values 4 at a time
        // ══════════════════════════════════════════════════════════════

        private static Result ShowPagedPicker(UIDocument uidoc, string title, string subtitle,
            List<(string Label, int Count, List<ElementId> Ids)> items)
        {
            if (items.Count == 0)
            {
                TaskDialog.Show(title, "No matching tags found.");
                return Result.Succeeded;
            }

            // If only one group, select immediately
            if (items.Count == 1)
            {
                uidoc.Selection.SetElementIds(items[0].Ids);
                TaskDialog.Show(title,
                    $"Selected all {items[0].Ids.Count} tags: {items[0].Label}");
                return Result.Succeeded;
            }

            int page = 0;
            while (true)
            {
                int start = page * 4;
                int remaining = items.Count - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = items.Count; }

                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                // Also offer "Select ALL" as first option on page 1
                bool showSelectAll = page == 0 && items.Count > 1;

                TaskDialog td = new TaskDialog(title);
                td.MainInstruction = subtitle;
                td.MainContent = $"Page {page + 1} — Click a value to select matching tags:";

                int linkIdx = 0;
                if (showSelectAll)
                {
                    int totalCount = items.Sum(i => i.Ids.Count);
                    td.AddCommandLink((TaskDialogCommandLinkId)(linkIdx + 1001),
                        $"★ Select ALL ({totalCount} tags)",
                        $"Select all tags across all {items.Count} values");
                    linkIdx++;
                    // Reduce show count to fit
                    show = hasMore ? 2 : Math.Min(remaining, 3);
                    hasMore = remaining > 3;
                }

                for (int i = 0; i < show; i++)
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(linkIdx + 1001),
                        $"{items[start + i].Label} — {items[start + i].Count} tags");
                    linkIdx++;
                }

                if (hasMore)
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(linkIdx + 1001),
                        "More values →",
                        $"{remaining - show} more values");
                    linkIdx++;
                }

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = td.Show();
                int sel = -1;
                switch (result)
                {
                    case TaskDialogResult.CommandLink1: sel = 0; break;
                    case TaskDialogResult.CommandLink2: sel = 1; break;
                    case TaskDialogResult.CommandLink3: sel = 2; break;
                    case TaskDialogResult.CommandLink4: sel = 3; break;
                    default: return Result.Cancelled;
                }

                int effectiveIdx = sel;

                // Handle "Select ALL"
                if (showSelectAll && sel == 0)
                {
                    var allIds = items.SelectMany(i => i.Ids).Distinct().ToList();
                    uidoc.Selection.SetElementIds(allIds);
                    TaskDialog.Show(title, $"Selected all {allIds.Count} tags.");
                    return Result.Succeeded;
                }

                if (showSelectAll)
                    effectiveIdx = sel - 1; // Adjust for the "Select ALL" link

                // Handle "More"
                int morePosition = show + (showSelectAll ? 1 : 0);
                if (hasMore && sel == morePosition)
                {
                    page++;
                    continue;
                }

                // Handle value selection
                int itemIdx = start + effectiveIdx;
                if (itemIdx >= 0 && itemIdx < items.Count)
                {
                    uidoc.Selection.SetElementIds(items[itemIdx].Ids);
                    TaskDialog.Show(title,
                        $"Selected {items[itemIdx].Ids.Count} tags: {items[itemIdx].Label}");
                    return Result.Succeeded;
                }

                return Result.Cancelled;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Property extraction helpers
        // ══════════════════════════════════════════════════════════════

        private static string SafeGetTagText(IndependentTag tag)
        {
            try
            {
                string text = tag.TagText;
                return string.IsNullOrWhiteSpace(text) ? "<Empty>" : text;
            }
            catch { return "<Error>"; }
        }

        private static double GetTagTextSizeMm(Document doc, IndependentTag tag)
        {
            try
            {
                // Get tag type → look for TEXT_SIZE parameter
                ElementId typeId = tag.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return 0;
                Element tagType = doc.GetElement(typeId);
                if (tagType == null) return 0;

                // Try TEXT_SIZE on the type directly
                Parameter sizeParam = tagType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                if (sizeParam != null && sizeParam.HasValue)
                {
                    double sizeFt = sizeParam.AsDouble();
                    return sizeFt * 304.8; // feet to mm
                }

                // Try to get from the family's text note type
                // For annotation symbol types, text size is in the family definition
                if (tagType is FamilySymbol fs)
                {
                    Family fam = fs.Family;
                    if (fam != null)
                    {
                        var textSizeParam = tagType.get_Parameter(BuiltInParameter.TEXT_SIZE);
                        if (textSizeParam != null && textSizeParam.HasValue)
                            return textSizeParam.AsDouble() * 304.8;
                    }
                }

                return 0;
            }
            catch { return 0; }
        }

        private static string GetArrowheadName(Document doc, IndependentTag tag)
        {
            try
            {
                ElementId typeId = tag.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return "<No Type>";
                Element tagType = doc.GetElement(typeId);
                if (tagType == null) return "<No Type>";

                // Check for LEADER_ARROWHEAD parameter on tag type
                Parameter arrowParam = tagType.get_Parameter(BuiltInParameter.LEADER_ARROWHEAD);
                if (arrowParam != null && arrowParam.HasValue)
                {
                    ElementId arrowId = arrowParam.AsElementId();
                    if (arrowId != null && arrowId != ElementId.InvalidElementId)
                    {
                        Element arrow = doc.GetElement(arrowId);
                        return arrow?.Name ?? "<Unknown>";
                    }
                    return "<None>";
                }

                // Fallback: check instance-level parameter
                arrowParam = tag.get_Parameter(BuiltInParameter.LEADER_ARROWHEAD);
                if (arrowParam != null && arrowParam.HasValue)
                {
                    ElementId arrowId = arrowParam.AsElementId();
                    if (arrowId != null && arrowId != ElementId.InvalidElementId)
                    {
                        Element arrow = doc.GetElement(arrowId);
                        return arrow?.Name ?? "<Unknown>";
                    }
                    return "<None>";
                }

                return "<Default>";
            }
            catch { return "<Error>"; }
        }

        private static int GetLeaderLineWeight(Document doc, View view, IndependentTag tag)
        {
            try
            {
                // Check view-level graphic overrides first
                OverrideGraphicSettings ogs = view.GetElementOverrides(tag.Id);
                int projWeight = ogs.ProjectionLineWeight;
                if (projWeight > 0) return projWeight;

                // Check the tag type for line weight
                ElementId typeId = tag.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    Element tagType = doc.GetElement(typeId);
                    if (tagType != null)
                    {
                        // Check LINE_PEN parameter (common for annotation types)
                        Parameter penParam = tagType.get_Parameter(BuiltInParameter.LINE_PEN);
                        if (penParam != null && penParam.HasValue)
                            return penParam.AsInteger();
                    }
                }

                // Check the category's object style line weight
                if (tag.Category != null)
                {
                    Category cat = tag.Category;
                    try
                    {
                        int catWeight = cat.GetLineWeight(GraphicsStyleType.Projection) ?? -1;
                        if (catWeight > 0) return catWeight;
                    }
                    catch { }
                }

                return -1;
            }
            catch { return -1; }
        }

        private static string GetElbowAngleCategory(Document doc, IndependentTag tag, View view)
        {
            try
            {
                bool hasLeader;
                try { hasLeader = tag.HasLeader; } catch { return "No Leader"; }
                if (!hasLeader) return "No Leader";

                // Get leader elbow and end points to compute angle
                XYZ headPos = tag.TagHeadPosition;
                if (headPos == null) return "No Leader";

                // Try to get elbow and end positions
                var hostIds = tag.GetTaggedLocalElementIds();
                if (hostIds == null || hostIds.Count == 0) return "No Host";

                Reference refr = null;
                try { refr = tag.GetTaggedReferences().FirstOrDefault(); }
                catch { return "<Unknown>"; }

                if (refr == null) return "<Unknown>";

                XYZ elbowPt = null;
                XYZ endPt = null;
                try
                {
                    elbowPt = tag.GetLeaderElbow(refr);
                    endPt = tag.GetLeaderEnd(refr);
                }
                catch { return "Attached (no elbow)"; }

                if (elbowPt == null || endPt == null) return "Attached (no elbow)";

                // Calculate angle between head→elbow and elbow→end segments
                XYZ seg1 = (elbowPt - headPos).Normalize();
                XYZ seg2 = (endPt - elbowPt).Normalize();

                if (seg1.IsZeroLength() || seg2.IsZeroLength())
                    return "Straight (0°)";

                double dot = seg1.DotProduct(seg2);
                dot = Math.Max(-1.0, Math.Min(1.0, dot));
                double angleDeg = Math.Acos(dot) * 180.0 / Math.PI;

                if (angleDeg < 5) return "Straight (0°)";
                if (Math.Abs(angleDeg - 45) < 10) return "45° Elbow";
                if (Math.Abs(angleDeg - 90) < 10) return "90° Elbow";
                if (Math.Abs(angleDeg - 135) < 10) return "135° Elbow";
                if (Math.Abs(angleDeg - 180) < 10) return "180° (U-turn)";
                return $"~{angleDeg:F0}° Elbow";
            }
            catch { return "<Unknown>"; }
        }

        private static string GetTagFamilyName(Document doc, IndependentTag tag)
        {
            try
            {
                ElementId typeId = tag.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId) return "<No Type>";
                Element tagType = doc.GetElement(typeId);
                if (tagType is FamilySymbol fs)
                    return $"{fs.Family?.Name ?? "?"} : {fs.Name}";
                return tagType?.Name ?? "<Unknown>";
            }
            catch { return "<Error>"; }
        }

        private static string GetHostCategoryName(Document doc, IndependentTag tag)
        {
            Element host = GetHostElement(doc, tag);
            if (host?.Category != null)
                return host.Category.Name;
            return "<No Host>";
        }

        private static string GetHostDiscipline(Document doc, IndependentTag tag)
        {
            Element host = GetHostElement(doc, tag);
            if (host == null) return "<No Host>";
            string disc = ParameterHelpers.GetString(host, ParamRegistry.DISC);
            if (!string.IsNullOrEmpty(disc)) return disc;

            // Fallback: derive from category
            string cat = ParameterHelpers.GetCategoryName(host);
            if (TagConfig.DiscMap.TryGetValue(cat, out string mapped))
                return mapped;
            return "<Unassigned>";
        }

        private static string GetLeaderStateName(IndependentTag tag)
        {
            try
            {
                bool hasLeader;
                try { hasLeader = tag.HasLeader; } catch { return "<Unknown>"; }
                if (!hasLeader) return "No Leader";

                LeaderEndCondition endCond;
                try { endCond = tag.LeaderEndCondition; }
                catch { return "Has Leader"; }

                switch (endCond)
                {
                    case LeaderEndCondition.Attached: return "Leader: Attached";
                    case LeaderEndCondition.Free: return "Leader: Free End";
                    default: return "Has Leader";
                }
            }
            catch { return "<Unknown>"; }
        }

        private static Element GetHostElement(Document doc, IndependentTag tag)
        {
            try
            {
                var hostIds = tag.GetTaggedLocalElementIds();
                if (hostIds != null && hostIds.Count > 0)
                    return doc.GetElement(hostIds.First());
            }
            catch { }
            return null;
        }

        private static bool BoxesOverlap(
            (ElementId id, double minX, double minY, double maxX, double maxY) a,
            (ElementId id, double minX, double minY, double maxX, double maxY) b)
        {
            return a.minX < b.maxX && a.maxX > b.minX &&
                   a.minY < b.maxY && a.maxY > b.minY;
        }

        private static string TruncateText(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "<Empty>";
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen - 3) + "...";
        }
    }
}
