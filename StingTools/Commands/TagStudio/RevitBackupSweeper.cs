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

                foreach (var bk in Directory.GetFiles(dir, stem + ".[0-9][0-9][0-9][0-9].rfa"))
                {
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
    }
}
