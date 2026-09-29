// StingTools — tag family load pre-check and in-memory repair.
//
// Revit refuses to load a family whose shared parameter has the same GUID as one
// the project holds but a different type ("cannot be added with name ... and
// type Text because it conflicts with the existing name ... and type Number").
// The shipped tag families were authored when 154 STING parameters were TEXT;
// MR_PARAMETERS.txt now gives them their real types, so a project set up with
// Load Params refuses those families. Because Load Tag Families loads every file
// in one transaction, one refused family rolled all of them back.
//
// Here each family is opened before it is loaded. A conflicting TEXT family
// parameter is swapped for its TEXT display mirror (ReplaceParameter keeps every
// label cell), or, when there is no mirror, turned into a plain family parameter.
// A conflicting parameter that only a label reads (a shared parameter element,
// not a family parameter, which is how tag labels hold them) is removed, since the
// API cannot repoint a label row: the field drops off that tag and the family loads. The repaired copy is saved under the
// family's exact file name in a temp folder and loaded from there. The shipped
// .rfa files are not changed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Tags
{
    internal sealed class TagFamilyLoadItem
    {
        public string FamilyName;
        /// <summary>The file to load: the original, or the repaired copy in the temp folder.</summary>
        public string LoadPath;
        /// <summary>What the repair did, one entry per parameter; empty for an untouched family.</summary>
        public List<string> Repairs = new List<string>();
        /// <summary>Why the family cannot be loaded; null when it can.</summary>
        public string Blocked;
    }

    /// <summary>
    /// Loads families in one transaction that rolls back, without Revit's modal
    /// failure list, if Revit refuses any of them, and says why.
    /// </summary>
    internal sealed class TagFamilyBatchLoader
    {
        private readonly Document _doc;
        public int Transactions { get; private set; }
        public TagFamilyBatchLoader(Document doc) { _doc = doc; }

        public bool TryLoad(IList<string> paths, out string why)
        {
            why = null;
            var failures = new CapturingFailuresPreprocessor(rollBackOnError: true);
            var thrown = new List<string>();
            TransactionStatus status;
            Transactions++;
            using (var tx = new Transaction(_doc, "STING Load Tag Families"))
            {
                tx.Start();
                tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures));
                foreach (string p in paths)
                {
                    try { _doc.LoadFamily(p, new TagFamilyLoadOptions(), out Family _); }
                    catch (Exception ex)
                    {
                        thrown.Add($"{Path.GetFileNameWithoutExtension(p)}: {ex.Message}");
                        StingLog.Warn($"LoadTagFamilies: LoadFamily threw for {p}: {ex.Message}");
                    }
                }
                status = tx.Commit();
            }
            if (status == TransactionStatus.Committed) return true;
            why = failures.Summary(4) ?? (thrown.Count > 0 ? string.Join("; ", thrown.Take(3)) : $"transaction {status}");
            StingLog.Warn($"LoadTagFamilies: a group of {paths.Count} was refused: {why}");
            return false;
        }
    }

    internal sealed class TagFamilyLoadRepair : IDisposable
    {
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly List<SharedParamFacts> _project;
        private Dictionary<string, ExternalDefinition> _mirrors;   // base name -> TEXT mirror
        private string _mirrorError;
        public string TempDir { get; }

        public TagFamilyLoadRepair(Document projectDoc)
        {
            _app = projectDoc.Application;
            _project = SharedParamPreflight.CollectProject(projectDoc);
            TempDir = Path.Combine(Path.GetTempPath(), ".sting-tagload-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>True when the project holds no shared parameters, so nothing can conflict.</summary>
        public bool NothingToCheck => _project.Count == 0;

        public TagFamilyLoadItem Prepare(string rfaPath)
        {
            string famName = Path.GetFileNameWithoutExtension(rfaPath);
            var item = new TagFamilyLoadItem { FamilyName = famName, LoadPath = rfaPath };
            if (NothingToCheck) return item;

            Document famDoc = null;
            try
            {
                famDoc = _app.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    item.Blocked = "could not be opened as a family";
                    return item;
                }
                var fm = famDoc.FamilyManager;
                var side = FamilySide(famDoc);
                var conflicts = SharedParamConflictDetector.Detect(side, _project);
                conflicts.AddRange(UnreadableTextCopies(side, conflicts));
                StingLog.Info($"LoadTagFamilies: '{famName}' carries {side.Count} shared parameter(s), " +
                              $"{side.Count(f => string.IsNullOrWhiteSpace(f.DataType))} with an unreadable type; " +
                              $"{conflicts.Count} conflict(s): " +
                              string.Join(", ", conflicts.Select(c => c.FamilyName)));
                if (conflicts.Count == 0) return item;

                var names = conflicts.Where(c => c.Kind == SharedParamConflictKind.NameCollision).ToList();
                if (names.Count > 0)
                {
                    item.Blocked = "same name, different GUID: " + string.Join(", ", names.Select(c => c.FamilyName));
                    return item;
                }

                using (var tx = new Transaction(famDoc, "STING Repair tag family parameters"))
                {
                    tx.Start();
                    foreach (var c in conflicts)
                    {
                        var fp = fm.Parameters.Cast<FamilyParameter>()
                            .FirstOrDefault(p => p.IsShared && p.GUID == c.FamilyGuid);
                        if (fp == null)
                        {
                            // Read only by a label: a tag's label parameters are shared
                            // parameter elements, not family parameters, and the API
                            // cannot repoint a label row. Removing the element drops that
                            // field from the label and lets the family load.
                            var spe = SharedParameterElement.Lookup(famDoc, c.FamilyGuid);
                            if (spe == null) continue;
                            famDoc.Delete(spe.Id);
                            item.Repairs.Add($"{c.FamilyName} removed from the label (field no longer shown)");
                            continue;
                        }
                        ForgeTypeId group = GroupOf(fp);
                        ExternalDefinition mirror = MirrorFor(c.FamilyName);
                        bool mirrorPresent = mirror != null && fm.Parameters.Cast<FamilyParameter>()
                            .Any(p => p.IsShared && p.GUID == mirror.GUID);
                        if (mirror != null && !mirrorPresent && fp.StorageType == StorageType.String)
                        {
                            fm.ReplaceParameter(fp, mirror, group, fp.IsInstance);
                            item.Repairs.Add($"{c.FamilyName} -> {mirror.Name}");
                        }
                        else
                        {
                            // No usable mirror: keep the label cell, lose the value.
                            fm.ReplaceParameter(fp, c.FamilyName + "_LOCAL", group, fp.IsInstance);
                            item.Repairs.Add($"{c.FamilyName} -> family parameter (label field will be empty" +
                                (mirror == null ? (_mirrorError != null ? $"; {_mirrorError}" : "; no display mirror") : "") + ")");
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        item.Blocked = "the parameter repair did not commit";
                        return item;
                    }
                }

                var leftSide = FamilySide(famDoc);
                var left = SharedParamConflictDetector.Detect(leftSide, _project);
                left.AddRange(UnreadableTextCopies(leftSide, left));
                if (left.Count > 0)
                {
                    item.Blocked = "still conflicts after repair: " + SharedParamConflictDetector.Describe(left, 4);
                    return item;
                }

                Directory.CreateDirectory(TempDir);
                // A loaded family's name is its file name, so the copy keeps the exact name.
                string copy = Path.Combine(TempDir, famName + ".rfa");
                famDoc.SaveAs(copy, new SaveAsOptions { OverwriteExistingFile = true });
                item.LoadPath = copy;
                return item;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadTagFamilies: pre-check of '{famName}' failed: {ex.Message}");
                item.Blocked = "pre-check failed: " + ex.Message;
                return item;
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"LoadTagFamilies: closing '{famName}': {ex.Message}"); }
            }
        }

        /// <summary>
        /// Every shared parameter the family carries: its family parameters AND the
        /// shared parameter elements its labels read. A tag family's label parameters
        /// are only the latter, so reading family parameters alone saw no conflict.
        /// </summary>
        private static List<SharedParamFacts> FamilySide(Document famDoc)
        {
            var byGuid = new Dictionary<Guid, SharedParamFacts>();
            foreach (var f in SharedParamPreflight.CollectProject(famDoc)
                         .Concat(SharedParamPreflight.CollectFamily(famDoc.FamilyManager)))
                if (f.Guid != Guid.Empty) byGuid[f.Guid] = f;
            return byGuid.Values.ToList();
        }

        private HashSet<string> _libraryText;

        /// <summary>
        /// Parameters whose family-side type could not be read (the detector skips
        /// those) but which the tag library is known to carry as TEXT while the project
        /// holds another type. Only used on a family Revit has already refused.
        /// </summary>
        private List<SharedParamTypeConflict> UnreadableTextCopies(
            List<SharedParamFacts> side, List<SharedParamTypeConflict> already)
        {
            var found = new List<SharedParamTypeConflict>();
            if (_libraryText == null) _libraryText = LoadLibraryText();
            var have = new HashSet<Guid>(already.Select(c => c.FamilyGuid));
            var project = _project.Where(p => p.Guid != Guid.Empty)
                .GroupBy(p => p.Guid).ToDictionary(g => g.Key, g => g.Last());
            foreach (var f in side)
            {
                if (!string.IsNullOrWhiteSpace(f.DataType) || have.Contains(f.Guid)) continue;
                if (!project.TryGetValue(f.Guid, out var proj)) continue;
                if (SharedParamConflictDetector.SameType(proj.DataType, "TEXT")) continue;
                if (!_libraryText.Contains(f.Name ?? proj.Name ?? "")) continue;
                found.Add(new SharedParamTypeConflict
                {
                    Kind = SharedParamConflictKind.TypeMismatch,
                    FamilyName = f.Name ?? proj.Name, ProjectName = proj.Name,
                    FamilyGuid = f.Guid, ProjectGuid = proj.Guid,
                    FamilyDataType = "TEXT (unreadable; tag library audit)", ProjectDataType = proj.DataType
                });
            }
            return found;
        }

        /// <summary>Names TAG_PARAM_ALIGNMENT_AUDIT.csv records the tag library authoring as TEXT.</summary>
        private static HashSet<string> LoadLibraryText()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = StingToolsApp.FindDataFile("TAG_PARAM_ALIGNMENT_AUDIT.csv");
                if (string.IsNullOrEmpty(f) || !File.Exists(f))
                {
                    StingLog.Warn("LoadTagFamilies: TAG_PARAM_ALIGNMENT_AUDIT.csv not found; parameters with an unreadable type are not repaired.");
                    return names;
                }
                int nameCol = -1, typeCol = -1;
                foreach (string line in File.ReadLines(f))
                {
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                    var cells = StingToolsApp.ParseCsvLine(line);
                    if (nameCol < 0)
                    {
                        nameCol = Array.FindIndex(cells, c => c.Trim() == "ParameterName");
                        typeCol = Array.FindIndex(cells, c => c.Trim() == "MR_DataType");
                        if (nameCol < 0 || typeCol < 0) { StingLog.Warn("LoadTagFamilies: audit CSV header not recognised."); return names; }
                        continue;
                    }
                    if (cells.Length > Math.Max(nameCol, typeCol) && cells[typeCol].Trim() == "TEXT")
                        names.Add(cells[nameCol].Trim());
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadTagFamilies: reading the tag library audit: {ex.Message}"); }
            return names;
        }

        private ExternalDefinition MirrorFor(string paramName)
        {
            if (_mirrors == null) LoadMirrors();
            return _mirrors.TryGetValue(paramName ?? "", out var d) ? d : null;
        }

        private void LoadMirrors()
        {
            _mirrors = new Dictionary<string, ExternalDefinition>(StringComparer.OrdinalIgnoreCase);
            string mr = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(mr) || !File.Exists(mr))
            {
                _mirrorError = "MR_PARAMETERS.txt not found";
                StingLog.Warn("LoadTagFamilies: MR_PARAMETERS.txt not found; conflicting parameters become family parameters.");
                return;
            }
            string previous = _app.SharedParametersFilename;
            try
            {
                _app.SharedParametersFilename = mr;
                var file = _app.OpenSharedParameterFile();
                if (file == null) { _mirrorError = "MR_PARAMETERS.txt could not be read"; return; }
                foreach (DefinitionGroup g in file.Groups)
                    foreach (Definition d in g.Definitions)
                    {
                        if (!(d is ExternalDefinition ext)) continue;
                        string baseName = DisplayMirrorNames.MirroredParameter(ext.Description);
                        if (baseName != null && ext.GetDataType() == SpecTypeId.String.Text)
                            _mirrors[baseName] = ext;
                    }
            }
            catch (Exception ex)
            {
                _mirrorError = "reading MR_PARAMETERS.txt failed";
                StingLog.Warn($"LoadTagFamilies: reading display mirrors: {ex.Message}");
            }
            finally
            {
                try { _app.SharedParametersFilename = previous ?? ""; }
                catch (Exception ex) { StingLog.Warn($"LoadTagFamilies: restoring shared parameter file: {ex.Message}"); }
            }
        }

        private static ForgeTypeId GroupOf(FamilyParameter fp)
        {
            try { return fp.Definition?.GetGroupTypeId() ?? GroupTypeId.General; }
            catch (Exception ex) { StingLog.Warn($"LoadTagFamilies: group of '{fp.Definition?.Name}': {ex.Message}"); return GroupTypeId.General; }
        }

        public void Dispose()
        {
            try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); }
            catch (Exception ex) { StingLog.Warn($"LoadTagFamilies: removing {TempDir}: {ex.Message}"); }
        }
    }
}
