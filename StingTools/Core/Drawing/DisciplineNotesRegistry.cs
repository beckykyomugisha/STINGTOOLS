using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// W3 — General / discipline standard-notes registry + renderer.
    ///
    /// Loads the corporate baseline <c>STING_DISCIPLINE_NOTES.json</c> and
    /// layers a per-project override at
    /// <c>&lt;project&gt;/_BIM_COORD/discipline_notes.json</c> (project wins by
    /// discipline key). Per-document cache keyed by project path — edit either
    /// JSON and call <see cref="Reload()"/> (or re-open) to pick up changes
    /// without rebuilding the plugin.
    ///
    /// The same renderer is used by the standalone <c>NotesBlockCommand</c>
    /// (places a notes block on the active sheet's notes slot) and by the
    /// <see cref="DrawingProducer"/> Notes production path (mints a drafting
    /// view carrying the discipline's notes).
    /// </summary>
    public static class DisciplineNotesRegistry
    {
        public const string DataFileName = "STING_DISCIPLINE_NOTES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/discipline_notes.json";

        public sealed class NoteBlock
        {
            [JsonProperty("title")] public string Title { get; set; }
            [JsonProperty("notes")] public List<string> Notes { get; set; } = new List<string>();
        }

        public sealed class DisciplineNotesLibrary
        {
            [JsonProperty("version")] public int Version { get; set; } = 1;

            /// <summary>Discipline code (General / M / E / P / A / S) → note block.</summary>
            [JsonProperty("disciplines")]
            public Dictionary<string, NoteBlock> Disciplines { get; set; }
                = new Dictionary<string, NoteBlock>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly ConcurrentDictionary<string, DisciplineNotesLibrary> _cache
            = new ConcurrentDictionary<string, DisciplineNotesLibrary>(StringComparer.OrdinalIgnoreCase);

        public static DisciplineNotesLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload() => _cache.Clear();
        public static void Reload(Document doc) => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static DisciplineNotesLibrary Load(Document doc)
        {
            var lib = new DisciplineNotesLibrary();

            // 1. Corporate baseline.
            try
            {
                string basePath = StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                {
                    var parsed = JsonConvert.DeserializeObject<DisciplineNotesLibrary>(File.ReadAllText(basePath));
                    if (parsed?.Disciplines != null)
                        lib = parsed;
                }
                else
                {
                    StingLog.Warn($"DisciplineNotesRegistry: {DataFileName} not found.");
                }
            }
            catch (Exception ex) { StingLog.Warn($"DisciplineNotesRegistry baseline: {ex.Message}"); }

            // 2. Project override — merge by discipline key (project wins).
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(projPath))
                    {
                        var proj = JsonConvert.DeserializeObject<DisciplineNotesLibrary>(File.ReadAllText(projPath));
                        if (proj?.Disciplines != null)
                            foreach (var kv in proj.Disciplines)
                                lib.Disciplines[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"DisciplineNotesRegistry override: {ex.Message}"); }

            return lib;
        }

        /// <summary>Ordered note sections for a discipline: the General block
        /// first (if present), then the discipline-specific block (if present
        /// and distinct from General).</summary>
        public static List<NoteBlock> GetSections(Document doc, string disciplineCode)
        {
            var lib = Get(doc);
            var sections = new List<NoteBlock>();
            if (lib?.Disciplines == null) return sections;

            if (lib.Disciplines.TryGetValue("General", out var general)
                && general?.Notes?.Count > 0)
                sections.Add(general);

            string code = (disciplineCode ?? "").Trim();
            if (!string.IsNullOrEmpty(code)
                && !code.Equals("General", StringComparison.OrdinalIgnoreCase)
                && !code.Equals("G", StringComparison.OrdinalIgnoreCase)
                && lib.Disciplines.TryGetValue(code, out var disc)
                && disc?.Notes?.Count > 0)
                sections.Add(disc);

            return sections;
        }

        /// <summary>
        /// Render note sections onto a view (drafting view or sheet) as stacked
        /// TextNotes, starting at <paramref name="topLeftFt"/> and decrementing
        /// downward. Must be called inside an active Transaction. Returns the
        /// number of note lines placed.
        /// </summary>
        public static int RenderSections(Document doc, View view, List<NoteBlock> sections,
            XYZ topLeftFt, double wrapWidthFt, List<string> warnings)
        {
            if (doc == null || view == null || sections == null || sections.Count == 0) return 0;

            ElementId textTypeId = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .OrderBy(t => t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0.01)
                .Select(t => t.Id)
                .FirstOrDefault();
            if (textTypeId == null || textTypeId == ElementId.InvalidElementId)
            {
                warnings?.Add("NotesBlock: no TextNoteType in document.");
                return 0;
            }

            // Row pitch scales with the text height so the block is legible at
            // any text-type size; fall back to ~4 mm if the height is unreadable.
            double textHeightFt = 4.0 / 304.8;
            try
            {
                var th = (doc.GetElement(textTypeId) as TextNoteType)?
                    .get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                if (th > 0) textHeightFt = th;
            }
            catch (Exception ex) { StingLog.Warn($"NotesBlock text height: {ex.Message}"); }

            double linePitch = textHeightFt * 2.4;
            double sectionGap = textHeightFt * 1.4;
            double x = topLeftFt.X;
            double y = topLeftFt.Y;
            int placed = 0;

            foreach (var section in sections)
            {
                if (section?.Notes == null || section.Notes.Count == 0) continue;

                // Section title (bold).
                string title = string.IsNullOrWhiteSpace(section.Title) ? "NOTES" : section.Title;
                try
                {
                    var titleNote = CreateNote(doc, view.Id, new XYZ(x, y, 0), title, textTypeId, wrapWidthFt);
                    if (titleNote != null)
                    {
                        try
                        {
                            var ft = titleNote.GetFormattedText();
                            ft.SetBoldStatus(new TextRange(0, title.Length), true);
                            titleNote.SetFormattedText(ft);
                        }
                        catch (Exception ex) { StingLog.Warn($"NotesBlock bold: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { warnings?.Add($"NotesBlock title '{title}': {ex.Message}"); }
                y -= linePitch;

                int n = 1;
                foreach (var note in section.Notes)
                {
                    if (string.IsNullOrWhiteSpace(note)) continue;
                    string line = $"{n++}. {note.Trim()}";
                    try
                    {
                        if (CreateNote(doc, view.Id, new XYZ(x, y, 0), line, textTypeId, wrapWidthFt) != null)
                            placed++;
                    }
                    catch (Exception ex) { warnings?.Add($"NotesBlock line: {ex.Message}"); }
                    y -= linePitch;
                }
                y -= sectionGap;
            }
            return placed;
        }

        private static TextNote CreateNote(Document doc, ElementId viewId, XYZ pos, string text,
            ElementId typeId, double wrapWidthFt)
        {
            if (wrapWidthFt > 0)
            {
                var opts = new TextNoteOptions(typeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    KeepRotatedTextReadable = true,
                };
                return TextNote.Create(doc, viewId, pos, wrapWidthFt, text, opts);
            }
            return TextNote.Create(doc, viewId, pos, text, typeId);
        }

        /// <summary>
        /// Create a drafting view carrying the discipline's standard notes.
        /// Used by the <see cref="DrawingProducer"/> Notes production path.
        /// Must be called inside an active Transaction. Returns null on failure.
        /// </summary>
        public static View CreateNotesView(Document doc, string disciplineCode, List<string> warnings)
        {
            var sections = GetSections(doc, disciplineCode);
            if (sections.Count == 0)
            {
                warnings?.Add("NotesBlock: no notes defined for discipline '" + (disciplineCode ?? "") + "'.");
                return null;
            }

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (vft == null)
            {
                warnings?.Add("NotesBlock: no Drafting ViewFamilyType found.");
                return null;
            }

            ViewDrafting view = ViewDrafting.Create(doc, vft.Id);
            try { view.Scale = 1; } catch (Exception ex) { StingLog.Warn($"NotesBlock scale: {ex.Message}"); }

            // A4-ish column width (~180 mm) so long notes wrap into a tidy block.
            double wrapWidthFt = 180.0 / 304.8;
            RenderSections(doc, view, sections, XYZ.Zero, wrapWidthFt, warnings);
            return view;
        }
    }
}
