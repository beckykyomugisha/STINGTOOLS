using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;   // G-13: MaterialLookupCsv — backs lookup() in the parser

namespace StingTools.Temp
{
    /// <summary>
    /// Formula evaluation engine for FORMULAS_WITH_DEPENDENCIES.csv.
    /// Reads 280 formula definitions (v3.0) across 10 disciplines, evaluates them in
    /// dependency order (level 0 → 6), and writes computed values to element parameters.
    /// Formula types: paragraph assembly (36), warning thresholds (30), derived calculations (17),
    /// plus 197 original formulas. Supports: arithmetic (+,-,*,/,^), parentheses, if() conditionals,
    /// log(), string concatenation, and Revit built-in geometry inputs (Width, Height, Length, etc.).
    /// Paragraph formulas are gated by TAG_PARA_STATE_1/2/3_BOOL for 3-state depth control.
    /// Warning formulas auto-append threshold violations gated by TAG_WARN_VISIBLE_BOOL.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FormulaEvaluatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // G-5: batch boundary — restore the formula-failure warn allowance so
            // this run's diagnostics are not suppressed by an earlier run's.
            FormulaEngine.ResetWarnBudget();

            // G-27 — report-only lookup() resolution audit for this run.
            LookupAudit.BeginRun();

            string csvPath = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
            if (csvPath == null)
            {
                TaskDialog.Show("Formula Evaluator",
                    "FORMULAS_WITH_DEPENDENCIES.csv not found.\n" +
                    $"Searched: {StingToolsApp.DataPath}");
                return Result.Failed;
            }

            // Load and parse formula definitions
            var formulas = FormulaEngine.LoadFormulas(csvPath);
            if (formulas.Count == 0)
            {
                TaskDialog.Show("Formula Evaluator", "No formulas found in CSV.");
                return Result.Failed;
            }

            // LoadFormulas now returns topologically sorted list with cycle detection

            // ENH-P3: Validate formulas against parameter registry (warn on orphans)
            FormulaEngine.ValidateAgainstRegistry(formulas);

            // DAT-005: Validate dependency DAG — check that formulas only depend on
            // parameters written at equal or lower dependency levels
            ValidateFormulaDag(formulas);

            // Collect taggable elements only (skip views, sheets, annotations, etc.)
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(
                    SharedParamGuids.AllCategoryEnums.ToList()))
                .ToList();

            int totalEvaluated = 0;
            int totalWritten = 0;
            int totalErrors = 0;
            int totalSkipped = 0;
            int elementsProcessed = 0;

            // Per-formula error tracking
            var formulaErrorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var formulaSampleFailures = new Dictionary<string, List<ElementId>>(StringComparer.Ordinal);
            // Per-formula skip tracking — missing input parameters
            var formulaSkipCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            // PERF-003: Build discipline-to-formula index before element loop so per-element
            // formula iteration is limited to relevant formulas, not all 199 for every element.
            // Elements with DISC=M skip E/P/A/S-only formulas, reducing inner iterations by ~75%.
            // Formulas with empty/ALL Discipline apply to every element (stored under "" key).
            var formulasByDisc = new Dictionary<string, List<FormulaEngine.FormulaDefinition>>(
                StringComparer.OrdinalIgnoreCase);
            var formulasForAll = new List<FormulaEngine.FormulaDefinition>(); // applies to all disciplines
            foreach (var f in formulas)
            {
                string disc = (f.Discipline ?? "").Trim();
                if (string.IsNullOrEmpty(disc) ||
                    disc.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                    disc.Equals("GEN", StringComparison.OrdinalIgnoreCase))
                {
                    formulasForAll.Add(f);
                }
                else
                {
                    if (!formulasByDisc.ContainsKey(disc))
                        formulasByDisc[disc] = new List<FormulaEngine.FormulaDefinition>();
                    formulasByDisc[disc].Add(f);
                }
            }

            bool cancelled = false;
            int elIndex = 0;

            using (Transaction tx = new Transaction(doc, "STING Evaluate Formulas"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    // Cancellation check every 200 elements
                    if (++elIndex % 200 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        cancelled = true;
                        StingLog.Info($"Formula evaluator: cancelled by user at {elIndex}/{collector.Count} elements");
                        break;
                    }

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName)) continue;

                    // PERF-003: Only evaluate formulas relevant to this element's discipline.
                    // Read the element's DISC token; combine discipline-specific + universal formulas.
                    string elDisc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    IEnumerable<FormulaEngine.FormulaDefinition> activeFormulas = formulasForAll;
                    if (!string.IsNullOrEmpty(elDisc) &&
                        formulasByDisc.TryGetValue(elDisc, out var discFormulas))
                        activeFormulas = formulasForAll.Concat(discFormulas);

                    bool anyWritten = false;

                    foreach (var formula in activeFormulas)
                    {
                        try
                        {
                            // Check if the element has the target parameter
                            Parameter targetParam = ParameterHelpers.CachedLookup(el, formula.ParameterName);
                            if (targetParam == null || targetParam.IsReadOnly) continue;

                            // Collect input values
                            var context = FormulaEngine.BuildContext(el, formula);
                            if (context == null)
                            {
                                // Track formulas skipped due to missing inputs for audit
                                totalSkipped++;
                                string sKey = formula.ParameterName;
                                formulaSkipCounts.TryGetValue(sKey, out int sk);
                                formulaSkipCounts[sKey] = sk + 1;
                                continue;
                            }

                            totalEvaluated++;

                            if (formula.DataType == "TEXT")
                            {
                                // String concatenation formulas
                                string result = FormulaEngine.EvaluateText(formula.Expression, context);
                                if (result != null && targetParam.StorageType == StorageType.String)
                                {
                                    string current = targetParam.AsString() ?? "";
                                    // Display mirrors (<source>_DISP_TXT) auto-refresh: they re-write
                                    // whenever the numeric source changes so the tag text stays in
                                    // sync. Every other text formula stays write-once (fill only when
                                    // empty) so it never clobbers a user's manual edit.
                                    bool isMirror = formula.ParameterName != null &&
                                        formula.ParameterName.EndsWith("_DISP_TXT", StringComparison.OrdinalIgnoreCase);
                                    if ((string.IsNullOrEmpty(current) || isMirror) && current != result)
                                    {
                                        targetParam.Set(result);
                                        totalWritten++;
                                        anyWritten = true;
                                    }
                                }
                            }
                            else
                            {
                                // Numeric formulas
                                double? result = FormulaEngine.EvaluateNumeric(
                                    formula.Expression, context);
                                if (result.HasValue && !double.IsNaN(result.Value)
                                    && !double.IsInfinity(result.Value))
                                {
                                    // G-4 — the ConvertToInternalUnits call that stood here
                                    // has been REMOVED. It was the only real call site of it
                                    // in the codebase, so this single line decided whether a
                                    // metric formula result was stored as metres or as feet.
                                    //
                                    // It cannot be correct anywhere: MR_PARAMETERS.txt
                                    // declares no LENGTH, AREA or VOLUME parameters at all
                                    // (TEXT 2,819 / YESNO 265 / NUMBER 221 / INTEGER 93), so
                                    // there is no unit-typed target for feet to be the right
                                    // storage FOR. Every _MM/_SQ_M/_CU_M target is TEXT
                                    // holding metric, and nothing converts back on read.
                                    //
                                    // It was also applied inconsistently by accident rather
                                    // than by design: the switch carries M2/SQ_M/
                                    // SQUARE_METERS but not "m²", so of two rows with
                                    // BYTE-IDENTICAL expressions —
                                    //   CST_S_MAS_WALL_AREA_SQ_M - CST_S_MAS_OPENING_AREA_SQ_M
                                    // — CST_S_MAS_NET_WALL_AREA_SQ_M (unit "m2") was scaled
                                    // by 1/0.3048² = 10.7639 and CST_S_MAS_NET_AREA_SQ_M
                                    // (unit "m²") was not. Whether a quantity was corrupted
                                    // depended on which glyph the author typed.
                                    //
                                    // Applying it consistently at all eight EvaluateNumeric
                                    // sites would make every metric target uniformly wrong;
                                    // removing it makes them uniformly right.
                                    bool written = FormulaEngine.WriteNumericResult(
                                        targetParam, result.Value);
                                    if (written)
                                    {
                                        totalWritten++;
                                        anyWritten = true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            totalErrors++;
                            // Per-formula error tracking
                            string fKey = formula.ParameterName;
                            formulaErrorCounts.TryGetValue(fKey, out int ec);
                            formulaErrorCounts[fKey] = ec + 1;
                            if (ec == 0) formulaSampleFailures[fKey] = new List<ElementId>();
                            if (formulaSampleFailures[fKey].Count < 5)
                                formulaSampleFailures[fKey].Add(el.Id);

                            if (totalErrors <= 10)
                                StingLog.Warn($"Formula '{formula.ParameterName}' on element {el.Id}: {ex.Message}");
                        }
                    }

                    if (anyWritten) elementsProcessed++;

                    // Progress logging every 1000 elements
                    if (elementsProcessed > 0 && elementsProcessed % 1000 == 0)
                        StingLog.Info($"Formula evaluator: {elementsProcessed} elements processed...");
                }

                if (cancelled)
                    tx.RollBack();
                else
                    tx.Commit();
            }

            var report = new StringBuilder();
            if (cancelled)
                report.AppendLine($"Formula Evaluation CANCELLED by user at {elIndex}/{collector.Count} elements");
            else
                report.AppendLine($"Formula Evaluation Complete");
            report.AppendLine($"Formulas loaded: {formulas.Count} (dependency levels 0-6)");
            report.AppendLine($"Elements updated: {elementsProcessed}");
            report.AppendLine($"Values written: {totalWritten}");
            report.AppendLine($"Evaluations attempted: {totalEvaluated}");
            if (totalSkipped > 0)
            {
                report.AppendLine($"Skipped: {totalSkipped} (missing input parameters)");
                // Report top-5 skipped formulas
                var topSkipped = formulaSkipCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5);
                report.AppendLine();
                report.AppendLine("Top skipped formulas (missing inputs):");
                foreach (var kvp in topSkipped)
                {
                    report.AppendLine($"  {kvp.Key}: {kvp.Value} elements lacked inputs");
                    StingLog.Info($"Formula skip: '{kvp.Key}' skipped {kvp.Value} elements — missing input parameters");
                }
            }

            if (totalErrors > 0)
            {
                report.AppendLine($"Errors: {totalErrors} (see log for details)");

                // Report top-5 failing formulas
                var topFailures = formulaErrorCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5);
                report.AppendLine();
                report.AppendLine("Top failing formulas:");
                foreach (var kvp in topFailures)
                {
                    string sampleIds = string.Join(", ",
                        formulaSampleFailures[kvp.Key].Select(id => id.ToString()));
                    report.AppendLine($"  {kvp.Key}: {kvp.Value} errors (samples: {sampleIds})");
                    StingLog.Warn($"Formula summary: '{kvp.Key}' failed {kvp.Value} times, " +
                        $"sample elements: {sampleIds}");
                }
            }

            // G-27 — how many lookups were MEASURED vs ASSUMED. Surfaced here
            // because a flag only in the log is a flag nobody reads.
            string g27 = LookupAudit.EndRun();
            if (!string.IsNullOrEmpty(g27))
                report.AppendLine().AppendLine("── Quantity confidence (G-27) ──").AppendLine(g27);

            TaskDialog.Show("Formula Evaluator", report.ToString());

            return Result.Succeeded;
        }

        /// <summary>
        /// DAT-005: Validate dependency DAG — each formula should only reference
        /// parameters written at equal or lower dependency levels. Log warnings
        /// for any violations.
        /// </summary>
        private static void ValidateFormulaDag(List<FormulaEngine.FormulaDefinition> formulas)
        {
            // Build output-to-level map: parameter name → dependency level it's written at
            var outputLevel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in formulas)
            {
                if (!outputLevel.ContainsKey(f.ParameterName))
                    outputLevel[f.ParameterName] = f.DependencyLevel;
            }

            int violations = 0;
            foreach (var f in formulas)
            {
                foreach (string input in f.InputParameters)
                {
                    if (string.IsNullOrEmpty(input)) continue;
                    if (outputLevel.TryGetValue(input, out int inputLevel))
                    {
                        if (inputLevel > f.DependencyLevel)
                        {
                            violations++;
                            if (violations <= 10)
                                StingLog.Warn($"Formula DAG violation: '{f.ParameterName}' (level {f.DependencyLevel}) " +
                                    $"reads '{input}' which is written at level {inputLevel}");
                        }
                    }
                }
            }

            if (violations > 0)
                StingLog.Warn($"Formula DAG: {violations} dependency violation(s) detected — " +
                    "some formulas may read stale values from a previous session");
            else
                StingLog.Info("Formula DAG: all dependencies validated — no violations");
        }
    }

    /// <summary>
    /// Formula evaluation engine — parses and evaluates expressions from
    /// FORMULAS_WITH_DEPENDENCIES.csv. Handles arithmetic, conditionals,
    /// string concatenation, and Revit geometry inputs.
    /// </summary>
    internal static class FormulaEngine
    {
        // LOG-02: Cache parsed formulas keyed by file path to avoid re-reading CSV
        // on every call. Invalidated when file path changes (document switch) or
        // when file modification time changes (manual edit).
        private static string _cachedCsvPath;
        private static DateTime _cachedCsvWriteTime;
        private static List<FormulaDefinition> _cachedFormulas;
        private static readonly object _formulaCacheLock = new object();

        /// <summary>Invalidate cached formulas (call on document switch).</summary>
        public static void InvalidateFormulaCache()
        {
            lock (_formulaCacheLock)
            {
                _cachedFormulas = null;
                _cachedCsvPath = null;
            }
        }

        /// <summary>Backwards-compatible alias for InvalidateFormulaCache.</summary>
        public static void ClearCache() => InvalidateFormulaCache();

        /// <summary>Parsed formula definition from CSV.</summary>
        internal class FormulaDefinition
        {
            public string Discipline;
            public string ParameterName;
            public string DataType;       // TEXT, NUMBER, AREA, VOLUME, LENGTH, etc.
            public string Expression;
            public string Description;
            public string[] InputParameters;
            public string Unit;
            public int DependencyLevel;
            public bool UsesBuiltinGeometry;
            public string[] BuiltinInputs;
        }

        /// <summary>Load formula definitions from CSV file (cached; re-reads on path or file change).</summary>
        public static List<FormulaDefinition> LoadFormulas(string csvPath)
        {
            // LOG-02: Return cached formulas if path and file modification time match
            lock (_formulaCacheLock)
            {
                if (_cachedFormulas != null && _cachedCsvPath == csvPath)
                {
                    try
                    {
                        var writeTime = File.GetLastWriteTimeUtc(csvPath);
                        if (writeTime == _cachedCsvWriteTime)
                            return _cachedFormulas;
                    }
                    catch (Exception ex) { StingLog.Warn($"file access error — reload: {ex.Message}"); }
                }
            }

            var formulas = new List<FormulaDefinition>();

            try
            {
                var lines = File.ReadAllLines(csvPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1); // skip header

                int droppedShort = 0;
                var droppedNames = new List<string>();

                foreach (string line in lines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 10)
                    {
                        // G-6 — was a bare `continue`. A row that terminates early was
                        // dropped with no log line at all, so the formula simply did not
                        // exist and nothing said why. That is the same invisible-failure
                        // class as G-5, one layer earlier: G-5 makes a formula that CANNOT
                        // BE EVALUATED visible; this makes a formula that was never LOADED
                        // visible.
                        droppedShort++;
                        droppedNames.Add(cols.Length > 1 && !string.IsNullOrWhiteSpace(cols[1])
                            ? $"{cols[1].Trim()} ({cols.Length} cols)"
                            : $"<unnamed> ({cols.Length} cols)");
                        continue;
                    }

                    var formula = new FormulaDefinition
                    {
                        Discipline = cols[0].Trim(),
                        ParameterName = cols[1].Trim(),
                        DataType = cols[2].Trim(),
                        Expression = cols[3].Trim(),
                        Description = cols[4].Trim(),
                        InputParameters = cols[5].Trim()
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).ToArray(),
                        Unit = cols[6].Trim(),
                    };

                    // Dependency_Level (col 9) is only a HINT. The authoritative
                    // ordering is COMPUTED below by topological sort over each
                    // formula's real token dependencies, so a shifted/corrupted
                    // column (unescaped commas push a GUID into this slot on some
                    // rows) cannot break ordering — and we never warn on it.
                    int.TryParse(cols[9].Trim(), out int depLevel);
                    formula.DependencyLevel = depLevel; // provisional — overwritten after topo sort

                    // Parse uses builtin geometry (column 10)
                    formula.UsesBuiltinGeometry = cols.Length > 10 &&
                        cols[10].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);

                    // Parse builtin inputs (column 11)
                    formula.BuiltinInputs = cols.Length > 11
                        ? cols[11].Trim()
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).ToArray()
                        : Array.Empty<string>();

                    if (!string.IsNullOrEmpty(formula.ParameterName)
                        && !string.IsNullOrEmpty(formula.Expression))
                    {
                        // Warn on corrupt discipline values (unescaped CSV commas)
                        if (formula.Discipline.Contains("(") || formula.Discipline.Contains("="))
                            StingLog.Warn($"Formula '{formula.ParameterName}': suspect Discipline value '{formula.Discipline}' — check CSV quoting");
                        formulas.Add(formula);
                    }
                }

                // G-6 — report the drop. A formula that never loaded is
                // indistinguishable, from the model, from one that loaded and did
                // nothing; naming them is the only way a user finds out the CSV is
                // truncated rather than the feature being broken.
                if (droppedShort > 0)
                {
                    StingLog.Warn($"Formula load: DROPPED {droppedShort} row(s) with fewer than 10 columns — "
                                + "these formulas do not exist at runtime. The CSV row is truncated; "
                                + "repair it to all 12 columns. Names: "
                                + string.Join(", ", droppedNames.Take(40))
                                + (droppedNames.Count > 40 ? $", …(+{droppedNames.Count - 40} more)" : ""));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to load formulas: {ex.Message}", ex);
            }

            // Dedupe duplicate parameter definitions. A second row for the same
            // ParameterName (e.g. a stray alternate expression that references a
            // parameter which in turn references it back) would otherwise inflate
            // formulas.Count and surface as a FALSE "cycle" in Kahn's sort below
            // (the name can only be emitted once). Keep the first occurrence and
            // warn once. The corporate CSV is curated so the kept definition is
            // the single source of truth (e.g. the geometric CST_S_CON_VOLUME_CU_M).
            {
                var seenName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var deduped = new List<FormulaDefinition>(formulas.Count);
                foreach (var f in formulas)
                {
                    if (seenName.Add(f.ParameterName)) deduped.Add(f);
                    else StingLog.Warn($"Formula '{f.ParameterName}': duplicate definition ignored (kept first; expression='{f.Expression}').");
                }
                formulas = deduped;
            }

            // Sort by the (provisional) dependency level as a stable starting
            // order; the real ordering + levels are computed by the topo sort.
            formulas.Sort((a, b) => a.DependencyLevel.CompareTo(b.DependencyLevel));

            // Cycle detection + level computation via topological sort (Kahn's algorithm)
            var formulaByName = formulas.GroupBy(f => f.ParameterName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var adjList = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in formulas)
            {
                if (!adjList.ContainsKey(f.ParameterName)) adjList[f.ParameterName] = new List<string>();
                if (!inDegree.ContainsKey(f.ParameterName)) inDegree[f.ParameterName] = 0;
            }

            // PERF-002: Detect dependencies by extracting word tokens from each expression
            // and checking against the set of formula parameter names — O(n) total instead of O(n²).
            // Previous approach ran a regex per (formula × formula) pair = 199² = ~40K regex evaluations.
            var formulaNameSet = new HashSet<string>(
                formulas.Select(f => f.ParameterName), StringComparer.OrdinalIgnoreCase);
            var wordTokenRegex = new System.Text.RegularExpressions.Regex(
                @"\b[A-Za-z_][A-Za-z0-9_]*\b", System.Text.RegularExpressions.RegexOptions.Compiled);

            foreach (var f in formulas)
            {
                if (string.IsNullOrEmpty(f.Expression)) continue;
                var matches = wordTokenRegex.Matches(f.Expression);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    string token = m.Value;
                    if (string.Equals(token, f.ParameterName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!formulaNameSet.Contains(token)) continue;
                    if (!seen.Add(token)) continue; // deduplicate within same expression

                    // token is a dependency of f (f depends on token)
                    if (!adjList.ContainsKey(token)) adjList[token] = new List<string>();
                    adjList[token].Add(f.ParameterName);
                    inDegree[f.ParameterName] = inDegree.GetValueOrDefault(f.ParameterName) + 1;
                }
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var sorted = new List<FormulaDefinition>();
            // Computed dependency level = longest path from a root (a node with
            // no formula-typed inputs). Roots start at 0; every dependent is at
            // least one deeper than its deepest dependency.
            var levelOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in inDegree) if (kv.Value == 0) levelOf[kv.Key] = 0;
            while (queue.Count > 0)
            {
                string name = queue.Dequeue();
                int myLevel = levelOf.TryGetValue(name, out var lv) ? lv : 0;
                if (formulaByName.TryGetValue(name, out var fd))
                {
                    fd.DependencyLevel = myLevel;   // overwrite the CSV hint with the computed level
                    sorted.Add(fd);
                }
                if (adjList.TryGetValue(name, out var neighbors))
                {
                    foreach (var n in neighbors)
                    {
                        int cand = myLevel + 1;
                        if (!levelOf.TryGetValue(n, out var ex) || cand > ex) levelOf[n] = cand;
                        inDegree[n]--;
                        if (inDegree[n] == 0) queue.Enqueue(n);
                    }
                }
            }

            if (sorted.Count < formulas.Count)
            {
                // LOGIC-001: Use HashSet for O(1) membership test instead of O(n) sorted.Any(...).
                // For 199 formulas this is a 199x speedup for each cycle-node check.
                var sortedNames = new HashSet<string>(
                    sorted.Select(s => s.ParameterName), StringComparer.OrdinalIgnoreCase);
                var cycleNodes = formulas.Where(f => !sortedNames.Contains(f.ParameterName)).ToList();
                foreach (var cn in cycleNodes)
                {
                    string inputs = cn.InputParameters != null && cn.InputParameters.Length > 0
                        ? string.Join(", ", cn.InputParameters) : "(none)";
                    StingLog.Error($"Formula cycle detected: {cn.ParameterName} (level {cn.DependencyLevel}, depends on: {inputs}, expression: {cn.Expression})");
                }
                // Add cycle nodes at the end — they execute with potentially stale/wrong input values
                sorted.AddRange(cycleNodes);
                StingLog.Warn($"Formula cycle: {cycleNodes.Count} formula(s) in dependency cycle — results may be inaccurate: {string.Join(", ", cycleNodes.Select(c => c.ParameterName))}");

            }
            formulas = sorted;

            // LOG-02: Cache parsed formulas for subsequent calls
            lock (_formulaCacheLock)
            {
                _cachedFormulas = formulas;
                _cachedCsvPath = csvPath;
                try { _cachedCsvWriteTime = File.GetLastWriteTimeUtc(csvPath); }
                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); _cachedCsvWriteTime = DateTime.MinValue; }
            }

            return formulas;
        }

        /// <summary>
        /// ENH-P3: Validate loaded formulas against ParamRegistry.
        /// Logs warnings for orphaned formulas (no matching GUID in registry) and
        /// formulas referencing input parameters not in the registry.
        /// </summary>
        public static void ValidateAgainstRegistry(List<FormulaDefinition> formulas)
        {
            if (formulas == null || formulas.Count == 0) return;
            int orphaned = 0;
            int missingInputs = 0;

            var knownParams = ParamRegistry.AllParamGuids;

            foreach (var f in formulas)
            {
                // Check target parameter exists in registry
                if (!knownParams.ContainsKey(f.ParameterName))
                {
                    orphaned++;
                    if (orphaned <= 10) // Limit log spam
                        StingLog.Warn($"Formula orphan: '{f.ParameterName}' has no GUID in ParamRegistry — values will be lost");
                }

                // Check input parameters exist
                foreach (string input in f.InputParameters)
                {
                    if (!knownParams.ContainsKey(input) &&
                        !input.StartsWith("BIP_") && // Built-in parameter refs
                        !input.Equals("Element_Id", StringComparison.OrdinalIgnoreCase))
                    {
                        missingInputs++;
                    }
                }
            }

            if (orphaned > 0)
                StingLog.Warn($"FormulaEngine: {orphaned} of {formulas.Count} formulas have no matching GUID in ParamRegistry — computed values will be lost for these");
            if (missingInputs > 0)
                StingLog.Info($"FormulaEngine: {missingInputs} input parameter references not found in ParamRegistry (may use built-in or type parameters)");
        }

        /// <summary>
        /// Build evaluation context (parameter name → value) for an element.
        /// Returns null if required inputs are missing.
        /// </summary>
        public static Dictionary<string, object> BuildContext(
            Element el, FormulaDefinition formula)
        {
            var context = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            bool hasAnyInput = false;

            // Constant formulas (no inputs) should still evaluate
            if (formula.InputParameters == null || formula.InputParameters.Length == 0)
                return context;

            // Resolve shared/instance parameter values
            foreach (string inputName in formula.InputParameters)
            {
                if (string.IsNullOrEmpty(inputName)) continue;

                // Check for built-in geometry inputs
                if (IsBuiltinGeometry(inputName))
                {
                    double? geomVal = GetBuiltinGeometry(el, inputName);
                    if (geomVal.HasValue)
                    {
                        context[inputName] = geomVal.Value;
                        hasAnyInput = true;
                    }
                    continue;
                }

                // Try custom parameter
                Parameter param = ParameterHelpers.CachedLookup(el, inputName);
                if (param == null) continue;

                switch (param.StorageType)
                {
                    case StorageType.Double:
                        double dVal = param.AsDouble();
                        // Convert from Revit internal units (feet) to metric where needed
                        context[inputName] = dVal;
                        hasAnyInput = true;
                        break;
                    case StorageType.Integer:
                        context[inputName] = (double)param.AsInteger();
                        hasAnyInput = true;
                        break;
                    case StorageType.String:
                        string sVal = param.AsString() ?? "";
                        // Always add string params to context (even empty strings)
                        // so conditional formulas like if(PARAM<>"", ...) evaluate correctly
                        context[inputName] = sVal;
                        hasAnyInput = true;
                        // Also try parsing as number for dual-type params
                        if (!string.IsNullOrEmpty(sVal)
                            && double.TryParse(sVal, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out double parsed))
                            context[inputName + "_NUM"] = parsed;
                        break;
                }
            }

            return hasAnyInput ? context : null;
        }

        /// <summary>Check if a parameter name is a built-in Revit geometry property.</summary>
        private static bool IsBuiltinGeometry(string name)
        {
            return name == "Width" || name == "Height" || name == "Length"
                || name == "Diameter" || name == "Thickness"
                || name == "Tile_Width" || name == "Tile_Height";
        }

        /// <summary>Get built-in geometry value from element (in mm for dimensional params).</summary>
        private static double? GetBuiltinGeometry(Element el, string name)
        {
            // Convert from feet to mm (Revit internal unit)
            const double ftToMm = 304.8;

            try
            {
                Parameter p = null;
                switch (name)
                {
                    case "Width":
                        p = el.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM)
                            ?? ParameterHelpers.CachedLookup(el, "Width");
                        break;
                    case "Height":
                        p = el.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM)
                            ?? ParameterHelpers.CachedLookup(el, "Height");
                        break;
                    case "Length":
                        p = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)
                            ?? ParameterHelpers.CachedLookup(el, "Length");
                        break;
                    case "Diameter":
                        p = ParameterHelpers.CachedLookup(el, "Diameter")
                            ?? ParameterHelpers.CachedLookup(el, "Overall Size");
                        break;
                    case "Thickness":
                        p = ParameterHelpers.CachedLookup(el, "Thickness");
                        break;
                    default:
                        p = ParameterHelpers.CachedLookup(el, name);
                        break;
                }

                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble() * ftToMm;
            }
            catch (Exception ex) { StingLog.Warn($"Read dimension '{name}' from element: {ex.Message}"); }

            return null;
        }

        /// <summary>
        /// Evaluate a TEXT formula (string concatenation).
        /// Format: ASS_ID_TXT + "-" + ASS_TAG_1_TXT
        /// </summary>
        public static string EvaluateText(string expression, Dictionary<string, object> context)
        {
            // G-3 — route through the real recursive evaluator.
            //
            // The legacy path below splits on top-level '+' and emits quoted literals,
            // format(PARAM) and context values, silently dropping anything else — which
            // meant every if() was discarded. 65 of the 112 TEXT formulas begin with
            // if(, 36 of them nested, so the entire conditional-narrative and WARN_*
            // threshold surface produced nothing.
            //
            // TextExpressionParser handles the real grammar and delegates the CONDITION
            // to EvaluateNumeric, so comparisons, arithmetic and lookup() reuse the path
            // that is already exercised rather than being reimplemented here.
            try
            {
                var tp = new TextExpressionParser(expression, context);
                string parsed = tp.Parse();
                if (tp.Failed) return null;   // G-5 semantics: a failure is absent, not blank
                return string.IsNullOrEmpty(parsed) ? null : parsed;
            }
            catch (Exception ex) { StingLog.Warn($"EvaluateText: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Legacy concatenation-only text evaluator. Retained for reference; superseded by
        /// <see cref="TextExpressionParser"/> (G-3). Not called.
        /// </summary>
        private static string EvaluateTextLegacy(string expression, Dictionary<string, object> context)
        {
            try
            {
                // Split on + for concatenation, handling quoted strings
                var parts = TokenizeTextExpression(expression);
                var sb = new StringBuilder();

                foreach (string part in parts)
                {
                    string trimmed = part.Trim();
                    if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                    {
                        // Quoted literal string
                        sb.Append(trimmed.Substring(1, trimmed.Length - 2));
                    }
                    else if (trimmed.StartsWith("format(", StringComparison.OrdinalIgnoreCase)
                             && trimmed.EndsWith(")"))
                    {
                        // format(PARAM) — thousands separators; N0 for whole values,
                        // N2 for fractional. Used by the <source>_DISP_TXT mirror formulas
                        // so numeric values read nicely in tag labels.
                        string inner = trimmed.Substring(7, trimmed.Length - 8).Trim();
                        if (context.TryGetValue(inner, out object fval))
                            sb.Append(FormatNumberForDisplay(fval));
                    }
                    else if (context.TryGetValue(trimmed, out object val))
                    {
                        sb.Append(val?.ToString() ?? "");
                    }
                    // else skip unknown references
                }

                string result = sb.ToString();
                return string.IsNullOrEmpty(result) ? null : result;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Renders a numeric value for display in a tag: thousands separators, no
        /// decimals when whole (250000 → "250,000"), two decimals when fractional
        /// (1234.5 → "1,234.50"). Accepts a double in the context, or a numeric
        /// string (dual-type params); falls back to the raw ToString otherwise.
        /// </summary>
        private static string FormatNumberForDisplay(object value)
        {
            double d;
            if (value is double dd) d = dd;
            else if (value is int ii) d = ii;
            else if (!double.TryParse(value?.ToString() ?? "", NumberStyles.Any,
                         CultureInfo.InvariantCulture, out d))
                return value?.ToString() ?? "";

            bool whole = Math.Abs(d - Math.Round(d)) < 1e-9;
            return d.ToString(whole ? "N0" : "N2", CultureInfo.InvariantCulture);
        }

        /// <summary>Split text expression on + operator, respecting quoted strings.</summary>
        private static List<string> TokenizeTextExpression(string expr)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < expr.Length; i++)
            {
                char c = expr[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    current.Append(c);
                }
                else if (c == '+' && !inQuote)
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString().Trim());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.Length > 0)
                parts.Add(current.ToString().Trim());

            return parts;
        }

        /// <summary>
        /// G-5 — restore the per-batch allowance of formula-failure warnings.
        /// Call at every evaluation batch boundary. Without it the budget is a
        /// session-lifetime counter: one model full of broken formulas exhausts
        /// it and every subsequent run in that Revit session logs nothing, which
        /// is the same invisible-failure problem the G-5 work set out to remove.
        /// </summary>
        public static void ResetWarnBudget()
        {
            ExpressionParser.ResetWarnBudget();
            TextExpressionParser.ResetWarnBudget();   // G-3 — the TEXT path has its own budget
        }

        /// <summary>
        /// Evaluate a numeric formula using recursive descent parsing.
        /// Supports: +, -, *, /, ^, (), if(), log(), comparison operators.
        /// </summary>
        public static double? EvaluateNumeric(string expression, Dictionary<string, object> context)
        {
            try
            {
                var parser = new ExpressionParser(expression, context);
                double result = parser.Parse();

                // G-5: the parser hit a path that cannot produce a real number
                // (division by zero, undefined power, unknown identifier, or an
                // unresolved function such as lookup()). Returning null makes the
                // caller SKIP the write; returning 0 would stamp a false quantity
                // into the model and read as a real, priced figure downstream.
                if (parser.Failed)
                    return null;

                // Guard against NaN/Infinity from Math.Pow (e.g., 0^-1, (-1)^0.5)
                // or pathological division chains that produce Infinity
                if (double.IsNaN(result) || double.IsInfinity(result))
                    return null;
                return result;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// DATA-03: Convert a value from a named display unit to Revit internal units (feet/ft2/ft3).
        ///
        /// <para><b>G-4 — DEAD CODE. Do not reintroduce a call to this.</b> Retained for one
        /// release so the removal is reviewable in place, then delete.</para>
        ///
        /// <para>The single call site (the formula writer) was removed because the
        /// conversion cannot be correct anywhere. <c>MR_PARAMETERS.txt</c> declares no
        /// LENGTH, AREA or VOLUME parameters — TEXT 2,819 / YESNO 265 / NUMBER 221 /
        /// INTEGER 93 — so no target exists for which Revit internal units are the right
        /// storage. Every <c>_MM</c> / <c>_SQ_M</c> / <c>_CU_M</c> parameter is TEXT holding
        /// metric, and no reader converts back.</para>
        ///
        /// <para>It was also silently selective: the switch below has <c>M2</c>,
        /// <c>SQ_M</c>, <c>SQUARE_METERS</c> but no <c>m²</c>, so two parameters computed
        /// from byte-identical expressions diverged by 10.7639× on the strength of which
        /// glyph the author typed in the Unit column.</para>
        /// </summary>
        [Obsolete("G-4: formula results are stored metric; there are no unit-typed targets. " +
                  "Do not call. Retained one release for reviewability, then delete.", error: false)]
        public static double ConvertToInternalUnits(double value, string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return value;

            switch (unit.Trim().ToUpperInvariant())
            {
                // Length → feet (exact: 1 ft = 0.3048 m)
                case "M":
                case "METERS":
                case "METRES":
                    return value / 0.3048;
                case "MM":
                case "MILLIMETERS":
                case "MILLIMETRES":
                    return value / 304.8;
                case "CM":
                case "CENTIMETERS":
                case "CENTIMETRES":
                    return value / 30.48;
                case "IN":
                case "INCHES":
                    return value / 12.0;

                // Area → ft² (exact: 1 ft² = 0.3048² m² = 0.09290304 m²)
                case "M2":
                case "SQ_M":
                case "SQUARE_METERS":
                    return value / (0.3048 * 0.3048);
                case "MM2":
                case "SQ_MM":
                case "SQUARE_MILLIMETERS":
                    return value / (304.8 * 304.8);

                // Volume → ft³ (exact: 1 ft³ = 0.3048³ m³)
                case "M3":
                case "CU_M":
                case "CUBIC_METERS":
                    return value / (0.3048 * 0.3048 * 0.3048);
                case "L":
                case "LITERS":
                case "LITRES":
                    return value / (0.3048 * 0.3048 * 0.3048 * 1000.0);

                // Temperature → Rankine (Revit internal for some parameters)
                case "C":
                case "CELSIUS":
                    return (value * 9.0 / 5.0) + 491.67;

                // Mass → (Revit uses kg internally for mass params)
                case "KG":
                case "KILOGRAMS":
                    return value; // no conversion needed
                case "LB":
                case "POUNDS":
                    return value * 0.453592;

                // Pressure → Pa (Revit internal)
                case "KPA":
                case "KILOPASCALS":
                    return value * 1000.0;
                case "PA":
                case "PASCALS":
                    return value;
                case "PSI":
                    return value * 6894.76;

                // Flow → ft3/s (Revit internal)
                case "L/S":
                case "LPS":
                    return value * 0.0353147;
                case "CFM":
                    return value / 60.0;

                // Already in internal units or dimensionless
                case "FT":
                case "FEET":
                case "FT2":
                case "FT3":
                case "":
                case "NONE":
                case "RATIO":
                case "PERCENT":
                case "%":
                    return value;

                default:
                    StingLog.Warn($"ConvertToInternalUnits: unknown unit '{unit}' — passing value through unchanged");
                    return value;
            }
        }

        /// <summary>Write numeric result to parameter, handling type conversion.
        /// Value should already be in Revit internal units (use ConvertToInternalUnits first).
        /// When overwrite is false (default), only writes if current value is empty/zero.</summary>
        public static bool WriteNumericResult(Parameter param, double value, bool overwrite = false)
        {
            try
            {
                // Guard against NaN/Infinity corrupting Revit parameters
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return false;

                if (param.StorageType == StorageType.Double)
                {
                    if (overwrite || Math.Abs(param.AsDouble()) < 0.0001)
                    {
                        param.Set(value);
                        return true;
                    }
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    if (overwrite || param.AsInteger() == 0)
                    {
                        param.Set((int)Math.Round(value));
                        return true;
                    }
                }
                else if (param.StorageType == StorageType.String)
                {
                    string current = param.AsString();
                    if (overwrite || string.IsNullOrEmpty(current))
                    {
                        param.Set(value.ToString("G6", CultureInfo.InvariantCulture));
                        return true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Write formula result to parameter: {ex.Message}"); }

            return false;
        }

        /// <summary>
        /// Recursive descent expression parser for Revit-style formulas.
        /// Grammar:
        ///   expr       = comparison
        ///   comparison = addition (comp_op addition)?
        ///   addition   = multiply (('+' | '-') multiply)*
        ///   multiply   = power (('*' | '/') power)*
        ///   power      = unary ('^' unary)?
        ///   unary      = '-' primary | primary
        ///   primary    = NUMBER | IDENTIFIER | '(' expr ')' | function_call
        ///   function   = 'if' '(' expr ',' expr ',' expr ')' | 'log' '(' expr ')'
        /// </summary>
        /// <summary>
        /// G-3 — string-valued recursive-descent evaluator for TEXT formulas.
        ///
        ///   textExpr := textTerm ('+' textTerm)*
        ///   textTerm := '"' literal '"' | '(' textExpr ')' | if(...) | format(P) | IDENT
        ///   if       := 'if' '(' &lt;condition&gt; ',' textExpr ',' textExpr ')'
        ///
        /// The CONDITION is handed to <see cref="EvaluateNumeric"/> rather than
        /// re-implemented, so comparisons, arithmetic, if()/log() nesting and lookup()
        /// all reuse the numeric path that is already exercised. Measured over the
        /// shipped data, every TEXT condition is either a bare boolean parameter (36)
        /// or a &lt;/&gt; comparison (29) — no string equality — so the delegation covers
        /// the whole surface.
        ///
        /// Two behaviours carried over from the numeric parser deliberately:
        ///
        /// 1. **G-5 failure semantics.** An unresolvable branch or condition FAILS the
        ///    formula (null) rather than yielding "". A WARN_* threshold that cannot be
        ///    computed must be absent, not silently blank — a blank warning reads as
        ///    "no warning", which is the wrong answer in the safe direction.
        /// 2. **Branch laziness.** Both branches are parsed to advance the cursor, but
        ///    only the branch actually returned may fail the formula.
        /// </summary>
        private class TextExpressionParser
        {
            private readonly string _expr;
            private readonly Dictionary<string, object> _ctx;
            private int _pos;
            private string _failure;

            public bool Failed => _failure != null;
            public string FailureReason => _failure;

            private static int _warnBudget = ExpressionParser.WarnBudgetPerBatch;
            internal static void ResetWarnBudget()
                => System.Threading.Interlocked.Exchange(ref _warnBudget, ExpressionParser.WarnBudgetPerBatch);

            public TextExpressionParser(string expr, Dictionary<string, object> ctx)
            { _expr = expr ?? ""; _ctx = ctx ?? new Dictionary<string, object>(); }

            private void Fail(string reason)
            {
                if (_failure != null) return;
                _failure = reason;
                int remaining = System.Threading.Interlocked.Decrement(ref _warnBudget);
                if (remaining >= 0)
                {
                    string shown = _expr.Length > 120 ? _expr.Substring(0, 120) + "…" : _expr;
                    StingLog.Warn($"TEXT formula not evaluated ({reason}) in: {shown}");
                    if (remaining == 0)
                        StingLog.Warn("Further TEXT formula warnings suppressed for this batch.");
                }
            }

            public string Parse() => ParseTextExpr();

            private void SkipWs()
            { while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos])) _pos++; }

            private string ParseTextExpr()
            {
                var sb = new StringBuilder();
                sb.Append(ParseTextTerm());
                while (true)
                {
                    SkipWs();
                    if (_pos < _expr.Length && _expr[_pos] == '+') { _pos++; sb.Append(ParseTextTerm()); }
                    else break;
                }
                return sb.ToString();
            }

            private string ParseTextTerm()
            {
                SkipWs();
                if (_pos >= _expr.Length) return "";
                char c = _expr[_pos];

                if (c == '"') return ReadQuoted();

                if (c == '(')
                {
                    _pos++;
                    string inner = ParseTextExpr();
                    SkipWs();
                    if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                    return inner;
                }

                string ident = ReadIdent();
                if (ident.Length == 0) { _pos++; return ""; }   // stray punctuation

                SkipWs();
                bool isCall = _pos < _expr.Length && _expr[_pos] == '(';

                if (isCall && ident.Equals("if", StringComparison.OrdinalIgnoreCase))
                    return ParseIfText();
                if (isCall && ident.Equals("format", StringComparison.OrdinalIgnoreCase))
                    return ParseFormatCall();
                if (isCall)
                {
                    Fail($"unresolved function '{ident}()'");
                    SkipBalancedParens();
                    return "";
                }

                if (_ctx.TryGetValue(ident, out object val)) return val?.ToString() ?? "";

                Fail($"unknown identifier '{ident}'");
                return "";
            }

            private string ParseIfText()
            {
                _pos++;                                   // past '('
                string cond = ReadRawUntilTopLevelComma();
                if (_pos < _expr.Length && _expr[_pos] == ',') _pos++;

                // A STRING condition is resolved here; everything else is delegated to
                // the numeric parser. Measured over the shipped data, the 65 TEXT
                // formulas contain 265 conditions:
                //
                //   200  PARAM <> ""          not-empty test   <- the dominant shape
                //    36  bare boolean param
                //    27  numeric comparison
                //     2  param vs expression
                //
                // EvaluateNumeric cannot do the first: ParseComparison implements
                // <= >= < > and a single =, but NOT <>, and the operand is a TEXT
                // parameter that fails to parse as a number. Delegating everything
                // would therefore have failed 200 of 265 conditions — 42 of the 65
                // formulas — which is what the pre-commit simulation caught.
                double? c = TryStringCondition(cond) ?? EvaluateNumeric(cond, _ctx);

                string before = _failure;

                string trueVal = ParseTextExpr();
                string failTrue = _failure; _failure = before;

                SkipWs();
                if (_pos < _expr.Length && _expr[_pos] == ',') _pos++;

                string falseVal = ParseTextExpr();
                string failFalse = _failure; _failure = before;

                SkipWs();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;

                if (!c.HasValue)
                {
                    Fail($"if() condition could not be evaluated: {cond.Trim()}");
                    return "";
                }

                bool takeTrue = c.Value != 0;
                string taken = takeTrue ? failTrue : failFalse;
                if (taken != null) _failure = taken;      // only the TAKEN branch may fail us
                return takeTrue ? trueVal : falseVal;
            }

            /// <summary>
            /// Resolve <c>IDENT &lt;&gt; "literal"</c> / <c>IDENT = "literal"</c> against the
            /// context. Returns 1/0, or null when this is not a string condition (leave it
            /// to the numeric parser).
            ///
            /// An identifier ABSENT from the context is a failure — the same rule the
            /// numeric path applies — because it means the name is missing from the row's
            /// Input_Parameters and the test can never be meaningful. An identifier that
            /// is PRESENT but empty is not a failure: it is exactly what
            /// <c>&lt;&gt; ""</c> exists to detect, and must yield the false branch rather
            /// than skipping the formula.
            /// </summary>
            private double? TryStringCondition(string cond)
            {
                if (string.IsNullOrWhiteSpace(cond)) return null;
                var m = System.Text.RegularExpressions.Regex.Match(
                    cond.Trim(),
                    "^([A-Za-z_][A-Za-z0-9_]*)\\s*(<>|=)\\s*\"((?:[^\"]|\"\")*)\"$");
                if (!m.Success) return null;

                string name = m.Groups[1].Value;
                string op   = m.Groups[2].Value;
                string lit  = m.Groups[3].Value.Replace("\"\"", "\"");

                if (!_ctx.TryGetValue(name, out object raw))
                {
                    Fail($"unknown identifier '{name}' in condition");
                    return null;
                }

                string actual = raw?.ToString() ?? "";
                bool equal = string.Equals(actual, lit, StringComparison.OrdinalIgnoreCase);
                return (op == "=" ? equal : !equal) ? 1.0 : 0.0;
            }

            private string ParseFormatCall()
            {
                _pos++;                                   // past '('
                string inner = ReadRawUntilTopLevelComma().Trim();
                SkipWs();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                if (_ctx.TryGetValue(inner, out object v)) return FormatNumberForDisplay(v);
                Fail($"format() on unknown identifier '{inner}'");
                return "";
            }

            /// <summary>Raw substring up to the next top-level ',' or the closing ')'.</summary>
            private string ReadRawUntilTopLevelComma()
            {
                int start = _pos, depth = 0; bool q = false;
                while (_pos < _expr.Length)
                {
                    char c = _expr[_pos];
                    if (q) { if (c == '"') q = false; }
                    else if (c == '"') q = true;
                    else if (c == '(') depth++;
                    else if (c == ')') { if (depth == 0) break; depth--; }
                    else if (c == ',' && depth == 0) break;
                    _pos++;
                }
                return _expr.Substring(start, _pos - start);
            }

            private string ReadQuoted()
            {
                _pos++;                                   // opening quote
                var sb = new StringBuilder();
                while (_pos < _expr.Length)
                {
                    char c = _expr[_pos];
                    if (c == '"')
                    {
                        // "" inside a literal is an escaped quote
                        if (_pos + 1 < _expr.Length && _expr[_pos + 1] == '"') { sb.Append('"'); _pos += 2; continue; }
                        _pos++; break;
                    }
                    sb.Append(c); _pos++;
                }
                return sb.ToString();
            }

            private string ReadIdent()
            {
                SkipWs();
                int start = _pos;
                while (_pos < _expr.Length &&
                       (char.IsLetterOrDigit(_expr[_pos]) || _expr[_pos] == '_')) _pos++;
                return _pos > start ? _expr.Substring(start, _pos - start) : "";
            }

            private void SkipBalancedParens()
            {
                if (_pos >= _expr.Length || _expr[_pos] != '(') return;
                int depth = 0; bool q = false;
                while (_pos < _expr.Length)
                {
                    char c = _expr[_pos];
                    if (q) { if (c == '"') q = false; }
                    else if (c == '"') q = true;
                    else if (c == '(') depth++;
                    else if (c == ')') { depth--; if (depth == 0) { _pos++; return; } }
                    _pos++;
                }
            }
        }

        private class ExpressionParser
        {
            private readonly string _expr;
            private readonly Dictionary<string, object> _ctx;
            private int _pos;

            // G-5: a formula that cannot be evaluated must NOT resolve to zero.
            // Every path that used to substitute 0 for a failure now records the
            // reason here; EvaluateNumeric turns a non-null reason into a null
            // result so WriteNumericResult skips the element instead of stamping
            // a false quantity into the model. First failure wins — it is the
            // root cause; later ones are usually its knock-on effects.
            private string _failure;

            /// <summary>Non-null when evaluation hit a path that cannot produce a real number.</summary>
            public bool Failed => _failure != null;

            /// <summary>Reason for the first failure, or null.</summary>
            public string FailureReason => _failure;

            /// <summary>Warn allowance for ONE evaluation batch — see <see cref="_warnBudget"/>.</summary>
            internal const int WarnBudgetPerBatch = 200;

            // Failures fire per element per formula, so an unguarded Warn floods
            // StingTools.log on a batch run — hence the budget. It is reset at each
            // batch boundary (FormulaEngine.ResetWarnBudget, called from
            // PostTagCleanup and FormulaEvaluatorCommand.Execute) rather than being
            // a once-per-session allowance: as a session-lifetime counter, one messy
            // model could exhaust it and every later run — including the one someone
            // is actually watching the log for — would be silent.
            private static int _warnBudget = WarnBudgetPerBatch;

            internal static void ResetWarnBudget()
                => System.Threading.Interlocked.Exchange(ref _warnBudget, WarnBudgetPerBatch);

            private void Fail(string reason)
            {
                if (_failure != null) return;   // keep the first (root-cause) failure
                _failure = reason;

                int remaining = System.Threading.Interlocked.Decrement(ref _warnBudget);
                if (remaining >= 0)
                {
                    string shown = _expr != null && _expr.Length > 120
                        ? _expr.Substring(0, 120) + "…"
                        : _expr;
                    StingLog.Warn($"Formula not evaluated ({reason}) in: {shown}");
                    if (remaining == 0)
                        StingLog.Warn("Further formula-evaluation warnings suppressed for this session.");
                }
            }

            public ExpressionParser(string expr, Dictionary<string, object> ctx)
            {
                _expr = expr;
                _ctx = ctx;
                _pos = 0;
            }

            public double Parse()
            {
                double result = ParseComparison();
                return result;
            }

            private void SkipWhitespace()
            {
                while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos]))
                    _pos++;
            }

            private char Peek()
            {
                SkipWhitespace();
                return _pos < _expr.Length ? _expr[_pos] : '\0';
            }

            private bool Match(string s)
            {
                SkipWhitespace();
                if (_pos + s.Length <= _expr.Length &&
                    _expr.Substring(_pos, s.Length) == s)
                {
                    _pos += s.Length;
                    return true;
                }
                return false;
            }

            private double ParseComparison()
            {
                double left = ParseAddition();
                SkipWhitespace();

                if (Match("<=")) return left <= ParseAddition() ? 1 : 0;
                if (Match(">=")) return left >= ParseAddition() ? 1 : 0;
                if (Match("<")) return left < ParseAddition() ? 1 : 0;
                if (Match(">")) return left > ParseAddition() ? 1 : 0;
                if (_pos < _expr.Length && _expr[_pos] == '=' &&
                    (_pos + 1 >= _expr.Length || _expr[_pos + 1] != '='))
                {
                    // Single = is equality in Revit formulas (not ==)
                    _pos++;
                    return left == ParseAddition() ? 1 : 0;
                }

                return left;
            }

            private double ParseAddition()
            {
                double result = ParseMultiply();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == '+')
                    {
                        _pos++;
                        result += ParseMultiply();
                    }
                    else if (_pos < _expr.Length && _expr[_pos] == '-')
                    {
                        _pos++;
                        result -= ParseMultiply();
                    }
                    else break;
                }
                return result;
            }

            private double ParseMultiply()
            {
                double result = ParsePower();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == '*')
                    {
                        _pos++;
                        result *= ParsePower();
                    }
                    else if (_pos < _expr.Length && _expr[_pos] == '/')
                    {
                        _pos++;
                        double divisor = ParsePower();
                        if (divisor == 0)
                        {
                            // G-5: was `result = 0`. A division by zero means an input
                            // was missing or zero-valued; zero is not the answer.
                            Fail("division by zero");
                            result = 0;
                        }
                        else result /= divisor;
                    }
                    else break;
                }
                return result;
            }

            private double ParsePower()
            {
                double result = ParseUnary();
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '^')
                {
                    _pos++;
                    double exp = ParseUnary();
                    double powered = Math.Pow(result, exp);
                    // Guard: Math.Pow(0,-1)=Infinity, Math.Pow(-1,0.5)=NaN
                    // G-5: was `result = 0`, which hid the undefined result from
                    // EvaluateNumeric's own NaN/Infinity check further up.
                    if (double.IsNaN(powered) || double.IsInfinity(powered))
                    {
                        Fail($"undefined power ({result}^{exp})");
                        result = 0;
                    }
                    else result = powered;
                }
                return result;
            }

            private double ParseUnary()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '-')
                {
                    _pos++;
                    return -ParsePrimary();
                }
                return ParsePrimary();
            }

            private double ParsePrimary()
            {
                SkipWhitespace();

                // Parenthesized expression
                if (_pos < _expr.Length && _expr[_pos] == '(')
                {
                    _pos++;
                    double result = ParseComparison();
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == ')')
                        _pos++;
                    return result;
                }

                // Number literal
                if (_pos < _expr.Length && (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.'))
                {
                    return ParseNumber();
                }

                // String literal (skip in numeric context)
                if (_pos < _expr.Length && _expr[_pos] == '"')
                {
                    SkipString();
                    return 0;
                }

                // Identifier or function
                string ident = ParseIdentifier();
                if (string.IsNullOrEmpty(ident)) return 0;

                // Functions
                if (ident.Equals("if", StringComparison.OrdinalIgnoreCase))
                    return ParseIf();
                if (ident.Equals("log", StringComparison.OrdinalIgnoreCase))
                    return ParseLog();
                if (ident.Equals("lookup", StringComparison.OrdinalIgnoreCase))
                    return ParseLookup();

                // G-5: an identifier immediately followed by '(' is a function call.
                // Only if() and log() are implemented, so anything else — lookup()
                // above all — used to evaluate to 0 AND abandon the rest of the
                // expression, because the unconsumed argument list stops the parse.
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(')
                {
                    Fail($"unresolved function '{ident}()'");
                    SkipBalancedParens();   // leave the cursor somewhere sane
                    return 0;
                }

                // Variable lookup
                if (_ctx.TryGetValue(ident, out object val))
                {
                    if (val is double d) return d;
                    if (val is string s && double.TryParse(s, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out double parsed))
                        return parsed;

                    // Present but not a number — a TEXT parameter used in arithmetic.
                    Fail($"non-numeric value for '{ident}'");
                    return 0;
                }

                // G-5: was `return 0` — an unresolved input is not a zero input.
                Fail($"unknown identifier '{ident}'");
                return 0;
            }

            private double ParseNumber()
            {
                int start = _pos;
                while (_pos < _expr.Length &&
                    (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.'))
                    _pos++;

                string numStr = _expr.Substring(start, _pos - start);
                return double.TryParse(numStr, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double result) ? result : 0;
            }

            private string ParseIdentifier()
            {
                SkipWhitespace();
                int start = _pos;
                while (_pos < _expr.Length &&
                    (char.IsLetterOrDigit(_expr[_pos]) || _expr[_pos] == '_'))
                    _pos++;
                return _pos > start ? _expr.Substring(start, _pos - start) : "";
            }

            /// <summary>
            /// Consume a parenthesised argument list from the opening '(' to its match,
            /// so an unresolved function call does not strand the cursor mid-expression.
            /// </summary>
            private void SkipBalancedParens()
            {
                if (_pos >= _expr.Length || _expr[_pos] != '(') return;
                int depth = 0;
                while (_pos < _expr.Length)
                {
                    char c = _expr[_pos];
                    if (c == '"') { SkipString(); continue; }
                    if (c == '(') depth++;
                    else if (c == ')')
                    {
                        depth--;
                        if (depth == 0) { _pos++; return; }
                    }
                    _pos++;
                }
            }

            private void SkipString()
            {
                _pos++; // skip opening quote
                while (_pos < _expr.Length && _expr[_pos] != '"')
                    _pos++;
                if (_pos < _expr.Length) _pos++; // skip closing quote
            }

            private double ParseIf()
            {
                // if(condition, trueValue, falseValue)
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;

                // Parse condition — may include string comparison
                double condition = ParseIfCondition();

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ',') _pos++;

                // G-5: both branches are parsed (the cursor has to cross them), but
                // if() is logically lazy — only the branch actually returned may
                // fail the formula. Without this, a divide-by-zero in the discarded
                // branch would void a result the model is entitled to.
                string failureBefore = _failure;

                double trueVal = ParseComparison();
                string failureTrue = _failure;
                _failure = failureBefore;

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ',') _pos++;

                double falseVal = ParseComparison();
                string failureFalse = _failure;
                _failure = failureBefore;

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;

                bool takeTrue = condition != 0;
                string takenFailure = takeTrue ? failureTrue : failureFalse;
                if (takenFailure != null) _failure = takenFailure;

                return takeTrue ? trueVal : falseVal;
            }

            private double ParseIfCondition()
            {
                SkipWhitespace();

                // Check for string comparison: PARAM = "value"
                int savedPos = _pos;
                string ident = ParseIdentifier();
                SkipWhitespace();

                if (_pos < _expr.Length && _expr[_pos] == '=')
                {
                    _pos++;
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == '"')
                    {
                        // String comparison
                        _pos++;
                        int strStart = _pos;
                        while (_pos < _expr.Length && _expr[_pos] != '"')
                            _pos++;
                        string compareValue = _expr.Substring(strStart, _pos - strStart);
                        if (_pos < _expr.Length) _pos++; // skip closing quote

                        if (_ctx.TryGetValue(ident, out object val))
                        {
                            string strVal = val?.ToString() ?? "";
                            return strVal.Equals(compareValue,
                                StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        }
                        return 0;
                    }
                }

                // Not a string comparison — restore position and parse as numeric
                _pos = savedPos;
                return ParseComparison();
            }

            /// <summary>
            /// G-13 — lookup(TABLE, KEY, COLUMN) against MATERIAL_LOOKUP.csv.
            ///
            /// TABLE and COLUMN are bare literals (CONCRETE, CEMENT_BAGS_PER_M3).
            /// KEY is usually the NAME OF A PARAMETER whose *value* is the row key
            /// ("C25"), but may also be a literal TypeKey (PRIMER) — so it is
            /// resolved through the context first and used verbatim if absent.
            ///
            /// 27 formulas / 29 calls use this. Until now none of them worked:
            /// 'lookup' fell through to the variable branch, evaluated to 0, and
            /// left the unconsumed argument list to terminate the parse — so the
            /// REST of the expression was discarded too. Every one of those wrote
            /// a zero into a bill.
            /// </summary>
            private double ParseLookup()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;

                string table  = ReadBareToken();
                SkipArgSeparator();
                string keyRef = ReadBareToken();
                SkipArgSeparator();
                string column = ReadBareToken();

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;

                if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column))
                {
                    Fail($"malformed lookup({table},{keyRef},{column})");
                    return 0;
                }

                // The key may be a parameter holding the TypeKey, or a literal.
                string key = keyRef;
                string rawKeyValue = null;
                bool keyWasEmpty = false;
                if (!string.IsNullOrEmpty(keyRef) && _ctx.TryGetValue(keyRef, out object kv))
                {
                    string resolved = kv as string ?? kv?.ToString();
                    rawKeyValue = resolved;
                    // An EMPTY parameter is not a key — fall through to DEFAULT
                    // rather than querying "CONCRETE " and silently missing.
                    if (!string.IsNullOrWhiteSpace(resolved)) key = resolved;
                    else { key = "DEFAULT"; keyWasEmpty = true; }
                }

                // TryGetProperty, not GetProperty: the latter returns 0 for both
                // "absent" and "present and zero", and MATERIAL_LOOKUP.csv holds
                // eight legitimate zeros in exactly these columns (unreinforced
                // blinding steel, nailed-tile fasteners, self-standing formwork
                // props). Treating those as a miss would fail a formula whose
                // correct answer is 0 — inverting the G-5 fix for those rows.
                // G-27 — record WHICH of the three ways this resolved, into the same
                // QuantityResolution structure the C# take-off uses. Until now a
                // DEFAULT was indistinguishable from a measurement on the page, which
                // is the mechanism behind G-15 and is universal: all 26 lookup()
                // calls read a table that ships a DEFAULT row.
                //
                // Note the ordering subtlety: an empty parameter is rewritten to the
                // literal key "DEFAULT" above, so it resolves through the SPECIFIC-row
                // branch below and would otherwise look measured. keyWasEmpty is what
                // distinguishes it.
                bool defaulted = keyWasEmpty
                                 || string.Equals(key, "DEFAULT", StringComparison.OrdinalIgnoreCase);

                if (MaterialLookupCsv.TryGetProperty($"{table} {key}", column, out double v))
                {
                    LookupAudit.Record(table, keyRef, rawKeyValue, column,
                        defaulted ? StingTools.BOQ.Takeoff.LookupState.Defaulted
                                  : StingTools.BOQ.Takeoff.LookupState.Measured);
                    return v;
                }

                // Fall back to the table's DEFAULT row, which the registry
                // indexes under the bare category name. Reaching here means the key
                // was SET but did not match — RC-1's "unmatched" case, the more
                // dangerous of the two because it is usually a typo.
                if (MaterialLookupCsv.TryGetProperty(table, column, out v))
                {
                    LookupAudit.Record(table, keyRef, rawKeyValue, column,
                        StingTools.BOQ.Takeoff.LookupState.Defaulted);
                    return v;
                }

                // G-5 composition: no value means the formula cannot be evaluated,
                // so it is skipped rather than written as 0.
                LookupAudit.Record(table, keyRef, rawKeyValue, column,
                    StingTools.BOQ.Takeoff.LookupState.Unresolved);
                Fail($"lookup({table},{key},{column}) found no value");
                return 0;
            }

            /// <summary>
            /// Read one bare lookup argument — an unquoted identifier, optionally
            /// quoted. Stops at ',' or ')'. Does not evaluate.
            /// </summary>
            private string ReadBareToken()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '"')
                {
                    _pos++;
                    int qs = _pos;
                    while (_pos < _expr.Length && _expr[_pos] != '"') _pos++;
                    string quoted = _expr.Substring(qs, _pos - qs).Trim();
                    if (_pos < _expr.Length) _pos++;   // closing quote
                    return quoted;
                }
                int start = _pos;
                while (_pos < _expr.Length && _expr[_pos] != ',' && _expr[_pos] != ')')
                    _pos++;
                return _expr.Substring(start, _pos - start).Trim();
            }

            private void SkipArgSeparator()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ',') _pos++;
            }

            private double ParseLog()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;
                double val = ParseComparison();
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return val > 0 ? Math.Log10(val) : 0;
            }
        }
    }
}
