// ============================================================================
// SharedParamPreflight.cs — read both sides of a family load, before the load.
//
// The Revit-bound half of Core/SharedParamTypeConflict.cs: it collects what a
// family document offers and what a project document holds, and hands both to
// the Revit-free comparison. Read the header of that file for why this exists —
// "LoadFamily back into project failed" is the whole of what a failed
// propagation used to say, and the twelve parameter names behind it were only
// available from a modal dialog nobody could capture.
//
// SharedParameterElement is the right project-side source: a document holds one
// per shared parameter it has ever acquired, whether by binding or by loading a
// family that carried it. That is exactly the set a load is checked against —
// and it is why a "blank project with no parameters" still conflicts. The
// project does not have to have been set up; loading ONE family that carries a
// parameter is enough to fix that GUID's type for every family loaded after it.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>What a pre-flight of the propagation master found.</summary>
    public class MasterPreflight
    {
        /// <summary>Shared parameters that will make the load fail. Empty is the good case.</summary>
        public List<SharedParamTypeConflict> Conflicts { get; set; } = new List<SharedParamTypeConflict>();

        /// <summary>
        /// Text notes in the master that read as instructions to its author. Not a
        /// blocker - the propagation still works - but they are cloned into every
        /// target, so the operator is told before, not after.
        /// </summary>
        public List<string> AuthoringNotes { get; set; } = new List<string>();

        /// <summary>
        /// Standard parameters the master does NOT carry, which propagation will add
        /// to every clone. Not a fault: propagation enforces the standard set rather
        /// than copying whatever the master happens to have. It is named because the
        /// asymmetry is startling when you compare the two families side by side -
        /// "why does TAG_PARA_STATE_3_BOOL exist in the propagated family when it is
        /// not in the universal?" (asked 2026-09-17, and the answer is this list).
        /// </summary>
        public List<string> CloneWillGain { get; set; } = new List<string>();

        /// <summary>
        /// True when the master was actually opened and read. Without it an empty
        /// <see cref="MasterParamNames"/> is ambiguous - "the master carries nothing"
        /// and "we could not look" are opposite facts, and a caller that filters on
        /// the second would strip every parameter.
        /// </summary>
        public bool MasterRead { get; set; }

        /// <summary>Every shared parameter name the master carries.</summary>
        public HashSet<string> MasterParamNames { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads family-side and project-side shared parameter facts out of Revit.</summary>
    public static class SharedParamPreflight
    {
        /// <summary>
        /// Every shared parameter this project already holds a type for. Non-shared
        /// project parameters are absent by construction — they cannot collide with
        /// a family's shared parameter, which Revit keys on GUID.
        /// </summary>
        public static List<SharedParamFacts> CollectProject(Document doc)
        {
            var facts = new List<SharedParamFacts>();
            if (doc == null) return facts;

            foreach (var spe in new FilteredElementCollector(doc)
                        .OfClass(typeof(SharedParameterElement))
                        .Cast<SharedParameterElement>())
            {
                try
                {
                    var def = spe.GetDefinition();
                    facts.Add(new SharedParamFacts
                    {
                        Name = def?.Name ?? spe.Name,
                        Guid = spe.GuidValue,
                        DataType = DataTypeOf(def)
                    });
                }
                catch (Exception ex)
                {
                    // One unreadable element must not blind the whole check, but it
                    // must not pass silently either: a short project-side list is
                    // what makes a conflict check quietly find nothing.
                    StingLog.Warn($"SharedParamPreflight: project parameter unreadable: {ex.Message}");
                }
            }
            return facts;
        }

        /// <summary>Every shared parameter a family document offers.</summary>
        public static List<SharedParamFacts> CollectFamily(FamilyManager fm)
        {
            var facts = new List<SharedParamFacts>();
            if (fm == null) return facts;

            foreach (FamilyParameter fp in fm.Parameters)
            {
                try
                {
                    if (!fp.IsShared) continue;
                    facts.Add(new SharedParamFacts
                    {
                        Name = fp.Definition?.Name,
                        Guid = fp.GUID,
                        DataType = DataTypeOf(fp.Definition)
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SharedParamPreflight: family parameter unreadable: {ex.Message}");
                }
            }
            return facts;
        }

        /// <summary>
        /// The named definitions from an open shared-parameter file, as the facts
        /// they will carry into a family when added. Names that are not in the file
        /// are skipped — a missing definition cannot be added, so it cannot conflict.
        /// </summary>
        public static List<SharedParamFacts> CollectDefinitions(
            DefinitionFile defFile, IEnumerable<string> names)
        {
            var facts = new List<SharedParamFacts>();
            if (defFile == null || names == null) return facts;

            var wanted = new HashSet<string>(
                names.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return facts;

            foreach (DefinitionGroup group in defFile.Groups)
            {
                foreach (Definition d in group.Definitions)
                {
                    var ext = d as ExternalDefinition;
                    if (ext == null || !wanted.Contains(ext.Name)) continue;
                    try
                    {
                        facts.Add(new SharedParamFacts
                        {
                            Name = ext.Name,
                            Guid = ext.GUID,
                            DataType = DataTypeOf(ext)
                        });
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SharedParamPreflight: definition '{ext.Name}' unreadable: {ex.Message}");
                    }
                }
            }
            return facts;
        }

        /// <summary>
        /// The shared parameters that will make loading <paramref name="master"/>
        /// (or a clone of it) into <paramref name="doc"/> fail.
        ///
        /// Opens the family for editing, so it must be called with NO transaction
        /// open on <paramref name="doc"/>. Returns an empty list when the family
        /// could not be opened — a pre-flight that cannot read must not be able to
        /// block the run it is advising on, and it says so in the log.
        /// </summary>
        /// <param name="willAdd">
        /// Definitions the caller is about to add to the clone (the style and
        /// visibility parameters). They are checked too: a parameter that does not
        /// conflict today conflicts the moment propagation adds it.
        /// </param>
        public static MasterPreflight CheckMaster(
            Document doc, Family master, IList<SharedParamFacts> willAdd = null)
        {
            var none = new MasterPreflight();
            if (doc == null || master == null) return none;

            Document famDoc = null;
            try
            {
                famDoc = doc.EditFamily(master);
                if (famDoc == null)
                {
                    StingLog.Warn("SharedParamPreflight: EditFamily(master) returned null — conflict check skipped");
                    return none;
                }

                var familySide = CollectFamily(famDoc.FamilyManager);
                if (willAdd != null && willAdd.Count > 0)
                {
                    // A definition the family already carries is not added again, so
                    // the family's own facts win on a GUID clash.
                    var have = new HashSet<Guid>(familySide.Select(f => f.Guid));
                    familySide.AddRange(willAdd.Where(f => f != null && !have.Contains(f.Guid)));
                }

                var projectSide = CollectProject(doc);
                StingLog.Info($"SharedParamPreflight: '{master.Name}' offers {familySide.Count} shared parameters; " +
                              $"project holds {projectSide.Count}");

                // What the clone will gain. willAdd is the standard style+visibility
                // set; anything in it the master lacks is added to the clone, which is
                // exactly why the two families differ afterwards.
                var masterNames = new HashSet<string>(
                    CollectFamily(famDoc.FamilyManager)
                        .Select(f => f.Name).Where(n => !string.IsNullOrEmpty(n)),
                    StringComparer.OrdinalIgnoreCase);
                var gain = (willAdd ?? new List<SharedParamFacts>())
                    .Where(f => f != null && !string.IsNullOrEmpty(f.Name) && !masterNames.Contains(f.Name))
                    .Select(f => f.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var result = new MasterPreflight
                {
                    MasterRead = true,
                    MasterParamNames = masterNames,
                    CloneWillGain = gain,
                    Conflicts = SharedParamConflictDetector.Detect(familySide, projectSide),
                    // Same open document, so this costs nothing extra. Worth having
                    // here specifically: whatever is in the master is cloned into
                    // every target, so a note the author meant to delete becomes 206
                    // notes that print.
                    AuthoringNotes = AuthoringNoteMatcher.Flag(CollectTextNotes(famDoc))
                };
                if (result.CloneWillGain.Count > 0)
                {
                    // The tier gates are named individually: eleven at most, and they
                    // are the ones an operator notices. The style matrix is counted.
                    var gates = result.CloneWillGain
                        .Where(n => n.StartsWith("TAG_PARA_STATE_", StringComparison.OrdinalIgnoreCase) ||
                                    n.StartsWith("TAG_WARN_", StringComparison.OrdinalIgnoreCase) ||
                                    n.StartsWith("TAG_DEPTH_", StringComparison.OrdinalIgnoreCase) ||
                                    n.StartsWith("TAG_SCALE_", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    StingLog.Info($"SharedParamPreflight: each clone will GAIN {result.CloneWillGain.Count} " +
                                  $"standard parameter(s) the master does not carry" +
                                  (gates.Count > 0 ? ", including " + string.Join(", ", gates) : "") + ".");
                }
                if (result.AuthoringNotes.Count > 0)
                    StingLog.Warn($"SharedParamPreflight: '{master.Name}' carries " +
                                  $"{result.AuthoringNotes.Count} authoring note(s): " +
                                  string.Join(" | ", result.AuthoringNotes));
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SharedParamPreflight: check failed, continuing without it: {ex.Message}");
                return none;
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"SharedParamPreflight: close famDoc: {ex.Message}"); }
            }
        }

        /// <summary>
        /// The text of every text note in a family document. Used to catch
        /// authoring scaffolding before propagation clones it into 206 families -
        /// see <see cref="AuthoringNoteMatcher"/> for what counts.
        /// </summary>
        public static List<string> CollectTextNotes(Document famDoc)
        {
            var notes = new List<string>();
            if (famDoc == null) return notes;
            try
            {
                foreach (var tn in new FilteredElementCollector(famDoc)
                            .OfClass(typeof(TextNote))
                            .Cast<TextNote>())
                {
                    try { if (!string.IsNullOrWhiteSpace(tn.Text)) notes.Add(tn.Text); }
                    catch (Exception ex) { StingLog.Warn($"SharedParamPreflight: text note unreadable: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SharedParamPreflight: collecting text notes: {ex.Message}"); }
            return notes;
        }

        private static string DataTypeOf(Definition def)
        {
            if (def == null) return null;
            try { return def.GetDataType()?.TypeId; }
            catch (Exception ex)
            {
                StingLog.Warn($"SharedParamPreflight: GetDataType on '{def.Name}': {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Collects Revit's own failure text instead of letting it reach a modal
    /// dialog. Used on the family-load transaction so a refused load can say WHY:
    /// the shared-parameter conflicts arrive here as failure messages, and
    /// <c>Document.LoadFamily</c>'s false return carries none of them.
    /// </summary>
    public class CapturingFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly List<string> _messages = new List<string>();

        /// <summary>Distinct failure descriptions seen, in order.</summary>
        public IReadOnlyList<string> Messages => _messages;

        /// <summary>The captured failures as one line, or null if there were none.</summary>
        public string Summary(int maxItems = 12)
        {
            if (_messages.Count == 0) return null;
            var shown = _messages.Take(maxItems).ToList();
            string s = string.Join("; ", shown);
            int hidden = _messages.Count - shown.Count;
            return hidden > 0 ? $"{s}; … and {hidden} more" : s;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            try
            {
                foreach (var f in accessor.GetFailureMessages())
                {
                    string desc = null;
                    try { desc = f.GetDescriptionText(); }
                    catch (Exception ex) { StingLog.Warn($"CapturingFailuresPreprocessor: {ex.Message}"); }
                    if (!string.IsNullOrWhiteSpace(desc) && !_messages.Contains(desc))
                        _messages.Add(desc);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CapturingFailuresPreprocessor: {ex.Message}");
            }

            // Capture only. Resolving or deleting here would change what the load
            // does; the caller already rolls the transaction back on failure.
            return FailureProcessingResult.Continue;
        }
    }
}
