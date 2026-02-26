using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Ported from shared_params.py — complete parameter GUID map extracted
    /// from MR_PARAMETERS.txt. Two binding passes:
    ///   Pass 1 (UniversalParams) — 17 ASS_MNG parameters → all 53 categories.
    ///   Pass 2 (DisciplineParams) — discipline-specific tag containers → correct category subsets.
    /// Uses BuiltInCategory enum values directly for type-safe category resolution.
    /// </summary>
    public static class SharedParamGuids
    {
        public const int NumPad = 4;
        public const string Separator = "-";

        /// <summary>Parameter name → GUID string from MR_PARAMETERS.txt.</summary>
        public static readonly Dictionary<string, Guid> ParamGuids = new Dictionary<string, Guid>
        {
            // ── Pass 1: Universal source tokens (ASS_MNG, group 2) ──
            { "ASS_DISCIPLINE_COD_TXT", new Guid("8c7dcfd7-f922-52d0-b859-81cae8d17dc0") },
            { "ASS_LOC_TXT", new Guid("b7469c27-c80e-5b59-b999-1a99ba620cd1") },
            { "ASS_ZONE_TXT", new Guid("dc0d940f-e4ce-5e73-a0a7-fc7094148c84") },
            { "ASS_LVL_COD_TXT", new Guid("b1e51fab-fa88-50df-8b2f-bcdbe48e7c78") },
            { "ASS_SYSTEM_TYPE_TXT", new Guid("2b3658d9-bfc6-56db-9df5-901337fde0f5") },
            { "ASS_FUNC_TXT", new Guid("1ddff9a8-6e66-4a93-88fe-f3b94fbd5710") },
            { "ASS_PRODCT_COD_TXT", new Guid("082a2a05-3387-5501-b355-51dd45e23e9f") },
            { "ASS_SEQ_NUM_TXT", new Guid("bbe1cd55-247b-48bd-94ba-a08031f06d5b") },
            { "ASS_STATUS_TXT", new Guid("b97665a8-5e34-585b-9674-fbdd83d7637f") },
            { "ASS_INST_DETAIL_NUM_TXT", new Guid("73f74429-76da-4ae8-ae90-66884bad8a06") },
            { "MNT_TYPE_TXT", new Guid("9358b203-900a-52c5-9e80-3a4d67dc5c51") },

            // ── Pass 1: T1 / T2 assembled tag containers ──
            { "ASS_TAG_1_TXT", new Guid("1eeb577d-342d-5039-97f1-f1dd8d80c8c4") },
            { "ASS_TAG_2_TXT", new Guid("bf6cb687-478f-459a-8c5d-ece073c24831") },
            { "ASS_TAG_3_TXT", new Guid("068abd3b-650e-4a7c-ac07-82e556160de0") },
            { "ASS_TAG_4_TXT", new Guid("9432c6e4-8912-4a0b-bf04-2ee1cf3afadf") },
            { "ASS_TAG_5_TXT", new Guid("c875d795-d764-4a8b-826a-2677635b15b9") },
            { "ASS_TAG_6_TXT", new Guid("6fcfc27b-a42d-4f54-92ad-66c875f9be38") },

            // ── Pass 2: HVAC Equipment ──
            { "HVC_EQP_TAG_01_TXT", new Guid("62f66802-8bd6-4861-aad0-f23e5b1905d2") },
            { "HVC_EQP_TAG_02_TXT", new Guid("c3974615-09ac-46ee-8c69-589df04a35c0") },
            { "HVC_EQP_TAG_03_TXT", new Guid("59605dc9-0029-4704-8dff-cf3bd25b749d") },

            // ── Ducts / Duct Fittings / Flex Ducts / Air Terminals ──
            { "HVC_DCT_TAG_01_TXT", new Guid("9751957e-f2c7-4f9a-8f3a-dd432af861f5") },
            { "HVC_DCT_TAG_02_TXT", new Guid("0a3ab853-91c0-4e01-9c93-68c7fb333a85") },
            { "HVC_DCT_TAG_03_TXT", new Guid("7ed52b35-4486-428d-b7ee-8f01c0140763") },
            { "HVC_FLX_TAG_01_TXT", new Guid("cd984834-8273-4d2c-89c4-dfe33520e2d2") },

            // ── Electrical Equipment ──
            { "ELC_EQP_TAG_01_TXT", new Guid("d9441c7a-025c-4034-a7da-dc960df4a57c") },
            { "ELC_EQP_TAG_02_TXT", new Guid("64805f01-8548-44c5-8091-ffcf04d35d7d") },

            // ── Electrical Fixtures + Lighting ──
            { "ELE_FIX_TAG_1_TXT", new Guid("5b5c4c6c-c420-4f30-a67e-bdeca6944577") },
            { "ELE_FIX_TAG_2_TXT", new Guid("6b727a38-f448-4b8e-81fb-a2570637ee57") },
            { "LTG_FIX_TAG_01_TXT", new Guid("647a8916-560f-4c0c-b6ca-f2ee23a97fb0") },
            { "LTG_FIX_TAG_02_TXT", new Guid("7783ce57-766b-4903-8e9f-bde24b706b42") },

            // ── Pipework ──
            { "PLM_EQP_TAG_01_TXT", new Guid("e4f6bae0-bc3b-42f3-a3c9-ef31a23a80d0") },
            { "PLM_EQP_TAG_02_TXT", new Guid("4a31aa91-9511-4fa4-8024-632f64b6a2ae") },

            // ── Fire & Life Safety ──
            { "FLS_DEV_TAG_01_TXT", new Guid("c611d904-3f4f-45c6-81e6-11f3cc64759d") },
            { "FLS_DEV_TAG_02_TXT", new Guid("86a39b3f-6ef2-46d1-a9ab-a0620f1761ac") },

            // ── Conduits ──
            { "ELC_CDT_TAG_01_TXT", new Guid("f867c60f-1fda-4f3f-a052-cf81268c0600") },
            { "ELC_CDT_TAG_02_TXT", new Guid("aab93953-601b-4bab-8d52-079193f71524") },

            // ── Cable Trays ──
            { "ELC_CTR_TAG_01_TXT", new Guid("aed9d3de-71ff-4a73-aba3-5bbd16af7526") },

            // ── Low-voltage / Communications ──
            { "COM_DEV_TAG_01_TXT", new Guid("7775b009-25a6-43b9-b0ec-31ac063a9183") },
            { "SEC_DEV_TAG_01_TXT", new Guid("e892601e-68a8-4f68-9618-223326a2cac4") },
            { "NCL_DEV_TAG_01_TXT", new Guid("34b75836-a597-4bc6-b7ee-6713a5caf6a5") },
            { "ICT_DEV_TAG_01_TXT", new Guid("ec3cc840-731f-47f7-943e-465a027fea74") },

            // ── Material Tags ──
            { "MAT_TAG_1_TXT", new Guid("6b6c3ada-95bb-4a7b-aa9c-beb47c639928") },
            { "MAT_TAG_2_TXT", new Guid("0d78850d-45b1-4179-9079-3e3ec525a4ef") },
            { "MAT_TAG_3_TXT", new Guid("04883c90-411f-4ab5-b937-2909c54fdf65") },
            { "MAT_TAG_4_TXT", new Guid("1d5e82d6-e988-4e9d-8be1-e195f2782041") },
            { "MAT_TAG_5_TXT", new Guid("c7efed83-bbd2-4a2e-b79a-292a1cfa3a18") },
            { "MAT_TAG_6_TXT", new Guid("d0d35a3a-abea-4898-9deb-4cdca865e8ee") },
        };

        /// <summary>The 17 universal parameters bound to all 53 categories (Pass 1).</summary>
        public static readonly string[] UniversalParams = new[]
        {
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
            "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
            "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
            "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
            "ASS_STATUS_TXT", "ASS_INST_DETAIL_NUM_TXT", "MNT_TYPE_TXT",
        };

        /// <summary>
        /// All 53 built-in categories targeted by Pass 1, using type-safe BuiltInCategory
        /// enum values. No string parsing needed — direct enum-to-Category resolution.
        /// </summary>
        public static readonly BuiltInCategory[] AllCategoryEnums = new[]
        {
            // MEP — Mechanical
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_FlexPipeCurves,
            // MEP — Fire Protection
            BuiltInCategory.OST_Sprinklers,
            // MEP — Plumbing
            BuiltInCategory.OST_PlumbingFixtures,
            // MEP — Electrical
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_ConduitFitting,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            // MEP — Life Safety / Low Voltage
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_TelephoneDevices,
            // Architecture
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_Casework,
            // Structure
            BuiltInCategory.OST_Columns,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralStiffener,
            // Architecture — Circulation
            BuiltInCategory.OST_Railings,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_Ramps,
            // Generic / Specialty
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_SpecialityEquipment,
            BuiltInCategory.OST_MedicalEquipment,
            // Site
            BuiltInCategory.OST_Parking,
            BuiltInCategory.OST_Site,
            // Other
            BuiltInCategory.OST_Mass,
            BuiltInCategory.OST_Parts,
            BuiltInCategory.OST_Assemblies,
            BuiltInCategory.OST_DetailComponents,
        };

        /// <summary>
        /// Discipline-specific parameter → category mappings for Pass 2.
        /// Maps each discipline tag parameter to the specific categories it should bind to.
        /// </summary>
        public static readonly Dictionary<string, BuiltInCategory[]> DisciplineBindings =
            new Dictionary<string, BuiltInCategory[]>
        {
            // HVAC Equipment tags → Mechanical Equipment only
            { "HVC_EQP_TAG_01_TXT", new[] { BuiltInCategory.OST_MechanicalEquipment } },
            { "HVC_EQP_TAG_02_TXT", new[] { BuiltInCategory.OST_MechanicalEquipment } },
            { "HVC_EQP_TAG_03_TXT", new[] { BuiltInCategory.OST_MechanicalEquipment } },
            // Duct tags → Ducts, Duct Fittings, Flex Ducts, Air Terminals, Duct Accessories
            { "HVC_DCT_TAG_01_TXT", new[] {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_DuctAccessory } },
            { "HVC_DCT_TAG_02_TXT", new[] {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_DuctAccessory } },
            { "HVC_DCT_TAG_03_TXT", new[] {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_DuctAccessory } },
            { "HVC_FLX_TAG_01_TXT", new[] { BuiltInCategory.OST_FlexDuctCurves } },
            // Electrical Equipment tags
            { "ELC_EQP_TAG_01_TXT", new[] { BuiltInCategory.OST_ElectricalEquipment } },
            { "ELC_EQP_TAG_02_TXT", new[] { BuiltInCategory.OST_ElectricalEquipment } },
            // Electrical Fixtures + Lighting
            { "ELE_FIX_TAG_1_TXT", new[] { BuiltInCategory.OST_ElectricalFixtures } },
            { "ELE_FIX_TAG_2_TXT", new[] { BuiltInCategory.OST_ElectricalFixtures } },
            { "LTG_FIX_TAG_01_TXT", new[] {
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices } },
            { "LTG_FIX_TAG_02_TXT", new[] {
                BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_LightingDevices } },
            // Pipework
            { "PLM_EQP_TAG_01_TXT", new[] {
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_PlumbingFixtures } },
            { "PLM_EQP_TAG_02_TXT", new[] {
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_PlumbingFixtures } },
            // Fire & Life Safety
            { "FLS_DEV_TAG_01_TXT", new[] {
                BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_FireAlarmDevices } },
            { "FLS_DEV_TAG_02_TXT", new[] {
                BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_FireAlarmDevices } },
            // Conduits
            { "ELC_CDT_TAG_01_TXT", new[] {
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting } },
            { "ELC_CDT_TAG_02_TXT", new[] {
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting } },
            // Cable Trays
            { "ELC_CTR_TAG_01_TXT", new[] {
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting } },
            // Low-voltage / Communications
            { "COM_DEV_TAG_01_TXT", new[] { BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_TelephoneDevices } },
            { "SEC_DEV_TAG_01_TXT", new[] { BuiltInCategory.OST_SecurityDevices } },
            { "NCL_DEV_TAG_01_TXT", new[] { BuiltInCategory.OST_NurseCallDevices } },
            { "ICT_DEV_TAG_01_TXT", new[] { BuiltInCategory.OST_DataDevices } },
            // Material Tags → all compound-structure categories
            { "MAT_TAG_1_TXT", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows } },
            { "MAT_TAG_2_TXT", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows } },
            { "MAT_TAG_3_TXT", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs } },
            { "MAT_TAG_4_TXT", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs } },
            { "MAT_TAG_5_TXT", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs } },
            { "MAT_TAG_6_TXT", new[] {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs } },
        };

        /// <summary>
        /// Build a CategorySet from BuiltInCategory enum values (type-safe).
        /// </summary>
        public static CategorySet BuildCategorySet(Document doc, BuiltInCategory[] categories)
        {
            CategorySet catSet = new CategorySet();
            Categories cats = doc.Settings.Categories;
            foreach (BuiltInCategory bic in categories)
            {
                try
                {
                    Category cat = cats.get_Item(bic);
                    if (cat != null && cat.AllowsBoundParameters)
                        catSet.Insert(cat);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Category {bic} not available: {ex.Message}");
                }
            }
            return catSet;
        }
    }
}
