using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  4D/5D BIM — Construction Scheduling & Cost Estimation
    //
    //  4D BIM links model elements to a construction schedule (time dimension).
    //  5D BIM links model elements to cost data (cost dimension).
    //
    //  Architecture:
    //    - Schedule data stored as JSON in STING_BIM_MANAGER/schedule_4d.json
    //    - Cost rates stored in STING_BIM_MANAGER/cost_rates_5d.json
    //    - MS Project XML import parsed via System.Xml.Linq
    //    - Element-to-task linkage via Revit Phase + Level + Category mapping
    //    - Auto-scheduling uses construction trade sequencing logic
    //
    //  Inspired by: Synchro 4D Pro, Navisworks TimeLiner, BEXEL Manager,
    //  CostX, Asta Powerproject BIM integration
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: Scheduling4DEngine ──

    internal static class Scheduling4DEngine
    {
        // ── Construction Trade Sequence (standard UK/international order) ──
        // Each trade has a weight determining build order (lower = earlier)
        internal static readonly Dictionary<string, (int order, string trade, int daysPerUnit)> TradeSequence =
            new Dictionary<string, (int, string, int)>
        {
            // Substructure
            ["FOUNDATIONS"]    = (100, "Substructure — Foundations", 14),
            ["PILING"]         = (90, "Substructure — Piling", 21),
            ["BASEMENT"]       = (110, "Substructure — Basement", 21),

            // Structure (by category)
            ["Structural Foundations"] = (100, "Substructure — Foundations", 14),
            ["Structural Framing"]     = (200, "Superstructure — Frame", 7),
            ["Structural Columns"]     = (210, "Superstructure — Columns", 5),
            ["Floors"]                 = (220, "Superstructure — Floors/Slabs", 7),
            ["Walls"]                  = (300, "Envelope/Partitions — Walls", 5),
            ["Curtain Panels"]         = (310, "Envelope — Curtain Wall", 7),
            ["Roofs"]                  = (320, "Envelope — Roofing", 7),

            // Envelope & Finishes
            ["Windows"]        = (400, "Envelope — Windows", 3),
            ["Doors"]          = (410, "Internal — Doors", 2),
            ["Ceilings"]       = (500, "Finishes — Ceilings", 3),

            // MEP First Fix
            ["Ducts"]              = (600, "MEP 1st Fix — Ductwork", 5),
            ["Duct Fittings"]      = (610, "MEP 1st Fix — Duct Fittings", 3),
            ["Flex Ducts"]         = (615, "MEP 1st Fix — Flex Ducts", 2),
            ["Pipes"]              = (620, "MEP 1st Fix — Pipework", 5),
            ["Pipe Fittings"]      = (625, "MEP 1st Fix — Pipe Fittings", 3),
            ["Cable Trays"]        = (630, "MEP 1st Fix — Cable Trays", 3),
            ["Conduits"]           = (635, "MEP 1st Fix — Conduits", 3),
            ["Sprinklers"]         = (640, "MEP 1st Fix — Sprinklers", 3),

            // MEP Equipment
            ["Mechanical Equipment"] = (700, "MEP Equipment — Mechanical", 5),
            ["Electrical Equipment"] = (710, "MEP Equipment — Electrical", 5),
            ["Plumbing Fixtures"]    = (720, "MEP Equipment — Plumbing", 3),
            ["Lighting Fixtures"]    = (730, "MEP Equipment — Lighting", 2),

            // MEP Second Fix
            ["Air Terminals"]    = (800, "MEP 2nd Fix — Air Terminals", 2),
            ["Electrical Fixtures"] = (810, "MEP 2nd Fix — Electrical", 2),
            ["Communication Devices"] = (820, "MEP 2nd Fix — Comms", 2),
            ["Fire Alarm Devices"]    = (830, "MEP 2nd Fix — Fire Alarm", 2),
            ["Security Devices"]      = (840, "MEP 2nd Fix — Security", 2),
            ["Data Devices"]          = (845, "MEP 2nd Fix — Data", 2),
            ["Nurse Call Devices"]    = (850, "MEP 2nd Fix — Nurse Call", 2),

            // Furniture & FF&E
            ["Furniture"]           = (900, "FF&E — Furniture", 2),
            ["Furniture Systems"]   = (910, "FF&E — Furniture Systems", 2),
            ["Specialty Equipment"] = (920, "FF&E — Specialty Equipment", 2),
            ["Casework"]            = (930, "FF&E — Casework", 3)
        };

        // ── Default Unit Cost Rates (GBP per unit, approximate) ──
        internal static readonly Dictionary<string, (double ratePerUnit, string unit, string description)> DefaultCostRates =
            new Dictionary<string, (double, string, string)>
        {
            // Structure
            ["Structural Foundations"] = (250, "m³", "RC foundations"),
            ["Structural Framing"]     = (180, "m", "Steel/RC beams"),
            ["Structural Columns"]     = (350, "each", "Columns"),
            ["Floors"]                 = (120, "m²", "Floor slabs"),
            ["Walls"]                  = (85, "m²", "Internal/external walls"),
            ["Roofs"]                  = (150, "m²", "Roofing system"),
            ["Windows"]                = (450, "each", "Window units"),
            ["Doors"]                  = (350, "each", "Door sets"),
            ["Ceilings"]               = (45, "m²", "Suspended ceilings"),

            // MEP
            ["Ducts"]                  = (55, "m", "Ductwork"),
            ["Pipes"]                  = (35, "m", "Pipework"),
            ["Cable Trays"]            = (28, "m", "Cable management"),
            ["Conduits"]               = (15, "m", "Conduit runs"),
            ["Sprinklers"]             = (85, "each", "Sprinkler heads"),
            ["Mechanical Equipment"]   = (2500, "each", "Plant/AHU/FCU"),
            ["Electrical Equipment"]   = (1500, "each", "DB/switchgear"),
            ["Plumbing Fixtures"]      = (450, "each", "Sanitary ware"),
            ["Lighting Fixtures"]      = (120, "each", "Luminaires"),
            ["Air Terminals"]          = (65, "each", "Grilles/diffusers"),
            ["Electrical Fixtures"]    = (35, "each", "Sockets/switches"),
            ["Fire Alarm Devices"]     = (45, "each", "Detectors/sounders"),
            ["Communication Devices"]  = (75, "each", "Data/comms points"),
            ["Security Devices"]       = (120, "each", "CCTV/access control"),

            // Furniture
            ["Furniture"]              = (300, "each", "General furniture"),
            ["Casework"]               = (200, "m", "Fitted furniture")
        };

        // ═══════════════════════════════════════════════════════════
        //  4D Auto-Schedule Generation
        // ═══════════════════════════════════════════════════════════

        internal static JObject AutoGenerateSchedule(Document doc, DateTime projectStart)
        {
            var schedule = new JObject();
            schedule["project_name"] = doc.ProjectInformation?.Name ?? "Untitled";
            schedule["project_start"] = projectStart.ToString("yyyy-MM-dd");
            schedule["generated_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            // Get all levels ordered by elevation
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();

            // Get phases
            var phases = doc.Phases.Cast<Phase>()
                .OrderBy(p => p.Id.Value).ToList();

            // Collect element counts by category and level
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            var elementsByLevelAndCat = new Dictionary<string, Dictionary<string, int>>();

            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat) && !TradeSequence.ContainsKey(cat)) continue;

                string levelName = "Unassigned";
                var levelParam = el.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                    ?? el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                    ?? el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                if (levelParam != null)
                {
                    var lvl = doc.GetElement(levelParam.AsElementId()) as Level;
                    if (lvl != null) levelName = lvl.Name;
                }

                if (!elementsByLevelAndCat.ContainsKey(levelName))
                    elementsByLevelAndCat[levelName] = new Dictionary<string, int>();
                if (!elementsByLevelAndCat[levelName].ContainsKey(cat))
                    elementsByLevelAndCat[levelName][cat] = 0;
                elementsByLevelAndCat[levelName][cat]++;
            }

            // Generate tasks in construction sequence
            var tasks = new JArray();
            int taskId = 1;
            DateTime currentDate = projectStart;

            // Phase 0: Pre-construction
            tasks.Add(CreateTask(taskId++, "Mobilization & Site Setup", "PRELIMS",
                projectStart, projectStart.AddDays(14), 0, new JArray(), 0));

            // Process levels in order (bottom to top = construction sequence)
            foreach (var level in levels)
            {
                if (!elementsByLevelAndCat.ContainsKey(level.Name)) continue;
                var catCounts = elementsByLevelAndCat[level.Name];

                // Sort categories by trade sequence order
                var sortedCats = catCounts.Keys
                    .OrderBy(c => TradeSequence.ContainsKey(c) ? TradeSequence[c].order : 999)
                    .ToList();

                int levelTaskId = taskId;
                // Summary task for level
                tasks.Add(CreateTask(taskId++, $"Level: {level.Name}", "SUMMARY",
                    currentDate, currentDate, 0, new JArray(), 0));

                foreach (string cat in sortedCats)
                {
                    int count = catCounts[cat];
                    var trade = TradeSequence.ContainsKey(cat)
                        ? TradeSequence[cat]
                        : (999, $"General — {cat}", 3);

                    // Duration scales with element count
                    int baseDays = trade.daysPerUnit;
                    int duration = Math.Max(1, Math.Min((int)(baseDays * (1.0 + count / 50.0)), 30));

                    DateTime taskStart = currentDate;
                    DateTime taskEnd = taskStart.AddDays(duration);

                    // Skip weekends
                    while (taskEnd.DayOfWeek == DayOfWeek.Saturday || taskEnd.DayOfWeek == DayOfWeek.Sunday)
                        taskEnd = taskEnd.AddDays(1);

                    var elementFilter = new JObject
                    {
                        ["category"] = cat,
                        ["level"] = level.Name
                    };

                    tasks.Add(CreateTask(taskId++, $"{trade.trade} — {level.Name}",
                        cat, taskStart, taskEnd, count,
                        new JArray(elementFilter), levelTaskId));

                    // Overlap: structure tasks are sequential, MEP can overlap
                    if (trade.order < 400) // Structure/envelope — sequential
                        currentDate = taskEnd;
                    else if (trade.order < 600) // Internal — 50% overlap
                        currentDate = taskStart.AddDays(duration / 2);
                    // MEP and finishes can run in parallel, so currentDate stays
                }

                currentDate = currentDate.AddDays(2); // Buffer between levels
            }

            // Final tasks
            tasks.Add(CreateTask(taskId++, "Testing & Commissioning", "T&C",
                currentDate, currentDate.AddDays(14), 0, new JArray(), 0));
            currentDate = currentDate.AddDays(14);

            tasks.Add(CreateTask(taskId++, "Snagging & Defects", "SNAGGING",
                currentDate, currentDate.AddDays(7), 0, new JArray(), 0));
            currentDate = currentDate.AddDays(7);

            tasks.Add(CreateTask(taskId++, "Handover", "HANDOVER",
                currentDate, currentDate.AddDays(5), 0, new JArray(), 0));

            schedule["tasks"] = tasks;
            schedule["total_tasks"] = tasks.Count;
            schedule["project_end"] = currentDate.AddDays(5).ToString("yyyy-MM-dd");

            int totalDays = (int)(currentDate.AddDays(5) - projectStart).TotalDays;
            schedule["total_duration_days"] = totalDays;
            schedule["total_duration_weeks"] = Math.Round(totalDays / 7.0, 1);

            return schedule;
        }

        private static JObject CreateTask(int id, string name, string category,
            DateTime start, DateTime finish, int elementCount, JArray elementFilters, int parentId)
        {
            int duration = Math.Max(1, (int)(finish - start).TotalDays);
            return new JObject
            {
                ["task_id"] = id,
                ["wbs"] = $"{id:D3}",
                ["name"] = name,
                ["category"] = category,
                ["start"] = start.ToString("yyyy-MM-dd"),
                ["finish"] = finish.ToString("yyyy-MM-dd"),
                ["duration_days"] = duration,
                ["element_count"] = elementCount,
                ["element_filters"] = elementFilters,
                ["parent_task_id"] = parentId,
                ["percent_complete"] = 0,
                ["status"] = "Not Started",
                ["predecessors"] = new JArray(),
                ["notes"] = ""
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  MS Project XML Import
        // ═══════════════════════════════════════════════════════════

        internal static JObject ImportMSProjectXML(string xmlPath)
        {
            var schedule = new JObject();
            try
            {
                var xdoc = XDocument.Load(xmlPath);
                var ns = xdoc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                // Project properties
                schedule["source_file"] = Path.GetFileName(xmlPath);
                schedule["imported_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                schedule["project_name"] = xdoc.Root?.Element(ns + "Name")?.Value ?? "";
                schedule["project_start"] = xdoc.Root?.Element(ns + "StartDate")?.Value ?? "";
                schedule["project_end"] = xdoc.Root?.Element(ns + "FinishDate")?.Value ?? "";

                // Parse tasks
                var tasks = new JArray();
                var taskElements = xdoc.Descendants(ns + "Task");
                int imported = 0;

                foreach (var taskEl in taskElements)
                {
                    string uid = taskEl.Element(ns + "UID")?.Value ?? "";
                    string name = taskEl.Element(ns + "Name")?.Value ?? "";
                    string startStr = taskEl.Element(ns + "Start")?.Value ?? "";
                    string finishStr = taskEl.Element(ns + "Finish")?.Value ?? "";
                    string durationStr = taskEl.Element(ns + "Duration")?.Value ?? "";
                    string wbs = taskEl.Element(ns + "WBS")?.Value ?? "";
                    string outlineLevel = taskEl.Element(ns + "OutlineLevel")?.Value ?? "0";
                    string pctComplete = taskEl.Element(ns + "PercentComplete")?.Value ?? "0";
                    string isSummary = taskEl.Element(ns + "Summary")?.Value ?? "0";

                    if (string.IsNullOrEmpty(name)) continue;

                    // Parse ISO 8601 duration (PT8H0M0S → hours)
                    int durationDays = ParseDuration(durationStr);

                    // Parse dates
                    DateTime.TryParse(startStr, out DateTime start);
                    DateTime.TryParse(finishStr, out DateTime finish);

                    // Parse predecessors
                    var predecessors = new JArray();
                    foreach (var predEl in taskEl.Elements(ns + "PredecessorLink"))
                    {
                        string predUid = predEl.Element(ns + "PredecessorUID")?.Value ?? "";
                        string predType = predEl.Element(ns + "Type")?.Value ?? "1"; // 1=FS
                        predecessors.Add(new JObject
                        {
                            ["predecessor_uid"] = predUid,
                            ["type"] = predType == "0" ? "FF" : predType == "1" ? "FS" :
                                       predType == "2" ? "SF" : "SS"
                        });
                    }

                    var task = new JObject
                    {
                        ["task_id"] = int.TryParse(uid, out int id) ? id : imported + 1,
                        ["ms_project_uid"] = uid,
                        ["wbs"] = wbs,
                        ["name"] = name,
                        ["start"] = start != DateTime.MinValue ? start.ToString("yyyy-MM-dd") : startStr,
                        ["finish"] = finish != DateTime.MinValue ? finish.ToString("yyyy-MM-dd") : finishStr,
                        ["duration_days"] = durationDays,
                        ["outline_level"] = int.TryParse(outlineLevel, out int ol) ? ol : 0,
                        ["is_summary"] = isSummary == "1",
                        ["percent_complete"] = int.TryParse(pctComplete, out int pct) ? pct : 0,
                        ["predecessors"] = predecessors,
                        ["element_filters"] = new JArray(),
                        ["category"] = "",
                        ["element_count"] = 0,
                        ["auto_linked"] = false,
                        ["notes"] = ""
                    };

                    tasks.Add(task);
                    imported++;
                }

                schedule["tasks"] = tasks;
                schedule["total_tasks"] = imported;

                StingLog.Info($"MS Project import: {imported} tasks from {xmlPath}");
            }
            catch (Exception ex)
            {
                schedule["error"] = ex.Message;
                StingLog.Error($"MS Project import failed: {xmlPath}", ex);
            }

            return schedule;
        }

        private static int ParseDuration(string isoDuration)
        {
            // MS Project duration format: PT8H0M0S or P5D etc.
            if (string.IsNullOrEmpty(isoDuration)) return 0;
            try
            {
                isoDuration = isoDuration.ToUpper().Trim();
                if (isoDuration.StartsWith("PT"))
                {
                    // Hours-based: PT8H0M0S → 1 day per 8 hours
                    string hoursPart = isoDuration.Replace("PT", "").Split('H')[0];
                    if (double.TryParse(hoursPart, out double hours))
                        return Math.Max(1, (int)Math.Ceiling(hours / 8.0));
                }
                else if (isoDuration.Contains("D"))
                {
                    string daysPart = isoDuration.Replace("P", "").Split('D')[0];
                    if (int.TryParse(daysPart, out int days))
                        return Math.Max(1, days);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ParseDuration failed for '{isoDuration}': {ex.Message}"); }
            return 1;
        }

        /// <summary>
        /// Auto-link imported MS Project tasks to Revit elements by matching
        /// task names to category names, trade descriptions, or level names.
        /// </summary>
        internal static int AutoLinkTasksToElements(Document doc, JArray tasks)
        {
            int linked = 0;
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Build level index
            var levelNames = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks)
            {
                if ((bool)(task["is_summary"] ?? false)) continue;
                string name = task["name"]?.ToString()?.ToUpper() ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                // Try matching task name to category
                string matchedCat = null;
                string matchedLevel = null;

                // Direct category match
                foreach (string cat in knownCats)
                {
                    if (name.Contains(cat.ToUpper()))
                    {
                        matchedCat = cat;
                        break;
                    }
                }

                // Trade sequence match
                if (matchedCat == null)
                {
                    foreach (var ts in TradeSequence)
                    {
                        if (name.Contains(ts.Value.trade.ToUpper().Split('—')[0].Trim()) ||
                            name.Contains(ts.Key.ToUpper()))
                        {
                            matchedCat = ts.Key;
                            break;
                        }
                    }
                }

                // Keyword matching for common schedule task names
                if (matchedCat == null)
                {
                    if (name.Contains("DUCT") || name.Contains("HVAC")) matchedCat = "Ducts";
                    else if (name.Contains("PIPE") || name.Contains("PLUMB")) matchedCat = "Pipes";
                    else if (name.Contains("CABLE") || name.Contains("TRAY")) matchedCat = "Cable Trays";
                    else if (name.Contains("CONDUIT")) matchedCat = "Conduits";
                    else if (name.Contains("LIGHT")) matchedCat = "Lighting Fixtures";
                    else if (name.Contains("ELECTR")) matchedCat = "Electrical Equipment";
                    else if (name.Contains("MECH")) matchedCat = "Mechanical Equipment";
                    else if (name.Contains("SPRINK") || name.Contains("FIRE PROT")) matchedCat = "Sprinklers";
                    else if (name.Contains("WALL")) matchedCat = "Walls";
                    else if (name.Contains("FLOOR") || name.Contains("SLAB")) matchedCat = "Floors";
                    else if (name.Contains("ROOF")) matchedCat = "Roofs";
                    else if (name.Contains("DOOR")) matchedCat = "Doors";
                    else if (name.Contains("WINDOW") || name.Contains("GLAZING")) matchedCat = "Windows";
                    else if (name.Contains("CEILING")) matchedCat = "Ceilings";
                    else if (name.Contains("COLUMN")) matchedCat = "Structural Columns";
                    else if (name.Contains("BEAM") || name.Contains("FRAME")) matchedCat = "Structural Framing";
                    else if (name.Contains("FOUNDATION")) matchedCat = "Structural Foundations";
                }

                // Level match
                foreach (string lvl in levelNames)
                {
                    if (name.Contains(lvl.ToUpper()))
                    {
                        matchedLevel = lvl;
                        break;
                    }
                }

                if (matchedCat != null)
                {
                    var filter = new JObject { ["category"] = matchedCat };
                    if (matchedLevel != null) filter["level"] = matchedLevel;
                    task["element_filters"] = new JArray(filter);
                    task["category"] = matchedCat;
                    task["auto_linked"] = true;
                    linked++;
                }
            }

            return linked;
        }

        // ═══════════════════════════════════════════════════════════
        //  5D Cost Estimation Engine
        // ═══════════════════════════════════════════════════════════

        internal static JObject GenerateCostEstimate(Document doc,
            Dictionary<string, (double rate, string unit)> costRates)
        {
            var estimate = new JObject();
            estimate["project_name"] = doc.ProjectInformation?.Name ?? "";
            estimate["generated_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            estimate["currency"] = "GBP";

            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            var lineItems = new JArray();
            double grandTotal = 0;

            // Group elements by category
            var byCategory = new Dictionary<string, List<Element>>();
            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat) && !DefaultCostRates.ContainsKey(cat)) continue;
                if (!byCategory.ContainsKey(cat)) byCategory[cat] = new List<Element>();
                byCategory[cat].Add(el);
            }

            foreach (var kv in byCategory.OrderBy(x => x.Key))
            {
                string cat = kv.Key;
                var elems = kv.Value;
                int qty = elems.Count;

                double rate;
                string unit;
                if (costRates != null && costRates.ContainsKey(cat))
                {
                    rate = costRates[cat].rate;
                    unit = costRates[cat].unit;
                }
                else if (DefaultCostRates.ContainsKey(cat))
                {
                    rate = DefaultCostRates[cat].ratePerUnit;
                    unit = DefaultCostRates[cat].unit;
                }
                else continue;

                double lineTotal = qty * rate;
                grandTotal += lineTotal;

                string disc = "";
                if (elems.Count > 0)
                    disc = ParameterHelpers.GetString(elems[0], ParamRegistry.DISC);

                lineItems.Add(new JObject
                {
                    ["category"] = cat,
                    ["discipline"] = disc,
                    ["quantity"] = qty,
                    ["unit"] = unit,
                    ["unit_rate"] = rate,
                    ["total"] = Math.Round(lineTotal, 2),
                    ["description"] = DefaultCostRates.ContainsKey(cat) ? DefaultCostRates[cat].description : cat
                });
            }

            estimate["line_items"] = lineItems;
            estimate["subtotal"] = Math.Round(grandTotal, 2);
            estimate["preliminaries_pct"] = 12;
            estimate["preliminaries"] = Math.Round(grandTotal * 0.12, 2);
            estimate["contingency_pct"] = 10;
            estimate["contingency"] = Math.Round(grandTotal * 0.10, 2);
            estimate["overhead_profit_pct"] = 8;
            estimate["overhead_profit"] = Math.Round(grandTotal * 0.08, 2);
            estimate["grand_total"] = Math.Round(grandTotal * 1.30, 2); // sub + 12% + 10% + 8%

            // Breakdown by discipline
            var byDisc = lineItems.GroupBy(i => i["discipline"]?.ToString() ?? "?")
                .ToDictionary(g => g.Key, g => g.Sum(i => (double)(i["total"] ?? 0)));
            estimate["discipline_totals"] = JObject.FromObject(
                byDisc.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)));

            return estimate;
        }

        /// <summary>
        /// Generate S-curve cash flow by distributing cost across 4D schedule timeline.
        /// </summary>
        internal static JObject GenerateCashFlow(JObject schedule4D, JObject costEstimate)
        {
            var cashFlow = new JObject();
            cashFlow["generated_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            // Get project timeline
            DateTime.TryParse(schedule4D["project_start"]?.ToString(), out DateTime projStart);
            DateTime.TryParse(schedule4D["project_end"]?.ToString(), out DateTime projEnd);
            if (projStart == DateTime.MinValue) projStart = DateTime.Now;
            if (projEnd == DateTime.MinValue) projEnd = projStart.AddMonths(12);

            double grandTotal = (double)(costEstimate["grand_total"] ?? 0);
            if (grandTotal <= 0) grandTotal = 0;
            int totalMonths = Math.Max(1, (int)Math.Ceiling((projEnd - projStart).TotalDays / 30.0));

            // Generate monthly cash flow (S-curve distribution)
            var monthly = new JArray();
            double cumulative = 0;
            double prevSCurve = 0;
            for (int m = 0; m < totalMonths; m++)
            {
                // S-curve formula: sigmoid distribution (differential between consecutive points)
                double t = (double)(m + 1) / totalMonths;
                double sCurve = 1.0 / (1.0 + Math.Exp(-10 * (t - 0.5)));
                double monthlySpend = grandTotal > 0 ? grandTotal * (sCurve - prevSCurve) : 0;
                if (monthlySpend < 0) monthlySpend = 0;
                prevSCurve = sCurve;
                cumulative += monthlySpend;

                DateTime monthStart = projStart.AddMonths(m);
                monthly.Add(new JObject
                {
                    ["month"] = monthStart.ToString("yyyy-MM"),
                    ["month_name"] = monthStart.ToString("MMM yyyy"),
                    ["planned_spend"] = Math.Round(monthlySpend, 2),
                    ["cumulative"] = Math.Round(cumulative, 2),
                    ["percent_complete"] = grandTotal > 0 ? Math.Round(cumulative / grandTotal * 100, 1) : 0
                });
            }

            cashFlow["monthly"] = monthly;
            cashFlow["total_months"] = totalMonths;
            cashFlow["grand_total"] = Math.Round(grandTotal, 2);

            return cashFlow;
        }

        // ═══════════════════════════════════════════════════════════
        //  Export to MS Project XML
        // ═══════════════════════════════════════════════════════════

        internal static string ExportToMSProjectXML(JObject schedule)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<Project xmlns=\"http://schemas.microsoft.com/project\">");
            sb.AppendLine($"  <Name>{EscapeXml(schedule["project_name"]?.ToString() ?? "")}</Name>");
            sb.AppendLine($"  <StartDate>{schedule["project_start"]}</StartDate>");
            sb.AppendLine($"  <FinishDate>{schedule["project_end"]}</FinishDate>");
            sb.AppendLine("  <CalendarUID>1</CalendarUID>");
            sb.AppendLine("  <Tasks>");

            var tasks = schedule["tasks"] as JArray;
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    sb.AppendLine("    <Task>");
                    sb.AppendLine($"      <UID>{task["task_id"]}</UID>");
                    sb.AppendLine($"      <Name>{EscapeXml(task["name"]?.ToString() ?? "")}</Name>");
                    sb.AppendLine($"      <Start>{task["start"]}T08:00:00</Start>");
                    sb.AppendLine($"      <Finish>{task["finish"]}T17:00:00</Finish>");
                    int days = (int)(task["duration_days"] ?? 1);
                    sb.AppendLine($"      <Duration>PT{days * 8}H0M0S</Duration>");
                    sb.AppendLine($"      <PercentComplete>{task["percent_complete"] ?? 0}</PercentComplete>");

                    // Predecessors
                    var preds = task["predecessors"] as JArray;
                    if (preds != null)
                    {
                        foreach (var pred in preds)
                        {
                            sb.AppendLine("      <PredecessorLink>");
                            sb.AppendLine($"        <PredecessorUID>{pred["predecessor_uid"] ?? pred["task_id"]}</PredecessorUID>");
                            string predType = pred["type"]?.ToString() ?? "FS";
                            int typeCode = predType == "FF" ? 0 : predType == "FS" ? 1 : predType == "SF" ? 2 : 3;
                            sb.AppendLine($"        <Type>{typeCode}</Type>");
                            sb.AppendLine("      </PredecessorLink>");
                        }
                    }
                    sb.AppendLine("    </Task>");
                }
            }

            sb.AppendLine("  </Tasks>");
            sb.AppendLine("</Project>");
            return sb.ToString();
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        // ═══════════════════════════════════════════════════════════
        //  Import Cost Rates from CSV
        // ═══════════════════════════════════════════════════════════

        internal static Dictionary<string, (double rate, string unit)> LoadCostRatesFromCSV(string csvPath)
        {
            var rates = new Dictionary<string, (double, string)>();
            try
            {
                foreach (string line in File.ReadLines(csvPath).Skip(1)) // skip header
                {
                    var parts = StingToolsApp.ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        string cat = parts[0].Trim();
                        if (double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double rate))
                        {
                            string unit = parts.Length >= 3 ? parts[2].Trim() : "each";
                            rates[cat] = (rate, unit);
                        }
                    }
                }
                StingLog.Info($"Loaded {rates.Count} cost rates from {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to load cost rates from {csvPath}", ex);
            }
            return rates;
        }
    }

    #endregion


    // ════════════════════════════════════════════════════════════════════════════
    //  4D/5D COMMANDS
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Command: Auto-Generate 4D Schedule ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSchedule4DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("4D BIM: Auto-generating construction schedule...");

            // Pick start date
            var dateDlg = new TaskDialog("STING 4D BIM — Project Start");
            dateDlg.MainInstruction = "Select construction start date:";
            dateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Today", DateTime.Now.ToString("yyyy-MM-dd"));
            dateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Next Monday", GetNextMonday().ToString("yyyy-MM-dd"));
            dateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "In 1 Month", DateTime.Now.AddMonths(1).ToString("yyyy-MM-dd"));
            dateDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "In 3 Months", DateTime.Now.AddMonths(3).ToString("yyyy-MM-dd"));
            var dateResult = dateDlg.Show();
            DateTime startDate = dateResult switch
            {
                TaskDialogResult.CommandLink1 => DateTime.Now,
                TaskDialogResult.CommandLink2 => GetNextMonday(),
                TaskDialogResult.CommandLink3 => DateTime.Now.AddMonths(1),
                TaskDialogResult.CommandLink4 => DateTime.Now.AddMonths(3),
                _ => DateTime.Now
            };

            var schedule = Scheduling4DEngine.AutoGenerateSchedule(doc, startDate);

            string schedulePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "schedule_4d.json");
            BIMManagerEngine.SaveJsonFile(schedulePath, schedule);

            var tasks = schedule["tasks"] as JArray;
            var report = new StringBuilder();
            report.AppendLine("4D Construction Schedule Generated");
            report.AppendLine(new string('═', 55));
            report.AppendLine($"  Project:  {schedule["project_name"]}");
            report.AppendLine($"  Start:    {schedule["project_start"]}");
            report.AppendLine($"  End:      {schedule["project_end"]}");
            report.AppendLine($"  Duration: {schedule["total_duration_weeks"]} weeks ({schedule["total_duration_days"]} days)");
            report.AppendLine($"  Tasks:    {schedule["total_tasks"]}");
            report.AppendLine();

            // Show first 15 tasks
            if (tasks != null)
            {
                report.AppendLine("  CONSTRUCTION SEQUENCE:");
                foreach (var task in tasks.Take(15))
                {
                    string name = task["name"]?.ToString() ?? "";
                    if (name.Length > 35) name = name.Substring(0, 32) + "...";
                    int elCount = (int)(task["element_count"] ?? 0);
                    report.AppendLine($"    {task["start"],-12} {task["duration_days"],3}d  {name,-35} {(elCount > 0 ? $"({elCount} el)" : "")}");
                }
                if (tasks.Count > 15) report.AppendLine($"    ... and {tasks.Count - 15} more tasks");
            }

            report.AppendLine();
            report.AppendLine($"  Saved: {schedulePath}");

            TaskDialog.Show("STING 4D BIM", report.ToString());
            StingLog.Info($"4D schedule: {schedule["total_tasks"]} tasks, {schedule["total_duration_weeks"]} weeks");
            return Result.Succeeded;
        }

        private static DateTime GetNextMonday()
        {
            var d = DateTime.Now.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
            return d;
        }
    }

    #endregion

    #region ── Command: Import MS Project XML ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportMSProjectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Look for XML files in the BIM Manager directory
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            var xmlFiles = Directory.GetFiles(bimDir, "*.xml")
                .Concat(Directory.GetFiles(bimDir, "*.XML"))
                .Distinct().ToList();

            // Also check project directory
            string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
            if (!string.IsNullOrEmpty(projDir) && Directory.Exists(projDir))
            {
                xmlFiles.AddRange(Directory.GetFiles(projDir, "*.xml")
                    .Where(f => !xmlFiles.Contains(f)));
            }

            if (xmlFiles.Count == 0)
            {
                TaskDialog.Show("STING 4D BIM",
                    "No Microsoft Project XML files found.\n\n" +
                    "Export from MS Project:\n" +
                    "  File → Save As → XML Format (.xml)\n\n" +
                    $"Place the XML file in:\n  {bimDir}");
                return Result.Succeeded;
            }

            // Pick file
            var fileDlg = new TaskDialog("STING 4D BIM — Import MS Project");
            fileDlg.MainInstruction = $"Found {xmlFiles.Count} XML file(s). Select one:";
            if (xmlFiles.Count >= 1)
                fileDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, Path.GetFileName(xmlFiles[0]));
            if (xmlFiles.Count >= 2)
                fileDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, Path.GetFileName(xmlFiles[1]));
            if (xmlFiles.Count >= 3)
                fileDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, Path.GetFileName(xmlFiles[2]));
            if (xmlFiles.Count >= 4)
                fileDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, Path.GetFileName(xmlFiles[3]));
            var fileResult = fileDlg.Show();
            int fileIdx = fileResult switch
            {
                TaskDialogResult.CommandLink1 => 0,
                TaskDialogResult.CommandLink2 => 1,
                TaskDialogResult.CommandLink3 => 2,
                TaskDialogResult.CommandLink4 => 3,
                _ => -1
            };
            if (fileIdx < 0 || fileIdx >= xmlFiles.Count) return Result.Cancelled;

            string xmlPath = xmlFiles[fileIdx];
            StingLog.Info($"4D BIM: Importing MS Project: {xmlPath}");

            var schedule = Scheduling4DEngine.ImportMSProjectXML(xmlPath);

            if (schedule["error"] != null)
            {
                TaskDialog.Show("STING 4D BIM", $"Import failed:\n{schedule["error"]}");
                return Result.Failed;
            }

            // Auto-link tasks to elements
            var tasks = schedule["tasks"] as JArray;
            int linked = 0;
            if (tasks != null)
                linked = Scheduling4DEngine.AutoLinkTasksToElements(doc, tasks);

            // Save
            string schedulePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "schedule_4d.json");
            BIMManagerEngine.SaveJsonFile(schedulePath, schedule);

            var report = new StringBuilder();
            report.AppendLine("MS Project Schedule Imported");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  File:    {Path.GetFileName(xmlPath)}");
            report.AppendLine($"  Project: {schedule["project_name"]}");
            report.AppendLine($"  Start:   {schedule["project_start"]}");
            report.AppendLine($"  End:     {schedule["project_end"]}");
            report.AppendLine($"  Tasks:   {schedule["total_tasks"]}");
            report.AppendLine($"  Auto-linked to elements: {linked} tasks");
            report.AppendLine();
            report.AppendLine("Tasks are linked to Revit elements by matching");
            report.AppendLine("task names to category names and levels.");
            report.AppendLine($"\nSaved: {schedulePath}");

            TaskDialog.Show("STING 4D BIM", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command: View 4D Timeline ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTimeline4DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string schedulePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "schedule_4d.json");
            if (!File.Exists(schedulePath))
            {
                TaskDialog.Show("STING 4D BIM", "No schedule found.\nUse 'Auto Schedule' or 'Import MS Project' first.");
                return Result.Succeeded;
            }

            var schedule = BIMManagerEngine.LoadJsonFile(schedulePath);
            var tasks = schedule["tasks"] as JArray;
            if (tasks == null || tasks.Count == 0)
            {
                TaskDialog.Show("STING 4D BIM", "Schedule has no tasks.");
                return Result.Succeeded;
            }

            var report = new StringBuilder();
            report.AppendLine("4D Construction Timeline");
            report.AppendLine(new string('═', 70));
            report.AppendLine($"  Project: {schedule["project_name"]}");
            report.AppendLine($"  {schedule["project_start"]} → {schedule["project_end"]}");
            report.AppendLine($"  Tasks: {schedule["total_tasks"]}");
            report.AppendLine();

            // Gantt-style text timeline
            DateTime.TryParse(schedule["project_start"]?.ToString(), out DateTime projStart);
            DateTime.TryParse(schedule["project_end"]?.ToString(), out DateTime projEnd);
            int totalWeeks = Math.Max(1, (int)Math.Ceiling((projEnd - projStart).TotalDays / 7.0));
            int barWidth = Math.Min(40, totalWeeks);

            report.AppendLine($"  {"Task",-35} {"Start",-12} {"End",-12} {"Days",4} {"Timeline"}");
            report.AppendLine($"  {new string('─', 35)} {new string('─', 12)} {new string('─', 12)} {new string('─', 4)} {new string('─', barWidth)}");

            foreach (var task in tasks.Take(30))
            {
                string name = task["name"]?.ToString() ?? "";
                if (name.Length > 33) name = name.Substring(0, 30) + "...";
                int days = (int)(task["duration_days"] ?? 0);

                DateTime.TryParse(task["start"]?.ToString(), out DateTime tStart);
                DateTime.TryParse(task["finish"]?.ToString(), out DateTime tFinish);

                // Calculate bar position
                int startPos = totalWeeks > 0 ? (int)((tStart - projStart).TotalDays / 7.0 * barWidth / totalWeeks) : 0;
                int endPos = totalWeeks > 0 ? (int)((tFinish - projStart).TotalDays / 7.0 * barWidth / totalWeeks) : 0;
                startPos = Math.Max(0, Math.Min(startPos, barWidth - 1));
                endPos = Math.Max(startPos + 1, Math.Min(endPos, barWidth));

                string bar = new string(' ', startPos) + new string('█', endPos - startPos) + new string(' ', Math.Max(0, barWidth - endPos));

                report.AppendLine($"  {name,-35} {task["start"],-12} {task["finish"],-12} {days,4} {bar}");
            }

            if (tasks.Count > 30) report.AppendLine($"\n  ... and {tasks.Count - 30} more tasks");
            report.AppendLine($"\n  File: {schedulePath}");

            TaskDialog.Show("STING 4D BIM — Timeline", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command: Export 4D Schedule ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSchedule4DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string schedulePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "schedule_4d.json");
            if (!File.Exists(schedulePath))
            {
                TaskDialog.Show("STING 4D BIM", "No schedule found. Generate one first.");
                return Result.Succeeded;
            }

            var schedule = BIMManagerEngine.LoadJsonFile(schedulePath);

            var dlg = new TaskDialog("STING 4D BIM — Export Format");
            dlg.MainInstruction = "Export schedule as:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "MS Project XML (.xml)", "Compatible with Microsoft Project, Asta Powerproject");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CSV (.csv)", "Compatible with Excel, Primavera P6");
            var result = dlg.Show();

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string exportPath;

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    string xml = Scheduling4DEngine.ExportToMSProjectXML(schedule);
                    exportPath = Path.Combine(bimDir, $"STING_4D_Schedule_{DateTime.Now:yyyyMMdd}.xml");
                    File.WriteAllText(exportPath, xml);
                    TaskDialog.Show("STING 4D BIM", $"Exported to MS Project XML:\n{exportPath}");
                    break;

                case TaskDialogResult.CommandLink2:
                    var csv = new StringBuilder();
                    csv.AppendLine("Task_ID,WBS,Name,Category,Start,Finish,Duration_Days,Element_Count,Percent_Complete,Status");
                    var tasks = schedule["tasks"] as JArray;
                    if (tasks != null)
                    {
                        foreach (var task in tasks)
                        {
                            csv.AppendLine(string.Join(",",
                                BIMManagerEngine.QuoteCSV(task["task_id"]?.ToString()),
                                BIMManagerEngine.QuoteCSV(task["wbs"]?.ToString()),
                                BIMManagerEngine.QuoteCSV(task["name"]?.ToString()),
                                BIMManagerEngine.QuoteCSV(task["category"]?.ToString()),
                                BIMManagerEngine.QuoteCSV(task["start"]?.ToString()),
                                BIMManagerEngine.QuoteCSV(task["finish"]?.ToString()),
                                task["duration_days"]?.ToString() ?? "0",
                                task["element_count"]?.ToString() ?? "0",
                                task["percent_complete"]?.ToString() ?? "0",
                                BIMManagerEngine.QuoteCSV(task["status"]?.ToString())
                            ));
                        }
                    }
                    exportPath = Path.Combine(bimDir, $"STING_4D_Schedule_{DateTime.Now:yyyyMMdd}.csv");
                    File.WriteAllText(exportPath, csv.ToString());
                    TaskDialog.Show("STING 4D BIM", $"Exported to CSV:\n{exportPath}");
                    break;

                default: return Result.Cancelled;
            }
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command: Auto Cost Estimate (5D) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoCost5DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            StingLog.Info("5D BIM: Auto-generating cost estimate...");

            // Check for custom rates
            string ratesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "cost_rates_5d.csv");
            Dictionary<string, (double rate, string unit)> customRates = null;
            if (File.Exists(ratesPath))
                customRates = Scheduling4DEngine.LoadCostRatesFromCSV(ratesPath);

            var estimate = Scheduling4DEngine.GenerateCostEstimate(doc, customRates);

            // Save
            string estimatePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "cost_estimate_5d.json");
            BIMManagerEngine.SaveJsonFile(estimatePath, estimate);

            var lineItems = estimate["line_items"] as JArray;
            var report = new StringBuilder();
            report.AppendLine("5D Cost Estimate Generated");
            report.AppendLine(new string('═', 60));
            report.AppendLine($"  Project: {estimate["project_name"]}");
            report.AppendLine($"  Currency: {estimate["currency"]}");
            report.AppendLine($"  Using: {(customRates != null ? "Custom rates" : "Default rates")}");
            report.AppendLine();

            report.AppendLine($"  {"Category",-25} {"Qty",6} {"Unit",-5} {"Rate",10} {"Total",12}");
            report.AppendLine($"  {new string('─', 25)} {new string('─', 6)} {new string('─', 5)} {new string('─', 10)} {new string('─', 12)}");

            if (lineItems != null)
            {
                foreach (var item in lineItems.Take(20))
                {
                    string cat = item["category"]?.ToString() ?? "";
                    if (cat.Length > 23) cat = cat.Substring(0, 20) + "...";
                    report.AppendLine($"  {cat,-25} {item["quantity"],6} {item["unit"],-5} {(double)(item["unit_rate"] ?? 0),10:N2} {(double)(item["total"] ?? 0),12:N2}");
                }
                if (lineItems.Count > 20) report.AppendLine($"  ... and {lineItems.Count - 20} more items");
            }

            report.AppendLine();
            report.AppendLine($"  {"Subtotal",-50} {(double)(estimate["subtotal"] ?? 0),12:N2}");
            report.AppendLine($"  {"Preliminaries (" + estimate["preliminaries_pct"] + "%)",-50} {(double)(estimate["preliminaries"] ?? 0),12:N2}");
            report.AppendLine($"  {"Contingency (" + estimate["contingency_pct"] + "%)",-50} {(double)(estimate["contingency"] ?? 0),12:N2}");
            report.AppendLine($"  {"OH&P (" + estimate["overhead_profit_pct"] + "%)",-50} {(double)(estimate["overhead_profit"] ?? 0),12:N2}");
            report.AppendLine($"  {new string('═', 62)}");
            report.AppendLine($"  {"GRAND TOTAL",-50} {(double)(estimate["grand_total"] ?? 0),12:N2}");
            report.AppendLine();

            // Discipline totals
            var discTotals = estimate["discipline_totals"] as JObject;
            if (discTotals != null)
            {
                report.AppendLine("  BY DISCIPLINE:");
                foreach (var kv in discTotals)
                    report.AppendLine($"    {kv.Key,-6} {(double)(kv.Value ?? 0),12:N2}");
            }

            report.AppendLine();
            report.AppendLine($"  Saved: {estimatePath}");

            TaskDialog.Show("STING 5D BIM — Cost Estimate", report.ToString());
            StingLog.Info($"5D estimate: {(double)(estimate["grand_total"] ?? 0):N2} GBP");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command: Import Cost Rates ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportCostRatesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string ratesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "cost_rates_5d.csv");

            if (!File.Exists(ratesPath))
            {
                // Create template CSV
                var template = new StringBuilder();
                template.AppendLine("Category,Unit_Rate,Unit,Description");
                foreach (var kv in Scheduling4DEngine.DefaultCostRates)
                    template.AppendLine($"\"{kv.Key}\",{kv.Value.ratePerUnit},\"{kv.Value.unit}\",\"{kv.Value.description}\"");

                try
                {
                    File.WriteAllText(ratesPath, template.ToString());
                    TaskDialog.Show("STING 5D BIM",
                        $"Cost rates template created with {Scheduling4DEngine.DefaultCostRates.Count} default rates:\n\n" +
                        $"{ratesPath}\n\n" +
                        "Edit the unit rates in this CSV file, then run\n" +
                        "'Auto Cost' to generate an estimate with your rates.");
                }
                catch (Exception ex) { TaskDialog.Show("STING", $"Failed: {ex.Message}"); }
            }
            else
            {
                var rates = Scheduling4DEngine.LoadCostRatesFromCSV(ratesPath);
                TaskDialog.Show("STING 5D BIM",
                    $"Loaded {rates.Count} custom cost rates from:\n{ratesPath}\n\n" +
                    "These will be used by 'Auto Cost' instead of defaults.\n" +
                    "Edit the CSV to update rates.");
            }

            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command: Cost Report (5D) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostReport5DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string estimatePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "cost_estimate_5d.json");
            if (!File.Exists(estimatePath))
            {
                TaskDialog.Show("STING 5D BIM", "No cost estimate found.\nUse 'Auto Cost' first.");
                return Result.Succeeded;
            }

            var estimate = BIMManagerEngine.LoadJsonFile(estimatePath);

            // Export as CSV
            string csvPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                $"STING_5D_Cost_Report_{DateTime.Now:yyyyMMdd}.csv");

            var csv = new StringBuilder();
            csv.AppendLine("Category,Discipline,Quantity,Unit,Unit_Rate,Total,Description");
            var lineItems = estimate["line_items"] as JArray;
            if (lineItems != null)
            {
                foreach (var item in lineItems)
                {
                    csv.AppendLine(string.Join(",",
                        BIMManagerEngine.QuoteCSV(item["category"]?.ToString()),
                        BIMManagerEngine.QuoteCSV(item["discipline"]?.ToString()),
                        item["quantity"]?.ToString() ?? "0",
                        BIMManagerEngine.QuoteCSV(item["unit"]?.ToString()),
                        item["unit_rate"]?.ToString() ?? "0",
                        item["total"]?.ToString() ?? "0",
                        BIMManagerEngine.QuoteCSV(item["description"]?.ToString())
                    ));
                }

                csv.AppendLine();
                csv.AppendLine($"\"Subtotal\",,,,,,{estimate["subtotal"]}");
                csv.AppendLine($"\"Preliminaries ({estimate["preliminaries_pct"]}%)\",,,,,,{estimate["preliminaries"]}");
                csv.AppendLine($"\"Contingency ({estimate["contingency_pct"]}%)\",,,,,,{estimate["contingency"]}");
                csv.AppendLine($"\"OH&P ({estimate["overhead_profit_pct"]}%)\",,,,,,{estimate["overhead_profit"]}");
                csv.AppendLine($"\"GRAND TOTAL\",,,,,,{estimate["grand_total"]}");
            }

            try
            {
                File.WriteAllText(csvPath, csv.ToString());
                TaskDialog.Show("STING 5D BIM",
                    $"Cost report exported:\n{csvPath}\n\n" +
                    $"Grand Total: {estimate["currency"]} {(double)(estimate["grand_total"] ?? 0):N2}");
            }
            catch (Exception ex) { TaskDialog.Show("STING", $"Export failed: {ex.Message}"); }

            return Result.Succeeded;
        }
    }

    #endregion

    #region ── Command: Cash Flow S-Curve (4D + 5D) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CashFlow5DCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string schedulePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "schedule_4d.json");
            string estimatePath = BIMManagerEngine.GetBIMManagerFilePath(doc, "cost_estimate_5d.json");

            if (!File.Exists(schedulePath) || !File.Exists(estimatePath))
            {
                TaskDialog.Show("STING 4D/5D BIM",
                    "Cash flow requires both:\n" +
                    "  • 4D Schedule (use 'Auto Schedule' or 'Import MS Project')\n" +
                    "  • 5D Cost Estimate (use 'Auto Cost')\n\n" +
                    "Generate both first, then run Cash Flow.");
                return Result.Succeeded;
            }

            var schedule = BIMManagerEngine.LoadJsonFile(schedulePath);
            var estimate = BIMManagerEngine.LoadJsonFile(estimatePath);

            if (schedule == null || estimate == null || estimate["grand_total"] == null)
            {
                TaskDialog.Show("STING 4D/5D BIM",
                    "Schedule or cost estimate data is corrupt or incomplete.\n" +
                    "Re-generate using 'Auto Schedule' and 'Auto Cost' commands.");
                return Result.Failed;
            }

            var cashFlow = Scheduling4DEngine.GenerateCashFlow(schedule, estimate);

            // Save
            string cashFlowPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "cash_flow_5d.json");
            BIMManagerEngine.SaveJsonFile(cashFlowPath, cashFlow);

            var monthly = cashFlow["monthly"] as JArray;
            var report = new StringBuilder();
            report.AppendLine("Cash Flow Projection (S-Curve)");
            report.AppendLine(new string('═', 65));
            report.AppendLine($"  Project Duration: {cashFlow["total_months"]} months");
            report.AppendLine($"  Grand Total:      {estimate["currency"]} {(double)(cashFlow["grand_total"] ?? 0):N2}");
            report.AppendLine();

            if (monthly != null)
            {
                report.AppendLine($"  {"Month",-12} {"Spend",12} {"Cumulative",12} {"Progress",10} {"S-Curve"}");
                report.AppendLine($"  {new string('─', 12)} {new string('─', 12)} {new string('─', 12)} {new string('─', 10)} {new string('─', 20)}");

                foreach (var m in monthly)
                {
                    double pct = (double)(m["percent_complete"] ?? 0);
                    int barLen = Math.Max(0, Math.Min((int)(pct / 5), 20));
                    string bar = new string('█', barLen) + new string('░', 20 - barLen);

                    report.AppendLine($"  {m["month_name"],-12} {(double)(m["planned_spend"] ?? 0),12:N0} {(double)(m["cumulative"] ?? 0),12:N0} {pct,8:F1}%  {bar}");
                }
            }

            report.AppendLine();

            // Export CSV
            string csvPath = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc),
                $"STING_CashFlow_{DateTime.Now:yyyyMMdd}.csv");
            var csv = new StringBuilder();
            csv.AppendLine("Month,Planned_Spend,Cumulative,Percent_Complete");
            if (monthly != null)
                foreach (var m in monthly)
                    csv.AppendLine($"{m["month_name"]},{m["planned_spend"]},{m["cumulative"]},{m["percent_complete"]}");
            try { File.WriteAllText(csvPath, csv.ToString()); }
            catch { }

            report.AppendLine($"  Saved: {cashFlowPath}");
            report.AppendLine($"  CSV:   {csvPath}");

            TaskDialog.Show("STING 4D/5D BIM — Cash Flow", report.ToString());
            StingLog.Info($"Cash flow: {cashFlow["total_months"]} months, {(double)(cashFlow["grand_total"] ?? 0):N0}");
            return Result.Succeeded;
        }
    }

    #endregion
}
