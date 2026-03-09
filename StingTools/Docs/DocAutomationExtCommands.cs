using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  DOCUMENTATION AUTOMATION ENGINE
    //  Inspired by Naviate, Ideate, Glyph, and DiRoots — enhanced with
    //  multi-layer intelligence for maximum automation.
    //
    //  Commands:
    //    1. BatchCreateViewsCommand         — Create views from levels × disciplines × scope boxes
    //    2. BatchCreateSheetsCommand         — Create sheets with views placed from template layout
    //    3. CreateDependentViewsCommand       — Create dependent views from scope boxes
    //    4. ScopeBoxManagerCommand            — Assign scope boxes to views, audit coverage
    //    5. ViewTemplateAssignerCommand       — Intelligent template assignment with naming rules
    //    6. DocumentationPackageCommand       — One-click full documentation set
    //    7. BatchCreateSectionsCommand        — Create sections from grids/scope boxes
    //    8. BatchCreateElevationsCommand      — Create exterior/interior elevations
    //    9. DrawingRegisterCommand            — ISO 19650 drawing register with full revision tracking
    //   10. ProjectBrowserOrganizerCommand    — Auto-organize Project Browser by discipline/type/level
    //
    //  Helper: DocAutomationHelper — shared intelligence engine
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shared documentation automation intelligence engine.
    /// Contains naming rules, level detection, scope box management,
    /// view template matching, and sheet numbering logic.
    /// </summary>
    internal static class DocAutomationHelper
    {
        // ── Discipline definitions ──
        internal static readonly (string Code, string Name, ViewFamily[] ViewTypes)[] Disciplines =
        {
            ("A",  "Architectural",  new[] { ViewFamily.FloorPlan, ViewFamily.CeilingPlan, ViewFamily.Elevation, ViewFamily.Section }),
            ("S",  "Structural",     new[] { ViewFamily.StructuralPlan, ViewFamily.Section }),
            ("M",  "Mechanical",     new[] { ViewFamily.FloorPlan, ViewFamily.CeilingPlan, ViewFamily.Section }),
            ("E",  "Electrical",     new[] { ViewFamily.FloorPlan, ViewFamily.CeilingPlan }),
            ("P",  "Plumbing",       new[] { ViewFamily.FloorPlan }),
            ("FP", "Fire Protection", new[] { ViewFamily.FloorPlan }),
            ("C",  "Coordination",   new[] { ViewFamily.FloorPlan }),
        };

        // ── Sheet numbering ranges (ISO 19650-inspired) ──
        internal static readonly Dictionary<string, int> SheetStartNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["G"]  = 0,    // General
            ["A"]  = 100,  // Architectural
            ["S"]  = 200,  // Structural
            ["M"]  = 300,  // Mechanical
            ["E"]  = 400,  // Electrical
            ["P"]  = 500,  // Plumbing
            ["FP"] = 600,  // Fire Protection
            ["C"]  = 700,  // Coordination
            ["L"]  = 800,  // Landscape
        };

        // ── View naming pattern ──
        internal static string BuildViewName(string discipline, string viewType, string levelName, string scopeBoxName = null)
        {
            string baseName = $"STING - {discipline} {viewType} - {levelName}";
            if (!string.IsNullOrEmpty(scopeBoxName))
                baseName += $" - {SanitizeScopeBoxName(scopeBoxName)}";
            return baseName;
        }

        internal static string SanitizeScopeBoxName(string name)
        {
            // Replace characters not allowed in Revit view names
            char[] bad = { '{', '}', ':', '\\', '|', '[', ']', ';', '<', '>', '?', '\'', '~' };
            foreach (char c in bad)
                name = name.Replace(c, '_');
            return name;
        }

        // ── Level intelligence ──
        internal static string GetShortLevelName(Level level)
        {
            string name = level.Name;
            string upper = name.ToUpperInvariant();
            if (upper.Contains("GROUND") || upper == "GF") return "GF";
            if (upper.Contains("ROOF") || upper == "RF") return "RF";
            if (upper.Contains("BASEMENT") || upper.StartsWith("B"))
            {
                string digits = new string(name.Where(char.IsDigit).ToArray());
                return "B" + (string.IsNullOrEmpty(digits) ? "1" : digits);
            }
            // Extract numeric level
            string num = new string(name.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(num))
                return "L" + num.PadLeft(2, '0');
            return name.Length > 10 ? name.Substring(0, 10) : name;
        }

        // ── Find ViewFamilyType by family ──
        internal static ViewFamilyType FindViewFamilyType(Document doc, ViewFamily family)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == family);
        }

        // ── Find view template by name (partial match) ──
        internal static View FindViewTemplate(Document doc, string nameContains)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate &&
                    v.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Template matching intelligence (7-layer) ──
        internal static View FindBestTemplate(Document doc, string discipline, ViewFamily viewFamily, string levelName)
        {
            // Layer 1: Exact discipline + view type match (e.g. "STING - Mechanical Plan")
            string viewTypeName = ViewFamilyToTypeName(viewFamily);
            View template = FindViewTemplate(doc, $"STING - {discipline} {viewTypeName}");
            if (template != null) return template;

            // Layer 2: Discipline code match (e.g. "STING - M ")
            template = FindViewTemplate(doc, $"STING - {discipline.Substring(0, 1)}");
            if (template != null) return template;

            // Layer 3: View type match without discipline (e.g. "STING - Working Plan")
            template = FindViewTemplate(doc, $"STING - Working {viewTypeName}");
            if (template != null) return template;

            // Layer 4: Level-specific (e.g. basement → structural, plant room → mechanical)
            string upperLevel = (levelName ?? "").ToUpperInvariant();
            if (upperLevel.Contains("BASEMENT") || upperLevel.Contains("FOUNDATION"))
            {
                template = FindViewTemplate(doc, "STING - Structural");
                if (template != null) return template;
            }
            if (upperLevel.Contains("PLANT") || upperLevel.Contains("MECHANICAL"))
            {
                template = FindViewTemplate(doc, "STING - Mechanical");
                if (template != null) return template;
            }

            // Layer 5: Generic view type (e.g. any "Plan" or "Section" template)
            template = FindViewTemplate(doc, $"STING - {viewTypeName}");
            if (template != null) return template;

            // Layer 6: Coordination fallback
            template = FindViewTemplate(doc, "STING - Coordination");
            if (template != null) return template;

            // Layer 7: Any STING template
            template = FindViewTemplate(doc, "STING");
            return template; // may be null
        }

        internal static string ViewFamilyToTypeName(ViewFamily family)
        {
            switch (family)
            {
                case ViewFamily.FloorPlan: return "Plan";
                case ViewFamily.CeilingPlan: return "RCP";
                case ViewFamily.StructuralPlan: return "Structural Plan";
                case ViewFamily.Section: return "Section";
                case ViewFamily.Elevation: return "Elevation";
                case ViewFamily.ThreeDimensional: return "3D";
                case ViewFamily.AreaPlan: return "Area Plan";
                case ViewFamily.Drafting: return "Drafting";
                default: return "Plan";
            }
        }

        // ── Collect scope boxes ──
        internal static List<Element> GetScopeBoxes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .OrderBy(e => e.Name)
                .ToList();
        }

        // ── Assign scope box to view ──
        internal static bool AssignScopeBox(View view, ElementId scopeBoxId)
        {
            try
            {
                Parameter p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(scopeBoxId);
                    return true;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AssignScopeBox: {ex.Message}");
            }
            return false;
        }

        // ── Title block helpers ──
        internal static FamilySymbol GetFirstTitleBlock(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .FirstOrDefault();
        }

        internal static (double width, double height) GetTitleBlockSize(Document doc, ViewSheet sheet)
        {
            var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();
            if (titleBlocks.Count > 0)
            {
                BoundingBoxXYZ bb = titleBlocks[0].get_BoundingBox(null);
                if (bb != null)
                    return (bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
            }
            return (2.76, 1.95); // A1 default in feet
        }

        // ── Infer discipline from view name / properties ──
        internal static string InferDisciplineFromView(View v)
        {
            string name = (v.Name ?? "").ToUpperInvariant();
            if (name.Contains("MECHANICAL") || name.Contains("HVAC") || name.Contains("- M ")) return "Mechanical";
            if (name.Contains("ELECTRICAL") || name.Contains("LIGHTING") || name.Contains("- E ")) return "Electrical";
            if (name.Contains("PLUMBING") || name.Contains("SANITARY") || name.Contains("- P ")) return "Plumbing";
            if (name.Contains("STRUCTURAL") || name.Contains("- S ")) return "Structural";
            if (name.Contains("FIRE") || name.Contains("- FP ")) return "Fire Protection";
            if (name.Contains("COORDINATION") || name.Contains("- C ")) return "Coordination";
            if (name.Contains("ARCHITECT") || name.Contains("- A ")) return "Architectural";

            // Check view discipline property
            try
            {
                Parameter discParam = v.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
                if (discParam != null)
                {
                    int disc = discParam.AsInteger();
                    switch (disc)
                    {
                        case 1: return "Architectural";
                        case 2: return "Structural";
                        case 4: return "Mechanical";
                        case 8: return "Electrical";
                        case 4095: return "Coordination";
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferDisciplineFromView: {ex.Message}");
            }

            return "Architectural";
        }

        // ── Infer discipline prefix code from view name ──
        internal static string InferDisciplinePrefixCode(View v)
        {
            string name = (v.Name ?? "").ToUpperInvariant();
            if (name.Contains("MECHANICAL") || name.Contains("HVAC") || name.Contains("- M ")) return "M";
            if (name.Contains("ELECTRICAL") || name.Contains("LIGHTING") || name.Contains("- E ")) return "E";
            if (name.Contains("PLUMBING") || name.Contains("SANITARY") || name.Contains("- P ")) return "P";
            if (name.Contains("STRUCTURAL") || name.Contains("- S ")) return "S";
            if (name.Contains("FIRE") || name.Contains("- FP ")) return "FP";
            if (name.Contains("COORDINATION") || name.Contains("- C ")) return "C";
            if (name.Contains("ARCHITECT") || name.Contains("- A ")) return "A";
            return "G";
        }

        // ── Convert ViewType to ViewFamily ──
        internal static ViewFamily ViewFamilyFromViewType(ViewType vt)
        {
            switch (vt)
            {
                case ViewType.FloorPlan: return ViewFamily.FloorPlan;
                case ViewType.CeilingPlan: return ViewFamily.CeilingPlan;
                case ViewType.Section: return ViewFamily.Section;
                case ViewType.Elevation: return ViewFamily.Elevation;
                case ViewType.ThreeD: return ViewFamily.ThreeDimensional;
                case ViewType.AreaPlan: return ViewFamily.AreaPlan;
                case ViewType.DraftingView: return ViewFamily.Drafting;
                case ViewType.EngineeringPlan: return ViewFamily.StructuralPlan;
                default: return ViewFamily.FloorPlan;
            }
        }

        // ── Get level name for a view ──
        internal static string GetViewLevelName(Document doc, View v)
        {
            try
            {
                Parameter lvlParam = v.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
                if (lvlParam != null)
                {
                    ElementId lvlId = lvlParam.AsElementId();
                    if (lvlId != null && lvlId != ElementId.InvalidElementId)
                    {
                        Level lvl = doc.GetElement(lvlId) as Level;
                        if (lvl != null) return GetShortLevelName(lvl);
                    }
                    string name = lvlParam.AsString();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetViewLevelName: {ex.Message}");
            }
            return "";
        }

        // ── Check if view name exists (uncached — for single-use checks) ──
        internal static bool ViewNameExists(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Any(v => v.Name == name);
        }

        // ── Build cached view name index for batch operations ──
        internal static HashSet<string> BuildViewNameIndex(Document doc)
        {
            return new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name),
                StringComparer.Ordinal);
        }

        // ── Build unique view name (cached version for batch use) ──
        internal static string GetUniqueViewName(Document doc, string baseName, HashSet<string> nameCache = null)
        {
            if (nameCache != null)
            {
                if (!nameCache.Contains(baseName))
                {
                    nameCache.Add(baseName);
                    return baseName;
                }
                int suffix = 2;
                while (nameCache.Contains($"{baseName} ({suffix})"))
                    suffix++;
                string unique = $"{baseName} ({suffix})";
                nameCache.Add(unique);
                return unique;
            }
            // Fallback: uncached single-use
            if (!ViewNameExists(doc, baseName)) return baseName;
            int s = 2;
            while (ViewNameExists(doc, $"{baseName} ({s})"))
                s++;
            return $"{baseName} ({s})";
        }

        // ── Create view by family type (handles Plan, Section, Elevation correctly) ──
        internal static View CreateViewByFamily(Document doc, ViewFamily family,
            ViewFamilyType vft, Level level, string viewName, HashSet<string> nameCache)
        {
            viewName = GetUniqueViewName(doc, viewName, nameCache);

            switch (family)
            {
                case ViewFamily.FloorPlan:
                case ViewFamily.CeilingPlan:
                case ViewFamily.StructuralPlan:
                case ViewFamily.AreaPlan:
                    ViewPlan plan = ViewPlan.Create(doc, vft.Id, level.Id);
                    if (plan != null) plan.Name = viewName;
                    return plan;

                case ViewFamily.Section:
                    // Create a default section at the level height
                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                    Transform t = Transform.Identity;
                    t.Origin = new XYZ(0, 0, level.Elevation);
                    t.BasisX = XYZ.BasisX;
                    t.BasisY = XYZ.BasisZ;
                    t.BasisZ = -XYZ.BasisY; // looking south
                    sectionBox.Transform = t;
                    sectionBox.Min = new XYZ(-50, -10, 0);
                    sectionBox.Max = new XYZ(50, 30, 50);
                    ViewSection section = ViewSection.CreateSection(doc, vft.Id, sectionBox);
                    if (section != null) section.Name = viewName;
                    return section;

                case ViewFamily.Elevation:
                    // Elevations require a floor plan host — skip in batch loop
                    // (use BatchCreateElevationsCommand instead)
                    return null;

                default:
                    return null;
            }
        }

        // ── Collect existing sheet numbers ──
        internal static HashSet<string> GetExistingSheetNumbers(Document doc)
        {
            return new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Select(s => s.SheetNumber),
                StringComparer.OrdinalIgnoreCase);
        }

        // ── Generate next sheet number ──
        internal static string NextSheetNumber(string prefix, int seq, HashSet<string> existing)
        {
            string num;
            do
            {
                num = $"{prefix}-{seq:D3}";
                seq++;
            } while (existing.Contains(num));
            existing.Add(num);
            return num;
        }

        // ── Get placed view IDs across all sheets ──
        internal static HashSet<ElementId> GetPlacedViewIds(Document doc)
        {
            return new HashSet<ElementId>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .SelectMany(s => s.GetAllPlacedViews()));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  1. BATCH CREATE VIEWS — Naviate/Ideate-inspired with 7-layer intelligence
    //     Creates views from: Levels × Disciplines × ViewTypes × ScopeBoxes
    //     with auto-template assignment and dependent view support
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCreateViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var sw = Stopwatch.StartNew();

            // Collect levels
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0)
            {
                TaskDialog.Show("Batch Create Views", "No levels found in the project.");
                return Result.Failed;
            }

            // Collect scope boxes
            var scopeBoxes = DocAutomationHelper.GetScopeBoxes(doc);

            // Step 1: Choose disciplines
            TaskDialog discDlg = new TaskDialog("Batch Create Views — Disciplines");
            discDlg.MainInstruction = "Select disciplines to create views for";
            discDlg.MainContent =
                $"Project has {levels.Count} levels, {scopeBoxes.Count} scope boxes.\n\n" +
                "Views created = Levels × Disciplines × View Types";
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "All disciplines (A, S, M, E, P, FP, C)",
                $"Creates up to {levels.Count * 7} parent views");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MEP only (M, E, P, FP)",
                $"Creates up to {levels.Count * 4} parent views");
            discDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Architecture + Structure only (A, S)",
                $"Creates up to {levels.Count * 2} parent views");
            discDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var discResult = discDlg.Show();
            (string Code, string Name, ViewFamily[] ViewTypes)[] selectedDiscs;
            switch (discResult)
            {
                case TaskDialogResult.CommandLink1:
                    selectedDiscs = DocAutomationHelper.Disciplines;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedDiscs = DocAutomationHelper.Disciplines
                        .Where(d => d.Code == "M" || d.Code == "E" || d.Code == "P" || d.Code == "FP")
                        .ToArray();
                    break;
                case TaskDialogResult.CommandLink3:
                    selectedDiscs = DocAutomationHelper.Disciplines
                        .Where(d => d.Code == "A" || d.Code == "S")
                        .ToArray();
                    break;
                default:
                    return Result.Cancelled;
            }

            // Step 2: View creation options
            TaskDialog optDlg = new TaskDialog("Batch Create Views — Options");
            optDlg.MainInstruction = "View creation mode";
            int scopeCount = scopeBoxes.Count;
            string depInfo = scopeCount > 0
                ? $"\n{scopeCount} scope boxes detected — dependent views will be created per scope box."
                : "\nNo scope boxes found — only parent views will be created.";
            optDlg.MainContent =
                "Choose how views are created:" + depInfo;
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Floor plans + RCPs" + (scopeCount > 0 ? " + dependents" : ""),
                "Standard plan views per level" + (scopeCount > 0 ? $" with {scopeCount} dependent views each" : ""));
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Floor plans + RCPs + Sections + Elevations",
                "Full documentation set including building sections along grids");
            optDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Floor plans only (fast)",
                "Minimal: one plan per level per discipline");
            optDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int mode;
            switch (optDlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = 1; break;
                case TaskDialogResult.CommandLink2: mode = 2; break;
                case TaskDialogResult.CommandLink3: mode = 3; break;
                default: return Result.Cancelled;
            }

            // Step 3: Template assignment
            TaskDialog tplDlg = new TaskDialog("Batch Create Views — Templates");
            tplDlg.MainInstruction = "View template assignment";
            tplDlg.MainContent = "Click an option to proceed with view creation:";
            tplDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto-assign STING templates (recommended)",
                "7-layer intelligent matching: discipline + view type + level");
            tplDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "No templates (assign later)",
                "Create views without template assignment");
            tplDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            tplDlg.DefaultButton = TaskDialogResult.CommandLink1;

            bool autoTemplate;
            switch (tplDlg.Show())
            {
                case TaskDialogResult.CommandLink1: autoTemplate = true; break;
                case TaskDialogResult.CommandLink2: autoTemplate = false; break;
                default: return Result.Cancelled;
            }

            // Execute
            int viewsCreated = 0;
            int dependentsCreated = 0;
            int templatesAssigned = 0;
            int errors = 0;
            var createdViews = new List<(string name, string discipline, string level)>();
            var nameCache = DocAutomationHelper.BuildViewNameIndex(doc);

            using (Transaction tx = new Transaction(doc, "STING Batch Create Views"))
            {
                tx.Start();

                foreach (var disc in selectedDiscs)
                {
                    foreach (Level level in levels)
                    {
                        string shortLevel = DocAutomationHelper.GetShortLevelName(level);

                        // Determine which view families to create
                        var families = new List<ViewFamily>();
                        switch (mode)
                        {
                            case 1: // Plans + RCPs
                                families.AddRange(disc.ViewTypes.Where(f =>
                                    f == ViewFamily.FloorPlan || f == ViewFamily.CeilingPlan ||
                                    f == ViewFamily.StructuralPlan));
                                break;
                            case 2: // Full set — filter out Elevation (requires ElevationMarker API)
                                families.AddRange(disc.ViewTypes.Where(f => f != ViewFamily.Elevation));
                                break;
                            case 3: // Plans only
                                families.AddRange(disc.ViewTypes.Where(f =>
                                    f == ViewFamily.FloorPlan || f == ViewFamily.StructuralPlan));
                                break;
                        }

                        foreach (ViewFamily family in families)
                        {
                            try
                            {
                                ViewFamilyType vft = DocAutomationHelper.FindViewFamilyType(doc, family);
                                if (vft == null) continue;

                                string typeName = DocAutomationHelper.ViewFamilyToTypeName(family);
                                string viewName = DocAutomationHelper.BuildViewName(disc.Name, typeName, shortLevel);

                                // Use CreateViewByFamily — handles Plan/Section/Elevation correctly
                                View newView = DocAutomationHelper.CreateViewByFamily(
                                    doc, family, vft, level, viewName, nameCache);
                                if (newView == null) continue;

                                viewsCreated++;
                                createdViews.Add((newView.Name, disc.Code, shortLevel));

                                // Auto-assign template (7-layer intelligence)
                                if (autoTemplate)
                                {
                                    View template = DocAutomationHelper.FindBestTemplate(
                                        doc, disc.Name, family, level.Name);
                                    if (template != null)
                                    {
                                        newView.ViewTemplateId = template.Id;
                                        templatesAssigned++;
                                    }
                                }

                                // Create dependent views from scope boxes (plan views only)
                                bool canDuplicate = family == ViewFamily.FloorPlan ||
                                    family == ViewFamily.CeilingPlan ||
                                    family == ViewFamily.StructuralPlan;
                                if (scopeBoxes.Count > 0 && mode != 3 && canDuplicate)
                                {
                                    foreach (Element scopeBox in scopeBoxes)
                                    {
                                        try
                                        {
                                            string depName = DocAutomationHelper.BuildViewName(
                                                disc.Name, typeName, shortLevel, scopeBox.Name);
                                            depName = DocAutomationHelper.GetUniqueViewName(doc, depName, nameCache);

                                            ElementId depId = newView.Duplicate(ViewDuplicateOption.AsDependent);
                                            View depView = doc.GetElement(depId) as View;
                                            if (depView != null)
                                            {
                                                depView.Name = depName;
                                                DocAutomationHelper.AssignScopeBox(depView, scopeBox.Id);
                                                dependentsCreated++;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            StingLog.Warn($"Dependent view for scope box '{scopeBox.Name}': {ex.Message}");
                                            errors++;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Create view {disc.Code}/{family}/{level.Name}: {ex.Message}");
                                errors++;
                            }
                        }
                    }
                }

                tx.Commit();
            }

            sw.Stop();
            var report = new StringBuilder();
            report.AppendLine("Batch Create Views Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Disciplines:  {selectedDiscs.Length}");
            report.AppendLine($"  Levels:       {levels.Count}");
            report.AppendLine($"  Scope boxes:  {scopeBoxes.Count}");
            report.AppendLine($"  Mode:         {(mode == 1 ? "Plans+RCPs" : mode == 2 ? "Full set" : "Plans only")}");
            report.AppendLine();
            report.AppendLine("── RESULTS ──");
            report.AppendLine($"  Parent views:    {viewsCreated}");
            report.AppendLine($"  Dependent views: {dependentsCreated}");
            report.AppendLine($"  Templates set:   {templatesAssigned}");
            if (errors > 0)
                report.AppendLine($"  Errors:          {errors}");
            report.AppendLine($"  Duration:        {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();

            // Breakdown by discipline
            report.AppendLine("── BY DISCIPLINE ──");
            foreach (var disc in selectedDiscs)
            {
                int count = createdViews.Count(v => v.discipline == disc.Code);
                if (count > 0)
                    report.AppendLine($"  [{disc.Code}] {disc.Name}: {count} views");
            }

            TaskDialog td = new TaskDialog("Batch Create Views");
            td.MainInstruction = $"Created {viewsCreated} views + {dependentsCreated} dependents";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"BatchCreateViews: views={viewsCreated}, dependents={dependentsCreated}, " +
                $"templates={templatesAssigned}, errors={errors}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. BATCH CREATE SHEETS — Template-based with auto-placement
    //     Creates sheets from a template layout, places views, auto-numbers
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCreateSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Get title blocks
            var titleBlocks = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .ToList();

            if (titleBlocks.Count == 0)
            {
                TaskDialog.Show("Batch Create Sheets", "No title block families loaded.");
                return Result.Failed;
            }

            // Get unplaced views
            var placedIds = DocAutomationHelper.GetPlacedViewIds(doc);
            var unplacedViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted &&
                    !(v is ViewSheet) && !placedIds.Contains(v.Id))
                .OrderBy(v => v.Name)
                .ToList();

            // Step 1: Creation mode
            TaskDialog modeDlg = new TaskDialog("Batch Create Sheets");
            modeDlg.MainInstruction = $"Create sheets ({unplacedViews.Count} unplaced views available)";
            modeDlg.MainContent = "Click an option below to create sheets:";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "One view per sheet (recommended)",
                $"Create {unplacedViews.Count} sheets, each with one view centered");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Group by level (multiple views per sheet)",
                "Views from the same level share a sheet, overflow creates new sheets");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Group by discipline",
                "Views of the same discipline share a sheet");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            modeDlg.DefaultButton = TaskDialogResult.CommandLink1;

            int mode;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = 1; break;
                case TaskDialogResult.CommandLink2: mode = 2; break;
                case TaskDialogResult.CommandLink3: mode = 3; break;
                default: return Result.Cancelled;
            }

            if (unplacedViews.Count == 0)
            {
                TaskDialog.Show("Batch Create Sheets", "All views are already placed on sheets.");
                return Result.Succeeded;
            }

            FamilySymbol titleBlock = titleBlocks[0];
            if (!titleBlock.IsActive)
            {
                using (Transaction activateTx = new Transaction(doc, "Activate Title Block"))
                {
                    activateTx.Start();
                    titleBlock.Activate();
                    activateTx.Commit();
                }
            }

            var existingNums = DocAutomationHelper.GetExistingSheetNumbers(doc);
            int sheetsCreated = 0;
            int viewsPlaced = 0;
            int errors = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Create Sheets"))
            {
                tx.Start();

                if (mode == 1)
                {
                    // One view per sheet
                    foreach (View v in unplacedViews)
                    {
                        try
                        {
                            string prefix = InferDisciplinePrefix(v);
                            int startNum = DocAutomationHelper.SheetStartNumbers.TryGetValue(prefix, out int sn) ? sn + 1 : 1;
                            string sheetNum = DocAutomationHelper.NextSheetNumber(prefix, startNum, existingNums);

                            ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                            sheet.SheetNumber = sheetNum;
                            sheet.Name = v.Name.Replace("STING - ", "");
                            sheetsCreated++;

                            if (Viewport.CanAddViewToSheet(doc, sheet.Id, v.Id))
                            {
                                var (w, h) = DocAutomationHelper.GetTitleBlockSize(doc, sheet);
                                XYZ center = new XYZ(w / 2, h / 2, 0);
                                Viewport.Create(doc, sheet.Id, v.Id, center);
                                viewsPlaced++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Create sheet for '{v.Name}': {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Group views
                    IEnumerable<IGrouping<string, View>> groups;
                    if (mode == 2)
                    {
                        groups = unplacedViews.GroupBy(v => ExtractLevelFromViewName(v));
                    }
                    else
                    {
                        groups = unplacedViews.GroupBy(v => InferDisciplinePrefix(v));
                    }

                    foreach (var group in groups.OrderBy(g => g.Key))
                    {
                        var viewsInGroup = group.ToList();
                        int maxPerSheet = 4; // 2x2 grid
                        int sheetsNeeded = (int)Math.Ceiling(viewsInGroup.Count / (double)maxPerSheet);

                        for (int s = 0; s < sheetsNeeded; s++)
                        {
                            try
                            {
                                string prefix = mode == 3 ? group.Key : InferDisciplinePrefix(viewsInGroup[0]);
                                int startNum = DocAutomationHelper.SheetStartNumbers.TryGetValue(prefix, out int sn) ? sn + 1 : 1;
                                string sheetNum = DocAutomationHelper.NextSheetNumber(prefix, startNum, existingNums);

                                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                                sheet.SheetNumber = sheetNum;
                                string suffix = sheetsNeeded > 1 ? $" ({s + 1}/{sheetsNeeded})" : "";
                                sheet.Name = $"{group.Key} Views{suffix}";
                                sheetsCreated++;

                                var (w, h) = DocAutomationHelper.GetTitleBlockSize(doc, sheet);
                                double margin = 0.15;
                                double usableW = w - 2 * margin;
                                double usableH = h - 2 * margin;

                                var batch = viewsInGroup.Skip(s * maxPerSheet).Take(maxPerSheet).ToList();
                                int cols = batch.Count <= 1 ? 1 : 2;
                                int rows = (int)Math.Ceiling(batch.Count / (double)cols);
                                double cellW = usableW / cols;
                                double cellH = usableH / rows;

                                int idx = 0;
                                for (int r = 0; r < rows && idx < batch.Count; r++)
                                {
                                    for (int c = 0; c < cols && idx < batch.Count; c++)
                                    {
                                        View v = batch[idx++];
                                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, v.Id)) continue;

                                        double cx = margin + cellW * (c + 0.5);
                                        double cy = h - margin - cellH * (r + 0.5);
                                        try
                                        {
                                            Viewport.Create(doc, sheet.Id, v.Id, new XYZ(cx, cy, 0));
                                            viewsPlaced++;
                                        }
                                        catch (Exception ex)
                                        {
                                            StingLog.Warn($"Place viewport '{v.Name}': {ex.Message}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                StingLog.Warn($"Create sheet for group '{group.Key}': {ex.Message}");
                            }
                        }
                    }
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine("Batch Create Sheets Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Sheets created: {sheetsCreated}");
            report.AppendLine($"  Views placed:   {viewsPlaced}");
            if (errors > 0)
                report.AppendLine($"  Errors:         {errors}");

            TaskDialog td = new TaskDialog("Batch Create Sheets");
            td.MainInstruction = $"Created {sheetsCreated} sheets with {viewsPlaced} viewports";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"BatchCreateSheets: sheets={sheetsCreated}, viewports={viewsPlaced}, errors={errors}");
            return Result.Succeeded;
        }

        private static string InferDisciplinePrefix(View v)
        {
            return DocAutomationHelper.InferDisciplinePrefixCode(v);
        }

        private static string ExtractLevelFromViewName(View v)
        {
            string name = v.Name ?? "";
            // Try to extract level code: L01, GF, B1, RF, etc.
            var match = System.Text.RegularExpressions.Regex.Match(name, @"\b(L\d{2}|GF|RF|B\d)\b");
            if (match.Success) return match.Value;

            // Try Level parameter
            try
            {
                Parameter lvlParam = v.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
                if (lvlParam != null)
                {
                    string lvlName = lvlParam.AsString();
                    if (!string.IsNullOrEmpty(lvlName)) return lvlName;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExtractLevelFromViewName: {ex.Message}");
            }

            return "Misc";
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. CREATE DEPENDENT VIEWS — One-click scope box dependents
    //     (Naviate Quick Dependents + Ideate Apply Dependents)
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateDependentViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var scopeBoxes = DocAutomationHelper.GetScopeBoxes(doc);
            if (scopeBoxes.Count == 0)
            {
                TaskDialog.Show("Create Dependent Views",
                    "No scope boxes found in the project.\n" +
                    "Create scope boxes first, then run this command.");
                return Result.Failed;
            }

            // Get parent views (non-dependent plan views)
            var parentViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted &&
                    !(v is ViewSheet) &&
                    (v.ViewType == ViewType.FloorPlan ||
                     v.ViewType == ViewType.CeilingPlan ||
                     v.ViewType == ViewType.EngineeringPlan) &&
                    v.GetPrimaryViewId() == ElementId.InvalidElementId) // not already dependent
                .OrderBy(v => v.Name)
                .ToList();

            if (parentViews.Count == 0)
            {
                TaskDialog.Show("Create Dependent Views", "No parent plan views found.");
                return Result.Failed;
            }

            // Step 1: Scope
            TaskDialog scopeDlg = new TaskDialog("Create Dependent Views");
            scopeDlg.MainInstruction = $"Create dependents from {scopeBoxes.Count} scope boxes";
            scopeDlg.MainContent =
                $"Parent views: {parentViews.Count}\n" +
                $"Scope boxes: {scopeBoxes.Count}\n" +
                $"Total dependents: {parentViews.Count * scopeBoxes.Count}\n\n" +
                "Each parent view gets one dependent per scope box.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"All parent views ({parentViews.Count})",
                $"Create {parentViews.Count * scopeBoxes.Count} dependent views");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "STING views only",
                "Only views with 'STING' in the name");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Active view only",
                "Create dependents for the current view");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<View> selectedParents;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    selectedParents = parentViews;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedParents = parentViews
                        .Where(v => v.Name.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    break;
                case TaskDialogResult.CommandLink3:
                    View active = ctx.ActiveView;
                    if (active is ViewSheet || active.IsTemplate)
                    {
                        TaskDialog.Show("Create Dependent Views", "Active view must be a plan view.");
                        return Result.Failed;
                    }
                    selectedParents = new List<View> { active };
                    break;
                default:
                    return Result.Cancelled;
            }

            if (selectedParents.Count == 0)
            {
                TaskDialog.Show("Create Dependent Views", "No matching parent views.");
                return Result.Succeeded;
            }

            int created = 0;
            int errors = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Dependent Views"))
            {
                tx.Start();

                foreach (View parent in selectedParents)
                {
                    foreach (Element scopeBox in scopeBoxes)
                    {
                        try
                        {
                            string depName = $"{parent.Name} - {DocAutomationHelper.SanitizeScopeBoxName(scopeBox.Name)}";
                            depName = DocAutomationHelper.GetUniqueViewName(doc, depName);

                            ElementId depId = parent.Duplicate(ViewDuplicateOption.AsDependent);
                            View depView = doc.GetElement(depId) as View;
                            if (depView != null)
                            {
                                depView.Name = depName;
                                DocAutomationHelper.AssignScopeBox(depView, scopeBox.Id);
                                created++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Dependent view '{parent.Name}'/'{scopeBox.Name}': {ex.Message}");
                        }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Create Dependent Views",
                $"Created {created} dependent views from {selectedParents.Count} parents × {scopeBoxes.Count} scope boxes." +
                (errors > 0 ? $"\n{errors} errors (see log)." : ""));

            StingLog.Info($"CreateDependentViews: parents={selectedParents.Count}, scopeBoxes={scopeBoxes.Count}, " +
                $"created={created}, errors={errors}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  4. SCOPE BOX MANAGER — Assign, audit, and manage scope boxes
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScopeBoxManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var scopeBoxes = DocAutomationHelper.GetScopeBoxes(doc);

            // Build scope box usage map
            var usage = new Dictionary<ElementId, List<string>>();
            foreach (var sb in scopeBoxes)
                usage[sb.Id] = new List<string>();

            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && !(v is ViewSheet))
                .ToList();

            int viewsWithScopeBox = 0;
            int viewsWithoutScopeBox = 0;

            foreach (View v in allViews)
            {
                try
                {
                    Parameter p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (p != null)
                    {
                        ElementId sbId = p.AsElementId();
                        if (sbId != null && sbId != ElementId.InvalidElementId)
                        {
                            viewsWithScopeBox++;
                            if (usage.ContainsKey(sbId))
                                usage[sbId].Add(v.Name);
                        }
                        else
                        {
                            viewsWithoutScopeBox++;
                        }
                    }
                    else
                    {
                        viewsWithoutScopeBox++;
                    }
                }
                catch
                {
                    viewsWithoutScopeBox++;
                }
            }

            // Action selection
            TaskDialog actionDlg = new TaskDialog("Scope Box Manager");
            actionDlg.MainInstruction = $"{scopeBoxes.Count} scope boxes, {allViews.Count} views";
            actionDlg.MainContent =
                $"Views with scope box:    {viewsWithScopeBox}\n" +
                $"Views without scope box: {viewsWithoutScopeBox}\n\n" +
                "Choose action:";
            actionDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Audit scope box usage",
                "Report which scope boxes are used by which views");
            actionDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Auto-assign scope boxes to unassigned views",
                $"Assign scope boxes to {viewsWithoutScopeBox} views by level/name matching");
            actionDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Clear all scope box assignments",
                "Remove scope box assignments from all views");
            actionDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var action = actionDlg.Show();

            if (action == TaskDialogResult.CommandLink1)
            {
                // Audit report
                var report = new StringBuilder();
                report.AppendLine("Scope Box Usage Audit");
                report.AppendLine(new string('═', 50));
                report.AppendLine($"  Total scope boxes: {scopeBoxes.Count}");
                report.AppendLine($"  Views assigned:    {viewsWithScopeBox}");
                report.AppendLine($"  Views unassigned:  {viewsWithoutScopeBox}");
                report.AppendLine();

                foreach (var sb in scopeBoxes)
                {
                    var views = usage[sb.Id];
                    report.AppendLine($"  [{sb.Name}] — {views.Count} views");
                    foreach (string vn in views.Take(5))
                        report.AppendLine($"    • {vn}");
                    if (views.Count > 5)
                        report.AppendLine($"    ... and {views.Count - 5} more");
                }

                // Unused scope boxes
                var unused = scopeBoxes.Where(sb => usage[sb.Id].Count == 0).ToList();
                if (unused.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("── UNUSED SCOPE BOXES ──");
                    foreach (var sb in unused)
                        report.AppendLine($"  {sb.Name} (0 views)");
                }

                TaskDialog td = new TaskDialog("Scope Box Audit");
                td.MainInstruction = $"{scopeBoxes.Count} scope boxes, {viewsWithScopeBox} assigned";
                td.MainContent = report.ToString();
                td.Show();
                return Result.Succeeded;
            }
            else if (action == TaskDialogResult.CommandLink2)
            {
                // Auto-assign: match scope box names to view names/levels
                int assigned = 0;
                using (Transaction tx = new Transaction(doc, "STING Auto-Assign Scope Boxes"))
                {
                    tx.Start();
                    foreach (View v in allViews)
                    {
                        try
                        {
                            Parameter p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (p == null || p.IsReadOnly) continue;
                            ElementId currentSb = p.AsElementId();
                            if (currentSb != null && currentSb != ElementId.InvalidElementId) continue;

                            // Find best matching scope box by name similarity
                            Element bestMatch = FindBestScopeBox(v, scopeBoxes);
                            if (bestMatch != null)
                            {
                                p.Set(bestMatch.Id);
                                assigned++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Assign scope box to '{v.Name}': {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Scope Box Manager",
                    $"Auto-assigned scope boxes to {assigned} views.");
                return Result.Succeeded;
            }
            else if (action == TaskDialogResult.CommandLink3)
            {
                int cleared = 0;
                using (Transaction tx = new Transaction(doc, "STING Clear Scope Box Assignments"))
                {
                    tx.Start();
                    foreach (View v in allViews)
                    {
                        try
                        {
                            Parameter p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (p != null && !p.IsReadOnly)
                            {
                                ElementId currentSb = p.AsElementId();
                                if (currentSb != null && currentSb != ElementId.InvalidElementId)
                                {
                                    p.Set(ElementId.InvalidElementId);
                                    cleared++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Clear scope box from view: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("Scope Box Manager", $"Cleared scope box from {cleared} views.");
                return Result.Succeeded;
            }

            return Result.Cancelled;
        }

        private static Element FindBestScopeBox(View view, List<Element> scopeBoxes)
        {
            if (scopeBoxes.Count == 0) return null;
            if (scopeBoxes.Count == 1) return scopeBoxes[0];

            string viewName = (view.Name ?? "").ToUpperInvariant();

            // Try to match by name overlap
            int bestScore = 0;
            Element bestMatch = null;

            foreach (var sb in scopeBoxes)
            {
                string sbName = (sb.Name ?? "").ToUpperInvariant();
                int score = 0;

                // Score based on shared words
                var sbWords = sbName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in sbWords)
                {
                    if (word.Length >= 2 && viewName.Contains(word))
                        score += word.Length;
                }

                // Bonus for zone/level matching
                if (sbName.Contains("ZONE") && viewName.Contains("ZONE"))
                    score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = sb;
                }
            }

            return bestMatch; // may still be null if no match
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  5. VIEW TEMPLATE ASSIGNER — 7-layer intelligent assignment
    //     with naming-based rule engine and override modes
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewTemplateAssignerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Collect templates and views
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .OrderBy(v => v.Name)
                .ToList();

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && !(v is ViewSheet))
                .OrderBy(v => v.Name)
                .ToList();

            int withTemplate = views.Count(v => v.ViewTemplateId != ElementId.InvalidElementId);
            int withoutTemplate = views.Count - withTemplate;
            int stingTemplates = templates.Count(t => t.Name.IndexOf("STING", StringComparison.OrdinalIgnoreCase) >= 0);

            TaskDialog dlg = new TaskDialog("View Template Assigner");
            dlg.MainInstruction = $"{views.Count} views, {templates.Count} templates ({stingTemplates} STING)";
            dlg.MainContent =
                $"Views with template:    {withTemplate}\n" +
                $"Views without template: {withoutTemplate}\n\n" +
                "Choose assignment mode:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto-assign (unassigned only)",
                $"Assign templates to {withoutTemplate} views using 7-layer intelligence");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Force re-assign all views",
                $"Override existing assignments on all {views.Count} views");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Remove all template assignments",
                "Clear template from all views (set to <None>)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int mode;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = 1; break;
                case TaskDialogResult.CommandLink2: mode = 2; break;
                case TaskDialogResult.CommandLink3: mode = 3; break;
                default: return Result.Cancelled;
            }

            int assigned = 0;
            int removed = 0;
            int skipped = 0;
            var assignmentLog = new List<(string viewName, string templateName)>();

            using (Transaction tx = new Transaction(doc, "STING View Template Assigner"))
            {
                tx.Start();

                foreach (View v in views)
                {
                    try
                    {
                        if (mode == 3)
                        {
                            // Remove all
                            if (v.ViewTemplateId != ElementId.InvalidElementId)
                            {
                                v.ViewTemplateId = ElementId.InvalidElementId;
                                removed++;
                            }
                            continue;
                        }

                        if (mode == 1 && v.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            skipped++;
                            continue;
                        }

                        // 7-layer intelligent matching
                        string disc = DocAutomationHelper.InferDisciplineFromView(v);
                        ViewFamily family = DocAutomationHelper.ViewFamilyFromViewType(v.ViewType);
                        string levelName = DocAutomationHelper.GetViewLevelName(doc, v);

                        View template = DocAutomationHelper.FindBestTemplate(doc, disc, family, levelName);
                        if (template != null)
                        {
                            v.ViewTemplateId = template.Id;
                            assigned++;
                            assignmentLog.Add((v.Name, template.Name));
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Template assign '{v.Name}': {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            if (mode == 3)
            {
                report.AppendLine($"Removed templates from {removed} views.");
            }
            else
            {
                report.AppendLine($"Assigned: {assigned}  Skipped: {skipped}");
                report.AppendLine();
                foreach (var (vn, tn) in assignmentLog.Take(20))
                    report.AppendLine($"  {vn} → {tn}");
                if (assignmentLog.Count > 20)
                    report.AppendLine($"  ... and {assignmentLog.Count - 20} more");
            }

            TaskDialog td = new TaskDialog("View Template Assigner");
            td.MainInstruction = mode == 3
                ? $"Removed {removed} template assignments"
                : $"Assigned {assigned} templates ({skipped} skipped)";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        // Delegate to consolidated helper methods in DocAutomationHelper
    }

    // ════════════════════════════════════════════════════════════════════
    //  6. DOCUMENTATION PACKAGE — One-click full documentation set
    //     Creates: Views + Dependent Views + Sheets + Templates + Tags
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DocumentationPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var sw = Stopwatch.StartNew();

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            var scopeBoxes = DocAutomationHelper.GetScopeBoxes(doc);
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate && v.Name.Contains("STING")).ToList();

            // Summary dialog
            TaskDialog dlg = new TaskDialog("Documentation Package");
            dlg.MainInstruction = "One-click full documentation set";
            dlg.MainContent =
                $"Project: {doc.Title}\n" +
                $"Levels: {levels.Count}\n" +
                $"Scope boxes: {scopeBoxes.Count}\n" +
                $"STING templates: {templates.Count}\n\n" +
                "This will:\n" +
                "  1. Create floor plans + RCPs for all levels × all disciplines\n" +
                "  2. Create dependent views from scope boxes\n" +
                "  3. Auto-assign STING view templates\n" +
                "  4. Create sheets with ISO 19650 numbering\n" +
                "  5. Place views on sheets with grid layout\n\n" +
                "This may take a few minutes on large projects.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Full package (all disciplines)",
                "A, S, M, E, P, FP, C — complete documentation set");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MEP package",
                "M, E, P, FP — mechanical, electrical, plumbing, fire");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var discResult = dlg.Show();
            (string Code, string Name, ViewFamily[] ViewTypes)[] selectedDiscs;
            switch (discResult)
            {
                case TaskDialogResult.CommandLink1:
                    selectedDiscs = DocAutomationHelper.Disciplines;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedDiscs = DocAutomationHelper.Disciplines
                        .Where(d => d.Code == "M" || d.Code == "E" || d.Code == "P" || d.Code == "FP")
                        .ToArray();
                    break;
                default:
                    return Result.Cancelled;
            }

            int viewsCreated = 0;
            int dependentsCreated = 0;
            int templatesAssigned = 0;
            int sheetsCreated = 0;
            int viewsPlaced = 0;
            int errors = 0;
            var nameCache = DocAutomationHelper.BuildViewNameIndex(doc);

            // Phase 1: Create Views
            using (Transaction tx1 = new Transaction(doc, "STING Doc Package — Create Views"))
            {
                tx1.Start();

                    foreach (var disc in selectedDiscs)
                    {
                        foreach (Level level in levels)
                        {
                            string shortLevel = DocAutomationHelper.GetShortLevelName(level);
                            // Plan views only for doc package (sections/elevations via dedicated commands)
                            var families = disc.ViewTypes.Where(f =>
                                f == ViewFamily.FloorPlan || f == ViewFamily.CeilingPlan ||
                                f == ViewFamily.StructuralPlan).ToList();

                            foreach (ViewFamily family in families)
                            {
                                try
                                {
                                    ViewFamilyType vft = DocAutomationHelper.FindViewFamilyType(doc, family);
                                    if (vft == null) continue;

                                    string typeName = DocAutomationHelper.ViewFamilyToTypeName(family);
                                    string viewName = DocAutomationHelper.BuildViewName(disc.Name, typeName, shortLevel);

                                    View newView = DocAutomationHelper.CreateViewByFamily(
                                        doc, family, vft, level, viewName, nameCache);
                                    if (newView == null) continue;
                                    viewsCreated++;

                                    // Auto template
                                    View template = DocAutomationHelper.FindBestTemplate(doc, disc.Name, family, level.Name);
                                    if (template != null)
                                    {
                                        newView.ViewTemplateId = template.Id;
                                        templatesAssigned++;
                                    }

                                    // Dependents from scope boxes
                                    foreach (Element sb in scopeBoxes)
                                    {
                                        try
                                        {
                                            string depName = DocAutomationHelper.BuildViewName(disc.Name, typeName, shortLevel, sb.Name);
                                            depName = DocAutomationHelper.GetUniqueViewName(doc, depName, nameCache);
                                            ElementId depId = newView.Duplicate(ViewDuplicateOption.AsDependent);
                                            View depView = doc.GetElement(depId) as View;
                                            if (depView != null)
                                            {
                                                depView.Name = depName;
                                                DocAutomationHelper.AssignScopeBox(depView, sb.Id);
                                                dependentsCreated++;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            errors++;
                                            StingLog.Warn($"DocPackage dependent: {ex.Message}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    errors++;
                                    StingLog.Warn($"DocPackage view: {ex.Message}");
                                }
                            }
                        }
                    }

                    tx1.Commit();
                }

                // Phase 2: Create Sheets and Place Views
                using (Transaction tx2 = new Transaction(doc, "STING Doc Package — Create Sheets"))
                {
                    tx2.Start();

                    FamilySymbol titleBlock = DocAutomationHelper.GetFirstTitleBlock(doc);
                    if (titleBlock != null)
                    {
                        if (!titleBlock.IsActive) titleBlock.Activate();

                        var existingNums = DocAutomationHelper.GetExistingSheetNumbers(doc);
                        var placedIds = DocAutomationHelper.GetPlacedViewIds(doc);

                        // Get newly created views (unplaced, STING-named)
                        var newViews = new FilteredElementCollector(doc)
                            .OfClass(typeof(View)).Cast<View>()
                            .Where(v => !v.IsTemplate && v.CanBePrinted &&
                                !(v is ViewSheet) && !placedIds.Contains(v.Id) &&
                                v.Name.StartsWith("STING") &&
                                v.GetPrimaryViewId() == ElementId.InvalidElementId) // parent views only
                            .OrderBy(v => v.Name)
                            .ToList();

                        foreach (View v in newViews)
                        {
                            try
                            {
                                string prefix = "G";
                                string nameUpper = (v.Name ?? "").ToUpperInvariant();
                                foreach (var disc in DocAutomationHelper.Disciplines)
                                {
                                    if (nameUpper.Contains(disc.Name.ToUpperInvariant()))
                                    {
                                        prefix = disc.Code;
                                        break;
                                    }
                                }

                                int startNum = DocAutomationHelper.SheetStartNumbers.TryGetValue(prefix, out int sn) ? sn + 1 : 1;
                                string sheetNum = DocAutomationHelper.NextSheetNumber(prefix, startNum, existingNums);

                                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                                sheet.SheetNumber = sheetNum;
                                sheet.Name = v.Name.Replace("STING - ", "");
                                sheetsCreated++;

                                if (Viewport.CanAddViewToSheet(doc, sheet.Id, v.Id))
                                {
                                    var (w, h) = DocAutomationHelper.GetTitleBlockSize(doc, sheet);
                                    Viewport.Create(doc, sheet.Id, v.Id, new XYZ(w / 2, h / 2, 0));
                                    viewsPlaced++;
                                }
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                StingLog.Warn($"DocPackage sheet: {ex.Message}");
                            }
                        }
                    }

                    tx2.Commit();
                }

            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine("Documentation Package Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Phase 1: {viewsCreated} parent views + {dependentsCreated} dependents");
            report.AppendLine($"  Templates: {templatesAssigned} auto-assigned");
            report.AppendLine($"  Phase 2: {sheetsCreated} sheets + {viewsPlaced} viewports");
            if (errors > 0)
                report.AppendLine($"  Errors: {errors}");
            report.AppendLine($"  Duration: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("Next: Run 'Tag & Combine' to tag all elements,");
            report.AppendLine("then 'Smart Place Tags' for annotation tags.");

            TaskDialog td = new TaskDialog("Documentation Package");
            td.MainInstruction = $"Created {viewsCreated + dependentsCreated} views, {sheetsCreated} sheets";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"DocPackage: views={viewsCreated}, deps={dependentsCreated}, " +
                $"templates={templatesAssigned}, sheets={sheetsCreated}, placed={viewsPlaced}, " +
                $"errors={errors}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  7. BATCH CREATE SECTIONS — From grids or scope boxes
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCreateSectionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .OrderBy(g => g.Name)
                .ToList();

            if (grids.Count == 0)
            {
                TaskDialog.Show("Batch Create Sections", "No grids found in the project.");
                return Result.Failed;
            }

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();

            double minElev = levels.Count > 0 ? levels.First().Elevation - 5.0 : -10.0;
            double maxElev = levels.Count > 0 ? levels.Last().Elevation + 15.0 : 50.0;

            ViewFamilyType sectionType = DocAutomationHelper.FindViewFamilyType(doc, ViewFamily.Section);
            if (sectionType == null)
            {
                TaskDialog.Show("Batch Create Sections", "No section ViewFamilyType found.");
                return Result.Failed;
            }

            TaskDialog dlg = new TaskDialog("Batch Create Sections");
            dlg.MainInstruction = $"Create sections from {grids.Count} grids";
            dlg.MainContent =
                $"Height range: {minElev * 0.3048:F1}m to {maxElev * 0.3048:F1}m\n" +
                $"Section depth: 50ft (adjustable after creation)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"All grids ({grids.Count})",
                "One section per grid line");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Primary grids only (numbered)",
                "Only grids with numeric names (1, 2, 3...)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Primary grids only (lettered)",
                "Only grids with letter names (A, B, C...)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            List<Grid> selectedGrids;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    selectedGrids = grids;
                    break;
                case TaskDialogResult.CommandLink2:
                    selectedGrids = grids.Where(g => g.Name.Length > 0 && g.Name.All(char.IsDigit)).ToList();
                    break;
                case TaskDialogResult.CommandLink3:
                    selectedGrids = grids.Where(g => g.Name.Length > 0 && g.Name.All(char.IsLetter)).ToList();
                    break;
                default:
                    return Result.Cancelled;
            }

            int created = 0;
            int errors = 0;

            using (Transaction tx = new Transaction(doc, "STING Batch Create Sections"))
            {
                tx.Start();

                foreach (Grid grid in selectedGrids)
                {
                    try
                    {
                        Curve gridCurve = grid.Curve;
                        if (gridCurve == null) continue;

                        XYZ start = gridCurve.GetEndPoint(0);
                        XYZ end = gridCurve.GetEndPoint(1);
                        XYZ midpoint = (start + end) / 2;
                        XYZ direction = (end - start).Normalize();

                        // Section perpendicular to grid
                        XYZ viewDirection = new XYZ(-direction.Y, direction.X, 0);
                        XYZ upDirection = XYZ.BasisZ;
                        double halfWidth = gridCurve.Length / 2 + 5.0;
                        double height = maxElev - minElev;
                        double farClip = 50.0;

                        BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                        Transform t = Transform.Identity;
                        t.Origin = new XYZ(midpoint.X, midpoint.Y, minElev);
                        t.BasisX = direction;
                        t.BasisY = upDirection;
                        t.BasisZ = viewDirection;
                        sectionBox.Transform = t;
                        sectionBox.Min = new XYZ(-halfWidth, 0, 0);
                        sectionBox.Max = new XYZ(halfWidth, height, farClip);

                        ViewSection section = ViewSection.CreateSection(doc, sectionType.Id, sectionBox);
                        if (section != null)
                        {
                            string name = $"STING - Section - Grid {grid.Name}";
                            name = DocAutomationHelper.GetUniqueViewName(doc, name);
                            section.Name = name;

                            // Auto-assign template
                            View template = DocAutomationHelper.FindViewTemplate(doc, "STING - Section");
                            if (template == null) template = DocAutomationHelper.FindViewTemplate(doc, "STING - Working Section");
                            if (template != null)
                                section.ViewTemplateId = template.Id;

                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        StingLog.Warn($"Create section for grid '{grid.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Create Sections",
                $"Created {created} sections from {selectedGrids.Count()} grids." +
                (errors > 0 ? $"\n{errors} errors (see log)." : ""));

            StingLog.Info($"BatchCreateSections: grids={selectedGrids.Count()}, created={created}, errors={errors}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  8. BATCH CREATE ELEVATIONS — Exterior + interior from rooms
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCreateElevationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            ViewFamilyType elevType = DocAutomationHelper.FindViewFamilyType(doc, ViewFamily.Elevation);
            if (elevType == null)
            {
                TaskDialog.Show("Batch Create Elevations", "No elevation ViewFamilyType found.");
                return Result.Failed;
            }

            // Need a floor plan view for the elevation marker
            var floorPlans = new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.ViewType == ViewType.FloorPlan && !v.IsTemplate && v.CanBePrinted)
                .ToList();

            if (floorPlans.Count == 0)
            {
                TaskDialog.Show("Batch Create Elevations", "No floor plan views found. Create floor plans first.");
                return Result.Failed;
            }

            TaskDialog dlg = new TaskDialog("Batch Create Elevations");
            dlg.MainInstruction = "Create elevation views";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "4 exterior elevations (N/S/E/W)",
                "Cardinal building elevations at project origin");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Interior elevations for rooms",
                "4 interior elevations per room (selected or all)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int mode;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = 1; break;
                case TaskDialogResult.CommandLink2: mode = 2; break;
                default: return Result.Cancelled;
            }

            View hostView = floorPlans[0];
            int created = 0;
            int errors = 0;
            string[] cardinals = { "North", "East", "South", "West" };

            using (Transaction tx = new Transaction(doc, "STING Batch Create Elevations"))
            {
                tx.Start();

                if (mode == 1)
                {
                    // Exterior elevations at origin
                    try
                    {
                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(
                            doc, elevType.Id, XYZ.Zero, 100);

                        for (int i = 0; i < 4; i++)
                        {
                            if (marker.GetViewId(i) != ElementId.InvalidElementId) continue;
                            try
                            {
                                ViewSection elev = marker.CreateElevation(doc, hostView.Id, i);
                                if (elev != null)
                                {
                                    string name = $"STING - Elevation - {cardinals[i]}";
                                    name = DocAutomationHelper.GetUniqueViewName(doc, name);
                                    elev.Name = name;

                                    View template = DocAutomationHelper.FindViewTemplate(doc, "STING - Elevation");
                                    if (template == null)
                                        template = DocAutomationHelper.FindViewTemplate(doc, "STING - Presentation Elevation");
                                    if (template != null)
                                        elev.ViewTemplateId = template.Id;

                                    created++;
                                }
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                StingLog.Warn($"Create elevation {cardinals[i]}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        StingLog.Warn($"Create elevation marker: {ex.Message}");
                    }
                }
                else
                {
                    // Interior elevations per room
                    var rooms = ctx.UIDoc.Selection.GetElementIds()
                        .Select(id => doc.GetElement(id) as Room)
                        .Where(r => r != null && r.Area > 0)
                        .ToList();

                    if (rooms.Count == 0)
                    {
                        rooms = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Rooms)
                            .WhereElementIsNotElementType()
                            .Cast<Room>()
                            .Where(r => r.Area > 0)
                            .Take(20) // limit to avoid performance issues
                            .ToList();
                    }

                    if (rooms.Count == 0)
                    {
                        TaskDialog.Show("Batch Create Elevations",
                            "No rooms found. Place rooms first or select rooms before running.");
                        tx.RollBack();
                        return Result.Failed;
                    }

                    foreach (Room room in rooms)
                    {
                        try
                        {
                            // Get room center point
                            LocationPoint loc = room.Location as LocationPoint;
                            if (loc == null) continue;
                            XYZ roomCenter = loc.Point;

                            ElevationMarker marker = ElevationMarker.CreateElevationMarker(
                                doc, elevType.Id, roomCenter, 50);

                            for (int i = 0; i < 4; i++)
                            {
                                if (marker.GetViewId(i) != ElementId.InvalidElementId) continue;
                                try
                                {
                                    ViewSection elev = marker.CreateElevation(doc, hostView.Id, i);
                                    if (elev != null)
                                    {
                                        string rmName = room.Name ?? "Room";
                                        string rmNum = room.Number ?? "";
                                        string name = $"STING - Interior Elev - {rmNum} {rmName} - {cardinals[i]}";
                                        name = DocAutomationHelper.GetUniqueViewName(doc, name);
                                        elev.Name = name;
                                        created++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    errors++;
                                    StingLog.Warn($"Interior elevation {room.Number}/{cardinals[i]}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            StingLog.Warn($"Elevation marker for room {room.Number}: {ex.Message}");
                        }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Create Elevations",
                $"Created {created} elevation views." +
                (errors > 0 ? $"\n{errors} errors (see log)." : ""));

            StingLog.Info($"BatchCreateElevations: mode={mode}, created={created}, errors={errors}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  9. DRAWING REGISTER — ISO 19650 full register with revision tracking
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Drawing Register", "No sheets found.");
                return Result.Succeeded;
            }

            // Collect all revisions
            var revisions = new FilteredElementCollector(doc)
                .OfClass(typeof(Revision))
                .Cast<Revision>()
                .OrderBy(r => r.SequenceNumber)
                .ToList();

            string projectName = doc.ProjectInformation?.Name ?? "Unknown";
            string projectNumber = doc.ProjectInformation?.Number ?? "";
            string originator = doc.ProjectInformation?.get_Parameter(BuiltInParameter.PROJECT_ORGANIZATION_NAME)?.AsString() ?? "";

            // Build CSV
            var csv = new List<string>();
            csv.Add("Sheet_Number,Sheet_Name,Discipline,Scale,Format," +
                "Status,Suitability,Revision_Number,Revision_Date,Revision_Description," +
                "Views_Count,Drawn_By,Checked_By,Approved_By");

            var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in sheets)
            {
                string num = sheet.SheetNumber;
                string name = sheet.Name;

                // Infer discipline from sheet number prefix
                string disc = num.Length >= 2 ? num.Substring(0, 1).ToUpperInvariant() : "G";
                if (num.Length >= 2 && num[1] != '-' && !char.IsDigit(num[1]))
                    disc = num.Substring(0, 2).ToUpperInvariant();

                if (!discCounts.ContainsKey(disc)) discCounts[disc] = 0;
                discCounts[disc]++;

                // Get revision info
                var revIds = sheet.GetAllRevisionIds();
                string revNum = "";
                string revDate = "";
                string revDesc = "";
                if (revIds.Count > 0)
                {
                    ElementId lastRevId = revIds.Last();
                    Revision rev = doc.GetElement(lastRevId) as Revision;
                    if (rev != null)
                    {
                        revNum = rev.SequenceNumber.ToString();
                        revDate = rev.RevisionDate ?? "";
                        revDesc = rev.Description ?? "";
                    }
                }

                // Get sheet parameters
                string drawnBy = sheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)?.AsString() ?? "";
                string checkedBy = sheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY)?.AsString() ?? "";
                string approvedBy = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                int viewCount = sheet.GetAllPlacedViews().Count;

                // Determine format from title block size
                string format = "A1"; // default
                var titleBlocks = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType().ToList();
                if (titleBlocks.Count > 0)
                {
                    BoundingBoxXYZ bb = titleBlocks[0].get_BoundingBox(null);
                    if (bb != null)
                    {
                        double widthMm = (bb.Max.X - bb.Min.X) * 304.8;
                        if (widthMm < 450) format = "A3";
                        else if (widthMm < 650) format = "A2";
                        else format = "A1";
                    }
                }

                // Status / suitability (ISO 19650)
                string status = revIds.Count > 0 ? "S3" : "S2";
                string suitability = revIds.Count > 0 ? "Stage approval" : "Information";

                csv.Add($"\"{num}\",\"{name}\",\"{disc}\",\"\",\"{format}\"," +
                    $"\"{status}\",\"{suitability}\",\"{revNum}\",\"{revDate}\",\"{revDesc.Replace("\"", "'")}\","+
                    $"{viewCount},\"{drawnBy}\",\"{checkedBy}\",\"{approvedBy}\"");
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine("ISO 19650 Drawing Register");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Project: {projectName}");
            report.AppendLine($"  Number:  {projectNumber}");
            report.AppendLine($"  Sheets:  {sheets.Count}");
            report.AppendLine($"  Revisions: {revisions.Count}");
            report.AppendLine();

            report.AppendLine("── BY DISCIPLINE ──");
            foreach (var kvp in discCounts.OrderBy(x => x.Key))
                report.AppendLine($"  [{kvp.Key}] {kvp.Value} sheets");
            report.AppendLine();

            // Revision summary
            if (revisions.Count > 0)
            {
                report.AppendLine("── REVISIONS ──");
                foreach (var rev in revisions.Take(10))
                    report.AppendLine($"  Rev {rev.SequenceNumber}: {rev.RevisionDate} — {rev.Description}");
                if (revisions.Count > 10)
                    report.AppendLine($"  ... and {revisions.Count - 10} more");
            }

            // Export CSV
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                string csvPath = Path.Combine(dir, $"STING_DrawingRegister_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(csvPath, string.Join("\n", csv));
                report.AppendLine();
                report.AppendLine($"  CSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingRegister CSV: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Drawing Register");
            td.MainInstruction = $"ISO 19650 Drawing Register — {sheets.Count} sheets";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  10. PROJECT BROWSER ORGANIZER — Automated view organization
    //      Sets browser organization by discipline/type/level
    // ════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectBrowserOrganizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && !(v is ViewSheet))
                .ToList();

            // Count views by type and analyze naming
            var byType = views.GroupBy(v => v.ViewType).OrderBy(g => g.Key.ToString());
            int stingViews = views.Count(v => v.Name.Contains("STING"));
            int withTemplate = views.Count(v => v.ViewTemplateId != ElementId.InvalidElementId);
            int dependentViews = views.Count(v => v.GetPrimaryViewId() != ElementId.InvalidElementId);

            TaskDialog dlg = new TaskDialog("Project Browser Organizer");
            dlg.MainInstruction = $"Organize {views.Count} views";
            dlg.MainContent =
                $"STING views: {stingViews}\n" +
                $"With template: {withTemplate}\n" +
                $"Dependent: {dependentViews}\n\n" +
                "Choose organization action:";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Rename views by discipline convention",
                "Apply 'STING - {Discipline} {Type} - {Level}' naming to all views");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Report view organization status",
                "Audit view naming, templates, scope boxes, and levels");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Clean up view names (remove duplicates/prefixes)",
                "Standardize naming across all views");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int mode;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: mode = 1; break;
                case TaskDialogResult.CommandLink2: mode = 2; break;
                case TaskDialogResult.CommandLink3: mode = 3; break;
                default: return Result.Cancelled;
            }

            if (mode == 2)
            {
                // Audit report
                var report = new StringBuilder();
                report.AppendLine("View Organization Audit");
                report.AppendLine(new string('═', 50));
                report.AppendLine($"  Total views: {views.Count}");
                report.AppendLine($"  STING-named: {stingViews}");
                report.AppendLine($"  Templated:   {withTemplate}");
                report.AppendLine($"  Dependent:   {dependentViews}");
                report.AppendLine();

                report.AppendLine("── BY VIEW TYPE ──");
                foreach (var g in byType)
                    report.AppendLine($"  {g.Key,-20} {g.Count(),5} views");
                report.AppendLine();

                // Views without templates
                var untemplated = views.Where(v => v.ViewTemplateId == ElementId.InvalidElementId).ToList();
                if (untemplated.Count > 0)
                {
                    report.AppendLine($"── VIEWS WITHOUT TEMPLATE ({untemplated.Count}) ──");
                    foreach (var v in untemplated.Take(15))
                        report.AppendLine($"  {v.ViewType,-15} {v.Name}");
                    if (untemplated.Count > 15)
                        report.AppendLine($"  ... and {untemplated.Count - 15} more");
                }

                // Views without scope boxes
                int noScopeBox = 0;
                foreach (var v in views)
                {
                    try
                    {
                        Parameter p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                        if (p != null)
                        {
                            ElementId sbId = p.AsElementId();
                            if (sbId == null || sbId == ElementId.InvalidElementId)
                                noScopeBox++;
                        }
                        else noScopeBox++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Scope box audit: {ex.Message}"); noScopeBox++; }
                }
                report.AppendLine();
                report.AppendLine($"  Views without scope box: {noScopeBox}");

                // Non-standard names
                var nonStandard = views.Where(v =>
                    !v.Name.StartsWith("STING") &&
                    !v.Name.StartsWith("{") && // dependent views
                    v.GetPrimaryViewId() == ElementId.InvalidElementId)
                    .ToList();
                if (nonStandard.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine($"── NON-STANDARD NAMES ({nonStandard.Count}) ──");
                    foreach (var v in nonStandard.Take(15))
                        report.AppendLine($"  {v.Name}");
                    if (nonStandard.Count > 15)
                        report.AppendLine($"  ... and {nonStandard.Count - 15} more");
                }

                TaskDialog td = new TaskDialog("View Organization Audit");
                td.MainInstruction = $"{views.Count} views analyzed";
                td.MainContent = report.ToString();
                td.Show();
                return Result.Succeeded;
            }

            // Mode 1 or 3: rename views
            int renamed = 0;
            int errors = 0;

            using (Transaction tx = new Transaction(doc, "STING Project Browser Organize"))
            {
                tx.Start();

                foreach (View v in views)
                {
                    if (v.GetPrimaryViewId() != ElementId.InvalidElementId)
                        continue; // skip dependent views

                    try
                    {
                        string oldName = v.Name;
                        string newName;

                        if (mode == 1)
                        {
                            // Build STING convention name
                            string disc = DocAutomationHelper.InferDisciplineFromView(v);
                            string typeName = DocAutomationHelper.ViewFamilyToTypeName(
                                DocAutomationHelper.ViewFamilyFromViewType(v.ViewType));
                            string levelName = DocAutomationHelper.GetViewLevelName(doc, v);
                            newName = DocAutomationHelper.BuildViewName(disc, typeName, levelName);
                        }
                        else // mode == 3
                        {
                            // Clean up name
                            newName = oldName;
                            // Remove common prefixes
                            newName = System.Text.RegularExpressions.Regex.Replace(
                                newName, @"\s*Copy\s*\d*$", "");
                            newName = System.Text.RegularExpressions.Regex.Replace(
                                newName, @"\s*\(\d+\)$", "");
                            newName = newName.Trim();
                        }

                        if (newName == oldName) continue;
                        newName = DocAutomationHelper.GetUniqueViewName(doc, newName);

                        v.Name = newName;
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        StingLog.Warn($"Rename view '{v.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Project Browser Organizer",
                $"Renamed {renamed} views." +
                (errors > 0 ? $"\n{errors} errors." : ""));

            return Result.Succeeded;
        }

        // Delegate to consolidated helper methods in DocAutomationHelper
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  REVISION CLOUD AUTO-CREATE
    //  Automatically creates revision clouds on sheets when elements have changed
    //  status (NEW → EXISTING, parameter modifications, etc.).
    //  Uses Revit RevisionCloud.Create API with element bounding boxes.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-place revision clouds on sheets where tagged elements have changed
    /// status or been modified. Tracks changes via STING STATUS parameter
    /// and tag completeness state.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionCloudAutoCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            // Get the latest revision in the project (or create one)
            var revisions = new FilteredElementCollector(doc)
                .OfClass(typeof(Revision))
                .Cast<Revision>()
                .OrderByDescending(r => r.SequenceNumber)
                .ToList();

            if (revisions.Count == 0)
            {
                TaskDialog.Show("Revision Cloud Auto-Create",
                    "No revisions found in the project.\nCreate a revision first using Revit's Sheet Issues/Revisions dialog.");
                return Result.Cancelled;
            }

            Revision latestRev = revisions[0];

            // Find all sheets with placed views
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Revision Cloud Auto-Create", "No sheets found in the project.");
                return Result.Cancelled;
            }

            // Scan for elements with status changes or incomplete tags
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            var changedElements = new List<Element>();

            foreach (var el in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat)) continue;

                string status = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                // Flag: status is NEW or DEMOLISHED (recent changes), or tag is incomplete
                if (status == "NEW" || status == "DEMOLISHED" ||
                    (!string.IsNullOrEmpty(tag1) && !TagConfig.TagIsComplete(tag1)))
                {
                    changedElements.Add(el);
                }
            }

            if (changedElements.Count == 0)
            {
                TaskDialog.Show("Revision Cloud Auto-Create",
                    "No recently changed elements detected.\n" +
                    "Elements with STATUS=NEW or DEMOLISHED, or incomplete tags, trigger clouds.");
                return Result.Succeeded;
            }

            // Confirm
            var confirm = new TaskDialog("Revision Cloud Auto-Create");
            confirm.MainInstruction = $"Found {changedElements.Count} changed elements";
            confirm.MainContent =
                $"Revision: {latestRev.Description} (#{latestRev.SequenceNumber})\n" +
                $"Sheets: {sheets.Count}\n\n" +
                "Place revision clouds on sheets containing changed elements?";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Build a lookup: ElementId → bounding box (in model space)
            var elementBoxes = new Dictionary<ElementId, BoundingBoxXYZ>();
            foreach (var el in changedElements)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb != null) elementBoxes[el.Id] = bb;
            }

            int cloudsCreated = 0;
            int sheetsAffected = 0;

            using (Transaction tx = new Transaction(doc, "STING Auto Revision Clouds"))
            {
                tx.Start();

                foreach (var sheet in sheets)
                {
                    // Get viewports on this sheet
                    var vpIds = sheet.GetAllViewports();
                    bool sheetHasCloud = false;

                    foreach (ElementId vpId in vpIds)
                    {
                        Viewport vp = doc.GetElement(vpId) as Viewport;
                        if (vp == null) continue;

                        View view = doc.GetElement(vp.ViewId) as View;
                        if (view == null) continue;

                        // Check if any changed elements are visible in this view
                        var visibleChanged = new List<ElementId>();
                        foreach (var kvp in elementBoxes)
                        {
                            try
                            {
                                Element el = doc.GetElement(kvp.Key);
                                if (el != null && el.get_BoundingBox(view) != null)
                                    visibleChanged.Add(kvp.Key);
                            }
                            catch { }
                        }

                        if (visibleChanged.Count == 0) continue;

                        // Create a revision cloud around the viewport center area
                        // Using viewport outline on sheet as the cloud boundary
                        try
                        {
                            Outline vpOutline = vp.GetBoxOutline();
                            XYZ minPt = vpOutline.MinimumPoint;
                            XYZ maxPt = vpOutline.MaximumPoint;

                            // Shrink outline slightly to show cloud within viewport
                            double shrink = 0.05; // feet
                            XYZ p1 = new XYZ(minPt.X + shrink, minPt.Y + shrink, 0);
                            XYZ p2 = new XYZ(maxPt.X - shrink, minPt.Y + shrink, 0);
                            XYZ p3 = new XYZ(maxPt.X - shrink, maxPt.Y - shrink, 0);
                            XYZ p4 = new XYZ(minPt.X + shrink, maxPt.Y - shrink, 0);

                            var curves = new List<Curve>
                            {
                                Line.CreateBound(p1, p2),
                                Line.CreateBound(p2, p3),
                                Line.CreateBound(p3, p4),
                                Line.CreateBound(p4, p1)
                            };

                            RevisionCloud.Create(doc, sheet, latestRev.Id, curves);
                            cloudsCreated++;
                            sheetHasCloud = true;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"RevCloud on sheet {sheet.SheetNumber}: {ex.Message}");
                        }
                    }

                    // Add revision to sheet if cloud was placed
                    if (sheetHasCloud)
                    {
                        sheetsAffected++;
                        try
                        {
                            var revIds = sheet.GetAdditionalRevisionIds();
                            if (!revIds.Contains(latestRev.Id))
                            {
                                var newRevIds = new List<ElementId>(revIds) { latestRev.Id };
                                sheet.SetAdditionalRevisionIds(newRevIds);
                            }
                        }
                        catch { }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Revision Cloud Auto-Create",
                $"Created {cloudsCreated} revision clouds on {sheetsAffected} sheets.\n\n" +
                $"Changed elements detected: {changedElements.Count}\n" +
                $"Revision: {latestRev.Description} (#{latestRev.SequenceNumber})");

            StingLog.Info($"RevisionCloudAuto: {cloudsCreated} clouds on {sheetsAffected} sheets, " +
                $"{changedElements.Count} changed elements");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  FM O/M HANDOVER MANUAL CREATOR
    //  Generates a comprehensive Facility Management / Operations & Maintenance
    //  handover manual from BIM data — asset register, maintenance schedules,
    //  system descriptions, and spatial summaries per ISO 19650 / BS 8210.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates an FM O/M Handover Manual — a structured CSV/text export containing:
    /// - Project summary and building information
    /// - Asset register grouped by discipline and system
    /// - Maintenance schedule recommendations per asset type
    /// - Spatial summary (rooms, levels, zones)
    /// - System descriptions and equipment lists
    /// - Tag completeness and compliance summary
    ///
    /// Inspired by StingBIM.AI.FacilityManagement and StingBIM.AI.Maintenance
    /// from the original Stingtools-Original repository.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HandoverManualCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                string projectName = doc.ProjectInformation?.Name ?? "Unnamed Project";
                string projectNumber = doc.ProjectInformation?.Number ?? "000";
                string projectAddress = doc.ProjectInformation?.Address ?? "";
                string projectStatus = doc.ProjectInformation?.Status ?? "";
                string clientName = doc.ProjectInformation?.ClientName ?? "";
                string projLoc = ParameterHelpers.GetString(doc.ProjectInformation, ParamRegistry.LOC);
                string projZone = ParameterHelpers.GetString(doc.ProjectInformation, ParamRegistry.ZONE);
                string createdBy = Environment.UserName;
                string createdOn = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

                var known = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);

                // Collect all taggable elements
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && known.Contains(e.Category.Name))
                    .ToList();

                // Group by discipline, system, level
                var byDisc = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
                var bySys = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
                var byLevel = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
                int tagged = 0, untagged = 0;

                foreach (var el in allElements)
                {
                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                    string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);

                    if (TagConfig.TagIsComplete(tag1)) tagged++; else untagged++;

                    string discKey = string.IsNullOrEmpty(disc) ? "Unassigned" : disc;
                    if (!byDisc.ContainsKey(discKey)) byDisc[discKey] = new List<Element>();
                    byDisc[discKey].Add(el);

                    string sysKey = string.IsNullOrEmpty(sys) ? "Unassigned" : sys;
                    if (!bySys.ContainsKey(sysKey)) bySys[sysKey] = new List<Element>();
                    bySys[sysKey].Add(el);

                    string lvlKey = string.IsNullOrEmpty(lvl) ? "Unknown" : lvl;
                    if (!byLevel.ContainsKey(lvlKey)) byLevel[lvlKey] = new List<Element>();
                    byLevel[lvlKey].Add(el);
                }

                // Collect rooms
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 0)
                    .OrderBy(r => r.Level?.Name ?? "")
                    .ThenBy(r => r.Number)
                    .ToList();

                // Collect levels
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                // PERF: Pre-build room→asset count index O(n)
                var roomAssetCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var el in allElements)
                {
                    string elRoom = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NUM);
                    if (string.IsNullOrEmpty(elRoom))
                        elRoom = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NUM);
                    if (!string.IsNullOrEmpty(elRoom))
                    {
                        roomAssetCounts.TryGetValue(elRoom, out int c);
                        roomAssetCounts[elRoom] = c + 1;
                    }
                }

                // Group types
                var typeGroups = allElements
                    .GroupBy(e =>
                    {
                        string family = ParameterHelpers.GetFamilyName(e);
                        string typeName = ParameterHelpers.GetFamilySymbolName(e);
                        return $"{family}:{typeName}";
                    })
                    .OrderBy(g => g.Key)
                    .ToList();

                double pct = allElements.Count > 0 ? (tagged * 100.0 / allElements.Count) : 0;

                // ── Save dialog (.xlsx) ─────────────────────────────────────────
                string defaultDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(defaultDir)) defaultDir = Path.GetTempPath();
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string defaultName = $"STING_COBie_FM_Handover_{timestamp}.xlsx";

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export COBie FM Handover Manual (.xlsx)",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    FileName = defaultName,
                    InitialDirectory = defaultDir
                };
                if (dlg.ShowDialog() != true)
                    return Result.Cancelled;
                string exportPath = dlg.FileName;

                // ══════════════════════════════════════════════════════════════════
                //  BUILD COBie 2.4 XLSX WORKBOOK (20 worksheets per NBIMS-US V3)
                //  Compliant with: BS 1192-4:2014 / BS EN ISO 19650-4:2022
                //  Column order per COBie V2.4 / COBie V3 specification
                // ══════════════════════════════════════════════════════════════════
                using (var wb = new XLWorkbook())
                {
                    int cobieComponentCount = 0;

                    // ── 1. INSTRUCTION worksheet ────────────────────────────────
                    var wsInst = wb.AddWorksheet("Instruction");
                    WriteRow(wsInst, 1, "Sheet Name", "Row Count", "Description");
                    StyleHeader(wsInst, 1, 3);
                    // Populated at end after all sheets are built
                    var instructionData = new List<(string sheet, int rows, string desc)>();

                    // ── 2. CONTACT worksheet ────────────────────────────────────
                    var wsCon = wb.AddWorksheet("Contact");
                    WriteRow(wsCon, 1,
                        "Email", "CreatedBy", "CreatedOn", "Category", "Company", "Phone",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Department", "OrganizationCode", "GivenName", "FamilyName",
                        "Street", "PostalBox", "Town", "StateRegion", "PostalCode", "Country");
                    StyleHeader(wsCon, 1, 19);
                    int conRow = 2;
                    // Project contact
                    WriteRow(wsCon, conRow++,
                        $"project@{projectName.Replace(" ", "").ToLower()}.com",
                        createdBy, createdOn, "Facility Manager", projectName, "",
                        "Revit", "IfcOrganization", projectNumber,
                        "", "", "", "",
                        projectAddress, "", "", "", "", "");
                    // Client contact
                    if (!string.IsNullOrEmpty(clientName))
                    {
                        WriteRow(wsCon, conRow++,
                            $"client@{clientName.Replace(" ", "").ToLower()}.com",
                            createdBy, createdOn, "Client / Owner", clientName, "",
                            "Revit", "IfcOrganization", "",
                            "", "", "", "",
                            "", "", "", "", "", "");
                    }
                    instructionData.Add(("Contact", conRow - 2, "Project stakeholders and contacts"));

                    // ── 3. FACILITY worksheet ───────────────────────────────────
                    var wsFac = wb.AddWorksheet("Facility");
                    WriteRow(wsFac, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category", "ProjectName", "SiteName",
                        "LinearUnits", "AreaUnits", "VolumeUnits", "CurrencyUnit",
                        "AreaMeasurement", "ExternalSystem", "ExternalProjectObject",
                        "ExternalProjectIdentifier", "ExternalSiteObject", "ExternalSiteIdentifier",
                        "ExternalFacilityObject", "ExternalFacilityIdentifier", "Description",
                        "ProjectDescription", "SiteDescription", "Phase");
                    StyleHeader(wsFac, 1, 22);
                    WriteRow(wsFac, 2,
                        projectName, createdBy, createdOn, "Office", projectName, projectAddress,
                        "meters", "square meters", "cubic meters", "UGX",
                        "ISO 19650", "Revit", "IfcProject",
                        projectNumber, "IfcSite", projLoc,
                        "IfcBuilding", projLoc, $"ISO 19650 compliant BIM facility",
                        projectStatus, projectAddress, "As-Built");
                    instructionData.Add(("Facility", 1, "Building / facility identification"));

                    // ── 4. FLOOR worksheet ──────────────────────────────────────
                    var wsFlr = wb.AddWorksheet("Floor");
                    WriteRow(wsFlr, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description", "Elevation", "Height");
                    StyleHeader(wsFlr, 1, 10);
                    int flrRow = 2;
                    foreach (var level in levels)
                    {
                        string lvlCode = ParameterHelpers.GetLevelCode(doc, level);
                        double elevM = level.Elevation * 0.3048;
                        // Compute floor height (distance to next level)
                        int idx = levels.IndexOf(level);
                        double heightM = idx < levels.Count - 1
                            ? (levels[idx + 1].Elevation - level.Elevation) * 0.3048 : 0;
                        WriteRow(wsFlr, flrRow++,
                            level.Name, createdBy, createdOn, "Floor",
                            "Revit", "IfcBuildingStorey", level.Id.ToString(),
                            lvlCode, elevM.ToString("F3"), heightM > 0 ? heightM.ToString("F3") : "");
                    }
                    instructionData.Add(("Floor", levels.Count, "Building levels / storeys"));

                    // ── 5. SPACE worksheet ──────────────────────────────────────
                    var wsSpc = wb.AddWorksheet("Space");
                    WriteRow(wsSpc, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "FloorName", "Description", "ExternalSystem", "ExternalObject",
                        "ExternalIdentifier", "RoomTag", "UsableHeight", "GrossArea", "NetArea");
                    StyleHeader(wsSpc, 1, 13);
                    int spcRow = 2;
                    foreach (var room in rooms)
                    {
                        string levelName = room.Level?.Name ?? "Unknown";
                        string dept = "";
                        try { dept = room.LookupParameter("Department")?.AsString() ?? ""; } catch { }
                        double areaM2 = room.Area * 0.092903;
                        double heightM = 0;
                        try
                        {
                            var ubh = room.LookupParameter("Unbounded Height");
                            if (ubh != null) heightM = ubh.AsDouble() * 0.3048;
                        }
                        catch { }

                        WriteRow(wsSpc, spcRow++,
                            $"{room.Number} - {room.Name}", createdBy, createdOn,
                            string.IsNullOrEmpty(dept) ? "Room" : dept,
                            levelName, room.Name, "Revit", "IfcSpace",
                            room.Id.ToString(), room.Number,
                            heightM.ToString("F2"), areaM2.ToString("F2"), areaM2.ToString("F2"));
                    }
                    instructionData.Add(("Space", rooms.Count, "Rooms / spaces"));

                    // ── 6. ZONE worksheet ───────────────────────────────────────
                    var wsZone = wb.AddWorksheet("Zone");
                    WriteRow(wsZone, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "SpaceNames", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description");
                    StyleHeader(wsZone, 1, 9);
                    int zoneRow = 2;
                    var zoneGroups = rooms
                        .GroupBy(r =>
                        {
                            string dept = "";
                            try { dept = r.LookupParameter("Department")?.AsString() ?? ""; } catch { }
                            return string.IsNullOrEmpty(dept) ? "General" : dept;
                        })
                        .OrderBy(g => g.Key);
                    foreach (var zg in zoneGroups)
                    {
                        var spaceNames = string.Join(", ", zg.Select(r => $"{r.Number} - {r.Name}").Take(50));
                        WriteRow(wsZone, zoneRow++,
                            zg.Key, createdBy, createdOn, "Occupancy Zone",
                            spaceNames, "Revit", "IfcZone", "",
                            $"{zg.Count()} spaces in {zg.Key} department");
                    }
                    instructionData.Add(("Zone", zoneGroups.Count(), "Spatial zone groupings"));

                    // ── 7. TYPE worksheet (mapped to MR_PARAMETERS) ─────────────
                    var wsType = wb.AddWorksheet("Type");
                    WriteRow(wsType, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category", "Description",
                        "AssetType", "Manufacturer", "ModelNumber",
                        "WarrantyGuarantorParts", "WarrantyDurationParts", "WarrantyGuarantorLabor",
                        "WarrantyDurationLabor", "WarrantyDurationUnit",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "ReplacementCost", "ExpectedLife", "DurationUnit",
                        "NominalLength", "NominalWidth", "NominalHeight",
                        "ModelReference", "Shape", "Size", "Color",
                        "Finish", "Grade", "Material", "Constituents",
                        "Features", "AccessibilityPerformance", "CodePerformance",
                        "SustainabilityPerformance");
                    StyleHeader(wsType, 1, 34);
                    int typeRow = 2;
                    foreach (var tg in typeGroups)
                    {
                        var sample = tg.First();
                        string typeName = ParameterHelpers.GetFamilySymbolName(sample);
                        string cat = sample.Category?.Name ?? "";
                        string mfr = ParameterHelpers.GetString(sample, ParamRegistry.MFR);
                        string modelNr = ParameterHelpers.GetString(sample, ParamRegistry.MODEL);
                        string desc = ParameterHelpers.GetString(sample, ParamRegistry.DESC);
                        if (string.IsNullOrEmpty(desc)) desc = typeName;
                        string cost = ParameterHelpers.GetString(sample, ParamRegistry.COST);
                        string size = ParameterHelpers.GetString(sample, ParamRegistry.SIZE);

                        // COBie fields mapped to MR_PARAMETERS via ParamRegistry
                        string disc = ParameterHelpers.GetString(sample, ParamRegistry.DISC);
                        string assetType = disc switch
                        {
                            "M" or "E" or "P" or "FP" => "Fixed",
                            "A" or "S" => "Fixed",
                            _ => "Moveable"
                        };
                        string warrGuarParts = ParameterHelpers.GetString(sample, ParamRegistry.WARR_GUAR_PARTS);
                        if (string.IsNullOrEmpty(warrGuarParts)) warrGuarParts = mfr;
                        string warrDurParts = ParameterHelpers.GetString(sample, ParamRegistry.WARR_DUR_PARTS);
                        string warrGuarLabor = ParameterHelpers.GetString(sample, ParamRegistry.WARR_GUAR_LABOR);
                        if (string.IsNullOrEmpty(warrGuarLabor)) warrGuarLabor = mfr;
                        string warrDurLabor = ParameterHelpers.GetString(sample, ParamRegistry.WARR_DUR_LABOR);
                        string warrDurUnit = ParameterHelpers.GetString(sample, ParamRegistry.WARR_DUR_UNIT);
                        if (string.IsNullOrEmpty(warrDurUnit)) warrDurUnit = "year";
                        string replaceCost = ParameterHelpers.GetString(sample, ParamRegistry.REPLACE_COST);
                        if (string.IsNullOrEmpty(replaceCost)) replaceCost = cost;
                        string expectedLife = ParameterHelpers.GetString(sample, "ASS_EXPECTED_LIFE_YEARS_YRS");
                        string durUnit = ParameterHelpers.GetString(sample, ParamRegistry.DUR_UNIT);
                        if (string.IsNullOrEmpty(durUnit)) durUnit = "year";
                        string nomLen = ParameterHelpers.GetString(sample, ParamRegistry.NOM_LENGTH);
                        string nomWid = ParameterHelpers.GetString(sample, ParamRegistry.NOM_WIDTH);
                        string nomHt = ParameterHelpers.GetString(sample, ParamRegistry.NOM_HEIGHT);
                        string modelRef = ParameterHelpers.GetString(sample, ParamRegistry.MODEL_REF);
                        string shape = ParameterHelpers.GetString(sample, ParamRegistry.SHAPE);
                        string color = ParameterHelpers.GetString(sample, ParamRegistry.COLOR);
                        string finish = ParameterHelpers.GetString(sample, ParamRegistry.FINISH);
                        string grade = ParameterHelpers.GetString(sample, ParamRegistry.GRADE);
                        string material = ParameterHelpers.GetString(sample, ParamRegistry.MATERIAL);
                        string constituents = ParameterHelpers.GetString(sample, ParamRegistry.CONSTITUENTS);
                        string features = ParameterHelpers.GetString(sample, ParamRegistry.FEATURES);
                        string accessPerf = ParameterHelpers.GetString(sample, ParamRegistry.ACCESS_PERF);
                        string codePerf = ParameterHelpers.GetString(sample, ParamRegistry.CODE_PERF);
                        string sustainPerf = ParameterHelpers.GetString(sample, ParamRegistry.SUSTAIN_PERF);

                        WriteRow(wsType, typeRow++,
                            typeName, createdBy, createdOn, cat, desc,
                            assetType, mfr, modelNr,
                            warrGuarParts, warrDurParts, warrGuarLabor, warrDurLabor, warrDurUnit,
                            "Revit", "IfcTypeObject", "",
                            replaceCost, expectedLife, durUnit,
                            nomLen, nomWid, nomHt,
                            modelRef, shape, size, color,
                            finish, grade, material, constituents,
                            features, accessPerf, codePerf, sustainPerf);
                    }
                    instructionData.Add(("Type", typeGroups.Count, "Asset types / product categories"));

                    // ── 8. COMPONENT worksheet (mapped to MR_PARAMETERS) ────────
                    var wsComp = wb.AddWorksheet("Component");
                    WriteRow(wsComp, 1,
                        "Name", "CreatedBy", "CreatedOn", "TypeName", "Space",
                        "Description", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "SerialNumber", "InstallationDate", "WarrantyStartDate",
                        "TagNumber", "BarCode", "AssetIdentifier");
                    StyleHeader(wsComp, 1, 15);
                    int compRow = 2;
                    foreach (var el in allElements)
                    {
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1)) continue;

                        string assetName = ParameterHelpers.GetString(el, ParamRegistry.DESC);
                        if (string.IsNullOrEmpty(assetName))
                            assetName = ParameterHelpers.GetFamilySymbolName(el);
                        string typeName = ParameterHelpers.GetFamilySymbolName(el);
                        string roomName = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NAME);
                        if (string.IsNullOrEmpty(roomName))
                            roomName = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NAME);
                        // All COBie Component fields mapped to MR_PARAMETERS
                        string serial = ParameterHelpers.GetString(el, "ASS_SERIAL_NR_TXT");
                        string installDate = ParameterHelpers.GetString(el, ParamRegistry.INSTALL_DATE);
                        string warrStart = ParameterHelpers.GetString(el, ParamRegistry.WARRANTY_START);
                        if (string.IsNullOrEmpty(warrStart)) warrStart = installDate;
                        string barcode = ParameterHelpers.GetString(el, ParamRegistry.BARCODE);
                        if (string.IsNullOrEmpty(barcode)) barcode = tag1;
                        string assetId = ParameterHelpers.GetString(el, ParamRegistry.ASSET_ID);
                        if (string.IsNullOrEmpty(assetId)) assetId = tag1;

                        WriteRow(wsComp, compRow++,
                            assetName, createdBy, createdOn, typeName, roomName,
                            assetName, "Revit", "IfcElement", el.Id.ToString(),
                            serial, installDate, warrStart,
                            tag1, barcode, assetId);
                        cobieComponentCount++;
                    }
                    instructionData.Add(("Component", cobieComponentCount, "Individual asset instances"));

                    // ── 9. SYSTEM worksheet ─────────────────────────────────────
                    var wsSys = wb.AddWorksheet("System");
                    WriteRow(wsSys, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "ComponentNames", "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description");
                    StyleHeader(wsSys, 1, 9);
                    int sysRow = 2;
                    foreach (var kvp in bySys.OrderBy(x => x.Key))
                    {
                        if (kvp.Key == "Unassigned") continue;
                        string sysDesc = GetSystemDescription(kvp.Key);
                        var componentTags = string.Join(", ",
                            kvp.Value
                            .Select(e => ParameterHelpers.GetString(e, ParamRegistry.TAG1))
                            .Where(t => !string.IsNullOrEmpty(t))
                            .Take(50));
                        WriteRow(wsSys, sysRow++,
                            $"{kvp.Key} - {sysDesc}", createdBy, createdOn, kvp.Key,
                            componentTags, "Revit", "IfcSystem", "",
                            $"{sysDesc} ({kvp.Value.Count} components)");
                    }
                    int sysCount = sysRow - 2;
                    instructionData.Add(("System", sysCount, "MEP building systems"));

                    // ── 10. ASSEMBLY worksheet ──────────────────────────────────
                    var wsAsm = wb.AddWorksheet("Assembly");
                    WriteRow(wsAsm, 1,
                        "Name", "CreatedBy", "CreatedOn", "SheetName", "ParentName",
                        "ChildNames", "AssemblyType", "ExternalSystem", "ExternalObject",
                        "ExternalIdentifier", "Description");
                    StyleHeader(wsAsm, 1, 11);
                    instructionData.Add(("Assembly", 0, "Assembly relationships (populated during commissioning)"));

                    // ── 11. CONNECTION worksheet ────────────────────────────────
                    var wsConn = wb.AddWorksheet("Connection");
                    WriteRow(wsConn, 1,
                        "Name", "CreatedBy", "CreatedOn", "ConnectionType",
                        "SheetName", "RowName1", "RowName2",
                        "RealizingElement", "PortName1", "PortName2",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description");
                    StyleHeader(wsConn, 1, 14);
                    instructionData.Add(("Connection", 0, "Element connections (populated during commissioning)"));

                    // ── 12. SPARE worksheet ─────────────────────────────────────
                    var wsSpare = wb.AddWorksheet("Spare");
                    WriteRow(wsSpare, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "TypeName", "Suppliers",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description", "SetNumber", "PartNumber");
                    StyleHeader(wsSpare, 1, 12);
                    instructionData.Add(("Spare", 0, "Spare parts (populated by commissioning agent)"));

                    // ── 13. RESOURCE worksheet ──────────────────────────────────
                    var wsRes = wb.AddWorksheet("Resource");
                    WriteRow(wsRes, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description");
                    StyleHeader(wsRes, 1, 8);
                    instructionData.Add(("Resource", 0, "Materials, tools, training (populated by commissioning agent)"));

                    // ── 14. JOB worksheet ───────────────────────────────────────
                    var wsJob = wb.AddWorksheet("Job");
                    WriteRow(wsJob, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "Status", "TypeName", "Description",
                        "Duration", "DurationUnit", "Start",
                        "TaskStartUnit", "Frequency", "FrequencyUnit",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "TaskNumber", "Priors");
                    StyleHeader(wsJob, 1, 18);
                    int jobRow = 2;
                    int jobNum = 1;
                    foreach (var kvp in bySys.OrderBy(x => x.Key))
                    {
                        if (kvp.Key == "Unassigned") continue;
                        var (maintType, frequency, priority, notes) =
                            GetMaintenanceSchedule(kvp.Key, kvp.Value.FirstOrDefault()?.Category?.Name ?? "");

                        string freqUnit = frequency switch
                        {
                            "Quarterly" => "quarter",
                            "6-Monthly" => "month",
                            "Annually" => "year",
                            "5-Yearly" => "year",
                            _ => "year"
                        };
                        string freqVal = frequency switch
                        {
                            "Quarterly" => "3",
                            "6-Monthly" => "6",
                            "Annually" => "12",
                            "5-Yearly" => "60",
                            _ => "12"
                        };

                        WriteRow(wsJob, jobRow++,
                            $"{kvp.Key} - {maintType} Maintenance", createdBy, createdOn, maintType,
                            "Not Started", kvp.Key, notes,
                            "1", "day", "",
                            "", freqVal, freqUnit,
                            "BS 8210/SFG20", "IfcTask", "",
                            jobNum.ToString("D3"), priority);
                        jobNum++;
                    }
                    instructionData.Add(("Job", jobNum - 1, "Maintenance tasks (BS 8210 / SFG20)"));

                    // ── 15. IMPACT worksheet ────────────────────────────────────
                    var wsImp = wb.AddWorksheet("Impact");
                    WriteRow(wsImp, 1,
                        "Name", "CreatedBy", "CreatedOn", "ImpactType",
                        "ImpactStage", "SheetName", "RowName",
                        "Value", "Unit", "LeadInTime", "Duration", "LeadOutTime",
                        "ExternalSystem", "ExternalObject", "ExternalIdentifier",
                        "Description");
                    StyleHeader(wsImp, 1, 16);
                    instructionData.Add(("Impact", 0, "Environmental impacts (populated at handover)"));

                    // ── 16. DOCUMENT worksheet ──────────────────────────────────
                    var wsDoc = wb.AddWorksheet("Document");
                    WriteRow(wsDoc, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "ApprovalBy", "Stage", "SheetName", "RowName",
                        "Directory", "File", "ExternalSystem", "ExternalObject",
                        "ExternalIdentifier", "Description", "Reference");
                    StyleHeader(wsDoc, 1, 15);
                    int docRow = 2;
                    WriteRow(wsDoc, docRow++,
                        "FM Handover Manual", createdBy, createdOn, "Handover",
                        projectName, "As-Built", "Facility", projectName,
                        "", exportPath, "STING Tools", "IfcDocumentInformation",
                        projectNumber, "ISO 19650 FM O/M Handover Manual", projectNumber);
                    WriteRow(wsDoc, docRow++,
                        "Asset Tag Register", createdBy, createdOn, "Register",
                        projectName, "As-Built", "Component", "",
                        "", "", "STING Tools", "IfcDocumentInformation",
                        "", "Complete STING ISO 19650 asset tag register", "");
                    WriteRow(wsDoc, docRow++,
                        "Maintenance Schedule", createdBy, createdOn, "Schedule",
                        projectName, "As-Built", "Job", "",
                        "", "", "STING Tools", "IfcDocumentInformation",
                        "", "BS 8210/SFG20 maintenance recommendations", "");
                    WriteRow(wsDoc, docRow++,
                        "O&M Manual", createdBy, createdOn, "Operation",
                        projectName, "As-Built", "Facility", projectName,
                        "", "", "STING Tools", "IfcDocumentInformation",
                        "", "Operations & Maintenance Manual", "");
                    WriteRow(wsDoc, docRow++,
                        "Health & Safety File", createdBy, createdOn, "Safety",
                        projectName, "As-Built", "Facility", projectName,
                        "", "", "STING Tools", "IfcDocumentInformation",
                        "", "CDM Health & Safety File", "");
                    instructionData.Add(("Document", docRow - 2, "Project document references"));

                    // ── 17. ATTRIBUTE worksheet (all STING parameters per component) ──
                    var wsAttr = wb.AddWorksheet("Attribute");
                    WriteRow(wsAttr, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "SheetName", "RowName", "Value", "Unit",
                        "ExtSystem", "ExtObject", "ExtIdentifier",
                        "Description", "AllowedValues");
                    StyleHeader(wsAttr, 1, 13);
                    int attrRow = 2;
                    // Project-level attributes
                    WriteRow(wsAttr, attrRow++,
                        "TagCompliance", createdBy, createdOn, "ISO 19650",
                        "Facility", projectName, $"{pct:F1}%", "percent",
                        "STING Tools", "IfcPropertySingleValue", "",
                        "ISO 19650 tag completeness percentage", "");
                    WriteRow(wsAttr, attrRow++,
                        "TotalAssets", createdBy, createdOn, "Asset",
                        "Facility", projectName, allElements.Count.ToString(), "count",
                        "STING Tools", "IfcPropertySingleValue", "",
                        "Total taggable assets in project", "");
                    WriteRow(wsAttr, attrRow++,
                        "GrossFloorArea", createdBy, createdOn, "Spatial",
                        "Facility", projectName,
                        (rooms.Sum(r => r.Area) * 0.092903).ToString("F2"), "m2",
                        "Revit", "IfcPropertySingleValue", "",
                        "Gross internal floor area", "");

                    // Per-component STING parameter attributes (aligned with MR_PARAMETERS)
                    // Export all non-empty STING parameters as COBie Attributes per component
                    var attrParams = new (string Key, string ParamName, string Unit, string Desc)[]
                    {
                        ("DISC", ParamRegistry.DISC, "", "Discipline code"),
                        ("LOC", ParamRegistry.LOC, "", "Location code"),
                        ("ZONE", ParamRegistry.ZONE, "", "Zone code"),
                        ("LVL", ParamRegistry.LVL, "", "Level code"),
                        ("SYS", ParamRegistry.SYS, "", "System type"),
                        ("FUNC", ParamRegistry.FUNC, "", "Function code"),
                        ("PROD", ParamRegistry.PROD, "", "Product code"),
                        ("SEQ", ParamRegistry.SEQ, "", "Sequence number"),
                        ("STATUS", ParamRegistry.STATUS, "", "Asset status"),
                        ("MFR", ParamRegistry.MFR, "", "Manufacturer"),
                        ("MODEL", ParamRegistry.MODEL, "", "Model number"),
                        ("SERIAL", "ASS_SERIAL_NR_TXT", "", "Serial number"),
                        ("COST", ParamRegistry.COST, "UGX", "Unit cost"),
                        ("REPLACE_COST", ParamRegistry.REPLACE_COST, "UGX", "Replacement cost"),
                        ("EXPECTED_LIFE", "ASS_EXPECTED_LIFE_YEARS_YRS", "year", "Expected life"),
                        ("WARRANTY_PERIOD", "ASS_WARRANTY_PERIOD_TXT", "", "Warranty period"),
                        ("WARRANTY_EXPIRY", "ASS_WARRANTY_EXPIRATION_DATE_TXT", "", "Warranty expiration"),
                        ("CONDITION", ParamRegistry.CONDITION, "", "Condition assessment"),
                        ("FIRE_RATING", ParamRegistry.FIRE_RATING, "min", "Fire resistance rating"),
                        ("COLOR", ParamRegistry.COLOR, "", "Element colour"),
                        ("FINISH", ParamRegistry.FINISH, "", "Surface finish"),
                        ("MATERIAL", ParamRegistry.MATERIAL, "", "Primary material"),
                        ("SUPPLIER", ParamRegistry.SUPPLIER, "", "Supplier / vendor"),
                        ("UNIFORMAT", ParamRegistry.UNIFORMAT, "", "Uniformat code"),
                        ("OMNICLASS", ParamRegistry.OMNICLASS, "", "OmniClass code"),
                        ("KEYNOTE", ParamRegistry.KEYNOTE, "", "Keynote"),
                        ("ROOM_NAME", ParamRegistry.ROOM_NAME, "", "Room name"),
                        ("ROOM_NUM", ParamRegistry.ROOM_NUM, "", "Room number"),
                        ("DEPT", ParamRegistry.DEPT, "", "Department"),
                        ("GRID_REF", ParamRegistry.GRID_REF, "", "Grid reference"),
                        ("ELC_POWER", ParamRegistry.ELC_POWER, "kW", "Circuit power"),
                        ("ELC_VOLTAGE", ParamRegistry.ELC_VOLTAGE, "V", "Voltage"),
                        ("HVC_AIRFLOW", ParamRegistry.HVC_AIRFLOW, "L/s", "Airflow"),
                        ("PLM_PIPE_SIZE", ParamRegistry.PLM_PIPE_SIZE, "mm", "Pipe size"),
                        ("LTG_WATTAGE", ParamRegistry.LTG_WATTAGE, "W", "Lamp wattage"),
                        ("LTG_LUMENS", ParamRegistry.LTG_LUMENS, "lm", "Lumen output"),
                    };
                    foreach (var el in allElements)
                    {
                        string compTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(compTag)) continue;
                        string compName = ParameterHelpers.GetString(el, ParamRegistry.DESC);
                        if (string.IsNullOrEmpty(compName))
                            compName = ParameterHelpers.GetFamilySymbolName(el);

                        foreach (var (key, paramName, unit, attrDesc) in attrParams)
                        {
                            if (string.IsNullOrEmpty(paramName)) continue;
                            string val = ParameterHelpers.GetString(el, paramName);
                            if (string.IsNullOrEmpty(val)) continue;
                            WriteRow(wsAttr, attrRow++,
                                key, createdBy, createdOn, "STING Parameter",
                                "Component", compTag, val, unit,
                                "STING Tools", "IfcPropertySingleValue", "",
                                attrDesc, "");
                        }
                    }
                    instructionData.Add(("Attribute", attrRow - 2, "Extended STING parameter data per component"));

                    // ── 18. COORDINATE worksheet ────────────────────────────────
                    var wsCoord = wb.AddWorksheet("Coordinate");
                    WriteRow(wsCoord, 1,
                        "Name", "CreatedBy", "CreatedOn", "Category",
                        "SheetName", "RowName",
                        "CoordinateXAxis", "CoordinateYAxis", "CoordinateZAxis",
                        "ExtSystem", "ExtObject", "ExtIdentifier",
                        "ClockwiseRotation", "ElevationalRotation", "YawRotation");
                    StyleHeader(wsCoord, 1, 15);
                    instructionData.Add(("Coordinate", 0, "Element coordinates (export via IFC)"));

                    // ── 19. ISSUE worksheet ─────────────────────────────────────
                    var wsIssue = wb.AddWorksheet("Issue");
                    WriteRow(wsIssue, 1,
                        "Name", "CreatedBy", "CreatedOn", "Type",
                        "Risk", "Chance", "Impact",
                        "SheetName1", "RowName1", "SheetName2", "RowName2",
                        "Description", "Owner", "Mitigation");
                    StyleHeader(wsIssue, 1, 14);
                    int issueRow = 2;
                    // Report untagged assets as issues
                    if (untagged > 0)
                    {
                        WriteRow(wsIssue, issueRow++,
                            "Incomplete Asset Tags", createdBy, createdOn, "Non-Compliance",
                            "Medium", "High", "Medium",
                            "Component", "", "", "",
                            $"{untagged} assets have incomplete ISO 19650 tags ({pct:F1}% compliance)",
                            "BIM Manager", "Run STING Tag & Combine to complete all asset tags");
                    }
                    instructionData.Add(("Issue", issueRow - 2, "Project issues and risks"));

                    // ── 20. PICKLISTS worksheet ─────────────────────────────────
                    var wsPick = wb.AddWorksheet("PickLists");
                    WriteRow(wsPick, 1,
                        "CategoryType", "FloorType", "SpaceType", "ZoneType",
                        "AssetType", "ComponentType", "ImpactType", "ImpactStage",
                        "ImpactUnit", "ConnectionType", "ApprovalType", "StageType",
                        "objType", "ResourceType", "StatusType", "UnitType",
                        "AreaUnit", "LinearUnit", "VolumeUnit", "CostUnit",
                        "DurationUnit", "FrequencyUnit");
                    StyleHeader(wsPick, 1, 22);
                    // Standard pick list values per COBie 2.4 / Uniclass 2015
                    string[] floorTypes = { "Site", "Floor", "Roof", "Basement" };
                    string[] assetTypes = { "Fixed", "Moveable" };
                    string[] statusTypes = { "Not Started", "Started", "Completed", "In Progress" };
                    string[] durationUnits = { "day", "week", "month", "year" };
                    string[] areaUnits = { "square meters", "square feet" };
                    string[] linearUnits = { "meters", "millimeters", "feet" };
                    string[] volumeUnits = { "cubic meters", "cubic feet" };
                    string[] approvalTypes = { "Approved", "Pending", "Rejected" };
                    int maxRows = Math.Max(Math.Max(floorTypes.Length, assetTypes.Length),
                        Math.Max(statusTypes.Length, durationUnits.Length));
                    for (int i = 0; i < maxRows; i++)
                    {
                        int r = i + 2;
                        if (i < floorTypes.Length) wsPick.Cell(r, 2).Value = floorTypes[i];
                        if (i < assetTypes.Length) wsPick.Cell(r, 5).Value = assetTypes[i];
                        if (i < approvalTypes.Length) wsPick.Cell(r, 11).Value = approvalTypes[i];
                        if (i < statusTypes.Length) wsPick.Cell(r, 15).Value = statusTypes[i];
                        if (i < areaUnits.Length) wsPick.Cell(r, 17).Value = areaUnits[i];
                        if (i < linearUnits.Length) wsPick.Cell(r, 18).Value = linearUnits[i];
                        if (i < volumeUnits.Length) wsPick.Cell(r, 19).Value = volumeUnits[i];
                        if (i < durationUnits.Length) wsPick.Cell(r, 21).Value = durationUnits[i];
                    }
                    instructionData.Add(("PickLists", maxRows, "Standard enumeration values (Uniclass 2015)"));

                    // ── BONUS: Asset Register worksheet (all STING MR_PARAMETERS) ──
                    var wsAsset = wb.AddWorksheet("Asset Register");
                    WriteRow(wsAsset, 1,
                        "Discipline", "Category", "Family", "Type", "ISO 19650 Tag",
                        "Level", "Room", "System", "Function", "Status",
                        "Manufacturer", "Model", "Serial Nr", "Description",
                        "Cost (UGX)", "Condition", "Warranty Period", "Fire Rating",
                        "Material", "Supplier", "Barcode", "Uniformat", "OmniClass");
                    StyleHeader(wsAsset, 1, 23);
                    int assetRow = 2;
                    foreach (var discGroup in byDisc.OrderBy(x => x.Key))
                    {
                        foreach (var el in discGroup.Value.OrderBy(e => ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                        {
                            string cat = el.Category?.Name ?? "";
                            string family = ParameterHelpers.GetFamilyName(el);
                            string typeName = ParameterHelpers.GetFamilySymbolName(el);
                            string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            string lvl = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                            string roomName = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NAME);
                            if (string.IsNullOrEmpty(roomName))
                                roomName = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NAME);
                            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
                            string status = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                            string mfr = ParameterHelpers.GetString(el, ParamRegistry.MFR);
                            string model = ParameterHelpers.GetString(el, ParamRegistry.MODEL);
                            string serial = ParameterHelpers.GetString(el, "ASS_SERIAL_NR_TXT");
                            string desc = ParameterHelpers.GetString(el, ParamRegistry.DESC);
                            string cost = ParameterHelpers.GetString(el, ParamRegistry.COST);
                            string condition = ParameterHelpers.GetString(el, ParamRegistry.CONDITION);
                            string warranty = ParameterHelpers.GetString(el, "ASS_WARRANTY_PERIOD_TXT");
                            string fireRating = ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING);
                            string material = ParameterHelpers.GetString(el, ParamRegistry.MATERIAL);
                            string supplier = ParameterHelpers.GetString(el, ParamRegistry.SUPPLIER);
                            string barcode = ParameterHelpers.GetString(el, ParamRegistry.BARCODE);
                            string uniformat = ParameterHelpers.GetString(el, ParamRegistry.UNIFORMAT);
                            string omniclass = ParameterHelpers.GetString(el, ParamRegistry.OMNICLASS);

                            WriteRow(wsAsset, assetRow++,
                                discGroup.Key, cat, family, typeName, tag1,
                                lvl, roomName, sys, func, status,
                                mfr, model, serial, desc,
                                cost, condition, warranty, fireRating,
                                material, supplier, barcode, uniformat, omniclass);
                        }
                    }

                    // ── BONUS: Maintenance Schedule worksheet ───────────────────
                    var wsMaint = wb.AddWorksheet("Maintenance Schedule");
                    WriteRow(wsMaint, 1,
                        "System", "System Description", "Category", "Asset Count",
                        "Maintenance Type", "Frequency", "Priority", "BS 8210 Notes");
                    StyleHeader(wsMaint, 1, 8);
                    int maintRow = 2;
                    foreach (var kvp in bySys.OrderBy(x => x.Key))
                    {
                        var categories = kvp.Value
                            .GroupBy(e => e.Category?.Name ?? "Unknown")
                            .OrderBy(g => g.Key);
                        foreach (var catGroup in categories)
                        {
                            var (maintType, frequency, priority, notes) =
                                GetMaintenanceSchedule(kvp.Key, catGroup.Key);
                            WriteRow(wsMaint, maintRow++,
                                kvp.Key, GetSystemDescription(kvp.Key), catGroup.Key,
                                catGroup.Count().ToString(),
                                maintType, frequency, priority, notes);
                        }
                    }

                    // ── Populate Instruction sheet ──────────────────────────────
                    int instRow = 2;
                    foreach (var (sheet, rowCount, desc) in instructionData)
                    {
                        WriteRow(wsInst, instRow++, sheet, rowCount.ToString(), desc);
                    }
                    WriteRow(wsInst, instRow++, "Asset Register", (assetRow - 2).ToString(), "Full STING asset register by discipline");
                    WriteRow(wsInst, instRow++, "Maintenance Schedule", (maintRow - 2).ToString(), "BS 8210/SFG20 maintenance recommendations");
                    WriteRow(wsInst, instRow, "", "", "");
                    WriteRow(wsInst, instRow + 1, "Standard", "", "COBie V2.4 / BS EN ISO 19650-4:2022 / BS 8210 / SFG20");
                    WriteRow(wsInst, instRow + 2, "Generated", "", createdOn);
                    WriteRow(wsInst, instRow + 3, "Tool", "", "STING Tools for Revit");

                    // ── Format all sheets ───────────────────────────────────────
                    foreach (var ws in wb.Worksheets)
                    {
                        ws.Columns().AdjustToContents(1, 80);
                        ws.SheetView.FreezeRows(1);
                        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    }

                    wb.SaveAs(exportPath);
                }

                // ── Summary dialog ──────────────────────────────────────────────
                var report = new StringBuilder();
                report.AppendLine("COBie FM Handover Manual Generated (.xlsx)");
                report.AppendLine(new string('\u2550', 55));
                report.AppendLine($"  Standard:         COBie V2.4 / BS EN ISO 19650-4:2022");
                report.AppendLine($"  Project:          {projectName}");
                report.AppendLine($"  Total assets:     {allElements.Count}");
                report.AppendLine($"  Tagged complete:  {tagged} ({pct:F1}%)");
                report.AppendLine($"  Systems:          {bySys.Count}");
                report.AppendLine($"  Rooms:            {rooms.Count}");
                report.AppendLine($"  Levels:           {levels.Count}");
                report.AppendLine($"  Asset types:      {typeGroups.Count}");
                report.AppendLine($"  COBie components: {cobieComponentCount}");
                report.AppendLine();
                report.AppendLine("  COBie V2.4 Worksheets (20 standard + 2 STING):");
                report.AppendLine("    1.  Instruction     11. Connection");
                report.AppendLine("    2.  Contact         12. Spare");
                report.AppendLine("    3.  Facility        13. Resource");
                report.AppendLine("    4.  Floor           14. Job");
                report.AppendLine("    5.  Space           15. Impact");
                report.AppendLine("    6.  Zone            16. Document");
                report.AppendLine("    7.  Type            17. Attribute");
                report.AppendLine("    8.  Component       18. Coordinate");
                report.AppendLine("    9.  System          19. Issue");
                report.AppendLine("    10. Assembly        20. PickLists");
                report.AppendLine("    + Asset Register    + Maintenance Schedule");
                report.AppendLine();
                report.AppendLine($"  File: {exportPath}");

                var td = new TaskDialog("STING Tools - COBie FM Handover");
                td.MainInstruction = "COBie FM Handover Manual Generated";
                td.MainContent = report.ToString();
                td.CommonButtons = TaskDialogCommonButtons.Ok;
                td.DefaultButton = TaskDialogResult.Ok;
                td.Show();
                StingLog.Info($"COBie Handover: {allElements.Count} assets, {cobieComponentCount} components, " +
                    $"{bySys.Count} systems, {rooms.Count} rooms, {levels.Count} levels → {exportPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HandoverManualCommand crashed", ex);
                TaskDialog.Show("STING Tools", $"COBie FM Handover Manual failed:\n{ex.Message}");
                return Result.Failed;
            }
        }

        // ── Helper methods ──────────────────────────────────────────────────

        /// <summary>Write a row of values to a worksheet.</summary>
        private static void WriteRow(IXLWorksheet ws, int row, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                ws.Cell(row, i + 1).Value = values[i] ?? "";
        }

        /// <summary>Style header row with bold, background colour, and borders.</summary>
        private static void StyleHeader(IXLWorksheet ws, int row, int colCount)
        {
            var range = ws.Range(row, 1, row, colCount);
            range.Style.Font.Bold = true;
            range.Style.Font.FontSize = 10;
            range.Style.Font.FontName = "Arial";
            range.Style.Fill.BackgroundColor = XLColor.FromArgb(79, 129, 189); // COBie blue
            range.Style.Font.FontColor = XLColor.White;
            range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        /// <summary>Get human-readable system description from code.</summary>
        private static string GetSystemDescription(string sysCode)
        {
            return sysCode switch
            {
                "HVAC" => "Heating, Ventilation & Air Conditioning",
                "DCW" => "Domestic Cold Water",
                "DHW" => "Domestic Hot Water",
                "HWS" => "Heating Hot Water System",
                "SAN" => "Sanitary / Drainage",
                "RWD" => "Rainwater Drainage",
                "GAS" => "Gas Supply",
                "FP" => "Fire Protection & Suppression",
                "LV" => "Low Voltage Systems",
                "FLS" => "Fire Life Safety",
                "COM" => "Communications & Data",
                "ICT" => "Information & Communications Technology",
                "NCL" => "Nurse Call / Patient Systems",
                "SEC" => "Security & Access Control",
                "ARC" => "Architectural Elements",
                "STR" => "Structural Elements",
                "GEN" => "General / Miscellaneous",
                _ => sysCode
            };
        }

        /// <summary>Get maintenance schedule recommendation based on system and category (BS 8210 / SFG20).</summary>
        private static (string MaintType, string Frequency, string Priority, string Notes)
            GetMaintenanceSchedule(string sysCode, string category)
        {
            return sysCode switch
            {
                "HVAC" => ("Preventive", "Quarterly", "High",
                    "Filter replacement, belt inspection, refrigerant check per SFG20 schedule 1-010"),
                "DCW" or "DHW" => ("Preventive", "6-Monthly", "High",
                    "Legionella risk assessment (L8/HSG274), TMV testing, flush dead legs"),
                "HWS" => ("Preventive", "Quarterly", "High",
                    "Boiler service, pump inspection, valve check per SFG20 schedule 3-030"),
                "SAN" or "RWD" => ("Preventive", "Annually", "Medium",
                    "Drain survey, gully clean, interceptor maintenance per SFG20 schedule 4-010"),
                "GAS" => ("Statutory", "Annually", "Critical",
                    "Gas safety inspection (Gas Safety Regulations 1998), leak detection, emergency valve test"),
                "FP" => ("Statutory", "6-Monthly", "Critical",
                    "Sprinkler flow test (BS EN 12845), extinguisher service (BS 5306), hydrant test"),
                "FLS" => ("Statutory", "Quarterly", "Critical",
                    "Fire alarm test (BS 5839), emergency lighting (BS 5266), smoke detector service"),
                "LV" or "COM" or "ICT" => ("Preventive", "Annually", "Medium",
                    "Thermal imaging, connection check, UPS battery test per SFG20 schedule 6-010"),
                "SEC" => ("Preventive", "Quarterly", "Medium",
                    "Access control audit, CCTV review, intruder alarm test (BS EN 50131)"),
                "NCL" => ("Preventive", "6-Monthly", "High",
                    "System function test, battery replacement, handset check per HTM 08-03"),
                "ARC" => ("Reactive", "As-needed", "Low",
                    "Fabric inspection, decorative maintenance per BS 8210 Section 4"),
                "STR" => ("Condition", "5-Yearly", "Medium",
                    "Structural survey, movement monitoring per BS 8210 Section 3"),
                _ => ("Planned", "Annually", "Medium",
                    "General inspection and maintenance per BS 8210")
            };
        }
    }
}
