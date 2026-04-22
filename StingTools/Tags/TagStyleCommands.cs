// ============================================================================
// TagStyleCommands.cs — IExternalCommand classes for Tag Style Control
//
// Commands:
//   1. ApplyTagStyleCommand — Pick a specific size×style×color combination
//   2. ApplyColorSchemeCommand — Apply a named color scheme (Discipline/Warm/Cool/etc.)
//   3. ClearColorSchemeCommand — Remove all graphic overrides from view
//   4. SetParagraphDepthExtCommand — Extended paragraph depth (1-10 tiers)
//   5. TagStyleReportCommand — Report current tag style status
//   6. SwitchTagStyleByDiscCommand — Discipline-aware tag style switching
//   7. BatchTagStyleCommand — Apply style across all views
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ══════════════════════════════════════════════════════════════════
    // 1. APPLY TAG STYLE — Pick size×style×color
    // ══════════════════════════════════════════════════════════════════

    // ── Tag Style Grid Dialog — replaces 3-step TaskDialog with single visual grid ──

    /// <summary>
    /// WPF visual color grid dialog for tag style selection.
    /// Shows a 4×3×8 matrix (sizes × styles × colors = 96 cells + 32 extended)
    /// with colored preview cells. User clicks one cell to select the combination.
    /// </summary>
    internal static class TagStyleGridDialog
    {
        private static readonly string[] Sizes = { "2", "2.5", "3", "3.5" };
        private static readonly string[] Styles = { "NOM", "BOLD", "ITALIC" };
        private static readonly string[] Colors = { "BLACK", "BLUE", "GREEN", "RED", "YELLOW", "ORANGE", "PURPLE", "WHITE" };

        private static readonly Dictionary<string, System.Windows.Media.Color> ColorMap =
            new Dictionary<string, System.Windows.Media.Color>(StringComparer.OrdinalIgnoreCase)
            {
                ["BLACK"]  = System.Windows.Media.Color.FromRgb(30, 30, 30),
                ["BLUE"]   = System.Windows.Media.Color.FromRgb(40, 100, 200),
                ["GREEN"]  = System.Windows.Media.Color.FromRgb(40, 160, 60),
                ["RED"]    = System.Windows.Media.Color.FromRgb(200, 40, 40),
                ["YELLOW"] = System.Windows.Media.Color.FromRgb(200, 180, 30),
                ["ORANGE"] = System.Windows.Media.Color.FromRgb(220, 120, 30),
                ["PURPLE"] = System.Windows.Media.Color.FromRgb(130, 50, 180),
                ["WHITE"]  = System.Windows.Media.Color.FromRgb(240, 240, 240),
            };

        /// <summary>
        /// Show the visual grid dialog. Returns (size, style, color) tuple, or null if cancelled.
        /// </summary>
        public static (string size, string style, string color)? Show()
        {
            (string size, string style, string color)? result = null;

            var window = new System.Windows.Window
            {
                Title = "Tag Style — Visual Grid",
                Width = 680, Height = 480,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(245, 245, 245))
            };

            // Set Revit as owner for modality
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"TagStyleGrid owner: {ex.Message}"); }

            var mainPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };

            // Header
            var header = new System.Windows.Controls.TextBlock
            {
                Text = "Select tag style: Size × Weight × Color",
                FontSize = 16, FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 10)
            };
            mainPanel.Children.Add(header);

            // Column headers (colors)
            var colHeaderGrid = new System.Windows.Controls.Grid();
            colHeaderGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(90) });
            foreach (var color in Colors)
            {
                colHeaderGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(65) });
                var colLabel = new System.Windows.Controls.TextBlock
                {
                    Text = color, FontSize = 10, FontWeight = System.Windows.FontWeights.SemiBold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Foreground = new System.Windows.Media.SolidColorBrush(ColorMap.TryGetValue(color, out var c) ? c : System.Windows.Media.Colors.Black)
                };
                System.Windows.Controls.Grid.SetColumn(colLabel, Colors.ToList().IndexOf(color) + 1);
                colHeaderGrid.Children.Add(colLabel);
            }
            mainPanel.Children.Add(colHeaderGrid);

            // Grid cells: rows = Size×Style combos, cols = Colors
            foreach (string size in Sizes)
            {
                foreach (string style in Styles)
                {
                    var rowGrid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 1, 0, 1) };
                    rowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(90) });

                    // Row label
                    string sizeLabel = $"{size}mm {style}";
                    var rowLabel = new System.Windows.Controls.TextBlock
                    {
                        Text = sizeLabel, FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        FontStyle = style == "ITALIC" ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
                        FontWeight = style == "BOLD" ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                    };
                    System.Windows.Controls.Grid.SetColumn(rowLabel, 0);
                    rowGrid.Children.Add(rowLabel);

                    for (int ci = 0; ci < Colors.Length; ci++)
                    {
                        string color = Colors[ci];
                        rowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(65) });

                        var cellColor = ColorMap.TryGetValue(color, out var mc) ? mc : System.Windows.Media.Colors.Gray;
                        var btn = new System.Windows.Controls.Button
                        {
                            Width = 58, Height = 22,
                            Background = new System.Windows.Media.SolidColorBrush(cellColor),
                            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DarkGray),
                            BorderThickness = new System.Windows.Thickness(1),
                            Content = new System.Windows.Controls.TextBlock
                            {
                                Text = "Aa", FontSize = double.Parse(size, System.Globalization.CultureInfo.InvariantCulture) * 3.5,
                                Foreground = new System.Windows.Media.SolidColorBrush(
                                    color == "WHITE" || color == "YELLOW" ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White),
                                FontStyle = style == "ITALIC" ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal,
                                FontWeight = style == "BOLD" ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                            },
                            Cursor = System.Windows.Input.Cursors.Hand,
                            ToolTip = $"TAG_{size.Replace(".", "")}{style}_{color}_BOOL"
                        };

                        string capturedSize = size, capturedStyle = style, capturedColor = color;
                        btn.Click += (s, e) =>
                        {
                            result = (capturedSize, capturedStyle, capturedColor);
                            window.DialogResult = true;
                            window.Close();
                        };

                        System.Windows.Controls.Grid.SetColumn(btn, ci + 1);
                        rowGrid.Children.Add(btn);
                    }

                    mainPanel.Children.Add(rowGrid);
                }

                // Separator between size groups
                mainPanel.Children.Add(new System.Windows.Controls.Separator
                {
                    Margin = new System.Windows.Thickness(0, 4, 0, 4),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray)
                });
            }

            // Cancel button
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel", Width = 100, Height = 30,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(0, 8, 0, 0)
            };
            cancelBtn.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            mainPanel.Children.Add(cancelBtn);

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = mainPanel
            };
            window.Content = scroll;
            window.ShowDialog();

            return result;
        }
    }

    /// <summary>
    /// Applies a specific tag style (size, weight, color) to all element types.
    /// Uses a visual color grid dialog instead of 3 sequential TaskDialogs.
    /// Sets exactly one TAG_{SIZE}{STYLE}_{COLOR}_BOOL to true, making that
    /// label row visible in tag families.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyTagStyleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            // Single-step visual grid dialog replaces 3-step TaskDialog flow
            var selection = TagStyleGridDialog.Show();
            if (selection == null) return Result.Cancelled;

            string size = selection.Value.size;
            string style = selection.Value.style;
            string color = selection.Value.color;

            var preset = new StylePreset { Name = $"{size}{style}_{color}", Size = size, Style = style, Color = color };

            using (Transaction tx = new Transaction(doc, $"STING Tag Style: {preset.TypeName}"))
            {
                tx.Start();
                int updated = TagStyleEngine.ApplyTagStyle(doc, preset);
                tx.Commit();

                var diag = TagStyleEngine.LastApplyDiagnostics;
                var body = new System.Text.StringBuilder();
                body.AppendLine($"Style: {preset.TypeName}");
                body.AppendLine($"Parameter: {preset.ParamName}");
                body.AppendLine($"Element types updated: {updated}");
                if (updated == 0 && diag != null)
                {
                    body.AppendLine();
                    body.AppendLine($"Scanned {diag.Scanned} element types.");
                    if (diag.HadAnyStyleParam == 0)
                    {
                        body.AppendLine("None of them carry any TAG_*_BOOL style parameter.");
                        body.AppendLine("Fix: load STING-compatible tag families, or run");
                        body.AppendLine("Family Parameter Creator to inject style params into existing families.");
                    }
                    else if (diag.MissingActiveParam > 0)
                    {
                        body.AppendLine($"{diag.HadAnyStyleParam} types carry style params, but " +
                                         $"{diag.MissingActiveParam} of them lack '{diag.ActiveParam}'.");
                        body.AppendLine("Fix: run Family Parameter Creator to inject the missing style parameter.");
                    }
                    else
                    {
                        body.AppendLine("All style parameters already match the requested style — nothing to change.");
                    }
                }
                TaskDialog.Show("Tag Style Applied", body.ToString());
            }

            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 2. APPLY COLOR SCHEME — View-level discipline coloring
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a named color scheme to the active view, coloring elements by discipline
    /// and optionally switching tag styles to match (as seen in the presentation screenshots).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyColorSchemeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            var view = doc.ActiveView;
            if (view == null || view is ViewSheet)
            {
                TaskDialog.Show("Color Scheme", "Please switch to a model view (not a sheet).");
                return Result.Failed;
            }

            var dlg = new TaskDialog("Color Scheme");
            dlg.MainInstruction = "Select a view color scheme:";
            dlg.MainContent = "Colors all elements by discipline and switches tag text styles to match.\n" +
                "These schemes match the STING presentation templates.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Discipline (Standard)",
                "M=Blue, E=Gold, P=Green, A=Grey, S=Red — ISO 19650 default");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Warm (Salmon/Terracotta)",
                "Warm tones — architectural presentation");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Cool (Green/Teal)",
                "Cool tones — MEP and environmental presentation");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "More schemes...",
                "Red, Yellow, Blue, Monochrome, Dark");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = dlg.Show();
            string schemeName;
            switch (choice)
            {
                case TaskDialogResult.CommandLink1: schemeName = "Discipline"; break;
                case TaskDialogResult.CommandLink2: schemeName = "Warm"; break;
                case TaskDialogResult.CommandLink3: schemeName = "Cool"; break;
                case TaskDialogResult.CommandLink4:
                    schemeName = PickExtendedScheme();
                    if (schemeName == null) return Result.Cancelled;
                    break;
                default: return Result.Cancelled;
            }

            if (!TagStyleEngine.BuiltInSchemes.TryGetValue(schemeName, out ColorScheme scheme))
            {
                TaskDialog.Show("Color Scheme", $"Scheme '{schemeName}' not found.");
                return Result.Failed;
            }

            using (Transaction tx = new Transaction(doc, $"STING Color Scheme: {schemeName}"))
            {
                tx.Start();

                // Apply view element colors
                int colored = TagStyleEngine.ApplyColorScheme(doc, view, scheme);

                // Apply discipline tag styles if the scheme has them
                int styled = 0;
                if (scheme.DisciplineTagStyles.Count > 0)
                    styled = TagStyleEngine.ApplyDisciplineTagStyles(doc, scheme);

                tx.Commit();

                TaskDialog.Show("Color Scheme Applied",
                    $"Scheme: {scheme.Name}\n" +
                    $"{scheme.Description}\n\n" +
                    $"Elements colored: {colored}\n" +
                    $"Tag styles switched: {styled}");
            }

            return Result.Succeeded;
        }

        private static string PickExtendedScheme()
        {
            var dlg = new TaskDialog("Extended Color Schemes");
            dlg.MainInstruction = "Select an extended scheme:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Red (Structural)", "Bold red tones");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Yellow (Electrical)", "Amber/gold tones");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Monochrome (Print)", "Black and white — print-ready");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Dark (Inverted)", "Dark background presentation");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: return "Red";
                case TaskDialogResult.CommandLink2: return "Yellow";
                case TaskDialogResult.CommandLink3: return "Mono";
                case TaskDialogResult.CommandLink4: return "Dark";
                default: return null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 3. CLEAR COLOR SCHEME — Remove all overrides
    // ══════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearColorSchemeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var view = doc.ActiveView;
            using (Transaction tx = new Transaction(doc, "STING Clear Color Scheme"))
            {
                tx.Start();
                int cleared = TagStyleEngine.ClearColorScheme(doc, view);
                tx.Commit();
                TaskDialog.Show("Clear Color Scheme", $"Cleared overrides from {cleared} elements.");
            }

            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 4. EXTENDED PARAGRAPH DEPTH — 1-10 tiers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extended paragraph depth control with 10 tiers (up from original 3).
    /// Sets TAG_PARA_STATE_1_BOOL through TAG_PARA_STATE_10_BOOL.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetParagraphDepthExtCommand : IExternalCommand
    {
        private static readonly string[] TierLabels =
        {
            "", // 0 unused
            ".01 Minimal — Tag code only",
            ".02 Compact — Code + type name",
            ".03 Comprehensive — Standard engineering detail",
            ".04 Extended — MEP specs added",
            ".05 Moderate — Material properties",
            ".06 Detailed — Classification codes",
            ".07 Specification — BOQ data",
            ".08 Full detail — Maintenance info",
            ".09 Complete — Lifecycle data",
            ".10 Maximum — All tiers visible",
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            // Task 8: deprecation notice.  Paragraph depth is now a TYPE property
            // cached on every tag variant by MigrateTagFamiliesCommand (variants are
            // named "{size}_{style}_{colour}_{arrow}_T{n}" — T1..T10).  The placement
            // path picks the right variant before IndependentTag.Create based on the
            // ParaDepth ExtraParam from the Tag Studio slider — so there is no need
            // to mutate PARA_STATE_* bools on instances anymore.
            //
            // Bulk-writing to PARA_STATE bools on every element type is the legacy
            // behaviour and only works when those params are actually bound to the
            // type, which we now guarantee via Create/Migrate Tag Families.
            var deprNotice = new TaskDialog("Paragraph Depth — deprecated entry point");
            deprNotice.MainInstruction = "This command writes PARA_STATE_* directly on element types.";
            deprNotice.MainContent =
                "Depth is now type-based: every tag family variant carries its depth tier " +
                "in its name (e.g. '2.5_BOLD_RED_Filled30_T3') and in TAG_DEPTH_TIER_INT.\n\n" +
                "The Tag Studio ParaDepth slider picks the correct variant at placement time. " +
                "Prefer the Tag Studio 'Apply style' / 'Apply scheme' path, or use the ParaDepth " +
                "slider before SmartPlace / BatchPlace / Tag & Combine.\n\n" +
                "Continue with the legacy bulk-write anyway?";
            deprNotice.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (deprNotice.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            // Unified WPF slider dialog replacing 2-3 step TaskDialog chain
            var result = ShowParagraphDepthDialog();
            if (result == null) return Result.Cancelled;

            int maxTier = result.Value.tier;
            bool warn = result.Value.warnings;

            using (Transaction tx = new Transaction(doc, $"STING Paragraph Depth: Tier .{maxTier:D2}"))
            {
                tx.Start();
                int updated = TagStyleEngine.SetParagraphDepth(doc, maxTier, warn);
                tx.Commit();

                TaskDialog.Show("Paragraph Depth",
                    $"Depth: Tier .{maxTier:D2} of .10\n" +
                    $"Warnings: {(warn ? "ON" : "OFF")}\n" +
                    $"Element types updated: {updated}");
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Unified WPF dialog with slider + preset buttons replacing 2-3 sequential TaskDialogs.
        /// </summary>
        private static (int tier, bool warnings)? ShowParagraphDepthDialog()
        {
            (int tier, bool warnings)? result = null;

            var window = new System.Windows.Window
            {
                Title = "Paragraph Depth — Extended",
                Width = 480, Height = 380,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(245, 245, 245))
            };
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"ParagraphDepth dialog owner: {ex.Message}"); }

            var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };

            // Header
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Tag Label Visibility Depth",
                FontSize = 16, FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Each tier adds more detail to tag labels. Higher = more information.",
                FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new System.Windows.Thickness(0, 0, 0, 12)
            });

            // Tier label (updated by slider)
            var tierLabel = new System.Windows.Controls.TextBlock
            {
                Text = TierLabels[3], FontSize = 13, FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 145, 45)),
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(tierLabel);

            // Slider
            var slider = new System.Windows.Controls.Slider
            {
                Minimum = 1, Maximum = 10, Value = 3,
                TickFrequency = 1, IsSnapToTickEnabled = true,
                TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                Margin = new System.Windows.Thickness(0, 0, 0, 4)
            };
            slider.ValueChanged += (s, e) =>
            {
                int v = (int)slider.Value;
                if (v >= 1 && v <= 10) tierLabel.Text = TierLabels[v];
            };
            panel.Children.Add(slider);

            // Scale labels
            var scaleGrid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 12) };
            scaleGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            scaleGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            scaleGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            var lMin = new System.Windows.Controls.TextBlock { Text = "Minimal", FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray };
            var lMid = new System.Windows.Controls.TextBlock { Text = "Standard", FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var lMax = new System.Windows.Controls.TextBlock { Text = "Maximum", FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            System.Windows.Controls.Grid.SetColumn(lMin, 0);
            System.Windows.Controls.Grid.SetColumn(lMid, 1);
            System.Windows.Controls.Grid.SetColumn(lMax, 2);
            scaleGrid.Children.Add(lMin); scaleGrid.Children.Add(lMid); scaleGrid.Children.Add(lMax);
            panel.Children.Add(scaleGrid);

            // Preset buttons
            var presetPanel = new System.Windows.Controls.WrapPanel { Margin = new System.Windows.Thickness(0, 0, 0, 12) };
            foreach (var (label, val) in new[] { ("Compact", 3), ("Extended", 6), ("Full", 10) })
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = $"{label} (.{val:D2})", Width = 120, Height = 28,
                    Margin = new System.Windows.Thickness(0, 0, 8, 0)
                };
                int capturedVal = val;
                btn.Click += (s, e) => slider.Value = capturedVal;
                presetPanel.Children.Add(btn);
            }
            panel.Children.Add(presetPanel);

            // Warnings checkbox
            var chkWarn = new System.Windows.Controls.CheckBox
            {
                Content = "Show tag warnings (tiers .02+)", IsChecked = true,
                Margin = new System.Windows.Thickness(0, 0, 0, 16), FontSize = 12
            };
            panel.Children.Add(chkWarn);

            // Buttons
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var applyBtn = new System.Windows.Controls.Button
            {
                Content = "Apply", Width = 100, Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 145, 45)),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 8, 0)
            };
            applyBtn.Click += (s, e) =>
            {
                result = ((int)slider.Value, chkWarn.IsChecked == true);
                window.DialogResult = true;
                window.Close();
            };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, Height = 32 };
            cancelBtn.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            btnPanel.Children.Add(applyBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 5. TAG STYLE REPORT
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reports which tag styles are currently active across element types.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagStyleReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            string report = TagStyleEngine.GenerateStyleReport(doc);
            TaskDialog.Show("Tag Style Report", report);
            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 6. DISCIPLINE-AWARE TAG STYLE
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Switches tag styles based on element discipline codes.
    /// Each discipline gets its own size×style×color combination.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwitchTagStyleByDiscCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            if (!TagStyleEngine.BuiltInSchemes.TryGetValue("Discipline", out ColorScheme scheme))
            {
                TaskDialog.Show("Error", "Discipline scheme not found.");
                return Result.Failed;
            }

            using (Transaction tx = new Transaction(doc, "STING Disc Tag Styles"))
            {
                tx.Start();
                int updated = TagStyleEngine.ApplyDisciplineTagStyles(doc, scheme);
                tx.Commit();

                TaskDialog.Show("Discipline Tag Styles",
                    $"Applied discipline-aware tag styles:\n" +
                    $"  M (Mechanical) → 2pt Bold Blue\n" +
                    $"  E (Electrical) → 2pt Bold Red\n" +
                    $"  P (Plumbing) → 2pt Bold Green\n" +
                    $"  A (Architecture) → 2pt Normal Black\n" +
                    $"  S (Structural) → 2pt Bold Red\n\n" +
                    $"Element types updated: {updated}");
            }

            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 7. BATCH TAG STYLE — Apply across all views
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Apply a color scheme to ALL non-template views in the project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchApplyColorSchemeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var dlg = new TaskDialog("Batch Color Scheme");
            dlg.MainInstruction = "Apply color scheme to ALL views?";
            dlg.MainContent = "This will color elements in every non-template view.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Discipline (Standard)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Monochrome (Print-ready)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Clear ALL overrides");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = dlg.Show();
            string schemeName = null;
            bool clearMode = false;
            switch (choice)
            {
                case TaskDialogResult.CommandLink1: schemeName = "Discipline"; break;
                case TaskDialogResult.CommandLink2: schemeName = "Mono"; break;
                case TaskDialogResult.CommandLink3: clearMode = true; break;
                default: return Result.Cancelled;
            }

            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Schedule &&
                           v.ViewType != ViewType.DrawingSheet)
                .ToList();

            using (Transaction tx = new Transaction(doc, clearMode ? "STING Clear All Schemes" : $"STING Batch {schemeName}"))
            {
                tx.Start();
                int totalColored = 0;

                if (clearMode)
                {
                    foreach (var v in allViews)
                        totalColored += TagStyleEngine.ClearColorScheme(doc, v);
                }
                else
                {
                    var scheme = TagStyleEngine.BuiltInSchemes[schemeName];
                    foreach (var v in allViews)
                    {
                        try { totalColored += TagStyleEngine.ApplyColorScheme(doc, v, scheme); }
                        catch (Exception ex) { StingLog.Warn($"Skip view '{v.Name}': {ex.Message}"); }
                    }
                }

                tx.Commit();

                TaskDialog.Show("Batch Color Scheme",
                    $"{(clearMode ? "Cleared" : $"Scheme: {schemeName}")}\n" +
                    $"Views processed: {allViews.Count}\n" +
                    $"Elements affected: {totalColored}");
            }

            return Result.Succeeded;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 8. COLOR BY VARIABLE — Any tag variable → color + style + box
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Color elements and switch tag styles based on ANY tag variable:
    /// Discipline, System, Status, Zone, Level, Function, or Location.
    /// Box colors match element colors but are controlled separately.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorByVariableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            var view = doc.ActiveView;
            if (view == null || view is ViewSheet)
            {
                TaskDialog.Show("Color By Variable", "Please switch to a model view.");
                return Result.Failed;
            }

            // Unified single-dialog: variable + apply options
            var result = ShowColorByVariableDialog();
            if (result == null) return Result.Cancelled;

            string schemeName = result.Value.scheme;
            bool doElements = result.Value.doElements;
            bool doStyles = result.Value.doStyles;
            bool doBoxes = result.Value.doBoxes;

            if (!TagStyleEngine.VariableSchemes.TryGetValue(schemeName, out VariableColorScheme scheme))
            {
                TaskDialog.Show("Error", $"Scheme '{schemeName}' not found.");
                return Result.Failed;
            }

            using (Transaction tx = new Transaction(doc, $"STING Color By {scheme.Name}"))
            {
                tx.Start();

                int colored = 0, styled = 0, boxed = 0;

                if (doElements)
                    colored = TagStyleEngine.ApplyVariableScheme(doc, view, scheme);

                if (doStyles && scheme.ValueStyles.Count > 0)
                    styled = TagStyleEngine.ApplyVariableTagStyles(doc, scheme);

                if (doBoxes && scheme.ValueBoxColors.Count > 0)
                    boxed = TagStyleEngine.ApplyBoxColorsByVariable(doc, view, scheme);

                tx.Commit();

                var report = new System.Text.StringBuilder();
                report.AppendLine($"Variable: {scheme.Variable}");
                report.AppendLine($"Scheme: {scheme.Name}");
                report.AppendLine($"{scheme.Description}\n");
                if (doElements) report.AppendLine($"Elements colored: {colored}");
                if (doStyles) report.AppendLine($"Tag styles switched: {styled}");
                if (doBoxes) report.AppendLine($"Box colors set: {boxed}");
                report.AppendLine($"\nColor meanings:");
                foreach (var kv in scheme.ValueColors.Take(8))
                    report.AppendLine($"  {kv.Key} → RGB({kv.Value.Red},{kv.Value.Green},{kv.Value.Blue})");
                if (scheme.ValueColors.Count > 8)
                    report.AppendLine($"  ... +{scheme.ValueColors.Count - 8} more");

                TaskDialog.Show("Color By Variable", report.ToString());
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Unified WPF dialog replacing 3 sequential TaskDialogs.
        /// Left column: variable selector (radio buttons with descriptions).
        /// Right column: apply mode checkboxes (Elements, Tag Styles, Box Colors).
        /// </summary>
        private static (string scheme, bool doElements, bool doStyles, bool doBoxes)? ShowColorByVariableDialog()
        {
            (string scheme, bool doElements, bool doStyles, bool doBoxes)? result = null;

            var window = new System.Windows.Window
            {
                Title = "Color By Variable",
                Width = 560, Height = 420,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(245, 245, 245))
            };
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"ColorByVariable dialog owner: {ex.Message}"); }

            var mainGrid = new System.Windows.Controls.Grid();
            mainGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(200) });
            mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(50) });
            mainGrid.Margin = new System.Windows.Thickness(12);

            // Left: Variable selector
            var varPanel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(0, 0, 8, 0) };
            var varHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Color Variable", FontSize = 14, FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            varPanel.Children.Add(varHeader);

            var variables = new[]
            {
                ("System", "System Type (HVAC/DCW/SAN/FP/LV...)", "16 MEP system types"),
                ("Status", "Status (NEW/EXISTING/DEMOLISHED/TEMP)", "4 lifecycle states"),
                ("Zone", "Zone (Z01/Z02/Z03/Z04)", "Spatial zones"),
                ("Level", "Level (GF/L01/L02/B1/RF)", "Building levels"),
                ("Location", "Location (BLD1/BLD2/BLD3/EXT)", "Building locations"),
                ("Function", "Function (SUP/HTG/PWR/LTG...)", "12 CIBSE function types"),
            };

            System.Windows.Controls.RadioButton firstRadio = null;
            var radioGroup = new System.Windows.Controls.StackPanel();
            foreach (var (key, label, desc) in variables)
            {
                var radio = new System.Windows.Controls.RadioButton
                {
                    Content = new System.Windows.Controls.StackPanel
                    {
                        Children =
                        {
                            new System.Windows.Controls.TextBlock { Text = label, FontWeight = System.Windows.FontWeights.SemiBold, FontSize = 12 },
                            new System.Windows.Controls.TextBlock { Text = desc, FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray }
                        }
                    },
                    Tag = key,
                    Margin = new System.Windows.Thickness(0, 4, 0, 4),
                    GroupName = "Variable"
                };
                if (firstRadio == null) { firstRadio = radio; radio.IsChecked = true; }
                radioGroup.Children.Add(radio);
            }
            varPanel.Children.Add(radioGroup);
            System.Windows.Controls.Grid.SetColumn(varPanel, 0);
            mainGrid.Children.Add(varPanel);

            // Right: Apply options
            var optPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new System.Windows.Thickness(8, 0, 0, 0)
            };
            var optHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Apply To", FontSize = 14, FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };
            optPanel.Children.Add(optHeader);

            var chkElements = new System.Windows.Controls.CheckBox
            {
                Content = "Element Colors", IsChecked = true,
                Margin = new System.Windows.Thickness(0, 6, 0, 2), FontSize = 12
            };
            var chkStyles = new System.Windows.Controls.CheckBox
            {
                Content = "Tag Text Styles", IsChecked = true,
                Margin = new System.Windows.Thickness(0, 2, 0, 2), FontSize = 12
            };
            var chkBoxes = new System.Windows.Controls.CheckBox
            {
                Content = "Box Colors", IsChecked = true,
                Margin = new System.Windows.Thickness(0, 2, 0, 2), FontSize = 12
            };
            optPanel.Children.Add(chkElements);
            optPanel.Children.Add(chkStyles);
            optPanel.Children.Add(chkBoxes);

            // Quick presets
            optPanel.Children.Add(new System.Windows.Controls.Separator { Margin = new System.Windows.Thickness(0, 12, 0, 8) });
            var presetLabel = new System.Windows.Controls.TextBlock
            {
                Text = "Quick Presets", FontSize = 12, FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new System.Windows.Thickness(0, 0, 0, 4)
            };
            optPanel.Children.Add(presetLabel);

            var btnFull = new System.Windows.Controls.Button { Content = "Full (all)", Margin = new System.Windows.Thickness(0, 2, 0, 2) };
            btnFull.Click += (s, e) => { chkElements.IsChecked = true; chkStyles.IsChecked = true; chkBoxes.IsChecked = true; };
            var btnElemOnly = new System.Windows.Controls.Button { Content = "Elements only", Margin = new System.Windows.Thickness(0, 2, 0, 2) };
            btnElemOnly.Click += (s, e) => { chkElements.IsChecked = true; chkStyles.IsChecked = false; chkBoxes.IsChecked = false; };
            var btnTagOnly = new System.Windows.Controls.Button { Content = "Tags only", Margin = new System.Windows.Thickness(0, 2, 0, 2) };
            btnTagOnly.Click += (s, e) => { chkElements.IsChecked = false; chkStyles.IsChecked = true; chkBoxes.IsChecked = true; };
            optPanel.Children.Add(btnFull);
            optPanel.Children.Add(btnElemOnly);
            optPanel.Children.Add(btnTagOnly);

            System.Windows.Controls.Grid.SetColumn(optPanel, 1);
            mainGrid.Children.Add(optPanel);

            // Bottom: Apply + Cancel
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var applyBtn = new System.Windows.Controls.Button
            {
                Content = "Apply", Width = 100, Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 145, 45)),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 8, 0)
            };
            applyBtn.Click += (s, e) =>
            {
                string selected = null;
                foreach (System.Windows.Controls.RadioButton rb in radioGroup.Children)
                    if (rb.IsChecked == true) { selected = rb.Tag as string; break; }
                if (selected == null) return;
                result = (selected, chkElements.IsChecked == true, chkStyles.IsChecked == true, chkBoxes.IsChecked == true);
                window.DialogResult = true;
                window.Close();
            };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, Height = 32 };
            cancelBtn.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            btnPanel.Children.Add(applyBtn);
            btnPanel.Children.Add(cancelBtn);
            System.Windows.Controls.Grid.SetRow(btnPanel, 1);
            System.Windows.Controls.Grid.SetColumnSpan(btnPanel, 2);
            mainGrid.Children.Add(btnPanel);

            window.Content = mainGrid;
            window.ShowDialog();
            return result;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 9. SET BOX COLOR — Direct bounding box color control
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set tag bounding box fill color for selected elements.
    /// Box color is independent of text color — e.g. blue box with red text.
    /// Colors are semantically linked: DISC colors match box colors by default.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetBoxColorCommand : IExternalCommand
    {
        private static readonly (string name, byte r, byte g, byte b)[] BoxColors =
        {
            ("Blue (Mechanical)", 200, 220, 255),
            ("Green (Plumbing)", 200, 255, 220),
            ("Red (Structural/Fire)", 255, 200, 200),
            ("Orange (Electrical/FP)", 255, 230, 200),
            ("Purple (Low Voltage)", 220, 200, 255),
            ("Grey (General)", 220, 220, 220),
            ("Yellow (Comms)", 255, 255, 200),
            ("Cyan (HVAC)", 200, 245, 255),
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            var selection = uidoc.Selection.GetElementIds();

            // Unified WPF dialog: mode + color picker in one step
            var result = ShowBoxColorDialog();
            if (result == null) return Result.Cancelled;

            string mode = result.Value.mode;
            BoxColorPreset colorPreset = result.Value.preset;

            using (Transaction tx = new Transaction(doc, "STING Set Box Color"))
            {
                tx.Start();
                int updated = 0;

                var targets = selection.Count > 0
                    ? selection.Select(id => doc.GetElement(id)).Where(e => e != null).ToList()
                    : new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType().ToList();

                if (mode == "Auto")
                {
                    var discScheme = TagStyleEngine.BuiltInSchemes.TryGetValue("Discipline", out var ds) ? ds : null;
                    foreach (var el in targets)
                    {
                        string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                        if (string.IsNullOrEmpty(disc)) continue;
                        if (discScheme?.DisciplineColors.TryGetValue(disc, out Color dc) == true)
                        {
                            var preset = new BoxColorPreset { Name = disc, R = dc.Red, G = dc.Green, B = dc.Blue };
                            TagStyleEngine.SetBoxColor(el, preset);
                            TagStyleEngine.SetLeaderColor(el, preset);
                            updated++;
                        }
                    }
                }
                else if (mode == "Pick" && colorPreset != null)
                {
                    foreach (var el in targets)
                    {
                        TagStyleEngine.SetBoxColor(el, colorPreset);
                        updated++;
                    }
                }
                else if (mode == "Clear")
                {
                    var clear = new BoxColorPreset { R = 0, G = 0, B = 0, BoxVisible = false, BoxStyle = "NONE" };
                    foreach (var el in targets)
                    {
                        TagStyleEngine.SetBoxColor(el, clear);
                        updated++;
                    }
                }

                tx.Commit();
                TaskDialog.Show("Box Color", $"Updated {updated} elements.");
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Unified WPF dialog with mode selector + color swatch grid, replacing 2-step TaskDialog.
        /// </summary>
        private static (string mode, BoxColorPreset preset)? ShowBoxColorDialog()
        {
            (string mode, BoxColorPreset preset)? result = null;

            var window = new System.Windows.Window
            {
                Title = "Tag Box Color",
                Width = 420, Height = 340,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(245, 245, 245))
            };
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"BoxColor dialog owner: {ex.Message}"); }

            var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };

            // Header
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Tag Box Color", FontSize = 16, FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 0, 12)
            });

            // Mode selector
            var radioAuto = new System.Windows.Controls.RadioButton
            {
                Content = "Auto (by Discipline)", GroupName = "Mode", IsChecked = true,
                Margin = new System.Windows.Thickness(0, 0, 0, 4), FontSize = 12
            };
            var radioPick = new System.Windows.Controls.RadioButton
            {
                Content = "Pick a color", GroupName = "Mode",
                Margin = new System.Windows.Thickness(0, 0, 0, 4), FontSize = 12
            };
            var radioClear = new System.Windows.Controls.RadioButton
            {
                Content = "Clear box colors", GroupName = "Mode",
                Margin = new System.Windows.Thickness(0, 0, 0, 8), FontSize = 12
            };
            panel.Children.Add(radioAuto);
            panel.Children.Add(radioPick);
            panel.Children.Add(radioClear);

            // Color swatch grid (visible when Pick is selected)
            var swatchGrid = new System.Windows.Controls.WrapPanel
            {
                Margin = new System.Windows.Thickness(20, 4, 0, 12),
                Visibility = System.Windows.Visibility.Collapsed
            };

            BoxColorPreset selectedPreset = null;
            System.Windows.Controls.Border selectedBorder = null;

            foreach (var (name, r, g, b) in BoxColors)
            {
                var swatch = new System.Windows.Controls.Border
                {
                    Width = 70, Height = 40,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
                    BorderBrush = System.Windows.Media.Brushes.DarkGray,
                    BorderThickness = new System.Windows.Thickness(1),
                    Margin = new System.Windows.Thickness(2),
                    CornerRadius = new System.Windows.CornerRadius(3),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = name.Split('(')[0].Trim(), FontSize = 9,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    },
                    ToolTip = $"{name} — RGB({r},{g},{b})"
                };
                var capturedName = name; byte cr = r, cg = g, cb = b;
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    if (selectedBorder != null)
                        selectedBorder.BorderBrush = System.Windows.Media.Brushes.DarkGray;
                    selectedBorder = swatch;
                    swatch.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 145, 45));
                    swatch.BorderThickness = new System.Windows.Thickness(3);
                    selectedPreset = new BoxColorPreset { Name = capturedName, R = cr, G = cg, B = cb };
                };
                swatchGrid.Children.Add(swatch);
            }
            panel.Children.Add(swatchGrid);

            // Toggle swatch visibility based on radio selection
            radioPick.Checked += (s, e) => swatchGrid.Visibility = System.Windows.Visibility.Visible;
            radioAuto.Checked += (s, e) => swatchGrid.Visibility = System.Windows.Visibility.Collapsed;
            radioClear.Checked += (s, e) => swatchGrid.Visibility = System.Windows.Visibility.Collapsed;

            // Buttons
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(0, 8, 0, 0)
            };
            var applyBtn = new System.Windows.Controls.Button
            {
                Content = "Apply", Width = 100, Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 145, 45)),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = System.Windows.FontWeights.Bold,
                Margin = new System.Windows.Thickness(0, 0, 8, 0)
            };
            applyBtn.Click += (s, e) =>
            {
                string mode = radioAuto.IsChecked == true ? "Auto" : radioPick.IsChecked == true ? "Pick" : "Clear";
                if (mode == "Pick" && selectedPreset == null)
                {
                    System.Windows.MessageBox.Show("Please click a color swatch.", "Box Color");
                    return;
                }
                result = (mode, selectedPreset);
                window.DialogResult = true;
                window.Close();
            };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, Height = 32 };
            cancelBtn.Click += (s, e) => { window.DialogResult = false; window.Close(); };
            btnPanel.Children.Add(applyBtn);
            btnPanel.Children.Add(cancelBtn);
            panel.Children.Add(btnPanel);

            window.Content = panel;
            window.ShowDialog();
            return result;
        }
    }

    /// <summary>
    /// Set a per-view tag style preference. When set, batch style commands
    /// will use this view's preferred style instead of the global selection.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SetViewTagStyleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;
            if (view == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }

            var td = new TaskDialog("STING — View Tag Style");
            td.MainInstruction = "Select tag style for this view";
            td.MainContent = $"Current view: {view.Name}";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Discipline (M=Blue, E=Gold, P=Green...)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Monochrome (black on white)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Warm (red/orange/yellow)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Cool (blue/cyan/mint)");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            var result = td.Show();

            string styleName;
            switch (result)
            {
                case TaskDialogResult.CommandLink1: styleName = "Discipline"; break;
                case TaskDialogResult.CommandLink2: styleName = "Monochrome"; break;
                case TaskDialogResult.CommandLink3: styleName = "Warm"; break;
                case TaskDialogResult.CommandLink4: styleName = "Cool"; break;
                default: return Result.Cancelled;
            }

            // Pre-check for the binding BEFORE the write transaction so we can offer
            // inline remediation instead of a dead-end "Run Load Parameters first" message.
            bool parameterBound = view.LookupParameter(ParamRegistry.VIEW_TAG_STYLE) != null;
            if (!parameterBound)
            {
                var remediation = new TaskDialog("STING — Parameter not bound");
                remediation.MainInstruction = "STING_VIEW_TAG_STYLE is not bound to the View category.";
                remediation.MainContent =
                    "Per-view tag styles need this parameter bound to OST_Views first. " +
                    "STING can run the shared-parameter loader now (safe and reversible).";
                remediation.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Bind parameters now", "Runs Load Shared Parameters, then retries the style change.");
                remediation.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Cancel");
                var r = remediation.Show();
                if (r != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                try
                {
                    var loader = new LoadSharedParamsCommand();
                    string loadMsg = null;
                    var loadElems = new ElementSet();
                    loader.Execute(commandData, ref loadMsg, loadElems);
                }
                catch (Exception loadEx)
                {
                    StingLog.Error("SetViewTagStyle: LoadSharedParams bind failed", loadEx);
                    TaskDialog.Show("STING",
                        $"Parameter binding failed: {loadEx.Message}\n\n" +
                        "Open Manage → Project Parameters and bind STING_VIEW_TAG_STYLE to the Views category manually.");
                    return Result.Failed;
                }

                parameterBound = view.LookupParameter(ParamRegistry.VIEW_TAG_STYLE) != null;
                if (!parameterBound)
                {
                    TaskDialog.Show("STING",
                        "STING_VIEW_TAG_STYLE is still not bound to the View category after running Load Parameters.\n\n" +
                        "Open Manage → Project Parameters and verify the Views category is ticked when binding " +
                        "STING_VIEW_TAG_STYLE from the shared-parameter file.");
                    return Result.Failed;
                }
            }

            string resultMsg = null;
            using (Transaction tx = new Transaction(doc, "STING Set View Tag Style"))
            {
                tx.Start();
                try
                {
                    Parameter p = view.LookupParameter(ParamRegistry.VIEW_TAG_STYLE);
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set(styleName);
                        resultMsg = $"View '{view.Name}' tag style set to: {styleName}";
                    }
                    else
                    {
                        resultMsg = "STING_VIEW_TAG_STYLE is bound but read-only on this view — " +
                                    "check that the parameter is not marked read-only in the shared-parameter file.";
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Error("SetViewTagStyle", ex);
                    resultMsg = $"Failed: {ex.Message}";
                }
                tx.Commit();
            }
            if (resultMsg != null)
                TaskDialog.Show("STING", resultMsg);
            return Result.Succeeded;
        }
    }
}
