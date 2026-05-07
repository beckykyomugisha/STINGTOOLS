// ============================================================================
// StingTools Standards - Compliance Engine
// Multi-regional building code compliance checking
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace StingTools.Standards.Compliance
{
    // Pluggable log sink; default no-op so the library has no third-party
    // logging dependency. Hosts (e.g. StingTools plugin) install StingLog
    // by setting StandardsLog.Sink at startup.
    public enum StandardsLogLevel { Info, Warn, Error }

    public static class StandardsLog
    {
        public static Action<StandardsLogLevel, string, Exception> Sink { get; set; } =
            (_, __, ___) => { };
        internal static void Info(string m)               => Sink(StandardsLogLevel.Info, m, null);
        internal static void Warn(string m)               => Sink(StandardsLogLevel.Warn, m, null);
        internal static void Warn(Exception ex, string m) => Sink(StandardsLogLevel.Warn, m, ex);
        internal static void Error(Exception ex, string m)=> Sink(StandardsLogLevel.Error, m, ex);
    }

    /// <summary>
    /// Compliance engine for checking designs against building standards.
    /// Supports IBC, ASHRAE, ADA, ISO 19650, and regional codes.
    /// </summary>
    public class StandardsComplianceEngine
    {
        private readonly Dictionary<string, BuildingStandard> _standards;
        private readonly Dictionary<string, List<ComplianceRule>> _rulesByStandard;
        private readonly List<string> _enabledStandards;

        public StandardsComplianceEngine()
        {
            _standards = new Dictionary<string, BuildingStandard>(StringComparer.OrdinalIgnoreCase);
            _rulesByStandard = new Dictionary<string, List<ComplianceRule>>(StringComparer.OrdinalIgnoreCase);
            _enabledStandards = new List<string>();

            InitializeBuiltInStandards();
        }

        /// <summary>
        /// Loads standards and rules from a CSV file.
        /// </summary>
        public void LoadFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
            {
                StandardsLog.Warn($"Standards file not found: {filePath}");
                return;
            }

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null
                };

                using var reader = new StreamReader(filePath);
                using var csv = new CsvReader(reader, config);

                var records = csv.GetRecords<StandardsCsvRecord>();

                foreach (var record in records)
                {
                    var rule = MapToRule(record);
                    AddRule(rule);
                }

                StandardsLog.Info($"Loaded standards from {filePath}");
            }
            catch (Exception ex)
            {
                StandardsLog.Error(ex, $"Failed to load standards from {filePath}");
            }
        }

        /// <summary>
        /// Enables a standard for compliance checking.
        /// </summary>
        public void EnableStandard(string standardCode)
        {
            if (!_enabledStandards.Contains(standardCode, StringComparer.OrdinalIgnoreCase))
            {
                _enabledStandards.Add(standardCode);
                StandardsLog.Info($"Enabled standard: {standardCode}");
            }
        }

        /// <summary>
        /// Disables a standard.
        /// </summary>
        public void DisableStandard(string standardCode)
        {
            _enabledStandards.RemoveAll(s => s.Equals(standardCode, StringComparison.OrdinalIgnoreCase));
            StandardsLog.Info($"Disabled standard: {standardCode}");
        }

        /// <summary>
        /// Checks compliance for a design element.
        /// </summary>
        public ComplianceCheckResult CheckCompliance(DesignElement element, string region = null)
        {
            var result = new ComplianceCheckResult
            {
                ElementId = element.Id,
                ElementType = element.Type,
                CheckedAt = DateTime.UtcNow
            };

            var applicableRules = GetApplicableRules(element.Type, element.Category, region);

            foreach (var rule in applicableRules)
            {
                var ruleResult = EvaluateRule(rule, element);
                result.RuleResults.Add(ruleResult);

                if (!ruleResult.IsCompliant)
                {
                    if (rule.Severity == RuleSeverity.Critical)
                    {
                        result.Violations.Add(ruleResult);
                    }
                    else
                    {
                        result.Warnings.Add(ruleResult);
                    }
                }
            }

            result.IsFullyCompliant = !result.Violations.Any();
            result.ComplianceScore = CalculateComplianceScore(result);

            return result;
        }

        /// <summary>
        /// Checks compliance for multiple elements.
        /// </summary>
        public BatchComplianceResult CheckBatchCompliance(
            IEnumerable<DesignElement> elements,
            string region = null)
        {
            var batchResult = new BatchComplianceResult
            {
                CheckedAt = DateTime.UtcNow,
                Region = region
            };

            foreach (var element in elements)
            {
                var result = CheckCompliance(element, region);
                batchResult.ElementResults.Add(result);
            }

            batchResult.TotalElements = batchResult.ElementResults.Count;
            batchResult.CompliantElements = batchResult.ElementResults.Count(r => r.IsFullyCompliant);
            batchResult.OverallComplianceScore = batchResult.ElementResults.Any()
                ? batchResult.ElementResults.Average(r => r.ComplianceScore)
                : 1.0;

            return batchResult;
        }

        /// <summary>
        /// Gets all rules for a specific standard.
        /// </summary>
        public IEnumerable<ComplianceRule> GetRulesForStandard(string standardCode)
        {
            return _rulesByStandard.TryGetValue(standardCode, out var rules)
                ? rules.AsReadOnly()
                : Enumerable.Empty<ComplianceRule>();
        }

        /// <summary>
        /// Gets all enabled standards.
        /// </summary>
        public IEnumerable<string> GetEnabledStandards()
        {
            return _enabledStandards.AsReadOnly();
        }

        /// <summary>
        /// Gets all available standards.
        /// </summary>
        public IEnumerable<BuildingStandard> GetAllStandards()
        {
            return _standards.Values;
        }

        /// <summary>
        /// Adds a compliance rule.
        /// </summary>
        public void AddRule(ComplianceRule rule)
        {
            if (!_rulesByStandard.ContainsKey(rule.StandardCode))
            {
                _rulesByStandard[rule.StandardCode] = new List<ComplianceRule>();
            }
            _rulesByStandard[rule.StandardCode].Add(rule);
        }

        private void InitializeBuiltInStandards()
        {
            // International Building Code
            _standards["IBC"] = new BuildingStandard
            {
                Code = "IBC",
                Name = "International Building Code",
                Version = "2021",
                Region = "International",
                Categories = new List<string> { "Egress", "Fire", "Structure", "Accessibility" }
            };

            // ASHRAE Standards
            _standards["ASHRAE"] = new BuildingStandard
            {
                Code = "ASHRAE",
                Name = "ASHRAE Standards",
                Version = "90.1-2019",
                Region = "International",
                Categories = new List<string> { "Energy", "HVAC", "Ventilation", "Thermal" }
            };

            // ADA Accessibility
            _standards["ADA"] = new BuildingStandard
            {
                Code = "ADA",
                Name = "Americans with Disabilities Act",
                Version = "2010",
                Region = "USA",
                Categories = new List<string> { "Accessibility", "Clearances", "Signage" }
            };

            // ISO 19650 BIM Standards
            _standards["ISO19650"] = new BuildingStandard
            {
                Code = "ISO19650",
                Name = "ISO 19650 BIM Standards",
                Version = "2018",
                Region = "International",
                Categories = new List<string> { "Information", "Naming", "Classification" }
            };

            // Initialize built-in rules
            InitializeBuiltInRules();
        }

        private void InitializeBuiltInRules()
        {
            // IBC Egress Rules
            AddRule(new ComplianceRule
            {
                Id = "IBC-1005.1",
                StandardCode = "IBC",
                Category = "Egress",
                Name = "Minimum Egress Width",
                Description = "Minimum clear width of means of egress components",
                Condition = "EgressWidth >= 0.914", // 36 inches = 0.914m
                RequiredValue = "0.914",
                Unit = "m",
                Severity = RuleSeverity.Critical,
                ApplicableTypes = new List<string> { "Door", "Corridor", "Stair" }
            });

            AddRule(new ComplianceRule
            {
                Id = "IBC-1006.2",
                StandardCode = "IBC",
                Category = "Egress",
                Name = "Maximum Travel Distance",
                Description = "Maximum travel distance to an exit",
                Condition = "TravelDistance <= 76.2", // 250 feet = 76.2m (sprinklered)
                RequiredValue = "76.2",
                Unit = "m",
                Severity = RuleSeverity.Critical,
                ApplicableTypes = new List<string> { "Room", "Space" }
            });

            // ADA Accessibility Rules
            AddRule(new ComplianceRule
            {
                Id = "ADA-404.2.4",
                StandardCode = "ADA",
                Category = "Accessibility",
                Name = "Door Maneuvering Clearance",
                Description = "Clear floor space at doors",
                Condition = "ManeuveringClearance >= 1.524", // 60 inches = 1.524m
                RequiredValue = "1.524",
                Unit = "m",
                Severity = RuleSeverity.Major,
                ApplicableTypes = new List<string> { "Door" }
            });

            AddRule(new ComplianceRule
            {
                Id = "ADA-403.5",
                StandardCode = "ADA",
                Category = "Accessibility",
                Name = "Corridor Width",
                Description = "Minimum clear width for accessible routes",
                Condition = "CorridorWidth >= 0.915", // 36 inches minimum
                RequiredValue = "0.915",
                Unit = "m",
                Severity = RuleSeverity.Major,
                ApplicableTypes = new List<string> { "Corridor" }
            });

            // ASHRAE Energy Rules
            AddRule(new ComplianceRule
            {
                Id = "ASHRAE-5.5",
                StandardCode = "ASHRAE",
                Category = "Thermal",
                Name = "Wall Insulation R-Value",
                Description = "Minimum wall insulation for climate zone 4",
                Condition = "WallRValue >= 2.29", // R-13 = 2.29 m²·K/W
                RequiredValue = "2.29",
                Unit = "m²·K/W",
                Severity = RuleSeverity.Major,
                ApplicableTypes = new List<string> { "Wall" }
            });

            AddRule(new ComplianceRule
            {
                Id = "ASHRAE-5.5.3",
                StandardCode = "ASHRAE",
                Category = "Thermal",
                Name = "Window U-Factor",
                Description = "Maximum window U-factor for climate zone 4",
                Condition = "WindowUFactor <= 2.27", // U-0.40 = 2.27 W/(m²·K)
                RequiredValue = "2.27",
                Unit = "W/(m²·K)",
                Severity = RuleSeverity.Major,
                ApplicableTypes = new List<string> { "Window" }
            });

            // ISO 19650 BIM Rules
            AddRule(new ComplianceRule
            {
                Id = "ISO19650-5.1",
                StandardCode = "ISO19650",
                Category = "Naming",
                Name = "Parameter Naming Convention",
                Description = "Parameters must follow ISO 19650 naming conventions",
                Condition = "ParameterNamingCompliant == true",
                Severity = RuleSeverity.Minor,
                ApplicableTypes = new List<string> { "Parameter" }
            });
        }

        private IEnumerable<ComplianceRule> GetApplicableRules(
            string elementType,
            string category,
            string region)
        {
            var rules = new List<ComplianceRule>();

            foreach (var standardCode in _enabledStandards)
            {
                if (_rulesByStandard.TryGetValue(standardCode, out var standardRules))
                {
                    rules.AddRange(standardRules.Where(r =>
                        r.ApplicableTypes.Contains(elementType, StringComparer.OrdinalIgnoreCase) ||
                        r.ApplicableTypes.Contains(category, StringComparer.OrdinalIgnoreCase) ||
                        !r.ApplicableTypes.Any()));
                }
            }

            // Filter by region if specified
            if (!string.IsNullOrEmpty(region))
            {
                rules = rules.Where(r =>
                    string.IsNullOrEmpty(r.Region) ||
                    r.Region.Equals(region, StringComparison.OrdinalIgnoreCase) ||
                    r.Region.Equals("International", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return rules;
        }

        private RuleCheckResult EvaluateRule(ComplianceRule rule, DesignElement element)
        {
            var result = new RuleCheckResult
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                StandardCode = rule.StandardCode,
                Severity = rule.Severity
            };

            try
            {
                // Simple condition evaluation
                var isCompliant = EvaluateCondition(rule.Condition, element);
                result.IsCompliant = isCompliant;

                if (!isCompliant)
                {
                    result.ActualValue = GetActualValue(rule.Condition, element);
                    result.RequiredValue = rule.RequiredValue;
                    result.Message = $"{rule.Description}. Required: {rule.RequiredValue} {rule.Unit}, Actual: {result.ActualValue} {rule.Unit}";
                    result.Recommendation = GenerateRecommendation(rule, element);
                }
            }
            catch (Exception ex)
            {
                result.IsCompliant = false;
                result.Message = $"Error evaluating rule: {ex.Message}";
                StandardsLog.Warn(ex, $"Failed to evaluate rule {rule.Id}");
            }

            return result;
        }

        private bool EvaluateCondition(string condition, DesignElement element)
        {
            // Simple condition parser
            // Format: "PropertyName >= Value" or "PropertyName == Value"

            var parts = condition.Split(new[] { ">=", "<=", "==", ">", "<" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return true; // Invalid condition passes

            var propertyName = parts[0].Trim();
            var requiredValue = parts[1].Trim();

            if (!element.Properties.TryGetValue(propertyName, out var actualValue))
            {
                return true; // Property not found, assume compliant
            }

            if (condition.Contains(">="))
            {
                return Convert.ToDouble(actualValue) >= Convert.ToDouble(requiredValue);
            }
            if (condition.Contains("<="))
            {
                return Convert.ToDouble(actualValue) <= Convert.ToDouble(requiredValue);
            }
            if (condition.Contains("=="))
            {
                return actualValue.ToString().Equals(requiredValue, StringComparison.OrdinalIgnoreCase);
            }
            if (condition.Contains(">"))
            {
                return Convert.ToDouble(actualValue) > Convert.ToDouble(requiredValue);
            }
            if (condition.Contains("<"))
            {
                return Convert.ToDouble(actualValue) < Convert.ToDouble(requiredValue);
            }

            return true;
        }

        private string GetActualValue(string condition, DesignElement element)
        {
            var parts = condition.Split(new[] { ">=", "<=", "==", ">", "<" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1) return "N/A";

            var propertyName = parts[0].Trim();
            return element.Properties.TryGetValue(propertyName, out var value)
                ? value.ToString()
                : "N/A";
        }

        private string GenerateRecommendation(ComplianceRule rule, DesignElement element)
        {
            return $"Adjust {element.Type} to meet {rule.StandardCode} {rule.Category} requirements. {rule.Description}";
        }

        private double CalculateComplianceScore(ComplianceCheckResult result)
        {
            if (!result.RuleResults.Any()) return 1.0;

            var totalWeight = 0.0;
            var passedWeight = 0.0;

            foreach (var ruleResult in result.RuleResults)
            {
                var weight = ruleResult.Severity switch
                {
                    RuleSeverity.Critical => 3.0,
                    RuleSeverity.Major => 2.0,
                    RuleSeverity.Minor => 1.0,
                    _ => 1.0
                };

                totalWeight += weight;
                if (ruleResult.IsCompliant)
                {
                    passedWeight += weight;
                }
            }

            return totalWeight > 0 ? passedWeight / totalWeight : 1.0;
        }

        private ComplianceRule MapToRule(StandardsCsvRecord record)
        {
            return new ComplianceRule
            {
                Id = record.RuleId,
                StandardCode = record.StandardCode,
                Category = record.Category,
                Name = record.Name,
                Description = record.Description,
                Condition = record.Condition,
                RequiredValue = record.RequiredValue,
                Unit = record.Unit,
                Severity = Enum.TryParse<RuleSeverity>(record.Severity, true, out var sev) ? sev : RuleSeverity.Minor,
                Region = record.Region,
                ApplicableTypes = record.ApplicableTypes?.Split(',').Select(t => t.Trim()).ToList() ?? new List<string>()
            };
        }
    }

    // Supporting classes
    public class BuildingStandard
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Region { get; set; }
        public List<string> Categories { get; set; } = new List<string>();
    }

    public class ComplianceRule
    {
        public string Id { get; set; }
        public string StandardCode { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Condition { get; set; }
        public string RequiredValue { get; set; }
        public string Unit { get; set; }
        public RuleSeverity Severity { get; set; }
        public string Region { get; set; }
        public List<string> ApplicableTypes { get; set; } = new List<string>();
    }

    public class DesignElement
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    public class ComplianceCheckResult
    {
        public string ElementId { get; set; }
        public string ElementType { get; set; }
        public DateTime CheckedAt { get; set; }
        public bool IsFullyCompliant { get; set; }
        public double ComplianceScore { get; set; }
        public List<RuleCheckResult> RuleResults { get; set; } = new List<RuleCheckResult>();
        public List<RuleCheckResult> Violations { get; set; } = new List<RuleCheckResult>();
        public List<RuleCheckResult> Warnings { get; set; } = new List<RuleCheckResult>();
    }

    public class RuleCheckResult
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string StandardCode { get; set; }
        public RuleSeverity Severity { get; set; }
        public bool IsCompliant { get; set; }
        public string ActualValue { get; set; }
        public string RequiredValue { get; set; }
        public string Message { get; set; }
        public string Recommendation { get; set; }
    }

    public class BatchComplianceResult
    {
        public DateTime CheckedAt { get; set; }
        public string Region { get; set; }
        public int TotalElements { get; set; }
        public int CompliantElements { get; set; }
        public double OverallComplianceScore { get; set; }
        public List<ComplianceCheckResult> ElementResults { get; set; } = new List<ComplianceCheckResult>();
    }

    public enum RuleSeverity
    {
        Minor,
        Major,
        Critical
    }

    internal class StandardsCsvRecord
    {
        public string RuleId { get; set; }
        public string StandardCode { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Condition { get; set; }
        public string RequiredValue { get; set; }
        public string Unit { get; set; }
        public string Severity { get; set; }
        public string Region { get; set; }
        public string ApplicableTypes { get; set; }
    }
}
