using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Cobie
{
    /// <summary>
    /// The COBie Component field mapping, and the order an export reads each field.
    ///
    /// WHY THIS IS ITS OWN CLASS
    /// The import wrote ASS_INSTALLATION_DATE_TXT and the export read only
    /// COM_INSTALL_DATE_TXT, so a COBie file imported into a model did not
    /// survive being exported again: the installation date fell through to the
    /// phase-derived fallback and came back out as a different value, or as
    /// nothing. Both halves looked correct in isolation. Nothing compared them,
    /// because the two maps lived in two files -- one in the import command, one
    /// spelled out line by line in the export -- and neither named the other.
    ///
    /// Keeping the mapping in one place makes the round-trip property checkable:
    /// whatever the import writes for a column, the export must read for that
    /// same column. StingTools.Tags.Tests asserts exactly that, over this map.
    ///
    /// Revit-free by design. The mapping is a dictionary and a read preference;
    /// only the get and set around it need an Element, so the part that was
    /// wrong is the part that can be tested.
    /// </summary>
    public static class CobieFieldMap
    {
        /// <summary>
        /// COBie Component column -> the STING parameter the import writes.
        /// This is the canonical target: one column, one parameter.
        /// </summary>
        public static readonly Dictionary<string, string> ComponentColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Description"] = "ASS_DESCRIPTION_TXT",
                ["SerialNumber"] = "ASS_SERIAL_NR_TXT",
                ["BarCode"] = "ASS_BARCODE_TXT",
                ["AssetIdentifier"] = "ASS_ASSET_ID_TXT",
                ["WarrantyDurationParts"] = "MNT_WARRANTY_YRS_TXT",
                ["WarrantyGuarantorParts"] = "MNT_WARRANTY_PROVIDER_TXT",
                ["InstallationDate"] = "ASS_INSTALLATION_DATE_TXT",
                ["WarrantyStartDate"] = "MNT_WARRANTY_START_TXT",
            };

        /// <summary>
        /// Additional parameters an export should fall back to for a column,
        /// after the canonical one, in order.
        ///
        /// These exist because projects populated the COBie-group parameter
        /// directly before the canonical one existed. They are read on export and
        /// never written on import: writing to a legacy alias would create the
        /// second copy this consolidation removed. See ParamRegistry.Deprecated-
        /// Params for why the aliases are not deleted outright.
        /// </summary>
        public static readonly Dictionary<string, string[]> LegacyReadFallbacks =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["InstallationDate"] = new[] { "COM_INSTALL_DATE_TXT" },
                ["WarrantyStartDate"] = new[] { "COM_WARRANTY_START_TXT" },
            };

        /// <summary>
        /// The parameters an export should try for <paramref name="column"/>, in
        /// order: the canonical target first, then any legacy alias.
        ///
        /// Canonical FIRST is the whole point. Reading the alias first would mean
        /// a model carrying both -- one imported, one left over -- exported the
        /// stale copy.
        /// </summary>
        public static IReadOnlyList<string> ReadOrder(string column)
        {
            var order = new List<string>();
            string canonical;
            if (!string.IsNullOrEmpty(column) && ComponentColumns.TryGetValue(column, out canonical))
                order.Add(canonical);

            string[] fallbacks;
            if (!string.IsNullOrEmpty(column) && LegacyReadFallbacks.TryGetValue(column, out fallbacks))
                order.AddRange(fallbacks.Where(p => !order.Contains(p, StringComparer.OrdinalIgnoreCase)));

            return order;
        }

        /// <summary>Every COBie column this mapping covers.</summary>
        public static IEnumerable<string> Columns
        {
            get { return ComponentColumns.Keys; }
        }
    }
}
