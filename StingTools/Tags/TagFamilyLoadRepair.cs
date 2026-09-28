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
// Here each family is opened before it is loaded. A conflicting TEXT parameter is
// swapped for its TEXT display mirror (ReplaceParameter keeps every label cell),
// or, when there is no mirror, turned into a plain family parameter so the family
// can load with that one label field empty. The repaired copy is saved under the
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
                var conflicts = SharedParamConflictDetector.Detect(SharedParamPreflight.CollectFamily(fm), _project);
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
                        if (fp == null) continue;
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

                var left = SharedParamConflictDetector.Detect(SharedParamPreflight.CollectFamily(fm), _project);
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
