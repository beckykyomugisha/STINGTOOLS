// TAG-01: Migration command — writes TAG_STYLE_CODE_TXT for all elements that
// currently have a TAG style BOOL set to true, without changing any BOOL values.
// Run once per project after upgrading to the v2 style-code parameter.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MigrateTagStyleCodeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;
            int migrated = 0;
            int alreadySet = 0;
            int noStyle = 0;

            try
            {
                string[] allStyleParams = ParamRegistry.AllTagStyleParams;

                // Collect all element types (BOOL params live on types, not instances)
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToList();

                using (var t = new Transaction(doc, "STING TAG-01 Migrate Tag Style Code"))
                {
                    t.Start();
                    foreach (Element el in types)
                    {
                        try
                        {
                            // Skip if already has a style code
                            string existing = ParameterHelpers.GetString(el, ParamRegistry.TAG_STYLE_CODE);
                            if (!string.IsNullOrEmpty(existing)) { alreadySet++; continue; }

                            // Find first true BOOL
                            string foundCode = null;
                            foreach (string pname in allStyleParams)
                            {
                                Parameter p = ParameterHelpers.CachedLookup(el, pname);
                                if (p == null) continue;
                                int val = 0;
                                if (p.StorageType == StorageType.Integer) val = p.AsInteger();
                                else if (p.StorageType == StorageType.String && int.TryParse(p.AsString(), out int sv)) val = sv;
                                if (val != 0)
                                {
                                    // Strip "TAG_" prefix and "_BOOL" suffix
                                    if (pname.StartsWith("TAG_") && pname.EndsWith("_BOOL"))
                                        foundCode = pname.Substring(4, pname.Length - 9);
                                    else
                                        foundCode = pname;
                                    break;
                                }
                            }

                            if (foundCode != null)
                            {
                                ParameterHelpers.SetString(el, ParamRegistry.TAG_STYLE_CODE, foundCode, overwrite: true);
                                migrated++;
                            }
                            else
                            {
                                noStyle++;
                            }
                        }
                        catch (Exception exEl) { StingLog.Warn($"MigrateTagStyleCode el {el.Id}: {exEl.Message}"); }
                    }
                    t.Commit();
                }

                TaskDialog.Show("TAG-01 Migration Complete",
                    $"TAG_STYLE_CODE_TXT migration finished.\n\n" +
                    $"Migrated: {migrated} element types\n" +
                    $"Already set: {alreadySet}\n" +
                    $"No style found: {noStyle}");

                StingLog.Info($"MigrateTagStyleCode: migrated={migrated}, alreadySet={alreadySet}, noStyle={noStyle}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MigrateTagStyleCodeCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
