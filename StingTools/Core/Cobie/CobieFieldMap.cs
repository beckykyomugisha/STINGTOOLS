using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Cobie
{
    /// <summary>
    /// The COBie Component and Type field mappings, and the order an export reads
    /// each field. One definition, shared by every import and the export.
    ///
    /// WHY THIS IS ITS OWN CLASS
    /// There were THREE hand-written copies of this mapping -- the primary
    /// Component import, an extended Component import, and the Type import --
    /// and they disagreed. The export read a fourth set of names spelled out
    /// line by line. Nothing compared any of them, because none named the others.
    ///
    /// The disagreements were not cosmetic. Eleven of the targets across those
    /// maps NAMED PARAMETERS THAT DO NOT EXIST in PARAMETER_REGISTRY.json:
    ///
    ///   MNT_WARRANTY_START_TXT, MNT_WARRANTY_YRS_TXT, MNT_WARRANTY_PROVIDER_TXT,
    ///   ASS_MODEL_NUM_TXT, MNT_EXPECTED_LIFE_TXT, ASS_REPLACEMENT_COST_TXT,
    ///   BLE_LENGTH_TXT, BLE_WIDTH_TXT, BLE_HEIGHT_TXT, ASS_COLOUR_TXT
    ///
    /// ParameterHelpers.SetString returns false when the parameter is not on the
    /// element, so every one of those columns was READ FROM THE SPREADSHEET AND
    /// DISCARDED IN SILENCE. Warranty guarantor, warranty duration, expected
    /// life, nominal dimensions and colour never reached a single element.
    ///
    /// That is not a cosmetic defect: the KUT LOD overlay requires
    /// ASS_WARRANTY_PARTS_TXT and ASS_WARRANTY_DURATION_PARTS_YRS at rung 500 for
    /// Tier A and Tier C, and ASS_MODEL_REF_TXT at rung 400. A COBie handover
    /// file could therefore never satisfy the close-out gate by import alone --
    /// the data went nowhere and the gate correctly reported it missing.
    ///
    /// The real COBie parameters existed the whole time. ASS_NOM_LENGTH_TXT,
    /// ASS_NOM_WIDTH_TXT, ASS_NOM_HEIGHT_TXT, ASS_COLOR_TXT and ASS_MODEL_REF_TXT
    /// all carry "[COBie V2.4]" in their own registry descriptions.
    ///
    /// TWO THINGS A TARGET MUST SATISFY, both asserted by CobieFieldMapTests:
    ///   1. it exists in PARAMETER_REGISTRY.json and resolves in
    ///      RESOLVED_BINDINGS.csv -- an unbound parameter cannot be written, so
    ///      requiring it is indistinguishable from not requiring it;
    ///   2. its DATATYPE in MR_PARAMETERS.txt is TEXT -- SetString returns false
    ///      on any other storage type, which fails exactly as silently as a
    ///      missing parameter. A _YRS or _UGX suffix is a naming convention here,
    ///      not a storage type; every target below is genuinely TEXT.
    ///
    /// This is the blind spot the LOD matrix already had and already fixed: a map
    /// validated only against itself. tools/build_kut_lod_overlay.py gained the
    /// same binding gate for the same reason, and its comment records that "two
    /// COBie-group parameters were caught this way".
    ///
    /// Revit-free by design, so the half that was wrong is the half that is tested.
    /// </summary>
    public static class CobieFieldMap
    {
        /// <summary>
        /// COBie Component column -> the STING parameter the import writes.
        ///
        /// These are the targets the PRIMARY import already used; they exist, are
        /// bound to every category, and are what the close-out gate reads.
        /// </summary>
        public static readonly Dictionary<string, string> ComponentColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Description"] = "ASS_DESCRIPTION_TXT",
                ["SerialNumber"] = "ASS_SERIAL_NR_TXT",
                ["BarCode"] = "ASS_BARCODE_TXT",
                ["AssetIdentifier"] = "ASS_ASSET_ID_TXT",
                ["WarrantyDurationParts"] = "ASS_WARRANTY_DURATION_PARTS_YRS",
                ["WarrantyGuarantorParts"] = "ASS_WARRANTY_PARTS_TXT",
                ["InstallationDate"] = "ASS_INSTALLATION_DATE_TXT",
                ["WarrantyStartDate"] = "COM_WARRANTY_START_TXT",
            };

        /// <summary>
        /// COBie Type column -> the STING parameter the import writes, onto the
        /// ElementType rather than the instance.
        ///
        /// ReplacementCost is deliberately ABSENT. The only bound candidate is
        /// PER_REPLACEMENT_COST_UGX, which names a currency the COBie column does
        /// not carry. Writing an unknown-currency figure into a UGX-labelled
        /// field produces wrong data where today there is merely missing data,
        /// and a wrong number in a cost field is the more expensive failure.
        /// Logged in docs/ROADMAP.md rather than guessed at here.
        /// </summary>
        public static readonly Dictionary<string, string> TypeColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Description"] = "ASS_DESCRIPTION_TXT",
                ["Manufacturer"] = "ASS_MANUFACTURER_TXT",
                // COBie ModelNumber -> the model reference the LOD gate requires at
                // rung 400, and the parameter whose own description carries the
                // COBie tag. ASS_MODEL_NR_TXT ("Model number") also exists and is
                // bound; it is the closer literal match but nothing reads it, so
                // importing into it would satisfy no check.
                ["ModelNumber"] = "ASS_MODEL_REF_TXT",
                ["WarrantyDurationParts"] = "ASS_WARRANTY_DURATION_PARTS_YRS",
                ["WarrantyGuarantorParts"] = "ASS_WARRANTY_PARTS_TXT",
                ["ExpectedLife"] = "ASS_EXPECTED_LIFE_YEARS_YRS",
                ["NominalLength"] = "ASS_NOM_LENGTH_TXT",
                ["NominalWidth"] = "ASS_NOM_WIDTH_TXT",
                ["NominalHeight"] = "ASS_NOM_HEIGHT_TXT",
                ["Material"] = "ASS_MATERIAL_TXT",
                ["Color"] = "ASS_COLOR_TXT",
                ["Finish"] = "ASS_FINISH_TXT",
                ["Grade"] = "ASS_GRADE_TXT",
                ["Shape"] = "ASS_SHAPE_TXT",
                ["Size"] = "ASS_SIZE_TXT",
            };

        /// <summary>
        /// Additional parameters an export should fall back to for a Component
        /// column, after the canonical one, in order.
        ///
        /// Read on export and never written on import: writing to a legacy alias
        /// would recreate the second copy this consolidation removed.
        /// </summary>
        public static readonly Dictionary<string, string[]> LegacyReadFallbacks =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["InstallationDate"] = new[] { "COM_INSTALL_DATE_TXT" },
            };

        /// <summary>
        /// Legacy read fallbacks for the Type worksheet.
        ///
        /// The export read ASS_MODEL_NR_TXT for ModelNumber while the gate reads
        /// ASS_MODEL_REF_TXT, so making the import write the gate's parameter
        /// without this would have broken the round-trip in the other direction
        /// -- the same defect, newly introduced. Canonical is the one the gate
        /// reads; the older one is still read back so a model populated before
        /// this change still exports.
        /// </summary>
        public static readonly Dictionary<string, string[]> TypeLegacyReadFallbacks =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModelNumber"] = new[] { "ASS_MODEL_NR_TXT" },
            };

        /// <summary>
        /// Targets bound to a category list rather than to every category, with
        /// the categories they reach.
        ///
        /// Recorded rather than silently tolerated. A COBie warranty start date
        /// imported onto a chiller still goes nowhere, because
        /// COM_WARRANTY_START_TXT is bound only to the five comms and security
        /// categories -- the same silent-discard failure as a missing parameter,
        /// one level narrower. It is kept because it is the only warranty-start
        /// parameter that exists and the primary import already used it; the KUT
        /// pack does not rely on it, recording warranty EXPIRY plus duration
        /// instead precisely because those are bound everywhere (BEP section
        /// 14.3). Widening it is a registry change, logged in docs/ROADMAP.md.
        /// </summary>
        public static readonly Dictionary<string, string[]> NarrowlyBound =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["COM_WARRANTY_START_TXT"] = new[]
                {
                    "Data Devices", "Communication Devices", "Telephone Devices",
                    "Security Devices", "Nurse Call Devices",
                },
                ["COM_INSTALL_DATE_TXT"] = new[]
                {
                    "Data Devices", "Communication Devices", "Telephone Devices",
                    "Security Devices", "Nurse Call Devices",
                },
            };

        /// <summary>
        /// The parameters an export should try for a Component
        /// <paramref name="column"/>, in order: canonical first, then any legacy
        /// alias.
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

        /// <summary>
        /// The parameters an export should try for a Type <paramref name="column"/>,
        /// canonical first. Same contract as ReadOrder, for the other worksheet.
        /// </summary>
        public static IReadOnlyList<string> TypeReadOrder(string column)
        {
            var order = new List<string>();
            string canonical;
            if (!string.IsNullOrEmpty(column) && TypeColumns.TryGetValue(column, out canonical))
                order.Add(canonical);

            string[] fallbacks;
            if (!string.IsNullOrEmpty(column) && TypeLegacyReadFallbacks.TryGetValue(column, out fallbacks))
                order.AddRange(fallbacks.Where(p => !order.Contains(p, StringComparer.OrdinalIgnoreCase)));

            return order;
        }

        /// <summary>Every COBie Component column this mapping covers.</summary>
        public static IEnumerable<string> Columns
        {
            get { return ComponentColumns.Keys; }
        }

        /// <summary>
        /// Every parameter this map can write or read, across both worksheets.
        /// The set the registry, binding and datatype assertions run over.
        /// </summary>
        public static IEnumerable<string> AllTargets
        {
            get
            {
                return ComponentColumns.Values
                    .Concat(TypeColumns.Values)
                    .Concat(LegacyReadFallbacks.Values.SelectMany(v => v))
                    .Concat(TypeLegacyReadFallbacks.Values.SelectMany(v => v))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
