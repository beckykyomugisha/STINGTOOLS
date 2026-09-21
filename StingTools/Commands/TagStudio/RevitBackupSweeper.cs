// RevitBackupSweeper - delete the .0001.rfa Revit writes beside a saved family.
//
// SaveAsOptions.MaximumBackups CANNOT be set to zero. Revit requires at least 1,
// so a backup file is ALWAYS written, whatever the option says. This tree carried
// a comment claiming MaximumBackups = 1 prevented them; the 206-family parameter
// run on 2026-09-21 produced 141 backups and disproved it.
//
// Left alone they are not harmless litter: anything that enumerates *.rfa in the
// tag library counts them as families. That is why a 206-family library once
// reported 207, and 212 after several runs, and why 141 of them were nearly
// committed to git. Both fix-commands take their own pre-change copies into a
// _pre* folder, so Revit's backups are pure duplication.
//
// One copy of this logic, called by every command that saves a family in place.

using System;
using System.IO;
using System.Linq;
using StingTools.Core;

namespace StingTools.Commands.TagStudio
{
    internal static class RevitBackupSweeper
    {
        /// <summary>
        /// Deletes "&lt;stem&gt;.NNNN.rfa" beside the given family file.
        /// A failed delete is logged and swallowed - it must never fail a family
        /// that was otherwise repaired and saved correctly.
        /// </summary>
        internal static void Sweep(string rfaPath, string logPrefix)
        {
            try
            {
                string dir = Path.GetDirectoryName(rfaPath);
                string stem = Path.GetFileNameWithoutExtension(rfaPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem)) return;

                // ".????.rfa", not ".[0-9][0-9][0-9][0-9].rfa": Directory.GetFiles
                // understands only * and ?, and matches a character class LITERALLY.
                // The class version silently matched nothing on every call - measured
                // 2026-09-21, when a 10-family run left all 10 backups in place and
                // logged no error, because an empty result is not an error.
                // ? matches any character, so IsBackupName does the digit check.
                foreach (var bk in Directory.GetFiles(dir, stem + ".????.rfa"))
                {
                    if (!IsBackupName(bk)) continue;
                    try { File.Delete(bk); }
                    catch (Exception ex)
                    {
                        StingLog.Warn(logPrefix + ": could not delete Revit backup " +
                                      Path.GetFileName(bk) + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn(logPrefix + ": backup sweep failed for " + rfaPath + ": " + ex.Message);
            }
        }

        /// <summary>
        /// True for "Something.0001.rfa" - a four-DIGIT suffix, not merely four
        /// characters. Needed because the ? wildcard cannot express "digit", so a
        /// family legitimately named "Panel.TYPE.rfa" would otherwise be deleted.
        /// </summary>
        internal static bool IsBackupName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? "";
            int dot = name.LastIndexOf('.');
            if (dot < 0 || dot == name.Length - 1) return false;
            string tail = name.Substring(dot + 1);
            return tail.Length == 4 && tail.All(char.IsDigit);
        }


        /// <summary>
        /// Sweeps a whole folder once, after every family document has been closed.
        /// Per-file sweeping runs while the document is still open, and whether Revit
        /// has written its backup by then is a timing assumption; this is not.
        /// It also clears backups left behind by earlier builds. Returns the count.
        /// </summary>
        internal static int SweepFolder(string dir, string logPrefix)
        {
            int n = 0;
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
                foreach (var bk in Directory.GetFiles(dir, "*.rfa", SearchOption.TopDirectoryOnly))
                {
                    if (!IsBackupName(bk)) continue;
                    try { File.Delete(bk); n++; }
                    catch (Exception ex)
                    {
                        StingLog.Warn(logPrefix + ": could not delete Revit backup " +
                                      Path.GetFileName(bk) + ": " + ex.Message);
                    }
                }
                if (n > 0) StingLog.Info(logPrefix + ": removed " + n + " Revit backup file(s) from " + dir);
            }
            catch (Exception ex)
            {
                StingLog.Warn(logPrefix + ": folder backup sweep failed for " + dir + ": " + ex.Message);
            }
            return n;
        }

    }
}
