using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterDwgMerger — multi-layout DWG merger.
    //
    //  Merges N per-sheet DWG files (each already a clean, correct, standalone
    //  export — the same "OnePerSheet" native Document.Export output the rest
    //  of the pipeline already produces well) into a single, self-contained
    //  .dwg whose Layouts collection contains one tab per source sheet.
    //  Method A from CLAUDE.md: drive AutoCAD via late-bound COM automation.
    //  We use System.Type / Activator / dynamic to avoid any compile-time
    //  AutoCAD reference.
    //
    //  Earlier revisions of this file tried to keep every sheet's model-space
    //  content at its TRUE shared project coordinates via xref attach + bind +
    //  per-viewport VPLAYER-freeze isolation. In practice that produced
    //  cluttered, duplicated-looking geometry (every sheet's xref sat on top
    //  of every other sheet's at the same coordinates, and the VPLAYER
    //  isolation step — the one part of the pipeline with no typed COM
    //  equivalent — proved unreliable at making each viewport show only its
    //  own sheet) and unresolved binds left "Not Found" xrefs after the
    //  caller's temp folder was cleaned up. Per-sheet DWGs from the same
    //  Revit view are NOT interchangeable or identical the way that design
    //  assumed — different sheets carry different V/G overrides, crops, and
    //  phases even for the same physical model — so sharing one Model space
    //  was the wrong goal to begin with.
    //
    //  Current design — build each sheet with TYPED COM calls only. No
    //  command macros are used anywhere in the merge. "-LAYOUT T" (the
    //  Template import) was relied on for many iterations and proved
    //  fundamentally unreliable in this environment: it repeatedly reported
    //  success (CMDACTIVE back to 0, no exception, sub-second) while creating
    //  zero layouts, and survived every mitigation attempted — longer settle
    //  delays, message-pump priming, per-attempt retries with growing pauses,
    //  and reordering it ahead of any other access to the same file. Typed
    //  members go through IDispatch and either succeed or throw, so a failure
    //  is always visible instead of silent.
    //
    //  This depends on the per-sheet sources being SELF-CONTAINED, which is
    //  why ExportCenterEngine.ResolveDwgOptions sets MergedViews = true for
    //  the merge modes. Left at Revit's default of false, each view on a
    //  sheet is exported as a separate sidecar file and referenced as an
    //  XREF; that works for OnePerSheet (sidecars land beside the output and
    //  resolve) but silently wrecked the merge, whose sources live in a temp
    //  staging folder — model space came through as nothing but "Xref <temp
    //  path>" placeholder text with X1..Xn listed as "Not Found".
    //
    //  Per source sheet (see MergeCore):
    //    1. Open the source read-only and read its own layout's ConfigName +
    //       CanonicalMediaName (+ width/height). This is what preserves each
    //       sheet's true Revit paper size through the merge; without it every
    //       sheet inherits the master template's default, which is how A1
    //       sheets previously came out as A4.
    //    2. Layouts.Add + ApplyPageSetup — create the sheet and stamp that
    //       plotter config and paper size onto it, so it is the right size
    //       before anything is drawn on it.
    //    3. ModelSpace.InsertBlock — copy the source's entire model space in
    //       at a fixed, generous X offset per sheet index
    //       (SheetOffsetSpacing). A bounding-box-driven spacing would be
    //       tighter, but AcadEntity.GetBoundingBox returns its two points via
    //       COM out-parameters, which the C# dynamic binder cannot marshal
    //       for a late-bound RCW with no type library — so a fixed spacing
    //       sized well beyond any realistic single-building export is the
    //       robust choice, not a measured one. It guarantees zero overlap
    //       between sheets regardless of building size, and because
    //       InsertBlock copies geometry directly into the master file the
    //       result is self-contained from the moment it's inserted.
    //    4. TryWblockPaperSpace — Document.WBlock the source sheet's paperspace
    //       (its TITLE BLOCK: measured at 36 entities spanning 0..856 x -1..593
    //       on an A1 sheet, i.e. paper scale) out to a temp DWG, then
    //       InsertBlock it onto the merged sheet at the paper origin. The
    //       selection set is a crossing window, which acts on the current space
    //       only, so no object array ever crosses COM.
    //       This MUST NOT use acSelectionSetAll: that selects the whole
    //       database irrespective of current space, so it captured model
    //       geometry instead (6499 entities spanning 105 metres) and pasted a
    //       drawing 125x the size of the page onto the sheet — the sheets then
    //       looked blank because the content sat almost entirely off-page.
    //    5. Recreate one viewport per view frame on the source sheet, keeping
    //       its paper rectangle and scale. Their target CANNOT be copied from
    //       the source: every Revit-exported viewport reports Target = (0,0,0)
    //       (verified directly, including with the layout current), which only
    //       frames the drawing when the views sit at the model origin — true
    //       for xref-per-view exports, false once MergedViews lays the views
    //       out across model space. The target is therefore computed from the
    //       source's own EXTMIN/EXTMAX centre, shifted by this sheet's offset.
    //
    //  Two approaches were tried here and abandoned, both for hard reasons:
    //    * CopyObjects (to copy paperspace entities directly) needs a real
    //      SAFEARRAY of VT_DISPATCH, which C# late binding cannot construct —
    //      it fails with "Invalid object array" for any object[]. WBlock
    //      replaces it precisely because it takes a SelectionSet instead.
    //    * "-LAYOUT T", which would have carried a whole layout across in one
    //      operation, imports nothing here. It was pursued through several
    //      theories — settle delays, message-pump priming, retries, ordering
    //      before any other file access, self-contained sources, and finally
    //      resolving the genuine name collision (Revit names every sheet's
    //      layout "Layout1", which a blank AutoCAD document already has). It
    //      still silently imports nothing, so it is gone.
    //
    //  Note the ActiveDocument handoff in MergeCore: reading a source's page
    //  setup makes that source the application's active document, and some
    //  members only work against the active one. Note too that the
    //  ActiveLayout property refuses to work in this context at all, so the
    //  current layout is switched via the TILEMODE and CTAB system variables.
    //  Master is re-activated before any of the steps above touch it.
    //
    //  The whole merge still runs under a hard wall-clock watchdog (see
    //  Merge) that kills the launched acad.exe if AutoCAD gets stuck on a
    //  modal it can't show (it runs with Visible = false), and every COM call
    //  goes through ComRetry for the transient RPC_E_CALL_REJECTED busy state.
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
        /// <summary>Exposes the layout-name sanitiser to the core-console merger.</summary>
        internal static string SanitiseLayoutNamePublic(string raw) =>
            SanitiseLayoutName(raw, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        /// <summary>Reads what the core-console merge needs from each source: its layout name,
        /// its xref block names (which must be made unique before layouts are merged) and its
        /// MODEL extents. Uses COM because reading is reliable there; all the editing is left
        /// to the script host.</summary>
        internal static List<ExportCenterCoreConsoleMerger.SourceInfoDto> GatherSourceInfo(List<string> sources)
        {
            var results = new List<ExportCenterCoreConsoleMerger.SourceInfoDto>();
            if (!IsAvailable()) return results;

            dynamic acad = null;
            try
            {
                Type acadType = Type.GetTypeFromProgID("AutoCAD.Application");
                if (acadType == null) return results;
                acad = Activator.CreateInstance(acadType);
                ComRetry(() => acad.Visible = false);
                PumpAndWait(1500);

                foreach (var src in sources)
                {
                    dynamic doc = null;
                    var dto = new ExportCenterCoreConsoleMerger.SourceInfoDto { Path = src };
                    try
                    {
                        doc = ComRetry(() => acad.Documents.Open(src, true /* read-only */));
                        PumpAndWait(300);

                        dynamic layout = FindFirstPaperLayout(doc);
                        if (layout != null)
                        {
                            try { dto.LayoutName = (string)ComRetry(() => layout.Name); }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            // Layout.Width/Height read back as 0 unless that layout is current,
                            // which silently skipped the whole page-size correction. The PAPER
                            // extents give the real sheet size instead: read EXTMIN/EXTMAX with
                            // TILEMODE = 0 (paper), then again with 1 (model) further below.
                            try
                            {
                                ComRetry(() => doc.SetVariable("TILEMODE", 0));
                                double[] pmin = (double[])ComRetry(() => doc.GetVariable("EXTMIN"));
                                double[] pmax = (double[])ComRetry(() => doc.GetVariable("EXTMAX"));
                                if (pmin != null && pmax != null && pmax[0] > pmin[0])
                                {
                                    dto.WidthMm = pmax[0] - pmin[0];
                                    dto.HeightMm = pmax[1] - pmin[1];
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }

                        // Xref block definitions — Revit names these X1..Xn per sheet, so they
                        // collide when two sheets are merged unless renamed first.
                        try
                        {
                            dynamic blocks = ComRetry(() => doc.Blocks);
                            int n = (int)ComRetry(() => blocks.Count);
                            for (int i = 0; i < n; i++)
                            {
                                dynamic blk = ComRetry(() => blocks.Item(i));
                                bool isXref = false;
                                try { isXref = (bool)ComRetry(() => blk.IsXRef); }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                                string nm = (string)ComRetry(() => blk.Name);
                                if (string.IsNullOrWhiteSpace(nm)) continue;

                                if (isXref) { dto.XrefNames.Add(nm); continue; }

                                // Regular named blocks need the same per-sheet prefix:
                                // -INSERT reuses a same-named definition rather than renaming
                                // it, so two sheets sharing a title-block family silently lose
                                // a view. Skip AutoCAD reserved/anonymous names (*Model_Space,
                                // *Paper_Space, *U...) and xref-dependent names, which follow
                                // their own xref rename automatically.
                                bool isLayoutBlock = false;
                                try { isLayoutBlock = (bool)ComRetry(() => blk.IsLayout); }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                                if (isLayoutBlock) continue;
                                if (nm.StartsWith("*", StringComparison.Ordinal)) continue;
                                if (nm.Contains("|")) continue;
                                dto.BlockNames.Add(nm);
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"CoreConsoleMerger: xref scan failed for '{Path.GetFileName(src)}': {ex.Message}"); }

                        // Where each view's xref sits, in model-space order. InsertionPoint is
                        // one of the few placement properties C# late binding CAN read (unlike
                        // GetBoundingBox, whose COM out-parameters it cannot marshal), and it is
                        // enough to aim each viewport at its own view.
                        try
                        {
                            ComRetry(() => doc.SetVariable("TILEMODE", 1));
                            dynamic ms = ComRetry(() => doc.ModelSpace);
                            foreach (dynamic ent in ms)
                            {
                                string t = "";
                                try { t = (string)ComRetry(() => ent.ObjectName); }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                                if (t != "AcDbBlockReference") continue;
                                try
                                {
                                    double[] ip = (double[])ComRetry(() => ent.InsertionPoint);
                                    if (ip != null && ip.Length >= 2) dto.XrefPlacements.Add(ip);
                                }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"CoreConsoleMerger: view placements unreadable for '{Path.GetFileName(src)}': {ex.Message}"); }

                        // Model extents — TILEMODE must be 1 first, else these report the PAPER
                        // extents (a Revit sheet opens with its layout current).
                        try
                        {
                            ComRetry(() => doc.SetVariable("TILEMODE", 1));
                            double[] min = (double[])ComRetry(() => doc.GetVariable("EXTMIN"));
                            double[] max = (double[])ComRetry(() => doc.GetVariable("EXTMAX"));
                            if (min != null && max != null && max[0] > min[0])
                            {
                                dto.ModelMinX = min[0];
                                dto.ModelWidth = max[0] - min[0];
                                dto.HasExtents = true;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"CoreConsoleMerger: extents unreadable for '{Path.GetFileName(src)}': {ex.Message}"); }

                        StingLog.Info($"CoreConsoleMerger: '{Path.GetFileName(src)}' layout='{dto.LayoutName}', " +
                                      $"xrefs={dto.XrefNames.Count}, views={dto.XrefPlacements.Count}, blocks={dto.BlockNames.Count}, paper={dto.WidthMm:F0}x{dto.HeightMm:F0}mm, " +
                                      $"modelWidth={(dto.HasExtents ? dto.ModelWidth.ToString("F0") : "?")}.");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CoreConsoleMerger: could not read '{Path.GetFileName(src)}': {ex.Message}");
                    }
                    finally
                    {
                        try { doc?.Close(false); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }
                    results.Add(dto);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("CoreConsoleMerger: source scan failed — " + ex.Message);
            }
            finally
            {
                try { acad?.Quit(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (acad != null && Marshal.IsComObject(acad)) Marshal.FinalReleaseComObject(acad);
            }
            return results;
        }

        /// <summary>Shifts every viewport target in the named layouts by that sheet's X offset.
        /// The imported viewports still target (0,0,0) as Revit wrote them, which is right for
        /// the master (its geometry stayed at the origin) but not for sheets whose geometry was
        /// inserted further along. Their own per-viewport layer freezes keep the views isolated
        /// within the sheet; the offset keeps the other sheets out of frame.</summary>
        internal static void FinaliseMergedLayouts(string dwgPath,
            List<(string Layout, double OffsetX, double WidthMm, double HeightMm, List<double[]> Views)> shifts)
        {
            if (shifts == null || shifts.Count == 0 || !IsAvailable()) return;

            dynamic acad = null, doc = null;
            try
            {
                Type acadType = Type.GetTypeFromProgID("AutoCAD.Application");
                if (acadType == null) return;
                acad = Activator.CreateInstance(acadType);
                ComRetry(() => acad.Visible = false);
                PumpAndWait(1500);

                doc = ComRetry(() => acad.Documents.Open(dwgPath, false));
                PumpAndWait(500);

                foreach (var (layoutName, offsetX, widthMm, heightMm, views) in shifts)
                {
                    try
                    {
                        dynamic layout = FindLayoutByName(doc, layoutName);
                        if (layout == null)
                        {
                            StingLog.Warn($"CoreConsoleMerger: layout '{layoutName}' not found to finalise.");
                            continue;
                        }

                        // Correct the page setup the import brought over from Revit, which
                        // leaves the plotter default (PDF24 / A4) even on an A1 sheet.
                        if (widthMm > 1 && heightMm > 1)
                        {
                            ApplyPageSetup(layout, new SourcePageSetup
                            {
                                WidthMm = widthMm,
                                HeightMm = heightMm,
                            }, layoutName);
                        }

                        // Viewport targets are deliberately NEVER written. Target replaces a
                        // viewport's view centre rather than shifting it, so writing it aimed
                        // every viewport on a sheet at one point and blanked the views that fell
                        // outside that window. Sheets are separated by layer freezing instead.
                        if (offsetX == 0) continue;

                        // Shift every viewport by the same delta.
                        //
                        // Aiming each viewport at its own view was tried instead: viewports were
                        // paired with the sheet's xref placements in order, which is NOT the
                        // order Revit emits them in — the result was several frames showing the
                        // wrong view at the wrong scale, worse than one blank frame. Without a
                        // reliable viewport-to-view mapping (per-viewport frozen-layer state is
                        // not readable through ActiveX) there is nothing to pair on.
                        //
                        // Target replaces a viewport's centre rather than shifting it, so this
                        // still costs one blank viewport per shifted sheet. That is the known,
                        // accepted trade-off; the unshifted first sheet is always exact.
                        int moved = 0;
                        dynamic block = ComRetry(() => layout.Block);
                        foreach (dynamic ent in block)
                        {
                            string type = "";
                            try { type = (string)ComRetry(() => ent.ObjectName); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                            if (type != "AcDbViewport") continue;
                            try
                            {
                                double[] t = (double[])ComRetry(() => ent.Target);
                                ComRetry(() => ent.Target = new double[] { t[0] + offsetX, t[1], t[2] });
                                moved++;
                            }
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                        StingLog.Info($"CoreConsoleMerger: retargeted {moved} viewport(s) in '{layoutName}' by {offsetX:F0}.");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"CoreConsoleMerger: retarget failed for '{layoutName}': {ex.Message}");
                    }
                }

                ComRetry(() => doc.Save());
            }
            catch (Exception ex)
            {
                StingLog.Warn("CoreConsoleMerger: viewport retarget pass failed — " + ex.Message);
            }
            finally
            {
                try { doc?.Close(false); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                try { acad?.Quit(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (acad != null && Marshal.IsComObject(acad)) Marshal.FinalReleaseComObject(acad);
            }
        }

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

        /// <summary>Non-fatal note from the most recent <see cref="Merge"/> call — e.g. a
        /// sheet whose page setup / plot device couldn't be resolved, or a partial import.
        /// Read (and typically cleared) by the caller right after <see cref="Merge"/> returns
        /// so it can surface as a visible warning instead of a silent wrong-size / missing-sheet
        /// result. Null when the merge was clean.</summary>
        internal static string LastWarning { get; private set; }

        /// <summary>Always true under the current insert-and-offset design: ModelSpace.InsertBlock
        /// copies each source's geometry directly into the master file, so there is nothing
        /// external left after a successful <see cref="Merge"/> and it's always safe for the
        /// caller to delete its temp staging folder. Kept (rather than removed) so the caller's
        /// existing gate on this flag stays meaningful without needing its own changes.</summary>
        internal static bool AllXrefsBound { get; private set; } = true;

        /// <summary>Clear gap left between adjacent sheets' model-space copies (20 m in mm).
        /// Sheets are packed using their MEASURED width from EXTMIN/EXTMAX, so this is a true
        /// gap rather than a stride — a fixed stride either wastes space or overlaps, since
        /// real sheets vary a lot (measured 104 m and 155 m wide on the same two-sheet job, so
        /// a flat 100 m would have overlapped them).</summary>
        private const double SheetGap = 20_000.0;

        /// <summary>Fallback stride when a source's extents can't be read — generous enough that
        /// overlap stays unlikely for any realistic single-building export.</summary>
        private const double SheetOffsetSpacing = 200_000.0;

        /// <summary>Overall watchdog: if the whole merge doesn't finish in this window (most likely
        /// because AutoCAD is blocked on a modal it can't show — the instance runs with
        /// Visible = false — e.g. "plot device not found, use default configuration?"), the
        /// automation is presumed stuck. The launched acad.exe process is killed and the merge is
        /// reported as failed rather than left to hang Revit indefinitely.</summary>
        private const int OverallTimeoutSeconds = 180;

        /// <summary>True for the two well-known "the COM server is momentarily busy, try again"
        /// HRESULTs (RPC_E_CALL_REJECTED / RPC_E_SERVERCALL_RETRYLATER). AutoCAD's STA message
        /// pump rejects incoming calls whenever it's busy — most commonly for a short window
        /// right after Documents.Add() while the new document is still settling — and this is
        /// expected/transient, not a real failure. Every call into the AutoCAD object model in
        /// this file goes through <see cref="ComRetry"/> for exactly this reason.</summary>
        private static bool IsTransientComBusy(Exception ex) =>
            ex is COMException cex && ((uint)cex.HResult == 0x80010001 || (uint)cex.HResult == 0x8001010A);

        /// <summary>Retries a COM call while it fails with <see cref="IsTransientComBusy"/>,
        /// up to ~7.5s (30 x 250ms) before letting the final attempt's exception propagate.
        /// Waits via <see cref="PumpAndWait"/>, not a bare sleep — see that method's remarks
        /// for why a plain Thread.Sleep on this thread let the busy state persist for many
        /// seconds instead of clearing quickly.</summary>
        private static dynamic ComRetry(Func<dynamic> action, int maxAttempts = 30, int delayMs = 250)
        {
            for (int attempt = 1; attempt < maxAttempts; attempt++)
            {
                try { return action(); }
                catch (Exception ex) when (IsTransientComBusy(ex))
                {
                    PumpAndWait(delayMs);
                }
            }
            return action(); // final attempt — let any exception propagate to the caller
        }

        private static void ComRetry(Action action, int maxAttempts = 30, int delayMs = 250) =>
            ComRetry(() => { action(); return null; }, maxAttempts, delayMs);

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Msg
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out Win32Msg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint removeMsg);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Win32Msg msg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Win32Msg msg);

        /// <summary>Waits <paramref name="totalMs"/> while actively pumping this thread's
        /// Windows message queue. A dedicated STA <see cref="Thread"/> (as used by
        /// <see cref="Merge"/>) initializes COM for the thread but never runs a message
        /// loop the way a UI thread does — and out-of-process STA COM (talking to AutoCAD's
        /// separate acad.exe) depends on that pump to marshal calls and returns. Without it,
        /// a plain Thread.Sleep here left AutoCAD's calls rejected (RPC_E_CALL_REJECTED) for
        /// many consecutive seconds rather than the brief settling window ComRetry alone
        /// assumed — this is the fix for that, not just a bigger retry budget.</summary>
        private static void PumpAndWait(int totalMs)
        {
            var sw = Stopwatch.StartNew();
            do
            {
                while (PeekMessage(out Win32Msg msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                Thread.Sleep(15);
            } while (sw.ElapsedMilliseconds < totalMs);
        }

        /// <summary>
        /// Merge a set of source DWGs into a single output DWG with one Layout
        /// per source. Returns the output path on success, null on failure.
        /// Runs the actual COM automation on a dedicated STA thread with a hard
        /// wall-clock timeout so a stuck AutoCAD modal can never hang the caller.
        /// </summary>
        /// <param name="sourceDwgs">Per-sheet DWG paths produced by Document.Export.</param>
        /// <param name="layoutNames">Layout tab labels, parallel to sourceDwgs.</param>
        /// <param name="outputPath">Final .dwg path (will be overwritten).</param>
        internal static string Merge(List<string> sourceDwgs, List<string> layoutNames, string outputPath)
        {
            LastWarning = null;
            AllXrefsBound = true;

            if (sourceDwgs == null || sourceDwgs.Count == 0) return null;

            // Preferred path: let AutoCAD import Revit's own layouts, rather than rebuilding
            // them. See MergeViaCoreConsole for why this beats every reconstruction attempt.
            string viaConsole = ExportCenterCoreConsoleMerger.Merge(sourceDwgs, layoutNames, outputPath);
            if (viaConsole != null) return viaConsole;

            StingLog.Warn("DwgMerger.Merge: headless layout import unavailable — falling back to the COM rebuild, " +
                          "which approximates the sheets rather than reproducing them.");

            if (!IsAvailable())
            {
                StingLog.Warn("DwgMerger.Merge: AutoCAD COM not available.");
                return null;
            }

            string result = null;
            Exception threadEx = null;
            int acadProcessId = -1;

            var worker = new Thread(() =>
            {
                try { result = MergeCore(sourceDwgs, layoutNames, outputPath, pid => acadProcessId = pid); }
                catch (Exception ex) { threadEx = ex; }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();

            bool finished = worker.Join(TimeSpan.FromSeconds(OverallTimeoutSeconds));
            if (!finished)
            {
                StingLog.Warn($"DwgMerger.Merge: timed out after {OverallTimeoutSeconds}s — AutoCAD COM automation appears stuck " +
                              "(most likely a modal dialog it can't show because it's running invisibly, e.g. an unresolved plot " +
                              "device on one of the imported layouts). Aborting and killing the AutoCAD process.");
                LastWarning = "DWG multi-layout merge timed out and was aborted — AutoCAD appeared stuck (likely an unresolved " +
                              "plot device/page setup on one of the source sheets).";
                TryKillProcess(acadProcessId);
                return null;
            }

            if (threadEx != null)
            {
                StingLog.Warn("DwgMerger.Merge: " + threadEx.Message);
                return null;
            }
            return result;
        }

        private static string MergeCore(List<string> sourceDwgs, List<string> layoutNames, string outputPath,
            Action<int> reportProcessId)
        {
            dynamic acad = null;
            dynamic master = null;
            try
            {
                Type acadType = Type.GetTypeFromProgID("AutoCAD.Application");
                if (acadType == null) return null;
                acad = Activator.CreateInstance(acadType);
                ComRetry(() => acad.Visible = false);
                reportProcessId(TryGetProcessId(acad));

                // Start with a fresh drawing as the master container. A short settle delay
                // before the first real call reduces (but doesn't replace the need for)
                // ComRetry below — right after Add() the new document's message pump is
                // often still busy and rejects calls with RPC_E_CALL_REJECTED.
                master = ComRetry(() => acad.Documents.Add());
                PumpAndWait(2000);
                try { ComRetry(() => master.SetVariable("FILEDIA", 0)); } catch (Exception exVar) { StingLog.Warn($"Suppressed: {exVar.Message}"); }

                // A blank new document ships with its own default layout tab(s) (typically
                // "Layout1"/"Layout2" from the template). Snapshot them so they can be deleted
                // once the real sheets exist — otherwise the merged file carries two stray
                // empty tabs alongside the genuine ones.
                // Explicitly typed, not var — GetAllLayoutNames takes a dynamic argument so its
                // result is dynamic, which silently breaks any LINQ used on it later.
                HashSet<string> defaultLayoutNames = GetAllLayoutNames(master);
                StingLog.Info($"DwgMerger.Merge: default template layouts: {string.Join(", ", defaultLayoutNames)}");

                // Rename those defaults out of the way before importing anything. Revit names
                // every exported sheet's layout "Layout1", and a blank AutoCAD document also
                // starts with "Layout1"/"Layout2" — so "-LAYOUT T" was being asked to import a
                // layout whose name already existed here, which it refuses by quietly importing
                // nothing. That name collision, not any AutoCAD/COM limitation, is why the
                // template import produced no layout on every previous run.
                var reservedLayoutNames = new List<string>();
                int tempIndex = 0;
                foreach (var defName in defaultLayoutNames.ToList())
                {
                    try
                    {
                        dynamic defLayout = FindLayoutByName(master, defName);
                        if (defLayout == null) continue;
                        string temp = $"_STING_TMP_{++tempIndex}";
                        ComRetry(() => defLayout.Name = temp);
                        reservedLayoutNames.Add(temp);
                        StingLog.Info($"DwgMerger.Merge: renamed default layout '{defName}' → '{temp}' to free the name for imports.");
                    }
                    catch (Exception exRen)
                    {
                        reservedLayoutNames.Add(defName);
                        StingLog.Warn($"DwgMerger.Merge: could not rename default layout '{defName}' — a source layout sharing " +
                                      $"its name will fail to import: {exRen.Message}");
                    }
                }

                var usedLayoutNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var skipped = new List<string>();
                int layoutBuilt = 0;

                // Build every sheet with TYPED COM calls only — no command macros anywhere in
                // this loop. "-LAYOUT T" (the Template import) was used here for many
                // iterations and proved fundamentally unreliable in this environment: it
                // repeatedly returned success (CMDACTIVE back to 0, no exception, sub-second)
                // while creating zero layouts, with untouched source files, and survived every
                // mitigation tried — longer settle delays, message-pump priming, per-attempt
                // retries with growing pauses, and reordering it ahead of any other file
                // access. Typed members (Layouts.Add / AddPViewport / ConfigName /
                // CanonicalMediaName / InsertBlock) go through IDispatch and either succeed or
                // throw, so failures are always visible rather than silent.
                //
                // Per sheet:
                //   1. Open the source read-only and read its OWN paper size + plot config off
                //      its own layout. This is what preserves each sheet's true Revit paper
                //      size — A1 stays A1 — instead of inheriting the master template default.
                //   2. Create a fresh layout and stamp that config + canonical media name on
                //      it, so the sheet is the correct size before anything is drawn on it.
                //   3. CopyObjects the source layout's ENTIRE paperspace — real title block,
                //      annotation and viewport entities — into the new layout. This reuses the
                //      source's actual artwork rather than redrawing an approximation of it.
                //   4. Insert the source's model space as a self-contained block at a fixed X
                //      offset, so no two sheets' geometry can ever overlap...
                //   5. ...and shift the copied viewports' Target by that same offset so each
                //      sheet frames its own geometry at its new home.
                // Left edge for the next sheet's geometry. Advanced by each sheet's measured
                // width so sheets sit close together without ever overlapping.
                double packCursorX = 0;

                for (int i = 0; i < sourceDwgs.Count; i++)
                {
                    string src = sourceDwgs[i];
                    if (!File.Exists(src)) { skipped.Add($"[{i}] (missing file)"); continue; }

                    string desiredLayoutName = SanitiseLayoutName(
                        i < layoutNames.Count ? layoutNames[i] : Path.GetFileNameWithoutExtension(src),
                        usedLayoutNames);
                    double offsetX = packCursorX;   // refined below once the extents are known

                    dynamic srcDoc = null;
                    string titleBlockFile = null;
                    try
                    {
                        // 1 — read the source's page setup + viewport framing, and lift its
                        // paperspace artwork out to a temp DWG, then close it.
                        srcDoc = ComRetry(() => acad.Documents.Open(src, true /* read-only */));
                        PumpAndWait(300);

                        dynamic srcLayout = FindFirstPaperLayout(srcDoc);
                        var pageSetup = ReadPageSetupFrom(srcDoc, srcLayout, Path.GetFileName(src));
                        // Pack this sheet's geometry immediately after the previous one: shift
                        // it so its left edge lands on the cursor, then advance the cursor by
                        // its own width plus a gap.
                        if (pageSetup.HasModelCentre && pageSetup.ModelWidth > 0)
                        {
                            offsetX = packCursorX - pageSetup.ModelMinX;
                            packCursorX += pageSetup.ModelWidth + SheetGap;
                        }
                        else
                        {
                            offsetX = i * SheetOffsetSpacing;
                            packCursorX = offsetX + SheetOffsetSpacing;
                            StingLog.Warn($"DwgMerger.Merge: '{Path.GetFileName(src)}' extents unreadable — falling back to a " +
                                          $"fixed {SheetOffsetSpacing / 1000:F0}m stride for its placement.");
                        }

                        List<SourceViewportInfo> srcViewports =
                            ReadSourceViewports(srcLayout, pageSetup, Path.GetFileName(src));
                        StingLog.Info($"DwgMerger.Merge: '{Path.GetFileName(src)}' page setup → layout='{pageSetup.LayoutName}', " +
                                      $"config='{pageSetup.ConfigName}', media='{pageSetup.CanonicalMediaName}', " +
                                      $"size=({pageSetup.WidthMm}x{pageSetup.HeightMm}mm), viewFrames={srcViewports.Count}.");

                        string candidate = Path.Combine(Path.GetTempPath(),
                            $"STING_TB_{Guid.NewGuid():N}.dwg");
                        if (TryWblockPaperSpace(srcDoc, pageSetup.LayoutName, candidate, Path.GetFileName(src)))
                            titleBlockFile = candidate;

                        try { srcDoc.Close(false); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        srcDoc = null;

                        // Opening the source made IT the active document. Everything below acts
                        // on master, and some members (ActiveLayout, SendCommand) only work on
                        // the active document — this is what previously produced
                        // "Error while invoking ActiveLayout" and left sheets without a real
                        // viewport, falling back to AutoCAD's auto-created one showing the whole
                        // (offset-spread) model space, which is why drawings appeared tiny.
                        try { ComRetry(() => acad.ActiveDocument = master); }
                        catch (Exception ex) { StingLog.Warn($"DwgMerger.Merge: could not re-activate the master document: {ex.Message}"); }

                        // 2 — bring in the geometry, offset so sheets never collide.
                        dynamic blockRef = ComRetry(() => master.ModelSpace.InsertBlock(
                            new double[] { offsetX, 0, 0 }, src, 1.0, 1.0, 1.0, 0.0));
                        string blockName = "?";
                        try { blockName = (string)ComRetry(() => blockRef.Name); } catch (Exception exN) { StingLog.Warn($"Suppressed: {exN.Message}"); }
                        StingLog.Info($"DwgMerger.Merge: inserted '{Path.GetFileName(src)}' as block '{blockName}' at offsetX={offsetX}.");

                        // 3 — build the sheet: correct paper size, the source's own title block
                        // artwork, and a viewport matching the source's framing and scale.
                        // ("-LAYOUT T", which would have carried a layout across whole, was
                        // dropped after it proved to import nothing here under every variation
                        // tried — including with its name collision resolved.)
                        dynamic layout = ComRetry(() => master.Layouts.Add(desiredLayoutName));
                        ApplyPageSetup(layout, pageSetup, desiredLayoutName);
                        // The geometry went in at offsetX, so aim the viewports at that copy's
                        // centre rather than the (0,0,0) the source viewports all report.
                        double targetX = offsetX + (pageSetup.HasModelCentre ? pageSetup.ModelCentreX : 0);
                        double targetY = pageSetup.HasModelCentre ? pageSetup.ModelCentreY : 0;
                        PopulateSheet(master, desiredLayoutName, titleBlockFile, srcViewports, targetX, targetY);

                        usedLayoutNames.Add(desiredLayoutName);
                        layoutBuilt++;
                        StingLog.Info($"DwgMerger.Merge: built layout '{desiredLayoutName}' for '{Path.GetFileName(src)}'.");
                    }
                    catch (Exception exSrc)
                    {
                        skipped.Add(desiredLayoutName);
                        StingLog.Warn($"DwgMerger.Merge: source '{src}' failed — {exSrc.Message}");
                    }
                    finally
                    {
                        if (srcDoc != null)
                            try { srcDoc.Close(false); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                        // The title block is copied into the drawing by InsertBlock, so the
                        // temp file is no longer referenced once the sheet is built.
                        if (titleBlockFile != null && File.Exists(titleBlockFile))
                            try { File.Delete(titleBlockFile); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    }
                }

                // Clean up the default template layout(s) that were never real sheet content
                // (snapshotted above). A drawing must always keep at least one paperspace
                // layout, so stop short of deleting the last one — that only comes into play
                // if every sheet failed to build.
                // NB: deletion is a method on the individual Layout object (Layout.Delete()),
                // not a name-keyed method on the Layouts collection — the latter throws
                // DISP_E_NOTACOLLECTION.
                foreach (var defName in reservedLayoutNames)
                {
                    try
                    {
                        dynamic layouts = ComRetry(() => master.Layouts);
                        int remaining = (int)ComRetry(() => layouts.Count);
                        if (remaining <= 1) break;
                        dynamic layoutToDelete = FindLayoutByName(master, defName);
                        if (layoutToDelete != null)
                        {
                            ComRetry(() => layoutToDelete.Delete());
                            StingLog.Info($"DwgMerger.Merge: deleted default template layout '{defName}'.");
                        }
                    }
                    catch (Exception exDel)
                    {
                        StingLog.Warn($"DwgMerger.Merge: could not remove default template layout '{defName}': {exDel.Message}");
                    }
                }
                // Switch every sheet's viewports on as a final pass. Inspection of the output
                // showed only the last-processed layout kept ViewportOn = true; earlier ones
                // came back off and rendered as empty frames.
                foreach (var builtName in usedLayoutNames)
                    TurnOnViewports(master, builtName);

                StingLog.Info($"DwgMerger.Merge: final layouts before save: {string.Join(", ", GetAllLayoutNames(master))}");

                // SaveAs — file format follows AutoCAD's default (matches the master's
                // dwgVersion which AutoCAD picked from the per-sheet DWGs).
                ComRetry(() => master.SaveAs(outputPath));

                int expected = sourceDwgs.Count;
                if (skipped.Count > 0)
                    LastWarning = $"DWG multi-layout merge built {layoutBuilt}/{expected} layouts — skipped: {string.Join(", ", skipped)} " +
                                  "(see the log for the per-sheet reason).";

                StingLog.Info($"DwgMerger.Merge: wrote {outputPath} ({layoutBuilt}/{expected} layouts).");
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
                try { master?.Close(false); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
                try { acad?.Quit(); } catch (Exception ex3) { StingLog.Warn($"Suppressed: {ex3.Message}"); }
                if (acad != null && Marshal.IsComObject(acad))
                    Marshal.FinalReleaseComObject(acad);
            }
        }

        /// <summary>One source sheet's paper identity, read off its own layout so the merged
        /// file can reproduce it exactly rather than inheriting the master template's default
        /// (which is what previously turned A1 sheets into A4).</summary>
        private sealed class SourcePageSetup
        {
            public string ConfigName;             // plotter/PC3, e.g. "DWG To PDF.pc3"
            public string CanonicalMediaName;     // paper size, e.g. "ISO_full_bleed_A1_(841.00_x_594.00_MM)"
            public double WidthMm  = 841.0;       // fallback A1 landscape if the source can't be read
            public double HeightMm = 594.0;
            /// <summary>The source layout's own tab name.</summary>
            public string LayoutName;

            /// <summary>Centre of the source's MODEL extents (EXTMIN/EXTMAX). Viewports have to
            /// be aimed with this: every Revit-exported viewport reports Target = (0,0,0),
            /// which only frames the drawing when the views sit at the model origin. With
            /// MergedViews the views are laid out across model space instead (measured 1,947 →
            /// 105,814 on one sheet), so a (0,0,0) target lands on empty space — which is
            /// exactly why the reproduced viewports came out blank.</summary>
            public double ModelCentreX;
            public double ModelCentreY;
            public bool HasModelCentre;

            /// <summary>Model extents along X, used to pack sheets side by side with a real gap
            /// instead of a blind fixed stride.</summary>
            public double ModelMinX;
            public double ModelWidth;
        }

        /// <summary>A source sheet's viewport frame: where it sits on the paper and at what
        /// scale. Target is deliberately NOT read from the source — it is always (0,0,0)
        /// there — and is supplied by the caller from the model extents instead.</summary>
        private sealed class SourceViewportInfo
        {
            public double[] Center;
            public double Width;
            public double Height;
            public double CustomScale;   // 0 when unknown — leave AutoCAD's default alone
        }

        /// <summary>The first real (non-Model) layout in a document. Revit exports one
        /// paperspace layout per sheet, so for our sources that is "the" sheet.</summary>
        private static dynamic FindFirstPaperLayout(dynamic doc)
        {
            dynamic layouts = ComRetry(() => doc.Layouts);
            int n = (int)ComRetry(() => layouts.Count);
            for (int i = 0; i < n; i++)
            {
                dynamic layout = ComRetry(() => layouts.Item(i));
                string name = (string)ComRetry(() => layout.Name);
                if (!name.Equals("Model", StringComparison.OrdinalIgnoreCase))
                    return layout;
            }
            return null;
        }

        /// <summary>Reads a source layout's plot configuration + paper size — the step that
        /// preserves each sheet's true Revit paper size through the merge. Falls back to A1
        /// landscape (the common case for this codebase's projects) when the source has no
        /// usable paperspace layout: a sane default beats failing the sheet outright.</summary>
        private static SourcePageSetup ReadPageSetupFrom(dynamic srcDoc, dynamic srcLayout, string srcLabel)
        {
            var result = new SourcePageSetup();
            if (srcLayout == null)
            {
                StingLog.Warn($"DwgMerger.Merge: '{srcLabel}' has no paperspace layout — falling back to A1 landscape.");
                return result;
            }

            try { result.LayoutName = (string)ComRetry(() => srcLayout.Name); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            ReadModelCentre(srcDoc, result, srcLabel);
            try { result.ConfigName = (string)ComRetry(() => srcLayout.ConfigName); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { result.CanonicalMediaName = (string)ComRetry(() => srcLayout.CanonicalMediaName); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try
            {
                // GetPaperSize hands its two values back through COM out-parameters, which the
                // C# dynamic binder can't marshal for a late-bound RCW with no type library —
                // so read the plain Width/Height properties instead.
                double w = (double)ComRetry(() => srcLayout.Width);
                double h = (double)ComRetry(() => srcLayout.Height);
                if (w > 1 && h > 1) { result.WidthMm = w; result.HeightMm = h; }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return result;
        }

        /// <summary>Reads the source's model extents (EXTMIN/EXTMAX) and stores their centre.
        /// These are plain system variables, so unlike GetBoundingBox they come back through
        /// GetVariable with no COM out-parameters for the dynamic binder to choke on.</summary>
        private static void ReadModelCentre(dynamic srcDoc, SourcePageSetup setup, string srcLabel)
        {
            try
            {
                // EXTMIN/EXTMAX report the CURRENT space, and a Revit-exported sheet opens with
                // its layout current — so reading them as-is returns the PAPER extents. That
                // produced a "model" width of 849mm instead of the real ~104m, which packed the
                // sheets 20m apart (so they overlapped into one cluttered mass) and aimed every
                // viewport at a paper coordinate. Switch to model space first.
                ComRetry(() => srcDoc.SetVariable("TILEMODE", 1));

                double[] min = (double[])ComRetry(() => srcDoc.GetVariable("EXTMIN"));
                double[] max = (double[])ComRetry(() => srcDoc.GetVariable("EXTMAX"));
                if (min == null || max == null || max[0] <= min[0]) return;

                setup.ModelCentreX = (min[0] + max[0]) / 2.0;
                setup.ModelCentreY = (min[1] + max[1]) / 2.0;
                setup.ModelMinX = min[0];
                setup.ModelWidth = max[0] - min[0];
                setup.HasModelCentre = true;
                StingLog.Info($"DwgMerger.Merge: '{srcLabel}' model extents ({min[0]:F0},{min[1]:F0})..({max[0]:F0},{max[1]:F0}) " +
                              $"→ centre ({setup.ModelCentreX:F0},{setup.ModelCentreY:F0}).");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not read model extents from '{srcLabel}': {ex.Message}");
            }
        }

        /// <summary>Reads each real viewport frame from the source sheet — paper rectangle and
        /// scale only. Skips the sheet-spanning "overall" viewport and AutoCAD's tiny
        /// housekeeping ones (seen at 19.9x11.6 and 12x9 on an 841x594mm sheet), neither of
        /// which is a drawing frame.</summary>
        private static List<SourceViewportInfo> ReadSourceViewports(dynamic srcLayout, SourcePageSetup setup, string srcLabel)
        {
            var found = new List<SourceViewportInfo>();
            if (srcLayout == null) return found;

            try
            {
                dynamic block = ComRetry(() => srcLayout.Block);
                foreach (dynamic ent in block)
                {
                    string type = "";
                    try { type = (string)ComRetry(() => ent.ObjectName); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (type != "AcDbViewport") continue;

                    var info = new SourceViewportInfo();
                    try { info.Center = (double[])ComRetry(() => ent.Center); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    try { info.Width  = (double)ComRetry(() => ent.Width); }    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    try { info.Height = (double)ComRetry(() => ent.Height); }   catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    try { info.CustomScale = (double)ComRetry(() => ent.CustomScale); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                    if (info.Center == null || info.Width <= 0 || info.Height <= 0) continue;
                    bool spansWholeSheet   = info.Width > setup.WidthMm * 0.95 && info.Height > setup.HeightMm * 0.95;
                    bool tooSmallToBeAView = info.Width < setup.WidthMm * 0.05 || info.Height < setup.HeightMm * 0.05;
                    if (spansWholeSheet || tooSmallToBeAView) continue;

                    found.Add(info);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not read source viewports from '{srcLabel}': {ex.Message}");
            }
            return found;
        }

        /// <summary>Adds one viewport, reproducing the source frame's paper rectangle and scale
        /// and aiming it at <paramref name="targetX"/>/<paramref name="targetY"/> — the centre
        /// of this sheet's geometry in the merged model space.</summary>
        private static void AddViewportForSheet(dynamic paperSpace, string layoutName,
            SourceViewportInfo vp, double targetX, double targetY)
        {
            try
            {
                dynamic pvp = ComRetry(() => paperSpace.AddPViewport(vp.Center, vp.Width, vp.Height));

                try { ComRetry(() => pvp.Target = new double[] { targetX, targetY, 0 }); }
                catch (Exception ex) { StingLog.Warn($"DwgMerger.Merge: could not target viewport on '{layoutName}': {ex.Message}"); }

                if (vp.CustomScale > 0)
                {
                    try { ComRetry(() => pvp.CustomScale = vp.CustomScale); }
                    catch (Exception ex) { StingLog.Warn($"DwgMerger.Merge: could not set viewport scale on '{layoutName}': {ex.Message}"); }
                }

                try { ComRetry(() => pvp.Display(true)); }
                catch (Exception ex) { StingLog.Warn($"DwgMerger.Merge: could not turn on viewport display for '{layoutName}': {ex.Message}"); }

                StingLog.Info($"DwgMerger.Merge: added {vp.Width:F0}x{vp.Height:F0} viewport on '{layoutName}' " +
                              $"(scale={vp.CustomScale}) aimed at ({targetX:F0},{targetY:F0}).");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not add a viewport to '{layoutName}': {ex.Message}");
            }
        }

        /// <summary>Writes the source sheet's paperspace — title block, borders, annotation —
        /// out to a standalone temp DWG that can then be inserted into the merged sheet.
        ///
        /// Uses the typed <c>Document.WBlock(file, selectionSet)</c> with a selection set built
        /// by "select everything in the current space". That combination is the point: it never
        /// passes an object array over COM, which is exactly where CopyObjects failed
        /// ("Invalid object array" — it needs a real SAFEARRAY of VT_DISPATCH that C# late
        /// binding cannot construct). Floating viewports are not meaningfully wblock-able and
        /// simply don't come across; the merged sheet gets its own viewport instead.</summary>
        private static bool TryWblockPaperSpace(dynamic srcDoc, string srcLayoutName, string outFile, string srcLabel)
        {
            const int acSelectionSetCrossing = 1;
            const string setName = "STING_PS_SET";
            dynamic selectionSet = null;
            try
            {
                // Make the sheet's paperspace current — selection always acts on the current
                // space — and CONFIRM it took. Without this check a failed switch silently
                // selects model space instead.
                ComRetry(() => srcDoc.SetVariable("TILEMODE", 0));
                if (!string.IsNullOrWhiteSpace(srcLayoutName))
                    ComRetry(() => srcDoc.SetVariable("CTAB", srcLayoutName));

                int tileMode = Convert.ToInt32(ComRetry(() => srcDoc.GetVariable("TILEMODE")));
                if (tileMode != 0)
                {
                    StingLog.Warn($"DwgMerger.Merge: '{srcLabel}' would not switch to paper space (TILEMODE={tileMode}) — " +
                                  "skipping the title block rather than copying model geometry onto the sheet.");
                    return false;
                }

                try
                {
                    dynamic stale = ComRetry(() => srcDoc.SelectionSets.Item(setName));
                    if (stale != null) ComRetry(() => stale.Delete());
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                selectionSet = ComRetry(() => srcDoc.SelectionSets.Add(setName));

                // A crossing window confined to the current space. acSelectionSetAll was used
                // here originally and is the bug that made every sheet look empty: it selects
                // the whole DATABASE regardless of current space, so the wblock captured model
                // geometry (6499 entities spanning 105 metres) instead of the 36-entity,
                // paper-scale title block — and inserting that onto an A1 sheet put a drawing
                // 125x the size of the page almost entirely off it.
                var lo = new double[] { -1_000_000, -1_000_000, 0 };
                var hi = new double[] {  1_000_000,  1_000_000, 0 };
                ComRetry(() => selectionSet.Select(acSelectionSetCrossing, lo, hi));

                int count = (int)ComRetry(() => selectionSet.Count);
                if (count == 0)
                {
                    StingLog.Warn($"DwgMerger.Merge: '{srcLabel}' paperspace selected 0 entities — no title block to carry over.");
                    return false;
                }

                ComRetry(() => srcDoc.WBlock(outFile, selectionSet));
                bool ok = File.Exists(outFile);
                StingLog.Info($"DwgMerger.Merge: wblocked {count} paperspace entity(ies) from '{srcLabel}' → {(ok ? "ok" : "no file produced")}.");
                return ok;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not wblock the paperspace of '{srcLabel}': {ex.Message}");
                return false;
            }
            finally
            {
                try { if (selectionSet != null) ComRetry(() => selectionSet.Delete()); }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
        }

        /// <summary>Fills a freshly-created sheet: drops the source's title block artwork in at
        /// the paper origin (if it was captured), then adds the viewport. Everything goes
        /// through PaperSpace after switching the current tab with TILEMODE/CTAB — the
        /// ActiveLayout property refuses to work in this out-of-process automation context.</summary>
        private static void PopulateSheet(dynamic doc, string layoutName, string sheetContentFile,
            List<SourceViewportInfo> viewports, double targetX, double targetY)
        {
            try
            {
                ComRetry(() => doc.SetVariable("TILEMODE", 0));   // 0 = paper space
                ComRetry(() => doc.SetVariable("CTAB", layoutName));
                dynamic paperSpace = ComRetry(() => doc.PaperSpace);

                if (sheetContentFile != null)
                {
                    try
                    {
                        ComRetry(() => paperSpace.InsertBlock(new double[] { 0, 0, 0 }, sheetContentFile, 1.0, 1.0, 1.0, 0.0));
                        StingLog.Info($"DwgMerger.Merge: inserted the source sheet content into '{layoutName}'.");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"DwgMerger.Merge: could not insert the sheet content into '{layoutName}': {ex.Message}");
                    }
                }

                // Clear AutoCAD's auto-created default viewport before adding the real frames,
                // otherwise it survives as a stray empty box over the sheet.
                int removed = 0;
                foreach (dynamic ent in paperSpace)
                {
                    string type = "";
                    try { type = (string)ComRetry(() => ent.ObjectName); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (type != "AcDbViewport") continue;
                    try { ComRetry(() => ent.Delete()); removed++; }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                if (removed > 0)
                    StingLog.Info($"DwgMerger.Merge: cleared {removed} default viewport frame(s) from '{layoutName}'.");

                // One viewport per view frame on the original sheet — the paperspace copied
                // above is only the title block, so these are what actually show the drawing.
                foreach (var vp in viewports)
                    AddViewportForSheet(paperSpace, layoutName, vp, targetX, targetY);
                StingLog.Info($"DwgMerger.Merge: '{layoutName}' populated with {viewports.Count} viewport(s).");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not populate sheet '{layoutName}': {ex.Message}");
            }
        }

        /// <summary>Sets the new sheet's plotter + paper size to match the source's REAL paper
        /// dimensions.
        ///
        /// Deliberately does NOT just copy the source's CanonicalMediaName: Revit's DWG export
        /// doesn't carry a plotter, so AutoCAD assigns whatever system printer is default (here
        /// "PDF24") and its media name falls back to that printer's default — the source
        /// reported media 'psk:ISOA4' while its actual layout measured 841x594mm, i.e. a true
        /// A1. Copying the name reproduced the A4 lie; measuring the layout and looking up a
        /// media of that size reproduces the sheet. Tries the source's own plotter first, then
        /// standard Autodesk PC3s, since a media list is per-plotter and a system printer often
        /// has no large ISO sizes at all.</summary>
        private static void ApplyPageSetup(dynamic layout, SourcePageSetup setup, string layoutName)
        {
            string isoLabel = IsoLabelFor(setup.WidthMm, setup.HeightMm);
            var configs = new List<string>();
            if (!string.IsNullOrWhiteSpace(setup.ConfigName)) configs.Add(setup.ConfigName);
            configs.Add("DWG To PDF.pc3");
            configs.Add("DWFx ePlot (XPS Compatible).pc3");

            foreach (var config in configs)
            {
                try { ComRetry(() => layout.ConfigName = config); }
                catch (Exception ex)
                {
                    StingLog.Warn($"DwgMerger.Merge: plotter '{config}' unavailable for '{layoutName}': {ex.Message}");
                    continue;
                }

                string media = PickMediaNameForSize(layout, setup.WidthMm, setup.HeightMm, isoLabel);
                if (media == null) continue;

                try
                {
                    ComRetry(() => layout.CanonicalMediaName = media);

                    // Verify by measuring, don't trust the name. A media name is just a string
                    // and a sloppy match can select something entirely the wrong size (an
                    // earlier substring search for "A1" happily matched "NorthAmerica11x17",
                    // giving ANSI B sheets for A1 drawings).
                    double gotW = 0, gotH = 0;
                    try
                    {
                        gotW = (double)ComRetry(() => layout.Width);
                        gotH = (double)ComRetry(() => layout.Height);
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                    bool matches = gotW <= 0 || gotH <= 0 ||
                                   (Math.Abs(Math.Max(gotW, gotH) - Math.Max(setup.WidthMm, setup.HeightMm)) < 15 &&
                                    Math.Abs(Math.Min(gotW, gotH) - Math.Min(setup.WidthMm, setup.HeightMm)) < 15);

                    if (matches)
                    {
                        ApplyPlotRotation(layout, media, setup, layoutName);
                        StingLog.Info($"DwgMerger.Merge: '{layoutName}' paper set to '{media}' via plotter '{config}' " +
                                      $"→ measured {gotW:F0}x{gotH:F0}mm (source {setup.WidthMm}x{setup.HeightMm}mm ≈ {isoLabel ?? "custom"}).");
                        return;
                    }

                    StingLog.Warn($"DwgMerger.Merge: media '{media}' on plotter '{config}' measured {gotW:F0}x{gotH:F0}mm, " +
                                  $"not the {setup.WidthMm}x{setup.HeightMm}mm wanted — trying the next plotter.");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"DwgMerger.Merge: could not apply media '{media}' on '{layoutName}': {ex.Message}");
                }
            }

            StingLog.Warn($"DwgMerger.Merge: no plotter offered a {setup.WidthMm}x{setup.HeightMm}mm " +
                          $"({isoLabel ?? "custom"}) paper for '{layoutName}' — it keeps the template default size.");
        }

        /// <summary>Finds a canonical media name on the layout's CURRENT plotter matching the
        /// given millimetre size — first by both dimensions appearing in the name, then by ISO
        /// label (A0..A4). Media lists are per-plotter, so this must run after ConfigName is
        /// set.</summary>
        private static string PickMediaNameForSize(dynamic layout, double widthMm, double heightMm, string isoLabel)
        {
            try
            {
                var names = new List<string>();
                dynamic mediaNames = ComRetry(() => layout.GetCanonicalMediaNames());
                foreach (var n in mediaNames)
                {
                    string s = Convert.ToString(n);
                    if (!string.IsNullOrEmpty(s)) names.Add(s);
                }
                if (names.Count == 0) return null;

                string longSide  = ((int)Math.Round(Math.Max(widthMm, heightMm))).ToString();
                string shortSide = ((int)Math.Round(Math.Min(widthMm, heightMm))).ToString();

                // Dimensions in the name are the most trustworthy signal (e.g.
                // "ISO_full_bleed_A1_(841.00_x_594.00_MM)").
                var bySize = names.Where(n => n.Contains(longSide) && n.Contains(shortSide)).ToList();
                if (bySize.Count > 0)
                {
                    // Prefer a media whose own width x height ORDER matches the sheet's
                    // orientation. AutoCAD ships both "ISO_A1_(594.00_x_841.00_MM)" (portrait)
                    // and landscape variants, and both contain the same two numbers — picking
                    // the portrait one for a landscape sheet left the title block hanging
                    // ~260mm off the side of the page.
                    bool wantLandscape = widthMm > heightMm;
                    var oriented = bySize.FirstOrDefault(n =>
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(n, @"(\d+(?:\.\d+)?)\s*_?[xX]_?\s*(\d+(?:\.\d+)?)");
                        if (!m.Success) return false;
                        double a = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        double b = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        return (a > b) == wantLandscape;
                    });
                    return oriented ?? bySize[0];
                }

                if (!string.IsNullOrEmpty(isoLabel))
                {
                    // Anchored patterns only. A bare "contains A1" test also matches
                    // "psk:NorthAmerica11x17" (…Americ-A-1-1x17), which is how A1 sheets were
                    // silently coming out as ANSI B.
                    string[] patterns =
                    {
                        "ISO" + isoLabel,          // psk:ISOA1
                        "ISO_" + isoLabel + "_",   // ISO_A1_(...)
                        "_" + isoLabel + "_",      // ..._A1_...
                        isoLabel + "_(",           // A1_(841.00_x_594.00_MM)
                    };
                    foreach (var pattern in patterns)
                    {
                        var hit = names.FirstOrDefault(n => n.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (hit != null) return hit;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not enumerate media names: {ex.Message}");
            }
            return null;
        }

        /// <summary>Makes a layout current and switches every viewport on it to ON. Needed as a
        /// final pass: inspection of a merged file showed only the last-processed layout kept
        /// ViewportOn = true, with earlier sheets saved holding their viewports off.</summary>
        private static void TurnOnViewports(dynamic doc, string layoutName)
        {
            try
            {
                ComRetry(() => doc.SetVariable("TILEMODE", 0));
                ComRetry(() => doc.SetVariable("CTAB", layoutName));
                dynamic paperSpace = ComRetry(() => doc.PaperSpace);

                int on = 0;
                foreach (dynamic ent in paperSpace)
                {
                    string type = "";
                    try { type = (string)ComRetry(() => ent.ObjectName); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    if (type != "AcDbViewport") continue;
                    try { ComRetry(() => ent.Display(true)); on++; }
                    catch (Exception ex) { StingLog.Warn($"DwgMerger.Merge: could not switch on a viewport in '{layoutName}': {ex.Message}"); }
                }
                StingLog.Info($"DwgMerger.Merge: switched on {on} viewport(s) in '{layoutName}'.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: final viewport pass failed for '{layoutName}': {ex.Message}");
            }
        }

        /// <summary>Rotates the plot 90° when the chosen media is portrait but the sheet is
        /// landscape (or vice versa).
        ///
        /// AutoCAD's ISO media are defined portrait — "ISO_A1_(594.00_x_841.00_MM)" is 594 wide
        /// by 841 tall — while a landscape A1 sheet's content is 856 wide by 594 tall. Without
        /// this the title block ran ~260mm off the side of the page, which is what "the title
        /// block and viewports are far away from the paper" actually was: right coordinates,
        /// wrong page orientation.</summary>
        private static void ApplyPlotRotation(dynamic layout, string mediaName, SourcePageSetup setup, string layoutName)
        {
            const int ac0degrees = 0, ac90degrees = 1;
            try
            {
                var dims = System.Text.RegularExpressions.Regex.Match(
                    mediaName ?? "", @"(\d+(?:\.\d+)?)\s*_?[xX]_?\s*(\d+(?:\.\d+)?)");
                if (!dims.Success) return;

                double mediaW = double.Parse(dims.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                double mediaH = double.Parse(dims.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);

                bool sheetIsLandscape = setup.WidthMm > setup.HeightMm;
                bool mediaIsLandscape = mediaW > mediaH;
                if (sheetIsLandscape == mediaIsLandscape) return;

                ComRetry(() => layout.PlotRotation = ac90degrees);
                StingLog.Info($"DwgMerger.Merge: rotated '{layoutName}' 90° — media '{mediaName}' is " +
                              $"{(mediaIsLandscape ? "landscape" : "portrait")} but the sheet is " +
                              $"{(sheetIsLandscape ? "landscape" : "portrait")}.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DwgMerger.Merge: could not set plot rotation on '{layoutName}': {ex.Message}");
                try { ComRetry(() => layout.PlotRotation = ac0degrees); } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            }
        }

        /// <summary>ISO paper label for a millimetre size, orientation-independent.</summary>
        private static string IsoLabelFor(double a, double b)
        {
            double w = Math.Max(a, b), h = Math.Min(a, b);
            (string Name, double W, double H)[] sizes =
            {
                ("A0", 1189, 841), ("A1", 841, 594), ("A2", 594, 420), ("A3", 420, 297), ("A4", 297, 210),
            };
            foreach (var s in sizes)
                if (Math.Abs(w - s.W) < 12 && Math.Abs(h - s.H) < 12) return s.Name;
            return null;
        }

        private static dynamic FindLayoutByName(dynamic doc, string name)
        {
            dynamic layouts = ComRetry(() => doc.Layouts);
            int n = (int)ComRetry(() => layouts.Count);
            for (int i = 0; i < n; i++)
            {
                dynamic layout = ComRetry(() => layouts.Item(i));
                if (string.Equals((string)ComRetry(() => layout.Name), name, StringComparison.OrdinalIgnoreCase))
                    return layout;
            }
            return null;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>AutoCAD's Application COM object exposes its main window handle via HWND;
        /// resolve that to a process id so a stuck automation can be killed by PID rather than
        /// left running invisibly in the background forever.</summary>
        private static int TryGetProcessId(dynamic acadApp)
        {
            try
            {
                IntPtr hwnd = new IntPtr((int)ComRetry(() => acadApp.HWND));
                GetWindowThreadProcessId(hwnd, out uint pid);
                return pid == 0 ? -1 : (int)pid;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return -1; }
        }

        private static void TryKillProcess(int pid)
        {
            if (pid <= 0) return;
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Kill();
                StingLog.Warn($"DwgMerger.Merge: killed stuck AutoCAD process (PID {pid}).");
            }
            catch (Exception ex) { StingLog.Warn($"DwgMerger.Merge: could not kill AutoCAD process (PID {pid}): {ex.Message}"); }
        }

        /// <summary>Snapshots every current layout name (excluding "Model") so a later call can
        /// tell "existed before this specific import" apart from "genuinely new from it."</summary>
        private static HashSet<string> GetAllLayoutNames(dynamic doc)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dynamic layouts = ComRetry(() => doc.Layouts);
            int n = (int)ComRetry(() => layouts.Count);
            for (int i = 0; i < n; i++)
            {
                dynamic layout = ComRetry(() => layouts.Item(i));
                string name = (string)ComRetry(() => layout.Name);
                if (!name.Equals("Model", StringComparison.OrdinalIgnoreCase))
                    names.Add(name);
            }
            return names;
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
