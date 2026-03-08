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
    /// or(), and(), not(), min(), max(), abs(), round(), sqrt(), log(), string concatenation,
    /// comparison operators (=, &lt;&gt;, !=, &lt;, &gt;, &lt;=, &gt;=),
    /// and Revit built-in geometry inputs (Width, Height, Length, etc.).
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

            // Sort by dependency level (level 0 first, then 1, 2, ... 6)
            formulas.Sort((a, b) => a.DependencyLevel.CompareTo(b.DependencyLevel));

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
            int elementsProcessed = 0;

            // BUG-006: Per-formula error tracking
            var formulaErrorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var formulaSampleFailures = new Dictionary<string, List<ElementId>>(StringComparer.Ordinal);

            using (Transaction tx = new Transaction(doc, "STING Evaluate Formulas"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
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
                            if (context == null) continue;

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
            report.AppendLine($"Formula Evaluation Complete");
            report.AppendLine($"Formulas loaded: {formulas.Count} (dependency levels 0-6)");
            report.AppendLine($"Elements updated: {elementsProcessed}");
            report.AppendLine($"Values written: {totalWritten}");
            report.AppendLine($"Evaluations attempted: {totalEvaluated}");
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
                        if (!string.IsNullOrEmpty(sVal))
                        {
                            context[inputName] = sVal;
                            hasAnyInput = true;
                            // Also try parsing as number for dual-type params
                            if (double.TryParse(sVal, NumberStyles.Any,
                                CultureInfo.InvariantCulture, out double parsed))
                                context[inputName + "_NUM"] = parsed;
                        }
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
        /// Evaluate a TEXT formula (string concatenation or if-conditional text).
        /// Formats:
        ///   ASS_ID_TXT + "-" + ASS_TAG_1_TXT
        ///   if(TAG_PARA_STATE_3_BOOL, "long narrative text", "")
        ///   if(or(A &lt; 5, B &gt; 10), " [!WARNING]", "")
        /// </summary>
        public static string EvaluateText(string expression, Dictionary<string, object> context)
        {
            try
            {
                string trimmed = expression.Trim();

                // Handle if() conditionals in text formulas
                if (trimmed.StartsWith("if(", StringComparison.OrdinalIgnoreCase))
                {
                    return EvaluateTextIf(trimmed, context);
                }

                // Split on + for concatenation, handling quoted strings
                var parts = TokenizeTextExpression(expression);
                var sb = new StringBuilder();

                foreach (string part in parts)
                {
                    string p = part.Trim();
                    if (p.StartsWith("\"") && p.EndsWith("\""))
                    {
                        // Quoted literal string
                        sb.Append(p.Substring(1, p.Length - 2));
                    }
                    else if (context.TryGetValue(p, out object val))
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

        /// <summary>
        /// Evaluate an if() expression that returns text strings.
        /// Format: if(condition, "trueText", "falseText")
        /// The condition is evaluated numerically; the branches are text.
        /// </summary>
        private static string EvaluateTextIf(string expression, Dictionary<string, object> context)
        {
            // Use the numeric parser just for the condition, then extract the text branches
            // Find the condition part and the two text branches
            int openParen = expression.IndexOf('(');
            if (openParen < 0) return null;

            // Parse from inside the if(...)
            string inner = expression.Substring(openParen + 1);
            // Remove trailing )
            if (inner.EndsWith(")"))
                inner = inner.Substring(0, inner.Length - 1);

            // Find the condition end (first comma not inside parens or quotes)
            int condEnd = FindTopLevelComma(inner, 0);
            if (condEnd < 0) return null;

            string condExpr = inner.Substring(0, condEnd).Trim();

            // Find the second comma (between true and false branches)
            int trueEnd = FindTopLevelComma(inner, condEnd + 1);
            string trueBranch, falseBranch;
            if (trueEnd >= 0)
            {
                trueBranch = inner.Substring(condEnd + 1, trueEnd - condEnd - 1).Trim();
                falseBranch = inner.Substring(trueEnd + 1).Trim();
            }
            else
            {
                trueBranch = inner.Substring(condEnd + 1).Trim();
                falseBranch = "";
            }

            // Evaluate the condition numerically
            double condResult;
            try
            {
                var parser = new ExpressionParser(condExpr, context);
                condResult = parser.Parse();
            }
            catch
            {
                condResult = 0;
            }

            // Pick the branch and extract string content
            string branch = condResult != 0 ? trueBranch : falseBranch;
            return ExtractTextValue(branch, context);
        }

        /// <summary>Find the next comma at the top level (not inside parens or quotes).</summary>
        private static int FindTopLevelComma(string s, int start)
        {
            int depth = 0;
            bool inQuote = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') inQuote = !inQuote;
                else if (!inQuote)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>Extract text value: quoted string → literal, variable → lookup, nested if → recurse.</summary>
        private static string ExtractTextValue(string branch, Dictionary<string, object> context)
        {
            string trimmed = branch.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == "\"\"") return null;

            // Quoted literal
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                return trimmed.Substring(1, trimmed.Length - 2);

            // Nested if()
            if (trimmed.StartsWith("if(", StringComparison.OrdinalIgnoreCase))
                return EvaluateTextIf(trimmed, context);

            // Concatenation with +
            if (trimmed.Contains('+'))
                return EvaluateText(trimmed, context);

            // Variable reference
            if (context.TryGetValue(trimmed, out object val))
                return val?.ToString();

            return null;
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
        /// Supports: +, -, *, /, ^, (), if(), or(), and(), not(),
        /// min(), max(), abs(), round(), sqrt(), log(), comparison operators.
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
        ///   function   = if | or | and | not | min | max | abs | round | sqrt | log
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
                    // Ensure we don't match partial identifiers (e.g., "<=" from "<>")
                    _pos += s.Length;
                    return true;
                }
                return false;
            }

            private double ParseComparison()
            {
                double left = ParseAddition();
                SkipWhitespace();

                if (Match("<>")) return left != ParseAddition() ? 1 : 0;
                if (Match("!=")) return left != ParseAddition() ? 1 : 0;
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
                string identLower = ident.ToLowerInvariant();
                switch (identLower)
                {
                    case "if": return ParseIf();
                    case "or": return ParseOr();
                    case "and": return ParseAnd();
                    case "not": return ParseNot();
                    case "min": return ParseMin();
                    case "max": return ParseMax();
                    case "abs": return ParseAbs();
                    case "round": return ParseRound();
                    case "sqrt": return ParseSqrt();
                    case "log": return ParseLog();
                }

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

            private string ReadQuotedString()
            {
                if (_pos < _expr.Length && _expr[_pos] == '"')
                {
                    _pos++; // skip opening quote
                    int start = _pos;
                    while (_pos < _expr.Length && _expr[_pos] != '"')
                        _pos++;
                    string val = _expr.Substring(start, _pos - start);
                    if (_pos < _expr.Length) _pos++; // skip closing quote
                    return val;
                }
                return null;
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

                // trueVal/falseVal may be string literals (for TEXT formulas that
                // use if() to select between strings) — handle via ParseComparison
                // which dispatches string literals as 0, or nested if() for cascades
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

                // Check for function calls in condition position (or, and, not)
                int savedPos = _pos;
                string ident = ParseIdentifier();

                if (!string.IsNullOrEmpty(ident))
                {
                    string identLower = ident.ToLowerInvariant();
                    // Delegate to function parsers if matched
                    switch (identLower)
                    {
                        case "or": return ParseOr();
                        case "and": return ParseAnd();
                        case "not": return ParseNot();
                    }

                    SkipWhitespace();

                    // Check for string comparison: PARAM = "value" or PARAM <> "value"
                    bool isNotEqual = false;
                    if (Match("<>") || Match("!="))
                    {
                        isNotEqual = true;
                    }
                    else if (_pos < _expr.Length && _expr[_pos] == '=')
                    {
                        _pos++;
                    }
                    else
                    {
                        // Not a string comparison — restore and parse numeric
                        _pos = savedPos;
                        return ParseComparison();
                    }

                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == '"')
                    {
                        // String comparison
                        string compareValue = ReadQuotedString();
                        if (_ctx.TryGetValue(ident, out object val))
                        {
                            string strVal = val?.ToString() ?? "";
                            bool match = strVal.Equals(compareValue,
                                StringComparison.OrdinalIgnoreCase);
                            return (isNotEqual ? !match : match) ? 1 : 0;
                        }
                        return isNotEqual ? 1 : 0; // missing param: not-equal to anything is true
                    }
                }

                // Not a string comparison — restore position and parse as numeric
                _pos = savedPos;
                return ParseComparison();
            }

            /// <summary>or(condition1, condition2, ...) — returns 1 if ANY condition is non-zero.</summary>
            private double ParseOr()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;

                bool anyTrue = false;
                while (true)
                {
                    double val = ParseComparison();
                    if (val != 0) anyTrue = true;
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return anyTrue ? 1 : 0;
            }

            /// <summary>and(condition1, condition2, ...) — returns 1 if ALL conditions are non-zero.</summary>
            private double ParseAnd()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;

                bool allTrue = true;
                while (true)
                {
                    double val = ParseComparison();
                    if (val == 0) allTrue = false;
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return allTrue ? 1 : 0;
            }

            /// <summary>not(condition) — returns 1 if condition is 0, else 0.</summary>
            private double ParseNot()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;
                double val = ParseComparison();
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return val == 0 ? 1 : 0;
            }

            /// <summary>min(a, b, ...) — returns smallest value.</summary>
            private double ParseMin()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;

                double result = ParseComparison();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == ',')
                    {
                        _pos++;
                        double val = ParseComparison();
                        if (val < result) result = val;
                        continue;
                    }
                    break;
                }

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return result;
            }

            /// <summary>max(a, b, ...) — returns largest value.</summary>
            private double ParseMax()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;

                double result = ParseComparison();
                while (true)
                {
                    SkipWhitespace();
                    if (_pos < _expr.Length && _expr[_pos] == ',')
                    {
                        _pos++;
                        double val = ParseComparison();
                        if (val > result) result = val;
                        continue;
                    }
                    break;
                }

                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return result;
            }

            /// <summary>abs(value) — returns absolute value.</summary>
            private double ParseAbs()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;
                double val = ParseComparison();
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return Math.Abs(val);
            }

            /// <summary>round(value) or round(value, decimals) — rounds to specified decimal places.</summary>
            private double ParseRound()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;
                double val = ParseComparison();
                int decimals = 0;
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ',')
                {
                    _pos++;
                    decimals = (int)ParseComparison();
                    if (decimals < 0) decimals = 0;
                    if (decimals > 15) decimals = 15;
                }
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return Math.Round(val, decimals, MidpointRounding.AwayFromZero);
            }

            /// <summary>sqrt(value) — returns square root (0 for negative inputs).</summary>
            private double ParseSqrt()
            {
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == '(') _pos++;
                double val = ParseComparison();
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
                return val >= 0 ? Math.Sqrt(val) : 0;
            }

            /// <summary>log(value) — returns log base 10 (0 for non-positive inputs).</summary>
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
