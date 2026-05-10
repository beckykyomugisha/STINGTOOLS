// StingTools — PlumbingConnectorCompletenessValidator.
//
// Audits every plumbing fixture in the project against the connector
// count expected for its fixture type. Catches the swap-to-manufacturer
// regression where a vendor family ships fewer connectors than the
// seed (e.g. a basin .rfa from Vendor X declares only the cold-water
// connector, breaking AutoPipeDrop's hot + waste passes).
//
// Codes:
//   PLM.CONN.MISSING   — fixture is short the expected number of
//                        connectors for its declared PLM_FIX_TYPE_TXT
//   PLM.CONN.EXTRA     — fixture has more connectors than typical
//                        (informational; some specialist taps justify it)
//   PLM.CONN.UNTYPED   — connector domain isn't piping; flagged as a
//                        family-authoring issue
//   PLM.CONN.NO_TYPE   — fixture has no PLM_FIX_TYPE_TXT, can't audit
//
// Expected connector counts taken from BS 6465-2 fixture-unit tables
// and BS 8558 service requirements. The validator only reports under-
// count as a Warning — overcount is Info-only.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public static class PlumbingConnectorCompletenessValidator
    {
        // Expected free-connector count per PLM_FIX_TYPE_TXT value.
        // Counts before the fixture is wired into a system (so the
        // validator runs immediately after placement, not after
        // AutoPipeDrop). Cold + hot + waste = 3 for a basin etc.
        private static readonly Dictionary<string, int> _expected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "WC",            2 }, // DCW + soil
            { "WC_FLUSH",      2 },
            { "BIDET",         3 }, // DCW + DHW + waste
            { "URINAL",        2 }, // DCW + soil
            { "BASIN",         3 }, // DCW + DHW + waste
            { "WHB",           3 },
            { "SINK",          3 }, // kitchen / utility — DCW + DHW + waste
            { "KITCHEN",       3 },
            { "KITCHEN_SINK",  4 }, // pre-rinse adds a fourth
            { "SHOWER",        3 }, // DCW + DHW + waste
            { "BATH",          3 },
            { "FLOOR_GULLY",   1 }, // waste only
            { "RAINWATER",     1 },
            { "DRINKING_FOUNTAIN", 2 }, // DCW + waste
            { "ICEMAKER",      2 },
            { "DISHWASHER",    2 }, // DCW + waste (some have DHW too — info only)
            { "WASHING_MACHINE", 3 }, // DCW + DHW + waste
            { "SLOP_SINK",     3 },
            { "JANITOR",       3 },
        };

        public static List<ValidationResult> Validate(Document doc)
        {
            var findings = new List<ValidationResult>();
            if (doc == null) return findings;

            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType())
                {
                    if (!(el is FamilyInstance fi)) continue;

                    string type = ParameterHelpers.GetString(fi, "PLM_FIX_TYPE_TXT");
                    if (string.IsNullOrEmpty(type))
                    {
                        // Try ASS_PRODCT_COD_TXT as a fallback signal.
                        type = ParameterHelpers.GetString(fi, "ASS_PRODCT_COD_TXT");
                        if (string.IsNullOrEmpty(type))
                        {
                            findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Info,
                                "PLM.CONN.NO_TYPE",
                                $"Plumbing fixture has no PLM_FIX_TYPE_TXT — cannot audit connector completeness.",
                                "PlumbingConnectorCompleteness"));
                            continue;
                        }
                    }

                    if (!_expected.TryGetValue(type, out int wanted)) continue; // unknown type — skip

                    int total = 0, piping = 0, untypedDomain = 0;
                    try
                    {
                        var cm = fi.MEPModel?.ConnectorManager;
                        if (cm?.Connectors != null)
                        {
                            foreach (Connector c in cm.Connectors)
                            {
                                total++;
                                Domain d;
                                try { d = c.Domain; } catch { d = Domain.DomainUndefined; }
                                if (d == Domain.DomainPiping) piping++;
                                else if (d == Domain.DomainUndefined) untypedDomain++;
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"PlumbingConnectorCompleteness {fi.Id}: {ex.Message}"); continue; }

                    if (piping < wanted)
                    {
                        findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Warning,
                            "PLM.CONN.MISSING",
                            $"{type} has {piping} piping connector(s); expected {wanted} (cold + hot + waste etc.). " +
                            "AutoPipeDrop will leave this fixture only partly wired.",
                            "PlumbingConnectorCompleteness"));
                    }
                    else if (piping > wanted + 1)
                    {
                        findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Info,
                            "PLM.CONN.EXTRA",
                            $"{type} has {piping} piping connector(s); typical is {wanted}. Verify the family is the right product variant.",
                            "PlumbingConnectorCompleteness"));
                    }
                    if (untypedDomain > 0)
                    {
                        findings.Add(new ValidationResult(fi.Id, ValidationSeverity.Info,
                            "PLM.CONN.UNTYPED",
                            $"{type} has {untypedDomain} connector(s) of undefined domain — re-author the family to declare Piping domain.",
                            "PlumbingConnectorCompleteness"));
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"PlumbingConnectorCompletenessValidator: {ex.Message}"); }

            return findings;
        }
    }
}
