// StampDrawingTypeChecksums — writes corporate-lock checksums into the shipped
// drawing-type catalogue.
//
// Why this exists: DrawingTypeRegistry.ComputeChecksums already implements
// corporate-lock drift detection — it hashes each corporate DrawingType and,
// when a shipped checksum disagrees, warns and flips that type's origin to
// "project" so downstream code knows it is no longer canonical. The shipped
// STING_DRAWING_TYPES.json has never carried checksum fields, so that whole
// branch has been inert: a hand-edit to the corporate baseline is silently
// accepted as corporate. This tool stamps the missing values.
//
// Why it is C# and not Python: the hash is over
// JsonConvert.SerializeObject(drawingType, Formatting.None), i.e. the C#
// object's serialisation, not the file's bytes. Reproducing it elsewhere would
// mean re-implementing Newtonsoft's property ordering, every
// NullValueHandling attribute and every POCO default — and a WRONG hash is
// worse than no hash, because it flips all 93 types to "project" on first load
// and disables the lock completely. Linking the model source is the only way to
// stay correct as DrawingType evolves.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using StingTools.Core.Drawing;

namespace StingTools.Tools.StampChecksums
{
    internal static class Program
    {
        private const string DataRelative = "StingTools/Data/STING_DRAWING_TYPES.json";

        private static int Main(string[] args)
        {
            bool checkOnly = args.Any(a =>
                string.Equals(a, "--check", StringComparison.OrdinalIgnoreCase));

            string path = LocateCatalogue();
            if (path == null)
            {
                Console.Error.WriteLine(
                    $"Could not find {DataRelative}. Run from the repository root.");
                return 2;
            }

            string raw = File.ReadAllText(path);
            bool bom = HasUtf8Bom(path);

            var lib = JsonConvert.DeserializeObject<DrawingTypeLibrary>(raw);
            if (lib?.DrawingTypes == null || lib.DrawingTypes.Count == 0)
            {
                Console.Error.WriteLine("Catalogue deserialised to no drawing types.");
                return 2;
            }

            // Mirror DrawingTypeRegistry.LoadCorporate: an entry with no origin
            // is corporate. ComputeChecksums only hashes corporate entries, so
            // getting this wrong would skip them.
            foreach (var t in lib.DrawingTypes)
                if (string.IsNullOrEmpty(t.Origin)) t.Origin = "corporate";

            int stamped = 0, unchanged = 0, drifted = 0, skipped = 0;

            foreach (var t in lib.DrawingTypes)
            {
                if (!string.Equals(t.Origin, "corporate", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                string shipped = t.Checksum;
                string actual = ComputeChecksum(t);

                if (string.Equals(shipped, actual, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                if (!string.IsNullOrEmpty(shipped))
                {
                    drifted++;
                    Console.WriteLine($"DRIFT   {t.Id}");
                    Console.WriteLine($"        shipped {shipped}");
                    Console.WriteLine($"        actual  {actual}");
                }
                else
                {
                    Console.WriteLine($"stamp   {t.Id}  {actual}");
                }

                if (!checkOnly)
                {
                    raw = WriteChecksum(raw, t.Id, actual);
                    stamped++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"corporate types : {lib.DrawingTypes.Count - skipped}");
            Console.WriteLine($"already correct : {unchanged}");
            Console.WriteLine($"drifted         : {drifted}");
            Console.WriteLine($"project (skipped): {skipped}");

            if (checkOnly)
            {
                int bad = drifted + (lib.DrawingTypes.Count - skipped - unchanged - drifted);
                if (bad > 0)
                {
                    Console.Error.WriteLine(
                        $"--check: {bad} drawing type(s) have a missing or stale checksum. " +
                        "Run without --check to restamp.");
                    return 1;
                }
                Console.WriteLine("--check: every corporate drawing type carries a correct checksum.");
                return 0;
            }

            if (stamped == 0)
            {
                Console.WriteLine("Nothing to write.");
                return 0;
            }

            File.WriteAllText(path, raw, new UTF8Encoding(bom));
            Console.WriteLine($"wrote {stamped} checksum(s) to {path}");

            // Re-read and re-hash so a stamping bug fails here rather than in Revit.
            var verify = JsonConvert.DeserializeObject<DrawingTypeLibrary>(File.ReadAllText(path));
            foreach (var t in verify.DrawingTypes)
            {
                if (string.IsNullOrEmpty(t.Origin)) t.Origin = "corporate";
                if (!string.Equals(t.Origin, "corporate", StringComparison.OrdinalIgnoreCase)) continue;
                string shipped = t.Checksum;
                string actual = ComputeChecksum(t);
                if (!string.Equals(shipped, actual, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        $"VERIFY FAILED for '{t.Id}': shipped={shipped} actual={actual}");
                    return 3;
                }
            }
            Console.WriteLine("verified: every stamped checksum round-trips.");
            return 0;
        }

        /// <summary>
        /// Byte-for-byte the calculation in DrawingTypeRegistry.ComputeChecksums:
        /// null the checksum, serialise with Formatting.None, SHA-256, lower-case hex.
        /// </summary>
        private static string ComputeChecksum(DrawingType t)
        {
            string prior = t.Checksum;
            try
            {
                t.Checksum = null;
                string json = JsonConvert.SerializeObject(t, Formatting.None);
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            finally
            {
                t.Checksum = prior;
            }
        }

        /// <summary>
        /// Insert or replace one drawing type's "checksum" line in the raw text.
        /// Edits the text rather than re-serialising the library: the shipped file
        /// mixes raw UTF-8 em-dashes with \uXXXX escapes, so a round-trip through
        /// JsonConvert would renormalise every such string and turn a 93-line
        /// change into a whole-file rewrite.
        /// </summary>
        private static string WriteChecksum(string raw, string id, string checksum)
        {
            (int start, int end) = BlockSpan(raw, id);
            string seg = raw.Substring(start, end - start);
            string nl = seg.Contains("\r\n") ? "\r\n" : "\n";

            // Anchor after "origin", else "name", else "id" — all three are
            // authored at the top of every entry.
            string anchor = FindLine(seg, "\"origin\":")
                         ?? FindLine(seg, "\"name\":")
                         ?? FindLine(seg, "\"id\":");
            if (anchor == null)
                throw new InvalidOperationException($"No anchor line in '{id}'.");

            string existing = FindLine(seg, "\"checksum\":");
            string indent = new string(' ', anchor.Length - anchor.TrimStart().Length);
            string line = $"{indent}\"checksum\": \"{checksum}\",";

            seg = existing != null
                ? seg.Replace(existing, line)
                : seg.Replace(anchor, anchor + nl + line);

            return raw.Substring(0, start) + seg + raw.Substring(end);
        }

        private static string FindLine(string segment, string needle)
        {
            foreach (string line in segment.Split('\n'))
            {
                string trimmedOfCr = line.TrimEnd('\r');
                if (trimmedOfCr.TrimStart().StartsWith(needle, StringComparison.Ordinal))
                    return trimmedOfCr;
            }
            return null;
        }

        /// <summary>Character span of the JSON object whose "id" is <paramref name="id"/>.</summary>
        private static (int, int) BlockSpan(string raw, string id)
        {
            string needle = "\"id\": \"" + id + "\"";
            int at = raw.IndexOf(needle, StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException($"id not found: {id}");

            int start = raw.LastIndexOf('{', at);
            int depth = 0;
            for (int i = start; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '"')
                {
                    i++;
                    while (raw[i] != '"' || raw[i - 1] == '\\') i++;
                }
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return (start, i + 1);
                }
            }
            throw new InvalidOperationException($"Unterminated object for id: {id}");
        }

        private static string LocateCatalogue()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, DataRelative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static bool HasUtf8Bom(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var b = new byte[3];
                return fs.Read(b, 0, 3) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
            }
        }
    }
}
