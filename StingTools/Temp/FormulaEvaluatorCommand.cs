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

                    bool anyWritten = false;

                    foreach (var formula in formulas)
                    {
                        try
                        {
                            // Check if the element has the target parameter
                            Parameter targetParam = el.LookupParameter(formula.ParameterName);
                            if (targetParam == null || targetParam.IsReadOnly) continue;

                            // Collect input values
                            var context = FormulaEngine.BuildContext(el, formula);
                            if (context == null)
                            {
                                // Track formulas skipped due to missing inputs for audit
                                totalSkipped++;
                                string sKey = formula.ParameterName;
                                if (!formulaSkipCounts.ContainsKey(sKey))
                                    formulaSkipCounts[sKey] = 0;
                                formulaSkipCounts[sKey]++;
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
                                    if (string.IsNullOrEmpty(current))
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
                            if (!formulaErrorCounts.ContainsKey(fKey))
                            {
                                formulaErrorCounts[fKey] = 0;
                                formulaSampleFailures[fKey] = new List<ElementId>();
                            }
                            formulaErrorCounts[fKey]++;
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

        /// <summary>Load formula definitions from CSV file.</summary>
        public static List<FormulaDefinition> LoadFormulas(string csvPath)
        {
            var formulas = new List<FormulaDefinition>();

            try
            {
                var lines = File.ReadAllLines(csvPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                    .Skip(1); // skip header

                foreach (string line in lines)
                {
                    string[] cols = StingToolsApp.ParseCsvLine(line);
                    if (cols.Length < 10) continue;

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

                    // Parse dependency level (column 9)
                    int.TryParse(cols[9].Trim(), out int depLevel);
                    formula.DependencyLevel = depLevel;

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
                        formulas.Add(formula);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to load formulas: {ex.Message}", ex);
            }

            // Sort by dependency level (integer sort is correct here since levels are 0-6)
            formulas.Sort((a, b) => a.DependencyLevel.CompareTo(b.DependencyLevel));

            // Cycle detection via topological sort (Kahn's algorithm)
            var formulaByName = formulas.ToDictionary(f => f.ParameterName, StringComparer.OrdinalIgnoreCase);
            var adjList = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in formulas)
            {
                if (!adjList.ContainsKey(f.ParameterName)) adjList[f.ParameterName] = new List<string>();
                if (!inDegree.ContainsKey(f.ParameterName)) inDegree[f.ParameterName] = 0;
            }

            foreach (var f in formulas)
            {
                foreach (var dep in formulas)
                {
                    if (dep.ParameterName == f.ParameterName) continue;
                    if (f.Expression != null && f.Expression.Contains(dep.ParameterName))
                    {
                        if (!adjList.ContainsKey(dep.ParameterName)) adjList[dep.ParameterName] = new List<string>();
                        adjList[dep.ParameterName].Add(f.ParameterName);
                        inDegree[f.ParameterName] = inDegree.GetValueOrDefault(f.ParameterName) + 1;
                    }
                }
            }

            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var sorted = new List<FormulaDefinition>();
            while (queue.Count > 0)
            {
                string name = queue.Dequeue();
                if (formulaByName.TryGetValue(name, out var fd)) sorted.Add(fd);
                if (adjList.TryGetValue(name, out var neighbors))
                {
                    foreach (var n in neighbors)
                    {
                        inDegree[n]--;
                        if (inDegree[n] == 0) queue.Enqueue(n);
                    }
                }
            }

            if (sorted.Count < formulas.Count)
            {
                var cycleNodes = formulas.Where(f => !sorted.Any(s => s.ParameterName == f.ParameterName)).ToList();
                foreach (var cn in cycleNodes)
                    StingLog.Error($"Formula cycle detected: {cn.ParameterName} (level {cn.DependencyLevel})");
                // Add cycle nodes at the end so they still execute (with potentially wrong values)
                sorted.AddRange(cycleNodes);
            }
            formulas = sorted;

            return formulas;
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
                Parameter param = el.LookupParameter(inputName);
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
                            ?? el.LookupParameter("Width");
                        break;
                    case "Height":
                        p = el.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM)
                            ?? el.LookupParameter("Height");
                        break;
                    case "Length":
                        p = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)
                            ?? el.LookupParameter("Length");
                        break;
                    case "Diameter":
                        p = el.LookupParameter("Diameter")
                            ?? el.LookupParameter("Overall Size");
                        break;
                    case "Thickness":
                        p = el.LookupParameter("Thickness");
                        break;
                    default:
                        p = el.LookupParameter(name);
                        break;
                }

                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble() * ftToMm;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Evaluate a TEXT formula (string concatenation).
        /// Format: ASS_ID_TXT + "-" + ASS_TAG_1_TXT
        /// </summary>
        public static string EvaluateText(string expression, Dictionary<string, object> context)
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
                    else if (context.TryGetValue(trimmed, out object val))
                    {
                        sb.Append(val?.ToString() ?? "");
                    }
                    // else skip unknown references
                }

                string result = sb.ToString();
                return string.IsNullOrEmpty(result) ? null : result;
            }
            catch
            {
                return null;
            }
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
        /// Evaluate a numeric formula using recursive descent parsing.
        /// Supports: +, -, *, /, ^, (), if(), log(), comparison operators.
        /// </summary>
        public static double? EvaluateNumeric(string expression, Dictionary<string, object> context)
        {
            try
            {
                var parser = new ExpressionParser(expression, context);
                double result = parser.Parse();
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Write numeric result to parameter, handling type conversion.</summary>
        public static bool WriteNumericResult(Parameter param, double value)
        {
            try
            {
                // Only write if currently empty/zero
                if (param.StorageType == StorageType.Double)
                {
                    if (Math.Abs(param.AsDouble()) < 0.0001)
                    {
                        param.Set(value);
                        return true;
                    }
                }
                else if (param.StorageType == StorageType.Integer)
                {
                    if (param.AsInteger() == 0)
                    {
                        param.Set((int)Math.Round(value));
                        return true;
                    }
                }
                else if (param.StorageType == StorageType.String)
                {
                    string current = param.AsString();
                    if (string.IsNullOrEmpty(current))
                    {
                        param.Set(value.ToString("G6", CultureInfo.InvariantCulture));
                        return true;
                    }
                }
            }
            catch { }

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
        private class ExpressionParser
        {
            private readonly string _expr;
            private readonly Dictionary<string, object> _ctx;
            private int _pos;

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
                        result = divisor != 0 ? result / divisor : 0;
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
                    result = Math.Pow(result, exp);
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

                // Variable lookup
                if (_ctx.TryGetValue(ident, out object val))
                {
                    if (val is double d) return d;
                    if (val is string s && double.TryParse(s, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out double parsed))
                        return parsed;
                }

                return 0; // unknown variable defaults to 0
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

                double trueVal = ParseComparison();

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ',') _pos++;

                double falseVal = ParseComparison();

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;

                return condition != 0 ? trueVal : falseVal;
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
