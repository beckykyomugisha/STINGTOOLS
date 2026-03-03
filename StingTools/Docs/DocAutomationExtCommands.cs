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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
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
            tplDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto-assign STING templates (recommended)",
                "7-layer intelligent matching: discipline + view type + level");
            tplDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "No templates (assign later)",
                "Create views without template assignment");
            tplDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

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
                    View active = doc.ActiveView;
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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
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

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Documentation Package"))
            {
                tg.Start();

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

                tg.Assimilate();
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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

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
                            if (marker.HasElevation(i)) continue;
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
                    var rooms = uidoc.Selection.GetElementIds()
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
                                if (marker.HasElevation(i)) continue;
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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
            Document doc = commandData.Application.ActiveUIDocument.Document;

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
            var doc = commandData.Application.ActiveUIDocument.Document;

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
}
