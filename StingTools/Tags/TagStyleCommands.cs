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

    /// <summary>
    /// Applies a specific tag style (size, weight, color) to all element types.
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

            // Step 1: Pick size
            var sizeDlg = new TaskDialog("Tag Style — Size");
            sizeDlg.MainInstruction = "Select tag text size (mm):";
            sizeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "2.0 mm", "Compact — minimal annotation");
            sizeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "2.5 mm", "Standard — everyday drawings");
            sizeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "3.0 mm", "Large — presentation sheets");
            sizeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "3.5 mm", "Extra-large — titles and emphasis");
            sizeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            var sizeChoice = sizeDlg.Show();

            string size;
            switch (sizeChoice)
            {
                case TaskDialogResult.CommandLink1: size = "2"; break;
                case TaskDialogResult.CommandLink2: size = "2.5"; break;
                case TaskDialogResult.CommandLink3: size = "3"; break;
                case TaskDialogResult.CommandLink4: size = "3.5"; break;
                default: return Result.Cancelled;
            }

            // Step 2: Pick style
            var styleDlg = new TaskDialog("Tag Style — Weight");
            styleDlg.MainInstruction = "Select tag text style:";
            styleDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Normal (NOM)", "Standard weight text");
            styleDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Bold (BOLD)", "Heavy emphasis text");
            styleDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Italic (ITALIC)", "Slanted emphasis text");
            styleDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            var styleChoice = styleDlg.Show();

            string style;
            switch (styleChoice)
            {
                case TaskDialogResult.CommandLink1: style = "NOM"; break;
                case TaskDialogResult.CommandLink2: style = "BOLD"; break;
                case TaskDialogResult.CommandLink3: style = "ITALIC"; break;
                default: return Result.Cancelled;
            }

            // Step 3: Pick color
            var colorDlg = new TaskDialog("Tag Style — Color");
            colorDlg.MainInstruction = "Select tag text color:";
            colorDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Black", "Standard print-ready");
            colorDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Blue", "MEP / mechanical emphasis");
            colorDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Green", "Plumbing / environmental");
            colorDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Red", "Structural / fire / highlight");
            colorDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            var colorChoice = colorDlg.Show();

            string color;
            switch (colorChoice)
            {
                case TaskDialogResult.CommandLink1: color = "BLACK"; break;
                case TaskDialogResult.CommandLink2: color = "BLUE"; break;
                case TaskDialogResult.CommandLink3: color = "GREEN"; break;
                case TaskDialogResult.CommandLink4: color = "RED"; break;
                default: return Result.Cancelled;
            }

            var preset = new StylePreset { Name = $"{size}{style}_{color}", Size = size, Style = style, Color = color };

            using (Transaction tx = new Transaction(doc, $"STING Tag Style: {preset.TypeName}"))
            {
                tx.Start();
                int updated = TagStyleEngine.ApplyTagStyle(doc, preset);
                tx.Commit();

                TaskDialog.Show("Tag Style Applied",
                    $"Style: {preset.TypeName}\n" +
                    $"Parameter: {preset.ParamName}\n" +
                    $"Element types updated: {updated}");
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
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData).ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var dlg = new TaskDialog("Paragraph Depth — Extended");
            dlg.MainInstruction = "Select paragraph visibility depth (1-10 tiers):";
            dlg.MainContent =
                "Each tier adds more detail to tag labels.\n" +
                "Tiers 1-3 = original compact/standard/comprehensive.\n" +
                "Tiers 4-10 = extended detail for specifications and BOQ.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tier 1-3: Compact → Standard → Comprehensive",
                "Original 3-tier system (quick, engineering, detailed)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Tier 4-6: Extended detail",
                "MEP specs, material properties, classification codes");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Tier 7-10: Full specification",
                "Complete BOQ data, maintenance info, lifecycle data");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Custom tier (pick 1-10)...",
                "Set exact tier cutoff");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = dlg.Show();
            int maxTier;
            bool warn;

            switch (choice)
            {
                case TaskDialogResult.CommandLink1:
                    maxTier = 3; warn = true; break;
                case TaskDialogResult.CommandLink2:
                    maxTier = 6; warn = true; break;
                case TaskDialogResult.CommandLink3:
                    maxTier = 10; warn = true; break;
                case TaskDialogResult.CommandLink4:
                    var tierDlg = new TaskDialog("Custom Tier");
                    tierDlg.MainInstruction = "Select maximum visible tier:";
                    tierDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tier 1 (minimal)", "Tag code only");
                    tierDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Tier 2 (compact)", "Code + type name");
                    tierDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Tier 5 (moderate)", "Through extended properties");
                    tierDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Tier 8 (detailed)", "Through maintenance data");
                    tierDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                    switch (tierDlg.Show())
                    {
                        case TaskDialogResult.CommandLink1: maxTier = 1; break;
                        case TaskDialogResult.CommandLink2: maxTier = 2; break;
                        case TaskDialogResult.CommandLink3: maxTier = 5; break;
                        case TaskDialogResult.CommandLink4: maxTier = 8; break;
                        default: return Result.Cancelled;
                    }
                    warn = maxTier >= 2;
                    break;
                default: return Result.Cancelled;
            }

            using (Transaction tx = new Transaction(doc, $"STING Paragraph Depth: Tier {maxTier}"))
            {
                tx.Start();
                int updated = TagStyleEngine.SetParagraphDepth(doc, maxTier, warn);
                tx.Commit();

                TaskDialog.Show("Paragraph Depth",
                    $"Depth: Tier {maxTier} of 10\n" +
                    $"Warnings: {(warn ? "ON" : "OFF")}\n" +
                    $"Element types updated: {updated}");
            }

            return Result.Succeeded;
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

            // Step 1: Pick the variable
            var varDlg = new TaskDialog("Color By Variable");
            varDlg.MainInstruction = "Which variable should drive the colors?";
            varDlg.MainContent =
                "Each variable value gets a unique color for elements, tag text, " +
                "tag bounding boxes, and leader lines — all controlled separately.\n\n" +
                "Colors are semantically connected: each value REPRESENTS something " +
                "(e.g. HVAC=Blue, Fire=Red, NEW=Green, DEMOLISHED=Red).";
            varDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "System Type (HVAC/DCW/SAN/FP/LV...)",
                "Color by MEP system — 16 system types with distinct colors");
            varDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Status (NEW/EXISTING/DEMOLISHED/TEMP)",
                "Color by lifecycle status — 4 states");
            varDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Zone / Level / Location...",
                "Spatial variables — zones, levels, buildings");
            varDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Function Code (SUP/HTG/PWR/LTG...)",
                "Color by CIBSE function — 12 function types");
            varDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var varChoice = varDlg.Show();
            string schemeName;
            switch (varChoice)
            {
                case TaskDialogResult.CommandLink1: schemeName = "System"; break;
                case TaskDialogResult.CommandLink2: schemeName = "Status"; break;
                case TaskDialogResult.CommandLink3:
                    schemeName = PickSpatialVariable();
                    if (schemeName == null) return Result.Cancelled;
                    break;
                case TaskDialogResult.CommandLink4: schemeName = "Function"; break;
                default: return Result.Cancelled;
            }

            if (!TagStyleEngine.VariableSchemes.TryGetValue(schemeName, out VariableColorScheme scheme))
            {
                TaskDialog.Show("Error", $"Scheme '{schemeName}' not found.");
                return Result.Failed;
            }

            // Step 2: Pick what to apply
            var applyDlg = new TaskDialog("Apply Options");
            applyDlg.MainInstruction = $"Coloring by: {scheme.Name}";
            applyDlg.MainContent = scheme.Description;
            applyDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Elements + Tag Styles + Box Colors (Full)",
                "Color elements, switch tag text styles, AND set box fill colors");
            applyDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Elements Only",
                "Color elements in view — no tag style or box changes");
            applyDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Tag Styles + Box Colors Only",
                "Switch tag text styles and box colors — no element coloring");
            applyDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var applyChoice = applyDlg.Show();
            bool doElements, doStyles, doBoxes;
            switch (applyChoice)
            {
                case TaskDialogResult.CommandLink1: doElements = true; doStyles = true; doBoxes = true; break;
                case TaskDialogResult.CommandLink2: doElements = true; doStyles = false; doBoxes = false; break;
                case TaskDialogResult.CommandLink3: doElements = false; doStyles = true; doBoxes = true; break;
                default: return Result.Cancelled;
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

        private static string PickSpatialVariable()
        {
            var dlg = new TaskDialog("Spatial Variable");
            dlg.MainInstruction = "Select spatial variable:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Zone (Z01/Z02/Z03/Z04)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Level (GF/L01/L02/B1/RF)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Location (BLD1/BLD2/BLD3/EXT)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            return dlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => "Zone",
                TaskDialogResult.CommandLink2 => "Level",
                TaskDialogResult.CommandLink3 => "Location",
                _ => null,
            };
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
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            var selection = uidoc.Selection.GetElementIds();

            var dlg = new TaskDialog("Tag Box Color");
            dlg.MainInstruction = "Select box fill color:";
            dlg.MainContent =
                "Colors match discipline meanings:\n" +
                "  BLUE=Mechanical  GREEN=Plumbing  RED=Structural\n" +
                "  ORANGE=Fire/Elec  PURPLE=LowVoltage  GREY=General";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto (by Discipline)", "Match box color to element's discipline");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Pick color (Blue/Green/Red/Orange)",
                "Choose a fixed color for all selected");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Clear box colors",
                "Remove box color overrides (transparent)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = dlg.Show();

            using (Transaction tx = new Transaction(doc, "STING Set Box Color"))
            {
                tx.Start();
                int updated = 0;

                // Get target elements
                var targets = selection.Count > 0
                    ? selection.Select(id => doc.GetElement(id)).Where(e => e != null).ToList()
                    : new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType().ToList();

                switch (choice)
                {
                    case TaskDialogResult.CommandLink1: // Auto by discipline
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
                        break;

                    case TaskDialogResult.CommandLink2: // Pick color
                        var colorPreset = PickBoxColor();
                        if (colorPreset == null) { tx.RollBack(); return Result.Cancelled; }
                        foreach (var el in targets)
                        {
                            TagStyleEngine.SetBoxColor(el, colorPreset);
                            updated++;
                        }
                        break;

                    case TaskDialogResult.CommandLink3: // Clear
                        var clear = new BoxColorPreset { R = 0, G = 0, B = 0, BoxVisible = false, BoxStyle = "NONE" };
                        foreach (var el in targets)
                        {
                            TagStyleEngine.SetBoxColor(el, clear);
                            updated++;
                        }
                        break;

                    default: tx.RollBack(); return Result.Cancelled;
                }

                tx.Commit();
                TaskDialog.Show("Box Color", $"Updated {updated} elements.");
            }

            return Result.Succeeded;
        }

        private static BoxColorPreset PickBoxColor()
        {
            var dlg = new TaskDialog("Pick Box Color");
            dlg.MainInstruction = "Select color:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Blue (Mechanical)", "RGB(200,220,255)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Green (Plumbing)", "RGB(200,255,220)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Red (Structural/Fire)", "RGB(255,200,200)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Orange (Electrical/FP)", "RGB(255,230,200)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            return dlg.Show() switch
            {
                TaskDialogResult.CommandLink1 => new BoxColorPreset { Name = "Blue", R = 200, G = 220, B = 255 },
                TaskDialogResult.CommandLink2 => new BoxColorPreset { Name = "Green", R = 200, G = 255, B = 220 },
                TaskDialogResult.CommandLink3 => new BoxColorPreset { Name = "Red", R = 255, G = 200, B = 200 },
                TaskDialogResult.CommandLink4 => new BoxColorPreset { Name = "Orange", R = 255, G = 230, B = 200 },
                _ => null,
            };
        }
    }
}
