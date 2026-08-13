// StingTools — Visibility Center · dropdown code-behind
//
// The footer is computed with VisibilityRuleMatcher.PlanCore against the already-harvested
// snapshots. That call is Revit-free, so recomputing on every tick is safe on the WPF
// thread and costs nothing — which is the whole point of splitting Plan from Apply.

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
        private bool _initialising;

        /// <summary>Raised when the user asks for an action; the host closes the popup and dispatches.</summary>
        public event System.Action<string> ActionRequested;

        public VisibilityDropdown()
        {
            InitializeComponent();
            icGroups.ItemsSource = _groups;
        }

        /// <summary>
        /// Fill from a harvest taken on the Revit API thread. Categories are populated
        /// immediately; token groups stay collapsed and fill on first expand.
        /// </summary>
        public void Load(TokenHarvest harvest)
        {
            _initialising = true;
            try
            {
                _harvest = harvest ?? new TokenHarvest();
                _groups.Clear();

                var cats = new VisGroupVm { TokenKey = null, Header = "CATEGORIES", IsExpanded = true, IsLoaded = true };
                foreach (var c in _harvest.Categories)
                    cats.Rows.Add(new VisRowVm { Key = c.Name, CategoryId = c.CategoryId, Count = c.Count });
                cats.ApplySearch(null);
                _groups.Add(cats);

                foreach (var token in VisibilityTokens.All)
                {
                    _groups.Add(new VisGroupVm
                    {
                        TokenKey = token,
                        Header = VisibilityTokens.Label(token),
                        IsExpanded = false,
                        IsLoaded = false
                    });
                }
            }
            finally { _initialising = false; }

            UpdateFooter();
        }

        // ── Group population + search ───────────────────────────────────

        private void Group_Expanded(object sender, RoutedEventArgs e)
        {
            var group = (sender as FrameworkElement)?.DataContext as VisGroupVm;
            if (group == null || group.IsLoaded) return;

            foreach (var v in _harvest.ValuesFor(group.TokenKey))
                group.Rows.Add(new VisRowVm { Key = v.Value, Count = v.Count });

            group.IsLoaded = true;
            group.ApplySearch(txtSearch.Text);
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (_initialising) return;
            foreach (var g in _groups.Where(g => g.IsLoaded)) g.ApplySearch(txtSearch.Text);
        }

        // ── Tick handling ───────────────────────────────────────────────

        private void Row_Click(object sender, RoutedEventArgs e) => UpdateFooter();

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
                foreach (var v in _harvest.ValuesFor(group.TokenKey))
                    group.Rows.Add(new VisRowVm { Key = v.Value, Count = v.Count });
                group.IsLoaded = true;
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
                if (!g.IsLoaded || g.Rows.Count == 0) continue;

                var picked = action == VisibilityAction.Hide ? g.Unchecked() : g.Checked();
                if (picked.Count == 0) continue;
                // Everything ticked = no constraint from this group.
                if (action == VisibilityAction.ShowOnly && picked.Count == g.Rows.Count) continue;

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
        public void Snapshot(VisibilityAction action) =>
            VisibilitySession.Snapshot(CurrentMode, CurrentTarget, BuildRules(action));

        // ── Live footer ─────────────────────────────────────────────────

        private void UpdateFooter()
        {
            if (txtFooter == null) return;

            var rules = BuildRules(VisibilityAction.Hide);
            if (rules.Count == 0)
            {
                txtFooter.Text = $"Nothing hidden — {_harvest.TotalElements:N0} elements visible. " +
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
