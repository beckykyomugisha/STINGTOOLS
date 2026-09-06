using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterCoreConsoleMerger — multi-layout DWG merge that PRESERVES
    //  Revit's own sheets instead of rebuilding them.
    //
    //  Why this exists
    //  ---------------
    //  The per-sheet DWGs Revit produces (the "OnePerSheet" output) are already
    //  perfect: correct title block, correct viewports, correct framing. They
    //  achieve that framing in a specific way — every view on the sheet is
    //  exported as its own XREF sitting at the MODEL ORIGIN, and each viewport
    //  isolates its view with per-viewport layer freezes. That is why every
    //  exported viewport reports Target = (0,0,0) yet each frame shows something
    //  different.
    //
    //  That structure cannot be reconstructed through AutoCAD's ActiveX/COM API:
    //  there is no way to copy a layout, its viewports, or their per-viewport
    //  layer state into another drawing. CopyObjects needs a SAFEARRAY of
    //  VT_DISPATCH that C# late binding cannot build ("Invalid object array"),
    //  and viewports do not survive WBlock. Rebuilding the sheet by hand loses
    //  precisely the thing that makes it correct.
    //
    //  AutoCAD's own "-LAYOUT Template" command does exactly the right thing —
    //  it imports a layout with its title block, viewports, page setup and
    //  viewport layer states. Driven through COM SendCommand it silently did
    //  nothing, no matter what was tried. Driven through accoreconsole.exe (the
    //  headless AutoCAD script host that ships with AutoCAD) it works, verified
    //  directly: importing one sheet's layout into another's drawing produced a
    //  layout with all 7 viewports at their original paper positions and scales
    //  plus 43 title-block entities at paper extents.
    //
    //  How the merge works
    //  -------------------
    //    1. Gather each source's layout name, xref block names and model extents
    //       (via COM — reading is reliable there).
    //    2. Copy each source into a work folder and, for sources after the
    //       first, RENAME its xrefs to a per-sheet unique prefix. Revit names
    //       every sheet's view-xrefs X1..Xn, so without this the second sheet's
    //       xrefs collide with the first's and AutoCAD drops them
    //       ("Duplicate definition of block X1 (external reference) ignored"),
    //       leaving that sheet's viewports showing the wrong drawing. Renaming
    //       the xref also renames its dependent layers, so the imported
    //       viewport freezes still line up.
    //    3. Copy source #1 to the output path — it becomes the master, keeping
    //       its model space and layout exactly as Revit wrote them.
    //    4. In one script: rename the master's layout to the wanted sheet name,
    //       then for every other source import its layout (-LAYOUT T), rename
    //       it, and INSERT that source at an X offset so its geometry lands
    //       clear of the other sheets'.
    //    5. Shift the imported layouts' viewport targets by the same offset, so
    //       each sheet frames its own geometry at its new home. Within a sheet
    //       the original per-viewport freezes still isolate each view, and the
    //       offset keeps other sheets out of frame — no layer surgery needed.
    // ════════════════════════════════════════════════════════════════════════════

    internal static class ExportCenterCoreConsoleMerger
    {
        /// <summary>Clear gap between adjacent sheets' geometry (20 m, in mm).</summary>
        private const double SheetGap = 20_000.0;

        /// <summary>Fallback stride when a source's extents can't be measured.</summary>
        private const double FallbackStride = 200_000.0;

        private const int ScriptTimeoutMs = 300_000;

        /// <summary>Non-fatal note from the last <see cref="Merge"/>, surfaced by the caller.</summary>
        internal static string LastWarning { get; private set; }

        /// <summary>What the merge needs to know about one source sheet. Populated by
        /// ExportCenterDwgMerger.GatherSourceInfo, which reads it over COM.</summary>
        internal sealed class SourceInfoDto
        {
            public string Path;
            public string LayoutName = "Layout1";
            public List<string> XrefNames = new();
            /// <summary>Where each view's xref actually sits in the source's model space, in
            /// enumeration order. Each viewport is aimed at its OWN view using these, instead of
            /// every viewport on a sheet being aimed at one shared point.</summary>
            public List<double[]> XrefPlacements = new();
            /// <summary>Regular (non-xref, non-layout) block definitions. These must be made
            /// unique per sheet too: -INSERT reuses a same-named definition rather than
            /// renaming it, which is how a whole view came through blank when two sheets from
            /// the same title-block family shared a block name.</summary>
            public List<string> BlockNames = new();
            /// <summary>Names an EARLIER sheet already uses — the ones actually lost to
            /// -INSERT's reuse behaviour, and so the only ones worth renaming.</summary>
            public HashSet<string> CollidingBlockNames = new(StringComparer.OrdinalIgnoreCase);
            public double ModelMinX;
            public double ModelWidth;
            public bool HasExtents;
            public string WorkPath;      // prepped copy actually fed to the script
            public double OffsetX;
            /// <summary>The sheet's REAL paper size, measured off the source layout. Revit's
            /// own page setup is left on the plotter default (PDF24 / A4) even for an A1
            /// sheet, and -LAYOUT T faithfully imports that wrong setup, so it is corrected
            /// after the merge.</summary>
            public double WidthMm;
            public double HeightMm;
        }

        /// <summary>Locates accoreconsole.exe, newest AutoCAD first. Null when AutoCAD isn't
        /// installed, in which case the caller falls back to its COM path.</summary>
        private static string FindCoreConsole()
        {
            try
            {
                foreach (var root in new[]
                         {
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         })
                {
                    string autodesk = Path.Combine(root ?? "", "Autodesk");
                    if (!Directory.Exists(autodesk)) continue;

                    var hit = Directory.GetDirectories(autodesk, "AutoCAD *")
                                       .OrderByDescending(d => d)
                                       .Select(d => Path.Combine(d, "accoreconsole.exe"))
                                       .FirstOrDefault(File.Exists);
                    if (hit != null) return hit;
                }
            }
            catch (Exception ex) { StingLog.Warn($"CoreConsoleMerger: probing for accoreconsole failed: {ex.Message}"); }
            return null;
        }

        internal static string Merge(List<string> sourceDwgs, List<string> layoutNames, string outputPath)
        {
            LastWarning = null;

            var existing = (sourceDwgs ?? new List<string>()).Where(File.Exists).ToList();
            if (existing.Count == 0) return null;

            string exe = FindCoreConsole();
            if (exe == null)
            {
                StingLog.Warn("CoreConsoleMerger: accoreconsole.exe not found — cannot import Revit's layouts.");
                return null;
            }
            StingLog.Info($"CoreConsoleMerger: using {exe}");

            // Work IN the sources' own folder. Revit writes one sidecar DWG per view beside
            // each sheet and xrefs it; staging the copies anywhere else risks those xrefs not
            // resolving, and an xref that doesn't resolve binds to nothing — which is exactly
            // how the sheets ended up with empty viewports and ~29-unit "Xref <path>"
            // placeholder blocks named X1..Xn.
            string work = Path.GetDirectoryName(existing[0]) ?? Path.GetTempPath();
            var staged = new List<string>();
            try
            {
                Directory.CreateDirectory(work);

                var infos = ExportCenterDwgMerger.GatherSourceInfo(existing);
                if (infos == null || infos.Count == 0)
                {
                    StingLog.Warn("CoreConsoleMerger: could not read the sources.");
                    return null;
                }

                // ── Stage sources, giving each one's xrefs a unique prefix ──────────────
                var seenBlockNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                double cursorX = 0;
                for (int i = 0; i < infos.Count; i++)
                {
                    var info = infos[i];
                    info.WorkPath = Path.Combine(work, $"STING_S{i}.dwg");
                    File.Copy(info.Path, info.WorkPath, overwrite: true);
                    staged.Add(info.WorkPath);

                    // Space the sheets out along X so their geometry never overlaps.
                    // Known cost: viewports are re-aimed by the same delta afterwards, and
                    // Target replaces a view's centre rather than shifting it, so one viewport
                    // on a shifted sheet comes through blank. The first sheet is always exact.
                    if (info.HasExtents && info.ModelWidth > 0)
                    {
                        info.OffsetX = cursorX - info.ModelMinX;
                        cursorX += info.ModelWidth + SheetGap;
                    }
                    else
                    {
                        info.OffsetX = i * FallbackStride;
                        cursorX = info.OffsetX + FallbackStride;
                    }

                    // The master keeps its own xref names; later sheets get prefixed so their
                    // definitions aren't discarded as duplicates on import.
                    if (i > 0)
                    {
                        foreach (var b in info.BlockNames)
                            if (seenBlockNames.Contains(b)) info.CollidingBlockNames.Add(b);

                        if (info.XrefNames.Count > 0 || info.CollidingBlockNames.Count > 0)
                            RenameSymbols(exe, info, i);
                    }
                    foreach (var b in info.BlockNames) seenBlockNames.Add(b);
                }

                // ── Master = source #1, untouched apart from its layout name ────────────
                string outDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
                File.Copy(infos[0].WorkPath, outputPath, overwrite: true);

                string script = Path.Combine(work, "STING_merge.scr");
                staged.Add(script);
                string scriptText = BuildMergeScript(infos, layoutNames);
                File.WriteAllText(script, scriptText, Encoding.ASCII);

                if (!RunScript(exe, outputPath, script, "merge"))
                {
                    StingLog.Warn("CoreConsoleMerger: the merge script did not complete.");
                    return null;
                }
                if (!File.Exists(outputPath)) return null;

                // ── Aim each sheet at its own copy, and give it its real paper size ─────
                ExportCenterDwgMerger.FinaliseMergedLayouts(outputPath, infos
                    .Select((s, idx) => (Layout: LayoutNameFor(layoutNames, idx, s),
                                         OffsetX: idx == 0 ? 0 : s.OffsetX,
                                         s.WidthMm, s.HeightMm, s.XrefPlacements))
                    .Where(t => t.Layout != null)
                    .ToList());

                StingLog.Info($"CoreConsoleMerger: wrote {outputPath} ({infos.Count} layout(s) imported from Revit's own sheets).");
                return outputPath;
            }
            catch (Exception ex)
            {
                StingLog.Warn("CoreConsoleMerger: " + ex.Message);
                return null;
            }
            finally
            {
                // Only remove what we created — the folder is the caller's staging area.
                var scratch = staged.ToList();
                try { if (Directory.Exists(work)) scratch.AddRange(Directory.GetFiles(work, "STING_prep*.scr")); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                foreach (var f in scratch)
                {
                    try { if (File.Exists(f)) File.Delete(f); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
            }
        }

        private static string LayoutNameFor(List<string> layoutNames, int index, SourceInfoDto info)
        {
            string raw = layoutNames != null && index < layoutNames.Count
                ? layoutNames[index]
                : Path.GetFileNameWithoutExtension(info.Path);
            return ExportCenterDwgMerger.SanitiseLayoutNamePublic(raw);
        }

        /// <summary>Renames a staged source's xrefs to "S{index}_{name}" so they survive import
        /// alongside another sheet's identically-named ones.</summary>
        private static void RenameSymbols(string exe, SourceInfoDto info, int index)
        {
            var sb = new StringBuilder();
            sb.Append("FILEDIA\r\n0\r\n");
            // Xrefs always need renaming: Revit names them X1..Xn in EVERY sheet, so they
            // always collide. Regular blocks are renamed ONLY when they genuinely clash with
            // an earlier sheet AND their name is safe to type into a script.
            //
            // In an AutoCAD script a SPACE means ENTER. Revit block names are full of spaces
            // ("ACE_A1_TITLE BLOCK_POTRAIT v 24 - ...GROUND FLOOR PLAN LAYOUT"), so feeding one
            // to -RENAME desyncs the prompt sequence and the script hangs waiting for input —
            // which is exactly what renaming every block did.
            var renameable = info.CollidingBlockNames
                .Where(b => b.IndexOf(' ') < 0 && b.IndexOf('"') < 0 && b.IndexOf(',') < 0)
                .ToList();

            int unsafeClashes = info.CollidingBlockNames.Count - renameable.Count;
            if (unsafeClashes > 0)
                StingLog.Warn($"CoreConsoleMerger: '{Path.GetFileName(info.Path)}' has {unsafeClashes} clashing block " +
                              "name(s) containing spaces, which cannot be renamed from a script — a view using one of them " +
                              "may come through blank, reusing the earlier sheet's same-named definition.");

            foreach (var name in info.XrefNames.Concat(renameable).Distinct())
                sb.Append("-RENAME\r\nB\r\n").Append(name).Append("\r\n").Append($"S{index}_{name}").Append("\r\n");
            sb.Append("QSAVE\r\n");

            string script = Path.Combine(Path.GetDirectoryName(info.WorkPath) ?? "", $"STING_prep{index}.scr");
            File.WriteAllText(script, sb.ToString(), Encoding.ASCII);

            if (RunScript(exe, info.WorkPath, script, $"symbol-prefix S{index}"))
                StingLog.Info($"CoreConsoleMerger: prefixed {info.XrefNames.Count} xref(s) + {renameable.Count} clashing block(s) in '{Path.GetFileName(info.Path)}' with S{index}_.");
            else
                StingLog.Warn($"CoreConsoleMerger: could not prefix xrefs in '{Path.GetFileName(info.Path)}' — its views may be " +
                              "dropped as duplicates when its layout is imported.");
        }

        private static string BuildMergeScript(List<SourceInfoDto> infos, List<string> layoutNames)
        {
            var sb = new StringBuilder();
            sb.Append("FILEDIA\r\n0\r\n");

            // Free up the source layout name (Revit calls every sheet's layout "Layout1", so the
            // master's own must be renamed before another can be imported under that name).
            string first = LayoutNameFor(layoutNames, 0, infos[0]);
            sb.Append("-LAYOUT\r\n_R\r\n").Append(infos[0].LayoutName).Append("\r\n").Append(first).Append("\r\n");

            for (int i = 1; i < infos.Count; i++)
            {
                var info = infos[i];
                string wanted = LayoutNameFor(layoutNames, i, info);

                sb.Append("-LAYOUT\r\n_T\r\n\"").Append(info.WorkPath).Append("\"\r\n")
                  .Append(info.LayoutName).Append("\r\n");
                sb.Append("-LAYOUT\r\n_R\r\n").Append(info.LayoutName).Append("\r\n").Append(wanted).Append("\r\n");
            }

            // Model space MUST be current before inserting geometry. Importing a layout leaves
            // a LAYOUT current, so these inserts previously landed in PAPER space — sheet 2's
            // entire drawing ended up sitting inside sheet 1's title block at (138712, 0).
            sb.Append("TILEMODE\r\n1\r\n");
            for (int i = 1; i < infos.Count; i++)
            {
                var info = infos[i];
                // -INSERT, deliberately. "-XREF Attach" was tried here and its prompt sequence
                // differs, so the scripted answers landed on the wrong prompts and desynced
                // every command after it — including the Bind — leaving the whole merge with
                // unresolved xrefs. A raw insert reuses a same-named block definition rather
                // than renaming it, so collisions are prevented up-front instead: every named
                // block in a later source is given a per-sheet prefix during prep.
                sb.Append("-INSERT\r\n\"").Append(info.WorkPath).Append("\"\r\n")
                  .Append(info.OffsetX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(",0\r\n")
                  .Append("1\r\n1\r\n0\r\n");
            }

            // Bind every xref, LAST and before saving. Revit exports each view as an xref
            // beside its sheet in the caller's temp staging folder, which is deleted right
            // after the merge — without this the saved file keeps pointing at files that no
            // longer exist and every viewport comes back empty, showing only
            // "Xref <temp path>" placeholder text (~29-unit blocks named X1..Xn).
            sb.Append("-XREF\r\n_B\r\n*\r\n");


            sb.Append("QSAVE\r\n");
            return sb.ToString();
        }

        private static bool RunScript(string exe, string dwg, string script, string label)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, $"/i \"{dwg}\" /s \"{script}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                string stdout = proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();

                if (!proc.WaitForExit(ScriptTimeoutMs))
                {
                    try { proc.Kill(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    StingLog.Warn($"CoreConsoleMerger: '{label}' script timed out after {ScriptTimeoutMs / 1000}s.");
                    return false;
                }

                // accoreconsole writes its console transcript with embedded nulls; strip them so
                // the log stays readable when something needs diagnosing.
                string clean = new string(stdout.Where(c => c != '\0').ToArray());
                foreach (var line in clean.Split('\n')
                                          .Select(l => l.Trim())
                                          .Where(l => l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                                                   || l.IndexOf("Duplicate definition", StringComparison.OrdinalIgnoreCase) >= 0
                                                   || l.IndexOf("Unknown command", StringComparison.OrdinalIgnoreCase) >= 0
                                                   || l.IndexOf("xref", StringComparison.OrdinalIgnoreCase) >= 0
                                                   || l.IndexOf("bind", StringComparison.OrdinalIgnoreCase) >= 0)
                                          .Take(25))
                    StingLog.Warn($"CoreConsoleMerger [{label}]: {line}");

                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CoreConsoleMerger: '{label}' script failed — {ex.Message}");
                return false;
            }
        }
    }
}
