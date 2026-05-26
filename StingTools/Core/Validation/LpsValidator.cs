using System;
using System.Globalization;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    /// <summary>
    /// Phase 176 — Lightning Protection System validator (BS EN 62305).
    /// Evaluates the 10 LPS rules defined in TAG_CONFIG_v5_0_VALIDATION.csv §15
    /// and writes the WARN_ELC_LPS_* parameters that the Tier-warning rows in
    /// the LPS tag families render. Hooked into TagPipelineHelper.RunFullPipeline
    /// so any element passing the IsLightningProtection sniff gets BS EN 62305
    /// gates evaluated on every tagging pass.
    ///
    /// Convention — when a rule fires, the warning param holds the human-readable
    /// message; when it passes, the param is cleared. ELC_LPS_COMPLIANCE_STATUS_TXT
    /// rolls up: PASS / WARN / FAIL based on rule severity.
    /// </summary>
    public static class LpsValidator
    {
        /// <summary>
        /// Run all BS EN 62305 rules against the element and persist the
        /// resulting warning text into the WARN_ELC_LPS_* parameters.
        /// No-op for non-LPS elements. Safe to call inside a transaction —
        /// uses ParameterHelpers.SetString so read-only / missing params
        /// silently skip.
        /// </summary>
        public static void EvaluateAndWrite(Document doc, Element el, bool overwrite = true)
        {
            if (doc == null || el == null) return;
            if (!TagConfig.IsLightningProtection(el)) return;

            int critical = 0, high = 0, medium = 0;

            string lpsClass = ParameterHelpers.GetString(el, "ELC_LPS_CLASS_TXT");
            string zone     = ParameterHelpers.GetString(el, "ELC_LPS_ZONE_TXT");
            string risk     = ParameterHelpers.GetString(el, "ELC_LPS_RISK_ASSESSMENT_TXT");
            string bondType = ParameterHelpers.GetString(el, "ELC_LPS_BOND_TYPE_TXT");
            string condMat  = ParameterHelpers.GetString(el, "ELC_LPS_CONDUCTOR_MATERIAL_TXT");
            string testDate = ParameterHelpers.GetString(el, "ELC_LPS_TEST_DATE_TXT");

            int    downCount    = ParameterHelpers.GetInt(el, "ELC_LPS_DOWN_CONDUCTOR_COUNT_NR", 0);
            int    inspectMonths= ParameterHelpers.GetInt(el, "ELC_LPS_INSPECTION_INTERVAL_MONTHS", 0);
            double earthOhm     = ReadDouble(el, "ELC_LPS_EARTH_RESISTANCE_OHM");
            double meshM        = ReadDouble(el, "ELC_LPS_MESH_SIZE_M");
            double sepMm        = ReadDouble(el, "ELC_LPS_SEPARATION_DISTANCE_MM");
            double crossSect    = ReadDouble(el, "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2");

            // 1. Class missing — CRITICAL — BS EN 62305-1 §8
            critical += Write(el, "WARN_ELC_LPS_NO_CLASS",
                string.IsNullOrEmpty(lpsClass)
                    ? "[CRITICAL] LPS class missing — set I/II/III/IV per BS EN 62305-2 risk assessment"
                    : "", overwrite);

            // 2. Down conductor count below class minimum — HIGH — BS EN 62305-3 Table 4
            int downMin = DownCondMinForClass(lpsClass);
            high += Write(el, "WARN_ELC_LPS_DOWN_COND_INSUFFICIENT",
                (downMin > 0 && downCount > 0 && downCount < downMin)
                    ? $"[HIGH] Down conductor count {downCount} < class {lpsClass} minimum {downMin} (BS EN 62305-3 Tab 4)"
                    : "", overwrite);

            // 3. Earth resistance > 10 ohm — HIGH — BS EN 62305-3 §5.4.1
            high += Write(el, "WARN_ELC_LPS_EARTH_RESISTANCE_HIGH",
                (earthOhm > 10.0)
                    ? $"[HIGH] Earth resistance {earthOhm:F2} Ω exceeds 10 Ω (BS EN 62305-3 §5.4.1)"
                    : "", overwrite);

            // 4. No risk assessment — HIGH — BS EN 62305-2
            high += Write(el, "WARN_ELC_LPS_NO_RISK_ASSESSMENT",
                string.IsNullOrEmpty(risk)
                    ? "[HIGH] BS EN 62305-2 risk assessment (R1..R4) reference missing"
                    : "", overwrite);

            // 5. Separation distance — HIGH — BS EN 62305-3 §6.3
            // Plugin-side check: any positive sep > 0 below the conservative
            // 100 mm rule of thumb (proper s = ki·kc·l/km is computed by the
            // v4 LPS validator via project Ng + structure height inputs).
            high += Write(el, "WARN_ELC_LPS_SEPARATION_FAIL",
                (sepMm > 0 && sepMm < 100.0)
                    ? $"[HIGH] Separation distance {sepMm:F0} mm below safe minimum (BS EN 62305-3 §6.3)"
                    : "", overwrite);

            // 6. Inspection interval exceeded — MEDIUM — BS EN 62305-3 §E.7
            medium += Write(el, "WARN_ELC_LPS_INSPECTION_OVERDUE",
                IsInspectionOverdue(testDate, inspectMonths, lpsClass)
                    ? "[MEDIUM] Inspection interval exceeded (BS EN 62305-3 §E.7) — Class I/II=12mo, III/IV=24mo"
                    : "", overwrite);

            // 7. Conductor cross-section below class minimum — HIGH — BS EN 62305-3 Table 6
            double xMin = MinCrossSectionForMaterial(condMat);
            high += Write(el, "WARN_ELC_LPS_CONDUCTOR_CROSS_SECT_LOW",
                (xMin > 0 && crossSect > 0 && crossSect < xMin)
                    ? $"[HIGH] Cross-section {crossSect:F0} mm² below {condMat} minimum {xMin:F0} mm² (BS EN 62305-3 Tab 6)"
                    : "", overwrite);

            // 8. Bonding type missing — HIGH — BS EN 62305-3 §6.2
            high += Write(el, "WARN_ELC_LPS_NO_BONDING",
                string.IsNullOrEmpty(bondType)
                    ? "[HIGH] Equipotential bonding type missing — DIRECT / SPD / ISOLATING_SPARK_GAP required"
                    : "", overwrite);

            // 9. Mesh exceeds class limit — HIGH — BS EN 62305-3 Table 2
            double meshMax = MeshMaxForClass(lpsClass);
            high += Write(el, "WARN_ELC_LPS_MESH_SIZE_EXCEEDED",
                (meshMax > 0 && meshM > 0 && meshM > meshMax)
                    ? $"[HIGH] Mesh {meshM:F1} m exceeds class {lpsClass} limit {meshMax:F0} m (BS EN 62305-3 Tab 2)"
                    : "", overwrite);

            // 10. LPZ missing — MEDIUM — BS EN 62305-4 §4.1
            medium += Write(el, "WARN_ELC_LPS_NO_ZONE",
                string.IsNullOrEmpty(zone)
                    ? "[MEDIUM] Lightning protection zone (LPZ0A/LPZ0B/LPZ1/LPZ2/LPZ3) not assigned"
                    : "", overwrite);

            // Roll-up — BS EN 62305 compliance verdict
            string verdict = critical > 0 ? "FAIL"
                          : high > 0     ? "WARN"
                          : medium > 0   ? "WARN"
                          : "PASS";
            ParameterHelpers.SetString(el, "ELC_LPS_COMPLIANCE_STATUS_TXT", verdict, overwrite: true);
        }

        /// <summary>BS EN 62305-3 Table 4: minimum down-conductor count by class.</summary>
        private static int DownCondMinForClass(string lpsClass)
        {
            if (string.IsNullOrEmpty(lpsClass)) return 0;
            switch (lpsClass.Trim().ToUpperInvariant())
            {
                case "I":   return 4;
                case "II":  return 3;
                case "III": return 2;
                case "IV":  return 2;
                default:    return 0;
            }
        }

        /// <summary>BS EN 62305-3 Table 2: maximum mesh size (m) by class.</summary>
        private static double MeshMaxForClass(string lpsClass)
        {
            if (string.IsNullOrEmpty(lpsClass)) return 0;
            switch (lpsClass.Trim().ToUpperInvariant())
            {
                case "I":   return 5.0;
                case "II":  return 10.0;
                case "III": return 15.0;
                case "IV":  return 20.0;
                default:    return 0;
            }
        }

        /// <summary>BS EN 62305-3 Table 6: minimum cross-section by conductor material.</summary>
        private static double MinCrossSectionForMaterial(string material)
        {
            if (string.IsNullOrEmpty(material)) return 50.0; // default to Cu
            string m = material.Trim().ToUpperInvariant();
            if (m.Contains("ALUM") || m == "AL")    return 70.0;
            if (m.Contains("STEEL") || m == "FE")   return 50.0;
            if (m.Contains("COPPER") || m == "CU")  return 50.0;
            return 50.0;
        }

        /// <summary>True when ISO-8601 test date + interval is older than today.</summary>
        private static bool IsInspectionOverdue(string testDateIso, int intervalMonths, string lpsClass)
        {
            if (string.IsNullOrEmpty(testDateIso)) return false; // no date = no overdue claim
            if (!DateTime.TryParse(testDateIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var t))
                return false;
            int months = intervalMonths > 0
                ? intervalMonths
                : (lpsClass == "I" || lpsClass == "II" ? 12 : 24);
            return t.AddMonths(months) < DateTime.UtcNow;
        }

        /// <summary>Read a numeric STING param, returning 0 if absent / non-numeric.</summary>
        private static double ReadDouble(Element el, string paramName)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return v;
            }
            catch (Exception ex) { StingLog.Warn($"LpsValidator.ReadDouble {paramName}: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// Persist warning text. Returns 1 when the warning fired (non-empty
        /// message written), 0 when cleared. Callers tally these to compute
        /// the rolled-up ELC_LPS_COMPLIANCE_STATUS_TXT verdict.
        /// </summary>
        private static int Write(Element el, string paramName, string message, bool overwrite)
        {
            ParameterHelpers.SetString(el, paramName, message ?? string.Empty, overwrite: overwrite);
            return string.IsNullOrEmpty(message) ? 0 : 1;
        }
    }
}
