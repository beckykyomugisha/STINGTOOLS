// StingTools — SLD sync updater (Phase 175)
//
// IUpdater that re-runs SLDGenerator.UpdateSLD on every drafting view
// whose name starts with "STING - SLD" whenever an electrical element
// changes. Registered in StingToolsApp.OnStartup behind a project_config
// gate ("sld_sync_enabled").

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;

namespace StingTools.Core.SLD
{
    public class SLDSyncUpdater : IUpdater
    {
        private readonly UpdaterId _updaterId;

        public SLDSyncUpdater(AddInId addInId)
        {
            // Stable GUID for this updater — used to register/unregister.
            _updaterId = new UpdaterId(addInId,
                new Guid("E6E3FD2A-4E9B-4F7B-9C42-0B7D1F9E4B17"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING SLD Sync Updater";
        public string GetAdditionalInformation() => "Keeps STING SLD views in sync with electrical model changes.";
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null) return;

                var changed = new HashSet<ElementId>(data.GetModifiedElementIds());
                foreach (var id in data.GetAddedElementIds()) changed.Add(id);
                foreach (var id in data.GetDeletedElementIds()) changed.Add(id);

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => v.Name?.StartsWith("STING - SLD",
                        StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                if (views.Count == 0) return;

                foreach (var v in views)
                {
                    foreach (var id in changed)
                        SLDGenerator.UpdateSLD(doc, v, id);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SLDSyncUpdater: {ex.Message}");
            }
        }
    }
}
