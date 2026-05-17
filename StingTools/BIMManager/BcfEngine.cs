using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Planscape.Shared.BCF
{
    // ════════════════════════════════════════════════════════════════════════
    //  Phase 95 — BcfEngine
    //
    //  Pure-C# BCF 2.1 serialiser / deserialiser. No Revit API, no Newtonsoft.
    //  The file physically lives at StingTools/BIMManager/BcfEngine.cs so the
    //  Revit plugin can use it directly, and Planscape.Shared.csproj pulls the
    //  same source file via a <Compile Include="..\..\..\StingTools\..."/> link
    //  so the server runs the exact same serialisation code.
    //
    //  BCF 2.1 ZIP structure produced by Export():
    //     bcf.version                       (VersionId="2.1")
    //     {topic-guid}/markup.bcf           (Markup → Topic + Comments)
    //     {topic-guid}/viewpoint.bcfv       (stub OrthogonalCamera at 0,0,10)
    //
    //  Import() reads markup.bcf from every topic folder and maps fields back
    //  onto a CoordIssue. Viewpoints are intentionally ignored on import — the
    //  stub camera carries no useful data to round-trip back into STING.
    //
    //  The engine never throws on malformed input: Import() returns an empty
    //  list, Export() surfaces errors via its return value + IOException.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Round-trippable issue / coordination topic. Shared payload between the
    /// Revit plugin, the Planscape server, and BCF 2.1 consumers (Solibri,
    /// Navisworks, BIMcollab, Revizto, Trimble Connect, ACC).
    /// </summary>
    public sealed class CoordIssue
    {
        /// <summary>BCF topic GUID — stable identifier preserved across round-trips.</summary>
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>Short one-line summary (BCF Topic/Title).</summary>
        public string Title { get; set; } = "";

        /// <summary>Full narrative description (BCF Topic/Description).</summary>
        public string? Description { get; set; }

        /// <summary>STING priority — CRITICAL, HIGH, MEDIUM, LOW, INFO.</summary>
        public string Priority { get; set; } = "MEDIUM";

        /// <summary>STING issue type — RFI, NCR, CLASH, DESIGN, SITE, SNAGGING, CHANGE, RISK, ACTION, COMMENT.</summary>
        public string Type { get; set; } = "RFI";

        /// <summary>STING status — OPEN, IN_PROGRESS, RESPONDED, CLOSED, ACCEPTED, REJECTED, VOID.</summary>
        public string Status { get; set; } = "OPEN";

        /// <summary>Person assigned to resolve (BCF AssignedTo).</summary>
        public string? Assignee { get; set; }

        /// <summary>Person who raised the issue (BCF CreationAuthor).</summary>
        public string? Author { get; set; }

        /// <summary>Creation timestamp (BCF CreationDate, ISO 8601).</summary>
        public DateTime CreationDate { get; set; } = DateTime.UtcNow;

        /// <summary>Free-form topic labels — BCF Topic/Labels/Label*.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Discussion thread on this topic.</summary>
        public List<CoordComment> Comments { get; set; } = new List<CoordComment>();

        /// <summary>Optional reference link (BCF Topic/ReferenceLink) — usually the STING issue code.</summary>
        public string? ReferenceLink { get; set; }

        /// <summary>
        /// Optional pre-built BCF 2.1 viewpoint XML (.bcfv contents). When set,
        /// <see cref="BcfEngine.Export"/> writes this in place of the default
        /// stub camera so downstream viewers (BIMcollab, ACC, Solibri) open
        /// the topic with a real spatial anchor. Build via
        /// <c>StingTools.Core.Clash.BcfViewpointBuilder</c> or any other
        /// source that emits VisualizationInfo XML.
        /// </summary>
        public string? ViewpointBcfvXml { get; set; }
    }

    /// <summary>
    /// A single comment inside a BCF topic's discussion thread.
    /// </summary>
    public sealed class CoordComment
    {
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Pure-C# BCF 2.1 serialiser / deserialiser. Safe to call from any runtime
    /// (Revit plugin, ASP.NET Core, CLI). Uses only <c>System.IO.Compression</c>
    /// and <c>System.Xml.Linq</c> — no external ZIP or XML libraries.
    /// </summary>
    public static class BcfEngine
    {
        // ── Logging hook ───────────────────────────────────────────────────
        //
        // BcfEngine.cs is compiled into Planscape.Shared.dll (via linked
        // <Compile Include>), which has no reference to the StingTools
        // namespace. So we can't call StingTools.Core.StingLog.Warn directly
        // from here without breaking the server/shared build.
        //
        // Instead, expose a nullable Action<string> that the plugin sets once
        // at app startup (StingToolsApp.OnStartup wires it to StingLog.Warn).
        // Server-side callers can leave it null — the silent-catch contract
        // is preserved because Invoke is guarded by the null-conditional.
        public static Action<string>? Warn { get; set; }

        // ── Priority / status / type mappings (kept here so server + plugin share them) ──

        internal static readonly Dictionary<string, string> StingToBcfType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["RFI"]      = "Request",
            ["CLASH"]    = "Clash",
            ["DESIGN"]   = "Issue",
            ["SITE"]     = "Remark",
            ["NCR"]      = "Issue",
            ["SNAGGING"] = "Fault",
            ["CHANGE"]   = "Request",
            ["RISK"]     = "Issue",
            ["ACTION"]   = "Issue",
            ["COMMENT"]  = "Comment",
        };

        internal static readonly Dictionary<string, string> BcfToStingType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Request"] = "RFI",
            ["Clash"]   = "CLASH",
            ["Issue"]   = "DESIGN",
            ["Remark"]  = "SITE",
            ["Fault"]   = "SNAGGING",
            ["Comment"] = "COMMENT",
            ["Error"]   = "NCR",
            ["Warning"] = "RISK",
            ["Info"]    = "COMMENT",
        };

        internal static readonly Dictionary<string, string> StingToBcfPriority = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CRITICAL"] = "Critical",
            ["HIGH"]     = "Major",
            ["MEDIUM"]   = "Normal",
            ["LOW"]      = "Minor",
            ["INFO"]     = "On hold",
        };

        internal static readonly Dictionary<string, string> BcfToStingPriority = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Critical"] = "CRITICAL",
            ["Major"]    = "HIGH",
            ["Normal"]   = "MEDIUM",
            ["Minor"]    = "LOW",
            ["On hold"]  = "INFO",
        };

        // BCF TopicStatus is Active | Resolved | Closed | ReOpened (2.1 extension-defined).
        // Collapse any non-terminal status to "Active" on export and map back to STING OPEN on import.
        private static string StingStatusToBcf(string stingStatus) => (stingStatus ?? "OPEN").ToUpperInvariant() switch
        {
            "CLOSED"    => "Closed",
            "RESOLVED"  => "Resolved",
            "ACCEPTED"  => "Resolved",
            "VOID"      => "Closed",
            _           => "Active",
        };

        private static string BcfStatusToSting(string bcfStatus) => (bcfStatus ?? "Active").Trim() switch
        {
            "Closed"   => "CLOSED",
            "Resolved" => "CLOSED",
            _          => "OPEN",
        };

        // ── Export ─────────────────────────────────────────────────────────

        /// <summary>
        /// Serialise the given issues into a BCF 2.1 .bcfzip at <paramref name="outputPath"/>.
        /// Overwrites any existing file. Uses <see cref="System.IO.Compression.ZipArchive"/> —
        /// no external library. Returns the number of topics written.
        /// </summary>
        /// <exception cref="ArgumentNullException">outputPath is null or empty.</exception>
        /// <exception cref="IOException">Any underlying IO failure; caller should wrap in try/catch + StingLog.Error.</exception>
        public static int Export(IEnumerable<CoordIssue> issues, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException(nameof(outputPath));
            issues ??= Array.Empty<CoordIssue>();

            // Write into a MemoryStream first so a half-written ZIP never clobbers the target.
            using var ms = new MemoryStream();
            int topicCount = 0;

            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                // bcf.version at the archive root.
                WriteXmlEntry(zip, "bcf.version", BuildVersionXml());

                foreach (var issue in issues)
                {
                    if (issue == null) continue;
                    string guid = string.IsNullOrWhiteSpace(issue.Guid)
                        ? System.Guid.NewGuid().ToString()
                        : issue.Guid;

                    WriteXmlEntry(zip, $"{guid}/markup.bcf",     BuildMarkupXml(issue, guid));
                    WriteViewpointEntry(zip, guid, issue);
                    topicCount++;
                }
            }

            ms.Position = 0;
            // Ensure parent directory exists, then overwrite atomically.
            var parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllBytes(outputPath, ms.ToArray());
            return topicCount;
        }

        /// <summary>
        /// Server-friendly overload: serialise directly into a byte[] (for HTTP
        /// <c>File(bytes, "application/zip", ...)</c> responses). Never touches disk.
        /// </summary>
        public static byte[] ExportToBytes(IEnumerable<CoordIssue> issues)
        {
            issues ??= Array.Empty<CoordIssue>();
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteXmlEntry(zip, "bcf.version", BuildVersionXml());
                foreach (var issue in issues)
                {
                    if (issue == null) continue;
                    string guid = string.IsNullOrWhiteSpace(issue.Guid) ? System.Guid.NewGuid().ToString() : issue.Guid;
                    WriteXmlEntry(zip, $"{guid}/markup.bcf",     BuildMarkupXml(issue, guid));
                    WriteViewpointEntry(zip, guid, issue);
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Writes the viewpoint.bcfv entry for a topic. Uses
        /// <see cref="CoordIssue.ViewpointBcfvXml"/> when supplied (caller
        /// built a real spatial viewpoint) and falls back to the stub
        /// camera otherwise.
        /// </summary>
        private static void WriteViewpointEntry(ZipArchive zip, string guid, CoordIssue issue)
        {
            string entry = $"{guid}/viewpoint.bcfv";
            if (!string.IsNullOrWhiteSpace(issue.ViewpointBcfvXml))
            {
                try
                {
                    var doc = XDocument.Parse(issue.ViewpointBcfvXml);
                    WriteXmlEntry(zip, entry, doc);
                    return;
                }
                catch
                {
                    // Caller-supplied XML was malformed; fall through to stub.
                    Warn?.Invoke($"BcfEngine: invalid ViewpointBcfvXml on topic {guid}; using stub.");
                }
            }
            WriteXmlEntry(zip, entry, BuildViewpointStubXml(System.Guid.NewGuid().ToString()));
        }

        // ── Import ─────────────────────────────────────────────────────────

        /// <summary>
        /// Parse a BCF 2.1 .bcfzip at <paramref name="bcfPath"/> into a list of
        /// <see cref="CoordIssue"/>. Never throws — returns an empty list when the
        /// file is missing, unreadable, or not a valid ZIP. Viewpoints are ignored.
        /// </summary>
        public static List<CoordIssue> Import(string bcfPath)
        {
            var result = new List<CoordIssue>();
            if (string.IsNullOrWhiteSpace(bcfPath) || !File.Exists(bcfPath))
                return result;

            try
            {
                using var stream = File.OpenRead(bcfPath);
                return ImportFromStream(stream);
            }
            catch (InvalidDataException) { return result; }   // not a ZIP
            catch (IOException)          { return result; }   // file locked / truncated
            catch (Exception)            { return result; }   // never throw
        }

        /// <summary>
        /// Parse a BCF 2.1 ZIP from any readable stream (used by the server's
        /// <c>IFormFile</c> multipart upload path). Never throws.
        /// </summary>
        public static List<CoordIssue> ImportFromStream(Stream bcfStream)
        {
            var result = new List<CoordIssue>();
            if (bcfStream == null) return result;

            try
            {
                using var zip = new ZipArchive(bcfStream, ZipArchiveMode.Read, leaveOpen: true);
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    CoordIssue? issue = null;
                    try
                    {
                        using var es = entry.Open();
                        var doc = XDocument.Load(es, LoadOptions.None);
                        issue = ParseMarkup(doc);
                    }
                    catch (System.Xml.XmlException) { /* skip malformed topic */ }
                    catch (Exception)               { /* skip malformed topic */ }

                    if (issue != null) result.Add(issue);
                }
            }
            catch (InvalidDataException) { /* not a ZIP — return whatever we parsed */ }
            catch (IOException ioEx)     { Warn?.Invoke($"BcfEngine.Import I/O: {ioEx.Message}"); }
            catch (Exception ex)         { Warn?.Invoke($"BcfEngine.Import: {ex.Message}"); }

            return result;
        }

        // ── XML builders ───────────────────────────────────────────────────

        private static XDocument BuildVersionXml() =>
            new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("Version",
                    new XAttribute("VersionId", "2.1"),
                    new XElement("DetailedVersion", "2.1")));

        private static XDocument BuildMarkupXml(CoordIssue issue, string guid)
        {
            string bcfType     = MapOr(StingToBcfType, issue.Type,     "Issue");
            string bcfPriority = MapOr(StingToBcfPriority, issue.Priority, "Normal");
            string bcfStatus   = StingStatusToBcf(issue.Status);

            // CreationDate: always serialise as round-trip-safe ISO 8601 UTC.
            string creationIso = (issue.CreationDate == default
                ? DateTime.UtcNow
                : issue.CreationDate.ToUniversalTime()).ToString("o", CultureInfo.InvariantCulture);

            var topic = new XElement("Topic",
                new XAttribute("Guid", guid),
                new XAttribute("TopicType", bcfType),
                new XAttribute("TopicStatus", bcfStatus),
                new XElement("ReferenceLink", issue.ReferenceLink ?? ""),
                new XElement("Title", issue.Title ?? ""),
                new XElement("Priority", bcfPriority),
                new XElement("Index", 0),
                new XElement("Labels",
                    (issue.Labels ?? new List<string>())
                        .Where(l => !string.IsNullOrEmpty(l))
                        .Select(l => new XElement("Label", l))),
                new XElement("CreationDate", creationIso),
                new XElement("CreationAuthor", issue.Author ?? ""),
                new XElement("ModifiedDate", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)),
                new XElement("ModifiedAuthor", issue.Author ?? ""),
                new XElement("AssignedTo", issue.Assignee ?? ""),
                new XElement("Description", issue.Description ?? ""),
                // Lossless round-trip hint for STING — echoed back on import.
                new XElement("StingIssueType", issue.Type ?? "COMMENT"));

            var markup = new XElement("Markup",
                new XElement("Header"),
                topic);

            foreach (var c in issue.Comments ?? new List<CoordComment>())
            {
                if (c == null) continue;
                string cguid = string.IsNullOrWhiteSpace(c.Guid) ? System.Guid.NewGuid().ToString() : c.Guid;
                markup.Add(new XElement("Comment",
                    new XAttribute("Guid", cguid),
                    new XElement("Date", c.Date == default
                        ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                        : c.Date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
                    new XElement("Author", c.Author ?? ""),
                    new XElement("Comment", c.Text ?? ""),
                    new XElement("Topic", new XAttribute("Guid", guid))));
            }

            return new XDocument(new XDeclaration("1.0", "UTF-8", null), markup);
        }

        /// <summary>
        /// Stub viewpoint: a single <c>OrthogonalCamera</c> at (0,0,10) looking
        /// down −Z with up +Y. No Revit viewpoint API is invoked; the spec
        /// requires *some* camera per topic so downstream viewers can open the
        /// topic without falling back to a black canvas.
        /// </summary>
        private static XDocument BuildViewpointStubXml(string guid) =>
            new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("VisualizationInfo",
                    new XAttribute("Guid", guid),
                    new XElement("OrthogonalCamera",
                        new XElement("CameraViewPoint",
                            new XElement("X", FmtDouble(0)),
                            new XElement("Y", FmtDouble(0)),
                            new XElement("Z", FmtDouble(10))),
                        new XElement("CameraDirection",
                            new XElement("X", FmtDouble(0)),
                            new XElement("Y", FmtDouble(0)),
                            new XElement("Z", FmtDouble(-1))),
                        new XElement("CameraUpVector",
                            new XElement("X", FmtDouble(0)),
                            new XElement("Y", FmtDouble(1)),
                            new XElement("Z", FmtDouble(0))),
                        new XElement("ViewToWorldScale", FmtDouble(10))),
                    new XElement("Components",
                        new XElement("Selection"),
                        new XElement("Visibility",
                            new XAttribute("DefaultVisibility", "true"),
                            new XElement("Exceptions")))));

        // ── Markup parser ─────────────────────────────────────────────────

        /// <summary>
        /// Parse a markup.bcf XDocument. Tolerant of extra namespaces / unknown
        /// elements — we read by <see cref="XName.LocalName"/>, not the exact
        /// namespace, because different producers (Solibri, Navisworks, ACC)
        /// sometimes omit the buildingSMART xmlns.
        /// </summary>
        private static CoordIssue? ParseMarkup(XDocument doc)
        {
            if (doc?.Root == null) return null;

            var topic = doc.Root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Topic");
            if (topic == null) return null;

            string guid = topic.Attribute("Guid")?.Value ?? System.Guid.NewGuid().ToString();
            string bcfType = topic.Attribute("TopicType")?.Value ?? "Issue";
            string bcfStatus = topic.Attribute("TopicStatus")?.Value ?? "Active";

            // Prefer the STING extension element for lossless round-trip, else map BCF→STING.
            string stingExt = Child(topic, "StingIssueType");
            string stingType = !string.IsNullOrEmpty(stingExt) ? stingExt
                : (BcfToStingType.TryGetValue(bcfType, out var t) ? t : "COMMENT");

            string bcfPriority = Child(topic, "Priority", "Normal");
            string stingPriority = BcfToStingPriority.TryGetValue(bcfPriority, out var p) ? p : "MEDIUM";

            var issue = new CoordIssue
            {
                Guid           = guid,
                Title          = Child(topic, "Title", "(untitled)"),
                Description    = NullIfEmpty(Child(topic, "Description")),
                Priority       = stingPriority,
                Type           = stingType,
                Status         = BcfStatusToSting(bcfStatus),
                Assignee       = NullIfEmpty(Child(topic, "AssignedTo")),
                Author         = NullIfEmpty(Child(topic, "CreationAuthor")),
                CreationDate   = ParseIsoDate(Child(topic, "CreationDate")),
                ReferenceLink  = NullIfEmpty(Child(topic, "ReferenceLink")),
            };

            // Labels
            var labelsEl = topic.Elements().FirstOrDefault(e => e.Name.LocalName == "Labels");
            if (labelsEl != null)
            {
                foreach (var l in labelsEl.Elements().Where(e => e.Name.LocalName == "Label"))
                {
                    if (!string.IsNullOrWhiteSpace(l.Value))
                        issue.Labels.Add(l.Value);
                }
            }

            // Comments live as siblings of <Topic> under <Markup>.
            foreach (var c in doc.Root.Elements().Where(e => e.Name.LocalName == "Comment"))
            {
                string text = Child(c, "Comment");
                if (string.IsNullOrEmpty(text)) continue;
                issue.Comments.Add(new CoordComment
                {
                    Guid   = c.Attribute("Guid")?.Value ?? System.Guid.NewGuid().ToString(),
                    Text   = text,
                    Author = Child(c, "Author"),
                    Date   = ParseIsoDate(Child(c, "Date")),
                });
            }

            return issue;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static void WriteXmlEntry(ZipArchive zip, string path, XDocument xml)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var s = entry.Open();
            // Write as UTF-8 with the XDeclaration to match BCF 2.1 producer behaviour.
            using var writer = new StreamWriter(s, new UTF8Encoding(false));
            xml.Save(writer);
        }

        private static string MapOr(Dictionary<string, string> map, string key, string fallback) =>
            !string.IsNullOrEmpty(key) && map.TryGetValue(key, out var v) ? v : fallback;

        private static string Child(XElement parent, string localName, string fallback = "")
        {
            var el = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
            return el?.Value ?? fallback;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

        private static DateTime ParseIsoDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.UtcNow;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return DateTime.UtcNow;
        }

        private static string FmtDouble(double d) => d.ToString("F6", CultureInfo.InvariantCulture);
    }
}
