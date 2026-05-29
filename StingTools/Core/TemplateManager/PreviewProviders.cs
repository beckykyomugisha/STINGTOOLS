using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Per-op preview providers — return what the op would create, with each
    /// row's Exists flag pre-computed from a single collector pass over the
    /// active document. Used by TemplateManagerDashboardV2's preview grid.
    ///
    /// These providers are pure preview — they never start a transaction.
    /// </summary>
    public static class PreviewProviders
    {
        /// <summary>
        /// Wire every preview provider into OperationRegistry. Called once
        /// from StingToolsApp.OnStartup (or lazily on first dashboard open).
        /// </summary>
        public static void RegisterAll()
        {
            try { OperationRegistry.RegisterPreview("CreateFillPatterns",   doc => FillPatternsPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register FillPatterns preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateLineStyles",     doc => LineStylesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register LineStyles preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateObjectStyles",   doc => ObjectStylesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register ObjectStyles preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateTextStyles",     doc => TextStylesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register TextStyles preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateDimensionStyles",doc => DimensionStylesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register DimensionStyles preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateVGOverrides",    doc => VGOverridesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register VGOverrides preview: {ex.Message}"); }

            try { OperationRegistry.RegisterPreview("CreateFilters",        doc => FiltersPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register Filters preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateLinePatterns",   doc => LinePatternsPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register LinePatterns preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreateWorksets",       doc => WorksetsPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register Worksets preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("ViewTemplates",        doc => ViewTemplatesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register ViewTemplates preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("AutoAssignTemplates",  doc => AutoAssignPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register AutoAssign preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("ApplyFilters",         doc => ApplyFiltersPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register ApplyFilters preview: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("CreatePhases",         doc => PhasesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register Phases preview: {ex.Message}"); }

            // CROSS-ENGINE
            try { OperationRegistry.RegisterPreview("AecFiltersBrowse",      doc => CrossEngineFacade.AecFiltersPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register AecFiltersBrowse: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("DrawingTypesBrowse",    doc => CrossEngineFacade.DrawingTypesPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register DrawingTypesBrowse: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("ViewStylePacksBrowse",  doc => CrossEngineFacade.ViewStylePacksPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register ViewStylePacksBrowse: {ex.Message}"); }

            // GOVERNANCE
            try { OperationRegistry.RegisterPreview("DriftScan",             doc => DriftScanPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register DriftScan: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("SnapshotCapture",       doc => SnapshotsPreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register SnapshotCapture: {ex.Message}"); }
            try { OperationRegistry.RegisterPreview("LibraryConfigure",      doc => LibraryConfigurePreview(doc as Document)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Register LibraryConfigure: {ex.Message}"); }
        }

        // ── Governance previews ────────────────────────────────────────
        private static OperationPreview DriftScanPreview(Document doc)
        {
            var p = NewPreview("DriftScan", "Drift scan (read-only)");
            if (doc == null) return p;
            try
            {
                var drift = DriftDetector.Scan(doc);
                foreach (var d in drift)
                {
                    p.Rows.Add(new PreviewRow
                    {
                        Key = d.TemplateId.ToString(),
                        Name = d.TemplateName,
                        Category = "Template",
                        Discipline = GuessDisciplineFromName(d.TemplateName),
                        Exists = true,
                        Action = d.Kind,
                        Source = "drift",
                        Detail = d.Detail,
                        RevitElementId = d.TemplateId
                    });
                }
                p.Notes = drift.Count == 0 ? "No drift detected." : $"{drift.Count} drift entries.";
            }
            catch (Exception ex) { p.Notes = "Drift scan failed: " + ex.Message; }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview SnapshotsPreview(Document doc)
        {
            var p = NewPreview("SnapshotCapture", "Snapshots");
            if (doc == null) return p;
            try
            {
                var snaps = SnapshotEngine.ListSnapshots(doc);
                foreach (var path in snaps)
                {
                    var name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? path);
                    p.Rows.Add(new PreviewRow
                    {
                        Key = path,
                        Name = name,
                        Category = "Snapshot",
                        Discipline = "*",
                        Exists = true,
                        Action = "View",
                        Source = "snapshot",
                        Detail = path
                    });
                }
                if (snaps.Count == 0) p.Notes = "No snapshots yet — capture one before a destructive op.";
            }
            catch (Exception ex) { p.Notes = "List snapshots: " + ex.Message; }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview LibraryConfigurePreview(Document doc)
        {
            var p = NewPreview("LibraryConfigure", "Corporate library");
            if (doc == null) return p;
            try
            {
                string path = CorporateLibrary.ResolveLibraryPath(doc);
                string ver = CorporateLibrary.ResolveVersionStamp(doc);
                var cfg = CorporateLibrary.LoadGlobal();
                p.Rows.Add(new PreviewRow
                {
                    Key = "library_path",
                    Name = "Library path",
                    Category = "Config",
                    Discipline = "*",
                    Exists = !string.IsNullOrEmpty(path),
                    Action = "Inspect",
                    Source = "config",
                    Detail = path ?? "(none)"
                });
                p.Rows.Add(new PreviewRow
                {
                    Key = "version_stamp",
                    Name = "Stamped version",
                    Category = "Config",
                    Discipline = "*",
                    Exists = !string.IsNullOrEmpty(ver),
                    Action = "Inspect",
                    Source = "config",
                    Detail = ver ?? "(none)"
                });
                p.Rows.Add(new PreviewRow
                {
                    Key = "channel",
                    Name = "Channel",
                    Category = "Config",
                    Discipline = "*",
                    Exists = true,
                    Action = "Inspect",
                    Source = "config",
                    Detail = cfg.Channel
                });
            }
            catch (Exception ex) { p.Notes = "Read library config: " + ex.Message; }
            FillAvailable(p);
            return p;
        }

        // ── STYLES ─────────────────────────────────────────────────────
        private static OperationPreview FillPatternsPreview(Document doc)
        {
            var p = NewPreview("CreateFillPatterns", "Fill Patterns");
            if (doc == null) return p;
            HashSet<string> existing = CollectNames<FillPatternElement>(doc);
            foreach (var d in SafeFillPatternDefs())
            {
                bool exists = existing.Contains(d.name);
                p.Rows.Add(new PreviewRow
                {
                    Key = d.name,
                    Name = d.name,
                    Category = d.target.ToString(),
                    Discipline = GuessDisciplineFromName(d.name),
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded",
                    Detail = $"angle1={d.a1}° s1={d.s1}mm  angle2={d.a2}° s2={d.s2}mm"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview LineStylesPreview(Document doc)
        {
            var p = NewPreview("CreateLineStyles", "Line Styles");
            if (doc == null) return p;

            // Existing GraphicsStyles
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var coll = new FilteredElementCollector(doc).OfClass(typeof(GraphicsStyle));
                foreach (GraphicsStyle gs in coll) if (gs?.Name != null) existing.Add(gs.Name);
            }
            catch { /* tolerate */ }

            string source = "hardcoded";
            List<(string name, byte r, byte g, byte b, int weight, string pattern)> defs;
            try
            {
                var csv = global::StingTools.Temp.TemplateManager.LoadLineStylesFromCsv();
                if (csv != null && csv.Count > 0) { defs = csv; source = "CSV"; }
                else defs = global::StingTools.Temp.TemplateManager.LineStyleDefs
                    .Select(d => (d.name, d.r, d.g, d.b, d.weight, d.patternName)).ToList();
            }
            catch
            {
                defs = global::StingTools.Temp.TemplateManager.LineStyleDefs
                    .Select(d => (d.name, d.r, d.g, d.b, d.weight, d.patternName)).ToList();
            }

            foreach (var d in defs)
            {
                bool exists = existing.Contains(d.name);
                p.Rows.Add(new PreviewRow
                {
                    Key = d.name,
                    Name = d.name,
                    Category = "Line Style",
                    Discipline = GuessDisciplineFromName(d.name),
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = source,
                    Detail = $"RGB({d.r},{d.g},{d.b}) weight={d.weight} pattern={d.pattern ?? "solid"}"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview ObjectStylesPreview(Document doc)
        {
            var p = NewPreview("CreateObjectStyles", "Object Styles");
            if (doc == null) return p;

            string source = "hardcoded";
            List<(BuiltInCategory cat, int projWt, int cutWt, byte r, byte g, byte b)> defs;
            try
            {
                var csv = global::StingTools.Temp.TemplateManager.LoadObjectStylesFromCsv();
                if (csv != null && csv.Count > 0) { defs = csv; source = "CSV"; }
                else defs = global::StingTools.Temp.TemplateManager.ObjectStyleDefs.ToList();
            }
            catch { defs = global::StingTools.Temp.TemplateManager.ObjectStyleDefs.ToList(); }

            foreach (var d in defs)
            {
                Category cat = null;
                try { cat = Category.GetCategory(doc, d.cat); }
                catch { cat = null; }
                bool exists = cat != null;
                string catName = cat?.Name ?? d.cat.ToString();
                p.Rows.Add(new PreviewRow
                {
                    Key = d.cat.ToString(),
                    Name = catName,
                    Category = d.cat.ToString(),
                    Discipline = GuessDisciplineFromBuiltIn(d.cat),
                    Exists = exists,
                    Action = "Update",
                    Source = source,
                    Detail = $"proj={d.projWt} cut={d.cutWt} RGB({d.r},{d.g},{d.b})"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview TextStylesPreview(Document doc)
        {
            var p = NewPreview("CreateTextStyles", "Text Styles");
            if (doc == null) return p;
            HashSet<string> existing = CollectNames<TextNoteType>(doc);
            foreach (var d in global::StingTools.Temp.TemplateManager.TextStyleDefs)
            {
                bool exists = existing.Contains(d.name);
                p.Rows.Add(new PreviewRow
                {
                    Key = d.name,
                    Name = d.name,
                    Category = "Text Note Type",
                    Discipline = "*",
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded",
                    Detail = $"{d.font} {d.sizeMm}mm{(d.bold ? " bold" : "")}{(d.italic ? " italic" : "")}"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview DimensionStylesPreview(Document doc)
        {
            var p = NewPreview("CreateDimensionStyles", "Dimension Styles");
            if (doc == null) return p;
            HashSet<string> existing = CollectNames<DimensionType>(doc);
            foreach (var d in global::StingTools.Temp.TemplateManager.DimensionStyleDefs)
            {
                bool exists = existing.Contains(d.name);
                p.Rows.Add(new PreviewRow
                {
                    Key = d.name,
                    Name = d.name,
                    Category = "Dimension Type",
                    Discipline = "*",
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded",
                    Detail = $"text {d.textSizeMm}mm units={d.showUnits}"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview VGOverridesPreview(Document doc)
        {
            var p = NewPreview("CreateVGOverrides", "VG Overrides");
            if (doc == null) return p;
            // Each "row" represents a discipline scheme.
            string[] schemes = { "Mechanical", "Electrical", "Plumbing", "Architectural", "Structural", "Fire" };
            foreach (var s in schemes)
            {
                p.Rows.Add(new PreviewRow
                {
                    Key = s,
                    Name = $"VG Override scheme — {s}",
                    Category = "Scheme",
                    Discipline = DisciplineCodeFor(s),
                    Exists = false,   // we don't probe existing per-scheme
                    Action = "Apply",
                    Source = "hardcoded",
                    Detail = $"6-layer override stack for {s} discipline"
                });
            }
            FillAvailable(p);
            return p;
        }

        // ── SETUP ──────────────────────────────────────────────────────
        private static OperationPreview FiltersPreview(Document doc)
        {
            var p = NewPreview("CreateFilters", "STING View Filters");
            if (doc == null) return p;
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>().Where(f => f?.Name != null).Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

            // Probe the corporate filter library (Phase 166) if available.
            // Otherwise show a fixed set of 28 expected STING filter names.
            string[] fallback = {
                "STING - Mechanical", "STING - Mechanical-Demolition", "STING - Mechanical-Existing",
                "STING - Electrical", "STING - Electrical-Demolition", "STING - Electrical-Existing",
                "STING - Plumbing", "STING - Plumbing-Demolition", "STING - Plumbing-Existing",
                "STING - Architectural", "STING - Architectural-Demolition", "STING - Architectural-Existing",
                "STING - Structural", "STING - Structural-Demolition", "STING - Structural-Existing",
                "STING - Fire Protection", "STING - Low Voltage", "STING - Telecommunications",
                "STING - Combined Services", "STING - Coordination", "STING - Untagged",
                "STING - Tagged Incomplete", "STING - Stale Elements", "STING - QA Critical",
                "STING - QA Major", "STING - QA Minor", "STING - Review", "STING - Approved"
            };
            foreach (var name in fallback)
            {
                bool exists = existing.Contains(name);
                p.Rows.Add(new PreviewRow
                {
                    Key = name,
                    Name = name,
                    Category = "Filter",
                    Discipline = GuessDisciplineFromName(name),
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview LinePatternsPreview(Document doc)
        {
            var p = NewPreview("CreateLinePatterns", "Line Patterns");
            if (doc == null) return p;
            var existing = CollectNames<LinePatternElement>(doc);
            string[] standard = {
                "STING - Dashed", "STING - Dotted", "STING - Dash Dot", "STING - Dash Dot Dot",
                "STING - Long Dash", "STING - Center", "STING - Hidden", "STING - Phase Boundary",
                "STING - Fire Compartment", "STING - Setout"
            };
            foreach (var name in standard)
            {
                bool exists = existing.Contains(name);
                p.Rows.Add(new PreviewRow
                {
                    Key = name,
                    Name = name,
                    Category = "Line Pattern",
                    Discipline = "*",
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview WorksetsPreview(Document doc)
        {
            var p = NewPreview("CreateWorksets", "Worksets (ISO 19650)");
            if (doc == null) return p;
            if (!doc.IsWorkshared)
            {
                p.Notes = "Project is not workshared — enable worksharing first.";
                return p;
            }
            var existing = new HashSet<string>(
                new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                    .ToWorksets().Where(w => w?.Name != null).Select(w => w.Name),
                StringComparer.OrdinalIgnoreCase);
            string[] presets = {
                "STING - 00 Shared Levels & Grids",
                "STING - 01 Architecture - Walls",
                "STING - 01 Architecture - Floors", "STING - 01 Architecture - Roofs",
                "STING - 01 Architecture - Ceilings", "STING - 01 Architecture - Doors+Windows",
                "STING - 01 Architecture - Stairs+Railings", "STING - 01 Architecture - Interior",
                "STING - 02 Structure - Columns", "STING - 02 Structure - Framing",
                "STING - 02 Structure - Foundation", "STING - 02 Structure - Rebar",
                "STING - 03 Mech - Equipment", "STING - 03 Mech - Ductwork", "STING - 03 Mech - Hydronics",
                "STING - 04 Elec - Equipment", "STING - 04 Elec - Conduit", "STING - 04 Elec - Lighting",
                "STING - 04 Elec - Devices",
                "STING - 05 Plumb - Equipment", "STING - 05 Plumb - Supply", "STING - 05 Plumb - Drain",
                "STING - 06 Fire - Equipment", "STING - 06 Fire - Sprinklers", "STING - 06 Fire - Alarm",
                "STING - 07 LV - Equipment", "STING - 07 LV - Cabling",
                "STING - 08 Civil - Site", "STING - 08 Civil - External",
                "STING - 90 Existing", "STING - 91 Demolition", "STING - 92 Temporary",
                "STING - 95 References", "STING - 96 Annotation", "STING - 99 Coordination"
            };
            foreach (var name in presets)
            {
                bool exists = existing.Contains(name);
                p.Rows.Add(new PreviewRow
                {
                    Key = name,
                    Name = name,
                    Category = "Workset",
                    Discipline = GuessDisciplineFromName(name),
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview ViewTemplatesPreview(Document doc)
        {
            var p = NewPreview("ViewTemplates", "View Templates");
            if (doc == null) return p;
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Where(v => v.IsTemplate && v.Name != null).Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            string[] templates = {
                "STING - Architectural Plan", "STING - Ceiling RCP", "STING - Working Section",
                "STING - Working Elevation", "STING - Coordination 3D", "STING - Mechanical Plan",
                "STING - Electrical Plan", "STING - Lighting RCP", "STING - Plumbing Plan",
                "STING - Structural Plan", "STING - Fire Protection Plan", "STING - Low Voltage Plan",
                "STING - MEP Coordination", "STING - Combined Services", "STING - Demolition Plan",
                "STING - As-Built Plan", "STING - Detail Section", "STING - Presentation Section",
                "STING - Presentation 3D", "STING - Presentation Elevation", "STING - Area Plan",
                "STING - Engineering Plan", "STING - Handover Plan"
            };
            foreach (var name in templates)
            {
                bool exists = existing.Contains(name);
                p.Rows.Add(new PreviewRow
                {
                    Key = name,
                    Name = name,
                    Category = "View Template",
                    Discipline = GuessDisciplineFromName(name),
                    Exists = exists,
                    Action = exists ? "Skip" : "Create",
                    Source = "hardcoded"
                });
            }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview AutoAssignPreview(Document doc)
        {
            var p = NewPreview("AutoAssignTemplates", "Auto-Assign Templates");
            if (doc == null) return p;
            try
            {
                var views = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Where(v => !v.IsTemplate && CanHaveTemplate(v)).ToList();
                foreach (var v in views)
                {
                    bool hasTmpl = v.ViewTemplateId != ElementId.InvalidElementId;
                    string match = null;
                    try { match = global::StingTools.Temp.TemplateManager.FindMatchingTemplate(v); }
                    catch { match = null; }
                    p.Rows.Add(new PreviewRow
                    {
                        Key = v.Id.ToString(),
                        Name = v.Name,
                        Category = v.ViewType.ToString(),
                        Discipline = match != null ? GuessDisciplineFromName(match) : "?",
                        Exists = hasTmpl,
                        Action = match != null ? (hasTmpl ? "Skip" : "Assign") : "Skip",
                        Source = match != null ? "rule" : "—",
                        Detail = match ?? "no match",
                        RevitElementId = v.Id.Value
                    });
                }
            }
            catch (Exception ex) { p.Notes = "Scan failed: " + ex.Message; }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview ApplyFiltersPreview(Document doc)
        {
            var p = NewPreview("ApplyFilters", "Apply Filters to Templates");
            if (doc == null) return p;
            try
            {
                var stingFilters = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>().Where(f => f.Name?.StartsWith("STING") == true).ToList();
                var stingTemplates = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Where(v => v.IsTemplate && v.Name?.StartsWith("STING") == true).ToList();
                foreach (var t in stingTemplates)
                {
                    var applied = new HashSet<ElementId>(t.GetFilters());
                    int missing = stingFilters.Count(f => !applied.Contains(f.Id));
                    p.Rows.Add(new PreviewRow
                    {
                        Key = t.Id.ToString(),
                        Name = t.Name,
                        Category = "View Template",
                        Discipline = GuessDisciplineFromName(t.Name),
                        Exists = true,
                        Action = missing == 0 ? "Skip" : "Apply",
                        Source = "rule",
                        Detail = $"{applied.Count} applied · {missing} missing",
                        RevitElementId = t.Id.Value
                    });
                }
            }
            catch (Exception ex) { p.Notes = "Scan failed: " + ex.Message; }
            FillAvailable(p);
            return p;
        }

        private static OperationPreview PhasesPreview(Document doc)
        {
            var p = NewPreview("CreatePhases", "Phases (read-only)");
            if (doc == null) return p;
            try
            {
                foreach (Phase ph in doc.Phases)
                {
                    p.Rows.Add(new PreviewRow
                    {
                        Key = ph.Id.ToString(),
                        Name = ph.Name,
                        Category = "Phase",
                        Discipline = "*",
                        Exists = true,
                        Action = "—",
                        Source = "project",
                        RevitElementId = ph.Id.Value
                    });
                }
            }
            catch (Exception ex) { p.Notes = "Scan failed: " + ex.Message; }
            FillAvailable(p);
            return p;
        }

        // ── Helpers ────────────────────────────────────────────────────
        private static OperationPreview NewPreview(string opTag, string label) => new()
        {
            Operation = opTag,
            OperationLabel = label,
            SupportsConflictResolution = true,
            SupportsDryRun = true
        };

        private static HashSet<string> CollectNames<T>(Document doc) where T : Element
        {
            try
            {
                return new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(T)).Where(e => e?.Name != null).Select(e => e.Name),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        private static void FillAvailable(OperationPreview p)
        {
            p.AvailableDisciplines = p.Rows.Select(r => r.Discipline).Where(s => !string.IsNullOrEmpty(s))
                .Distinct().OrderBy(s => s).ToList();
            p.AvailableCategories = p.Rows.Select(r => r.Category).Where(s => !string.IsNullOrEmpty(s))
                .Distinct().OrderBy(s => s).ToList();
            p.AvailableSources = p.Rows.Select(r => r.Source).Where(s => !string.IsNullOrEmpty(s))
                .Distinct().OrderBy(s => s).ToList();
            p.Summary = $"{p.Rows.Count} item(s) · {p.NewCount} new · {p.ExistingCount} existing";
        }

        private static string GuessDisciplineFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            string n = name.ToUpperInvariant();
            if (n.Contains("MECH") || n.Contains("HVAC")) return "M";
            if (n.Contains("ELEC") || n.Contains("POWER") || n.Contains("LIGHTING")) return "E";
            if (n.Contains("PLUMB") || n.Contains("HYDR") || n.Contains("DRAIN")) return "P";
            if (n.Contains("ARCH") || n.Contains("INTER")) return "A";
            if (n.Contains("STRUCT")) return "S";
            if (n.Contains("FIRE") || n.Contains("SPRINK")) return "FP";
            if (n.Contains("LOW VOLT") || n.Contains("COMM") || n.Contains("SEC") || n.Contains("DATA")) return "LV";
            if (n.Contains("CIVIL") || n.Contains("SITE") || n.Contains("EXT")) return "G";
            if (n.Contains("STALE") || n.Contains("REVIEW") || n.Contains("QA") || n.Contains("APPROVED")) return "QA";
            if (n.Contains("EXISTING") || n.Contains("DEMOL")) return "A";
            if (n.Contains("COORD") || n.Contains("COMBINED")) return "*";
            return "*";
        }

        private static string GuessDisciplineFromBuiltIn(BuiltInCategory cat)
        {
            string s = cat.ToString();
            if (s.Contains("Mechanical") || s.Contains("Duct") || s.Contains("HVAC")) return "M";
            if (s.Contains("Electrical") || s.Contains("Lighting")) return "E";
            if (s.Contains("Plumbing") || s.Contains("Pipe")) return "P";
            if (s.Contains("Structural")) return "S";
            if (s.Contains("Sprinkler") || s.Contains("FireAlarm") || s.Contains("Fire")) return "FP";
            if (s.Contains("Wall") || s.Contains("Floor") || s.Contains("Ceiling") || s.Contains("Roof")
                || s.Contains("Door") || s.Contains("Window") || s.Contains("Stairs")
                || s.Contains("Railing") || s.Contains("Ramp") || s.Contains("Furniture")
                || s.Contains("Casework")) return "A";
            return "*";
        }

        private static string DisciplineCodeFor(string s)
        {
            if (string.IsNullOrEmpty(s)) return "*";
            string u = s.ToUpperInvariant();
            if (u.StartsWith("MECH")) return "M";
            if (u.StartsWith("ELEC")) return "E";
            if (u.StartsWith("PLUMB")) return "P";
            if (u.StartsWith("ARCH")) return "A";
            if (u.StartsWith("STRUCT")) return "S";
            if (u.StartsWith("FIRE")) return "FP";
            return "*";
        }

        private static bool CanHaveTemplate(View v)
        {
            try
            {
                return v.ViewType == ViewType.FloorPlan
                    || v.ViewType == ViewType.CeilingPlan
                    || v.ViewType == ViewType.Section
                    || v.ViewType == ViewType.Elevation
                    || v.ViewType == ViewType.ThreeD
                    || v.ViewType == ViewType.AreaPlan
                    || v.ViewType == ViewType.EngineeringPlan
                    || v.ViewType == ViewType.Detail;
            }
            catch { return false; }
        }

        private static IEnumerable<(string name, FillPatternTarget target, double a1, double s1, double a2, double s2)>
            SafeFillPatternDefs()
        {
            try
            {
                foreach (var d in global::StingTools.Temp.TemplateManager.FillPatternDefs)
                    yield return (d.name, d.target, d.angle1Deg, d.spacing1Mm, d.angle2Deg, d.spacing2Mm);
            }
            finally { }
        }
    }
}
