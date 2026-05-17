using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterDwgMerger — multi-layout DWG merger.
    //
    //  Merges N per-sheet DWG files into a single .dwg whose Layouts collection
    //  contains one tab per source sheet. Implementation strategy (Method A
    //  from CLAUDE.md): drive AutoCAD via late-bound COM automation. We use
    //  System.Type / Activator / dynamic to avoid any compile-time AutoCAD ref.
    //
    //  When AutoCAD COM (Method A) is unavailable, the engine optionally falls
    //  back to Method B — ExportCenterOdaConverter — which uses the FREE ODA
    //  File Converter to normalise per-sheet DWGs to a target version and emit
    //  a merge_manifest.json for a downstream tool. Method B does NOT itself
    //  produce a single multi-layout DWG (the free ODA tool can't); the user
    //  gets staged files + manifest + version normalisation rather than a
    //  true merged output. See ExportCenterOdaConverter for the scope note.
    // ════════════════════════════════════════════════════════════════════════════

    internal static class ExportCenterDwgMerger
    {
        /// <summary>Probe for AutoCAD COM (the only path that yields a true merged DWG).</summary>
        private static bool? _availableCache;

        internal static bool IsAvailable()
        {
            if (_availableCache.HasValue) return _availableCache.Value;
            try
            {
                var t = Type.GetTypeFromProgID("AutoCAD.Application");
                _availableCache = t != null;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); _availableCache = false; }
            return _availableCache.Value;
        }

        /// <summary>True if either Method A (AutoCAD COM) or Method B (ODA) is available.</summary>
        internal static bool IsAnyMergerAvailable() =>
            IsAvailable() || ExportCenterOdaConverter.IsAvailable();

        /// <summary>Friendly description of the active merger for status lines.</summary>
        internal static string DescribeAvailableMerger()
        {
            if (IsAvailable())                          return "AutoCAD COM (true layout merge)";
            if (ExportCenterOdaConverter.IsAvailable()) return "ODA File Converter (version-normalise + manifest, no true merge)";
            return null;
        }

        /// <summary>
        /// Method B fallback — copy staged DWGs into a final folder, normalise
        /// every file to <paramref name="targetDwgVersion"/>, and write a
        /// merge_manifest.json that downstream tools (Teigha SDK, ezdxf script,
        /// or AutoCAD COM on a different workstation) can use to do the actual
        /// layout merge. Returns the manifest path on success, null on failure.
        /// </summary>
        internal static string MergeViaOda(List<string> sourceDwgs, List<string> layoutNames,
            string outputDwg, string targetDwgVersion)
        {
            if (sourceDwgs == null || sourceDwgs.Count == 0) return null;
            if (!ExportCenterOdaConverter.IsAvailable())
            {
                StingLog.Warn("DwgMerger.MergeViaOda: ODA File Converter not available.");
                return null;
            }

            try
            {
                string outputFolder = Path.GetDirectoryName(outputDwg) ?? Path.GetTempPath();
                string stagedFolder = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(outputDwg) + "_staged");
                Directory.CreateDirectory(stagedFolder);

                // Stage 1 — assemble inputs in a single folder so the ODA CLI can sweep them.
                string odaIn = Path.Combine(stagedFolder, "_in");
                Directory.CreateDirectory(odaIn);
                foreach (var src in sourceDwgs)
                {
                    if (!File.Exists(src)) continue;
                    File.Copy(src, Path.Combine(odaIn, Path.GetFileName(src)), overwrite: true);
                }

                // Stage 2 — normalise to target DWG version.
                string odaVer = ExportCenterOdaConverter.MapStingVersionToOda(targetDwgVersion);
                int converted = ExportCenterOdaConverter.Convert(odaIn, stagedFolder, odaVer, "DWG");
                if (converted == 0)
                {
                    StingLog.Warn("DwgMerger.MergeViaOda: ODA conversion produced no files.");
                    return null;
                }

                // Stage 3 — drop a manifest describing the intended layout structure.
                string manifest = ExportCenterOdaConverter.WriteMergeManifest(
                    stagedFolder, sourceDwgs, layoutNames, outputDwg, targetDwgVersion);

                // Stage 4 — clean up the temporary input folder.
                try { Directory.Delete(odaIn, true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                StingLog.Info($"DwgMerger.MergeViaOda: staged {converted} normalised DWGs + manifest at {stagedFolder}.");
                return manifest;
            }
            catch (Exception ex)
            {
                StingLog.Warn("DwgMerger.MergeViaOda: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Merge a set of source DWGs into a single output DWG with one Layout
        /// per source. Returns the output path on success, null on failure.
        /// </summary>
        /// <param name="sourceDwgs">Per-sheet DWG paths produced by Document.Export.</param>
        /// <param name="layoutNames">Layout tab labels, parallel to sourceDwgs.</param>
        /// <param name="outputPath">Final .dwg path (will be overwritten).</param>
        internal static string Merge(List<string> sourceDwgs, List<string> layoutNames, string outputPath)
        {
            if (!IsAvailable())
            {
                StingLog.Warn("DwgMerger.Merge: AutoCAD COM not available.");
                return null;
            }
            if (sourceDwgs == null || sourceDwgs.Count == 0) return null;

            dynamic acad = null;
            dynamic master = null;
            try
            {
                Type acadType = Type.GetTypeFromProgID("AutoCAD.Application");
                if (acadType == null) return null;
                acad = Activator.CreateInstance(acadType);
                acad.Visible = false;

                // Start with a fresh drawing as the master container.
                master = acad.Documents.Add();
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < sourceDwgs.Count; i++)
                {
                    string src = sourceDwgs[i];
                    if (!File.Exists(src)) continue;

                    string desiredName = SanitiseLayoutName(
                        i < layoutNames.Count ? layoutNames[i] : Path.GetFileNameWithoutExtension(src),
                        usedNames);

                    try
                    {
                        // Copy each layout from src into master via SendCommand —
                        // less brittle than CopyObjects across documents.
                        // The LAYOUT command's Template option imports from another DWG.
                        // Syntax: LAYOUT T <path> <source-layout> <new-name>
                        string srcSafe = src.Replace("\\", "/");
                        // Try the most portable invocation: import every layout, then rename.
                        string cmd = $"_.-LAYOUT _T \"{srcSafe}\" * \n";
                        master.SendCommand(cmd);

                        // Rename newly-imported tabs to follow our layoutNames pattern.
                        TryRenameImportedLayouts(master, desiredName, usedNames);
                    }
                    catch (Exception exSrc)
                    {
                        StingLog.Warn($"DwgMerger.Merge: source '{src}' failed — {exSrc.Message}");
                    }
                }

                // SaveAs — file format follows AutoCAD's default (matches the master's
                // dwgVersion which AutoCAD picked from the per-sheet DWGs).
                master.SaveAs(outputPath);
                StingLog.Info($"DwgMerger.Merge: wrote {outputPath} ({sourceDwgs.Count} layouts).");
                return outputPath;
            }
            catch (COMException comEx)
            {
                StingLog.Warn("DwgMerger.Merge COM error: " + comEx.Message);
                return null;
            }
            catch (Exception ex)
            {
                StingLog.Warn("DwgMerger.Merge: " + ex.Message);
                return null;
            }
            finally
            {
                try { master?.Close(false); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                try { acad?.Quit(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (acad != null && Marshal.IsComObject(acad))
                    Marshal.FinalReleaseComObject(acad);
            }
        }

        private static void TryRenameImportedLayouts(dynamic doc, string baseName, HashSet<string> used)
        {
            try
            {
                dynamic layouts = doc.Layouts;
                int n = (int)layouts.Count;
                int seq = 1;
                for (int i = 0; i < n; i++)
                {
                    dynamic layout = layouts.Item(i);
                    string current = (string)layout.Name;
                    if (current.Equals("Model", StringComparison.OrdinalIgnoreCase)) continue;
                    if (used.Contains(current)) continue;

                    string candidate = baseName;
                    while (used.Contains(candidate))
                        candidate = baseName + "_" + (++seq).ToString();
                    layout.Name = candidate;
                    used.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("DwgMerger.TryRenameImportedLayouts: " + ex.Message);
            }
        }

        private static string SanitiseLayoutName(string raw, HashSet<string> taken)
        {
            string clean = ExportCenterEngine.Sanitise(raw ?? "Layout", "_");
            if (clean.Length > 31) clean = clean.Substring(0, 31);
            string candidate = clean;
            int i = 1;
            while (taken.Contains(candidate))
            {
                string suffix = "_" + (++i);
                int max = 31 - suffix.Length;
                candidate = (clean.Length > max ? clean.Substring(0, max) : clean) + suffix;
            }
            return candidate;
        }
    }
}
