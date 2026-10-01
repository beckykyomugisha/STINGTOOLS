using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>The connector domains the SYS choice cares about (Revit's <c>Domain</c>, minus the API).</summary>
    internal enum ServiceDomain { Undefined, Hvac, Piping, Electrical }

    /// <summary>One connected service as read from an element's connector.</summary>
    internal readonly struct ConnectorService
    {
        public ConnectorService(string code, bool isPrimary, ServiceDomain domain)
        {
            Code = code;
            IsPrimary = isPrimary;
            Domain = domain;
        }

        public string Code { get; }
        public bool IsPrimary { get; }
        public ServiceDomain Domain { get; }
    }

    /// <summary>
    /// TAGACC-7 / 8 rules for SYS from connectors, without the Revit API so they can be
    /// tested (TAGACC-20). <c>TagConfig.GetSysFromConnector</c> reads the connectors and
    /// the piping system type; the decisions are made here.
    /// </summary>
    internal static class SysConnectorChoice
    {
        /// <summary>Services that are a connection TO equipment rather than what it is for.</summary>
        public static readonly HashSet<string> AuxiliaryServices =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GAS", "FOL", "CON", "DRN", "CND" };

        /// <summary>15 °C — a hydronic system designed at or below this carries chilled water.</summary>
        public const double ChilledWaterMaxKelvin = 288.15;

        public static ServiceDomain PreferredDomain(string categoryName)
        {
            switch (categoryName ?? "")
            {
                case "Mechanical Equipment":
                case "Air Terminals":
                case "Duct Accessories":
                case "Duct Fittings":
                    return ServiceDomain.Hvac;
                case "Plumbing Fixtures":
                case "Plumbing Equipment":
                case "Pipe Accessories":
                case "Pipe Fittings":
                case "Sprinklers":
                    return ServiceDomain.Piping;
                case "Electrical Equipment":
                case "Electrical Fixtures":
                case "Lighting Fixtures":
                case "Lighting Devices":
                    return ServiceDomain.Electrical;
                default:
                    return ServiceDomain.Undefined;
            }
        }

        /// <summary>
        /// TAGACC-7, in connector order: the first PRIMARY connector's service wins;
        /// otherwise the first in the category's own domain; otherwise the first that is not
        /// auxiliary; otherwise the first. Services with no code are ignored. Null when none.
        /// </summary>
        public static string Choose(IEnumerable<ConnectorService> services, ServiceDomain preferred)
        {
            string inDomain = null, nonAuxiliary = null, first = null;
            if (services == null) return null;
            foreach (var s in services)
            {
                if (string.IsNullOrEmpty(s.Code)) continue;
                if (s.IsPrimary) return s.Code;
                if (inDomain == null && preferred != ServiceDomain.Undefined && s.Domain == preferred) inDomain = s.Code;
                if (nonAuxiliary == null && !AuxiliaryServices.Contains(s.Code)) nonAuxiliary = s.Code;
                if (first == null) first = s.Code;
            }
            return inDomain ?? nonAuxiliary ?? first;
        }

        /// <summary>
        /// TAGACC-8: Revit's default "Hydronic Supply / Return" serves chilled water as well as
        /// heating, and the name classifier reads it as HWS. When the code came from the word
        /// HYDRONIC and the system type's design fluid temperature (kelvin, Revit's internal
        /// unit) is known and at or below 15 °C, it is CHW. Anything else is left as it was.
        /// </summary>
        public static string RefineHydronic(string code, string sourceText, double? fluidKelvin)
        {
            if (!string.Equals(code, "HWS", StringComparison.OrdinalIgnoreCase)) return code;
            if (string.IsNullOrEmpty(sourceText)
                || sourceText.IndexOf("HYDRONIC", StringComparison.OrdinalIgnoreCase) < 0) return code;
            if (fluidKelvin is double k && k > 0 && k <= ChilledWaterMaxKelvin) return "CHW";
            return code;
        }
    }
}
