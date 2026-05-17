// StingBridge — External event handler for drop-folder IFC imports.
//
// When IfcDropWatcher detects a new .ifc file on a background thread,
// it raises an ExternalEvent so the actual import runs on the Revit
// API main thread — required by the Revit threading model.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingBridge.IFC
{
    /// <summary>
    /// Thin IExternalEventHandler wrapper that runs IfcRevitImporter.Import
    /// for a single file path on the Revit UI thread.
    /// </summary>
    public sealed class DropFolderImportEventHandler : IExternalEventHandler
    {
        private readonly Document _doc;
        private readonly string   _ifcPath;

        public DropFolderImportEventHandler(Document doc, string ifcPath)
        {
            _doc     = doc;
            _ifcPath = ifcPath;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = _doc ?? app.ActiveUIDocument?.Document;
                if (doc == null || doc.IsReadOnly)
                {
                    StingLog.Warn($"DropFolderImportEventHandler: document unavailable for {System.IO.Path.GetFileName(_ifcPath)}");
                    return;
                }

                StingLog.Info($"DropFolderImportEventHandler: importing {System.IO.Path.GetFileName(_ifcPath)}");
                var result = IfcRevitImporter.Import(doc, _ifcPath, IfcImportMode.Import, applyTags: true);

                if (!result.Success)
                    StingLog.Warn($"DropFolderImportEventHandler: import failed — {result.ErrorMessage}");
                else
                    StingLog.Info($"DropFolderImportEventHandler: imported {result.ElementsTagged} elements from {System.IO.Path.GetFileName(_ifcPath)}");
            }
            catch (Exception ex)
            {
                StingLog.Error("DropFolderImportEventHandler.Execute", ex);
            }
        }

        public string GetName() => "STING Drop-Folder IFC Import";
    }
}
