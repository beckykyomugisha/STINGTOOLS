// ServiceSystemName — the one reader for "which system is this element on".
//
// Six call sites (fabrication grouping, cut lists, the workspace filter pills, the
// spec validator, the HVAC panel) each read their own guess at a system parameter:
// HVC_SYS_TXT and ELC_SYS_TXT, which no shared-parameter file defines, and in some
// places a concatenation of three names. Ducts and conduits therefore read "" and
// fell into one unnamed group. This reads parameters that exist, in one order.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Mep
{
    public static class ServiceSystemName
    {
        /// <summary>
        /// The element's system, first non-blank of: <c>PLM_SYS_TXT</c>, <c>MEC_SYS_TXT</c>,
        /// Revit's own system name (<c>RBS_SYSTEM_NAME_PARAM</c>, on ducts, pipes, fittings and
        /// connected equipment), then the tag's SYS token (<see cref="ParamRegistry.SYS"/>).
        /// "" when none is set; the caller decides what an unnamed system is called.
        /// </summary>
        public static string Read(Element el)
        {
            if (el == null) return "";
            string s = ParameterHelpers.GetString(el, "PLM_SYS_TXT");
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            s = ParameterHelpers.GetString(el, "MEC_SYS_TXT");
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            try
            {
                s = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            catch (Exception ex) { StingLog.Warn($"ServiceSystemName {el.Id}: {ex.Message}"); }
            s = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            return string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
        }
    }
}
