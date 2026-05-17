using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterOdaConverter — Method B for DWG multi-layout output.
    //
    //  Honest scope note (read this before assuming this class "merges layouts"):
    //
    //  The FREE ODA File Converter CLI is a one-file-in, one-file-out
    //  version-normaliser — it cannot merge multiple DWGs into one DWG
    //  with N layout tabs. True ODA-side layout merging requires the paid
    //  Teigha / Drawings SDK, which we don't bundle.
    //
    //  What this Method B actually does:
    //    1. Detect ODAFileConverter.exe on disk (well-known installer paths
    //       + the registry uninstall keys it writes during install).
    //    2. Normalise every staged per-sheet DWG to a single target DWG
    //       version (e.g. AC2018), so downstream consumers running older
    //       AutoCAD/AutoCAD LT versions can open them all.
    //    3. Optionally also emit DXF copies (text format — diffable in git
    //       and consumable by ezdxf/LibreDWG/etc. for offline merging).
    //    4. Drop a merge_manifest.json next to the staged files that
    //       declares the intended layout structure (source DWG → target
    //       layout name + order). A downstream merger (Teigha, ezdxf script,
    //       AutoCAD COM on a different machine) can then consume it.
    //
    //  When AutoCAD COM (Method A) is unavailable AND this Method B runs,
    //  the engine still falls back to "individual files" semantics — the
    //  user gets a clean folder of staged DWGs at the right version, plus
    //  the manifest, instead of a single multi-layout DWG.
    // ════════════════════════════════════════════════════════════════════════════

    internal static class ExportCenterOdaConverter
    {
        private static string _cachedExePath;
        private static bool _probed;

        /// <summary>Path to ODAFileConverter.exe, or null if not installed.</summary>
        internal static string FindExecutable()
        {
            if (_probed) return _cachedExePath;
            _probed = true;

            // 1) Honour an explicit override stored in ExportCenterState.
            try
            {
                var st = ExportCenterEngine.LoadState();
                if (!string.IsNullOrEmpty(st.OdaLibraryPath) && File.Exists(st.OdaLibraryPath))
                    return _cachedExePath = st.OdaLibraryPath;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // 2) Well-known installer paths — ODA names the dir "ODA File Converter <version>".
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            foreach (var root in roots.Where(Directory.Exists))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root, "ODA File Converter*"))
                    {
                        var exe = Path.Combine(dir, "ODAFileConverter.exe");
                        if (File.Exists(exe)) return _cachedExePath = exe;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }

            // 3) Registry — ODA's installer drops an Uninstall entry that includes InstallLocation.
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (key != null)
                {
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var s = key.OpenSubKey(sub);
                        var name = s?.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(name) ||
                            !name.StartsWith("ODA File Converter", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var loc = s.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc))
                        {
                            var exe = Path.Combine(loc, "ODAFileConverter.exe");
                            if (File.Exists(exe)) return _cachedExePath = exe;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            return _cachedExePath = null;
        }

        internal static bool IsAvailable() => !string.IsNullOrEmpty(FindExecutable());

        /// <summary>
        /// Normalise a folder of DWGs to a single target version using ODA File
        /// Converter's CLI signature. Returns the count of successfully-converted
        /// files. The CLI is invoked once for the whole input folder.
        /// </summary>
        /// <param name="inputFolder">Folder containing per-sheet DWGs.</param>
        /// <param name="outputFolder">Target folder (created if missing).</param>
        /// <param name="targetVersion">"ACAD2018" / "ACAD2013" / "ACAD2010" / "ACAD2007" / "ACAD2004".</param>
        /// <param name="targetFormat">"DWG" or "DXF".</param>
        internal static int Convert(string inputFolder, string outputFolder, string targetVersion, string targetFormat)
        {
            string exe = FindExecutable();
            if (string.IsNullOrEmpty(exe))
            {
                StingLog.Warn("OdaConverter.Convert: ODAFileConverter.exe not found.");
                return 0;
            }
            if (!Directory.Exists(inputFolder))
            {
                StingLog.Warn($"OdaConverter.Convert: input folder missing — {inputFolder}");
                return 0;
            }
            Directory.CreateDirectory(outputFolder);

            // CLI signature (positional args, no flags):
            //   ODAFileConverter <inFolder> <outFolder> <outVer> <outFormat> <recurse> <audit> [filter]
            //   outVer:    ACAD9  ACAD10  ACAD12  ACAD13  ACAD14  ACAD2000  ACAD2004  ACAD2007  ACAD2010  ACAD2013  ACAD2018
            //   outFormat: DWG | DXF | DXB
            //   recurse:   0 | 1
            //   audit:     0 | 1
            //   filter:    optional file mask (default *.DWG;*.DXF)
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{inputFolder}\" \"{outputFolder}\" {targetVersion} {targetFormat} 0 1 \"*.DWG\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return 0;

                // The ODA tool can be slow on large folders; cap at 10 minutes.
                if (!proc.WaitForExit(10 * 60 * 1000))
                {
                    try { proc.Kill(true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    StingLog.Warn("OdaConverter.Convert timed out after 10 minutes.");
                    return 0;
                }

                if (proc.ExitCode != 0)
                {
                    string stderr = proc.StandardError.ReadToEnd();
                    StingLog.Warn($"OdaConverter.Convert exit {proc.ExitCode}: {stderr}");
                }

                string ext = "." + targetFormat.ToLowerInvariant();
                int count = Directory.EnumerateFiles(outputFolder, "*" + ext).Count();
                StingLog.Info($"OdaConverter.Convert wrote {count} {targetFormat} files to {outputFolder}.");
                return count;
            }
            catch (Exception ex)
            {
                StingLog.Warn("OdaConverter.Convert: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// Map STING's DWG version constant ("AC2018" etc.) onto the literal
        /// ODA CLI version flag ("ACAD2018" etc.).
        /// </summary>
        internal static string MapStingVersionToOda(string stingVersion) => stingVersion switch
        {
            "AC2018" => "ACAD2018",
            "AC2013" => "ACAD2013",
            "AC2010" => "ACAD2010",
            "AC2007" => "ACAD2007",
            "AC2004" => "ACAD2004",
            _        => "ACAD2018",
        };

        /// <summary>
        /// Write a merge manifest describing how a downstream merger should
        /// fold the staged per-sheet DWGs into a single multi-layout DWG.
        /// Format is intentionally simple JSON, version-stamped.
        /// </summary>
        internal static string WriteMergeManifest(
            string folder, List<string> sourceDwgs, List<string> layoutNames,
            string proposedOutputDwg, string targetDwgVersion)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var manifest = new
                {
                    schema = "sting-export-centre/dwg-merge-manifest/1",
                    generatedUtc = DateTime.UtcNow.ToString("o"),
                    proposedOutput = proposedOutputDwg,
                    targetDwgVersion,
                    note = "Each entry is a per-sheet DWG that should become one layout tab " +
                           "in the merged output. The free ODA File Converter cannot perform " +
                           "this merge — use AutoCAD COM, Teigha SDK, or an ezdxf script.",
                    layouts = sourceDwgs.Select((src, i) => new
                    {
                        index = i,
                        sourceDwg = src,
                        layoutName = i < layoutNames.Count ? layoutNames[i] : Path.GetFileNameWithoutExtension(src),
                    }),
                };
                string path = Path.Combine(folder, "merge_manifest.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
                StingLog.Info($"OdaConverter.WriteMergeManifest: {path}");
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn("OdaConverter.WriteMergeManifest: " + ex.Message);
                return null;
            }
        }
    }
}
