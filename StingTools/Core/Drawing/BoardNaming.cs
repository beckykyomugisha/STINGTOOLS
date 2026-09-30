// StingTools — Drawing Template Manager
//
// BoardNaming — how a board is named and how its per-board sheets are keyed.
// Revit-free on purpose: StingTools.Tags.Tests compiles this file.
//
// Panel schedules keyed their sheet "PANEL-" + element id; the panel door diagram
// keyed its sheet "PANEL-DOOR:" + Panel Name, so renaming a board minted a second
// door-diagram sheet. Both are keyed by element id now; the name-keyed tag is still
// recognised so a sheet made before the change is adopted, not duplicated.

using System;

namespace StingTools.Core.Drawing
{
    public static class BoardNaming
    {
        /// <summary>The board's name: its Panel Name, else its element name, else "Board {id}".</summary>
        public static string Resolve(string panelName, string elementName, long elementId)
        {
            if (!string.IsNullOrWhiteSpace(panelName)) return panelName.Trim();
            if (!string.IsNullOrWhiteSpace(elementName)) return elementName.Trim();
            return $"Board {elementId}";
        }

        /// <summary>Sheet context tag of a board's panel-schedule sheet.</summary>
        public static string ScheduleSheetTag(long elementId) => "PANEL-" + elementId;

        /// <summary>Sheet context tag of a board's door-diagram sheet — by element id, so a rename keeps it.</summary>
        public static string DoorDiagramSheetTag(long elementId) => "PANEL-DOOR:#" + elementId;

        /// <summary>The tag a door-diagram sheet carried before it was keyed by id (by board name).</summary>
        public static string LegacyDoorDiagramSheetTag(string boardName) => "PANEL-DOOR:" + (boardName ?? "");

        /// <summary>
        /// True when <paramref name="sheetTag"/> is the pre-id door-diagram tag of
        /// <paramref name="boardName"/> — a sheet to adopt (re-tag by id) rather than duplicate.
        /// Never true for an id-keyed tag, so one board cannot adopt another's sheet.
        /// </summary>
        public static bool IsLegacyDoorDiagramTagFor(string sheetTag, string boardName)
        {
            if (string.IsNullOrEmpty(sheetTag) || string.IsNullOrWhiteSpace(boardName)) return false;
            if (sheetTag.StartsWith("PANEL-DOOR:#", StringComparison.Ordinal)) return false;
            return string.Equals(sheetTag, LegacyDoorDiagramSheetTag(boardName.Trim()), StringComparison.Ordinal)
                || string.Equals(sheetTag, LegacyDoorDiagramSheetTag(boardName), StringComparison.Ordinal);
        }
    }
}
