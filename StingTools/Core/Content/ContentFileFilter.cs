// Revit-free: which .rfa files in a content library are candidates to load.
//
// Library folders hold more than families to load: Revit's own backups
// ("… .0001.rfa"), families Promote Library moved to _retired, and the tag
// families under Tags. The fixture resolver searches whole roots recursively by
// category name, so without this a request for "Audio Visual Devices" matched
// "STING - Audio Visual Devices Tag.rfa" and loaded a tag as the fixture.

using System;
using System.IO;
using System.Linq;

namespace StingTools.Core.Content
{
    public static class ContentFileFilter
    {
        /// <summary>
        /// True when <paramref name="path"/> may be loaded to satisfy a request for
        /// <paramref name="category"/>: not a Revit backup, not under a _retired
        /// folder, and not a tag family unless a tag category was asked for.
        /// </summary>
        public static bool IsLoadCandidate(string path, string category)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (RevitBackupFiles.IsBackup(path)) return false;
            if (UnderFolder(path, TagLibraryPromotion.RetiredFolderName)) return false;

            bool wantsTag = !string.IsNullOrEmpty(category) &&
                            category.Trim().EndsWith("Tags", StringComparison.OrdinalIgnoreCase);
            if (!wantsTag && IsTagFamily(path)) return false;
            return true;
        }

        /// <summary>A STING tag family: named "… Tag", or kept in a Tags or TagFamilies folder.</summary>
        public static bool IsTagFamily(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string stem = Path.GetFileNameWithoutExtension(path.Trim()) ?? "";
            return stem.EndsWith(" Tag", StringComparison.OrdinalIgnoreCase) ||
                   UnderFolder(path, "Tags") || UnderFolder(path, "TagFamilies");
        }

        /// <summary>True when any folder in the path is named <paramref name="folder"/>.</summary>
        public static bool UnderFolder(string path, string folder)
        {
            // Split by hand: Path.GetDirectoryName ignores '\\' off Windows.
            var segs = (path ?? "").Split('\\', '/');
            return segs.Take(segs.Length - 1)
                       .Any(seg => string.Equals(seg, folder, StringComparison.OrdinalIgnoreCase));
        }
    }
}
