using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.ExLink
{
    // ════════════════════════════════════════════════════════════════════════
    //  ExLinkEngine — Native .link file parser and Revit data exchange engine
    //
    //  Parses Ideate-compatible .link UTF-16 XML files and executes them
    //  against the Revit API for bidirectional Excel data exchange.
    //
    //  Public API:
    //    ParseLinkFile(path)       → LinkDefinition
    //    CollectElements(doc, def) → List<Element>
    //    ExportToExcel(doc, def, outputPath) → ExportResult
    //    ImportFromExcel(doc, def, inputPath) → ImportResult
    //    GetAllLinkFiles()         → List<string>
    //    GetPropertyValue(doc, el, prop) → string
    //    SetPropertyValue(doc, el, prop, value) → bool
    // ════════════════════════════════════════════════════════════════════════

    #region ── Data Models ──

    internal class LinkDefinition
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ElementType { get; set; } = "";
        public string DataVersion { get; set; } = "";
        public List<PropertyDef> Properties { get; set; } = new();
        public List<FilterDef> Filters { get; set; } = new();
        public List<SortDef> SortFields { get; set; } = new();
    }

    internal class PropertyDef
    {
        public string Name { get; set; } = "";
        public string PropertyType { get; set; } = "";   // BUILT_IN_PARAMETER, CALCULATED_PROPERTY, SHARED_PARAMETER
        public string LookupType { get; set; } = "";      // INVALID, ELEMENT_PROPERTY, CALCULATED_PROPERTY, BUILT_IN_PARAMETER
        public string BuiltInName { get; set; } = "";      // e.g. ELEM_FAMILY_AND_TYPE_PARAM
        public string SharedParamGuid { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public string RelationshipPath { get; set; } = ""; // For ELEMENT_PROPERTY lookup chain

        // ── Extended fields for ExLink Property Discovery ──
        public string DisplayName { get; set; } = "";
        public string DataType { get; set; } = "";         // Text, Integer, Double, YesNo, ElementId
        public string SourceType { get; set; } = "";       // STING Shared, Revit Built-in, Project/Family, Calculated
        public string ParameterGroup { get; set; } = "";   // Source Tokens, Tag Containers, Identity, Dimensions, etc.
        public bool IsHidden { get; set; }
        public List<string> ValidationList { get; set; }   // Dropdown values for STING token columns in Excel
    }

    internal class FilterDef
    {
        public string PropertyName { get; set; } = "";
        public string Comparison { get; set; } = "";       // Equals, NotEquals, Contains, HasValue, HasNoValue, GreaterThan, LessThan, StartsWith, EndsWith
        public string Value { get; set; } = "";
        public string PropertyType { get; set; } = "";
        public string BuiltInName { get; set; } = "";
    }

    internal class SortDef
    {
        public string PropertyName { get; set; } = "";
        public bool Ascending { get; set; } = true;
        public string PropertyType { get; set; } = "";
        public string BuiltInName { get; set; } = "";
    }

    internal class ExportResult
    {
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public string OutputPath { get; set; } = "";
        public List<string> Warnings { get; set; } = new();
        public bool Success { get; set; }
    }

    internal class ImportResult
    {
        public int RowsRead { get; set; }
        public int RowsUpdated { get; set; }
        public int RowsSkipped { get; set; }
        public int PropertiesWritten { get; set; }
        public List<string> Warnings { get; set; } = new();
        public bool Success { get; set; }
    }

    #endregion

    internal static class ExLinkEngine
    {
        // ── Link file discovery ──

        public static List<string> GetAllLinkFiles()
        {
            var results = new List<string>();
            try
            {
                var dataPath = StingToolsApp.DataPath;
                if (string.IsNullOrEmpty(dataPath)) return results;

                var exlinkDir = Path.Combine(dataPath, "ExLink");
                if (Directory.Exists(exlinkDir))
                    results.AddRange(Directory.GetFiles(exlinkDir, "*.link", SearchOption.AllDirectories));

                // Also check parent data directory
                results.AddRange(Directory.GetFiles(dataPath, "*.link", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex) { StingLog.Warn($"GetAllLinkFiles: {ex.Message}"); }
            return results.Distinct().ToList();
        }

        public static List<UI.ExLinkBrowserDialog.LinkFileInfo> BrowseLinkFiles()
        {
            var files = GetAllLinkFiles();
            var infos = new List<UI.ExLinkBrowserDialog.LinkFileInfo>();
            foreach (var f in files)
            {
                try
                {
                    var def = ParseLinkFile(f);
                    infos.Add(new UI.ExLinkBrowserDialog.LinkFileInfo
                    {
                        FileName = Path.GetFileName(f),
                        FilePath = f,
                        ElementType = def.ElementType,
                        PropertyCount = def.Properties.Count,
                        FilterCount = def.Filters.Count,
                        DataVersion = def.DataVersion
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BrowseLinkFiles skip {f}: {ex.Message}");
                }
            }
            return infos;
        }

        // ── .link XML parser ──

        public static LinkDefinition ParseLinkFile(string path)
        {
            var def = new LinkDefinition
            {
                FilePath = path,
                FileName = Path.GetFileName(path)
            };

            var doc = new XmlDocument();
            // XmlDocument handles UTF-16 BOM automatically
            doc.Load(path);

            var linkNode = doc.SelectSingleNode("//application/links/link");
            if (linkNode == null)
                linkNode = doc.SelectSingleNode("//link");
            if (linkNode == null)
                throw new InvalidOperationException($"No <link> element found in {path}");

            // Element type
            var elemNode = linkNode.SelectSingleNode("elements/element_type")
                           ?? linkNode.SelectSingleNode("elements/type");
            if (elemNode != null)
                def.ElementType = elemNode.InnerText.Trim();

            // Data version
            var verAttr = linkNode.Attributes?["data_version"]
                          ?? linkNode.Attributes?["version"];
            if (verAttr != null) def.DataVersion = verAttr.Value;

            // Properties
            var propNodes = linkNode.SelectNodes("properties/property");
            if (propNodes != null)
            {
                foreach (XmlNode pn in propNodes)
                {
                    var prop = new PropertyDef
                    {
                        Name = GetAttr(pn, "name", ""),
                        PropertyType = GetAttr(pn, "property_type", GetAttr(pn, "type", "")),
                        LookupType = GetAttr(pn, "lookup_type", "INVALID"),
                        BuiltInName = GetAttr(pn, "revit_name", GetAttr(pn, "built_in_name", "")),
                        SharedParamGuid = GetAttr(pn, "shared_param_guid", GetAttr(pn, "guid", "")),
                        IsReadOnly = GetAttr(pn, "read_only", "false").Equals("true", StringComparison.OrdinalIgnoreCase),
                        RelationshipPath = GetAttr(pn, "relationship", "")
                    };
                    if (!string.IsNullOrEmpty(prop.Name))
                        def.Properties.Add(prop);
                }
            }

            // Filter properties
            var filterNodes = linkNode.SelectNodes("filter_properties/filter_property");
            if (filterNodes != null)
            {
                foreach (XmlNode fn in filterNodes)
                {
                    var filter = new FilterDef
                    {
                        PropertyName = GetAttr(fn, "name", ""),
                        Comparison = GetAttr(fn, "comparison", "Equals"),
                        Value = GetAttr(fn, "value", fn.InnerText?.Trim() ?? ""),
                        PropertyType = GetAttr(fn, "property_type", GetAttr(fn, "type", "")),
                        BuiltInName = GetAttr(fn, "revit_name", GetAttr(fn, "built_in_name", ""))
                    };
                    if (!string.IsNullOrEmpty(filter.PropertyName))
                        def.Filters.Add(filter);
                }
            }

            // Sort properties
            var sortNodes = linkNode.SelectNodes("sort_properties/sort_property");
            if (sortNodes != null)
            {
                foreach (XmlNode sn in sortNodes)
                {
                    var sort = new SortDef
                    {
                        PropertyName = GetAttr(sn, "name", ""),
                        Ascending = !GetAttr(sn, "direction", "ascending").Equals("descending", StringComparison.OrdinalIgnoreCase),
                        PropertyType = GetAttr(sn, "property_type", GetAttr(sn, "type", "")),
                        BuiltInName = GetAttr(sn, "revit_name", GetAttr(sn, "built_in_name", ""))
                    };
                    if (!string.IsNullOrEmpty(sort.PropertyName))
                        def.SortFields.Add(sort);
                }
            }

            return def;
        }

        private static string GetAttr(XmlNode node, string name, string fallback)
        {
            var attr = node?.Attributes?[name];
            return attr != null ? attr.Value : fallback;
        }

        // ── Element collection ──

        public static List<Element> CollectElements(Document doc, LinkDefinition def)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var elements = ApplyElementTypeFilter(collector, def.ElementType, doc);

            // Apply property-based filters
            if (def.Filters.Count > 0)
                elements = elements.Where(el => PassesAllFilters(doc, el, def.Filters)).ToList();

            // Apply sort
            if (def.SortFields.Count > 0)
                elements = ApplySorting(doc, elements, def.SortFields);

            return elements;
        }

        private static List<Element> ApplyElementTypeFilter(FilteredElementCollector collector, string elementType, Document doc)
        {
            if (string.IsNullOrEmpty(elementType))
                return collector.ToList();

            var type = elementType.Trim();

            // Map common element type strings to Revit categories/classes
            switch (type.ToUpperInvariant())
            {
                case "WALLS":
                case "WALL":
                    return collector.OfCategory(BuiltInCategory.OST_Walls).ToList();
                case "DOORS":
                case "DOOR":
                    return collector.OfCategory(BuiltInCategory.OST_Doors).ToList();
                case "WINDOWS":
                case "WINDOW":
                    return collector.OfCategory(BuiltInCategory.OST_Windows).ToList();
                case "ROOMS":
                case "ROOM":
                    return collector.OfCategory(BuiltInCategory.OST_Rooms).ToList();
                case "FLOORS":
                case "FLOOR":
                    return collector.OfCategory(BuiltInCategory.OST_Floors).ToList();
                case "CEILINGS":
                case "CEILING":
                    return collector.OfCategory(BuiltInCategory.OST_Ceilings).ToList();
                case "ROOFS":
                case "ROOF":
                    return collector.OfCategory(BuiltInCategory.OST_Roofs).ToList();
                case "COLUMNS":
                case "COLUMN":
                case "STRUCTURAL COLUMNS":
                    return collector.OfCategory(BuiltInCategory.OST_StructuralColumns).ToList();
                case "BEAMS":
                case "BEAM":
                case "STRUCTURAL FRAMING":
                    return collector.OfCategory(BuiltInCategory.OST_StructuralFraming).ToList();
                case "FOUNDATIONS":
                case "STRUCTURAL FOUNDATIONS":
                    return collector.OfCategory(BuiltInCategory.OST_StructuralFoundation).ToList();
                case "FURNITURE":
                    return collector.OfCategory(BuiltInCategory.OST_Furniture).ToList();
                case "MECHANICAL EQUIPMENT":
                case "MECHANICAL_EQUIPMENT":
                    return collector.OfCategory(BuiltInCategory.OST_MechanicalEquipment).ToList();
                case "ELECTRICAL EQUIPMENT":
                case "ELECTRICAL_EQUIPMENT":
                    return collector.OfCategory(BuiltInCategory.OST_ElectricalEquipment).ToList();
                case "ELECTRICAL FIXTURES":
                case "ELECTRICAL_FIXTURES":
                    return collector.OfCategory(BuiltInCategory.OST_ElectricalFixtures).ToList();
                case "LIGHTING FIXTURES":
                case "LIGHTING_FIXTURES":
                    return collector.OfCategory(BuiltInCategory.OST_LightingFixtures).ToList();
                case "PLUMBING FIXTURES":
                case "PLUMBING_FIXTURES":
                    return collector.OfCategory(BuiltInCategory.OST_PlumbingFixtures).ToList();
                case "SPRINKLERS":
                    return collector.OfCategory(BuiltInCategory.OST_Sprinklers).ToList();
                case "DUCTS":
                case "DUCT":
                    return collector.OfCategory(BuiltInCategory.OST_DuctCurves).ToList();
                case "PIPES":
                case "PIPE":
                    return collector.OfCategory(BuiltInCategory.OST_PipeCurves).ToList();
                case "CONDUITS":
                case "CONDUIT":
                    return collector.OfCategory(BuiltInCategory.OST_Conduit).ToList();
                case "CABLE TRAYS":
                case "CABLE_TRAYS":
                    return collector.OfCategory(BuiltInCategory.OST_CableTray).ToList();
                case "AIR TERMINALS":
                case "AIR_TERMINALS":
                    return collector.OfCategory(BuiltInCategory.OST_DuctTerminal).ToList();
                case "FIRE ALARM DEVICES":
                case "FIRE_ALARM_DEVICES":
                    return collector.OfCategory(BuiltInCategory.OST_FireAlarmDevices).ToList();
                case "COMMUNICATION DEVICES":
                case "COMMUNICATION_DEVICES":
                    return collector.OfCategory(BuiltInCategory.OST_CommunicationDevices).ToList();
                case "SECURITY DEVICES":
                case "SECURITY_DEVICES":
                    return collector.OfCategory(BuiltInCategory.OST_SecurityDevices).ToList();
                case "STAIRS":
                    return collector.OfCategory(BuiltInCategory.OST_Stairs).ToList();
                case "RAILINGS":
                    return collector.OfCategory(BuiltInCategory.OST_StairsRailing).ToList();
                case "CURTAIN WALLS":
                case "CURTAIN_WALLS":
                    return collector.OfCategory(BuiltInCategory.OST_CurtainWallPanels).ToList();
                case "CURTAIN WALL MULLIONS":
                    return collector.OfCategory(BuiltInCategory.OST_CurtainWallMullions).ToList();
                case "GENERIC MODELS":
                case "GENERIC_MODELS":
                    return collector.OfCategory(BuiltInCategory.OST_GenericModel).ToList();
                case "SPECIALTY EQUIPMENT":
                case "SPECIALTY_EQUIPMENT":
                    return collector.OfCategory(BuiltInCategory.OST_SpecialityEquipment).ToList();
                case "PIPE FITTINGS":
                case "PIPE_FITTINGS":
                    return collector.OfCategory(BuiltInCategory.OST_PipeFitting).ToList();
                case "DUCT FITTINGS":
                case "DUCT_FITTINGS":
                    return collector.OfCategory(BuiltInCategory.OST_DuctFitting).ToList();
                case "PIPE ACCESSORIES":
                case "PIPE_ACCESSORIES":
                    return collector.OfCategory(BuiltInCategory.OST_PipeAccessory).ToList();
                case "DUCT ACCESSORIES":
                case "DUCT_ACCESSORIES":
                    return collector.OfCategory(BuiltInCategory.OST_DuctAccessory).ToList();
                case "FLEX DUCTS":
                case "FLEX_DUCTS":
                    return collector.OfCategory(BuiltInCategory.OST_FlexDuctCurves).ToList();
                case "FLEX PIPES":
                case "FLEX_PIPES":
                    return collector.OfCategory(BuiltInCategory.OST_FlexPipeCurves).ToList();
                case "AREAS":
                case "AREA":
                    return collector.OfCategory(BuiltInCategory.OST_Areas).ToList();
                case "SPACES":
                case "SPACE":
                    return collector.OfCategory(BuiltInCategory.OST_MEPSpaces).ToList();
                case "PARKING":
                    return collector.OfCategory(BuiltInCategory.OST_Parking).ToList();
                case "SHEETS":
                case "SHEET":
                    return new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).ToList();
                case "VIEWS":
                case "VIEW":
                    return new FilteredElementCollector(doc).OfClass(typeof(View))
                        .Cast<View>().Where(v => !v.IsTemplate).Cast<Element>().ToList();
                default:
                    StingLog.Warn($"ExLinkEngine: Unknown element type '{type}', collecting all non-type elements");
                    return collector.ToList();
            }
        }

        // ── Property value read ──

        public static string GetPropertyValue(Document doc, Element el, PropertyDef prop)
        {
            try
            {
                // 1. Try built-in parameter by name
                if (!string.IsNullOrEmpty(prop.BuiltInName) &&
                    Enum.TryParse<BuiltInParameter>(prop.BuiltInName, out var bip))
                {
                    var p = el.get_Parameter(bip);
                    if (p != null) return GetParamDisplayValue(p);
                }

                // 2. Try shared parameter by GUID
                if (!string.IsNullOrEmpty(prop.SharedParamGuid) &&
                    Guid.TryParse(prop.SharedParamGuid, out var guid))
                {
                    var p = el.get_Parameter(guid);
                    if (p != null) return GetParamDisplayValue(p);
                }

                // 3. Try by display name
                if (!string.IsNullOrEmpty(prop.Name))
                {
                    var p = el.LookupParameter(prop.Name);
                    if (p != null) return GetParamDisplayValue(p);
                }

                // 4. Calculated properties
                if (prop.PropertyType == "CALCULATED_PROPERTY" || prop.LookupType == "CALCULATED_PROPERTY")
                    return GetCalculatedProperty(doc, el, prop.Name);

                // 5. Element property (follow relationship)
                if (prop.LookupType == "ELEMENT_PROPERTY" && !string.IsNullOrEmpty(prop.RelationshipPath))
                    return GetRelatedPropertyValue(doc, el, prop);
            }
            catch (Exception ex) { StingLog.Warn($"GetPropertyValue '{prop.Name}': {ex.Message}"); }
            return "";
        }

        private static string GetParamDisplayValue(Parameter p)
        {
            if (!p.HasValue) return "";
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? "";
                case StorageType.Integer: return p.AsInteger().ToString();
                case StorageType.Double: return p.AsValueString() ?? p.AsDouble().ToString("F4");
                case StorageType.ElementId:
                    var id = p.AsElementId();
                    if (id == ElementId.InvalidElementId) return "";
                    var elem = p.Element?.Document?.GetElement(id);
                    return elem?.Name ?? id.ToString();
                default: return "";
            }
        }

        private static string GetCalculatedProperty(Document doc, Element el, string propName)
        {
            var upper = (propName ?? "").ToUpperInvariant().Replace(" ", "_");
            switch (upper)
            {
                case "ELEMENT_ID":
                case "ID":
                    return el.Id.ToString();
                case "UNIQUE_ID":
                case "UNIQUEID":
                    return el.UniqueId;
                case "CATEGORY":
                    return el.Category?.Name ?? "";
                case "FAMILY":
                case "FAMILY_NAME":
                    return (el as FamilyInstance)?.Symbol?.FamilyName
                           ?? el.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
                case "TYPE":
                case "TYPE_NAME":
                    return el.Name ?? "";
                case "FAMILY_AND_TYPE":
                    return el.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM)?.AsValueString() ?? "";
                case "LEVEL":
                    var lvlId = el.LevelId;
                    if (lvlId != null && lvlId != ElementId.InvalidElementId)
                        return doc.GetElement(lvlId)?.Name ?? "";
                    return el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)?.AsValueString() ?? "";
                case "PHASE_CREATED":
                    return el.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsValueString() ?? "";
                case "PHASE_DEMOLISHED":
                    return el.get_Parameter(BuiltInParameter.PHASE_DEMOLISHED)?.AsValueString() ?? "";
                case "DESIGN_OPTION":
                    return el.DesignOption?.Name ?? "";
                case "WORKSET":
                    if (doc.IsWorkshared)
                    {
                        var wsParam = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        return wsParam?.AsValueString() ?? "";
                    }
                    return "";
                default:
                    return "";
            }
        }

        private static string GetRelatedPropertyValue(Document doc, Element el, PropertyDef prop)
        {
            // Follow relationship chain (e.g., Room → Level)
            var parts = prop.RelationshipPath.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            Element current = el;
            foreach (var part in parts)
            {
                if (current == null) return "";
                var p = current.LookupParameter(part);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var nextId = p.AsElementId();
                    if (nextId == ElementId.InvalidElementId) return "";
                    current = doc.GetElement(nextId);
                }
                else if (p != null)
                {
                    return GetParamDisplayValue(p);
                }
                else return "";
            }
            return current?.Name ?? "";
        }

        // ── Property value write ──

        public static bool SetPropertyValue(Document doc, Element el, PropertyDef prop, string value)
        {
            if (prop.IsReadOnly) return false;
            try
            {
                Parameter param = null;

                // 1. Try built-in parameter
                if (!string.IsNullOrEmpty(prop.BuiltInName) &&
                    Enum.TryParse<BuiltInParameter>(prop.BuiltInName, out var bip))
                    param = el.get_Parameter(bip);

                // 2. Try shared parameter by GUID
                if (param == null && !string.IsNullOrEmpty(prop.SharedParamGuid) &&
                    Guid.TryParse(prop.SharedParamGuid, out var guid))
                    param = el.get_Parameter(guid);

                // 3. Try by name
                if (param == null && !string.IsNullOrEmpty(prop.Name))
                    param = el.LookupParameter(prop.Name);

                if (param == null || param.IsReadOnly) return false;

                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(value ?? "");
                        return true;
                    case StorageType.Integer:
                        if (int.TryParse(value, out var iv)) { param.Set(iv); return true; }
                        return false;
                    case StorageType.Double:
                        if (double.TryParse(value, out var dv)) { param.Set(dv); return true; }
                        return false;
                    case StorageType.ElementId:
                        if (long.TryParse(value, out var eid))
                        {
                            param.Set(new ElementId(eid));
                            return true;
                        }
                        return false;
                    default: return false;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetPropertyValue '{prop.Name}': {ex.Message}");
                return false;
            }
        }

        // ── Filter evaluation ──

        private static bool PassesAllFilters(Document doc, Element el, List<FilterDef> filters)
        {
            foreach (var filter in filters)
            {
                var propDef = new PropertyDef
                {
                    Name = filter.PropertyName,
                    PropertyType = filter.PropertyType,
                    BuiltInName = filter.BuiltInName
                };
                var actual = GetPropertyValue(doc, el, propDef);
                if (!EvaluateComparison(actual, filter.Comparison, filter.Value))
                    return false;
            }
            return true;
        }

        private static bool EvaluateComparison(string actual, string comparison, string expected)
        {
            var a = actual ?? "";
            var e = expected ?? "";
            switch (comparison)
            {
                case "Equals": return a.Equals(e, StringComparison.OrdinalIgnoreCase);
                case "NotEquals": return !a.Equals(e, StringComparison.OrdinalIgnoreCase);
                case "Contains": return a.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0;
                case "HasValue": return !string.IsNullOrWhiteSpace(a);
                case "HasNoValue": return string.IsNullOrWhiteSpace(a);
                case "GreaterThan":
                    return double.TryParse(a, out var ga) && double.TryParse(e, out var ge) && ga > ge;
                case "LessThan":
                    return double.TryParse(a, out var la) && double.TryParse(e, out var le) && la < le;
                case "StartsWith": return a.StartsWith(e, StringComparison.OrdinalIgnoreCase);
                case "EndsWith": return a.EndsWith(e, StringComparison.OrdinalIgnoreCase);
                case "GreaterThanOrEqual":
                    return double.TryParse(a, out var gea) && double.TryParse(e, out var gee) && gea >= gee;
                case "LessThanOrEqual":
                    return double.TryParse(a, out var lea) && double.TryParse(e, out var lee) && lea <= lee;
                default:
                    StingLog.Warn($"Unknown comparison '{comparison}', defaulting to Contains");
                    return a.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        // ── Sort ──

        private static List<Element> ApplySorting(Document doc, List<Element> elements, List<SortDef> sortFields)
        {
            IOrderedEnumerable<Element> ordered = null;
            for (int i = 0; i < sortFields.Count; i++)
            {
                var sf = sortFields[i];
                var propDef = new PropertyDef
                {
                    Name = sf.PropertyName,
                    PropertyType = sf.PropertyType,
                    BuiltInName = sf.BuiltInName
                };
                if (i == 0)
                {
                    ordered = sf.Ascending
                        ? elements.OrderBy(el => GetPropertyValue(doc, el, propDef), StringComparer.OrdinalIgnoreCase)
                        : elements.OrderByDescending(el => GetPropertyValue(doc, el, propDef), StringComparer.OrdinalIgnoreCase);
                }
                else if (ordered != null)
                {
                    var sf2 = sf;
                    var pd2 = propDef;
                    ordered = sf2.Ascending
                        ? ordered.ThenBy(el => GetPropertyValue(doc, el, pd2), StringComparer.OrdinalIgnoreCase)
                        : ordered.ThenByDescending(el => GetPropertyValue(doc, el, pd2), StringComparer.OrdinalIgnoreCase);
                }
            }
            return ordered?.ToList() ?? elements;
        }

        // ── Excel export ──

        public static ExportResult ExportToExcel(Document doc, LinkDefinition def, string outputPath)
        {
            var result = new ExportResult { OutputPath = outputPath };
            try
            {
                var elements = CollectElements(doc, def);
                if (elements.Count == 0)
                {
                    result.Warnings.Add("No elements matched the link definition filters.");
                    result.Success = true;
                    return result;
                }

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add(SanitizeSheetName(def.FileName));

                // Header row
                for (int c = 0; c < def.Properties.Count; c++)
                    ws.Cell(1, c + 1).Value = def.Properties[c].Name;

                // Style header
                var headerRange = ws.Range(1, 1, 1, def.Properties.Count);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#6A1B9A");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

                // Data rows
                for (int r = 0; r < elements.Count; r++)
                {
                    var el = elements[r];
                    for (int c = 0; c < def.Properties.Count; c++)
                    {
                        var val = GetPropertyValue(doc, el, def.Properties[c]);
                        ws.Cell(r + 2, c + 1).Value = val;
                    }
                }

                // Auto-fit columns (cap at 50 chars width)
                ws.Columns().AdjustToContents(1, 50);

                // Add data validation dropdowns for columns with ValidationList
                for (int c = 0; c < def.Properties.Count; c++)
                {
                    var vList = def.Properties[c].ValidationList;
                    if (vList != null && vList.Count > 0 && elements.Count > 0)
                    {
                        var dataRange = ws.Range(2, c + 1, elements.Count + 1, c + 1);
                        var validation = dataRange.CreateDataValidation();
                        validation.AllowedValues = ClosedXML.Excel.XLAllowedValues.List;
                        validation.List($"\"{string.Join(",", vList)}\"");
                        validation.IgnoreBlanks = true;
                        validation.ShowInputMessage = true;
                        validation.InputTitle = def.Properties[c].DisplayName ?? def.Properties[c].Name;
                        validation.InputMessage = $"Select from {vList.Count} valid codes";
                    }
                }

                // Add hidden ElementId column for import round-trip
                var idCol = def.Properties.Count + 1;
                ws.Cell(1, idCol).Value = "__ElementId__";
                for (int r = 0; r < elements.Count; r++)
                    ws.Cell(r + 2, idCol).Value = elements[r].Id.ToString();
                ws.Column(idCol).Hide();

                wb.SaveAs(outputPath);

                result.RowCount = elements.Count;
                result.ColumnCount = def.Properties.Count;
                result.Success = true;
                StingLog.Info($"ExLink export: {elements.Count} rows × {def.Properties.Count} cols → {outputPath}");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Export failed: {ex.Message}");
                StingLog.Error($"ExLink export error: {ex.Message}", ex);
            }
            return result;
        }

        private static string SanitizeSheetName(string name)
        {
            var clean = Path.GetFileNameWithoutExtension(name ?? "Sheet1");
            foreach (var c in new[] { '\\', '/', '?', '*', '[', ']', ':' })
                clean = clean.Replace(c, '_');
            return clean.Length > 31 ? clean.Substring(0, 31) : clean;
        }

        // ── Excel import ──

        public static ImportResult ImportFromExcel(Document doc, LinkDefinition def, string inputPath)
        {
            var result = new ImportResult();
            try
            {
                using var wb = new ClosedXML.Excel.XLWorkbook(inputPath);
                var ws = wb.Worksheet(1);
                var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
                var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;

                // Build header map
                var headers = new Dictionary<int, string>();
                for (int c = 1; c <= lastCol; c++)
                    headers[c] = ws.Cell(1, c).GetString().Trim();

                // Find ElementId column
                int idCol = -1;
                foreach (var kv in headers)
                {
                    if (kv.Value == "__ElementId__")
                    {
                        idCol = kv.Key;
                        break;
                    }
                }

                // Map columns to properties
                var colToProps = new Dictionary<int, PropertyDef>();
                foreach (var kv in headers)
                {
                    if (kv.Key == idCol) continue;
                    var prop = def.Properties.FirstOrDefault(p =>
                        p.Name.Equals(kv.Value, StringComparison.OrdinalIgnoreCase));
                    if (prop != null && !prop.IsReadOnly)
                        colToProps[kv.Key] = prop;
                }

                if (colToProps.Count == 0)
                {
                    result.Warnings.Add("No writable property columns matched the link definition.");
                    result.Success = true;
                    return result;
                }

                // Process rows
                for (int r = 2; r <= lastRow; r++)
                {
                    result.RowsRead++;

                    // Resolve element
                    Element el = null;
                    if (idCol > 0)
                    {
                        var idStr = ws.Cell(r, idCol).GetString().Trim();
                        if (long.TryParse(idStr, out var eid))
                            el = doc.GetElement(new ElementId(eid));
                    }

                    if (el == null)
                    {
                        result.RowsSkipped++;
                        continue;
                    }

                    bool anyWritten = false;
                    foreach (var kv in colToProps)
                    {
                        var val = ws.Cell(r, kv.Key).GetString();
                        if (SetPropertyValue(doc, el, kv.Value, val))
                        {
                            result.PropertiesWritten++;
                            anyWritten = true;
                        }
                    }

                    if (anyWritten) result.RowsUpdated++;
                    else result.RowsSkipped++;
                }

                result.Success = true;
                StingLog.Info($"ExLink import: {result.RowsRead} rows read, {result.RowsUpdated} updated, {result.PropertiesWritten} props written");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Import failed: {ex.Message}");
                StingLog.Error($"ExLink import error: {ex.Message}", ex);
            }
            return result;
        }

        // ── QTO (Quantity Take-Off) ──

        public static ExportResult ExportQTO(Document doc, string outputPath)
        {
            var result = new ExportResult { OutputPath = outputPath };
            try
            {
                var categories = new[]
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors,
                    BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings,
                    BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
                    BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_StructuralFoundation,
                    BuiltInCategory.OST_Rooms, BuiltInCategory.OST_Stairs
                };

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add("Quantity Take-Off");

                var headers = new[] { "Category", "Family", "Type", "Count", "Area (m²)", "Volume (m³)", "Length (m)" };
                for (int c = 0; c < headers.Length; c++)
                    ws.Cell(1, c + 1).Value = headers[c];

                var headerRange = ws.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

                int row = 2;
                int totalElements = 0;
                foreach (var cat in categories)
                {
                    var elements = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToList();
                    if (elements.Count == 0) continue;

                    // Group by type
                    var groups = elements.GroupBy(e =>
                    {
                        var famType = e.get_Parameter(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM)?.AsValueString() ?? "";
                        return famType;
                    });

                    foreach (var g in groups.OrderBy(g => g.Key))
                    {
                        double area = 0, volume = 0, length = 0;
                        foreach (var e in g)
                        {
                            var aParam = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                            if (aParam != null && aParam.HasValue) area += aParam.AsDouble() * 0.092903; // sqft to m²

                            var vParam = e.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                            if (vParam != null && vParam.HasValue) volume += vParam.AsDouble() * 0.0283168; // cuft to m³

                            var lParam = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                            if (lParam != null && lParam.HasValue) length += lParam.AsDouble() * 0.3048; // ft to m
                        }

                        var parts = g.Key.Split(new[] { ':' }, 2);
                        var family = parts.Length > 0 ? parts[0].Trim() : "";
                        var type = parts.Length > 1 ? parts[1].Trim() : "";

                        ws.Cell(row, 1).Value = doc.Settings.Categories.get_Item(cat)?.Name ?? cat.ToString();
                        ws.Cell(row, 2).Value = family;
                        ws.Cell(row, 3).Value = type;
                        ws.Cell(row, 4).Value = g.Count();
                        ws.Cell(row, 5).Value = Math.Round(area, 2);
                        ws.Cell(row, 6).Value = Math.Round(volume, 3);
                        ws.Cell(row, 7).Value = Math.Round(length, 2);
                        totalElements += g.Count();
                        row++;
                    }
                }

                ws.Columns().AdjustToContents(1, 40);
                wb.SaveAs(outputPath);

                result.RowCount = row - 2;
                result.ColumnCount = headers.Length;
                result.Success = true;
                StingLog.Info($"QTO export: {totalElements} elements, {result.RowCount} type groups → {outputPath}");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"QTO export failed: {ex.Message}");
                StingLog.Error($"QTO export error: {ex.Message}", ex);
            }
            return result;
        }
    }
}
