// BcfMarkupBuilder.cs — pure-logic builder for the BCF 2.1 markup.bcf XML
// document plus the stable per-clash topic GUID. Extracted from
// ClashBcfExportCommand so the BCF writer can be unit-tested without a Revit
// reference. The IExternalCommand class still owns the ZIP layout, snapshot
// rendering, and viewpoint export — those genuinely need Revit.
//
// Stable-GUID guarantee: BCF importers (ACC / Solibri / BIMcollab) dedup
// topics by the Topic.Guid attribute. Re-exporting the same clash across runs
// must produce the same GUID or each run lands as a new issue. We derive
// the GUID from the ClashIdentity hash via SHA-1 → first 16 bytes → Guid.
using System;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace StingTools.Core.Clash
{
    internal static class BcfMarkupBuilder
    {
        /// <summary>
        /// Derive a stable BCF topic Guid from the ClashIdentity hash so
        /// re-exports across runs collapse to the same topic.
        /// </summary>
        public static string DeriveStableGuid(ClashRecord c)
        {
            string seed = c?.Identity;
            if (string.IsNullOrEmpty(seed)) return Guid.NewGuid().ToString();
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
            return new Guid(bytes[..16]).ToString();
        }

        public static XDocument BuildVersionXml()
        {
            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("Version",
                    new XAttribute("VersionId", "2.1"),
                    new XElement("DetailedVersion", "2.1")));
        }

        public static XDocument BuildMarkupXml(ClashRecord c, string topicGuid)
        {
            string severity = (c.Severity ?? "MED").ToUpperInvariant();
            string bcfPriority = severity switch
            {
                "CRITICAL" => "Critical",
                "HIGH"     => "Major",
                "MED"      => "Normal",
                "MEDIUM"   => "Normal",
                "LOW"      => "Minor",
                _          => "Normal",
            };
            string bcfStatus = (c.State ?? "New") switch
            {
                "Resolved" => "Closed",
                "Void"     => "Closed",
                _          => "Active",
            };

            string title = $"[{c.Severity ?? "MED"}] {c.MatrixPairId} clash {c.Id}";
            var description = new StringBuilder();
            description.AppendLine($"Matrix pair: {c.MatrixPairId}");
            description.AppendLine($"Severity:    {c.Severity}");
            description.AppendLine($"Tolerance:   {c.Tolerance}");
            if (c.ElementA != null)
                description.AppendLine($"Element A:   {c.ElementA.Category}:{c.ElementA.ElementId} (IFC {c.ElementA.IfcGuid})");
            if (c.ElementB != null)
                description.AppendLine($"Element B:   {c.ElementB.Category}:{c.ElementB.ElementId} (IFC {c.ElementB.IfcGuid})");
            if (!string.IsNullOrEmpty(c.ResolutionHint))
                description.AppendLine($"Suggestion:  {c.ResolutionHint}");
            if (!string.IsNullOrEmpty(c.GroupId))
                description.AppendLine($"Group:       {c.GroupId}");
            description.AppendLine($"Identity:    {c.Identity}");
            description.AppendLine($"Volume:      {c.VolumeMm3:F0} mm³");

            var creationIso = (c.FirstSeenUtc == default ? DateTime.UtcNow : c.FirstSeenUtc)
                .ToString("o", CultureInfo.InvariantCulture);
            var modifiedIso = (c.LastSeenUtc == default ? DateTime.UtcNow : c.LastSeenUtc)
                .ToString("o", CultureInfo.InvariantCulture);

            var topic = new XElement("Topic",
                new XAttribute("Guid", topicGuid),
                new XAttribute("TopicType", "Clash"),
                new XAttribute("TopicStatus", bcfStatus),
                new XElement("ReferenceLink", string.IsNullOrEmpty(c.LinkedIssueGuid) ? "" : $"sting-issue://{c.LinkedIssueGuid}"),
                new XElement("Title", title),
                new XElement("Priority", bcfPriority),
                new XElement("Index", 0),
                new XElement("Labels",
                    new XElement("Label", "clash"),
                    new XElement("Label", c.MatrixPairId ?? "")),
                new XElement("CreationDate", creationIso),
                new XElement("CreationAuthor", Environment.UserName),
                new XElement("ModifiedDate", modifiedIso),
                new XElement("ModifiedAuthor", Environment.UserName),
                new XElement("AssignedTo", ""),
                new XElement("Description", description.ToString()),
                // Lossless round-trip hints so a BCF importer can reconstruct
                // the ClashRecord identity, id, matrix pair and severity.
                new XElement("StingClashIdentity", c.Identity ?? ""),
                new XElement("StingClashId", c.Id ?? ""),
                new XElement("StingMatrixPairId", c.MatrixPairId ?? ""),
                new XElement("StingSeverity", c.Severity ?? ""));

            var markup = new XElement("Markup",
                new XElement("Header"),
                topic);
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), markup);
        }

        /// <summary>
        /// Reverse of BuildMarkupXml: parse the lossless STING hints back into
        /// a ClashRecord stub. Only the fields STING writes are recovered;
        /// geometry and triage data must come from a fresh detection run.
        /// </summary>
        public static ClashRecord ParseMarkupXml(XDocument markupDoc)
        {
            if (markupDoc?.Root == null) return null;
            var topic = markupDoc.Root.Element("Topic");
            if (topic == null) return null;
            string topicStatus = topic.Attribute("TopicStatus")?.Value ?? "";
            string identity = topic.Element("StingClashIdentity")?.Value;
            string id       = topic.Element("StingClashId")?.Value;
            string pair     = topic.Element("StingMatrixPairId")?.Value;
            string sev      = topic.Element("StingSeverity")?.Value;
            return new ClashRecord
            {
                Identity = identity,
                Id = id,
                MatrixPairId = pair,
                Severity = sev,
                State = topicStatus == "Closed" ? "Resolved" : "Active",
            };
        }
    }
}
