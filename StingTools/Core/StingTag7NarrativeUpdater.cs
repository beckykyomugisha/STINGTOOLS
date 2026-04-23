using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Tags;

namespace StingTools.Core
{
    /// <summary>
    /// IUpdater that refreshes ASS_TAG_7_TXT on every tagged element whenever
    /// a source parameter referenced by the active paragraph preset changes.
    /// This replaces the old "21 label rows per tag family" approach: tag
    /// families now carry a single ASS_TAG_7_TXT label, and this updater keeps
    /// the composed narrative in sync with live parameter values.
    ///
    /// Preset selection mirrors ApplyParagraphPresetCommand:
    ///   1. project_config.json HANDOVER_MODE
    ///   2. PARAGRAPH_PRESETS.json active_preset
    ///   3. "Handover" fallback
    ///
    /// Revit guarantees that writes performed inside Execute do not re-trigger
    /// the same updater, so no manual re-entrancy guard is needed.
    /// </summary>
    public class StingTag7NarrativeUpdater : IUpdater
    {
        private static StingTag7NarrativeUpdater _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled;

        // Cached preset + param-name set; rebuilt on Invalidate() or preset switch
        private static ParagraphPresets _presetsCache;
        private static DateTime _presetsCacheStamp = DateTime.MinValue;
        private static readonly TimeSpan CacheTTL = TimeSpan.FromSeconds(30);

        private StingTag7NarrativeUpdater(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId, new Guid("A7B3C2D1-8E9F-4A5B-9C0D-1E2F3A4B5C6D"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;
        public string GetUpdaterName() => "STING Tag 7 Narrative Updater";
        public string GetAdditionalInformation() =>
            "Refreshes ASS_TAG_7_TXT on tagged elements whenever a source parameter " +
            "referenced by the active paragraph preset (PARAGRAPH_PRESETS.json) changes.";

        public static bool IsEnabled => _enabled;

        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new StingTag7NarrativeUpdater(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingLog.Info("StingTag7NarrativeUpdater registered (disabled).");
            }
            catch (Exception ex)
            {
                StingLog.Error("StingTag7NarrativeUpdater.Register", ex);
            }
        }

        public static void Unregister()
        {
            try
            {
                if (_instance != null) UpdaterRegistry.UnregisterUpdater(_updaterId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingTag7NarrativeUpdater.Unregister: {ex.Message}");
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var filter = StingAutoTagger.CreateMultiCategoryFilterStatic();
                    // Fire on any element modification (parameter change included).
                    // Revit does not re-trigger the same updater for changes made
                    // inside its own Execute, so no guard flag is needed.
                    UpdaterRegistry.AddTrigger(_updaterId, filter,
                        Element.GetChangeTypeAny());
                    _enabled = true;
                    StingLog.Info("StingTag7NarrativeUpdater enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("StingTag7NarrativeUpdater disabled.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingTag7NarrativeUpdater.SetEnabled", ex);
            }
        }

        /// <summary>Force the next Execute() to reload PARAGRAPH_PRESETS.json.</summary>
        public static void InvalidateCache()
        {
            _presetsCache = null;
            _presetsCacheStamp = DateTime.MinValue;
        }

        private const int MaxElementsPerTrigger = 200;

        public void Execute(UpdaterData data)
        {
            if (!_enabled) return;
            try
            {
                Document doc = data.GetDocument();
                if (doc == null || !doc.IsValidObject) return;

                var modified = data.GetModifiedElementIds();
                if (modified == null || modified.Count == 0) return;

                var (preset, sourceParams) = ResolveActivePreset(doc);
                if (preset == null || sourceParams.Count == 0) return;

                // Skip the trigger unless at least one modified element had one
                // of the source params changed. We don't have a per-parameter
                // filter so we over-trigger on geometry edits; the composed-vs-
                // current equality check below still short-circuits those.
                int processed = 0, updated = 0;
                foreach (ElementId id in modified)
                {
                    if (processed++ >= MaxElementsPerTrigger) break;
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    if (!ElementHasTag7(el)) continue;

                    string composed = ApplyParagraphPresetCommand_Compose(el, preset);
                    if (string.IsNullOrEmpty(composed)) continue;

                    Parameter p = el.LookupParameter(ParamRegistry.TAG7);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) continue;
                    string cur = p.AsString() ?? "";
                    if (cur == composed) continue;
                    p.Set(composed);
                    updated++;
                }
                if (updated > 0)
                    StingLog.Info($"Tag7NarrativeUpdater: refreshed {updated} element(s) via preset '{preset.Key}'.");
            }
            catch (Exception ex)
            {
                StingLog.Error("StingTag7NarrativeUpdater.Execute", ex);
            }
        }

        /// <summary>
        /// Cached preset resolution. Returns the active preset and the set of
        /// parameter names it depends on, used only for early-exit hints.
        /// </summary>
        private static (ParagraphPreset preset, HashSet<string> sourceParams) ResolveActivePreset(Document doc)
        {
            if (_presetsCache == null || DateTime.UtcNow - _presetsCacheStamp > CacheTTL)
            {
                try
                {
                    _presetsCache = ParagraphPresets.Load();
                    _presetsCacheStamp = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Tag7NarrativeUpdater: PARAGRAPH_PRESETS.json load failed: {ex.Message}");
                    return (null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }
            }

            string key = ReadHandoverMode(doc) ?? _presetsCache.ActivePreset ?? "Handover";
            if (!_presetsCache.Entries.TryGetValue(key, out var preset))
                preset = _presetsCache.Entries.Values.FirstOrDefault();

            var paramSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (preset != null)
            {
                foreach (var t in preset.Tiers.Values)
                    foreach (var r in t.Rows)
                        if (!string.IsNullOrEmpty(r.Parameter)) paramSet.Add(r.Parameter);
            }
            return (preset, paramSet);
        }

        private static string ReadHandoverMode(Document doc)
        {
            try
            {
                string cfgPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                    "project_config.json");
                if (!System.IO.File.Exists(cfgPath)) return null;
                var jo = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(cfgPath));
                return (string)jo["HANDOVER_MODE"];
            }
            catch { return null; }
        }

        private static bool ElementHasTag7(Element el)
        {
            // Only elements that carry ASS_TAG_7_TXT participate. Cheaper than
            // checking every category membership — LookupParameter returns null
            // for families without the binding.
            return el.LookupParameter(ParamRegistry.TAG7) != null;
        }

        /// <summary>
        /// Thin wrapper that re-uses ApplyParagraphPresetCommand's composition.
        /// Kept private+static here (one small duplication) so this file has no
        /// cross-project dependency on the command's internals.
        /// </summary>
        private static string ApplyParagraphPresetCommand_Compose(Element el, ParagraphPreset preset)
        {
            var sbAll = new System.Text.StringBuilder();
            string[] order = { "T4", "T5", "T6", "T7", "T8", "T9", "T10" };
            bool first = true;
            foreach (string t in order)
            {
                if (!preset.Tiers.TryGetValue(t, out var tier) || tier.Rows.Count == 0) continue;
                var sbT = new System.Text.StringBuilder();
                foreach (var row in tier.Rows)
                {
                    if (!row.Enabled) continue;
                    string v = ReadParamAsText(el, row.Parameter);
                    if (string.IsNullOrEmpty(v)) continue;
                    if (sbT.Length > 0) sbT.Append(row.Brk ? "\n" : " ");
                    if (!string.IsNullOrEmpty(row.Prefix)) { sbT.Append(row.Prefix); sbT.Append(' '); }
                    sbT.Append(v);
                    if (!string.IsNullOrEmpty(row.Suffix)) { sbT.Append(' '); sbT.Append(row.Suffix); }
                }
                if (sbT.Length == 0) continue;
                if (!first) sbAll.Append(" | ");
                sbAll.Append(sbT);
                first = false;
            }
            return sbAll.ToString();
        }

        private static string ReadParamAsText(Element el, string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            Parameter p = el.LookupParameter(name);
            if (p == null) return "";
            switch (p.StorageType)
            {
                case StorageType.String:    return p.AsString() ?? "";
                case StorageType.Integer:   return p.AsInteger().ToString();
                case StorageType.Double:    return p.AsValueString() ?? p.AsDouble().ToString("0.###");
                case StorageType.ElementId:
                    // Revit Parameter has no .Document property; fall back to
                    // the raw id string. Resolving to an element name is
                    // caller-context dependent.
                    var eid = p.AsElementId();
                    return eid == null || eid == ElementId.InvalidElementId
                           ? "" : eid.Value.ToString();
                default: return "";
            }
        }
    }

    // ------------------------------------------------------------------
    // Toggle commands (wired into StingDockPanel + StingCommandHandler)
    // ------------------------------------------------------------------

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class Tag7NarrativeUpdaterToggleCommand : IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(Autodesk.Revit.UI.ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            bool now = !StingTag7NarrativeUpdater.IsEnabled;
            StingTag7NarrativeUpdater.SetEnabled(now);
            StingTag7NarrativeUpdater.InvalidateCache();
            TaskDialog.Show("STING",
                $"Tag 7 narrative auto-update is now {(now ? "ENABLED" : "DISABLED")}.\n\n" +
                (now
                    ? "ASS_TAG_7_TXT will refresh whenever a parameter referenced by the\nactive paragraph preset changes on a tagged element."
                    : "Manual refresh only. Use Tag Studio > Tokens & Depth > Apply preset."));
            return Autodesk.Revit.UI.Result.Succeeded;
        }
    }
}
