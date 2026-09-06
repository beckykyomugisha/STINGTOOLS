// StingTools — Visibility Center · dropdown code-behind
//
// The footer is computed with VisibilityRuleMatcher.PlanCore against the already-harvested
// snapshots. That call is Revit-free, so recomputing on every tick is safe on the WPF
// thread and costs nothing — which is the whole point of splitting Plan from Apply.
//
// Rows open in the state VisibilityStateReader read off the view. They used to default to
// ticked, which made the panel assert "nothing is hidden" over a view that plainly was, and
// meant the next Apply was computed from a wrong baseline.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StingTools.Core.Visibility;

namespace StingTools.UI.VisibilityCenter
{
    public partial class VisibilityDropdown : UserControl
    {
        private readonly ObservableCollection<VisGroupVm> _groups = new ObservableCollection<VisGroupVm>();
        private TokenHarvest _harvest = new TokenHarvest();
        private VisibilityReadback _readback;
        private bool _initialising;

        /// <summary>
        /// Signature of the rule set the dropdown OPENED with. Rows now open unticked when the
        /// view is already hiding them, so on open BuildRules is non-empty — and without this
        /// the footer would read "Will hide 1,204 elements" about elements that are already
        /// hidden. While the ticks still match what was read, the footer reports the view's
        /// state; the moment the user changes one, it switches to the plan.
        /// </summary>
        private string _openingRuleKey = string.Empty;

        /// <summary>Raised when the user asks for an action; the host closes the popup and dispatches.</summary>
        public event System.Action<string> ActionRequested;

        public VisibilityDropdown()
        {
            InitializeComponent();
            icGroups.ItemsSource = _groups;
        }

        /// <summary>
        /// Fill from a state read taken on the Revit API thread. Categories are populated
        /// immediately; token groups stay collapsed and fill on first expand.
        /// </summary>
        /// <param name="readback">
        /// What the view is ALREADY hiding. Null is accepted — it means "state unknown", and
        /// every row falls back to ticked. Passing null routinely would reinstate the bug this
        /// parameter exists to fix, so callers should read the state.
        /// </param>
        public void Load(TokenHarvest harvest, VisibilityReadback readback = null)
        {
            _initialising = true;
            try
            {
                _harvest = harvest ?? new TokenHarvest();
                _readback = readback;
                _groups.Clear();

                foreach (var g in VisibilityGroupBuilder.BuildCategoryGroups(_harvest, _readback))
                    _groups.Add(g);
                foreach (var g in VisibilityGroupBuilder.BuildTokenGroups(_harvest, _readback))
                    _groups.Add(g);
            }
            finally { _initialising = false; }

            _openingRuleKey = RuleKey(BuildRules(VisibilityAction.Hide));
            UpdateFooter();
        }

        /// <summary>Order-independent signature of a rule set, for "has the user changed anything".</summary>
        private static string RuleKey(IEnumerable<VisibilityRule> rules) =>
            string.Join("|", rules
                .Select(r => r.Kind == VisibilityRuleKind.Category
                    ? "C:" + r.CategoryId
                    : "T:" + r.TokenKey + "=" + string.Join(",", (r.Values ?? new List<string>()).OrderBy(v => v)))
                .OrderBy(s => s, System.StringComparer.Ordinal));

        // ── Group population + search ───────────────────────────────────

        private void Group_Expanded(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as VisGroupVm;
            if (group == null || group.IsLoaded) return;

            VisibilityGroupBuilder.PopulateTokenRows(group, _harvest, _readback);
            group.ApplySearch(txtSearch.Text);
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initialising) return;
            foreach (var g in _groups.Where(g => g.IsLoaded)) g.ApplySearch(txtSearch.Text);
        }

        // ── Tick handling ───────────────────────────────────────────────

        private void Row_Click(object sender, RoutedEventArgs e) { UpdateFooter(); QueueLiveApply(); }

        // ── Live apply ──────────────────────────────────────────────────
        //
        // Ticking a box changes the view directly; the Apply button becomes a confirmation
        // rather than the only way to make anything happen. Debounced because each apply is a
        // transaction and a Revit regeneration — All / None / Invert flip dozens of rows in one
        // gesture, and firing per row would queue dozens of transactions and lock the UI.
        // One apply lands ~450 ms after the last click instead.
        private System.Windows.Threading.DispatcherTimer _liveTimer;

        /// <summary>Off for Saved-to-view mode: that path creates ParameterFilterElements, and
        /// minting filter elements on every tick would litter the project.</summary>
        private bool LiveEnabled => chkLive?.IsChecked == true && CurrentMode == VisibilityMode.Temporary;

        private void QueueLiveApply()
        {
            if (_initialising || !LiveEnabled) return;

            if (_liveTimer == null)
            {
                _liveTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromMilliseconds(450)
                };
                _liveTimer.Tick += (s, e) =>
                {
                    _liveTimer.Stop();
                    if (!LiveEnabled) return;
                    Snapshot(VisibilityAction.Hide);
                    ActionRequested?.Invoke("Vis_ApplyLive");
                };
            }
            _liveTimer.Stop();
            _liveTimer.Start();
        }

        private void Live_Changed(object sender, RoutedEventArgs e)
        {
            if (_initialising) return;
            if (LiveEnabled) QueueLiveApply();
            else _liveTimer?.Stop();
        }

        private void All_Click(object sender, RoutedEventArgs e) => SetGroup(sender, g => g.SetAll(true));
        private void None_Click(object sender, RoutedEventArgs e) => SetGroup(sender, g => g.SetAll(false));
        private void Invert_Click(object sender, RoutedEventArgs e) => SetGroup(sender, g => g.Invert());

        private void SetGroup(object sender, System.Action<VisGroupVm> op)
        {
            var group = (sender as FrameworkElement)?.Tag as VisGroupVm;
            if (group == null) return;

            // All/None/Invert on a group the user never expanded still needs its rows.
            if (!group.IsLoaded && group.TokenKey != null)
            {
                VisibilityGroupBuilder.PopulateTokenRows(group, _harvest, _readback);
                group.ApplySearch(txtSearch.Text);
            }

            op(group);
            UpdateFooter();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e) { if (!_initialising) UpdateFooter(); }
        private void Target_Changed(object sender, SelectionChangedEventArgs e) { if (!_initialising) UpdateFooter(); }

        // ── Rule construction ───────────────────────────────────────────

        private VisibilityMode CurrentMode =>
            rbSaved != null && rbSaved.IsChecked == true ? VisibilityMode.ViewFilter : VisibilityMode.Temporary;

        private VisibilityTarget CurrentTarget =>
            cboTarget != null && cboTarget.SelectedIndex == 1
                ? VisibilityTarget.AllViewsOnSheet
                : VisibilityTarget.ActiveView;

        /// <summary>
        /// Unticked rows become Hide rules; for Isolate the ticked rows become ShowOnly rules.
        /// A group where everything is ticked contributes nothing to a hide set, and a group
        /// the user never touched contributes nothing to an isolate set either — otherwise
        /// opening the dropdown and pressing Isolate would isolate the entire model.
        /// </summary>
        public List<VisibilityRule> BuildRules(VisibilityAction action)
        {
            var rules = new List<VisibilityRule>();

            foreach (var g in _groups)
            {
                if (!g.IsLoaded) continue;
                int total = g.AllRows().Count();
                if (total == 0) continue;

                var picked = action == VisibilityAction.Hide ? g.Unchecked() : g.Checked();
                if (picked.Count == 0) continue;
                // Everything ticked = no constraint from this group.
                if (action == VisibilityAction.ShowOnly && picked.Count == total) continue;

                if (g.TokenKey == null)
                {
                    foreach (var row in picked)
                        rules.Add(new VisibilityRule
                        {
                            Kind = VisibilityRuleKind.Category,
                            CategoryId = row.CategoryId,
                            CategoryName = row.Key,
                            Action = action
                        });
                }
                else
                {
                    rules.Add(new VisibilityRule
                    {
                        Kind = VisibilityRuleKind.Token,
                        TokenKey = g.TokenKey,
                        Values = picked.Select(r => r.Key).ToList(),
                        Action = action
                    });
                }
            }
            return rules;
        }

        /// <summary>Push the current tick state into the session for the API-thread commands.</summary>
        /// <remarks>
        /// Ticked categories travel alongside the rules so the apply is DECLARATIVE for
        /// categories — it states what should be visible as well as what should be hidden.
        /// Sending only "hide these" is why re-ticking a row never brought it back.
        /// </remarks>
        public void Snapshot(VisibilityAction action) =>
            VisibilitySession.Snapshot(CurrentMode, CurrentTarget, BuildRules(action), VisibleCategoryIds());

        /// <summary>Category ids the user has left ticked — everything they want visible.</summary>
        private List<int> VisibleCategoryIds()
        {
            var ids = new List<int>();
            foreach (var g in _groups)
            {
                if (g.TokenKey != null) continue;           // categories only
                foreach (var row in g.Checked())
                    if (row.CategoryId != 0) ids.Add(row.CategoryId);
            }
            return ids;
        }

        // ── Live footer ─────────────────────────────────────────────────

        private void UpdateFooter()
        {
            if (txtFooter == null) return;

            var rules = BuildRules(VisibilityAction.Hide);

            // Nothing NEW is selected — report what the view is already doing rather than the
            // old flat "Nothing hidden", which was false on a filtered view, and rather than
            // "Will hide N", which would be false about elements already out of sight.
            if (rules.Count == 0 || RuleKey(rules) == _openingRuleKey)
            {
                string current = _readback?.Footer();
                txtFooter.Text = current
                    ?? $"Nothing hidden — {_harvest.TotalElements:N0} elements visible. " +
                       "Untick something to hide it.";
                return;
            }

            var set = new VisibilitySet { Mode = CurrentMode, Target = CurrentTarget, Rules = rules };
            var plan = VisibilityRuleMatcher.PlanCore(_harvest.Elements, set, CurrentMode);
            txtFooter.Text = plan.Summary();
        }

        // ── Actions ─────────────────────────────────────────────────────

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            Snapshot(VisibilityAction.Hide);
            ActionRequested?.Invoke("Vis_Apply");
        }

        private void Isolate_Click(object sender, RoutedEventArgs e)
        {
            Snapshot(VisibilityAction.ShowOnly);
            ActionRequested?.Invoke("Vis_Isolate");
        }

        private void Reset_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke("Vis_ResetAll");

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            Snapshot(VisibilityAction.Hide);
            ActionRequested?.Invoke("Vis_SavePreset");
        }

        private void Presets_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke("Vis_LoadPreset");
    }
}
