// ══════════════════════════════════════════════════════════════════════════
//  PresetCombinationCommands.cs — Phase 108m
//  WPF picker + runner for the preset library. Presents a modeless dialog
//  listing every preset from PRESET_COMBINATIONS.json with shared-param
//  inputs, dispatches each step via StingDockPanel.DispatchCommand, wraps
//  the chain in a TransactionGroup for atomic rollback.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;

namespace StingTools.Presets
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PresetCombinationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;

                var presets = PresetCombinationEngine.LoadAll();
                if (presets.Count == 0)
                {
                    TaskDialog.Show("Presets", "No presets found — PRESET_COMBINATIONS.json missing or empty.");
                    return Result.Cancelled;
                }

                var dlg = new PresetPickerDialog(presets, ctx.Doc);
                try { new System.Windows.Interop.WindowInteropHelper(dlg).Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
                catch (Exception ex) { StingLog.Warn($"Preset dialog owner: {ex.Message}"); }
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                var preset = dlg.Chosen;
                var sharedValues = dlg.SharedValues;
                if (preset == null) return Result.Cancelled;

                int ok = 0, failed = 0, skipped = 0;
                using (var tg = new TransactionGroup(ctx.Doc, $"STING Preset: {preset.Label}"))
                {
                    tg.Start();
                    foreach (var step in preset.Steps)
                    {
                        try
                        {
                            var resolved = PresetCombinationEngine.ResolveParams(step.Params, sharedValues);
                            foreach (var kv in resolved)
                                StingCommandHandler.SetExtraParam($"Preset_{kv.Key}", kv.Value);
                            StingDockPanel.DispatchCommand(step.CommandTag);
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Preset step {step.CommandTag} failed: {ex.Message}");
                            if (!step.Optional) failed++;
                            else skipped++;
                        }
                    }
                    if (failed == 0) tg.Assimilate();
                    else tg.RollBack();
                }

                // onComplete chain — runs after the TransactionGroup commits
                if (failed == 0)
                {
                    foreach (var step in preset.OnComplete)
                    {
                        try { StingDockPanel.DispatchCommand(step.CommandTag); }
                        catch (Exception ex) { StingLog.Warn($"onComplete {step.CommandTag}: {ex.Message}"); }
                    }
                }

                StingResultPanel.Create($"Preset: {preset.Label}")
                    .SetSubtitle(preset.Description)
                    .AddSection("RESULT")
                    .Metric("Steps succeeded", ok.ToString())
                    .Metric("Steps failed",    failed.ToString())
                    .Metric("Steps skipped",   skipped.ToString())
                    .Metric("Rolled back",     failed > 0 ? "YES" : "no")
                    .Show();

                return failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { StingLog.Error("PresetCombinationCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    internal class PresetPickerDialog : Window
    {
        public PresetDefinition Chosen { get; private set; }
        public Dictionary<string, string> SharedValues { get; private set; } = new Dictionary<string, string>();

        private readonly List<PresetDefinition> _presets;
        private readonly StackPanel _paramsHost;
        private ListBox _list;

        public PresetPickerDialog(List<PresetDefinition> presets, Document doc)
        {
            _presets = presets;
            Title = "STING — Preset Combinations";
            Width = 680; Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xFA));

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0) };
            var header = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x5C)), Padding = new Thickness(14, 10, 14, 10) };
            header.Child = new TextBlock { Text = "PRESET COMBINATIONS", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

            var footer = new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Cancel", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var ok = new Button { Content = "Place ★", Width = 120, Height = 30, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0x91, 0x2D)), Foreground = Brushes.White, BorderThickness = new Thickness(0), IsDefault = true };
            ok.Click += (s, e) => { BuildSharedValues(); DialogResult = true; Close(); };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            row.Children.Add(cancel); row.Children.Add(ok);
            footer.Child = row; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

            var body = new Grid { Margin = new Thickness(12) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _list = new ListBox { FontSize = 12 };
            foreach (var p in _presets)
            {
                _list.Items.Add(new ListBoxItem { Content = p.Label, Tag = p });
            }
            _list.SelectionChanged += (s, e) => RebuildParams();
            Grid.SetColumn(_list, 0); body.Children.Add(_list);

            _paramsHost = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            var scroll = new ScrollViewer { Content = _paramsHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(scroll, 1); body.Children.Add(scroll);

            root.Children.Add(body);
            Content = root;
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void RebuildParams()
        {
            _paramsHost.Children.Clear();
            var sel = _list.SelectedItem as ListBoxItem;
            var preset = sel?.Tag as PresetDefinition;
            if (preset == null) return;
            Chosen = preset;

            _paramsHost.Children.Add(new TextBlock
            {
                Text = preset.Description, FontSize = 11, Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
            });
            foreach (var sp in preset.SharedParams)
            {
                _paramsHost.Children.Add(new TextBlock { Text = sp.Label, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) });
                if (sp.Choices != null && sp.Choices.Count > 0)
                {
                    var cb = new ComboBox { FontSize = 11, Height = 26, Tag = sp };
                    foreach (var c in sp.Choices) cb.Items.Add(c);
                    cb.SelectedItem = sp.DefaultValue;
                    _paramsHost.Children.Add(cb);
                }
                else
                {
                    var tb = new TextBox { Text = sp.DefaultValue ?? "", FontSize = 11, Height = 26, Tag = sp, Padding = new Thickness(4, 2, 4, 2) };
                    _paramsHost.Children.Add(tb);
                }
            }
        }

        private void BuildSharedValues()
        {
            SharedValues.Clear();
            if (Chosen == null) return;
            foreach (var child in _paramsHost.Children.OfType<FrameworkElement>())
            {
                PresetParam sp = null; string val = null;
                if (child is TextBox tb && tb.Tag is PresetParam p1) { sp = p1; val = tb.Text; }
                else if (child is ComboBox cb && cb.Tag is PresetParam p2) { sp = p2; val = cb.SelectedItem?.ToString() ?? p2.DefaultValue; }
                if (sp != null) SharedValues[sp.Name] = val ?? "";
            }
        }
    }
}
