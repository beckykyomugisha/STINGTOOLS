// StingTools — keep a STING seed element's "followsType" values with its type.
//
// A seed declares some INSTANCE parameters per type (ASS_PRODCT_COD_TXT,
// MGS_GAS_TYPE_TXT, PEN_FIRE_RATING_TXT, ELC_PNL_MAIN_BRK_A, ... — every parameter
// marked "followsType": true in Data/Seeds/*.json). Revit makes a type's value of an
// instance parameter only the default a NEW instance receives, so swapping a placed
// outlet from TERMINAL_UNIT_O2 to TERMINAL_UNIT_VAC kept "O2", a fire damper swapped
// FR60 -> FR120 kept "FR60", and MgasNetwork / the tags / the validators read stale
// values. This updater watches type changes on family instances and restamps each
// followsType value from the NEW type's declaration in the seed spec
// (SeedFollowTypeCatalog), under SeedFollowTypeRule: only over a blank or a value the
// seed itself declares. Revit reports the change after the fact, so the old type is
// not known here; any value the seed declares for the parameter counts as "untouched".
// A value a user typed survives.
//
// Scope: STING seed families only (family name = a seed id). A manufacturer family
// is never touched, nor a type the seed does not declare (a user's duplicate).
// Supersedes MedGasTypeSwapUpdater (MG-1), which did this for the gas alone.
//
// Registered at startup with its triggers attached (enabled): it reacts only to a
// type change on a family instance and returns at once for a non-seed family.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core.Symbols
{
    public class SeedTypeSwapUpdater : IUpdater
    {
        private static SeedTypeSwapUpdater _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled;
        private static int _suspended;

        private static SeedFollowTypeCatalog _catalog;
        private static readonly object _catalogLock = new object();

        private SeedTypeSwapUpdater(AddInId addInId)
        {
            // The MedGasTypeSwapUpdater's id, kept: one updater replaced another, so a
            // model that recorded the old one does not also learn of a second.
            _updaterId = new UpdaterId(addInId, new Guid("6C1E3B7A-94D2-4F0B-A8E5-2D7C9B1F4E63"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
        public string GetUpdaterName() => "STING Seed Type Swap";
        public string GetAdditionalInformation()
            => "Restamps a STING seed element's per-type instance values (followsType) when its type changes.";

        public static bool IsEnabled => _enabled;

        /// <summary>The followsType values of every shipped seed, read once.</summary>
        public static SeedFollowTypeCatalog Catalog
        {
            get
            {
                if (_catalog != null) return _catalog;
                lock (_catalogLock)
                {
                    if (_catalog == null)
                    {
                        string dir = null;
                        try
                        {
                            if (!string.IsNullOrEmpty(StingToolsApp.DataPath))
                                dir = Path.Combine(StingToolsApp.DataPath, "Seeds");
                            if (dir == null || !Directory.Exists(dir))
                            {
                                var any = StingToolsApp.FindDataFile("STING_SEED_MedGasOutlet.json");
                                if (!string.IsNullOrEmpty(any)) dir = Path.GetDirectoryName(any);
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"SeedTypeSwapUpdater seed folder: {ex.Message}"); }
                        _catalog = SeedFollowTypeCatalog.LoadDirectory(dir);
                    }
                    return _catalog;
                }
            }
        }

        /// <summary>
        /// While disposed-of scope is open, Execute does nothing. For a caller that has
        /// already applied the rule itself with more knowledge (SeedTypeMigrator knows the
        /// old type), so the updater does not re-decide the same elements at commit.
        /// </summary>
        public static IDisposable Suspend()
        {
            Interlocked.Increment(ref _suspended);
            return new Resume();
        }

        private sealed class Resume : IDisposable
        {
            private int _done;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref _suspended);
            }
        }

        /// <summary>Register at startup and attach the type-change triggers.</summary>
        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new SeedTypeSwapUpdater(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);   // optional: no warning when the add-in is absent
                StingLog.Info("SeedTypeSwapUpdater registered.");
                SetEnabled(true);
            }
            catch (Exception ex)
            {
                StingLog.Error("SeedTypeSwapUpdater.Register", ex);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    // Every category: seeds span a dozen of them, and the trigger is a type
                    // change only, which is rare and cheap to test (one family-name lookup).
                    var filter = new ElementClassFilter(typeof(FamilyInstance));
                    int added = 0;
                    // Which of these a type swap reports varies by how it was made
                    // (Properties palette, Change Type, API); each is cheap to watch.
                    foreach (var bip in new[] { BuiltInParameter.ELEM_TYPE_PARAM,
                                                BuiltInParameter.SYMBOL_ID_PARAM,
                                                BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM })
                    {
                        try
                        {
                            UpdaterRegistry.AddTrigger(_updaterId, filter,
                                Element.GetChangeTypeParameter(new ElementId(bip)));
                            added++;
                        }
                        catch (Exception ex) { StingLog.Warn($"SeedTypeSwapUpdater trigger {bip}: {ex.Message}"); }
                    }
                    _enabled = added > 0;
                    StingLog.Info($"SeedTypeSwapUpdater enabled ({added} trigger(s)).");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("SeedTypeSwapUpdater disabled.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("SeedTypeSwapUpdater.SetEnabled", ex);
            }
        }

        public static void Unregister()
        {
            try
            {
                if (_instance != null && _updaterId != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
            }
            catch (Exception ex) { StingLog.Warn($"SeedTypeSwapUpdater unregister failed: {ex.Message}"); }
        }

        public void Execute(UpdaterData data)
        {
            // Never let an exception escape: Revit disables an updater that throws.
            try
            {
                if (Volatile.Read(ref _suspended) > 0) return;
                var catalog = Catalog;
                if (catalog.IsEmpty) return;   // logged once when the catalog loaded

                var doc = data.GetDocument();
                var seen = new HashSet<long>();
                foreach (var id in data.GetModifiedElementIds())
                {
                    if (!seen.Add(id.Value)) continue;
                    try
                    {
                        if (!(doc.GetElement(id) is FamilyInstance fi)) continue;
                        var sym = fi.Symbol;
                        string family = sym?.Family?.Name;
                        if (catalog.SeedIdForFamily(family) == null) continue;   // not a STING seed

                        int n = SeedFollowTypeApplier.Apply(fi, family, sym.Name, catalog, null,
                            w => StingLog.Warn($"SeedTypeSwapUpdater: {w}"));
                        if (n > 0)
                            StingLog.Info($"SeedTypeSwapUpdater: {fi.Id} now {family} : {sym.Name} — {n} value(s) restamped.");
                    }
                    catch (Exception ex) { StingLog.Warn($"SeedTypeSwapUpdater element {id}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("SeedTypeSwapUpdater.Execute", ex);
            }
        }
    }
}
