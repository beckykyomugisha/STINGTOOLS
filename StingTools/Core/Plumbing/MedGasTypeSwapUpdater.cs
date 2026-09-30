// StingTools — keep MGS_GAS_TYPE_TXT with the type of a STING medical-gas seed outlet (MG-1).
//
// The seed declares MGS_GAS_TYPE_TXT as an instance parameter and sets it per type,
// which makes the type's value the DEFAULT a new instance receives — after that it
// belongs to the instance. Swapping a placed outlet from TERMINAL_UNIT_O2 to
// TERMINAL_UNIT_VAC therefore kept "O2", and MgasNetwork, the TU tag and the flow
// solver all read the wrong gas. This updater watches type changes on Plumbing
// Fixtures and restamps the gas the NEW type declares in the seed spec
// (MedGasSeedTypeMap), clearing it for a type that declares none (AVSU, panels).
//
// Scope: STING's own seed family only (MedicalGasFixtures.IsSeedFamilyName). A
// manufacturer outlet's gas is never touched, and a user-duplicated type the spec
// does not declare is left alone.
//
// Registered at startup with its triggers attached (enabled): it only reacts to a
// type change on a Plumbing Fixture instance, and does nothing unless the family is
// the seed. SetEnabled(false) removes the triggers.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core.Plumbing
{
    public class MedGasTypeSwapUpdater : IUpdater
    {
        private const string GasParam = "MGS_GAS_TYPE_TXT";

        private static MedGasTypeSwapUpdater _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled;

        private MedGasTypeSwapUpdater(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId, new Guid("6C1E3B7A-94D2-4F0B-A8E5-2D7C9B1F4E63"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.MEPFixtures;
        public string GetUpdaterName() => "STING Medical Gas Type Swap";
        public string GetAdditionalInformation()
            => "Restamps MGS_GAS_TYPE_TXT on STING medical-gas seed outlets when their type changes.";

        public static bool IsEnabled => _enabled;

        /// <summary>Register at startup and attach the type-change triggers.</summary>
        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new MedGasTypeSwapUpdater(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);   // optional: no warning when the add-in is absent
                StingLog.Info("MedGasTypeSwapUpdater registered.");
                SetEnabled(true);
            }
            catch (Exception ex)
            {
                StingLog.Error("MedGasTypeSwapUpdater.Register", ex);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var filter = new LogicalAndFilter(
                        new ElementCategoryFilter(BuiltInCategory.OST_PlumbingFixtures),
                        new ElementClassFilter(typeof(FamilyInstance)));
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
                        catch (Exception ex) { StingLog.Warn($"MedGasTypeSwapUpdater trigger {bip}: {ex.Message}"); }
                    }
                    _enabled = added > 0;
                    StingLog.Info($"MedGasTypeSwapUpdater enabled ({added} trigger(s)).");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("MedGasTypeSwapUpdater disabled.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MedGasTypeSwapUpdater.SetEnabled", ex);
            }
        }

        public static void Unregister()
        {
            try
            {
                if (_instance != null && _updaterId != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
            }
            catch (Exception ex) { StingLog.Warn($"MedGasTypeSwapUpdater unregister failed: {ex.Message}"); }
        }

        public void Execute(UpdaterData data)
        {
            // Never let an exception escape: Revit disables an updater that throws.
            try
            {
                var doc = data.GetDocument();
                var map = MedicalGasFixtures.SeedTypes;
                if (map.IsEmpty) return;   // logged once when the map loaded

                var seen = new HashSet<long>();
                foreach (var id in data.GetModifiedElementIds())
                {
                    if (!seen.Add(id.Value)) continue;
                    try
                    {
                        if (!(doc.GetElement(id) is FamilyInstance fi)) continue;
                        var sym = fi.Symbol;
                        if (!MedicalGasFixtures.IsSeedFamily(sym)) continue;

                        var p = fi.LookupParameter(GasParam);
                        if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) continue;

                        if (map.GasAfterTypeChange(sym.Name, p.AsString(), out string newGas))
                        {
                            p.Set(newGas);
                            StingLog.Info($"MedGasTypeSwapUpdater: {fi.Id} now {sym.Name} — {GasParam} = '{newGas}'.");
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"MedGasTypeSwapUpdater element {id}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MedGasTypeSwapUpdater.Execute", ex);
            }
        }
    }
}
