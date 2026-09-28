// PanelBoardProfile — Revit-free choice of which template role a board needs,
// from what the board IS rather than what it is called.
//
// The rules in STING_PANEL_SCHEDULE_TEMPLATES.json match names, and the name
// they were matched against was the family TYPE name — so "DB-L1" placed as a
// "Panelboard 208V" type missed its rule. The board's own properties
// (ElectricalEquipment.IsSwitchboard, the supply's phase count) answer the
// question directly; names are only needed for data panels, which Revit does
// not flag.

using System;
using System.Linq;

namespace StingTools.Core.Panels
{
    public static class PanelBoardProfile
    {
        // Rule panelType values in STING_PANEL_SCHEDULE_TEMPLATES.json.
        public const string Switchboard = "Switchboard";
        public const string ThreePhase  = "BranchPanelThreePhase";
        public const string SinglePhase = "BranchPanelSinglePhase";
        public const string Data        = "DataPanel";

        private static readonly string[] DataNameHints = { "DATA", "COMMS", "RACK", "PATCH", "COMM PANEL", "ICT" };

        /// <summary>
        /// The rule panelType a board needs, or null when its properties cannot say
        /// (unknown phases, not a switchboard, no data hint) — callers then fall back
        /// to the name rules.
        /// </summary>
        /// <param name="isSwitchboard">ElectricalEquipment.IsSwitchboard (null = unknown).</param>
        /// <param name="phases">1 or 3 from the distribution system; anything else = unknown.</param>
        /// <param name="names">Panel Name and type name, any order.</param>
        public static string RoleFor(bool? isSwitchboard, int phases, params string[] names)
        {
            if (isSwitchboard == true) return Switchboard;
            if (names != null && names.Any(n => !string.IsNullOrWhiteSpace(n) &&
                    DataNameHints.Any(h => ContainsToken(n, h))))
                return Data;
            if (phases == 3) return ThreePhase;
            if (phases == 1) return SinglePhase;
            return null;
        }

        // Whole-token match so "DATA" does not hit "DATALOGGER-DB" boards by accident
        // and "ICT" does not hit "DISTRICT".
        private static bool ContainsToken(string name, string token)
        {
            string n = name.ToUpperInvariant();
            int i = 0;
            while ((i = n.IndexOf(token, i, StringComparison.Ordinal)) >= 0)
            {
                bool left = i == 0 || !char.IsLetter(n[i - 1]);
                int end = i + token.Length;
                bool right = end >= n.Length || !char.IsLetter(n[end]);
                if (left && right) return true;
                i = end;
            }
            return false;
        }
    }
}
