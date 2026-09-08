using System;
using StingTools.Standards;

namespace StingTools.Core
{
    /// <summary>
    /// KUT-8 — the wire from the project-level region preset to the engines that
    /// actually have a region.
    ///
    /// <para><b>What was wrong.</b> <see cref="ProjectStandardsManager"/> holds the
    /// project region and raises <c>StandardsChanged</c> when it moves. That event had
    /// <b>no subscribers</b> — while two separate comments in the tree asserted that
    /// "the HVAC / Plumbing surfaces pick up the change automatically". Meanwhile the
    /// HVAC and LPS panels each carried their own region, in their own vocabulary,
    /// initialised to their own hardcoded constant, and nothing reconciled the three.
    /// Choosing Uganda in the setup wizard therefore left duct sizing on the UK size
    /// ladder and lightning risk on Ng ≈ 0.5–1.0.</para>
    ///
    /// <para><b>What this does.</b> Seeds both engine regions from the project preset at
    /// startup, and re-seeds them when the preset changes. It is the event's first
    /// subscriber, which is what makes those two comments true.</para>
    ///
    /// <para><b>What it deliberately does not do.</b> It never overrides a region the
    /// engineer has selected on the panel during a session: the panel writes its combo
    /// into the handler on every raise, so the preset supplies the <i>starting</i>
    /// selection and the panel keeps the last word. A project preset that quietly
    /// re-pointed a calculation mid-session would be worse than the gap it closes.</para>
    /// </summary>
    public static class EngineRegionSync
    {
        private static readonly object _gate = new object();
        private static bool _attached;

        /// <summary>The project region the engines were last seeded from, or null when
        /// no sync has run. Read by the panels so a panel constructed after startup
        /// starts on the same region as one constructed before it.</summary>
        public static string LastAppliedProjectRegion { get; private set; }

        /// <summary>Subscribe to <c>StandardsChanged</c> and seed once. Idempotent —
        /// safe to call from every entry point that might be first.</summary>
        public static void Attach()
        {
            lock (_gate)
            {
                if (_attached) return;
                try
                {
                    ProjectStandardsManager.Instance.StandardsChanged += OnStandardsChanged;
                    _attached = true;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"EngineRegionSync.Attach: {ex.Message}");
                    return;
                }
            }
            SyncNow("startup");
        }

        private static void OnStandardsChanged(object sender, StandardsChangedEventArgs e)
        {
            // Region is the only setting this wire cares about; the rest of the
            // configuration is read on demand by whoever needs it.
            if (e != null && !string.Equals(e.SettingName, "Region", StringComparison.OrdinalIgnoreCase))
                return;
            SyncNow("StandardsChanged");
        }

        /// <summary>Push the current project region into every engine that has one.
        /// Each engine is pushed independently so a failure in one does not strand
        /// the others.</summary>
        public static void SyncNow(string reason)
        {
            string projectRegion;
            try { projectRegion = ProjectStandardsManager.Instance.Region ?? ""; }
            catch (Exception ex) { StingLog.Warn($"EngineRegionSync: region read failed: {ex.Message}"); return; }
            if (string.IsNullOrWhiteSpace(projectRegion)) return;

            string mep = EngineRegionMap.ToMepSizingRegion(projectRegion);
            string lps = EngineRegionMap.ToLpsRegion(projectRegion);

            try { StingTools.UI.StingHvacCommandHandler.SetInputs(region: mep, standard: null,
                        pressureClassId: null, airDensityKgM3: double.NaN, sizingStrategyId: null, scope: null); }
            catch (Exception ex) { StingLog.Warn($"EngineRegionSync: HVAC region: {ex.Message}"); }

            try { StingTools.UI.StingLpsCommandHandler.CurrentRegion = lps; }
            catch (Exception ex) { StingLog.Warn($"EngineRegionSync: LPS region: {ex.Message}"); }

            // The panels own the selection the user sees, and they re-post it on every
            // raise — so seeding only the handler statics would be undone by the first
            // button click. Move the combos too, on the UI thread.
            try { StingTools.UI.StingHvacPanel.Instance?.ApplyProjectRegion(mep); }
            catch (Exception ex) { StingLog.Warn($"EngineRegionSync: HVAC combo: {ex.Message}"); }
            try { StingTools.UI.StingLpsPanel.Instance?.ApplyProjectRegion(lps); }
            catch (Exception ex) { StingLog.Warn($"EngineRegionSync: LPS combo: {ex.Message}"); }

            // The main dock panel's region chip had no caller at all, so it kept whatever
            // region it was built with for the life of the session while its tooltip
            // claimed otherwise. Repaint it here - this is the one place that knows the
            // region moved.
            try { StingTools.UI.StingDockPanel.LastInstance?.RefreshRegionIndicator(); }
            catch (Exception ex) { StingLog.Warn($"EngineRegionSync: region chip: {ex.Message}"); }

            lock (_gate) { LastAppliedProjectRegion = projectRegion; }
            StingLog.Info($"EngineRegionSync ({reason}): project region '{projectRegion}' -> MEP sizing '{mep}', LPS '{lps}'.");
        }
    }
}
