using StingTools.Core;
// StingTools — Cable Manifest IUpdater.
//
// Watches conduit and cable-tray elements for deletion or geometry change
// and invalidates the RouteTrayIds in CableManifest for any StingCable
// entries that referenced those elements.
//
// Prevents stale manifest entries when conduits are manually deleted or
// rerouted by the user outside STING's auto-route commands.
//
// Registration: StingToolsApp.OnStartup → CableManifestUpdater.Register(app)
// Shutdown:     StingToolsApp.OnShutdown → CableManifestUpdater.Unregister()
//
// The updater is always-on (unlike StingAutoTagger which the user toggles):
// the cost is very low — only fires on conduit/tray delete or geometry change.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core.Electrical;

namespace StingTools.Core.Routing
{
    /// <summary>
    /// <see cref="IUpdater"/> that invalidates <see cref="StingCable.RouteTrayIds"/>
    /// in the cable manifest when conduit or cable-tray elements are deleted or
    /// their geometry changes.
    /// </summary>
    public class CableManifestUpdater : IUpdater
    {
        // ── Updater identity ─────────────────────────────────────────────────

        // StingTools addin GUID must match StingTools.addin / AssemblyInfo.cs
        private static readonly Guid AddinGuid   = new Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC");
        private static readonly Guid UpdaterGuid = new Guid("B2C3D4E5-6789-ABCD-EF01-23456789ABCD");

        private static readonly UpdaterId _updaterId =
            new UpdaterId(new AddInId(AddinGuid), UpdaterGuid);

        // Singleton instance — kept alive for Unregister()
        private static CableManifestUpdater _instance;

        // ── IUpdater implementation ──────────────────────────────────────────

        public UpdaterId GetUpdaterId()          => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.MEPSystems;
        public string GetUpdaterName()            => "STING CableManifest Sync";
        public string GetAdditionalInformation()  =>
            "Invalidates cable manifest RouteTrayIds on conduit/tray delete or geometry change";

        /// <summary>
        /// Called by Revit when conduit/tray elements are deleted or modified.
        /// Loads the manifest, clears RouteTrayIds for affected cables, saves.
        /// </summary>
        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data?.GetDocument();
                if (doc == null) return;

                // Collect all affected element IDs (modified + deleted)
                var affectedIds = new HashSet<long>();

                try
                {
                    foreach (var id in data.GetModifiedElementIds())
                        affectedIds.Add(id.Value);
                }
                catch { /* GetModifiedElementIds can throw if doc is in bad state */ }

                try
                {
                    foreach (var id in data.GetDeletedElementIds())
                        affectedIds.Add(id.Value);
                }
                catch { /* GetDeletedElementIds can throw if doc is in bad state */ }

                if (affectedIds.Count == 0) return;

                // Load manifest — returns empty manifest on any error
                CableManifest manifest;
                try
                {
                    manifest = CableManifest.Load(doc);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CableManifestUpdater: failed to load manifest — {ex.Message}");
                    return;
                }

                if (manifest.Cables == null || manifest.Cables.Count == 0) return;

                // Find cables that reference any of the affected element IDs
                int invalidated = 0;
                foreach (var cable in manifest.Cables)
                {
                    if (cable.RouteTrayIds == null || cable.RouteTrayIds.Count == 0)
                        continue;

                    bool hit = cable.RouteTrayIds.Any(id => affectedIds.Contains(id));
                    if (!hit) continue;

                    // Clear routing info so the cable is re-routed next time
                    cable.RouteTrayIds.Clear();
                    cable.VoltageDropPct = 0.0; // needs recalculation after re-route
                    invalidated++;
                }

                if (invalidated == 0) return;

                // Persist the updated manifest
                try
                {
                    manifest.Save(doc);
                    StingLog.Info($"CableManifestUpdater: invalidated RouteTrayIds on {invalidated} cable(s) due to conduit/tray change.");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"CableManifestUpdater: failed to save manifest after invalidation — {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                // IUpdater failures must not crash Revit — swallow and log
                StingLog.Warn($"CableManifestUpdater.Execute: unexpected error — {ex.Message}");
            }
        }

        // ── Static registration helpers ──────────────────────────────────────

        /// <summary>
        /// Registers the updater with Revit.
        /// Call from <c>StingToolsApp.OnStartup</c>.
        /// </summary>
        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new CableManifestUpdater();
                UpdaterRegistry.RegisterUpdater(_instance, true);

                // Trigger on conduit delete / geometry change
                var conduitFilter = new ElementCategoryFilter(BuiltInCategory.OST_Conduit);
                UpdaterRegistry.AddTrigger(_updaterId, conduitFilter,
                    Element.GetChangeTypeElementDeletion());
                UpdaterRegistry.AddTrigger(_updaterId, conduitFilter,
                    Element.GetChangeTypeGeometry());

                // Trigger on cable-tray delete / geometry change
                var trayFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTray);
                UpdaterRegistry.AddTrigger(_updaterId, trayFilter,
                    Element.GetChangeTypeElementDeletion());
                UpdaterRegistry.AddTrigger(_updaterId, trayFilter,
                    Element.GetChangeTypeGeometry());

                StingLog.Info("CableManifestUpdater: registered (conduit + cable-tray delete/geometry triggers).");
            }
            catch (Exception ex)
            {
                StingLog.Error("CableManifestUpdater.Register", ex);
            }
        }

        /// <summary>
        /// Unregisters the updater from Revit.
        /// Call from <c>StingToolsApp.OnShutdown</c>.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                if (_instance != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
                StingLog.Info("CableManifestUpdater: unregistered.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CableManifestUpdater.Unregister: {ex.Message}");
            }
        }
    }
}
