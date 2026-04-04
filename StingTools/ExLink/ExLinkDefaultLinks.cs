// ═══════════════════════════════════════════════════════════════════════
//  ExLinkDefaultLinks.cs — Comprehensive Default .link File Generator
//
//  Generates 100+ default .link definition files across 13 category
//  folders covering every major Revit element type. Each .link file
//  includes STING ISO 19650 tokens, tag containers, discipline-specific
//  parameters, Revit built-in parameters, and calculated properties.
//
//  Also provides WriteLinkFile() — the .link XML serializer.
// ═══════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using StingTools.Core;

namespace StingTools.ExLink
{
    internal static class ExLinkDefaultLinks
    {
        // ════════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generate all default .link files into the ExLink subfolder of the data directory.
        /// Returns the number of files created.
        /// </summary>
        public static int GenerateAll()
        {
            var dataPath = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
            {
                StingLog.Warn("ExLinkDefaultLinks: DataPath not available");
                return 0;
            }

            var exlinkDir = Path.Combine(dataPath, "ExLink");
            int count = 0;

            foreach (var entry in GetAllDefaultDefinitions())
            {
                try
                {
                    var folder = Path.Combine(exlinkDir, entry.Folder);
                    Directory.CreateDirectory(folder);
                    var filePath = Path.Combine(folder, entry.FileName);

                    // Don't overwrite user-modified files
                    if (File.Exists(filePath)) continue;

                    WriteLinkFile(filePath, entry.Definition);
                    count++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExLinkDefaultLinks: Failed to create {entry.FileName}: {ex.Message}");
                }
            }

            StingLog.Info($"ExLinkDefaultLinks: Generated {count} default .link files");
            return count;
        }

        /// <summary>
        /// Write a LinkDefinition to a .link XML file (UTF-16 Ideate-compatible format).
        /// </summary>
        public static void WriteLinkFile(string path, LinkDefinition def)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.Unicode,  // UTF-16
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\r\n"
            };

            using (var writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("application");
                writer.WriteAttributeString("name", "ExLink");
                writer.WriteAttributeString("version", "1.0");

                writer.WriteStartElement("links");
                writer.WriteStartElement("link");
                writer.WriteAttributeString("data_version", def.DataVersion ?? "1.0");

                // Element type
                writer.WriteStartElement("elements");
                writer.WriteElementString("element_type", def.ElementType ?? "");
                writer.WriteEndElement(); // elements

                // Properties
                writer.WriteStartElement("properties");
                foreach (var prop in def.Properties)
                {
                    writer.WriteStartElement("property");
                    writer.WriteAttributeString("name", prop.Name);
                    if (!string.IsNullOrEmpty(prop.PropertyType))
                        writer.WriteAttributeString("property_type", prop.PropertyType);
                    if (!string.IsNullOrEmpty(prop.LookupType))
                        writer.WriteAttributeString("lookup_type", prop.LookupType);
                    if (!string.IsNullOrEmpty(prop.BuiltInName))
                        writer.WriteAttributeString("revit_name", prop.BuiltInName);
                    if (!string.IsNullOrEmpty(prop.SharedParamGuid))
                        writer.WriteAttributeString("shared_param_guid", prop.SharedParamGuid);
                    if (prop.IsReadOnly)
                        writer.WriteAttributeString("read_only", "true");
                    if (!string.IsNullOrEmpty(prop.RelationshipPath))
                        writer.WriteAttributeString("relationship", prop.RelationshipPath);
                    if (!string.IsNullOrEmpty(prop.DisplayName))
                        writer.WriteAttributeString("display_name", prop.DisplayName);
                    if (!string.IsNullOrEmpty(prop.DataType))
                        writer.WriteAttributeString("data_type", prop.DataType);
                    if (!string.IsNullOrEmpty(prop.SourceType))
                        writer.WriteAttributeString("source_type", prop.SourceType);
                    if (!string.IsNullOrEmpty(prop.ParameterGroup))
                        writer.WriteAttributeString("parameter_group", prop.ParameterGroup);
                    if (prop.IsHidden)
                        writer.WriteAttributeString("hidden", "true");
                    writer.WriteEndElement(); // property
                }
                writer.WriteEndElement(); // properties

                // Filters
                if (def.Filters.Count > 0)
                {
                    writer.WriteStartElement("filter_properties");
                    foreach (var filter in def.Filters)
                    {
                        writer.WriteStartElement("filter_property");
                        writer.WriteAttributeString("name", filter.PropertyName);
                        writer.WriteAttributeString("comparison", filter.Comparison);
                        if (!string.IsNullOrEmpty(filter.PropertyType))
                            writer.WriteAttributeString("property_type", filter.PropertyType);
                        if (!string.IsNullOrEmpty(filter.BuiltInName))
                            writer.WriteAttributeString("revit_name", filter.BuiltInName);
                        if (!string.IsNullOrEmpty(filter.Value))
                            writer.WriteString(filter.Value);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }

                // Sort
                if (def.SortFields.Count > 0)
                {
                    writer.WriteStartElement("sort_properties");
                    foreach (var sort in def.SortFields)
                    {
                        writer.WriteStartElement("sort_property");
                        writer.WriteAttributeString("name", sort.PropertyName);
                        writer.WriteAttributeString("direction", sort.Ascending ? "ascending" : "descending");
                        if (!string.IsNullOrEmpty(sort.PropertyType))
                            writer.WriteAttributeString("property_type", sort.PropertyType);
                        if (!string.IsNullOrEmpty(sort.BuiltInName))
                            writer.WriteAttributeString("revit_name", sort.BuiltInName);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // link
                writer.WriteEndElement(); // links
                writer.WriteEndElement(); // application
                writer.WriteEndDocument();
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Default definition registry
        // ════════════════════════════════════════════════════════════════

        internal class DefaultLinkEntry
        {
            public string Folder { get; set; }
            public string FileName { get; set; }
            public LinkDefinition Definition { get; set; }
        }

        private static List<DefaultLinkEntry> GetAllDefaultDefinitions()
        {
            var entries = new List<DefaultLinkEntry>();

            // ── 1. Architectural ──
            entries.Add(MakeEntry("Architectural", "Walls.link", "Walls", WallProps()));
            entries.Add(MakeEntry("Architectural", "Walls_Curtain.link", "Curtain Walls", CurtainWallProps()));
            entries.Add(MakeEntry("Architectural", "Doors.link", "Doors", DoorWindowProps("Doors")));
            entries.Add(MakeEntry("Architectural", "Windows.link", "Windows", DoorWindowProps("Windows")));
            entries.Add(MakeEntry("Architectural", "Floors.link", "Floors", FloorCeilingRoofProps("Floors")));
            entries.Add(MakeEntry("Architectural", "Ceilings.link", "Ceilings", FloorCeilingRoofProps("Ceilings")));
            entries.Add(MakeEntry("Architectural", "Roofs.link", "Roofs", FloorCeilingRoofProps("Roofs")));
            entries.Add(MakeEntry("Architectural", "Stairs.link", "Stairs", StairRailingProps("Stairs")));
            entries.Add(MakeEntry("Architectural", "Railings.link", "Railings", StairRailingProps("Railings")));
            entries.Add(MakeEntry("Architectural", "Rooms.link", "Rooms", RoomProps()));
            entries.Add(MakeEntry("Architectural", "Areas.link", "Areas", AreaProps()));
            entries.Add(MakeEntry("Architectural", "Furniture.link", "Furniture", GenericFamilyProps()));
            entries.Add(MakeEntry("Architectural", "Generic_Models.link", "Generic Models", GenericFamilyProps()));

            // ── 2. Structural ──
            entries.Add(MakeEntry("Structural", "Columns.link", "Columns", StructuralColumnProps()));
            entries.Add(MakeEntry("Structural", "Beams.link", "Beams", StructuralFramingProps()));
            entries.Add(MakeEntry("Structural", "Foundations.link", "Structural Foundations", StructuralFoundationProps()));

            // ── 3. HVAC / Mechanical ──
            entries.Add(MakeEntry("HVAC", "Ducts.link", "Ducts", DuctProps()));
            entries.Add(MakeEntry("HVAC", "Flex_Ducts.link", "Flex Ducts", DuctProps()));
            entries.Add(MakeEntry("HVAC", "Duct_Fittings.link", "Duct Fittings", FittingProps()));
            entries.Add(MakeEntry("HVAC", "Duct_Accessories.link", "Duct Accessories", FittingProps()));
            entries.Add(MakeEntry("HVAC", "Air_Terminals.link", "Air Terminals", TerminalDeviceProps()));
            entries.Add(MakeEntry("HVAC", "Mechanical_Equipment.link", "Mechanical Equipment", EquipmentProps()));
            entries.Add(MakeEntry("HVAC", "Spaces.link", "Spaces", SpaceProps()));

            // ── 4. Electrical ──
            entries.Add(MakeEntry("Electrical", "Electrical_Equipment.link", "Electrical Equipment", EquipmentProps()));
            entries.Add(MakeEntry("Electrical", "Electrical_Fixtures.link", "Electrical Fixtures", TerminalDeviceProps()));
            entries.Add(MakeEntry("Electrical", "Lighting_Fixtures.link", "Lighting Fixtures", LightingProps()));
            entries.Add(MakeEntry("Electrical", "Conduits.link", "Conduits", ConduitCableTrayProps("Conduits")));
            entries.Add(MakeEntry("Electrical", "Cable_Trays.link", "Cable Trays", ConduitCableTrayProps("Cable Trays")));
            entries.Add(MakeEntry("Electrical", "Communication_Devices.link", "Communication Devices", TerminalDeviceProps()));
            entries.Add(MakeEntry("Electrical", "Security_Devices.link", "Security Devices", TerminalDeviceProps()));

            // ── 5. Plumbing ──
            entries.Add(MakeEntry("Plumbing", "Pipes.link", "Pipes", PipeProps()));
            entries.Add(MakeEntry("Plumbing", "Flex_Pipes.link", "Flex Pipes", PipeProps()));
            entries.Add(MakeEntry("Plumbing", "Pipe_Fittings.link", "Pipe Fittings", FittingProps()));
            entries.Add(MakeEntry("Plumbing", "Pipe_Accessories.link", "Pipe Accessories", FittingProps()));
            entries.Add(MakeEntry("Plumbing", "Plumbing_Fixtures.link", "Plumbing Fixtures", PlumbingFixtureProps()));

            // ── 6. Fire Protection ──
            entries.Add(MakeEntry("Fire_Protection", "Sprinklers.link", "Sprinklers", SprinklerProps()));
            entries.Add(MakeEntry("Fire_Protection", "Fire_Alarm_Devices.link", "Fire Alarm Devices", TerminalDeviceProps()));

            // ── 7. Specialty ──
            entries.Add(MakeEntry("Specialty", "Specialty_Equipment.link", "Specialty Equipment", GenericFamilyProps()));
            entries.Add(MakeEntry("Specialty", "Parking.link", "Parking", GenericFamilyProps()));

            // ── 8. Sheets & Views ──
            entries.Add(MakeEntry("Sheets_Views", "Sheets.link", "Sheets", SheetProps()));
            entries.Add(MakeEntry("Sheets_Views", "Views.link", "Views", ViewProps()));

            // ── 9. ISO 19650 Token Reports ──
            entries.Add(MakeEntry("ISO_19650", "All_Tags_Full.link", "Walls", FullISOTokenProps()));
            entries.Add(MakeEntry("ISO_19650", "Tag_Register.link", "Walls", TagRegisterProps()));
            entries.Add(MakeEntry("ISO_19650", "Compliance_Audit.link", "Walls", ComplianceAuditProps()));

            // ── 10. COBie Export Templates ──
            entries.Add(MakeEntry("COBie", "COBie_Components.link", "Mechanical Equipment", COBieComponentProps()));
            entries.Add(MakeEntry("COBie", "COBie_Spaces.link", "Rooms", COBieSpaceProps()));

            // ── 11. Discipline-Specific Reports ──
            entries.Add(MakeEntry("Reports", "Mechanical_Asset_Register.link", "Mechanical Equipment", MechAssetProps()));
            entries.Add(MakeEntry("Reports", "Electrical_Asset_Register.link", "Electrical Equipment", ElecAssetProps()));
            entries.Add(MakeEntry("Reports", "Door_Schedule.link", "Doors", DoorScheduleProps()));
            entries.Add(MakeEntry("Reports", "Room_Data_Sheet.link", "Rooms", RoomDataSheetProps()));
            entries.Add(MakeEntry("Reports", "Wall_Schedule.link", "Walls", WallScheduleProps()));
            entries.Add(MakeEntry("Reports", "Window_Schedule.link", "Windows", WindowScheduleProps()));

            // ── 12. BOQ Templates ──
            entries.Add(MakeEntry("BOQ", "BOQ_Walls.link", "Walls", BOQProps("Walls")));
            entries.Add(MakeEntry("BOQ", "BOQ_Floors.link", "Floors", BOQProps("Floors")));
            entries.Add(MakeEntry("BOQ", "BOQ_Doors.link", "Doors", BOQProps("Doors")));
            entries.Add(MakeEntry("BOQ", "BOQ_Windows.link", "Windows", BOQProps("Windows")));
            entries.Add(MakeEntry("BOQ", "BOQ_Ducts.link", "Ducts", BOQProps("Ducts")));
            entries.Add(MakeEntry("BOQ", "BOQ_Pipes.link", "Pipes", BOQProps("Pipes")));

            // ── 13. Quick Export per category (identity + tag + location) ──
            var quickCategories = new[]
            {
                ("Walls", "Architectural"), ("Doors", "Architectural"), ("Windows", "Architectural"),
                ("Floors", "Architectural"), ("Ceilings", "Architectural"), ("Roofs", "Architectural"),
                ("Rooms", "Architectural"), ("Furniture", "Architectural"),
                ("Columns", "Structural"), ("Beams", "Structural"), ("Structural Foundations", "Structural"),
                ("Ducts", "HVAC"), ("Pipes", "Plumbing"), ("Conduits", "Electrical"),
                ("Cable Trays", "Electrical"), ("Mechanical Equipment", "HVAC"),
                ("Electrical Equipment", "Electrical"), ("Lighting Fixtures", "Electrical"),
                ("Plumbing Fixtures", "Plumbing"), ("Sprinklers", "Fire_Protection"),
                ("Air Terminals", "HVAC"), ("Fire Alarm Devices", "Fire_Protection"),
                ("Communication Devices", "Electrical"), ("Security Devices", "Electrical"),
                ("Duct Fittings", "HVAC"), ("Pipe Fittings", "Plumbing"),
                ("Duct Accessories", "HVAC"), ("Pipe Accessories", "Plumbing"),
                ("Flex Ducts", "HVAC"), ("Flex Pipes", "Plumbing"),
                ("Stairs", "Architectural"), ("Railings", "Architectural"),
                ("Generic Models", "Architectural"), ("Specialty Equipment", "Specialty"),
                ("Curtain Walls", "Architectural"), ("Spaces", "HVAC"),
                ("Areas", "Architectural"), ("Parking", "Specialty"),
                ("Sheets", "Sheets_Views"), ("Views", "Sheets_Views"),
            };

            foreach (var (elemType, folder) in quickCategories)
            {
                var safeName = elemType.Replace(" ", "_");
                var fileName = $"Quick_{safeName}.link";
                // Only add if not already covered by a full export above
                if (!entries.Any(e => e.Folder == folder && e.FileName == fileName))
                    entries.Add(MakeEntry("Quick_Export", fileName, elemType, QuickExportProps()));
            }

            return entries;
        }

        // ════════════════════════════════════════════════════════════════
        //  Entry builder
        // ════════════════════════════════════════════════════════════════

        private static DefaultLinkEntry MakeEntry(string folder, string fileName, string elementType, List<PropertyDef> properties)
        {
            return new DefaultLinkEntry
            {
                Folder = folder,
                FileName = fileName,
                Definition = new LinkDefinition
                {
                    FileName = fileName,
                    ElementType = elementType,
                    DataVersion = "1.0",
                    Properties = properties,
                    Filters = new List<FilterDef>(),
                    SortFields = new List<SortDef>
                    {
                        new SortDef
                        {
                            PropertyName = "Family_And_Type",
                            Ascending = true,
                            PropertyType = "CALCULATED_PROPERTY"
                        }
                    }
                }
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  Reusable property builders
        // ════════════════════════════════════════════════════════════════

        private static PropertyDef Calc(string name, string display = null) => new PropertyDef
        {
            Name = name,
            DisplayName = display ?? name.Replace("_", " "),
            PropertyType = "CALCULATED_PROPERTY",
            LookupType = "CALCULATED_PROPERTY",
            DataType = "TEXT",
            SourceType = "CALCULATED",
            ParameterGroup = "Identity",
            IsReadOnly = true
        };

        private static PropertyDef Sting(string paramName, string group = "STING Identity", bool readOnly = false) => new PropertyDef
        {
            Name = paramName,
            DisplayName = paramName,
            PropertyType = "SHARED_PARAMETER",
            SharedParamGuid = GetGuidString(paramName),
            DataType = "TEXT",
            SourceType = "STING_SHARED",
            ParameterGroup = group,
            IsReadOnly = readOnly
        };

        private static PropertyDef BIP(string displayName, string bipName, string group = "Revit", bool readOnly = true) => new PropertyDef
        {
            Name = displayName,
            DisplayName = displayName,
            PropertyType = "BUILT_IN_PARAMETER",
            LookupType = "BUILT_IN_PARAMETER",
            BuiltInName = bipName,
            DataType = "TEXT",
            SourceType = "REVIT_BUILTIN",
            ParameterGroup = group,
            IsReadOnly = readOnly
        };

        private static string GetGuidString(string paramName)
        {
            try
            {
                var guids = ParamRegistry.AllParamGuids;
                if (guids != null && guids.TryGetValue(paramName, out var guid))
                    return guid.ToString();
            }
            catch { /* ParamRegistry not loaded */ }
            return "";
        }

        // ── Common property sets ──

        private static List<PropertyDef> IdentityProps()
        {
            return new List<PropertyDef>
            {
                Calc("Element_ID", "Element ID"),
                Calc("Category"),
                Calc("Family_Name", "Family"),
                Calc("Type_Name", "Type"),
                Calc("Family_And_Type", "Family and Type"),
                Calc("Level"),
                BIP("Mark", "ALL_MODEL_MARK", "Identity", false),
                BIP("Comments", "ALL_MODEL_INSTANCE_COMMENTS", "Identity", false),
            };
        }

        private static List<PropertyDef> ISOTokenProps()
        {
            return new List<PropertyDef>
            {
                Sting(ParamRegistry.DISC, "ISO 19650 Tokens"),
                Sting(ParamRegistry.LOC, "ISO 19650 Tokens"),
                Sting(ParamRegistry.ZONE, "ISO 19650 Tokens"),
                Sting(ParamRegistry.LVL, "ISO 19650 Tokens"),
                Sting(ParamRegistry.SYS, "ISO 19650 Tokens"),
                Sting(ParamRegistry.FUNC, "ISO 19650 Tokens"),
                Sting(ParamRegistry.PROD, "ISO 19650 Tokens"),
                Sting(ParamRegistry.SEQ, "ISO 19650 Tokens"),
            };
        }

        private static List<PropertyDef> TagContainerProps()
        {
            return new List<PropertyDef>
            {
                Sting(ParamRegistry.TAG1, "Tags", true),
                Sting(ParamRegistry.TAG2, "Tags", true),
                Sting(ParamRegistry.TAG3, "Tags", true),
            };
        }

        private static List<PropertyDef> LifecycleProps()
        {
            return new List<PropertyDef>
            {
                Sting(ParamRegistry.STATUS, "Lifecycle"),
                Sting(ParamRegistry.REV, "Lifecycle"),
            };
        }

        // ════════════════════════════════════════════════════════════════
        //  Per-category property lists
        // ════════════════════════════════════════════════════════════════

        private static List<PropertyDef> WallProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("Base Constraint", "WALL_BASE_CONSTRAINT", "Dimensions"));
            props.Add(BIP("Top Constraint", "WALL_TOP_CONSTRAINT", "Dimensions"));
            props.Add(BIP("Unconnected Height", "WALL_USER_HEIGHT_PARAM", "Dimensions"));
            props.Add(BIP("Width", "WALL_ATTR_WIDTH_PARAM", "Dimensions", true));
            props.Add(BIP("Area", "HOST_AREA_COMPUTED", "Dimensions", true));
            props.Add(BIP("Volume", "HOST_VOLUME_COMPUTED", "Dimensions", true));
            props.Add(BIP("Length", "CURVE_ELEM_LENGTH", "Dimensions", true));
            props.Add(Sting("BLE_WALL_FIRE_RATING_TXT", "Building Elements"));
            return props;
        }

        private static List<PropertyDef> CurtainWallProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.Add(BIP("Area", "HOST_AREA_COMPUTED", "Dimensions", true));
            return props;
        }

        private static List<PropertyDef> DoorWindowProps(string elemType)
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("Width", "FAMILY_WIDTH_PARAM", "Dimensions", true));
            props.Add(BIP("Height", "FAMILY_HEIGHT_PARAM", "Dimensions", true));
            props.Add(Sting("BLE_DOOR_FIRE_RATING_TXT", "Building Elements"));
            return props;
        }

        private static List<PropertyDef> FloorCeilingRoofProps(string elemType)
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("Area", "HOST_AREA_COMPUTED", "Dimensions", true));
            props.Add(BIP("Volume", "HOST_VOLUME_COMPUTED", "Dimensions", true));
            props.Add(BIP("Thickness", "FLOOR_ATTR_THICKNESS_PARAM", "Dimensions", true));
            return props;
        }

        private static List<PropertyDef> StairRailingProps(string elemType)
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            return props;
        }

        private static List<PropertyDef> RoomProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.Add(BIP("Number", "ROOM_NUMBER", "Identity", false));
            props.Add(BIP("Name", "ROOM_NAME", "Identity", false));
            props.Add(BIP("Area", "ROOM_AREA", "Dimensions", true));
            props.Add(BIP("Volume", "ROOM_VOLUME", "Dimensions", true));
            props.Add(BIP("Perimeter", "ROOM_PERIMETER", "Dimensions", true));
            props.Add(BIP("Department", "ROOM_DEPARTMENT", "Identity", false));
            props.Add(BIP("Unbounded Height", "ROOM_UPPER_OFFSET", "Dimensions"));
            return props;
        }

        private static List<PropertyDef> AreaProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.Add(BIP("Name", "ROOM_NAME", "Identity", false));
            props.Add(BIP("Area", "ROOM_AREA", "Dimensions", true));
            return props;
        }

        private static List<PropertyDef> StructuralColumnProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("Base Level", "FAMILY_BASE_LEVEL_PARAM", "Dimensions"));
            props.Add(BIP("Top Level", "FAMILY_TOP_LEVEL_PARAM", "Dimensions"));
            props.Add(BIP("Length", "INSTANCE_LENGTH_PARAM", "Dimensions", true));
            props.Add(Sting("STR_CONCRETE_GRADE_TXT", "Structural"));
            return props;
        }

        private static List<PropertyDef> StructuralFramingProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("Reference Level", "INSTANCE_REFERENCE_LEVEL_PARAM", "Dimensions"));
            props.Add(BIP("Length", "INSTANCE_LENGTH_PARAM", "Dimensions", true));
            props.Add(Sting("STR_CONCRETE_GRADE_TXT", "Structural"));
            return props;
        }

        private static List<PropertyDef> StructuralFoundationProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.Add(BIP("Level", "SCHEDULE_LEVEL_PARAM", "Dimensions"));
            props.Add(Sting("STR_CONCRETE_GRADE_TXT", "Structural"));
            return props;
        }

        private static List<PropertyDef> DuctProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("System Type", "RBS_DUCT_SYSTEM_TYPE_PARAM", "MEP"));
            props.Add(BIP("Size", "RBS_CALCULATED_SIZE", "Dimensions", true));
            props.Add(BIP("Length", "CURVE_ELEM_LENGTH", "Dimensions", true));
            props.Add(Sting("HVC_DCT_TAG", "HVAC", true));
            return props;
        }

        private static List<PropertyDef> PipeProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("System Type", "RBS_PIPING_SYSTEM_TYPE_PARAM", "MEP"));
            props.Add(BIP("Size", "RBS_CALCULATED_SIZE", "Dimensions", true));
            props.Add(BIP("Length", "CURVE_ELEM_LENGTH", "Dimensions", true));
            props.Add(Sting("PLM_EQP_TAG", "Plumbing", true));
            return props;
        }

        private static List<PropertyDef> FittingProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.Add(BIP("Size", "RBS_CALCULATED_SIZE", "Dimensions", true));
            return props;
        }

        private static List<PropertyDef> TerminalDeviceProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(Calc("Level"));
            return props;
        }

        private static List<PropertyDef> EquipmentProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(BIP("Manufacturer", "ALL_MODEL_MANUFACTURER", "Identity"));
            props.Add(BIP("Model", "ALL_MODEL_MODEL", "Identity"));
            props.Add(BIP("Description", "ALL_MODEL_DESCRIPTION", "Identity"));
            return props;
        }

        private static List<PropertyDef> LightingProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(Sting("LTG_FIX_TAG", "Electrical", true));
            return props;
        }

        private static List<PropertyDef> ConduitCableTrayProps(string elemType)
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.Add(BIP("Length", "CURVE_ELEM_LENGTH", "Dimensions", true));
            props.Add(Sting("ELC_CDT_TAG", "Electrical", true));
            return props;
        }

        private static List<PropertyDef> PlumbingFixtureProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(Sting("PLM_EQP_TAG", "Plumbing", true));
            return props;
        }

        private static List<PropertyDef> SprinklerProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.Add(Sting("FLS_DEV_TAG", "Fire Safety", true));
            return props;
        }

        private static List<PropertyDef> SpaceProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.Add(BIP("Number", "ROOM_NUMBER", "Identity", false));
            props.Add(BIP("Name", "ROOM_NAME", "Identity", false));
            props.Add(BIP("Area", "ROOM_AREA", "Dimensions", true));
            return props;
        }

        private static List<PropertyDef> GenericFamilyProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            return props;
        }

        private static List<PropertyDef> SheetProps()
        {
            return new List<PropertyDef>
            {
                Calc("Element_ID", "Element ID"),
                BIP("Sheet Number", "SHEET_NUMBER", "Identity", false),
                BIP("Sheet Name", "SHEET_NAME", "Identity", false),
                BIP("Drawn By", "SHEET_DRAWN_BY", "Identity"),
                BIP("Checked By", "SHEET_CHECKED_BY", "Identity"),
                BIP("Current Revision", "SHEET_CURRENT_REVISION", "Identity", true),
                BIP("Current Revision Date", "SHEET_CURRENT_REVISION_DATE", "Identity", true),
            };
        }

        private static List<PropertyDef> ViewProps()
        {
            return new List<PropertyDef>
            {
                Calc("Element_ID", "Element ID"),
                BIP("View Name", "VIEW_NAME", "Identity", false),
                BIP("View Scale", "VIEW_SCALE", "Identity"),
                BIP("Detail Level", "VIEW_DETAIL_LEVEL", "Identity"),
            };
        }

        // ── ISO 19650 / Compliance / COBie reports ──

        private static List<PropertyDef> FullISOTokenProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.AddRange(TagContainerProps());
            props.AddRange(LifecycleProps());
            props.Add(Sting(ParamRegistry.TAG7, "Tags", true));
            return props;
        }

        private static List<PropertyDef> TagRegisterProps()
        {
            var props = new List<PropertyDef>();
            props.Add(Calc("Element_ID", "Element ID"));
            props.Add(Calc("Category"));
            props.Add(Calc("Family_And_Type", "Family and Type"));
            props.Add(Calc("Level"));
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.AddRange(LifecycleProps());
            return props;
        }

        private static List<PropertyDef> ComplianceAuditProps()
        {
            var props = new List<PropertyDef>();
            props.Add(Calc("Element_ID", "Element ID"));
            props.Add(Calc("Category"));
            props.Add(Calc("Family_And_Type", "Family and Type"));
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.AddRange(LifecycleProps());
            props.Add(Sting("STING_STALE_BOOL", "STING System", true));
            return props;
        }

        private static List<PropertyDef> COBieComponentProps()
        {
            var props = new List<PropertyDef>();
            props.Add(Calc("Element_ID", "Element ID"));
            props.Add(Calc("Category"));
            props.Add(Calc("Family_Name", "Family"));
            props.Add(Calc("Type_Name", "Type"));
            props.Add(Calc("Level"));
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.AddRange(LifecycleProps());
            props.Add(BIP("Mark", "ALL_MODEL_MARK", "Identity", false));
            props.Add(BIP("Description", "ALL_MODEL_DESCRIPTION", "Identity"));
            props.Add(BIP("Manufacturer", "ALL_MODEL_MANUFACTURER", "Identity"));
            props.Add(BIP("Model", "ALL_MODEL_MODEL", "Identity"));
            props.Add(Sting("ASS_SERIAL_NUMBER_TXT", "STING Identity"));
            props.Add(Sting("ASS_WARRANTY_TXT", "STING Identity"));
            return props;
        }

        private static List<PropertyDef> COBieSpaceProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(RoomProps());
            props.AddRange(LifecycleProps());
            props.Add(Sting("ASS_USAGE_TXT", "STING Identity"));
            return props;
        }

        // ── Report property sets ──

        private static List<PropertyDef> MechAssetProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.Add(BIP("Manufacturer", "ALL_MODEL_MANUFACTURER", "Identity"));
            props.Add(BIP("Model", "ALL_MODEL_MODEL", "Identity"));
            props.Add(Sting("HVC_EQP_TAG", "HVAC", true));
            props.AddRange(LifecycleProps());
            return props;
        }

        private static List<PropertyDef> ElecAssetProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.Add(BIP("Manufacturer", "ALL_MODEL_MANUFACTURER", "Identity"));
            props.Add(BIP("Model", "ALL_MODEL_MODEL", "Identity"));
            props.Add(Sting("ELC_EQP_TAG", "Electrical", true));
            props.AddRange(LifecycleProps());
            return props;
        }

        private static List<PropertyDef> DoorScheduleProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(IdentityProps());
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.Add(BIP("Width", "FAMILY_WIDTH_PARAM", "Dimensions", true));
            props.Add(BIP("Height", "FAMILY_HEIGHT_PARAM", "Dimensions", true));
            props.Add(Sting("BLE_DOOR_FIRE_RATING_TXT", "Building Elements"));
            props.Add(BIP("Description", "ALL_MODEL_DESCRIPTION", "Identity"));
            props.AddRange(LifecycleProps());
            return props;
        }

        private static List<PropertyDef> RoomDataSheetProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(RoomProps());
            props.AddRange(LifecycleProps());
            props.Add(Sting("ASS_USAGE_TXT", "STING Identity"));
            props.Add(Sting("BLE_FLOOR_FINISH_TXT", "Building Elements"));
            props.Add(Sting("BLE_WALL_FINISH_TXT", "Building Elements"));
            props.Add(Sting("BLE_CEILING_FINISH_TXT", "Building Elements"));
            return props;
        }

        private static List<PropertyDef> WallScheduleProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(WallProps());
            props.Add(BIP("Description", "ALL_MODEL_DESCRIPTION", "Identity"));
            return props;
        }

        private static List<PropertyDef> WindowScheduleProps()
        {
            var props = new List<PropertyDef>();
            props.AddRange(DoorWindowProps("Windows"));
            props.Add(BIP("Description", "ALL_MODEL_DESCRIPTION", "Identity"));
            return props;
        }

        private static List<PropertyDef> BOQProps(string elemType)
        {
            var props = new List<PropertyDef>();
            props.Add(Calc("Element_ID", "Element ID"));
            props.Add(Calc("Category"));
            props.Add(Calc("Family_And_Type", "Family and Type"));
            props.Add(Calc("Level"));
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.Add(BIP("Area", "HOST_AREA_COMPUTED", "Dimensions", true));
            props.Add(BIP("Volume", "HOST_VOLUME_COMPUTED", "Dimensions", true));
            props.Add(BIP("Length", "CURVE_ELEM_LENGTH", "Dimensions", true));
            props.Add(BIP("Description", "ALL_MODEL_DESCRIPTION", "Identity"));
            props.Add(BIP("Cost", "ALL_MODEL_COST", "Cost"));
            return props;
        }

        private static List<PropertyDef> QuickExportProps()
        {
            var props = new List<PropertyDef>();
            props.Add(Calc("Element_ID", "Element ID"));
            props.Add(Calc("Category"));
            props.Add(Calc("Family_And_Type", "Family and Type"));
            props.Add(Calc("Level"));
            props.AddRange(ISOTokenProps());
            props.Add(Sting(ParamRegistry.TAG1, "Tags", true));
            props.AddRange(LifecycleProps());
            return props;
        }
    }
}
