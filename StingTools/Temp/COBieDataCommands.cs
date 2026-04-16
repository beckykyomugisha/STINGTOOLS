using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Temp
{
    // ══════════════════════════════════════════════════════════════════
    //  COBie Data Commands — reference data lookup, parameter push,
    //  equipment mapping, job templates, spare parts, picklists
    //
    //  Data files (Data/ folder):
    //    COBIE_TYPE_MAP.csv        — 70+ equipment types with STING token mapping
    //    COBIE_PICKLISTS.csv       — COBie V2.4 controlled vocabularies
    //    COBIE_JOB_TEMPLATES.csv   — SFG20/BS 8210 maintenance job templates
    //    COBIE_SPARE_PARTS.csv     — Spare parts per equipment type
    //    COBIE_ATTRIBUTE_TEMPLATES.csv — Expected attributes per Type/Space/Component
    //    COBIE_ZONE_TYPES.csv      — 16 zone type classifications (fire, HVAC, lighting, etc.)
    //    COBIE_SYSTEM_MAP.csv      — 31 building system mappings with Uniclass/CIBSE codes
    //    COBIE_DOCUMENT_TYPES.csv  — 28 O&M document types with regulatory refs
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Browse COBie Type Map — shows equipment type reference data,
    /// lets user select a type and push its properties to selected elements.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieTypeMapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            var types = COBieDataHelper.LoadTypeMap();
            if (types.Count == 0)
            {
                TaskDialog.Show("COBie Type Map", "COBIE_TYPE_MAP.csv not found or empty.\nExpected in Data/ folder.");
                return Result.Failed;
            }

            // Group by category for browsing
            var byCategory = types.GroupBy(t => t.Category)
                .OrderBy(g => g.Key)
                .ToList();

            // Page 1: Choose category
            int page = 0;
            while (true)
            {
                int start = page * 4;
                int remaining = byCategory.Count - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = byCategory.Count; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog catDlg = new TaskDialog("COBie Type Map");
                catDlg.MainInstruction = $"COBie Equipment Type Reference ({types.Count} types)";
                catDlg.MainContent = $"Select an equipment category (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byCategory[start + i];
                    catDlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} types",
                        $"DISC: {grp.First().StingDiscCode}, SYS: {grp.First().StingSysCode}");
                }
                if (hasMore)
                    catDlg.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More categories →", $"{remaining - show} more");

                catDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (catDlg.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selectedCategory = byCategory[start + idx].ToList();
                var pushResult = BrowseAndPushTypes(uidoc, doc, selectedCategory);
                if (pushResult != Result.Cancelled) return pushResult;
                // If cancelled from type browse, go back to category picker
            }
        }

        private static Result BrowseAndPushTypes(UIDocument uidoc, Document doc,
            List<COBieTypeRecord> categoryTypes)
        {
            int page = 0;
            while (true)
            {
                int start = page * 3;
                int remaining = categoryTypes.Count - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = categoryTypes.Count; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog typeDlg = new TaskDialog("COBie Type Map");
                typeDlg.MainInstruction = $"{categoryTypes[0].Category} — Select equipment type";

                for (int i = 0; i < show; i++)
                {
                    var t = categoryTypes[start + i];
                    typeDlg.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{t.TypeCode}: {t.TypeName}",
                        $"£{t.ReplacementCostGBP:N0} | {t.ExpectedLifeYears}yr | Maint: {t.MaintenanceFreqMonths}mo | {t.UniclassCode}");
                }
                if (hasMore)
                    typeDlg.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More types →", $"{remaining - show} more");

                typeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (typeDlg.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selected = categoryTypes[start + idx];
                return ShowTypeDetailAndPush(uidoc, doc, selected);
            }
        }

        private static Result ShowTypeDetailAndPush(UIDocument uidoc, Document doc,
            COBieTypeRecord t)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Type Code:       {t.TypeCode}");
            sb.AppendLine($"Type Name:       {t.TypeName}");
            sb.AppendLine($"Category:        {t.Category}");
            sb.AppendLine($"Asset Type:      {t.AssetType}");
            sb.AppendLine($"Manufacturer:    {t.Manufacturer}");
            sb.AppendLine($"Model Number:    {t.ModelNumber}");
            sb.AppendLine($"Material:        {t.Material}");
            sb.AppendLine($"Finish:          {t.Finish}");
            sb.AppendLine($"Description:     {t.Description}");
            sb.AppendLine();
            sb.AppendLine($"Warranty:        {t.WarrantyDurationYears} years");
            sb.AppendLine($"Expected Life:   {t.ExpectedLifeYears} years");
            sb.AppendLine($"Replace Cost:    £{t.ReplacementCostGBP:N0}");
            sb.AppendLine($"Maintenance:     Every {t.MaintenanceFreqMonths} months");
            sb.AppendLine();
            sb.AppendLine($"SFG20 Code:      {t.SFG20Code}");
            sb.AppendLine($"Uniclass Code:   {t.UniclassCode}");
            sb.AppendLine($"Revit Category:  {t.RevitCategory}");
            sb.AppendLine();
            sb.AppendLine("STING Token Mapping:");
            sb.AppendLine($"  DISC: {t.StingDiscCode}  SYS: {t.StingSysCode}  FUNC: {t.StingFuncCode}  PROD: {t.StingProdCode}");

            var selCount = uidoc.Selection.GetElementIds().Count;

            TaskDialog td = new TaskDialog("COBie Type Detail");
            td.MainInstruction = $"{t.TypeCode}: {t.TypeName}";
            td.MainContent = sb.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Push to selected elements ({selCount})",
                "Write warranty, cost, material, classification, and STING tokens to selection");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Push to ALL matching category",
                $"Write to all '{t.RevitCategory}' elements in project");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "View only (don't push)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            ICollection<ElementId> targetIds;
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("COBie Type Map", "No elements selected. Select elements first.");
                        return Result.Succeeded;
                    }
                    break;
                case TaskDialogResult.CommandLink2:
                    targetIds = CollectByRevitCategory(doc, t.RevitCategory);
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("COBie Type Map", $"No '{t.RevitCategory}' elements found in project.");
                        return Result.Succeeded;
                    }
                    break;
                case TaskDialogResult.CommandLink3:
                    return Result.Succeeded;
                default:
                    return Result.Cancelled;
            }

            int pushed = PushTypeToElements(doc, targetIds, t);
            TaskDialog.Show("COBie Type Map",
                $"Pushed {t.TypeCode} properties to {pushed} of {targetIds.Count} elements.\n\n" +
                "Parameters set: MFR, MODEL, MATERIAL, FINISH, COLOR, SHAPE,\n" +
                "REPLACE_COST, EXPECTED_LIFE, WARRANTY, UNIFORMAT,\n" +
                "DISC, SYS, FUNC, PROD tokens.");
            return Result.Succeeded;
        }

        private static ICollection<ElementId> CollectByRevitCategory(Document doc, string revitCategory)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => ParameterHelpers.GetCategoryName(e) == revitCategory)
                .Select(e => e.Id)
                .ToList();
        }

        private static int PushTypeToElements(Document doc, ICollection<ElementId> ids,
            COBieTypeRecord t)
        {
            int pushed = 0;
            using (Transaction tx = new Transaction(doc, "STING COBie Type Push"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    bool any = false;

                    // COBie Type fields
                    if (SetIfVal(el, ParamRegistry.Ext("MFR"), t.Manufacturer)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("MODEL"), t.ModelNumber)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("MATERIAL"), t.Material)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("FINISH"), t.Finish)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("DESC"), t.Description)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("REPLACE_COST"), t.ReplacementCostGBP.ToString("F0"))) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("EXPECTED_LIFE"), t.ExpectedLifeYears.ToString())) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("DUR_UNIT"), t.DurationUnit)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("WARR_DUR_PARTS"), t.WarrantyDurationYears.ToString())) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("WARR_DUR_UNIT"), "year")) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("MODEL_REF"), t.TypeCode)) any = true;

                    // Classification
                    if (SetIfVal(el, ParamRegistry.Ext("UNIFORMAT"), t.UniclassCode)) any = true;

                    // STING tokens
                    if (SetIfVal(el, ParamRegistry.DISC, t.StingDiscCode)) any = true;
                    if (SetIfVal(el, ParamRegistry.SYS, t.StingSysCode)) any = true;
                    if (SetIfVal(el, ParamRegistry.FUNC, t.StingFuncCode)) any = true;
                    if (SetIfVal(el, ParamRegistry.PROD, t.StingProdCode)) any = true;

                    if (any) pushed++;
                }
                tx.Commit();
            }
            return pushed;
        }

        private static bool SetIfVal(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(paramName)) return false;
            return ParameterHelpers.SetIfEmpty(el, paramName, value);
        }
    }

    /// <summary>
    /// Auto-match elements to COBie Type Map by Revit category and family name,
    /// then push all matching properties in batch.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieAutoMatchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var types = COBieDataHelper.LoadTypeMap();
            if (types.Count == 0)
            {
                TaskDialog.Show("COBie Auto-Match", "COBIE_TYPE_MAP.csv not found or empty.");
                return Result.Failed;
            }

            // Build lookup: RevitCategory + StingProdCode → TypeRecord
            var lookup = new Dictionary<string, COBieTypeRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in types)
            {
                string key = $"{t.RevitCategory}|{t.StingProdCode}";
                if (!lookup.ContainsKey(key))
                    lookup[key] = t;
            }

            // Also build category-only fallback
            var catFallback = new Dictionary<string, COBieTypeRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in types)
            {
                if (!catFallback.ContainsKey(t.RevitCategory))
                    catFallback[t.RevitCategory] = t;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                .ToList();

            // Confirm
            TaskDialog confirm = new TaskDialog("COBie Auto-Match");
            confirm.MainInstruction = $"Auto-match {allElements.Count} elements to {types.Count} COBie types";
            confirm.MainContent = "This will:\n" +
                "1. Match elements by Revit category + PROD code\n" +
                "2. Push warranty, cost, material, classification data\n" +
                "3. Only fill empty parameters (won't overwrite existing values)\n\n" +
                "Proceed?";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int matched = 0, skipped = 0;
            var matchCounts = new Dictionary<string, int>();

            using (Transaction tx = new Transaction(doc, "STING COBie Auto-Match"))
            {
                tx.Start();
                foreach (Element el in allElements)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);

                    COBieTypeRecord match = null;

                    // Try exact match: category + PROD
                    if (!string.IsNullOrEmpty(prod))
                    {
                        string key = $"{cat}|{prod}";
                        lookup.TryGetValue(key, out match);
                    }

                    // Fallback: category only
                    if (match == null)
                        catFallback.TryGetValue(cat, out match);

                    if (match == null) { skipped++; continue; }

                    bool any = false;
                    if (SetIfVal(el, ParamRegistry.Ext("MFR"), match.Manufacturer)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("MODEL"), match.ModelNumber)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("MATERIAL"), match.Material)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("FINISH"), match.Finish)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("DESC"), match.Description)) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("REPLACE_COST"), match.ReplacementCostGBP.ToString("F0"))) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("EXPECTED_LIFE"), match.ExpectedLifeYears.ToString())) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("WARR_DUR_PARTS"), match.WarrantyDurationYears.ToString())) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("WARR_DUR_UNIT"), "year")) any = true;
                    if (SetIfVal(el, ParamRegistry.Ext("UNIFORMAT"), match.UniclassCode)) any = true;

                    if (any)
                    {
                        matched++;
                        if (!matchCounts.ContainsKey(match.TypeCode)) matchCounts[match.TypeCode] = 0;
                        matchCounts[match.TypeCode]++;
                    }
                }
                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Matched:  {matched} elements");
            report.AppendLine($"Skipped:  {skipped} (no matching type)");
            report.AppendLine();
            if (matchCounts.Count > 0)
            {
                report.AppendLine("Top matches:");
                foreach (var kvp in matchCounts.OrderByDescending(x => x.Value).Take(10))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value} elements");
            }

            TaskDialog.Show("COBie Auto-Match", report.ToString());
            return Result.Succeeded;
        }

        private static bool SetIfVal(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(paramName)) return false;
            return ParameterHelpers.SetIfEmpty(el, paramName, value);
        }
    }

    /// <summary>
    /// Browse COBie PickLists — view all COBie V2.4 controlled vocabularies.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBiePickListsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var picklists = COBieDataHelper.LoadPickLists();
            if (picklists.Count == 0)
            {
                TaskDialog.Show("COBie PickLists", "COBIE_PICKLISTS.csv not found or empty.");
                return Result.Failed;
            }

            var byList = picklists.GroupBy(p => p.ListName)
                .OrderBy(g => g.Key)
                .ToList();

            // Page through pick list groups
            int page = 0;
            while (true)
            {
                int start = page * 4;
                int remaining = byList.Count - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = byList.Count; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("COBie PickLists");
                td.MainInstruction = $"COBie V2.4 Controlled Vocabularies ({byList.Count} lists)";
                td.MainContent = $"Select a picklist to view (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byList[start + i];
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} values",
                        grp.First().Description);
                }
                if (hasMore)
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More lists →", $"{remaining - show} more");

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selectedList = byList[start + idx];
                var sb = new StringBuilder();
                sb.AppendLine($"PickList: {selectedList.Key}");
                sb.AppendLine($"Values ({selectedList.Count()}):");
                sb.AppendLine();
                foreach (var item in selectedList)
                    sb.AppendLine($"  • {item.Value} — {item.Description}");

                TaskDialog.Show($"COBie: {selectedList.Key}", sb.ToString());
                return Result.Succeeded;
            }
        }
    }

    /// <summary>
    /// Browse COBie Job Templates — SFG20/BS 8210 maintenance templates.
    /// Push maintenance data to elements as STING parameters.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieJobTemplatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var jobs = COBieDataHelper.LoadJobTemplates();
            if (jobs.Count == 0)
            {
                TaskDialog.Show("COBie Jobs", "COBIE_JOB_TEMPLATES.csv not found or empty.");
                return Result.Failed;
            }

            // Group by type code pattern prefix (e.g. AHU, FCU, BLR)
            var byEquipment = jobs.GroupBy(j =>
            {
                string pat = j.TypeCodePattern;
                int dash = pat.IndexOf('-');
                return dash > 0 ? pat.Substring(0, dash) : pat.Replace("*", "");
            }).OrderBy(g => g.Key).ToList();

            int page = 0;
            while (true)
            {
                int start = page * 4;
                int remaining = byEquipment.Count - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = byEquipment.Count; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("COBie Maintenance Jobs");
                td.MainInstruction = $"SFG20 / BS 8210 Maintenance Templates ({jobs.Count} jobs)";
                td.MainContent = $"Select equipment group (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byEquipment[start + i];
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} maintenance tasks",
                        grp.First().JobName);
                }
                if (hasMore)
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More equipment →", $"{remaining - show} more");

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selectedGroup = byEquipment[start + idx].ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"Maintenance Schedule: {byEquipment[start + idx].Key}");
                sb.AppendLine();
                foreach (var job in selectedGroup)
                {
                    sb.AppendLine($"  {job.JobType}: {job.JobName}");
                    sb.AppendLine($"    {job.Description}");
                    sb.AppendLine($"    Frequency: Every {job.Frequency} {job.FrequencyUnit}  |  Duration: {job.Duration} {job.DurationUnit}");
                    sb.AppendLine($"    Priority: {job.Priority}  |  SFG20: {job.SFG20Code}");
                    sb.AppendLine($"    Resources: {job.ResourceNames}");
                    sb.AppendLine();
                }

                TaskDialog.Show($"COBie Jobs: {byEquipment[start + idx].Key}", sb.ToString());
                return Result.Succeeded;
            }
        }
    }

    /// <summary>
    /// Browse COBie Spare Parts catalogue.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieSparePartsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var spares = COBieDataHelper.LoadSpareParts();
            if (spares.Count == 0)
            {
                TaskDialog.Show("COBie Spare Parts", "COBIE_SPARE_PARTS.csv not found or empty.");
                return Result.Failed;
            }

            var byEquipment = spares.GroupBy(s =>
            {
                string pat = s.TypeCodePattern;
                int dash = pat.IndexOf('-');
                return dash > 0 ? pat.Substring(0, dash) : pat.Replace("*", "");
            }).OrderBy(g => g.Key).ToList();

            int page = 0;
            while (true)
            {
                int start = page * 4;
                int remaining = byEquipment.Count - start;
                if (remaining <= 0) { page = 0; start = 0; remaining = byEquipment.Count; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("COBie Spare Parts");
                td.MainInstruction = $"Spare Parts Catalogue ({spares.Count} parts)";
                td.MainContent = $"Select equipment group (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byEquipment[start + i];
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} spare parts",
                        grp.First().SpareName);
                }
                if (hasMore)
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More equipment →", $"{remaining - show} more");

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selectedGroup = byEquipment[start + idx].ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"Spare Parts: {byEquipment[start + idx].Key}");
                sb.AppendLine();
                foreach (var part in selectedGroup)
                {
                    sb.AppendLine($"  {part.SpareName}");
                    sb.AppendLine($"    Part#: {part.PartNumber}  |  Set: {part.SetNumber}");
                    sb.AppendLine($"    Category: {part.Category}  |  {part.Description}");
                    sb.AppendLine();
                }

                TaskDialog.Show($"COBie Spares: {byEquipment[start + idx].Key}", sb.ToString());
                return Result.Succeeded;
            }
        }
    }

    /// <summary>
    /// Browse COBie Attribute Templates — expected attributes per Type/Space/Component.
    /// Push matching attribute defaults to selected elements.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieAttributeTemplatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var attrs = COBieDataHelper.LoadAttributeTemplates();
            if (attrs.Count == 0)
            {
                TaskDialog.Show("COBie Attributes", "COBIE_ATTRIBUTE_TEMPLATES.csv not found or empty.");
                return Result.Failed;
            }

            var bySheet = attrs.GroupBy(a => a.SheetName)
                .OrderBy(g => g.Key)
                .ToList();

            TaskDialog td = new TaskDialog("COBie Attribute Templates");
            td.MainInstruction = $"COBie Attribute Templates ({attrs.Count} attributes)";
            td.MainContent = "Select a worksheet scope:";

            for (int i = 0; i < Math.Min(bySheet.Count, 4); i++)
            {
                var grp = bySheet[i];
                td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                    $"{grp.Key} — {grp.Count()} attributes",
                    $"Expected attributes for COBie {grp.Key} worksheet");
            }
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            int idx = -1;
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: idx = 0; break;
                case TaskDialogResult.CommandLink2: idx = 1; break;
                case TaskDialogResult.CommandLink3: idx = 2; break;
                case TaskDialogResult.CommandLink4: idx = 3; break;
                default: return Result.Cancelled;
            }

            if (idx < 0 || idx >= bySheet.Count) return Result.Cancelled;

            var selectedSheet = bySheet[idx].ToList();
            // Group by row pattern (equipment type)
            var byPattern = selectedSheet.GroupBy(a => a.RowNamePattern).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"COBie {bySheet[idx].Key} — Expected Attributes");
            sb.AppendLine();
            foreach (var grp in byPattern)
            {
                sb.AppendLine($"  Pattern: {grp.Key}");
                foreach (var attr in grp)
                {
                    string mapped = !string.IsNullOrEmpty(attr.StingParamKey)
                        ? $" → STING:{attr.StingParamKey}"
                        : " (manual)";
                    sb.AppendLine($"    {attr.AttributeName} [{attr.Unit}]{mapped}");
                    if (!string.IsNullOrEmpty(attr.AllowedValues))
                        sb.AppendLine($"      Allowed: {attr.AllowedValues}");
                }
                sb.AppendLine();
            }

            TaskDialog.Show($"COBie Attributes: {bySheet[idx].Key}", sb.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Browse COBie Zone Types — fire, HVAC, lighting, security, acoustic zones.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieZoneTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var zones = COBieDataHelper.LoadZoneTypes();
            if (zones.Count == 0)
            {
                TaskDialog.Show("COBie Zones", "COBIE_ZONE_TYPES.csv not found or empty.");
                return Result.Failed;
            }

            var byCategory = zones.GroupBy(z => z.Category)
                .OrderBy(g => g.Key)
                .ToList();

            int page = 0;
            while (true)
            {
                int start = page * 3;
                int remaining = byCategory.Count - start;
                if (remaining <= 0) { page = 0; continue; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("COBie Zone Types");
                td.MainInstruction = $"COBie Zone Classifications ({zones.Count} types)";
                td.MainContent = $"Select zone category (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byCategory[start + i];
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} zone types",
                        grp.First().ZoneTypeName);
                }
                if (hasMore)
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More categories \u2192", $"{remaining - show} more");

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selectedGroup = byCategory[start + idx].ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"COBie Zone Types: {byCategory[start + idx].Key}");
                sb.AppendLine();
                foreach (var z in selectedGroup)
                {
                    sb.AppendLine($"  {z.ZoneTypeCode}: {z.ZoneTypeName}");
                    sb.AppendLine($"    {z.Description}");
                    sb.AppendLine($"    Classification: {z.ClassificationCode}");
                    sb.AppendLine($"    Regulatory: {z.RegulatoryDriver}");
                    sb.AppendLine($"    Properties: {z.Properties}");
                    sb.AppendLine();
                }

                TaskDialog.Show($"COBie Zones: {byCategory[start + idx].Key}", sb.ToString());
                return Result.Succeeded;
            }
        }
    }

    /// <summary>
    /// COBie Zone Type Audit — cross-references COBIE_ZONE_TYPES.csv
    /// against the project's Revit zones and rooms to identify missing
    /// zone coverage per BS 9999, CIBSE, BS 7671, BS 5266 requirements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieZoneTypeAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null)
            {
                TaskDialog.Show("COBie Zone Type Audit", "No active document.");
                return Result.Failed;
            }
            var doc = ctx.Doc;

            // Load reference data
            var zoneTypes = COBieDataHelper.LoadZoneTypes();
            if (zoneTypes.Count == 0)
            {
                TaskDialog.Show("COBie Zone Type Audit",
                    "Could not load COBIE_ZONE_TYPES.csv. Check data files.");
                return Result.Failed;
            }

            // Collect Revit zones
            var revitZones = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .ToList();

            // Collect Revit rooms for spatial coverage analysis
            var revitRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToList();

            // Build sets of detected zone categories from model data
            var detectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Check zone names/types for category matches
            foreach (var zone in revitZones)
            {
                string name = zone.Name ?? "";
                string dept = ParameterHelpers.GetString(zone, "Department");
                string combined = (name + " " + dept).ToLowerInvariant();

                if (combined.Contains("fire") || combined.Contains("compartment") ||
                    combined.Contains("escape") || combined.Contains("smoke"))
                    detectedCategories.Add("Fire Safety");
                if (combined.Contains("hvac") || combined.Contains("heating") ||
                    combined.Contains("cooling") || combined.Contains("vav") ||
                    combined.Contains("underfloor"))
                    detectedCategories.Add("Environmental");
                if (combined.Contains("light"))
                    detectedCategories.Add("Lighting");
                if (combined.Contains("security") || combined.Contains("access") ||
                    combined.Contains("intruder"))
                    detectedCategories.Add("Security");
                if (combined.Contains("acoustic") || combined.Contains("noise"))
                    detectedCategories.Add("Acoustic");
                if (combined.Contains("clean") || combined.Contains("tenant") ||
                    combined.Contains("fm") || combined.Contains("occupan"))
                    detectedCategories.Add("Management");
                if (combined.Contains("electr") || combined.Contains("distribut"))
                    detectedCategories.Add("Electrical");
                if (combined.Contains("ventilat") || combined.Contains("extract") ||
                    combined.Contains("fresh air"))
                    detectedCategories.Add("Ventilation");
                if (combined.Contains("water") || combined.Contains("plumb") ||
                    combined.Contains("legionella"))
                    detectedCategories.Add("Plumbing");
            }

            // Also check rooms for department-based zone inference
            foreach (var room in revitRooms)
            {
                string dept = ParameterHelpers.GetString(room, "Department");
                string zoneParam = ParameterHelpers.GetString(room, ParamRegistry.ZONE);
                string combined = (dept + " " + zoneParam).ToLowerInvariant();

                if (combined.Contains("fire")) detectedCategories.Add("Fire Safety");
                if (combined.Contains("hvac") || combined.Contains("mech"))
                    detectedCategories.Add("Environmental");
                if (combined.Contains("light")) detectedCategories.Add("Lighting");
                if (combined.Contains("secur")) detectedCategories.Add("Security");
            }

            // Check each COBie zone type for coverage
            var byCategory = zoneTypes.GroupBy(z => z.Category)
                .OrderBy(g => g.Key).ToList();

            int categoriesCovered = byCategory.Count(g =>
                detectedCategories.Contains(g.Key));
            int categoriesTotal = byCategory.Count;
            double coveragePct = categoriesTotal > 0
                ? (double)categoriesCovered / categoriesTotal * 100.0 : 0;

            // Build result panel
            var panel = StingResultPanel.Create("COBie Zone Type Audit");

            panel.AddSection("Model Summary");
            panel.Metric("Revit Zones", revitZones.Count.ToString());
            panel.Metric("Revit Rooms", revitRooms.Count.ToString());
            panel.Metric("COBie Zone Types (Reference)", zoneTypes.Count.ToString());
            panel.Separator();

            panel.AddSection("Zone Category Coverage");
            panel.RAGBar(coveragePct, "Category Coverage");
            panel.Metric("Categories Detected",
                $"{categoriesCovered} of {categoriesTotal}");

            // Coverage table
            var headers = new[] { "Category", "Zone Types", "Detected", "Status" };
            var rows = byCategory.Select(g =>
            {
                bool detected = detectedCategories.Contains(g.Key);
                return new[]
                {
                    g.Key,
                    g.Count().ToString(),
                    detected ? "Yes" : "No",
                    detected ? "COVERED" : "MISSING"
                };
            }).ToList();
            panel.Table(headers, rows);

            // Missing zone categories with required standards
            var missingCategories = byCategory
                .Where(g => !detectedCategories.Contains(g.Key))
                .ToList();

            if (missingCategories.Count > 0)
            {
                panel.AddSection("Missing Zone Categories — Required Actions");
                foreach (var grp in missingCategories)
                {
                    var first = grp.First();
                    panel.MetricWarn(grp.Key,
                        $"{grp.Count()} zone type(s) — Ref: {first.RegulatoryDriver}");
                }
                panel.Info("Create Revit Zones for missing categories " +
                    "to achieve full COBie V2.4 zone coverage.");
            }

            // Property completeness analysis per detected zone category
            if (categoriesCovered > 0)
            {
                panel.AddSection("Zone Property Requirements");
                var propHeaders = new[] { "Zone Type", "Code", "Required Properties" };
                var propRows = zoneTypes
                    .Where(z => detectedCategories.Contains(z.Category))
                    .Select(z => new[]
                    {
                        z.ZoneTypeName,
                        z.ZoneTypeCode,
                        z.Properties.Replace(";", ", ")
                    }).ToList();
                if (propRows.Count > 0)
                    panel.Table(propHeaders, propRows);
            }

            // Export action
            string exportDir = OutputLocationHelper.GetOutputDirectory(doc);
            panel.Action("Export Zone Audit CSV", "Export zone coverage analysis to CSV", (win) =>
            {
                try
                {
                    string csvPath = Path.Combine(exportDir,
                        $"COBie_ZoneType_Audit_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                    var sb = new StringBuilder();
                    sb.AppendLine("ZoneTypeCode,ZoneTypeName,Category,RegulatoryDriver,Status,Properties,ClassificationCode");
                    foreach (var z in zoneTypes)
                    {
                        string status = detectedCategories.Contains(z.Category)
                            ? "COVERED" : "MISSING";
                        sb.AppendLine($"\"{z.ZoneTypeCode}\",\"{z.ZoneTypeName}\"," +
                            $"\"{z.Category}\",\"{z.RegulatoryDriver}\"," +
                            $"\"{status}\",\"{z.Properties}\",\"{z.ClassificationCode}\"");
                    }
                    File.WriteAllText(csvPath, sb.ToString());
                    BIMManagerEngine.AutoRegisterExport(doc, csvPath,
                        "COBie Audit", "COBie Zone Type Coverage Audit Report");
                    TaskDialog.Show("Export Complete", $"Saved to:\n{csvPath}");
                }
                catch (Exception ex)
                {
                    StingLog.Error("COBie ZoneType audit export failed", ex);
                    TaskDialog.Show("Export Error", ex.Message);
                }
            });

            panel.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Browse COBie System Map — building systems with Uniclass/CIBSE codes and STING mapping.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieSystemMapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var systems = COBieDataHelper.LoadSystemMap();
            if (systems.Count == 0)
            {
                TaskDialog.Show("COBie Systems", "COBIE_SYSTEM_MAP.csv not found or empty.");
                return Result.Failed;
            }

            var byCategory = systems.GroupBy(s => s.Category)
                .OrderBy(g => g.Key)
                .ToList();

            int page = 0;
            while (true)
            {
                int start = page * 3;
                int remaining = byCategory.Count - start;
                if (remaining <= 0) { page = 0; continue; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("COBie System Map");
                td.MainInstruction = $"Building Systems ({systems.Count} systems)";
                td.MainContent = $"Select system category (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byCategory[start + i];
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} systems",
                        grp.First().SystemName);
                }
                if (hasMore)
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More categories \u2192", $"{remaining - show} more");

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                var selectedGroup = byCategory[start + idx].ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"COBie Systems: {byCategory[start + idx].Key}");
                sb.AppendLine();
                foreach (var s in selectedGroup)
                {
                    sb.AppendLine($"  {s.SystemCode}: {s.SystemName}");
                    sb.AppendLine($"    {s.Description}");
                    sb.AppendLine($"    Uniclass: {s.UniclassSsCode}  |  CIBSE: {s.CIBSECode}");
                    sb.AppendLine($"    STING: DISC={s.Discipline}  SYS={s.StingSysCode}  FUNC={s.StingFuncCode}");
                    sb.AppendLine($"    Components: {s.ComponentTypes}");
                    if (!string.IsNullOrEmpty(s.DesignCapacity))
                        sb.AppendLine($"    Capacity: {s.DesignCapacity}  |  Redundancy: {s.Redundancy}");
                    sb.AppendLine();
                }

                TaskDialog.Show($"COBie Systems: {byCategory[start + idx].Key}", sb.ToString());
                return Result.Succeeded;
            }
        }
    }

    /// <summary>
    /// Browse COBie Document Types — O&amp;M document types with regulatory refs and naming conventions.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieDocumentTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var docs = COBieDataHelper.LoadDocumentTypes();
            if (docs.Count == 0)
            {
                TaskDialog.Show("COBie Documents", "COBIE_DOCUMENT_TYPES.csv not found or empty.");
                return Result.Failed;
            }

            var byCategory = docs.GroupBy(d => d.Category)
                .OrderBy(g => g.Key)
                .ToList();

            int page = 0;
            while (true)
            {
                int start = page * 3;
                int remaining = byCategory.Count - start;
                if (remaining <= 0) { page = 0; continue; }
                bool hasMore = remaining > 4;
                int show = hasMore ? 3 : Math.Min(remaining, 4);

                TaskDialog td = new TaskDialog("COBie Document Types");
                td.MainInstruction = $"O&M Document Types ({docs.Count} types)";
                td.MainContent = $"Select document category (page {page + 1}):";

                for (int i = 0; i < show; i++)
                {
                    var grp = byCategory[start + i];
                    td.AddCommandLink((TaskDialogCommandLinkId)(i + 1001),
                        $"{grp.Key} — {grp.Count()} document types",
                        grp.First().DocTypeName);
                }
                if (hasMore)
                    td.AddCommandLink((TaskDialogCommandLinkId)(show + 1001),
                        "More categories \u2192", $"{remaining - show} more");

                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                int idx = -1;
                switch (td.Show())
                {
                    case TaskDialogResult.CommandLink1: idx = 0; break;
                    case TaskDialogResult.CommandLink2: idx = 1; break;
                    case TaskDialogResult.CommandLink3: idx = 2; break;
                    case TaskDialogResult.CommandLink4: idx = 3; break;
                    default: return Result.Cancelled;
                }

                if (hasMore && idx == show) { page++; continue; }
                if (idx < 0 || idx >= show) return Result.Cancelled;

                string catName = byCategory[start + idx].Key;
                var selectedGroup = byCategory[start + idx].ToList();

                // Second level: pick a specific document within the category
                int docPage = 0;
                while (true)
                {
                    int docStart = docPage * 3;
                    int docRemaining = selectedGroup.Count - docStart;
                    if (docRemaining <= 0) { docPage = 0; continue; }
                    bool docHasMore = docRemaining > 4;
                    int docShow = docHasMore ? 3 : Math.Min(docRemaining, 4);

                    TaskDialog td2 = new TaskDialog($"COBie Documents: {catName}");
                    td2.MainInstruction = $"{catName} — {selectedGroup.Count} document types";
                    td2.MainContent = docPage > 0
                        ? $"Select a document to view details (page {docPage + 1}):"
                        : "Select a document to view details:";

                    for (int j = 0; j < docShow; j++)
                    {
                        var d = selectedGroup[docStart + j];
                        string mandatory = d.Mandatory == "Yes" ? " [MANDATORY]" : "";
                        td2.AddCommandLink((TaskDialogCommandLinkId)(j + 1001),
                            $"{d.DocTypeCode}: {d.DocTypeName}{mandatory}",
                            d.Description);
                    }
                    if (docHasMore)
                        td2.AddCommandLink((TaskDialogCommandLinkId)(docShow + 1001),
                            "More documents \u2192", $"{docRemaining - docShow} more");

                    td2.CommonButtons = TaskDialogCommonButtons.Cancel;

                    int docIdx = -1;
                    switch (td2.Show())
                    {
                        case TaskDialogResult.CommandLink1: docIdx = 0; break;
                        case TaskDialogResult.CommandLink2: docIdx = 1; break;
                        case TaskDialogResult.CommandLink3: docIdx = 2; break;
                        case TaskDialogResult.CommandLink4: docIdx = 3; break;
                        default: return Result.Cancelled;
                    }

                    if (docHasMore && docIdx == docShow) { docPage++; continue; }
                    if (docIdx < 0 || docIdx >= docShow) return Result.Cancelled;

                    // Third level: show full detail for selected document
                    var sel = selectedGroup[docStart + docIdx];
                    var sb = new StringBuilder();
                    sb.AppendLine($"{sel.DocTypeCode}: {sel.DocTypeName}");
                    sb.AppendLine();
                    sb.AppendLine($"  Description:  {sel.Description}");
                    sb.AppendLine($"  Category:     {sel.Category}");
                    sb.AppendLine($"  Applies to:   {sel.ApplicableTo}");
                    sb.AppendLine($"  Mandatory:    {sel.Mandatory}");
                    if (!string.IsNullOrEmpty(sel.RegulatoryRef))
                        sb.AppendLine($"  Regulation:   {sel.RegulatoryRef}");
                    sb.AppendLine($"  Retention:    {sel.RetentionPeriod}");
                    sb.AppendLine($"  Format:       {sel.Format}");
                    sb.AppendLine($"  Naming:       {sel.NamingConvention}");

                    TaskDialog.Show($"COBie Document: {sel.DocTypeCode}", sb.ToString());
                    return Result.Succeeded;
                }
            }
        }
    }

    /// <summary>
    /// COBie Document Type Audit — cross-references COBIE_DOCUMENT_TYPES.csv
    /// against the project's document_register.json to identify missing
    /// mandatory O&amp;M documents per ISO 19650 / CDM 2015 requirements.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieDocumentTypeAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null)
            {
                TaskDialog.Show("COBie Document Type Audit", "No active document.");
                return Result.Failed;
            }
            var doc = ctx.Doc;

            // Load reference data
            var docTypes = COBieDataHelper.LoadDocumentTypes();
            if (docTypes.Count == 0)
            {
                TaskDialog.Show("COBie Document Type Audit",
                    "Could not load COBIE_DOCUMENT_TYPES.csv. Check data files.");
                return Result.Failed;
            }

            // Load project document register
            string regPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "document_register.json");
            var register = BIMManagerEngine.LoadJsonArray(regPath);

            // Build a set of registered document type codes for fast lookup
            var registeredTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in register)
            {
                string docType = entry["document_type"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(docType))
                    registeredTypes.Add(docType.Trim());
                // Also check file_name for document type code pattern (e.g., "O&M-001" in filename)
                string fileName = entry["file_name"]?.ToString() ?? "";
                foreach (var dt in docTypes)
                {
                    if (fileName.IndexOf(dt.DocTypeCode, StringComparison.OrdinalIgnoreCase) >= 0)
                        registeredTypes.Add(dt.DocTypeCode);
                }
            }

            // Classify each document type
            var mandatory = docTypes.Where(d =>
                d.Mandatory.Equals("Yes", StringComparison.OrdinalIgnoreCase)).ToList();
            var conditional = docTypes.Where(d =>
                d.Mandatory.Equals("Conditional", StringComparison.OrdinalIgnoreCase)).ToList();

            int mandatoryPresent = mandatory.Count(d => registeredTypes.Contains(d.DocTypeCode));
            int mandatoryMissing = mandatory.Count - mandatoryPresent;
            int conditionalPresent = conditional.Count(d => registeredTypes.Contains(d.DocTypeCode));

            double mandatoryPct = mandatory.Count > 0
                ? (double)mandatoryPresent / mandatory.Count * 100.0 : 100.0;

            // Build result panel
            var panel = StingResultPanel.Create("COBie Document Type Audit");

            // Summary section
            panel.AddSection("Summary");
            panel.Metric("Total Document Types (ISO 19650)", docTypes.Count.ToString());
            panel.Metric("Mandatory Types", mandatory.Count.ToString());
            panel.Metric("Conditional Types", conditional.Count.ToString());
            panel.Metric("Documents Registered", register.Count.ToString());
            panel.Separator();

            // Mandatory compliance
            panel.AddSection("Mandatory Document Compliance");
            panel.RAGBar(mandatoryPct, "Mandatory Coverage");
            if (mandatoryPct >= 90)
                panel.MetricHighlight("Status", $"{mandatoryPresent}/{mandatory.Count} mandatory documents present");
            else if (mandatoryPct >= 50)
                panel.MetricWarn("Status", $"{mandatoryMissing} mandatory documents MISSING");
            else
                panel.MetricError("Status", $"{mandatoryMissing} mandatory documents MISSING — action required");

            // Missing mandatory documents table
            var missingMandatory = mandatory
                .Where(d => !registeredTypes.Contains(d.DocTypeCode))
                .ToList();

            if (missingMandatory.Count > 0)
            {
                panel.AddSection("Missing Mandatory Documents");
                var headers = new[] { "Code", "Document Name", "Category", "Regulation" };
                var rows = missingMandatory.Select(d => new[]
                {
                    d.DocTypeCode,
                    d.DocTypeName,
                    d.Category,
                    string.IsNullOrEmpty(d.RegulatoryRef) ? "—" : d.RegulatoryRef
                }).ToList();
                panel.Table(headers, rows);
            }

            // Present documents table
            var presentDocs = docTypes
                .Where(d => registeredTypes.Contains(d.DocTypeCode))
                .ToList();

            if (presentDocs.Count > 0)
            {
                panel.AddSection("Registered Documents");
                var headers2 = new[] { "Code", "Document Name", "Mandatory", "Category" };
                var rows2 = presentDocs.Select(d => new[]
                {
                    d.DocTypeCode,
                    d.DocTypeName,
                    d.Mandatory,
                    d.Category
                }).ToList();
                panel.Table(headers2, rows2);
            }

            // Conditional documents status
            if (conditional.Count > 0)
            {
                panel.AddSection("Conditional Documents");
                panel.Metric("Present", conditionalPresent.ToString());
                panel.Metric("Not registered", (conditional.Count - conditionalPresent).ToString());
                panel.Info("Conditional documents are required based on project scope " +
                    "(e.g., F-Gas for refrigerant systems, BREEAM for certified projects).");
            }

            // By category breakdown
            panel.AddSection("Coverage by Category");
            var categories = docTypes.GroupBy(d => d.Category).OrderBy(g => g.Key);
            var catHeaders = new[] { "Category", "Total", "Present", "Missing" };
            var catRows = categories.Select(g =>
            {
                int total = g.Count();
                int present = g.Count(d => registeredTypes.Contains(d.DocTypeCode));
                return new[] { g.Key, total.ToString(), present.ToString(), (total - present).ToString() };
            }).ToList();
            panel.Table(catHeaders, catRows);

            // Export action
            string exportDir = OutputLocationHelper.GetOutputDirectory(doc);
            panel.Action("Export Gap Report CSV", "Export missing document list to CSV", (win) =>
            {
                try
                {
                    string csvPath = Path.Combine(exportDir,
                        $"COBie_DocType_Audit_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                    var sb = new StringBuilder();
                    sb.AppendLine("DocTypeCode,DocTypeName,Category,Mandatory,Status,RegulatoryRef,RetentionPeriod,Format,NamingConvention");
                    foreach (var dt in docTypes)
                    {
                        string status = registeredTypes.Contains(dt.DocTypeCode) ? "PRESENT" : "MISSING";
                        sb.AppendLine($"\"{dt.DocTypeCode}\",\"{dt.DocTypeName}\",\"{dt.Category}\"," +
                            $"\"{dt.Mandatory}\",\"{status}\",\"{dt.RegulatoryRef}\"," +
                            $"\"{dt.RetentionPeriod}\",\"{dt.Format}\",\"{dt.NamingConvention}\"");
                    }
                    File.WriteAllText(csvPath, sb.ToString());
                    BIMManagerEngine.AutoRegisterExport(doc, csvPath,
                        "COBie Audit", "COBie Document Type Gap Analysis Report");
                    TaskDialog.Show("Export Complete", $"Saved to:\n{csvPath}");
                }
                catch (Exception ex)
                {
                    StingLog.Error("COBie DocType audit export failed", ex);
                    TaskDialog.Show("Export Error", ex.Message);
                }
            });

            panel.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// COBie Data Summary — overview of all COBie reference data loaded.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieDataSummaryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var types = COBieDataHelper.LoadTypeMap();
            var picklists = COBieDataHelper.LoadPickLists();
            var jobs = COBieDataHelper.LoadJobTemplates();
            var spares = COBieDataHelper.LoadSpareParts();
            var attrs = COBieDataHelper.LoadAttributeTemplates();
            var zones = COBieDataHelper.LoadZoneTypes();
            var systems = COBieDataHelper.LoadSystemMap();
            var docs = COBieDataHelper.LoadDocumentTypes();

            var sb = new StringBuilder();
            sb.AppendLine("COBie Reference Data Summary");
            sb.AppendLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            sb.AppendLine();
            sb.AppendLine($"  COBIE_TYPE_MAP.csv:            {types.Count} equipment types");
            sb.AppendLine($"  COBIE_PICKLISTS.csv:           {picklists.Count} picklist values");
            sb.AppendLine($"  COBIE_JOB_TEMPLATES.csv:       {jobs.Count} maintenance jobs");
            sb.AppendLine($"  COBIE_SPARE_PARTS.csv:         {spares.Count} spare parts");
            sb.AppendLine($"  COBIE_ATTRIBUTE_TEMPLATES.csv:  {attrs.Count} attribute definitions");
            sb.AppendLine($"  COBIE_ZONE_TYPES.csv:          {zones.Count} zone classifications");
            sb.AppendLine($"  COBIE_SYSTEM_MAP.csv:          {systems.Count} building systems");
            sb.AppendLine($"  COBIE_DOCUMENT_TYPES.csv:      {docs.Count} document types");
            sb.AppendLine();

            if (types.Count > 0)
            {
                var cats = types.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
                sb.AppendLine($"  Equipment categories: {cats.Count}");
                foreach (string cat in cats)
                {
                    int count = types.Count(t => t.Category == cat);
                    sb.AppendLine($"    • {cat}: {count} types");
                }
                sb.AppendLine();
            }

            if (picklists.Count > 0)
            {
                var lists = picklists.Select(p => p.ListName).Distinct().OrderBy(l => l).ToList();
                sb.AppendLine($"  PickList groups: {lists.Count}");
                foreach (string list in lists)
                    sb.AppendLine($"    • {list}: {picklists.Count(p => p.ListName == list)} values");
                sb.AppendLine();
            }

            if (zones.Count > 0)
            {
                var zoneCats = zones.Select(z => z.Category).Distinct().OrderBy(c => c).ToList();
                sb.AppendLine($"  Zone categories: {zoneCats.Count}");
                foreach (string cat in zoneCats)
                    sb.AppendLine($"    \u2022 {cat}: {zones.Count(z => z.Category == cat)} types");
                sb.AppendLine();
            }

            if (systems.Count > 0)
            {
                var sysCats = systems.Select(s => s.Category).Distinct().OrderBy(c => c).ToList();
                sb.AppendLine($"  System categories: {sysCats.Count}");
                foreach (string cat in sysCats)
                    sb.AppendLine($"    \u2022 {cat}: {systems.Count(s => s.Category == cat)} systems");
                sb.AppendLine();
            }

            if (docs.Count > 0)
            {
                var docCats = docs.Select(d => d.Category).Distinct().OrderBy(c => c).ToList();
                sb.AppendLine($"  Document categories: {docCats.Count}");
                foreach (string cat in docCats)
                    sb.AppendLine($"    \u2022 {cat}: {docs.Count(d => d.Category == cat)} types");
                sb.AppendLine();
            }

            sb.AppendLine("  Standards: COBie V2.4, SFG20, BS 8210, Uniclass 2015,");
            sb.AppendLine("    BS EN ISO 19650, CIBSE, BS 9999, BS 7671, CDM 2015");
            sb.AppendLine("  All files are in Data/ folder and can be edited in Excel/text editor.");

            TaskDialog.Show("COBie Data Summary", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  COBie Data Helper — CSV loading engine
    // ══════════════════════════════════════════════════════════════════

    internal static class COBieDataHelper
    {
        // ── Type Map ───────────────────────────────────────────────────

        internal static List<COBieTypeRecord> LoadTypeMap()
        {
            string path = StingToolsApp.FindDataFile("COBIE_TYPE_MAP.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieTypeRecord>();

            var result = new List<COBieTypeRecord>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; } // skip header
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 21) continue;

                try
                {
                    result.Add(new COBieTypeRecord
                    {
                        TypeCode = fields[0].Trim(),
                        TypeName = fields[1].Trim(),
                        Category = fields[2].Trim(),
                        AssetType = fields[3].Trim(),
                        Manufacturer = fields[4].Trim(),
                        ModelNumber = fields[5].Trim(),
                        WarrantyDurationYears = ParseInt(fields[6]),
                        ReplacementCostGBP = ParseDouble(fields[7]),
                        ExpectedLifeYears = ParseInt(fields[8]),
                        DurationUnit = fields[9].Trim(),
                        MaintenanceFreqMonths = ParseInt(fields[10]),
                        Description = fields[11].Trim(),
                        Material = fields[12].Trim(),
                        Finish = fields[13].Trim(),
                        SFG20Code = fields[14].Trim(),
                        UniclassCode = fields[15].Trim(),
                        RevitCategory = fields[16].Trim(),
                        StingDiscCode = fields[17].Trim(),
                        StingSysCode = fields[18].Trim(),
                        StingFuncCode = fields[19].Trim(),
                        StingProdCode = fields[20].Trim(),
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"COBie TypeMap parse error: {ex.Message} — line: {line.Substring(0, Math.Min(80, line.Length))}");
                }
            }

            StingLog.Info($"COBie TypeMap loaded: {result.Count} types from {path}");
            return result;
        }

        // ── PickLists ──────────────────────────────────────────────────

        internal static List<COBiePickListEntry> LoadPickLists()
        {
            string path = StingToolsApp.FindDataFile("COBIE_PICKLISTS.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBiePickListEntry>();

            var result = new List<COBiePickListEntry>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 3) continue;
                result.Add(new COBiePickListEntry
                {
                    ListName = fields[0].Trim(),
                    Value = fields[1].Trim(),
                    Description = fields[2].Trim(),
                });
            }
            return result;
        }

        // ── Job Templates ──────────────────────────────────────────────

        internal static List<COBieJobRecord> LoadJobTemplates()
        {
            string path = StingToolsApp.FindDataFile("COBIE_JOB_TEMPLATES.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieJobRecord>();

            var result = new List<COBieJobRecord>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 13) continue;

                try
                {
                    result.Add(new COBieJobRecord
                    {
                        TypeCodePattern = fields[0].Trim(),
                        JobName = fields[1].Trim(),
                        JobType = fields[2].Trim(),
                        Description = fields[3].Trim(),
                        Duration = ParseDouble(fields[4]),
                        DurationUnit = fields[5].Trim(),
                        Start = ParseDouble(fields[6]),
                        TaskStartUnit = fields[7].Trim(),
                        Frequency = ParseDouble(fields[8]),
                        FrequencyUnit = fields[9].Trim(),
                        Priority = fields[10].Trim(),
                        SFG20Code = fields[11].Trim(),
                        ResourceNames = fields[12].Trim(),
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"COBie Job parse error: {ex.Message}");
                }
            }
            return result;
        }

        // ── Spare Parts ────────────────────────────────────────────────

        internal static List<COBieSpareRecord> LoadSpareParts()
        {
            string path = StingToolsApp.FindDataFile("COBIE_SPARE_PARTS.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieSpareRecord>();

            var result = new List<COBieSpareRecord>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 7) continue;
                result.Add(new COBieSpareRecord
                {
                    TypeCodePattern = fields[0].Trim(),
                    SpareName = fields[1].Trim(),
                    Category = fields[2].Trim(),
                    PartNumber = fields[3].Trim(),
                    SetNumber = fields[4].Trim(),
                    Description = fields[5].Trim(),
                    Supplier = fields[6].Trim(),
                });
            }
            return result;
        }

        // ── Attribute Templates ────────────────────────────────────────

        internal static List<COBieAttributeTemplate> LoadAttributeTemplates()
        {
            string path = StingToolsApp.FindDataFile("COBIE_ATTRIBUTE_TEMPLATES.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieAttributeTemplate>();

            var result = new List<COBieAttributeTemplate>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 6) continue;
                result.Add(new COBieAttributeTemplate
                {
                    SheetName = fields[0].Trim(),
                    RowNamePattern = fields[1].Trim(),
                    AttributeName = fields[2].Trim(),
                    Unit = fields[3].Trim(),
                    Description = fields[4].Trim(),
                    AllowedValues = fields[5].Trim(),
                    StingParamKey = fields.Length > 6 ? fields[6].Trim() : "",
                });
            }
            return result;
        }

        // ── Zone Types ────────────────────────────────────────────────

        internal static List<COBieZoneTypeRecord> LoadZoneTypes()
        {
            string path = StingToolsApp.FindDataFile("COBIE_ZONE_TYPES.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieZoneTypeRecord>();

            var result = new List<COBieZoneTypeRecord>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 7) continue;
                result.Add(new COBieZoneTypeRecord
                {
                    ZoneTypeCode = fields[0].Trim(),
                    ZoneTypeName = fields[1].Trim(),
                    Category = fields[2].Trim(),
                    Description = fields[3].Trim(),
                    ClassificationCode = fields[4].Trim(),
                    RegulatoryDriver = fields[5].Trim(),
                    Properties = fields[6].Trim(),
                });
            }
            return result;
        }

        // ── System Map ───────────────────────────────────────────────────

        internal static List<COBieSystemRecord> LoadSystemMap()
        {
            string path = StingToolsApp.FindDataFile("COBIE_SYSTEM_MAP.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieSystemRecord>();

            var result = new List<COBieSystemRecord>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 12) continue;
                try
                {
                    result.Add(new COBieSystemRecord
                    {
                        SystemCode = fields[0].Trim(),
                        SystemName = fields[1].Trim(),
                        Category = fields[2].Trim(),
                        Description = fields[3].Trim(),
                        UniclassSsCode = fields[4].Trim(),
                        CIBSECode = fields[5].Trim(),
                        Discipline = fields[6].Trim(),
                        StingSysCode = fields[7].Trim(),
                        StingFuncCode = fields[8].Trim(),
                        ComponentTypes = fields[9].Trim(),
                        DesignCapacity = fields[10].Trim(),
                        Redundancy = fields[11].Trim(),
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"COBie SystemMap parse error: {ex.Message}");
                }
            }
            return result;
        }

        // ── Document Types ───────────────────────────────────────────────

        internal static List<COBieDocumentTypeRecord> LoadDocumentTypes()
        {
            string path = StingToolsApp.FindDataFile("COBIE_DOCUMENT_TYPES.csv");
            if (string.IsNullOrEmpty(path)) return new List<COBieDocumentTypeRecord>();

            var result = new List<COBieDocumentTypeRecord>();
            bool first = true;
            foreach (string line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var fields = StingToolsApp.ParseCsvLine(line);
                if (fields.Length < 10) continue;
                try
                {
                    result.Add(new COBieDocumentTypeRecord
                    {
                        DocTypeCode = fields[0].Trim(),
                        DocTypeName = fields[1].Trim(),
                        Category = fields[2].Trim(),
                        Description = fields[3].Trim(),
                        ApplicableTo = fields[4].Trim(),
                        Mandatory = fields[5].Trim(),
                        RegulatoryRef = fields[6].Trim(),
                        RetentionPeriod = fields[7].Trim(),
                        Format = fields[8].Trim(),
                        NamingConvention = fields[9].Trim(),
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"COBie DocTypes parse error: {ex.Message}");
                }
            }
            return result;
        }

        // ── Parse helpers ──────────────────────────────────────────────

        private static int ParseInt(string s)
        {
            if (int.TryParse(s.Trim(), out int v)) return v;
            return 0;
        }

        private static double ParseDouble(string s)
        {
            if (double.TryParse(s.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            return 0;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Data model classes
    // ══════════════════════════════════════════════════════════════════

    internal class COBieTypeRecord
    {
        public string TypeCode { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public string AssetType { get; set; }
        public string Manufacturer { get; set; }
        public string ModelNumber { get; set; }
        public int WarrantyDurationYears { get; set; }
        public double ReplacementCostGBP { get; set; }
        public int ExpectedLifeYears { get; set; }
        public string DurationUnit { get; set; }
        public int MaintenanceFreqMonths { get; set; }
        public string Description { get; set; }
        public string Material { get; set; }
        public string Finish { get; set; }
        public string SFG20Code { get; set; }
        public string UniclassCode { get; set; }
        public string RevitCategory { get; set; }
        public string StingDiscCode { get; set; }
        public string StingSysCode { get; set; }
        public string StingFuncCode { get; set; }
        public string StingProdCode { get; set; }
    }

    internal class COBiePickListEntry
    {
        public string ListName { get; set; }
        public string Value { get; set; }
        public string Description { get; set; }
    }

    internal class COBieJobRecord
    {
        public string TypeCodePattern { get; set; }
        public string JobName { get; set; }
        public string JobType { get; set; }
        public string Description { get; set; }
        public double Duration { get; set; }
        public string DurationUnit { get; set; }
        public double Start { get; set; }
        public string TaskStartUnit { get; set; }
        public double Frequency { get; set; }
        public string FrequencyUnit { get; set; }
        public string Priority { get; set; }
        public string SFG20Code { get; set; }
        public string ResourceNames { get; set; }
    }

    internal class COBieSpareRecord
    {
        public string TypeCodePattern { get; set; }
        public string SpareName { get; set; }
        public string Category { get; set; }
        public string PartNumber { get; set; }
        public string SetNumber { get; set; }
        public string Description { get; set; }
        public string Supplier { get; set; }
    }

    internal class COBieAttributeTemplate
    {
        public string SheetName { get; set; }
        public string RowNamePattern { get; set; }
        public string AttributeName { get; set; }
        public string Unit { get; set; }
        public string Description { get; set; }
        public string AllowedValues { get; set; }
        public string StingParamKey { get; set; }
    }

    internal class COBieZoneTypeRecord
    {
        public string ZoneTypeCode { get; set; }
        public string ZoneTypeName { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string ClassificationCode { get; set; }
        public string RegulatoryDriver { get; set; }
        public string Properties { get; set; }
    }

    internal class COBieSystemRecord
    {
        public string SystemCode { get; set; }
        public string SystemName { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string UniclassSsCode { get; set; }
        public string CIBSECode { get; set; }
        public string Discipline { get; set; }
        public string StingSysCode { get; set; }
        public string StingFuncCode { get; set; }
        public string ComponentTypes { get; set; }
        public string DesignCapacity { get; set; }
        public string Redundancy { get; set; }
    }

    internal class COBieDocumentTypeRecord
    {
        public string DocTypeCode { get; set; }
        public string DocTypeName { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string ApplicableTo { get; set; }
        public string Mandatory { get; set; }
        public string RegulatoryRef { get; set; }
        public string RetentionPeriod { get; set; }
        public string Format { get; set; }
        public string NamingConvention { get; set; }
    }
}
