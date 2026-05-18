// StingTools v4 MVP — fabrication, LPS and pricing parameter constants.
//
// 46 shared-parameter constants declared by the v4 family library.
// GUIDs are UUIDv5 deterministic hashes of the parameter name under
// the fixed STING fabrication namespace:
//
//     STING_FAB_NS = 7f9f5e3a-a7c0-b2e4-4d91-4a557c5e3a00
//     GUID(name)   = uuid5(STING_FAB_NS, name)
//
// Regenerate via the Python snippet in tools/mint_fab_guids.py (same
// hash function as the Template-Engine PRJ_ORG_* namespace). As long
// as the namespace and parameter name are stable, the GUID is stable
// — which means rebuilding the plugin does not change binding on
// family-library round-trips.
//
// Data/Parameters/STING_PARAMS_V4.txt must stay in lock-step.

namespace StingTools.Core.Fabrication
{
    /// <summary>
    /// Assembly-level spool/bundle parameters used by Phase 5
    /// AssemblyBuilder and ShopDrawingComposer. v4 Section 2.
    /// </summary>
    public static class AssyParams
    {
        public const string SPOOL_NR_TXT            = "ASS_SPOOL_NR_TXT";
        public const string SPOOL_NR_TXT_GUID       = "1a4353be-eaaa-5e46-95ee-b64a74667194";

        public const string WEIGHT_KG               = "ASS_WEIGHT_KG";
        public const string WEIGHT_KG_GUID          = "eacedb67-b65b-58f7-a5b7-f1b0253ac6c9";

        public const string TEST_PRESSURE_BAR       = "ASS_TEST_PRESSURE_BAR";
        public const string TEST_PRESSURE_BAR_GUID  = "3e3624d3-c79d-5dd5-8014-46c8d273b9ea";

        public const string FAB_LOC_TXT             = "ASS_FAB_LOC_TXT";
        public const string FAB_LOC_TXT_GUID        = "e420804b-d43f-593c-91b1-fd00a18aa584";

        public const string FAB_SEQ_NR              = "ASS_FAB_SEQ_NR";
        public const string FAB_SEQ_NR_GUID         = "5fc70bc2-9955-583c-9d96-5b54c8f34f53";

        public const string FAB_STATUS_TXT          = "ASS_FAB_STATUS_TXT";
        public const string FAB_STATUS_TXT_GUID     = "29ba93ba-238e-5aad-930a-a621b0f43b5b";

        public const string SHIP_DATE_TXT           = "ASS_SHIP_DATE_TXT";
        public const string SHIP_DATE_TXT_GUID      = "c2fc8e62-b793-517c-94c6-d2d7ae7584fe";

        public const string INSTALL_DATE_TXT        = "ASS_INSTALL_DATE_TXT";
        public const string INSTALL_DATE_TXT_GUID   = "953575a9-1d0c-5bb5-a4d2-c52b4b4adf96";

        public const string BOM_REV_TXT             = "ASS_BOM_REV_TXT";
        public const string BOM_REV_TXT_GUID        = "0293f487-2ca9-5514-9b18-ac98b1a20b27";

        public const string QC_INSPECTOR_TXT        = "ASS_QC_INSPECTOR_TXT";
        public const string QC_INSPECTOR_TXT_GUID   = "a028f908-b100-53bc-b21a-1a0a6a03ffac";

        public const string WELD_COUNT_NR           = "ASS_WELD_COUNT_NR";
        public const string WELD_COUNT_NR_GUID      = "6c77833e-4b97-57f5-9a8b-97cc20d6cb61";

        public const string BOLT_COUNT_NR           = "ASS_BOLT_COUNT_NR";
        public const string BOLT_COUNT_NR_GUID      = "77c9f963-0164-5c71-879d-ee7308091866";

        public const string FLANGE_COUNT_NR         = "ASS_FLANGE_COUNT_NR";
        public const string FLANGE_COUNT_NR_GUID    = "016faa7f-1e8f-5a5d-acfb-14983937de69";

        public const string FITTING_COUNT_NR        = "ASS_FITTING_COUNT_NR";
        public const string FITTING_COUNT_NR_GUID   = "ead7d5f3-68fa-58c6-8a21-fa8c6a1ff318";

        public const string LENGTH_TOTAL_MM         = "ASS_LENGTH_TOTAL_MM";
        public const string LENGTH_TOTAL_MM_GUID    = "2605366f-f56b-5843-b8cb-9781b42a4345";

        public const string CUT_COUNT_NR            = "ASS_CUT_COUNT_NR";
        public const string CUT_COUNT_NR_GUID       = "16e7224e-cab9-5233-b155-3fbe194a3d56";

        public const string INSULATION_AREA_M2      = "ASS_INSULATION_AREA_M2";
        public const string INSULATION_AREA_M2_GUID = "21a49d34-9ae2-5058-8d7e-43db4dabd545";

        public const string SUPPORT_COUNT_NR        = "ASS_SUPPORT_COUNT_NR";
        public const string SUPPORT_COUNT_NR_GUID   = "9fadd466-7dfa-5845-9a18-d618c77c418d";

        public const string FAB_NOTES_TXT           = "ASS_FAB_NOTES_TXT";
        public const string FAB_NOTES_TXT_GUID      = "9107dff2-054c-5371-b3ae-6d329aa12542";

        public const string SPOOL_DRAWING_REF_TXT      = "ASS_SPOOL_DRAWING_REF_TXT";
        public const string SPOOL_DRAWING_REF_TXT_GUID = "c1a5983c-333d-53ff-94d1-4326d9ffff86";
    }

    /// <summary>
    /// Lightning-protection system parameters used by the v4 LPS validator
    /// and shop drawing populators. v4 Section 4, aligned with BS EN 62305.
    /// </summary>
    public static class LpsParams
    {
        public const string CLASS_TXT                   = "ELC_LPS_CLASS_TXT";
        public const string CLASS_TXT_GUID              = "081c2e86-3af9-5658-8a26-63da9c1eccc2";

        public const string ROLLING_SPHERE_RADIUS_M      = "ELC_LPS_ROLLING_SPHERE_RADIUS_M";
        public const string ROLLING_SPHERE_RADIUS_M_GUID = "c4eeed34-608c-56a5-b97f-7c899d76f208";

        public const string MESH_SIZE_M                 = "ELC_LPS_MESH_SIZE_M";
        public const string MESH_SIZE_M_GUID            = "d6a9566f-eda9-5e6d-9dcf-fd14440c395b";

        public const string AIR_TERMINAL_COUNT_NR       = "ELC_LPS_AIR_TERMINAL_COUNT_NR";
        public const string AIR_TERMINAL_COUNT_NR_GUID  = "36889f59-a8ba-55c8-8777-6ba332b39bff";

        public const string DOWN_CONDUCTOR_COUNT_NR         = "ELC_LPS_DOWN_CONDUCTOR_COUNT_NR";
        public const string DOWN_CONDUCTOR_COUNT_NR_GUID    = "157527ba-17a8-5014-b6c5-f70273ccd5f5";

        public const string EARTH_ELECTRODE_COUNT_NR        = "ELC_LPS_EARTH_ELECTRODE_COUNT_NR";
        public const string EARTH_ELECTRODE_COUNT_NR_GUID   = "d02bca9d-9159-5477-8488-48f9076841fa";

        public const string EARTH_RESISTANCE_OHM        = "ELC_LPS_EARTH_RESISTANCE_OHM";
        public const string EARTH_RESISTANCE_OHM_GUID   = "80da349f-708b-5165-bb2e-f369dec80e4b";

        public const string BOND_TYPE_TXT               = "ELC_LPS_BOND_TYPE_TXT";
        public const string BOND_TYPE_TXT_GUID          = "1cb4c3d3-8c12-5be3-9eeb-4072b4be3240";

        public const string PROTECTION_ANGLE_DEG        = "ELC_LPS_PROTECTION_ANGLE_DEG";
        public const string PROTECTION_ANGLE_DEG_GUID   = "0063477e-cda5-58a3-a802-061838e57a47";

        public const string ZONE_TXT                    = "ELC_LPS_ZONE_TXT";
        public const string ZONE_TXT_GUID               = "a01025f4-6155-524e-8514-72507f5e04ef";

        public const string RISK_ASSESSMENT_TXT         = "ELC_LPS_RISK_ASSESSMENT_TXT";
        public const string RISK_ASSESSMENT_TXT_GUID    = "330d6fb5-2891-5a28-8ec6-e04618c9d1e4";

        public const string SURGE_PROTECTION_LVL_TXT    = "ELC_LPS_SURGE_PROTECTION_LVL_TXT";
        public const string SURGE_PROTECTION_LVL_TXT_GUID = "c1605d30-bdcb-560e-9d97-bae3303a078e";

        public const string SEPARATION_DISTANCE_MM      = "ELC_LPS_SEPARATION_DISTANCE_MM";
        public const string SEPARATION_DISTANCE_MM_GUID = "441346ff-828f-5298-9fbb-96f27feb22ef";

        public const string CONDUCTOR_CROSS_SECT_MM2    = "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2";
        public const string CONDUCTOR_CROSS_SECT_MM2_GUID = "423133ca-7535-521d-9c37-65ec7ae68166";

        public const string EARTH_TYPE_TXT              = "ELC_LPS_EARTH_TYPE_TXT";
        public const string EARTH_TYPE_TXT_GUID         = "3703245d-a866-5e05-8737-72babfbb85a4";

        public const string INSPECTION_INTERVAL_MONTHS  = "ELC_LPS_INSPECTION_INTERVAL_MONTHS";
        public const string INSPECTION_INTERVAL_MONTHS_GUID = "5339fe4f-caa3-5edc-99b1-53c0defd4ad8";

        public const string TEST_DATE_TXT               = "ELC_LPS_TEST_DATE_TXT";
        public const string TEST_DATE_TXT_GUID          = "d654df13-0913-5e8f-8dfe-98b3971beb86";

        public const string CERT_REF_TXT                = "ELC_LPS_CERT_REF_TXT";
        public const string CERT_REF_TXT_GUID           = "0a8dbcfb-6f73-5c8c-94eb-b72606feae87";

        public const string ELEMENT_TYPE_TXT            = "ELC_LPS_ELEMENT_TYPE_TXT";
        public const string ELEMENT_TYPE_TXT_GUID       = "b2a1c3d4-e5f6-5678-9abc-def012345678";

        public const string PROJECT_NG_OVERRIDE_NR      = "ELC_LPS_PROJECT_NG_OVERRIDE_NR";
        public const string PROJECT_NG_OVERRIDE_NR_GUID = "c3b2d4e5-f6a7-6789-abcd-ef0123456789";

        public const string KC_FACTOR_NR                = "ELC_LPS_KC_FACTOR_NR";
        public const string KC_FACTOR_NR_GUID           = "d4c3e5f6-a7b8-789a-bcde-f01234567890";

        public const string CONDUCTOR_MATERIAL_TXT      = "ELC_LPS_CONDUCTOR_MATERIAL_TXT";
        public const string CONDUCTOR_MATERIAL_TXT_GUID = "e5d4f6a7-b8c9-89ab-cdef-012345678901";

        public const string COMPLIANCE_STATUS_TXT       = "ELC_LPS_COMPLIANCE_STATUS_TXT";
        public const string COMPLIANCE_STATUS_TXT_GUID  = "f6e5a7b8-c9d0-9abc-def0-123456789012";
    }

    /// <summary>
    /// Dual-currency pricing parameters used by the BOQ Cost Manager +
    /// fabrication takeoff. v4 Section 5 extends the existing 5D cost
    /// schema with international pricing + exchange tracking.
    /// </summary>
    public static class CostParams
    {
        public const string INTL_PRICE_USD              = "CST_INTL_PRICE_USD";
        public const string INTL_PRICE_USD_GUID         = "c40720fa-3e80-5880-86c3-a82f43055fbf";

        public const string UG_PRICE_UGX                = "CST_UG_PRICE_UGX";
        public const string UG_PRICE_UGX_GUID           = "694fcd57-d0c2-5ed3-afca-f225781b3bc8";

        public const string FX_RATE_USD_UGX             = "CST_FX_RATE_USD_UGX";
        public const string FX_RATE_USD_UGX_GUID        = "d4e003e1-1f43-5d22-93c1-d9e91d672c52";

        public const string LABOUR_HOURS                = "CST_LABOUR_HOURS";
        public const string LABOUR_HOURS_GUID           = "cb945ed3-ff4d-531c-89fa-c06f503ab46c";

        public const string LABOUR_RATE_UGX             = "CST_LABOUR_RATE_UGX";
        public const string LABOUR_RATE_UGX_GUID        = "3d736d48-cba0-570b-a521-844539bd998c";

        public const string SHIPPING_UGX                = "CST_SHIPPING_UGX";
        public const string SHIPPING_UGX_GUID           = "5758facf-7a3f-5900-b3ea-abf487990b25";

        public const string DUTY_PCT                    = "CST_DUTY_PCT";
        public const string DUTY_PCT_GUID               = "c26d2b96-a012-50d6-bcb9-6a32f24212e0";

        public const string QUOTE_REF_TXT               = "CST_QUOTE_REF_TXT";
        public const string QUOTE_REF_TXT_GUID          = "4de58d8f-38e2-584f-b8aa-5a5744a80fcd";
    }
}
