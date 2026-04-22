// StingTools v4 MVP — fabrication, LPS and pricing parameter constants.
//
// These 46 constants correspond to shared parameters that the v4 family
// library will declare. GUIDs here use a "v4-YYYY-xxxx-xxxx-xxxxxxxxxxxx"
// placeholder scheme; real GUIDs are assigned during family-library
// authoring and written back into this file plus the v4 shared parameter
// file fragment (Data/Parameters/STING_PARAMS_V4.txt).

namespace StingTools.Core.Fabrication
{
    /// <summary>
    /// Assembly-level spool/bundle parameters used by the Phase 5
    /// AssemblyBuilder and ShopDrawingComposer. v4 Section 2.
    /// </summary>
    public static class AssyParams
    {
        public const string SPOOL_NR_TXT            = "ASS_SPOOL_NR_TXT";
        public const string SPOOL_NR_TXT_GUID       = "v4-0001-0000-0000-000000000001";

        public const string WEIGHT_KG               = "ASS_WEIGHT_KG";
        public const string WEIGHT_KG_GUID          = "v4-0001-0000-0000-000000000002";

        public const string TEST_PRESSURE_BAR       = "ASS_TEST_PRESSURE_BAR";
        public const string TEST_PRESSURE_BAR_GUID  = "v4-0001-0000-0000-000000000003";

        public const string FAB_LOC_TXT             = "ASS_FAB_LOC_TXT";
        public const string FAB_LOC_TXT_GUID        = "v4-0001-0000-0000-000000000004";

        public const string FAB_SEQ_NR              = "ASS_FAB_SEQ_NR";
        public const string FAB_SEQ_NR_GUID         = "v4-0001-0000-0000-000000000005";

        public const string FAB_STATUS_TXT          = "ASS_FAB_STATUS_TXT";
        public const string FAB_STATUS_TXT_GUID     = "v4-0001-0000-0000-000000000006";

        public const string SHIP_DATE_TXT           = "ASS_SHIP_DATE_TXT";
        public const string SHIP_DATE_TXT_GUID      = "v4-0001-0000-0000-000000000007";

        public const string INSTALL_DATE_TXT        = "ASS_INSTALL_DATE_TXT";
        public const string INSTALL_DATE_TXT_GUID   = "v4-0001-0000-0000-000000000008";

        public const string BOM_REV_TXT             = "ASS_BOM_REV_TXT";
        public const string BOM_REV_TXT_GUID        = "v4-0001-0000-0000-000000000009";

        public const string QC_INSPECTOR_TXT        = "ASS_QC_INSPECTOR_TXT";
        public const string QC_INSPECTOR_TXT_GUID   = "v4-0001-0000-0000-00000000000a";

        public const string WELD_COUNT_NR           = "ASS_WELD_COUNT_NR";
        public const string WELD_COUNT_NR_GUID      = "v4-0001-0000-0000-00000000000b";

        public const string BOLT_COUNT_NR           = "ASS_BOLT_COUNT_NR";
        public const string BOLT_COUNT_NR_GUID      = "v4-0001-0000-0000-00000000000c";

        public const string FLANGE_COUNT_NR         = "ASS_FLANGE_COUNT_NR";
        public const string FLANGE_COUNT_NR_GUID    = "v4-0001-0000-0000-00000000000d";

        public const string FITTING_COUNT_NR        = "ASS_FITTING_COUNT_NR";
        public const string FITTING_COUNT_NR_GUID   = "v4-0001-0000-0000-00000000000e";

        public const string LENGTH_TOTAL_MM         = "ASS_LENGTH_TOTAL_MM";
        public const string LENGTH_TOTAL_MM_GUID    = "v4-0001-0000-0000-00000000000f";

        public const string CUT_COUNT_NR            = "ASS_CUT_COUNT_NR";
        public const string CUT_COUNT_NR_GUID       = "v4-0001-0000-0000-000000000010";

        public const string INSULATION_AREA_M2      = "ASS_INSULATION_AREA_M2";
        public const string INSULATION_AREA_M2_GUID = "v4-0001-0000-0000-000000000011";

        public const string SUPPORT_COUNT_NR        = "ASS_SUPPORT_COUNT_NR";
        public const string SUPPORT_COUNT_NR_GUID   = "v4-0001-0000-0000-000000000012";

        public const string FAB_NOTES_TXT           = "ASS_FAB_NOTES_TXT";
        public const string FAB_NOTES_TXT_GUID      = "v4-0001-0000-0000-000000000013";

        public const string SPOOL_DRAWING_REF_TXT      = "ASS_SPOOL_DRAWING_REF_TXT";
        public const string SPOOL_DRAWING_REF_TXT_GUID = "v4-0001-0000-0000-000000000014";
    }

    /// <summary>
    /// Lightning-protection system parameters used by the v4 LPS validator
    /// and shop drawing populators. v4 Section 4, aligned with BS EN 62305.
    /// </summary>
    public static class LpsParams
    {
        public const string CLASS_TXT                   = "ELC_LPS_CLASS_TXT";
        public const string CLASS_TXT_GUID              = "v4-0002-0000-0000-000000000001";

        public const string ROLLING_SPHERE_RADIUS_M      = "ELC_LPS_ROLLING_SPHERE_RADIUS_M";
        public const string ROLLING_SPHERE_RADIUS_M_GUID = "v4-0002-0000-0000-000000000002";

        public const string MESH_SIZE_M                 = "ELC_LPS_MESH_SIZE_M";
        public const string MESH_SIZE_M_GUID            = "v4-0002-0000-0000-000000000003";

        public const string AIR_TERMINAL_COUNT_NR       = "ELC_LPS_AIR_TERMINAL_COUNT_NR";
        public const string AIR_TERMINAL_COUNT_NR_GUID  = "v4-0002-0000-0000-000000000004";

        public const string DOWN_CONDUCTOR_COUNT_NR         = "ELC_LPS_DOWN_CONDUCTOR_COUNT_NR";
        public const string DOWN_CONDUCTOR_COUNT_NR_GUID    = "v4-0002-0000-0000-000000000005";

        public const string EARTH_ELECTRODE_COUNT_NR        = "ELC_LPS_EARTH_ELECTRODE_COUNT_NR";
        public const string EARTH_ELECTRODE_COUNT_NR_GUID   = "v4-0002-0000-0000-000000000006";

        public const string EARTH_RESISTANCE_OHM        = "ELC_LPS_EARTH_RESISTANCE_OHM";
        public const string EARTH_RESISTANCE_OHM_GUID   = "v4-0002-0000-0000-000000000007";

        public const string BOND_TYPE_TXT               = "ELC_LPS_BOND_TYPE_TXT";
        public const string BOND_TYPE_TXT_GUID          = "v4-0002-0000-0000-000000000008";

        public const string PROTECTION_ANGLE_DEG        = "ELC_LPS_PROTECTION_ANGLE_DEG";
        public const string PROTECTION_ANGLE_DEG_GUID   = "v4-0002-0000-0000-000000000009";

        public const string ZONE_TXT                    = "ELC_LPS_ZONE_TXT";
        public const string ZONE_TXT_GUID               = "v4-0002-0000-0000-00000000000a";

        public const string RISK_ASSESSMENT_TXT         = "ELC_LPS_RISK_ASSESSMENT_TXT";
        public const string RISK_ASSESSMENT_TXT_GUID    = "v4-0002-0000-0000-00000000000b";

        public const string SURGE_PROTECTION_LVL_TXT    = "ELC_LPS_SURGE_PROTECTION_LVL_TXT";
        public const string SURGE_PROTECTION_LVL_TXT_GUID = "v4-0002-0000-0000-00000000000c";

        public const string SEPARATION_DISTANCE_MM      = "ELC_LPS_SEPARATION_DISTANCE_MM";
        public const string SEPARATION_DISTANCE_MM_GUID = "v4-0002-0000-0000-00000000000d";

        public const string CONDUCTOR_CROSS_SECT_MM2    = "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2";
        public const string CONDUCTOR_CROSS_SECT_MM2_GUID = "v4-0002-0000-0000-00000000000e";

        public const string EARTH_TYPE_TXT              = "ELC_LPS_EARTH_TYPE_TXT";
        public const string EARTH_TYPE_TXT_GUID         = "v4-0002-0000-0000-00000000000f";

        public const string INSPECTION_INTERVAL_MONTHS  = "ELC_LPS_INSPECTION_INTERVAL_MONTHS";
        public const string INSPECTION_INTERVAL_MONTHS_GUID = "v4-0002-0000-0000-000000000010";

        public const string TEST_DATE_TXT               = "ELC_LPS_TEST_DATE_TXT";
        public const string TEST_DATE_TXT_GUID          = "v4-0002-0000-0000-000000000011";

        public const string CERT_REF_TXT                = "ELC_LPS_CERT_REF_TXT";
        public const string CERT_REF_TXT_GUID           = "v4-0002-0000-0000-000000000012";
    }

    /// <summary>
    /// Dual-currency pricing parameters used by the BOQ Cost Manager +
    /// fabrication takeoff. v4 Section 5 extends the existing 5D cost
    /// schema with international pricing + exchange tracking.
    /// </summary>
    public static class CostParams
    {
        public const string INTL_PRICE_USD              = "CST_INTL_PRICE_USD";
        public const string INTL_PRICE_USD_GUID         = "v4-0003-0000-0000-000000000001";

        public const string UG_PRICE_UGX                = "CST_UG_PRICE_UGX";
        public const string UG_PRICE_UGX_GUID           = "v4-0003-0000-0000-000000000002";

        public const string FX_RATE_USD_UGX             = "CST_FX_RATE_USD_UGX";
        public const string FX_RATE_USD_UGX_GUID        = "v4-0003-0000-0000-000000000003";

        public const string LABOUR_HOURS                = "CST_LABOUR_HOURS";
        public const string LABOUR_HOURS_GUID           = "v4-0003-0000-0000-000000000004";

        public const string LABOUR_RATE_UGX             = "CST_LABOUR_RATE_UGX";
        public const string LABOUR_RATE_UGX_GUID        = "v4-0003-0000-0000-000000000005";

        public const string SHIPPING_UGX                = "CST_SHIPPING_UGX";
        public const string SHIPPING_UGX_GUID           = "v4-0003-0000-0000-000000000006";

        public const string DUTY_PCT                    = "CST_DUTY_PCT";
        public const string DUTY_PCT_GUID               = "v4-0003-0000-0000-000000000007";

        public const string QUOTE_REF_TXT               = "CST_QUOTE_REF_TXT";
        public const string QUOTE_REF_TXT_GUID          = "v4-0003-0000-0000-000000000008";
    }
}
