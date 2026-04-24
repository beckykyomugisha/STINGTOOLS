using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Tags;

// WPF and Revit both declare Color / Grid — disambiguate once at the
// top so CS0104 doesn't fire on every colour / Grid reference in the
// file. UI code here always wants the WPF types.
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Grid = System.Windows.Controls.Grid;

namespace StingTools.UI
{
    /// <summary>
    /// BOQ-style builder for T4..T10 paragraph content. Users pick a preset
    /// (Handover / DesignConstruction / Custom / user-saved), edit rows via
    /// parameter dropdown + prefix + suffix + break + enabled, and press
    /// Apply to push the composed paragraph into ASS_TAG_7_TXT on elements
    /// in scope. Save As persists to Data/PARAGRAPH_PRESETS.json.
    /// </summary>
    public class ParagraphBuilderDialog : Window
    {
        // ── colour palette (light, contrast-safe — matches rest of app) ──
        private static readonly Color BgColor     = Color.FromRgb(0xFA, 0xFA, 0xFA); // window bg
        private static readonly Color AccentColor = Color.FromRgb(0xE8, 0x91, 0x2D); // STING orange
        private static readonly Color CardBg      = Color.FromRgb(0xFF, 0xFF, 0xFF); // input / card bg
        private static readonly Color CardBorder  = Color.FromRgb(0xCF, 0xD8, 0xDC); // subtle border
        private static readonly Color FgColor     = Color.FromRgb(0x22, 0x22, 0x22); // body text
        private static readonly Color SubtleColor = Color.FromRgb(0x66, 0x66, 0x66); // muted text

        // ── state ──
        private ParagraphPresets _presets;
        private string _currentKey;
        private ComboBox _cmbPreset;
        private ComboBox _cmbScope;
        private TextBlock _txtDesc;
        private StackPanel _tiersHost;
        private List<string> _paramCatalog;

        public ParagraphBuilderResult Result { get; private set; } = new ParagraphBuilderResult();

        public ParagraphBuilderDialog()
        {
            Title = "Paragraph Builder — T4..T10";
            Width = 820; Height = 680;
            MinWidth = 780; MinHeight = 520;
            Background = new SolidColorBrush(BgColor);
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DarkDialogTheme.ApplyComboBoxFix(this, CardBg, FgColor, CardBorder);
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch { /* ignore — unit test paths */ }

            LoadPresetsSafe();
            _paramCatalog = BuildParamCatalog();
            Content = BuildLayout();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------
        private UIElement BuildLayout()
        {
            var root = new DockPanel { Margin = new Thickness(12), LastChildFill = true };

            // ── Header: title + description ──
            var header = new StackPanel { Orientation = Orientation.Vertical };
            header.Children.Add(new TextBlock
            {
                Text = "Paragraph Builder",
                Foreground = new SolidColorBrush(AccentColor),
                FontSize = 18, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2),
            });
            header.Children.Add(new TextBlock
            {
                Text = "Edit T4..T10 paragraph rows — parameter + prefix + suffix. Applied to ASS_TAG_7_TXT.",
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, Margin = new Thickness(0, 0, 0, 8),
            });
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── Preset picker row ──
            var presetCard = MakeCard();
            var presetGrid = new Grid();
            for (int i = 0; i < 6; i++)
                presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            presetGrid.ColumnDefinitions[1] = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };

            AddLabel(presetGrid, 0, "Preset");
            _cmbPreset = new ComboBox { Height = 24, Margin = new Thickness(4, 0, 8, 0) };
            DarkDialogTheme.StyleInput(_cmbPreset, CardBg, FgColor, CardBorder);
            foreach (var kv in _presets.Entries) _cmbPreset.Items.Add(kv.Key);
            _cmbPreset.SelectedItem = _presets.ActivePreset;
            _cmbPreset.SelectionChanged += (s, e) => OnPresetChanged();
            Grid.SetColumn(_cmbPreset, 1); presetGrid.Children.Add(_cmbPreset);

            var btnLoad = MakeBtn("Load", (s, e) => OnPresetChanged());
            Grid.SetColumn(btnLoad, 2); presetGrid.Children.Add(btnLoad);
            var btnSaveAs = MakeBtn("Save As…", (s, e) => OnSaveAs());
            Grid.SetColumn(btnSaveAs, 3); presetGrid.Children.Add(btnSaveAs);
            var btnDelete = MakeBtn("Delete", (s, e) => OnDelete());
            Grid.SetColumn(btnDelete, 4); presetGrid.Children.Add(btnDelete);
            var btnReset = MakeBtn("Reset", (s, e) => OnResetToBuiltIn());
            Grid.SetColumn(btnReset, 5); presetGrid.Children.Add(btnReset);

            presetCard.Child = presetGrid;
            DockPanel.SetDock(presetCard, Dock.Top);
            root.Children.Add(presetCard);

            // ── Description of active preset ──
            _txtDesc = new TextBlock
            {
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 4, 4, 8),
            };
            DockPanel.SetDock(_txtDesc, Dock.Top);
            root.Children.Add(_txtDesc);

            // ── Bottom action bar ──
            var actionBar = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
            AddLabel(actionBar, null, "Scope");
            _cmbScope = new ComboBox { Height = 24, Width = 140, Margin = new Thickness(4, 0, 12, 0) };
            DarkDialogTheme.StyleInput(_cmbScope, CardBg, FgColor, CardBorder);
            _cmbScope.Items.Add("Selection (fallback: Active view)");
            _cmbScope.Items.Add("Active view");
            _cmbScope.Items.Add("Entire project");
            _cmbScope.SelectedIndex = 0;
            DockPanel.SetDock(_cmbScope, Dock.Left);
            actionBar.Children.Add(_cmbScope);

            var btnApply = MakeBtn("Apply preset", (s, e) => OnApply(), accent: true, width: 120);
            DockPanel.SetDock(btnApply, Dock.Right);
            actionBar.Children.Add(btnApply);
            var btnCancel = MakeBtn("Close", (s, e) => Close(), width: 80);
            DockPanel.SetDock(btnCancel, Dock.Right);
            actionBar.Children.Add(btnCancel);
            DockPanel.SetDock(actionBar, Dock.Bottom);
            root.Children.Add(actionBar);

            // ── Scrollable tier panel (fills) ──
            _tiersHost = new StackPanel { Orientation = Orientation.Vertical };
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _tiersHost,
            };
            root.Children.Add(sv);

            BindCurrentPreset();
            return root;
        }

        // ------------------------------------------------------------------
        // Preset binding (reads _currentKey from combo)
        // ------------------------------------------------------------------
        private void OnPresetChanged()
        {
            _currentKey = _cmbPreset.SelectedItem as string;
            BindCurrentPreset();
        }

        private void BindCurrentPreset()
        {
            if (string.IsNullOrEmpty(_currentKey)) _currentKey = _cmbPreset.SelectedItem as string;
            if (string.IsNullOrEmpty(_currentKey) || !_presets.Entries.ContainsKey(_currentKey))
                _currentKey = _presets.Entries.Keys.FirstOrDefault() ?? "Handover";

            var preset = _presets.Entries[_currentKey];
            _txtDesc.Text = (preset.Readonly ? "[built-in · read-only] " : "") +
                            preset.Description;
            _tiersHost.Children.Clear();
            string[] tiers = { "T4", "T5", "T6", "T7", "T8", "T9", "T10" };
            foreach (string t in tiers)
            {
                if (!preset.Tiers.TryGetValue(t, out var tier))
                    preset.Tiers[t] = tier = new ParagraphPresetTier();
                _tiersHost.Children.Add(BuildTierCard(t, tier, preset.Readonly));
            }
        }

        // ------------------------------------------------------------------
        // Tier card builder — one expander per tier (T4..T10)
        // ------------------------------------------------------------------
        private UIElement BuildTierCard(string tierKey, ParagraphPresetTier tier, bool readOnly)
        {
            var expander = new Expander
            {
                Foreground = new SolidColorBrush(FgColor),
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                IsExpanded = tier.Rows.Count > 0,
                Header = $"{tierKey} — {tier.Label}   ({tier.Rows.Count} row" +
                         (tier.Rows.Count == 1 ? ")" : "s)"),
            };

            var body = new StackPanel { Margin = new Thickness(10, 4, 4, 8) };

            // Tier label textbox
            var labelRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(MakeMini("Label"), Dock.Left);
            labelRow.Children.Add(MakeMini("Label"));
            var tbLabel = new TextBox
            {
                Text = tier.Label, Height = 22, FontSize = 11,
                IsReadOnly = readOnly,
            };
            DarkDialogTheme.StyleInput(tbLabel, CardBg, FgColor, CardBorder);
            tbLabel.TextChanged += (s, e) => { tier.Label = tbLabel.Text; RefreshHeader(expander, tierKey, tier); };
            labelRow.Children.Add(tbLabel);
            body.Children.Add(labelRow);

            // Rows table — column headers
            var tbl = new Grid();
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });    // enabled
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // param
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });   // prefix
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });    // suffix
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });    // brk
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });    // remove
            var hdr = new[] { "", "Parameter", "Prefix", "Suffix", "Brk", "" };
            for (int ci = 0; ci < hdr.Length; ci++)
            {
                var tbH = new TextBlock
                {
                    Text = hdr[ci],
                    Foreground = new SolidColorBrush(SubtleColor),
                    FontSize = 9, Margin = new Thickness(2, 0, 2, 2),
                };
                Grid.SetColumn(tbH, ci);
                tbl.RowDefinitions.Add(new RowDefinition());
                tbl.Children.Add(tbH);
            }

            // Existing rows
            for (int ri = 0; ri < tier.Rows.Count; ri++)
                AddRowControls(tbl, tier, tier.Rows[ri], ri + 1, readOnly, expander, tierKey);

            body.Children.Add(tbl);

            // + Add row button
            if (!readOnly)
            {
                var btnAdd = MakeBtn("+ Add row", (s, e) =>
                {
                    tier.Rows.Add(new ParagraphPresetRow { Enabled = true, Style = "NOM", Color = "GREY", Size = 2.0 });
                    BindCurrentPreset();  // re-render full panel so grid picks up new row
                });
                btnAdd.HorizontalAlignment = HorizontalAlignment.Left;
                btnAdd.Margin = new Thickness(0, 6, 0, 0);
                body.Children.Add(btnAdd);
            }

            expander.Content = body;
            // Wrap in bordered card
            var card = MakeCard();
            card.Child = expander;
            return card;
        }

        private void AddRowControls(Grid tbl, ParagraphPresetTier tier, ParagraphPresetRow row,
            int rowIndex, bool readOnly, Expander hdrOwner, string tierKey)
        {
            tbl.RowDefinitions.Add(new RowDefinition());

            var chk = new CheckBox
            {
                IsChecked = row.Enabled, IsEnabled = !readOnly,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(2, 1, 2, 1),
            };
            chk.Checked += (s, e) => { row.Enabled = true; };
            chk.Unchecked += (s, e) => { row.Enabled = false; };
            Grid.SetColumn(chk, 0); Grid.SetRow(chk, rowIndex); tbl.Children.Add(chk);

            var cmbParam = new ComboBox
            {
                IsEditable = true, IsReadOnly = readOnly,
                Height = 22, FontSize = 10, Margin = new Thickness(2, 1, 2, 1),
                Text = row.Parameter,
            };
            DarkDialogTheme.StyleInput(cmbParam, CardBg, FgColor, CardBorder);
            foreach (var p in _paramCatalog) cmbParam.Items.Add(p);
            cmbParam.Text = row.Parameter;
            cmbParam.LostFocus += (s, e) => { row.Parameter = cmbParam.Text?.Trim() ?? ""; };
            cmbParam.SelectionChanged += (s, e) =>
            {
                if (cmbParam.SelectedItem is string ps) row.Parameter = ps;
            };
            Grid.SetColumn(cmbParam, 1); Grid.SetRow(cmbParam, rowIndex); tbl.Children.Add(cmbParam);

            var tbPrefix = new TextBox
            {
                Text = row.Prefix, IsReadOnly = readOnly,
                Height = 22, FontSize = 10, Margin = new Thickness(2, 1, 2, 1),
            };
            DarkDialogTheme.StyleInput(tbPrefix, CardBg, FgColor, CardBorder);
            tbPrefix.TextChanged += (s, e) => row.Prefix = tbPrefix.Text;
            Grid.SetColumn(tbPrefix, 2); Grid.SetRow(tbPrefix, rowIndex); tbl.Children.Add(tbPrefix);

            var tbSuffix = new TextBox
            {
                Text = row.Suffix, IsReadOnly = readOnly,
                Height = 22, FontSize = 10, Margin = new Thickness(2, 1, 2, 1),
            };
            DarkDialogTheme.StyleInput(tbSuffix, CardBg, FgColor, CardBorder);
            tbSuffix.TextChanged += (s, e) => row.Suffix = tbSuffix.Text;
            Grid.SetColumn(tbSuffix, 3); Grid.SetRow(tbSuffix, rowIndex); tbl.Children.Add(tbSuffix);

            var chkBrk = new CheckBox
            {
                IsChecked = row.Brk, IsEnabled = !readOnly,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(2, 1, 2, 1),
                ToolTip = "Newline after this row (otherwise space-joined)",
            };
            chkBrk.Checked   += (s, e) => row.Brk = true;
            chkBrk.Unchecked += (s, e) => row.Brk = false;
            Grid.SetColumn(chkBrk, 4); Grid.SetRow(chkBrk, rowIndex); tbl.Children.Add(chkBrk);

            if (!readOnly)
            {
                var btnDel = MakeBtn("×", (s, e) =>
                {
                    tier.Rows.Remove(row);
                    BindCurrentPreset();
                });
                btnDel.Width = 22; btnDel.Height = 22;
                btnDel.Margin = new Thickness(0);
                btnDel.ToolTip = "Remove row";
                Grid.SetColumn(btnDel, 5); Grid.SetRow(btnDel, rowIndex); tbl.Children.Add(btnDel);
            }
        }

        private static void RefreshHeader(Expander ex, string tierKey, ParagraphPresetTier tier)
        {
            ex.Header = $"{tierKey} — {tier.Label}   ({tier.Rows.Count} row" +
                        (tier.Rows.Count == 1 ? ")" : "s)");
        }

        // ------------------------------------------------------------------
        // Save / Delete / Reset / Apply
        // ------------------------------------------------------------------
        private void OnSaveAs()
        {
            var cur = _presets.Entries[_currentKey];
            string defaultName = cur.Readonly ? $"{_currentKey}_Custom" : _currentKey;
            string name = PromptForName("Save preset as", "Preset name:", defaultName);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();

            if (_presets.Entries.TryGetValue(name, out var existing) && existing.Readonly)
            {
                MessageBox.Show($"'{name}' is a read-only built-in. Pick a different name.",
                    "STING", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var clone = new ParagraphPreset
            {
                Key = name,
                DisplayName = name,
                Description = $"Saved from {_currentKey}",
                Readonly = false,
                Source = "user",
            };
            foreach (var kv in cur.Tiers)
            {
                var t = new ParagraphPresetTier { Label = kv.Value.Label };
                foreach (var r in kv.Value.Rows)
                    t.Rows.Add(new ParagraphPresetRow
                    {
                        Parameter = r.Parameter, Prefix = r.Prefix, Suffix = r.Suffix,
                        Brk = r.Brk, Style = r.Style, Color = r.Color, Size = r.Size,
                        Enabled = r.Enabled,
                    });
                clone.Tiers[kv.Key] = t;
            }
            _presets.Entries[name] = clone;
            try { _presets.Save(); }
            catch (Exception ex)
            {
                StingLog.Error("ParagraphBuilder save failed", ex);
                MessageBox.Show($"Save failed:\n{ex.Message}", "STING",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!_cmbPreset.Items.Contains(name)) _cmbPreset.Items.Add(name);
            _cmbPreset.SelectedItem = name;
        }

        private void OnDelete()
        {
            var cur = _presets.Entries[_currentKey];
            if (cur.Readonly)
            {
                MessageBox.Show("Built-in presets cannot be deleted. Use Save As to derive a user copy.",
                    "STING", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Delete preset '{_currentKey}'?", "STING",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            _presets.Entries.Remove(_currentKey);
            try { _presets.Save(); }
            catch (Exception ex) { StingLog.Error("ParagraphBuilder delete save failed", ex); }
            _cmbPreset.Items.Remove(_currentKey);
            _cmbPreset.SelectedIndex = 0;
        }

        private void OnResetToBuiltIn()
        {
            if (MessageBox.Show(
                "Reload PARAGRAPH_PRESETS.json from disk?\n" +
                "Unsaved edits to the current preset will be discarded.",
                "STING", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            LoadPresetsSafe();
            _cmbPreset.Items.Clear();
            foreach (var kv in _presets.Entries) _cmbPreset.Items.Add(kv.Key);
            _cmbPreset.SelectedItem = _presets.ActivePreset;
            BindCurrentPreset();
        }

        private void OnApply()
        {
            // Persist any in-memory edits to JSON so the external command reads the same data
            try { _presets.ActivePreset = _currentKey; _presets.Save(); }
            catch (Exception ex)
            {
                StingLog.Error("ParagraphBuilder apply save failed", ex);
                MessageBox.Show($"Could not save preset before apply:\n{ex.Message}",
                    "STING", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string scope =
                _cmbScope.SelectedIndex == 2 ? "Project" :
                _cmbScope.SelectedIndex == 1 ? "ActiveView" : "SelectionOrActiveView";

            Result.ApplyRequested = true;
            Result.PresetKey = _currentKey;
            Result.Scope = scope;
            Close();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private void LoadPresetsSafe()
        {
            try { _presets = ParagraphPresets.Load(); }
            catch (Exception ex)
            {
                StingLog.Error("ParagraphBuilder: load failed", ex);
                _presets = new ParagraphPresets();
                _presets.Entries["Handover"] = new ParagraphPreset
                {
                    Key = "Handover", DisplayName = "Handover", Readonly = true,
                    Description = "Fallback — PARAGRAPH_PRESETS.json missing.",
                };
            }
            _currentKey = _presets.ActivePreset;
        }

        private List<string> BuildParamCatalog()
        {
            var list = new List<string>();
            try
            {
                foreach (var kv in ParamRegistry.AllParamGuids) list.Add(kv.Key);
            }
            catch (Exception ex) { StingLog.Warn($"ParagraphBuilder: catalog build: {ex.Message}"); }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private Border MakeCard()
        {
            return new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 4),
            };
        }

        private Button MakeBtn(string text, RoutedEventHandler onClick,
            bool accent = false, double width = 90)
        {
            var b = new Button
            {
                Content = text, Width = width, Height = 26,
                Margin = new Thickness(3, 0, 3, 0), FontSize = 11,
                Background = accent ? new SolidColorBrush(AccentColor) : new SolidColorBrush(CardBg),
                Foreground = new SolidColorBrush(accent ? Colors.White : (Color)FgColor),
                BorderBrush = new SolidColorBrush(CardBorder),
                Cursor = Cursors.Hand,
            };
            b.Click += onClick;
            return b;
        }

        private TextBlock MakeMini(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Width = 60,
            };
        }

        private void AddLabel(Grid grid, int col, string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, col); grid.Children.Add(tb);
        }

        private void AddLabel(DockPanel panel, int? col, string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(SubtleColor),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            DockPanel.SetDock(tb, Dock.Left);
            panel.Children.Add(tb);
        }

        private static string PromptForName(string title, string prompt, string defaultValue)
        {
            var dlg = new Window
            {
                Title = title, Width = 380, Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(BgColor),
                ResizeMode = ResizeMode.NoResize,
            };
            // Sub-dialog is a separate Window — its Resources dictionary is
            // not inherited from the parent, so the dark input theme must
            // be applied here explicitly or the TextBox below renders
            // white-on-white.
            DarkDialogTheme.ApplyDarkInputTheme(dlg, CardBg, FgColor, CardBorder);
            var sp = new StackPanel { Margin = new Thickness(12) };
            sp.Children.Add(new TextBlock
            {
                Text = prompt,
                Foreground = new SolidColorBrush(FgColor),
                Margin = new Thickness(0, 0, 0, 6),
            });
            var tb = new TextBox { Text = defaultValue, Height = 24 };
            DarkDialogTheme.StyleInput(tb, CardBg, FgColor, CardBorder);
            sp.Children.Add(tb);
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            string result = null;
            var btnOk = new Button { Content = "OK", Width = 70, Height = 24, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
            btnOk.Click += (s, e) => { result = tb.Text; dlg.DialogResult = true; };
            var btnCancel = new Button { Content = "Cancel", Width = 70, Height = 24, Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnOk);
            sp.Children.Add(btnRow);
            dlg.Content = sp;
            dlg.ShowDialog();
            return result;
        }
    }

    public sealed class ParagraphBuilderResult
    {
        public bool ApplyRequested { get; set; }
        public string PresetKey { get; set; }
        public string Scope { get; set; } = "SelectionOrActiveView";
    }
}
