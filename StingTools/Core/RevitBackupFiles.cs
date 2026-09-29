// Revit-free: Revit leaves "<name>.0001.rfa" beside a family on every save. Loading
// one mints a second family named "<name>.0001", so libraries must skip them.

using System.IO;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class RevitBackupFiles
    {
        private static readonly Regex Backup = new Regex(@"\.\d{4}$", RegexOptions.CultureInvariant);

        /// <summary>True for a Revit backup such as "STING - Duct Tag.0001.rfa".</summary>
        public static bool IsBackup(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return Backup.IsMatch(Path.GetFileNameWithoutExtension(path.Trim()));
        }
    }
}
