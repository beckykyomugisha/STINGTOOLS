using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  SHEET MANAGER COMMANDS
    //  ISO 19650-compliant sheet and viewport management with automated layout.
    //
    //  Commands:
    //    1. SheetManagerCommand          — Open full Sheet Manager WPF dialog
    //    2. AutoLayoutCommand            — Auto-arrange viewports on active sheet
    //    3. CloneSheetCommand            — Clone sheet with title block + viewports
    //    4. PlaceUnplacedViewsCommand    — Auto-place unplaced views on new sheets
    //    5. OptimalScaleCommand          — Calculate and apply optimal scale
    //    6. SheetAuditCommand            — Audit sheet/viewport statistics
    //    7. BatchArrangeCommand          — Arrange viewports across all sheets
    //    8. MoveViewportCommand          — Move viewport to different sheet
    // ════════════════════════════════════════════════════════════════════════════

    // ── 1. Sheet Manager Dialog ──────────────────────────────────────────────

    /// <summary>
    /// Opens the full STING Sheet Manager as a modeless floating WPF dialog
    /// with dual-panel sheet/viewport browser and context-sensitive actions.
    /// Double-click opens views/sheets in Revit. Drag-drop places views on sheets.
    /// All operations execute live via IExternalEventHandler.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            // If already open, just bring to front
            if (SheetManagerDialog.IsOpen)
            {
                SheetManagerDialog.RefreshData();
                return Result.Succeeded;
            }

            // Build data for dual-panel dialog
            var sheetNodes = BuildSheetNodes(doc);
            var unplacedViews = BuildUnplacedViewNodes(doc);
            var allViews = BuildAllViewNodes(doc);

            // Show modeless dialog with live execution callback
            SheetManagerDialog.ShowModeless(
                sheetNodes, unplacedViews, allViews,
                executeCallback: (operation, options) =>
                {
                    // This callback runs on the WPF thread —
                    // for Revit API calls, dispatch via StingDockPanel.DispatchCommand
                    DispatchLiveOperation(doc, uidoc, operation, options);
                },
                refreshSheets: () => BuildSheetNodes(doc),
                refreshUnplaced: () => BuildUnplacedViewNodes(doc),
                refreshAllViews: () => BuildAllViewNodes(doc)
            );

            return Result.Succeeded;
        }

        /// <summary>
        /// Dispatch a live operation from the modeless Sheet Manager.
        /// Operations that need the Revit API thread go via StingDockPanel.DispatchCommand.
        /// Simple operations that don't need Revit API (like ActivateView) run directly
        /// since UIDocument.RequestViewChange is safe from any thread.
        /// </summary>
        private void DispatchLiveOperation(Document doc, UIDocument uidoc,
            string operation, Dictionary<string, object> options)
        {
            // ── ActivateView — open the view/sheet in Revit ──────────
            if (operation == "ActivateView")
            {
                var viewId = options.ContainsKey("ViewTag") ? options["ViewTag"] as ElementId : null;
                if (viewId == null) return;
                var view = doc.GetElement(viewId) as View;
                if (view == null) return;

                try
                {
                    uidoc.RequestViewChange(view);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ActivateView failed: {ex.Message}");
                }
                return;
            }

            // ── Operations that need Revit API thread ────────────────
            // Pack options into ExtraParams for the command handler
            foreach (var kv in options)
                StingCommandHandler.SetExtraParam($"SM_{kv.Key}", kv.Value?.ToString() ?? "");

            // Map operation names to dispatch tags
            string dispatchTag = operation switch
            {
                "PlaceViewOnSheet" => "SM_PlaceViewOnSheet",
                "PlaceOnNewSheet" => "SM_PlaceOnNewSheet",
                "MoveViewportToSheet" => "SM_MoveViewportToSheet",
                "RemoveViewport" => "SM_RemoveViewport",
                "CreateSheet" => "SM_CreateSheet",
                "CloneSheet" => "SM_CloneSheet",
                "ArrangeOnSheet" => "SM_ArrangeOnSheet",
                "AutoScaleSheet" => "SM_AutoScaleSheet",
                "AutoLayoutMode" => "SM_AutoLayoutMode",
                "AutoPlaceUnplaced" => "SM_AutoPlaceUnplaced",
                "DuplicateView" => "SM_DuplicateView",
                "DeleteView" => "SM_DeleteView",
                "BatchArrange" => "SM_BatchArrange",
                "RenumberDisc" => "SM_RenumberDisc",
                "SwapTitleBlock" => "SM_SwapTitleBlock",
                "EnforceISONaming" => "SM_EnforceISONaming",
                "RevertISONaming" => "SM_RevertISONaming",
                "SM_GetViewTemplates" => "SM_GetViewTemplates",
                "SM_AssignSpecificTemplate" => "SM_AssignSpecificTemplate",
                "SM_RemoveViewTemplate" => "SM_RemoveViewTemplate",
                "SM_SelectElementIds" => "SM_SelectElementIds",
                _ => operation // Fall through to standard dispatch tags
            };

            StingDockPanel.DispatchCommand(dispatchTag);
        }

        internal Result DispatchOperation(Document doc, StingCommandContext ctx,
            SheetManagerResult result)
        {
            switch (result.Operation)
            {
                case "AutoLayout":
                case "ArrangeOnSheet":
                    return ArrangeActiveSheet(doc, ctx);

                case "AutoLayoutMode":
                    return ArrangeWithMode(doc, ctx, result);

                case "CloneSheet":
                    return CloneSelectedSheet(doc, result);

                case "CreateSheet":
                    return CreateNewSheet(doc);

                case "AutoPlaceUnplaced":
                case "PlaceByDiscipline":
                    return PlaceUnplacedViews(doc);

                case "AutoScaleSheet":
                    return AutoScaleViewports(doc, ctx);

                case "PlaceViewOnSheet":
                    return PlaceViewOnSheet(doc, result);

                case "PlaceOnNewSheet":
                    return PlaceViewOnNewSheet(doc, result);

                case "MoveViewportToSheet":
                    return MoveViewportToSheet(doc, result);

                case "RemoveViewport":
                    return RemoveSelectedViewport(doc, result);

                case "DuplicateView":
                    return DuplicateSelectedView(doc, result);

                case "DeleteView":
                    return DeleteSelectedView(doc, result);

                case "BatchArrange":
                    return BatchArrangeAll(doc);

                case "SheetAudit":
                    return RunSheetAudit(doc);

                case "RenumberDisc":
                    return RenumberSheets(doc);

                case "SwapTitleBlock":
                    return SwapTitleBlockOnSheet(doc, result);

                case "EnforceISONaming":
                    return EnforceISONaming(doc, result);

                case "RevertISONaming":
                    return RevertISONaming(doc, result);

                case "ActivateView":
                    if (result.Options.TryGetValue("ViewTag", out object avTag) && avTag is ElementId avId)
                    {
                        var view = doc.GetElement(avId) as View;
                        if (view != null && ctx.UIDoc != null)
                            ctx.UIDoc.ActiveView = view;
                    }
                    return Result.Succeeded;

                default:
                    StingLog.Info($"Sheet Manager operation '{result.Operation}' — no handler.");
                    return Result.Succeeded;
            }
        }

        private Result ArrangeActiveSheet(Document doc, StingCommandContext ctx)
        {
            var sheet = ctx.ActiveView as ViewSheet;
            if (sheet == null)
            {
                TaskDialog.Show("STING", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "STING Arrange Viewports"))
            {
                tx.Start();
                int moved = SheetManagerEngine.ArrangeViewportsOnSheet(doc, sheet);
                tx.Commit();
                TaskDialog.Show("Sheet Manager", $"Rearranged {moved} viewports on sheet '{sheet.SheetNumber}'.");
            }
            return Result.Succeeded;
        }

        private Result CloneSelectedSheet(Document doc, SheetManagerResult result)
        {
            var sheetId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;
            ViewSheet sheet = null;
            if (sheetId != null) sheet = doc.GetElement(sheetId) as ViewSheet;

            if (sheet == null)
            {
                TaskDialog.Show("STING", "No sheet selected to clone.");
                return Result.Succeeded;
            }

            string nextNum = SheetManagerEngine.GetNextSheetNumber(doc,
                SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber));

            var config = new SheetCloneConfig
            {
                NewSheetNumber = nextNum,
                NewSheetName = sheet.Name + " (Copy)",
                CopyViewports = true,
                CopyAnnotations = true,
                CopySchedules = true,
                DuplicateViews = true,
                DuplicateMode = ViewDuplicateOption.WithDetailing
            };

            using (var tx = new Transaction(doc, "STING Clone Sheet"))
            {
                tx.Start();
                var cloned = SheetManagerEngine.CloneSheet(doc, sheet, config);
                tx.Commit();

                if (cloned != null)
                    TaskDialog.Show("Sheet Manager",
                        $"Cloned sheet '{sheet.SheetNumber}' \u2192 '{cloned.SheetNumber} - {cloned.Name}'");
                else
                    TaskDialog.Show("Sheet Manager", "Failed to clone sheet.");
            }
            return Result.Succeeded;
        }

        private Result ArrangeWithMode(Document doc, StingCommandContext ctx, SheetManagerResult result)
        {
            string mode = result.Options.ContainsKey("LayoutMode") ? result.Options["LayoutMode"]?.ToString() : "Default";
            var sheetId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;

            ViewSheet sheet = null;
            if (sheetId != null) sheet = doc.GetElement(sheetId) as ViewSheet;
            if (sheet == null) sheet = ctx.ActiveView as ViewSheet;

            if (sheet == null)
            {
                TaskDialog.Show("Sheet Manager", "Select or navigate to a sheet first.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, $"STING Auto Layout ({mode})"))
            {
                tx.Start();
                int moved = SheetManagerEngine.ArrangeViewportsOnSheet(doc, sheet);
                tx.Commit();
                TaskDialog.Show("Sheet Manager",
                    $"Layout '{mode}': arranged {moved} viewports on '{sheet.SheetNumber}'.");
            }
            return Result.Succeeded;
        }

        private Result CreateNewSheet(Document doc)
        {
            // ── Collect title block types ──
            var tbTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .ToList();

            if (tbTypes.Count == 0)
            {
                TaskDialog.Show("Sheet Manager", "No title block families loaded in the project.");
                return Result.Succeeded;
            }

            var tbNames = tbTypes.Select(t => $"{t.FamilyName}: {t.Name}").OrderBy(n => n).ToList();
            string defaultTb = tbNames.Count > 0 ? tbNames[0] : "";

            // ── Collect scope boxes ──
            var scopeBoxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Select(e => e.Name)
                .OrderBy(n => n)
                .ToList();

            // ── Collect unplaced views (dependent views candidates) ──
            var unplacedViews = SheetManagerEngine.GetUnplacedViews(doc)
                .Select(v => v.Name)
                .OrderBy(n => n)
                .ToList();

            // ── Collect view templates ──
            var viewTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name)
                .OrderBy(n => n)
                .ToList();

            // ── Collect custom shared parameters bound to ViewSheet category ──
            var sheetParamNames = new List<string>();
            try
            {
                // Get shared params from an existing sheet, or from binding map
                var sampleSheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .FirstOrDefault() as ViewSheet;

                if (sampleSheet != null)
                {
                    // Iterate all parameters and find shared/project params (non built-in, writable text)
                    foreach (Parameter p in sampleSheet.Parameters)
                    {
                        if (p.IsShared && !p.IsReadOnly
                            && p.StorageType == StorageType.String
                            && p.Definition != null
                            && !string.IsNullOrEmpty(p.Definition.Name))
                        {
                            string pName = p.Definition.Name;
                            // Skip STING internal params and standard Revit params
                            if (!pName.StartsWith("STING_") && !pName.StartsWith("ASS_TAG_")
                                && pName != "Sheet Number" && pName != "Sheet Name")
                            {
                                sheetParamNames.Add(pName);
                            }
                        }
                    }
                }
                sheetParamNames.Sort();
            }
            catch (Exception ex) { StingLog.Warn($"Failed to collect sheet parameters: {ex.Message}"); }

            // Suggest next sheet number
            string suggestedNum = SheetManagerEngine.GetNextSheetNumber(doc, "A");

            // ── Show New Sheet dialog ──
            var rows = NewSheetDialog.Show(
                tbNames, scopeBoxes, unplacedViews, viewTemplates,
                out bool autoPlaceDependentViews,
                defaultTb, "A", suggestedNum, sheetParamNames);

            if (rows == null || rows.Count == 0)
                return Result.Succeeded; // Cancelled

            // ── Create sheets from dialog rows ──
            int created = 0;
            int errors = 0;

            // Build title block lookup (display name → FamilySymbol)
            var tbLookup = new Dictionary<string, FamilySymbol>();
            foreach (var tb in tbTypes)
            {
                string key = $"{tb.FamilyName}: {tb.Name}";
                if (!tbLookup.ContainsKey(key)) tbLookup[key] = tb;
            }

            // Build scope box lookup
            var scopeBoxLookup = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
            foreach (var sb in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType())
            {
                if (!scopeBoxLookup.ContainsKey(sb.Name)) scopeBoxLookup[sb.Name] = sb;
            }

            // Build unplaced view lookup
            var viewLookup = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in SheetManagerEngine.GetUnplacedViews(doc))
            {
                if (!viewLookup.ContainsKey(v.Name)) viewLookup[v.Name] = v;
            }

            using (var tx = new Transaction(doc, "STING Create Sheets"))
            {
                tx.Start();
                foreach (var row in rows)
                {
                    try
                    {
                        // Resolve title block
                        FamilySymbol tbSym = tbLookup.ContainsKey(row.TitleBlock)
                            ? tbLookup[row.TitleBlock]
                            : tbTypes.First();

                        // Activate if needed
                        if (!tbSym.IsActive) tbSym.Activate();

                        // Create sheet
                        var sheet = ViewSheet.Create(doc, tbSym.Id);
                        try { sheet.SheetNumber = row.SheetNumber; }
                        catch (Exception ex) { StingLog.Warn($"Sheet number conflict '{row.SheetNumber}': {ex.Message}"); }

                        try { sheet.Name = row.SheetName; }
                        catch (Exception ex) { StingLog.Warn($"Sheet name conflict '{row.SheetName}': {ex.Message}"); }

                        // Write custom shared parameter values
                        if (row.CustomParams != null && row.CustomParams.Count > 0)
                        {
                            foreach (var kvp in row.CustomParams)
                            {
                                if (string.IsNullOrEmpty(kvp.Value)) continue;
                                try
                                {
                                    var p = sheet.LookupParameter(kvp.Key);
                                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                                        p.Set(kvp.Value);
                                }
                                catch (Exception ex) { StingLog.Warn($"Failed to set param '{kvp.Key}': {ex.Message}"); }
                            }
                        }

                        // Assign scope box if specified
                        if (!string.IsNullOrEmpty(row.ScopeBox) && row.ScopeBox != "(None)"
                            && scopeBoxLookup.TryGetValue(row.ScopeBox, out Element sb))
                        {
                            try
                            {
                                var scopeParam = sheet.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                                if (scopeParam != null && !scopeParam.IsReadOnly)
                                    scopeParam.Set(sb.Id);
                            }
                            catch (Exception ex) { StingLog.Warn($"Scope box assignment failed: {ex.Message}"); }
                        }

                        // Place dependent views if requested
                        if (autoPlaceDependentViews && !string.IsNullOrEmpty(row.DependentViews))
                        {
                            var viewNames = row.DependentViews.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n));

                            double x = 0.5; // Start placement offset (feet)
                            foreach (var vName in viewNames)
                            {
                                if (viewLookup.TryGetValue(vName, out View depView))
                                {
                                    try
                                    {
                                        Viewport.Create(doc, sheet.Id, depView.Id, new XYZ(x, 0.5, 0));
                                        x += 0.8; // Offset each viewport
                                        // Remove from lookup so it can't be placed twice
                                        viewLookup.Remove(vName);
                                    }
                                    catch (Exception ex) { StingLog.Warn($"Failed to place view '{vName}': {ex.Message}"); }
                                }
                            }
                        }

                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Failed to create sheet '{row.SheetNumber}': {ex.Message}");
                        errors++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Sheet Manager",
                $"Created {created} sheet{(created == 1 ? "" : "s")}" +
                (errors > 0 ? $" ({errors} error{(errors == 1 ? "" : "s")})" : "") + ".");

            return Result.Succeeded;
        }

        private Result PlaceUnplacedViews(Document doc)
        {
            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            if (unplaced.Count == 0)
            {
                TaskDialog.Show("Sheet Manager", "All views are already placed on sheets.");
                return Result.Succeeded;
            }

            // Find title block type
            var tbType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault();

            if (tbType == null)
            {
                TaskDialog.Show("Sheet Manager", "No title block families loaded.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "STING Auto-Place Views"))
            {
                tx.Start();
                var (sheetsCreated, viewsPlaced) =
                    SheetManagerEngine.BatchCreateAndPlace(doc, unplaced, tbType.Id, autoScale: true);
                tx.Commit();
                TaskDialog.Show("Sheet Manager",
                    $"Created {sheetsCreated} sheets and placed {viewsPlaced} views.\n" +
                    $"{unplaced.Count - viewsPlaced} views could not be placed (overflow).");
            }
            return Result.Succeeded;
        }

        private Result AutoScaleViewports(Document doc, StingCommandContext ctx)
        {
            var sheet = ctx.ActiveView as ViewSheet;
            if (sheet == null)
            {
                TaskDialog.Show("STING", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
            int changed = 0;

            using (var tx = new Transaction(doc, "STING Auto-Scale Viewports"))
            {
                tx.Start();
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    int optimal = SheetManagerEngine.CalculateOptimalScale(view, zone.Width, zone.Height);
                    if (optimal > 0 && optimal != view.Scale)
                    {
                        try { view.Scale = optimal; changed++; }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not set scale for '{view.Name}': {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Sheet Manager", $"Updated scale on {changed} viewports.");
            return Result.Succeeded;
        }


        // ── Operation handlers ────────────────────────────────────────────

        private Result PlaceViewOnSheet(Document doc, SheetManagerResult result)
        {
            if (!result.Options.ContainsKey("ViewTag") || !result.Options.ContainsKey("SheetTag"))
                return Result.Succeeded;

            var viewId = result.Options["ViewTag"] as ElementId;
            var sheetId = result.Options["SheetTag"] as ElementId;
            if (viewId == null || sheetId == null) return Result.Succeeded;

            var sheet = doc.GetElement(sheetId) as ViewSheet;
            var view = doc.GetElement(viewId) as View;
            if (sheet == null || view == null)
            {
                TaskDialog.Show("Sheet Manager", "Could not resolve view or sheet.");
                return Result.Succeeded;
            }

            // Check if view can be placed on sheet (already placed, template, etc.)
            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
            {
                TaskDialog.Show("Sheet Manager",
                    $"Cannot place '{view.Name}' on '{sheet.SheetNumber}'.\n\n" +
                    "The view may already be placed on another sheet, or it may be a " +
                    "view type that cannot be placed (template, schedule, etc.).\n\n" +
                    "Only legends can be placed on multiple sheets.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "STING Place View on Sheet"))
            {
                tx.Start();
                try
                {
                    var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
                    Viewport.Create(doc, sheet.Id, view.Id, zone.Center);
                    tx.Commit();
                    TaskDialog.Show("Sheet Manager",
                        $"Placed '{view.Name}' on sheet '{sheet.SheetNumber}'.");
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    StingLog.Warn($"PlaceViewOnSheet failed: {ex.Message}");
                    TaskDialog.Show("Sheet Manager", $"Cannot place view: {ex.Message}");
                }
            }
            return Result.Succeeded;
        }

        private Result PlaceViewOnNewSheet(Document doc, SheetManagerResult result)
        {
            var viewId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;
            if (viewId == null) return Result.Succeeded;
            var view = doc.GetElement(viewId) as View;
            if (view == null) return Result.Succeeded;

            var tbType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault();
            if (tbType == null) { TaskDialog.Show("Sheet Manager", "No title blocks loaded."); return Result.Succeeded; }

            using (var tx = new Transaction(doc, "STING Place on New Sheet"))
            {
                tx.Start();
                try
                {
                    var sheet = ViewSheet.Create(doc, tbType.Id);

                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                    {
                        tx.RollBack();
                        TaskDialog.Show("Sheet Manager",
                            $"'{view.Name}' cannot be placed on a sheet (already placed or incompatible type).");
                        return Result.Succeeded;
                    }

                    var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
                    Viewport.Create(doc, sheet.Id, view.Id, zone.Center);
                    tx.Commit();
                    TaskDialog.Show("Sheet Manager", $"Created '{sheet.SheetNumber}' and placed '{view.Name}'.");
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    TaskDialog.Show("Sheet Manager", $"Error: {ex.Message}");
                }
            }
            return Result.Succeeded;
        }

        private Result MoveViewportToSheet(Document doc, SheetManagerResult result)
        {
            var vpId = result.Options.ContainsKey("ViewportTag") ? result.Options["ViewportTag"] as ElementId : null;
            var targetSheetId = result.Options.ContainsKey("TargetSheetTag") ? result.Options["TargetSheetTag"] as ElementId : null;
            if (vpId == null || targetSheetId == null) return Result.Succeeded;

            var vp = doc.GetElement(vpId) as Viewport;
            var targetSheet = doc.GetElement(targetSheetId) as ViewSheet;
            if (vp == null || targetSheet == null) return Result.Succeeded;

            var viewName = (doc.GetElement(vp.ViewId) as View)?.Name ?? "(unknown)";

            using (var tx = new Transaction(doc, "STING Move Viewport"))
            {
                tx.Start();
                try
                {
                    var newVp = SheetManagerEngine.MoveViewportToSheet(doc, vp, targetSheet);
                    if (newVp != null)
                    {
                        tx.Commit();
                        string targetNum = result.Options.ContainsKey("TargetSheetNumber")
                            ? result.Options["TargetSheetNumber"]?.ToString() : targetSheet.SheetNumber;
                        TaskDialog.Show("Sheet Manager", $"Moved '{viewName}' to sheet '{targetNum}'.");
                    }
                    else
                    {
                        tx.RollBack();
                        TaskDialog.Show("Sheet Manager",
                            $"Failed to move '{viewName}'. The view may already be on the target sheet or cannot be placed there.");
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    TaskDialog.Show("Sheet Manager", $"Cannot move viewport: {ex.Message}");
                }
            }
            return Result.Succeeded;
        }

        private Result RemoveSelectedViewport(Document doc, SheetManagerResult result)
        {
            var vpId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;
            if (vpId == null) return Result.Succeeded;

            using (var tx = new Transaction(doc, "STING Remove Viewport"))
            {
                tx.Start();
                doc.Delete(vpId);
                tx.Commit();
            }
            TaskDialog.Show("Sheet Manager", "Viewport removed from sheet.");
            return Result.Succeeded;
        }

        private Result DuplicateSelectedView(Document doc, SheetManagerResult result)
        {
            var viewId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;
            if (viewId == null) return Result.Succeeded;
            var view = doc.GetElement(viewId) as View;
            if (view == null) return Result.Succeeded;

            using (var tx = new Transaction(doc, "STING Duplicate View"))
            {
                tx.Start();
                var newId = view.Duplicate(ViewDuplicateOption.WithDetailing);
                tx.Commit();
                var newView = doc.GetElement(newId) as View;
                TaskDialog.Show("Sheet Manager", $"Duplicated: '{newView?.Name ?? "view"}'");
            }
            return Result.Succeeded;
        }

        private Result DeleteSelectedView(Document doc, SheetManagerResult result)
        {
            var viewId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;
            if (viewId == null) return Result.Succeeded;
            var view = doc.GetElement(viewId) as View;
            if (view == null) return Result.Succeeded;

            string viewName = view.Name;
            using (var tx = new Transaction(doc, "STING Delete View"))
            {
                tx.Start();
                doc.Delete(viewId);
                tx.Commit();
            }
            TaskDialog.Show("Sheet Manager", $"Deleted view: '{viewName}'");
            return Result.Succeeded;
        }

        private Result BatchArrangeAll(Document doc)
        {
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.GetAllViewports().Count > 0).ToList();

            int total = 0;
            using (var tx = new Transaction(doc, "STING Batch Arrange"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    try { total += SheetManagerEngine.ArrangeViewportsOnSheet(doc, sheet); }
                    catch (Exception ex) { StingLog.Warn($"Arrange error on {sheet.SheetNumber}: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Sheet Manager", $"Arranged {total} viewports across {sheets.Count} sheets.");
            return Result.Succeeded;
        }

        private Result RunSheetAudit(Document doc)
        {
            var auditSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder).ToList();
            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            int totalVps = auditSheets.Sum(s => s.GetAllViewports().Count);
            int empty = auditSheets.Count(s => s.GetAllViewports().Count == 0);

            TaskDialog.Show("Sheet Audit",
                $"Sheets: {auditSheets.Count}\nViewports: {totalVps}\n" +
                $"Empty sheets: {empty}\nUnplaced views: {unplaced.Count}");
            return Result.Succeeded;
        }

        private Result RenumberSheets(Document doc)
        {
            // Delegate to BatchRenumberSheetsCommand
            try
            {
                var cmd = new BatchRenumberSheetsCommand();
                string msg = null;
                return cmd.Execute(null, ref msg, null);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"RenumberSheets failed: {ex.Message}");
                TaskDialog.Show("Sheet Manager", "Renumber requires navigating to a sheet first.");
                return Result.Succeeded;
            }
        }

        private Result SwapTitleBlockOnSheet(Document doc, SheetManagerResult result)
        {
            var sheetId = result.Options.ContainsKey("SelectedTag") ? result.Options["SelectedTag"] as ElementId : null;
            ViewSheet sheet = null;
            if (sheetId != null) sheet = doc.GetElement(sheetId) as ViewSheet;

            // Fallback to active view if no sheet selected
            if (sheet == null && doc.ActiveView is ViewSheet activeSheet)
                sheet = activeSheet;

            if (sheet == null)
            {
                TaskDialog.Show("STING", "Navigate to a sheet view or select a sheet in the Sheet Manager.");
                return Result.Succeeded;
            }

            // Collect all title block types in the project
            var tbTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderBy(fs => $"{fs.FamilyName}: {fs.Name}")
                .ToList();

            if (tbTypes.Count == 0)
            {
                TaskDialog.Show("STING", "No title block families loaded in project.");
                return Result.Succeeded;
            }

            // Get current title block on the sheet
            var currentTb = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault() as FamilyInstance;

            string currentTypeName = currentTb?.Symbol != null
                ? $"{currentTb.Symbol.FamilyName}: {currentTb.Symbol.Name}" : "(none)";

            // Build picker list
            var items = tbTypes.Select(t => $"{t.FamilyName}: {t.Name}").ToList();
            var pickedItem = StingListPicker.Show(
                "Swap Title Block",
                $"Current: {currentTypeName}\nSelect new title block type:",
                items);
            var picked = string.IsNullOrEmpty(pickedItem) ? null : new List<string> { pickedItem };

            if (picked == null || picked.Count == 0)
                return Result.Succeeded;

            int idx = items.IndexOf(picked[0]);
            if (idx < 0 || idx >= tbTypes.Count) return Result.Succeeded;

            var newType = tbTypes[idx];

            if (currentTb != null)
            {
                using (var tx = new Transaction(doc, "STING Swap Title Block"))
                {
                    tx.Start();
                    currentTb.Symbol = newType;
                    tx.Commit();
                }
                TaskDialog.Show("Sheet Manager",
                    $"Swapped title block on '{sheet.SheetNumber}'\n" +
                    $"{currentTypeName} \u2192 {newType.FamilyName}: {newType.Name}");
            }
            else
            {
                // No title block instance — place one
                using (var tx = new Transaction(doc, "STING Place Title Block"))
                {
                    tx.Start();
                    if (!newType.IsActive) newType.Activate();
                    doc.Create.NewFamilyInstance(XYZ.Zero, newType, sheet as Element,
                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    tx.Commit();
                }
                TaskDialog.Show("Sheet Manager",
                    $"Placed title block '{newType.FamilyName}: {newType.Name}' on '{sheet.SheetNumber}'");
            }

            return Result.Succeeded;
        }

        // ── ISO Naming Backup Storage ───────────────────────────────────
        // Stores original sheet names before ISO rename for revert capability.
        // Key: ElementId.Value, Value: (OriginalNumber, OriginalName)
        private static Dictionary<long, (string Number, string Name)> _isoBackup;

        /// <summary>
        /// Get the ISO naming backup sidecar file path for the current project.
        /// </summary>
        private static string GetIsoBackupPath(Document doc)
        {
            if (string.IsNullOrEmpty(doc.PathName)) return null;
            return System.IO.Path.ChangeExtension(doc.PathName, ".sting_sheet_backup.json");
        }

        /// <summary>
        /// Save the ISO naming backup to sidecar file alongside the .rvt.
        /// </summary>
        private static void SaveIsoBackup(Document doc)
        {
            if (_isoBackup == null || _isoBackup.Count == 0) return;
            string path = GetIsoBackupPath(doc);
            if (path == null) return;
            try
            {
                var entries = _isoBackup.Select(kv =>
                    $"    \"{kv.Key}\": {{\"Number\": \"{EscapeJson(kv.Value.Number)}\", \"Name\": \"{EscapeJson(kv.Value.Name)}\"}}");
                string json = "{\n" + string.Join(",\n", entries) + "\n}";
                System.IO.File.WriteAllText(path, json);
                StingLog.Info($"ISO backup saved: {_isoBackup.Count} entries → {path}");
            }
            catch (Exception ex) { StingLog.Warn($"Save ISO backup: {ex.Message}"); }
        }

        private static string EscapeJson(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Load the ISO naming backup from sidecar file.
        /// </summary>
        private static void LoadIsoBackup(Document doc)
        {
            if (_isoBackup != null) return; // already loaded
            _isoBackup = new Dictionary<long, (string, string)>();
            string path = GetIsoBackupPath(doc);
            if (path == null || !System.IO.File.Exists(path)) return;
            try
            {
                string json = System.IO.File.ReadAllText(path);
                // Simple JSON parse — extract key/Number/Name pairs
                var matches = Regex.Matches(json, @"""(\d+)""\s*:\s*\{\s*""Number""\s*:\s*""([^""]*)""\s*,\s*""Name""\s*:\s*""([^""]*)""");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    if (long.TryParse(m.Groups[1].Value, out long id))
                        _isoBackup[id] = (m.Groups[2].Value, m.Groups[3].Value);
                }
                StingLog.Info($"ISO backup loaded: {_isoBackup.Count} entries from {path}");
            }
            catch (Exception ex) { StingLog.Warn($"Load ISO backup: {ex.Message}"); }
        }

        /// <summary>
        /// Revert sheets to their original names stored before ISO enforcement.
        /// </summary>
        private Result RevertISONaming(Document doc, SheetManagerResult result)
        {
            LoadIsoBackup(doc);
            if (_isoBackup == null || _isoBackup.Count == 0)
            {
                TaskDialog.Show("Revert ISO Naming",
                    "No ISO naming backup found.\nOriginal sheet names were not stored or the backup file is missing.");
                return Result.Succeeded;
            }

            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && _isoBackup.ContainsKey(s.Id.Value))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (allSheets.Count == 0)
            {
                TaskDialog.Show("Revert ISO Naming",
                    $"Backup has {_isoBackup.Count} entries but no matching sheets found in current project.");
                return Result.Succeeded;
            }

            // Show preview
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Revert {allSheets.Count} sheet(s) to original names:\n");
            int shown = 0;
            foreach (var sheet in allSheets)
            {
                var orig = _isoBackup[sheet.Id.Value];
                report.AppendLine($"  {sheet.SheetNumber,-30} \u2192 {orig.Number}");
                if (++shown >= 30) { report.AppendLine("  ... (truncated)"); break; }
            }

            var td = new TaskDialog("Revert ISO Naming");
            td.MainInstruction = $"Revert {allSheets.Count} sheet(s) to original names?";
            td.MainContent = report.ToString();
            td.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            if (td.Show() != TaskDialogResult.Yes) return Result.Succeeded;

            int reverted = 0;
            using (var tx = new Transaction(doc, "STING Revert ISO Naming"))
            {
                tx.Start();
                // Pass 1: temporary names to avoid conflicts
                for (int i = 0; i < allSheets.Count; i++)
                {
                    try { allSheets[i].SheetNumber = $"_STING_REVERT_{i:D5}"; }
                    catch (Exception ex) { StingLog.Warn($"ISO revert temp: {ex.Message}"); }
                }
                // Pass 2: restore original numbers
                foreach (var sheet in allSheets)
                {
                    try
                    {
                        var orig = _isoBackup[sheet.Id.Value];
                        sheet.SheetNumber = orig.Number;
                        sheet.Name = orig.Name;
                        reverted++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ISO revert failed for '{sheet.SheetNumber}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            // Clear backup after successful revert
            _isoBackup.Clear();
            string backupPath = GetIsoBackupPath(doc);
            if (backupPath != null && System.IO.File.Exists(backupPath))
            {
                try { System.IO.File.Delete(backupPath); }
                catch (Exception ex) { StingLog.Warn($"Delete ISO backup: {ex.Message}"); }
            }

            TaskDialog.Show("Revert ISO Naming",
                $"Reverted {reverted}/{allSheets.Count} sheets to original names.");
            return Result.Succeeded;
        }

        /// <summary>
        /// Enforce ISO 19650 naming convention on sheets.
        /// Format: PROJECT-ORIGINATOR-VOLUME-LEVEL-TYPE-ROLE-NUMBER
        /// Options: All sheets, selected sheet, non-compliant only.
        /// Stores original names for revert capability.
        /// </summary>
        private Result EnforceISONaming(Document doc, SheetManagerResult result)
        {
            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (allSheets.Count == 0)
            {
                TaskDialog.Show("STING", "No sheets found.");
                return Result.Succeeded;
            }

            // Get project info for ISO naming fields
            var projInfo = doc.ProjectInformation;
            string projectCode = projInfo?.Number ?? "PRJ";
            if (string.IsNullOrWhiteSpace(projectCode)) projectCode = "PRJ";
            if (projectCode.Length > 6) projectCode = projectCode.Substring(0, 6);

            string originator = projInfo?.OrganizationName ?? "STG";
            if (string.IsNullOrWhiteSpace(originator)) originator = "STG";
            if (originator.Length > 3) originator = originator.Substring(0, 3);
            originator = originator.ToUpperInvariant();

            // Mode selection — now includes Revert option
            var td = new TaskDialog("Enforce ISO 19650 Sheet Naming");
            td.MainInstruction = "ISO 19650 Sheet Naming Convention";
            td.MainContent =
                "Format: PROJECT-ORIGINATOR-VOLUME-LEVEL-TYPE-ROLE-NUMBER\n" +
                $"Example: {projectCode}-{originator}-ZZ-L01-DR-A-0001\n\n" +
                $"Project: {projectCode} | Originator: {originator}\n\n" +
                "Select enforcement scope:";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Audit Only — show non-compliant sheets",
                "Preview changes without modifying anything");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Enforce on Non-Compliant Only",
                "Rename only sheets that don't follow ISO format");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Enforce on ALL Sheets — overwrite existing",
                "Force ISO naming on every sheet (destructive — originals stored for revert)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "\u21BA Revert to Original Names",
                "Restore sheet names saved before last ISO enforcement");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Succeeded;

            // Handle Revert option
            if (choice == TaskDialogResult.CommandLink4)
                return RevertISONaming(doc, result);

            bool auditOnly = choice == TaskDialogResult.CommandLink1;
            bool allScope = choice == TaskDialogResult.CommandLink3;

            // Determine scope
            List<ViewSheet> targetSheets = allSheets;

            // Classify each sheet and build rename plan
            var renamePlan = new List<(ViewSheet sheet, string oldNum, string newNum, string reason)>();
            var disciplineCounters = new Dictionary<string, int>();

            // Pre-scan to find highest existing sequence per discipline
            foreach (var s in allSheets)
            {
                string role = DetectDisciplineRole(s.Name, s.SheetNumber);
                string digits = new string(s.SheetNumber.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits))
                {
                    int seq = 0;
                    int.TryParse(digits.Length > 4 ? digits.Substring(digits.Length - 4) : digits, out seq);
                    disciplineCounters.TryGetValue(role, out int cur);
                    if (seq > cur)
                        disciplineCounters[role] = seq;
                }
                if (!disciplineCounters.ContainsKey(role))
                    disciplineCounters[role] = 0;
            }

            foreach (var sheet in targetSheets)
            {
                string oldNum = sheet.SheetNumber;
                bool isCompliant = IsISOCompliant(oldNum, projectCode, originator);

                if (isCompliant && !allScope) continue;

                string role = DetectDisciplineRole(sheet.Name, oldNum);
                string level = DetectLevelCode(sheet.Name, doc, sheet);
                string volume = "ZZ"; // Default volume/zone
                string docType = "DR"; // Drawing

                // Detect doc type from sheet name
                string nameUpper = (sheet.Name ?? "").ToUpperInvariant();
                if (nameUpper.Contains("SCHEDULE")) docType = "SH";
                else if (nameUpper.Contains("DETAIL")) docType = "DR";
                else if (nameUpper.Contains("SECTION")) docType = "DR";
                else if (nameUpper.Contains("SPEC")) docType = "SP";
                else if (nameUpper.Contains("REPORT")) docType = "RP";

                // Increment counter for this discipline
                disciplineCounters.TryGetValue(role, out int dc);
                disciplineCounters[role] = dc + 1;
                int seqNum = disciplineCounters[role];

                string newNum = $"{projectCode}-{originator}-{volume}-{level}-{docType}-{role}-{seqNum:D4}";
                string reason = isCompliant ? "Forced overwrite" : "Non-compliant";

                renamePlan.Add((sheet, oldNum, newNum, reason));
            }

            if (renamePlan.Count == 0)
            {
                TaskDialog.Show("ISO Naming", "All sheets already comply with ISO 19650 naming.");
                return Result.Succeeded;
            }

            // Build report
            var report = new System.Text.StringBuilder();
            report.AppendLine($"ISO 19650 Naming Enforcement — {renamePlan.Count} sheet(s)\n");
            report.AppendLine($"Format: {projectCode}-{originator}-VOLUME-LEVEL-TYPE-ROLE-SEQ\n");
            foreach (var (sheet, oldNum, newNum, reason) in renamePlan)
            {
                report.AppendLine($"  {oldNum,-20} \u2192 {newNum,-30} [{reason}]");
                if (report.Length > 2500) { report.AppendLine("  ... (truncated)"); break; }
            }

            if (auditOnly)
            {
                TaskDialog.Show("ISO Naming Audit", report.ToString());
                return Result.Succeeded;
            }

            // Confirm before applying
            var confirm = new TaskDialog("Confirm ISO Rename");
            confirm.MainInstruction = $"Rename {renamePlan.Count} sheet(s)?";
            confirm.MainContent = report.ToString();
            confirm.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Succeeded;

            // Store original names for revert capability
            LoadIsoBackup(doc);
            foreach (var (sheet, oldNum, newNum, reason) in renamePlan)
            {
                if (!_isoBackup.ContainsKey(sheet.Id.Value))
                    _isoBackup[sheet.Id.Value] = (oldNum, sheet.Name);
            }

            // Apply renames in two-pass to avoid Revit duplicate number conflicts
            int renamed = 0;
            using (var tx = new Transaction(doc, "STING Enforce ISO Naming"))
            {
                tx.Start();

                // Pass 1: set temporary unique numbers to avoid conflicts
                for (int i = 0; i < renamePlan.Count; i++)
                {
                    try
                    {
                        renamePlan[i].sheet.SheetNumber = $"_STING_TEMP_{i:D5}";
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ISO rename temp pass failed for '{renamePlan[i].oldNum}': {ex.Message}");
                    }
                }

                // Pass 2: set final ISO-compliant numbers
                foreach (var (sheet, oldNum, newNum, reason) in renamePlan)
                {
                    try
                    {
                        sheet.SheetNumber = newNum;
                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ISO rename failed for '{oldNum}' \u2192 '{newNum}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            // Save backup for revert capability
            SaveIsoBackup(doc);

            TaskDialog.Show("ISO Naming",
                $"Renamed {renamed}/{renamePlan.Count} sheets to ISO 19650 format.\n\n" +
                $"Format: {projectCode}-{originator}-VOL-LVL-TYPE-ROLE-SEQ\n\n" +
                "Original names stored — use 'Revert to Original Names' to undo.");
            return Result.Succeeded;
        }

        /// <summary>
        /// Check if a sheet number already follows ISO 19650 format.
        /// Minimum: 5+ segments separated by hyphens, starts with project/originator codes.
        /// </summary>
        private static bool IsISOCompliant(string sheetNumber, string projectCode, string originator)
        {
            if (string.IsNullOrEmpty(sheetNumber)) return false;
            var parts = sheetNumber.Split('-');
            if (parts.Length < 5) return false;

            // Check project code and originator match
            if (!parts[0].Equals(projectCode, StringComparison.OrdinalIgnoreCase)) return false;
            if (!parts[1].Equals(originator, StringComparison.OrdinalIgnoreCase)) return false;

            // Last segment should be numeric (sequence number)
            string lastPart = parts[parts.Length - 1];
            if (!lastPart.All(char.IsDigit)) return false;

            return true;
        }

        /// <summary>
        /// Detect discipline/role code from sheet name or number.
        /// Returns ISO 19650 role codes: A, S, M, E, P, C, F, L, G, etc.
        /// </summary>
        private static string DetectDisciplineRole(string sheetName, string sheetNumber)
        {
            string name = (sheetName ?? "").ToUpperInvariant();
            string num = (sheetNumber ?? "").ToUpperInvariant();

            if (name.Contains("MECHANICAL") || name.Contains("HVAC") || name.Contains("HEATING")
                || name.Contains("VENTILAT")) return "M";
            if (name.Contains("ELECTRICAL") || name.Contains("LIGHTING") || name.Contains("POWER")) return "E";
            if (name.Contains("PLUMB") || name.Contains("SANIT") || name.Contains("DRAINAGE")
                || name.Contains("WATER")) return "P";
            if (name.Contains("STRUCT")) return "S";
            if (name.Contains("ARCH") || name.Contains("PLAN") || name.Contains("INTERIOR")
                || name.Contains("FINISH")) return "A";
            if (name.Contains("FIRE") || name.Contains("SPRINKLER")) return "F";
            if (name.Contains("CIVIL") || name.Contains("SITE")) return "C";
            if (name.Contains("LANDSCAPE")) return "L";
            if (name.Contains("COORDINATION") || name.Contains("COMBINED")) return "Z";

            // Fall back to first char of sheet number if it's a known role
            if (num.Length > 0)
            {
                char first = num[0];
                if ("ASMEPCFLGZ".Contains(first)) return first.ToString();
            }

            return "Z"; // General/unclassified
        }

        /// <summary>
        /// Detect level code from sheet name or associated views.
        /// Returns ISO level codes: L00, L01, B01, RF, XX, etc.
        /// </summary>
        private static string DetectLevelCode(string sheetName, Document doc, ViewSheet sheet)
        {
            string name = (sheetName ?? "").ToUpperInvariant();

            // Check for common level patterns in sheet name
            if (name.Contains("GROUND") || name.Contains("GF") || name.Contains("LEVEL 0")
                || name.Contains("L00")) return "L00";
            if (name.Contains("ROOF") || name.Contains("RF")) return "RF";
            if (name.Contains("BASEMENT") || name.Contains("B1") || name.Contains("LOWER")) return "B01";

            // Check for "LEVEL XX" or "LXX" pattern
            var levelMatch = System.Text.RegularExpressions.Regex.Match(name, @"(?:LEVEL\s*|L)(\d{1,2})");
            if (levelMatch.Success)
            {
                int lvl;
                if (int.TryParse(levelMatch.Groups[1].Value, out lvl))
                    return $"L{lvl:D2}";
            }

            // Try to detect from viewports on the sheet
            try
            {
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view?.GenLevel != null)
                    {
                        string lvlName = view.GenLevel.Name.ToUpperInvariant();
                        if (lvlName.Contains("GROUND") || lvlName.Contains("GF")) return "L00";
                        if (lvlName.Contains("ROOF")) return "RF";
                        var m = System.Text.RegularExpressions.Regex.Match(lvlName, @"(\d{1,2})");
                        if (m.Success)
                        {
                            int n;
                            if (int.TryParse(m.Groups[1].Value, out n))
                                return $"L{n:D2}";
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Level detect from viewports: {ex.Message}"); }

            return "XX"; // Unknown level
        }

        // ── Data builders for the dialog ─────────────────────────────────

        internal static List<SheetManagerDialog.SheetNode> BuildSheetNodes(Document doc)
        {
            var nodes = new List<SheetManagerDialog.SheetNode>();

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            foreach (var sheet in sheets)
            {
                var zone = SheetManagerEngine.GetDrawableZone(doc, sheet);
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilyInstance>()
                    .FirstOrDefault();

                string paperSize = "Unknown";
                string tbName = "(none)";
                if (tb != null)
                {
                    tbName = $"{tb.Symbol.FamilyName}: {tb.Name}";
                    var wParam = tb.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                    var hParam = tb.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
                    if (wParam != null && hParam != null)
                        paperSize = PaperSizes.Detect(wParam.AsDouble(), hParam.AsDouble());
                }

                var node = new SheetManagerDialog.SheetNode
                {
                    SheetNumber = sheet.SheetNumber,
                    SheetName = sheet.Name,
                    Discipline = SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber),
                    ViewportCount = sheet.GetAllViewports().Count,
                    PaperSize = paperSize,
                    TitleBlockName = tbName,
                    DrawableArea = $"{zone.Width * 304.8:F0} x {zone.Height * 304.8:F0} mm",
                    Tag = sheet.Id
                };

                // Build viewport children
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;

                    try
                    {
                        var outline = vp.GetBoxOutline();
                        double wMm = (outline.MaximumPoint.X - outline.MinimumPoint.X) * 304.8;
                        double hMm = (outline.MaximumPoint.Y - outline.MinimumPoint.Y) * 304.8;
                        var center = vp.GetBoxCenter();

                        node.Viewports.Add(new SheetManagerDialog.ViewportNode
                        {
                            ViewName = view.Name,
                            Scale = view.Scale.ToString(),
                            PaperSize = $"{wMm:F0} x {hMm:F0} mm",
                            Position = $"({center.X * 304.8:F1}, {center.Y * 304.8:F1})",
                            Tag = vp.Id,
                            ViewTag = view.Id,
                            HostSheetNumber = sheet.SheetNumber
                        });
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Error reading viewport data: {ex.Message}");
                    }
                }

                nodes.Add(node);
            }

            return nodes;
        }

        internal static List<SheetManagerDialog.UnplacedViewNode> BuildUnplacedViewNodes(Document doc)
        {
            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            return unplaced.Select(v => new SheetManagerDialog.UnplacedViewNode
            {
                ViewName = v.Name,
                ViewType = v.ViewType.ToString(),
                Scale = v.Scale.ToString(),
                Tag = v.Id
            }).ToList();
        }

        /// <summary>
        /// Build AllViewNode list for the Views browser (left panel).
        /// Includes all views in the project with placed/unplaced status.
        /// </summary>
        internal static List<SheetManagerDialog.AllViewNode> BuildAllViewNodes(Document doc)
        {
            var nodes = new List<SheetManagerDialog.AllViewNode>();

            // Build placed view lookup: ViewId → SheetNumber
            var placedLookup = new Dictionary<long, string>();
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            foreach (var sheet in sheets)
            {
                foreach (var vpId in sheet.GetAllViewports())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null && !placedLookup.ContainsKey(vp.ViewId.Value))
                        placedLookup[vp.ViewId.Value] = sheet.SheetNumber;
                }
            }

            // Collect all views (excluding sheets, schedule templates, etc.)
            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet
                    && v.ViewType != ViewType.Internal && v.ViewType != ViewType.Undefined
                    && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser)
                .OrderBy(v => v.Name)
                .ToList();

            foreach (var view in allViews)
            {
                bool isPlaced = placedLookup.ContainsKey(view.Id.Value);
                string placedSheet = isPlaced ? placedLookup[view.Id.Value] : null;

                // Detect discipline from view name or template
                string disc = "General";
                string name = view.Name.ToLowerInvariant();
                if (name.Contains("mechanical") || name.Contains("hvac")) disc = "Mechanical";
                else if (name.Contains("electrical") || name.Contains("lighting")) disc = "Electrical";
                else if (name.Contains("plumbing") || name.Contains("hydraulic")) disc = "Plumbing";
                else if (name.Contains("structural")) disc = "Structural";
                else if (name.Contains("architectural") || name.Contains("arch")) disc = "Architectural";
                else if (name.Contains("fire")) disc = "Fire Protection";
                else if (name.Contains("coordination")) disc = "Coordination";

                // Get level name
                string level = null;
                if (view.GenLevel != null)
                    level = view.GenLevel.Name;

                // ── View Template detection ──
                string templateName = null;
                try
                {
                    if (view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId)
                    {
                        var templateView = doc.GetElement(view.ViewTemplateId) as View;
                        if (templateView != null)
                            templateName = templateView.Name;
                    }
                }
                catch (Exception ex) { Core.StingLog.Warn($"Template detect: {ex.Message}"); }

                // ── Scope Box detection ──
                string scopeBoxName = null;
                try
                {
                    var sbParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (sbParam != null && sbParam.AsElementId() != null
                        && sbParam.AsElementId() != ElementId.InvalidElementId)
                    {
                        var sbElem = doc.GetElement(sbParam.AsElementId());
                        if (sbElem != null)
                            scopeBoxName = sbElem.Name;
                    }
                }
                catch (Exception ex) { Core.StingLog.Warn($"ScopeBox detect: {ex.Message}"); }

                // ── Dependent View detection ──
                bool isDependent = false;
                string parentViewName = null;
                try
                {
                    var primaryId = view.GetPrimaryViewId();
                    if (primaryId != null && primaryId != ElementId.InvalidElementId)
                    {
                        isDependent = true;
                        var parentView = doc.GetElement(primaryId) as View;
                        if (parentView != null)
                            parentViewName = parentView.Name;
                    }
                }
                catch (Exception ex) { Core.StingLog.Warn($"Dependent detect: {ex.Message}"); }

                nodes.Add(new SheetManagerDialog.AllViewNode
                {
                    ViewName = view.Name,
                    ViewType = view.ViewType.ToString(),
                    Scale = view.Scale.ToString(),
                    Tag = view.Id,
                    IsPlaced = isPlaced,
                    PlacedOnSheet = placedSheet,
                    Discipline = disc,
                    Level = level,
                    TemplateName = templateName,
                    ScopeBoxName = scopeBoxName,
                    IsDependent = isDependent,
                    ParentViewName = parentViewName
                });
            }

            return nodes;
        }
    }


    // ── 2. Auto-Layout Command ───────────────────────────────────────────

    /// <summary>
    /// Auto-arrange viewports on the active sheet using shelf-packing algorithm.
    /// Sorts viewports largest-first and packs into rows within the drawable zone.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoLayoutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!(ctx.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Auto Layout", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            var vpCount = sheet.GetAllViewports().Count;
            if (vpCount == 0)
            {
                TaskDialog.Show("Auto Layout", "Sheet has no viewports to arrange.");
                return Result.Succeeded;
            }

            // Margin selection
            var options = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "Default margins", Description = "15/55/10/15 mm (L/R/T/B), 8mm gap", Tag = "default", IsRecommended = true },
                new StingModePicker.ModeOption { Label = "ISO A1 margins", Description = "20/55/10/15 mm, 10mm gap", Tag = "a1" },
                new StingModePicker.ModeOption { Label = "ISO A3 margins", Description = "15/45/10/12 mm, 8mm gap", Tag = "a3" },
                new StingModePicker.ModeOption { Label = "Compact margins", Description = "10/40/8/10 mm, 5mm gap", Tag = "compact" },
            };

            string marginChoice = StingModePicker.Show("Auto Layout", $"Arrange {vpCount} viewports — select margin preset:", options);
            if (marginChoice == null) return Result.Cancelled;

            TitleBlockMargins margins = marginChoice switch
            {
                "a1" => TitleBlockMargins.ISO_A1,
                "a3" => TitleBlockMargins.ISO_A3,
                "compact" => TitleBlockMargins.Compact,
                _ => TitleBlockMargins.Default
            };

            using (var tx = new Transaction(doc, "STING Auto-Layout Viewports"))
            {
                tx.Start();
                int moved = SheetManagerEngine.ArrangeViewportsOnSheet(doc, sheet, margins);
                tx.Commit();
                TaskDialog.Show("Auto Layout",
                    $"Rearranged {moved} of {vpCount} viewports on '{sheet.SheetNumber}'.\n" +
                    $"Margins: {marginChoice}");
            }
            return Result.Succeeded;
        }
    }

    // ── 3. Clone Sheet Command ───────────────────────────────────────────

    /// <summary>
    /// Clone the active sheet with its title block, viewports, and schedules.
    /// Option to duplicate views (with detailing) or reference same views.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CloneSheetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            if (!(ctx.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Clone Sheet", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            // Clone options
            var options = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "Clone with duplicated views", Description = "Create copies of all views (Duplicate with Detailing)", Tag = "dup", IsRecommended = true },
                new StingModePicker.ModeOption { Label = "Clone with view references", Description = "Reference the same views (view-only copy, no duplication)", Tag = "ref" },
                new StingModePicker.ModeOption { Label = "Clone sheet only", Description = "Copy title block and schedules, no viewports", Tag = "empty" },
            };

            string choice = StingModePicker.Show("Clone Sheet",
                $"Clone '{sheet.SheetNumber} - {sheet.Name}':", options);
            if (choice == null) return Result.Cancelled;

            string disc = SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber);
            string nextNum = SheetManagerEngine.GetNextSheetNumber(doc, disc);

            var config = new SheetCloneConfig
            {
                NewSheetNumber = nextNum,
                NewSheetName = sheet.Name + " (Copy)",
                CopyViewports = choice != "empty",
                CopyAnnotations = true,
                CopySchedules = true,
                DuplicateViews = choice == "dup",
                DuplicateMode = ViewDuplicateOption.WithDetailing
            };

            using (var tx = new Transaction(doc, "STING Clone Sheet"))
            {
                tx.Start();
                var cloned = SheetManagerEngine.CloneSheet(doc, sheet, config);
                tx.Commit();

                if (cloned != null)
                {
                    TaskDialog.Show("Clone Sheet",
                        $"Created '{cloned.SheetNumber} - {cloned.Name}'\n" +
                        $"Mode: {choice}\n" +
                        $"Viewports: {cloned.GetAllViewports().Count}");
                }
                else
                {
                    TaskDialog.Show("Clone Sheet", "Failed to clone sheet. Check the log for details.");
                }
            }
            return Result.Succeeded;
        }
    }


    // ── 4. Place Unplaced Views Command ──────────────────────────────────

    /// <summary>
    /// Auto-place all unplaced views onto new sheets using shelf-packing layout.
    /// Groups views by discipline and creates one sheet per group.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceUnplacedViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            if (unplaced.Count == 0)
            {
                TaskDialog.Show("Place Views", "All views are already placed on sheets.");
                return Result.Succeeded;
            }

            // Confirm
            var td = new TaskDialog("Place Unplaced Views");
            td.MainInstruction = $"Found {unplaced.Count} unplaced views";
            td.MainContent = "Create sheets and auto-place all unplaced views using shelf-packing layout?\n\n" +
                "Views will be grouped by discipline. One sheet per group will be created.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto-place with optimal scale", "Calculate best scale per view");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Auto-place with current scale", "Keep each view's existing scale");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var r = td.Show();
            if (r == TaskDialogResult.Cancel) return Result.Cancelled;
            bool autoScale = r == TaskDialogResult.CommandLink1;

            // Find title block
            var tbType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault();

            if (tbType == null)
            {
                TaskDialog.Show("Place Views", "No title block families loaded in the project.");
                return Result.Succeeded;
            }

            using (var tx = new Transaction(doc, "STING Auto-Place Views"))
            {
                tx.Start();
                var (sheetsCreated, viewsPlaced) =
                    SheetManagerEngine.BatchCreateAndPlace(doc, unplaced, tbType.Id, autoScale: autoScale);
                tx.Commit();

                TaskDialog.Show("Place Views",
                    $"Created {sheetsCreated} new sheets.\n" +
                    $"Placed {viewsPlaced} of {unplaced.Count} views.\n" +
                    $"Overflow: {unplaced.Count - viewsPlaced} views could not fit.");
            }
            return Result.Succeeded;
        }
    }

    // ── 5. Optimal Scale Command ─────────────────────────────────────────

    /// <summary>
    /// Calculate the optimal standard scale for the active view to fit
    /// within the drawable area of a target sheet size.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OptimalScaleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            Document doc = ctx.Doc;
            View view = ctx.ActiveView;

            if (view is ViewSheet)
            {
                TaskDialog.Show("Optimal Scale", "Open a plan/section/elevation view, not a sheet.");
                return Result.Succeeded;
            }

            // Target paper sizes
            var options = new List<StingModePicker.ModeOption>
            {
                new StingModePicker.ModeOption { Label = "A1 (841 x 594 mm)", Description = "Standard AEC sheet size", Tag = "A1", IsRecommended = true },
                new StingModePicker.ModeOption { Label = "A3 (420 x 297 mm)", Description = "Half-size prints", Tag = "A3" },
                new StingModePicker.ModeOption { Label = "A0 (1189 x 841 mm)", Description = "Large format", Tag = "A0" },
                new StingModePicker.ModeOption { Label = "ARCH D (914 x 610 mm)", Description = "US Architectural D", Tag = "ARCHD" },
            };

            string choice = StingModePicker.Show("Optimal Scale",
                $"Calculate optimal scale for '{view.Name}':", options);
            if (choice == null) return Result.Cancelled;

            double wMm, hMm;
            switch (choice)
            {
                case "A0":   wMm = 1189; hMm = 841; break;
                case "A3":   wMm = 420;  hMm = 297; break;
                case "ARCHD": wMm = 914.4; hMm = 609.6; break;
                default:     wMm = 841;  hMm = 594; break; // A1
            }

            // Apply standard margins
            var margins = TitleBlockMargins.Default;
            double drawW = (wMm - margins.LeftMm - margins.RightMm) / 304.8;
            double drawH = (hMm - margins.TopMm - margins.BottomMm) / 304.8;

            int optimal = SheetManagerEngine.CalculateOptimalScale(view, drawW, drawH);
            if (optimal < 0)
            {
                TaskDialog.Show("Optimal Scale", "Cannot calculate scale — view has no crop box or zero extents.");
                return Result.Succeeded;
            }

            var (paperW, paperH) = SheetManagerEngine.GetPaperSize(view, optimal);

            var td2 = new TaskDialog("Optimal Scale");
            td2.MainInstruction = $"Optimal scale: 1:{optimal}";
            td2.MainContent = $"View: {view.Name}\n" +
                $"Current scale: 1:{view.Scale}\n" +
                $"Paper size: {choice}\n" +
                $"Viewport on sheet: {paperW * 304.8:F0} x {paperH * 304.8:F0} mm";
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Apply 1:{optimal}", "Change the view scale");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Keep current scale", "Do not change");

            var apply = td2.Show();
            if (apply == TaskDialogResult.CommandLink1)
            {
                using (var tx = new Transaction(doc, "STING Set Optimal Scale"))
                {
                    tx.Start();
                    view.Scale = optimal;
                    tx.Commit();
                }
                TaskDialog.Show("Optimal Scale", $"Scale set to 1:{optimal}.");
            }

            return Result.Succeeded;
        }
    }


    // ── 6. Sheet Audit Command ───────────────────────────────────────────

    /// <summary>
    /// Audit all sheets and viewports — report statistics, unplaced views,
    /// paper utilisation, and potential issues.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            var unplaced = SheetManagerEngine.GetUnplacedViews(doc);
            int totalVps = 0;
            int emptySheets = 0;
            var discCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in sheets)
            {
                int vpCount = sheet.GetAllViewports().Count;
                totalVps += vpCount;
                if (vpCount == 0) emptySheets++;

                string disc = SheetManagerEngine.ExtractDisciplinePrefix(sheet.SheetNumber);
                discCounts.TryGetValue(disc, out int sc);
                discCounts[disc] = sc + 1;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("=== STING Sheet Audit ===\n");
            report.AppendLine($"Total Sheets: {sheets.Count}");
            report.AppendLine($"Total Viewports: {totalVps}");
            report.AppendLine($"Empty Sheets (no viewports): {emptySheets}");
            report.AppendLine($"Unplaced Views: {unplaced.Count}");
            report.AppendLine($"\n--- By Discipline ---");

            foreach (var kv in discCounts.OrderBy(k => k.Key))
            {
                report.AppendLine($"  {kv.Key}: {kv.Value} sheets");
            }

            if (unplaced.Count > 0)
            {
                report.AppendLine($"\n--- Unplaced Views (top 10) ---");
                foreach (var v in unplaced.Take(10))
                {
                    report.AppendLine($"  {v.ViewType,-20} {v.Name}");
                }
                if (unplaced.Count > 10)
                    report.AppendLine($"  ... and {unplaced.Count - 10} more");
            }

            TaskDialog.Show("Sheet Audit", report.ToString());
            StingLog.Info($"Sheet Audit: {sheets.Count} sheets, {totalVps} viewports, {unplaced.Count} unplaced.");
            return Result.Succeeded;
        }
    }

    // ── 7. Batch Arrange Command ─────────────────────────────────────────

    /// <summary>
    /// Arrange viewports on ALL sheets in the project (or selected discipline).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchArrangeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.GetAllViewports().Count > 0)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Batch Arrange", "No sheets with viewports found.");
                return Result.Succeeded;
            }

            var td = new TaskDialog("Batch Arrange");
            td.MainInstruction = $"Arrange viewports on {sheets.Count} sheets?";
            td.MainContent = "This will re-layout all viewports on every sheet using shelf-packing.\n" +
                "Viewports will be sorted largest-first and packed into rows.";
            td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;

            if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            int totalMoved = 0;
            int sheetsProcessed = 0;

            using (var tx = new Transaction(doc, "STING Batch Arrange"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    try
                    {
                        int moved = SheetManagerEngine.ArrangeViewportsOnSheet(doc, sheet);
                        totalMoved += moved;
                        sheetsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Error arranging sheet '{sheet.SheetNumber}': {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Batch Arrange",
                $"Processed {sheetsProcessed} sheets.\n" +
                $"Rearranged {totalMoved} viewports total.");
            return Result.Succeeded;
        }
    }

    // ── 8. Move Viewport Command ─────────────────────────────────────────

    /// <summary>
    /// Move a viewport from the active sheet to a different sheet.
    /// Since the Revit API cannot reassign viewports, this deletes and recreates.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MoveViewportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            if (!(ctx.ActiveView is ViewSheet sourceSheet))
            {
                TaskDialog.Show("Move Viewport", "Navigate to a sheet view first.");
                return Result.Succeeded;
            }

            // Get selected viewport
            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Viewport>()
                .FirstOrDefault();

            if (selected == null)
            {
                TaskDialog.Show("Move Viewport", "Select a viewport on the sheet first.");
                return Result.Succeeded;
            }

            var view = doc.GetElement(selected.ViewId) as View;
            string viewName = view?.Name ?? "(unknown)";

            // Pick target sheet
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder && s.Id != sourceSheet.Id)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            {
                TaskDialog.Show("Move Viewport", "No other sheets available.");
                return Result.Succeeded;
            }

            var sheetOptions = sheets
                .Select(s => $"{s.SheetNumber} - {s.Name}")
                .ToList();

            string picked = StingListPicker.Show(
                "Move Viewport",
                $"Select target sheet for '{viewName}':",
                sheetOptions);

            if (picked == null) return Result.Cancelled;

            int idx = sheetOptions.IndexOf(picked);
            if (idx < 0) return Result.Cancelled;

            var targetSheet = sheets[idx];

            using (var tx = new Transaction(doc, "STING Move Viewport"))
            {
                tx.Start();
                var newVp = SheetManagerEngine.MoveViewportToSheet(doc, selected, targetSheet);
                if (newVp != null)
                {
                    tx.Commit();
                    TaskDialog.Show("Move Viewport",
                        $"Moved '{viewName}' from '{sourceSheet.SheetNumber}' to '{targetSheet.SheetNumber}'.");
                }
                else
                {
                    tx.RollBack();
                    TaskDialog.Show("Move Viewport",
                        $"Failed to move '{viewName}'. The view may not be placeable on the target sheet.");
                }
            }
            return Result.Succeeded;
        }
    }
}
