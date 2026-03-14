using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Platform Link Commands — ACC, CDE, BCF, SharePoint Integration
    //
    //  Provides seamless integration between STING BIM Manager data and
    //  external BIM platforms: Autodesk Construction Cloud (ACC/BIM 360),
    //  Common Data Environments (CDE), BIM Collaboration Format (BCF 2.1),
    //  and SharePoint/Teams document management.
    //
    //  All commands are read-only exports (no model modification).
    //  Data is sourced from STING_BIM_MANAGER/ JSON files alongside the .rvt.
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Engine: PlatformLinkEngine ──

    internal static class PlatformLinkEngine
    {
        // ── BCF Topic Types mapped to STING Issue Types ──
        internal static readonly Dictionary<string, string> StingToBcfType = new Dictionary<string, string>
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
            ["COMMENT"]  = "Comment"
        };

        internal static readonly Dictionary<string, string> BcfToStingType = new Dictionary<string, string>
        {
            ["Request"]  = "RFI",
            ["Clash"]    = "CLASH",
            ["Issue"]    = "DESIGN",
            ["Remark"]   = "SITE",
            ["Fault"]    = "SNAGGING",
            ["Comment"]  = "COMMENT",
            ["Error"]    = "NCR",
            ["Warning"]  = "RISK",
            ["Info"]     = "COMMENT"
        };

        // ── BCF Priority mapped to STING Priority ──
        internal static readonly Dictionary<string, string> BcfToStingPriority = new Dictionary<string, string>
        {
            ["Critical"] = "CRITICAL",
            ["Major"]    = "HIGH",
            ["Normal"]   = "MEDIUM",
            ["Minor"]    = "LOW",
            ["On hold"]  = "INFO"
        };

        // ── ISO 19650 File Naming Validator ──
        internal static bool ValidateISO19650FileName(string fileName, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(fileName))
            {
                reason = "File name is empty";
                return false;
            }

            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string[] parts = nameOnly.Split('-');

            // ISO 19650 naming: Project-Originator-Volume-Level-Type-Role-Classification-Number
            // Minimum 4 fields for simplified naming
            if (parts.Length < 4)
            {
                reason = $"Expected at least 4 hyphen-separated fields, found {parts.Length}: {nameOnly}";
                return false;
            }

            // Check for spaces in fields
            if (parts.Any(p => p.Contains(' ')))
            {
                reason = "Fields must not contain spaces — use underscores instead";
                return false;
            }

            // Check for lowercase (ISO 19650 recommends uppercase)
            if (nameOnly != nameOnly.ToUpperInvariant() && nameOnly != nameOnly.ToLowerInvariant())
            {
                reason = "Mixed case detected — ISO 19650 recommends consistent casing";
                // Warning, not failure
            }

            return true;
        }

        // ── Collect all BIM deliverable files from STING_BIM_MANAGER ──
        internal static List<DeliverableFile> CollectDeliverables(string bimDir, Document doc)
        {
            var files = new List<DeliverableFile>();
            if (!Directory.Exists(bimDir)) return files;

            // BEP
            string bepPath = Path.Combine(bimDir, "bep.json");
            if (File.Exists(bepPath))
                files.Add(new DeliverableFile(bepPath, "BEP", "RP", "S3", "WIP"));

            // Issues
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
                files.Add(new DeliverableFile(issuesPath, "Issue Register", "SH", "S2", "SHARED"));

            // Document register
            string docRegPath = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docRegPath))
                files.Add(new DeliverableFile(docRegPath, "Document Register", "SH", "S2", "SHARED"));

            // Transmittals
            string txPath = Path.Combine(bimDir, "transmittals.json");
            if (File.Exists(txPath))
                files.Add(new DeliverableFile(txPath, "Transmittal Log", "CR", "S2", "SHARED"));

            // COBie exports
            foreach (string cobieDir in Directory.GetDirectories(bimDir, "COBie_*"))
            {
                foreach (string csv in Directory.GetFiles(cobieDir, "*.csv"))
                    files.Add(new DeliverableFile(csv, $"COBie — {Path.GetFileNameWithoutExtension(csv)}", "IE", "S2", "SHARED"));
            }

            // Model health reports
            string healthPath = Path.Combine(bimDir, "model_health.json");
            if (File.Exists(healthPath))
                files.Add(new DeliverableFile(healthPath, "Model Health Report", "RP", "S1", "WIP"));

            // Compliance dashboard
            string compPath = Path.Combine(bimDir, "compliance_dashboard.json");
            if (File.Exists(compPath))
                files.Add(new DeliverableFile(compPath, "Compliance Dashboard", "RP", "S1", "WIP"));

            // Any exported CSV/XLSX reports
            foreach (string report in Directory.GetFiles(bimDir, "*.csv"))
            {
                if (!files.Any(f => f.FilePath == report))
                    files.Add(new DeliverableFile(report, Path.GetFileNameWithoutExtension(report), "SH", "S1", "WIP"));
            }
            foreach (string report in Directory.GetFiles(bimDir, "*.xlsx"))
            {
                if (!files.Any(f => f.FilePath == report))
                    files.Add(new DeliverableFile(report, Path.GetFileNameWithoutExtension(report), "SH", "S1", "WIP"));
            }

            return files;
        }

        // ── Build transmittal cover sheet text ──
        internal static string BuildTransmittalCoverSheet(Document doc, string suitability, List<DeliverableFile> deliverables)
        {
            var pi = doc.ProjectInformation;
            var sb = new StringBuilder();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  ISO 19650 INFORMATION TRANSMITTAL COVER SHEET");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"  Project:        {pi?.Name ?? "Untitled"}");
            sb.AppendLine($"  Project No:     {pi?.Number ?? "N/A"}");
            sb.AppendLine($"  Date:           {now}");
            sb.AppendLine($"  Originator:     {Environment.UserName}");
            sb.AppendLine($"  Suitability:    {suitability} — {(BIMManagerEngine.SuitabilityCodes.TryGetValue(suitability, out string desc) ? desc : suitability)}");
            sb.AppendLine($"  Package Tool:   STING BIM Manager v2.1");
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("  ENCLOSED DOCUMENTS");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine();

            int idx = 1;
            foreach (var file in deliverables)
            {
                sb.AppendLine($"  {idx,3}. {file.Description,-40} [{file.DocType}] {file.Suitability}");
                sb.AppendLine($"       File: {Path.GetFileName(file.FilePath)}");
                idx++;
            }

            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("  DECLARATION");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("  This information package has been prepared in accordance with");
            sb.AppendLine("  BS EN ISO 19650-1:2018 and BS EN ISO 19650-2:2018.");
            sb.AppendLine();
            sb.AppendLine($"  Total documents: {deliverables.Count}");
            sb.AppendLine($"  Generated:       {now}");
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            return sb.ToString();
        }

        // ── Create BCF markup.bcf XML for a single issue ──
        internal static XDocument CreateBcfMarkup(JToken issue, string guid)
        {
            string type = issue["type"]?.ToString() ?? "COMMENT";
            string bcfType = StingToBcfType.TryGetValue(type, out string bt) ? bt : "Issue";
            string priority = issue["priority"]?.ToString() ?? "MEDIUM";
            string bcfPriority = priority switch
            {
                "CRITICAL" => "Critical",
                "HIGH" => "Major",
                "MEDIUM" => "Normal",
                "LOW" => "Minor",
                "INFO" => "On hold",
                _ => "Normal"
            };
            string status = issue["status"]?.ToString() ?? "OPEN";
            string bcfStatus = status switch
            {
                "OPEN" => "Active",
                "IN_PROGRESS" => "Active",
                "RESPONDED" => "Active",
                "CLOSED" => "Resolved",
                "ACCEPTED" => "Resolved",
                "REJECTED" => "Active",
                "VOID" => "Resolved",
                _ => "Active"
            };

            var markup = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Markup",
                    new XElement("Header"),
                    new XElement("Topic",
                        new XAttribute("Guid", guid),
                        new XAttribute("TopicType", bcfType),
                        new XAttribute("TopicStatus", bcfStatus),
                        new XElement("ReferenceLink", issue["issue_id"]?.ToString() ?? ""),
                        new XElement("Title", issue["title"]?.ToString() ?? "Untitled"),
                        new XElement("Priority", bcfPriority),
                        new XElement("Index", 0),
                        new XElement("Labels", new XElement("Label", type)),
                        new XElement("CreationDate", ParseDateToIso(issue["date_raised"]?.ToString())),
                        new XElement("CreationAuthor", issue["raised_by"]?.ToString() ?? ""),
                        new XElement("ModifiedDate", DateTime.UtcNow.ToString("o")),
                        new XElement("ModifiedAuthor", Environment.UserName),
                        new XElement("AssignedTo", issue["assigned_to"]?.ToString() ?? ""),
                        new XElement("Description", issue["description"]?.ToString() ?? "")
                    )
                )
            );

            // Add comments
            var comments = issue["comments"] as JArray;
            if (comments != null)
            {
                var topicElement = markup.Root.Element("Topic");
                foreach (var comment in comments)
                {
                    topicElement.Parent.Add(
                        new XElement("Comment",
                            new XAttribute("Guid", Guid.NewGuid().ToString()),
                            new XElement("Date", ParseDateToIso(comment["date"]?.ToString())),
                            new XElement("Author", comment["author"]?.ToString() ?? ""),
                            new XElement("Comment", comment["text"]?.ToString() ?? ""),
                            new XElement("Topic", new XAttribute("Guid", guid))
                        )
                    );
                }
            }

            return markup;
        }

        // ── Create BCF viewpoint.bcfv XML (minimal) ──
        internal static XDocument CreateBcfViewpoint(string guid)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("VisualizationInfo",
                    new XAttribute("Guid", guid),
                    new XElement("Components",
                        new XElement("Selection"),
                        new XElement("Visibility",
                            new XAttribute("DefaultVisibility", "true"),
                            new XElement("Exceptions")
                        )
                    )
                )
            );
        }

        // ── Create BCF version.bcfv XML ──
        internal static XDocument CreateBcfVersion()
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Version",
                    new XAttribute("VersionId", "2.1"),
                    new XElement("DetailedVersion", "2.1")
                )
            );
        }

        // ── Create BCF project.bcfp XML ──
        internal static XDocument CreateBcfProject(Document doc)
        {
            var pi = doc.ProjectInformation;
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("ProjectExtension",
                    new XElement("Project",
                        new XAttribute("ProjectId", Guid.NewGuid().ToString()),
                        new XElement("Name", pi?.Name ?? "Untitled")
                    ),
                    new XElement("ExtensionSchema")
                )
            );
        }

        // ── Parse STING date format to ISO 8601 ──
        private static string ParseDateToIso(string dateStr)
        {
            if (DateTime.TryParse(dateStr, out DateTime dt))
                return dt.ToUniversalTime().ToString("o");
            return DateTime.UtcNow.ToString("o");
        }

        // ── Parse BCF issue back to STING JObject ──
        internal static JObject ParseBcfTopicToIssue(XDocument markup, string existingNextId)
        {
            var topic = markup.Root?.Element("Topic");
            if (topic == null) return null;

            string bcfType = topic.Attribute("TopicType")?.Value ?? "Issue";
            string stingType = BcfToStingType.TryGetValue(bcfType, out string st) ? st : "COMMENT";

            string bcfPriority = topic.Element("Priority")?.Value ?? "Normal";
            string stingPriority = BcfToStingPriority.TryGetValue(bcfPriority, out string sp) ? sp : "MEDIUM";

            string bcfStatus = topic.Attribute("TopicStatus")?.Value ?? "Active";
            string stingStatus = bcfStatus == "Resolved" || bcfStatus == "Closed" ? "CLOSED" : "OPEN";

            string title = topic.Element("Title")?.Value ?? "Imported BCF Topic";
            string description = topic.Element("Description")?.Value ?? "";
            string assignedTo = topic.Element("AssignedTo")?.Value ?? "";
            string createdBy = topic.Element("CreationAuthor")?.Value ?? "";
            string createdDate = topic.Element("CreationDate")?.Value ?? "";

            if (DateTime.TryParse(createdDate, out DateTime cd))
                createdDate = cd.ToString("yyyy-MM-dd HH:mm");
            else
                createdDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            string bcfGuid = topic.Attribute("Guid")?.Value ?? "";

            var issue = new JObject
            {
                ["issue_id"] = existingNextId,
                ["type"] = stingType,
                ["type_description"] = BIMManagerEngine.IssueTypes.ContainsKey(stingType) ? BIMManagerEngine.IssueTypes[stingType] : stingType,
                ["priority"] = stingPriority,
                ["title"] = title,
                ["description"] = description,
                ["status"] = stingStatus,
                ["assigned_to"] = assignedTo,
                ["discipline"] = "",
                ["raised_by"] = string.IsNullOrEmpty(createdBy) ? Environment.UserName : createdBy,
                ["date_raised"] = createdDate,
                ["date_due"] = stingPriority == "CRITICAL" ? DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") :
                               stingPriority == "HIGH" ? DateTime.Now.AddDays(3).ToString("yyyy-MM-dd") :
                               stingPriority == "MEDIUM" ? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd") :
                               DateTime.Now.AddDays(14).ToString("yyyy-MM-dd"),
                ["date_closed"] = stingStatus == "CLOSED" ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : "",
                ["response"] = "",
                ["element_ids"] = new JArray(),
                ["view_name"] = "",
                ["revision"] = "P01",
                ["bcf_guid"] = bcfGuid,
                ["import_source"] = "BCF 2.1",
                ["comments"] = new JArray()
            };

            // Import BCF comments
            var bcfComments = markup.Root?.Elements("Comment");
            if (bcfComments != null)
            {
                foreach (var c in bcfComments)
                {
                    string commentText = c.Element("Comment")?.Value ?? "";
                    string commentAuthor = c.Element("Author")?.Value ?? "";
                    string commentDate = c.Element("Date")?.Value ?? "";
                    if (DateTime.TryParse(commentDate, out DateTime cdt))
                        commentDate = cdt.ToString("yyyy-MM-dd HH:mm");

                    if (!string.IsNullOrEmpty(commentText))
                    {
                        ((JArray)issue["comments"]).Add(new JObject
                        {
                            ["text"] = commentText,
                            ["author"] = commentAuthor,
                            ["date"] = commentDate
                        });
                    }
                }
            }

            return issue;
        }

        // ── Build CDE manifest.json ──
        internal static JObject BuildCDEManifest(Document doc, List<DeliverableFile> deliverables, string packageDir)
        {
            var pi = doc.ProjectInformation;
            var manifest = new JObject
            {
                ["schema_version"] = "1.0",
                ["standard"] = "BS EN ISO 19650",
                ["generated_by"] = "STING BIM Manager v2.1",
                ["generated_date"] = DateTime.Now.ToString("o"),
                ["project"] = new JObject
                {
                    ["name"] = pi?.Name ?? "Untitled",
                    ["number"] = pi?.Number ?? "",
                    ["originator"] = Environment.UserName
                },
                ["package_directory"] = packageDir
            };

            var files = new JArray();
            foreach (var d in deliverables)
            {
                string relPath = d.FilePath;
                try { relPath = Path.GetRelativePath(packageDir, d.FilePath); }
                catch { relPath = Path.GetFileName(d.FilePath); }

                files.Add(new JObject
                {
                    ["file_name"] = Path.GetFileName(d.FilePath),
                    ["relative_path"] = relPath,
                    ["description"] = d.Description,
                    ["document_type"] = d.DocType,
                    ["suitability_code"] = d.Suitability,
                    ["suitability_description"] = BIMManagerEngine.SuitabilityCodes.TryGetValue(d.Suitability, out string sd) ? sd : d.Suitability,
                    ["cde_state"] = d.CDEState,
                    ["cde_state_description"] = BIMManagerEngine.CDEStates.TryGetValue(d.CDEState, out string cd) ? cd : d.CDEState,
                    ["file_size_bytes"] = File.Exists(d.FilePath) ? new FileInfo(d.FilePath).Length : 0,
                    ["sha256"] = ComputeFileSha256(d.FilePath)
                });
            }
            manifest["deliverables"] = files;
            manifest["total_files"] = deliverables.Count;

            return manifest;
        }

        // ── Compute SHA-256 hash of a file ──
        internal static string ComputeFileSha256(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        // ── Build SharePoint metadata XML ──
        internal static XDocument BuildSharePointMetadata(Document doc, List<DeliverableFile> deliverables)
        {
            var pi = doc.ProjectInformation;
            var root = new XElement("DocumentLibrary",
                new XAttribute("xmlns", "http://schemas.microsoft.com/sharepoint/"),
                new XElement("ProjectInfo",
                    new XElement("ProjectName", pi?.Name ?? "Untitled"),
                    new XElement("ProjectNumber", pi?.Number ?? ""),
                    new XElement("Originator", Environment.UserName),
                    new XElement("DateGenerated", DateTime.Now.ToString("o"))
                )
            );

            var columnsEl = new XElement("Columns",
                new XElement("Column", new XAttribute("Name", "DocumentType"), new XAttribute("Type", "Choice")),
                new XElement("Column", new XAttribute("Name", "SuitabilityCode"), new XAttribute("Type", "Choice")),
                new XElement("Column", new XAttribute("Name", "CDEState"), new XAttribute("Type", "Choice")),
                new XElement("Column", new XAttribute("Name", "Revision"), new XAttribute("Type", "Text")),
                new XElement("Column", new XAttribute("Name", "Originator"), new XAttribute("Type", "Text")),
                new XElement("Column", new XAttribute("Name", "Description"), new XAttribute("Type", "Note")),
                new XElement("Column", new XAttribute("Name", "DateIssued"), new XAttribute("Type", "DateTime")),
                new XElement("Column", new XAttribute("Name", "SHA256"), new XAttribute("Type", "Text"))
            );
            root.Add(columnsEl);

            var docs = new XElement("Documents");
            foreach (var d in deliverables)
            {
                docs.Add(new XElement("Document",
                    new XElement("FileName", Path.GetFileName(d.FilePath)),
                    new XElement("DocumentType", d.DocType),
                    new XElement("Description", d.Description),
                    new XElement("SuitabilityCode", d.Suitability),
                    new XElement("CDEState", d.CDEState),
                    new XElement("Revision", "P01"),
                    new XElement("Originator", Environment.UserName),
                    new XElement("DateIssued", DateTime.Now.ToString("o")),
                    new XElement("SHA256", ComputeFileSha256(d.FilePath))
                ));
            }
            root.Add(docs);

            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        // ── Build SharePoint index.html dashboard ──
        internal static string BuildSharePointDashboard(Document doc, List<DeliverableFile> deliverables, string packageDir)
        {
            var pi = doc.ProjectInformation;
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"utf-8\">");
            sb.AppendLine($"  <title>STING BIM Deliverables — {pi?.Name ?? "Project"}</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
            sb.AppendLine("    h1 { color: #1a237e; border-bottom: 3px solid #1a237e; padding-bottom: 10px; }");
            sb.AppendLine("    h2 { color: #283593; margin-top: 30px; }");
            sb.AppendLine("    table { border-collapse: collapse; width: 100%; background: white; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine("    th { background: #1a237e; color: white; padding: 12px 8px; text-align: left; }");
            sb.AppendLine("    td { padding: 8px; border-bottom: 1px solid #e0e0e0; }");
            sb.AppendLine("    tr:hover { background: #e3f2fd; }");
            sb.AppendLine("    .badge { padding: 3px 8px; border-radius: 3px; font-size: 11px; font-weight: bold; color: white; }");
            sb.AppendLine("    .wip { background: #ff9800; }");
            sb.AppendLine("    .shared { background: #2196f3; }");
            sb.AppendLine("    .published { background: #4caf50; }");
            sb.AppendLine("    .archive { background: #9e9e9e; }");
            sb.AppendLine("    .meta { color: #666; font-size: 12px; margin-top: 5px; }");
            sb.AppendLine("    .summary { display: flex; gap: 20px; margin: 20px 0; }");
            sb.AppendLine("    .card { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); flex: 1; text-align: center; }");
            sb.AppendLine("    .card h3 { margin: 0; font-size: 36px; color: #1a237e; }");
            sb.AppendLine("    .card p { margin: 5px 0 0; color: #666; }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine($"  <h1>STING BIM Deliverables</h1>");
            sb.AppendLine($"  <p class=\"meta\">Project: <strong>{pi?.Name ?? "Untitled"}</strong> | Number: <strong>{pi?.Number ?? "N/A"}</strong> | Generated: {DateTime.Now:yyyy-MM-dd HH:mm} | Tool: STING BIM Manager v2.1</p>");

            // Summary cards
            int wip = deliverables.Count(d => d.CDEState == "WIP");
            int shared = deliverables.Count(d => d.CDEState == "SHARED");
            int published = deliverables.Count(d => d.CDEState == "PUBLISHED");
            int archive = deliverables.Count(d => d.CDEState == "ARCHIVE");

            sb.AppendLine("  <div class=\"summary\">");
            sb.AppendLine($"    <div class=\"card\"><h3>{deliverables.Count}</h3><p>Total Documents</p></div>");
            sb.AppendLine($"    <div class=\"card\"><h3>{wip}</h3><p>Work in Progress</p></div>");
            sb.AppendLine($"    <div class=\"card\"><h3>{shared}</h3><p>Shared</p></div>");
            sb.AppendLine($"    <div class=\"card\"><h3>{published}</h3><p>Published</p></div>");
            sb.AppendLine("  </div>");

            // Document table
            sb.AppendLine("  <h2>Document Index</h2>");
            sb.AppendLine("  <table>");
            sb.AppendLine("    <tr><th>#</th><th>File</th><th>Description</th><th>Type</th><th>Suitability</th><th>CDE State</th></tr>");

            int row = 1;
            foreach (var d in deliverables)
            {
                string badge = d.CDEState.ToLowerInvariant();
                sb.AppendLine($"    <tr>");
                sb.AppendLine($"      <td>{row}</td>");
                sb.AppendLine($"      <td>{EscapeHtml(Path.GetFileName(d.FilePath))}</td>");
                sb.AppendLine($"      <td>{EscapeHtml(d.Description)}</td>");
                sb.AppendLine($"      <td>{EscapeHtml(d.DocType)}</td>");
                sb.AppendLine($"      <td>{EscapeHtml(d.Suitability)}</td>");
                sb.AppendLine($"      <td><span class=\"badge {badge}\">{EscapeHtml(d.CDEState)}</span></td>");
                sb.AppendLine($"    </tr>");
                row++;
            }

            sb.AppendLine("  </table>");
            sb.AppendLine("  <p class=\"meta\">Generated by STING BIM Manager. Compliant with BS EN ISO 19650.</p>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // ── Build platform sync delta ──
        internal static JObject BuildSyncDelta(string bimDir, DateTime lastSync)
        {
            var delta = new JObject
            {
                ["last_sync"] = lastSync.ToString("o"),
                ["current_sync"] = DateTime.UtcNow.ToString("o")
            };

            var changes = new JArray();

            // Check all JSON files for modifications since last sync
            if (Directory.Exists(bimDir))
            {
                foreach (string file in Directory.GetFiles(bimDir, "*.json"))
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTimeUtc > lastSync)
                    {
                        changes.Add(new JObject
                        {
                            ["file"] = Path.GetFileName(file),
                            ["modified"] = fi.LastWriteTimeUtc.ToString("o"),
                            ["size_bytes"] = fi.Length,
                            ["change_type"] = "modified"
                        });
                    }
                }

                // Check subdirectories (COBie exports, etc.)
                foreach (string dir in Directory.GetDirectories(bimDir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc > lastSync)
                        {
                            changes.Add(new JObject
                            {
                                ["file"] = Path.GetRelativePath(bimDir, file),
                                ["modified"] = fi.LastWriteTimeUtc.ToString("o"),
                                ["size_bytes"] = fi.Length,
                                ["change_type"] = "modified"
                            });
                        }
                    }
                }
            }

            delta["changes"] = changes;
            delta["total_changes"] = changes.Count;

            return delta;
        }

        // ── Create snapshot.png placeholder (1x1 white PNG) ──
        internal static byte[] CreatePlaceholderPng()
        {
            // Minimal valid 1x1 white PNG
            return new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,  // PNG signature
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,  // IHDR chunk
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,  // 1x1
                0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,  // 8-bit RGB
                0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,  // IDAT chunk
                0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,  // compressed data
                0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC,  // ...
                0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,  // IEND chunk
                0x44, 0xAE, 0x42, 0x60, 0x82                      // IEND CRC
            };
        }
    }

    // ── Deliverable file descriptor ──
    internal class DeliverableFile
    {
        public string FilePath { get; }
        public string Description { get; }
        public string DocType { get; }
        public string Suitability { get; set; }
        public string CDEState { get; set; }

        public DeliverableFile(string filePath, string description, string docType, string suitability, string cdeState)
        {
            FilePath = filePath;
            Description = description;
            DocType = docType;
            Suitability = suitability;
            CDEState = cdeState;
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  1. ACCPublishCommand — Publish model data to ACC/BIM 360
    // ═══════════════════════════════════════════════════════════════

    #region ── ACC Publish ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ACCPublishCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                StingLog.Info("PlatformLink: Starting ACC publish package creation...");

                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);

                // Ask for suitability code
                var suitDlg = new TaskDialog("STING ACC Publish — Suitability");
                suitDlg.MainInstruction = "Suitability code for this publish:";
                suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "S1 — Fit for Coordination");
                suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "S2 — Fit for Information");
                suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "S3 — Fit for Review and Comment");
                suitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "S4 — Fit for Stage Approval");
                var suitResult = suitDlg.Show();
                string suitability = suitResult switch
                {
                    TaskDialogResult.CommandLink1 => "S1",
                    TaskDialogResult.CommandLink2 => "S2",
                    TaskDialogResult.CommandLink3 => "S3",
                    TaskDialogResult.CommandLink4 => "S4",
                    _ => "S3"
                };

                // Collect deliverables
                var deliverables = PlatformLinkEngine.CollectDeliverables(bimDir, doc);
                if (deliverables.Count == 0)
                {
                    TaskDialog.Show("STING ACC Publish",
                        "No BIM deliverables found in STING_BIM_MANAGER directory.\n\n" +
                        "Run BIM Manager commands first to generate:\n" +
                        "- BEP (Create BEP)\n" +
                        "- Issues (Raise Issue)\n" +
                        "- COBie (COBie Export)\n" +
                        "- Document Register (Document Register)");
                    return Result.Cancelled;
                }

                // Update suitability on all deliverables
                foreach (var d in deliverables)
                    d.Suitability = suitability;

                // Create output package directory
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string projectName = doc.ProjectInformation?.Name ?? "Project";
                string safeName = string.Concat(projectName.Split(Path.GetInvalidFileNameChars()));
                string packageDir = Path.Combine(bimDir, $"ACC_PUBLISH_{safeName}_{timestamp}");
                Directory.CreateDirectory(packageDir);

                // Copy all deliverables to package
                int copied = 0;
                foreach (var d in deliverables)
                {
                    if (!File.Exists(d.FilePath)) continue;
                    string destFile = Path.Combine(packageDir, Path.GetFileName(d.FilePath));
                    // Avoid overwriting if same name from different dirs
                    if (File.Exists(destFile))
                        destFile = Path.Combine(packageDir, $"{Path.GetFileNameWithoutExtension(d.FilePath)}_{copied}{Path.GetExtension(d.FilePath)}");
                    File.Copy(d.FilePath, destFile, true);
                    copied++;
                }

                // Add model file reference
                var modelRef = new JObject
                {
                    ["model_file"] = Path.GetFileName(doc.PathName ?? "untitled.rvt"),
                    ["model_path"] = doc.PathName ?? "",
                    ["is_cloud_model"] = doc.IsWorkshared,
                    ["revit_version"] = doc.Application.VersionNumber,
                    ["export_date"] = DateTime.Now.ToString("o")
                };
                File.WriteAllText(Path.Combine(packageDir, "model_reference.json"),
                    modelRef.ToString(Formatting.Indented));

                // Generate transmittal cover sheet
                string coverSheet = PlatformLinkEngine.BuildTransmittalCoverSheet(doc, suitability, deliverables);
                File.WriteAllText(Path.Combine(packageDir, "TRANSMITTAL_COVER.txt"), coverSheet);

                // Generate manifest
                var manifest = PlatformLinkEngine.BuildCDEManifest(doc, deliverables, packageDir);
                File.WriteAllText(Path.Combine(packageDir, "manifest.json"),
                    manifest.ToString(Formatting.Indented));

                // Create ZIP archive
                string zipPath = packageDir + ".zip";
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(packageDir, zipPath, CompressionLevel.Optimal, false);

                // Auto-register in document register
                BIMManagerEngine.AutoRegisterExport(doc, zipPath, "CM",
                    $"ACC publish package — {suitability} — {deliverables.Count} deliverables");

                // Record transmittal
                string txPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "transmittals.json");
                var transmittals = BIMManagerEngine.LoadJsonArray(txPath);
                BIMManagerEngine.SyncSequentialCounter(transmittals, "TX");
                var tx = BIMManagerEngine.CreateTransmittal(doc, "ACC/BIM 360", "", suitability,
                    $"ACC publish package with {deliverables.Count} deliverables",
                    new JArray(deliverables.Select(d => Path.GetFileName(d.FilePath))));
                transmittals.Add(tx);
                BIMManagerEngine.SaveJsonFile(txPath, transmittals);

                long zipSize = new FileInfo(zipPath).Length;
                string sizeStr = zipSize < 1024 * 1024
                    ? $"{zipSize / 1024.0:F1} KB"
                    : $"{zipSize / (1024.0 * 1024.0):F1} MB";

                StingLog.Info($"PlatformLink: ACC publish complete — {copied} files, ZIP: {sizeStr}");

                var result = new TaskDialog("STING ACC Publish — Complete");
                result.MainInstruction = "ACC publish package created successfully";
                result.MainContent =
                    $"Deliverables packaged: {deliverables.Count}\n" +
                    $"Files copied: {copied}\n" +
                    $"Suitability: {suitability} — {(BIMManagerEngine.SuitabilityCodes.TryGetValue(suitability, out string sd) ? sd : suitability)}\n" +
                    $"ZIP size: {sizeStr}\n\n" +
                    $"Package: {Path.GetFileName(zipPath)}\n" +
                    $"Location: {Path.GetDirectoryName(zipPath)}\n\n" +
                    "Upload the ZIP file to ACC/BIM 360 document management.";
                result.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ACCPublishCommand failed", ex);
                TaskDialog.Show("STING Error", $"ACC publish failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  2. CDEPackageCommand — Create CDE-ready deliverable package
    // ═══════════════════════════════════════════════════════════════

    #region ── CDE Package ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CDEPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                StingLog.Info("PlatformLink: Creating CDE deliverable package...");

                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                var deliverables = PlatformLinkEngine.CollectDeliverables(bimDir, doc);

                if (deliverables.Count == 0)
                {
                    TaskDialog.Show("STING CDE Package",
                        "No BIM deliverables found.\n\nRun BIM Manager commands first to generate deliverables.");
                    return Result.Cancelled;
                }

                // Create timestamped output folder
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string projectName = doc.ProjectInformation?.Name ?? "Project";
                string safeName = string.Concat(projectName.Split(Path.GetInvalidFileNameChars()));
                string packageDir = Path.Combine(bimDir, $"CDE_PACKAGE_{safeName}_{timestamp}");
                Directory.CreateDirectory(packageDir);

                // Validate ISO 19650 naming on all files
                var namingIssues = new List<string>();
                foreach (var d in deliverables)
                {
                    string fileName = Path.GetFileName(d.FilePath);
                    if (!PlatformLinkEngine.ValidateISO19650FileName(fileName, out string reason))
                    {
                        if (!string.IsNullOrEmpty(reason))
                            namingIssues.Add($"  {fileName}: {reason}");
                    }
                }

                // Copy deliverables to package
                int copied = 0;
                foreach (var d in deliverables)
                {
                    if (!File.Exists(d.FilePath)) continue;
                    string destFile = Path.Combine(packageDir, Path.GetFileName(d.FilePath));
                    if (File.Exists(destFile))
                        destFile = Path.Combine(packageDir, $"{Path.GetFileNameWithoutExtension(d.FilePath)}_{copied}{Path.GetExtension(d.FilePath)}");
                    File.Copy(d.FilePath, destFile, true);
                    copied++;
                }

                // Add model file reference
                var modelRef = new JObject
                {
                    ["model_file"] = Path.GetFileName(doc.PathName ?? "untitled.rvt"),
                    ["model_path"] = doc.PathName ?? "",
                    ["revit_version"] = doc.Application.VersionNumber,
                    ["export_date"] = DateTime.Now.ToString("o")
                };
                File.WriteAllText(Path.Combine(packageDir, "model_reference.json"),
                    modelRef.ToString(Formatting.Indented));

                // Generate manifest.json
                var manifest = PlatformLinkEngine.BuildCDEManifest(doc, deliverables, packageDir);
                File.WriteAllText(Path.Combine(packageDir, "manifest.json"),
                    manifest.ToString(Formatting.Indented));

                // Generate BEP summary if available
                string bepPath = Path.Combine(bimDir, "bep.json");
                if (File.Exists(bepPath))
                {
                    File.Copy(bepPath, Path.Combine(packageDir, "bep.json"), true);
                }

                // Generate compliance summary
                var compScan = ComplianceScan.Scan(doc);
                if (compScan != null)
                {
                    var compJson = new JObject
                    {
                        ["scan_date"] = DateTime.Now.ToString("o"),
                        ["total_elements"] = compScan.TotalElements,
                        ["tagged_complete"] = compScan.TaggedComplete,
                        ["tagged_incomplete"] = compScan.TaggedIncomplete,
                        ["untagged"] = compScan.Untagged,
                        ["compliance_percent"] = compScan.CompliancePercent,
                        ["rag_status"] = compScan.RAGStatus,
                        ["top_issues"] = compScan.TopIssues
                    };
                    File.WriteAllText(Path.Combine(packageDir, "compliance_summary.json"),
                        compJson.ToString(Formatting.Indented));
                }

                // Auto-register
                BIMManagerEngine.AutoRegisterExport(doc, packageDir, "CM",
                    $"CDE deliverable package — {deliverables.Count} files");

                StingLog.Info($"PlatformLink: CDE package complete — {copied} files in {packageDir}");

                var sb = new StringBuilder();
                sb.AppendLine($"Deliverables packaged: {deliverables.Count}");
                sb.AppendLine($"Files copied: {copied}");
                sb.AppendLine($"Package: {Path.GetFileName(packageDir)}");
                if (namingIssues.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"ISO 19650 naming warnings ({namingIssues.Count}):");
                    foreach (string issue in namingIssues.Take(10))
                        sb.AppendLine(issue);
                    if (namingIssues.Count > 10)
                        sb.AppendLine($"  ... and {namingIssues.Count - 10} more");
                }
                sb.AppendLine();
                sb.AppendLine("Contents:");
                sb.AppendLine("  - manifest.json (deliverable index with SHA-256 hashes)");
                sb.AppendLine("  - model_reference.json (Revit model metadata)");
                if (File.Exists(bepPath)) sb.AppendLine("  - bep.json (BIM Execution Plan)");
                if (compScan != null) sb.AppendLine("  - compliance_summary.json (tag compliance)");
                sb.AppendLine($"  - {copied} deliverable files");

                var resultDlg = new TaskDialog("STING CDE Package — Complete");
                resultDlg.MainInstruction = "CDE deliverable package created";
                resultDlg.MainContent = sb.ToString();
                resultDlg.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CDEPackageCommand failed", ex);
                TaskDialog.Show("STING Error", $"CDE package creation failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  3. BCFExportCommand — Export issues as BCF 2.1
    // ═══════════════════════════════════════════════════════════════

    #region ── BCF Export ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BCFExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                StingLog.Info("PlatformLink: Starting BCF 2.1 export...");

                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
                var issues = BIMManagerEngine.LoadJsonArray(issuesPath);

                if (issues.Count == 0)
                {
                    TaskDialog.Show("STING BCF Export",
                        "No issues found in issues.json.\n\n" +
                        "Use 'Raise Issue' in the BIM tab to create issues first.");
                    return Result.Cancelled;
                }

                // Ask for export scope
                var scopeDlg = new TaskDialog("STING BCF Export — Scope");
                scopeDlg.MainInstruction = "Which issues to export?";
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "All Issues",
                    $"{issues.Count} issues total");
                int openCount = issues.Count(i => i["status"]?.ToString() == "OPEN" || i["status"]?.ToString() == "IN_PROGRESS");
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Open Issues Only",
                    $"{openCount} open/in-progress issues");
                var scopeResult = scopeDlg.Show();

                JArray exportIssues;
                if (scopeResult == TaskDialogResult.CommandLink2)
                {
                    exportIssues = new JArray(issues.Where(i =>
                        i["status"]?.ToString() == "OPEN" || i["status"]?.ToString() == "IN_PROGRESS"));
                }
                else
                {
                    exportIssues = issues;
                }

                if (exportIssues.Count == 0)
                {
                    TaskDialog.Show("STING BCF Export", "No issues match the selected scope.");
                    return Result.Cancelled;
                }

                // Create BCF ZIP
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string bcfPath = Path.Combine(bimDir, $"STING_Issues_{timestamp}.bcfzip");

                // Build BCF in a temp directory, then zip
                string tempDir = Path.Combine(Path.GetTempPath(), $"STING_BCF_{timestamp}");
                try
                {
                    Directory.CreateDirectory(tempDir);

                    // Write bcf.version
                    var versionDoc = PlatformLinkEngine.CreateBcfVersion();
                    versionDoc.Save(Path.Combine(tempDir, "bcf.version"));

                    // Write project.bcfp
                    var projectDoc = PlatformLinkEngine.CreateBcfProject(doc);
                    projectDoc.Save(Path.Combine(tempDir, "project.bcfp"));

                    // Write each issue as a topic folder
                    int exported = 0;
                    byte[] placeholderPng = PlatformLinkEngine.CreatePlaceholderPng();

                    foreach (var issue in exportIssues)
                    {
                        string topicGuid = Guid.NewGuid().ToString();
                        string topicDir = Path.Combine(tempDir, topicGuid);
                        Directory.CreateDirectory(topicDir);

                        // markup.bcf
                        var markupDoc = PlatformLinkEngine.CreateBcfMarkup(issue, topicGuid);
                        markupDoc.Save(Path.Combine(topicDir, "markup.bcf"));

                        // viewpoint.bcfv
                        string vpGuid = Guid.NewGuid().ToString();
                        var vpDoc = PlatformLinkEngine.CreateBcfViewpoint(vpGuid);
                        vpDoc.Save(Path.Combine(topicDir, "viewpoint.bcfv"));

                        // snapshot.png (placeholder)
                        File.WriteAllBytes(Path.Combine(topicDir, "snapshot.png"), placeholderPng);

                        exported++;
                    }

                    // Create ZIP
                    if (File.Exists(bcfPath)) File.Delete(bcfPath);
                    ZipFile.CreateFromDirectory(tempDir, bcfPath, CompressionLevel.Optimal, false);
                }
                finally
                {
                    // Clean up temp directory
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                    catch { /* best-effort cleanup */ }
                }

                // Auto-register
                BIMManagerEngine.AutoRegisterExport(doc, bcfPath, "RP",
                    $"BCF 2.1 export — {exportIssues.Count} issues");

                long fileSize = new FileInfo(bcfPath).Length;
                string sizeStr = fileSize < 1024 * 1024
                    ? $"{fileSize / 1024.0:F1} KB"
                    : $"{fileSize / (1024.0 * 1024.0):F1} MB";

                StingLog.Info($"PlatformLink: BCF export complete — {exportIssues.Count} issues, {sizeStr}");

                var resultDlg = new TaskDialog("STING BCF Export — Complete");
                resultDlg.MainInstruction = $"BCF 2.1 export: {exportIssues.Count} issues";
                resultDlg.MainContent =
                    $"Issues exported: {exportIssues.Count}\n" +
                    $"File: {Path.GetFileName(bcfPath)}\n" +
                    $"Size: {sizeStr}\n\n" +
                    "Compatible with: Navisworks, Solibri, BIMcollab, BIM Track,\n" +
                    "Trimble Connect, Revizto, and other BCF 2.1 viewers.";
                resultDlg.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BCFExportCommand failed", ex);
                TaskDialog.Show("STING Error", $"BCF export failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  4. BCFImportCommand — Import BCF files as STING issues
    // ═══════════════════════════════════════════════════════════════

    #region ── BCF Import ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BCFImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                StingLog.Info("PlatformLink: Starting BCF 2.1 import...");

                // Ask user to select BCF file
                var dlg = new TaskDialog("STING BCF Import");
                dlg.MainInstruction = "Import BCF File";
                dlg.MainContent =
                    "Place your .bcfzip file in the STING_BIM_MANAGER directory,\n" +
                    "then this command will import all topics as STING issues.\n\n" +
                    "Looking for .bcfzip files...";

                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                var bcfFiles = Directory.GetFiles(bimDir, "*.bcfzip")
                    .Concat(Directory.GetFiles(bimDir, "*.bcf"))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                if (bcfFiles.Count == 0)
                {
                    TaskDialog.Show("STING BCF Import",
                        $"No .bcfzip files found in:\n{bimDir}\n\n" +
                        "Copy a .bcfzip file to this directory and try again.");
                    return Result.Cancelled;
                }

                // If multiple BCF files, let user choose
                string selectedBcf;
                if (bcfFiles.Count == 1)
                {
                    selectedBcf = bcfFiles[0];
                }
                else
                {
                    var chooseDlg = new TaskDialog("STING BCF Import — Select File");
                    chooseDlg.MainInstruction = $"Found {bcfFiles.Count} BCF files. Select one:";
                    var sb = new StringBuilder();
                    for (int i = 0; i < Math.Min(bcfFiles.Count, 4); i++)
                    {
                        var fi = new FileInfo(bcfFiles[i]);
                        sb.AppendLine($"{i + 1}. {fi.Name} ({fi.Length / 1024.0:F0} KB, {fi.LastWriteTime:yyyy-MM-dd HH:mm})");
                    }
                    chooseDlg.MainContent = sb.ToString();
                    chooseDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        $"Import: {Path.GetFileName(bcfFiles[0])}");
                    if (bcfFiles.Count > 1)
                        chooseDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                            $"Import: {Path.GetFileName(bcfFiles[1])}");
                    if (bcfFiles.Count > 2)
                        chooseDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                            $"Import: {Path.GetFileName(bcfFiles[2])}");
                    if (bcfFiles.Count > 3)
                        chooseDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                            $"Import: {Path.GetFileName(bcfFiles[3])}");

                    var choice = chooseDlg.Show();
                    int idx = choice switch
                    {
                        TaskDialogResult.CommandLink1 => 0,
                        TaskDialogResult.CommandLink2 => 1,
                        TaskDialogResult.CommandLink3 => 2,
                        TaskDialogResult.CommandLink4 => 3,
                        _ => -1
                    };
                    if (idx < 0 || idx >= bcfFiles.Count) return Result.Cancelled;
                    selectedBcf = bcfFiles[idx];
                }

                // Load existing issues
                string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
                var existingIssues = BIMManagerEngine.LoadJsonArray(issuesPath);

                // Extract and process BCF
                string extractDir = Path.Combine(Path.GetTempPath(), $"STING_BCF_IMPORT_{Guid.NewGuid():N}");
                int imported = 0;
                int skipped = 0;

                try
                {
                    ZipFile.ExtractToDirectory(selectedBcf, extractDir);

                    // Each subdirectory with a markup.bcf is a topic
                    foreach (string topicDir in Directory.GetDirectories(extractDir))
                    {
                        string markupPath = Path.Combine(topicDir, "markup.bcf");
                        if (!File.Exists(markupPath)) continue;

                        try
                        {
                            var markupDoc = XDocument.Load(markupPath);
                            var topic = markupDoc.Root?.Element("Topic");
                            if (topic == null) continue;

                            string bcfGuid = topic.Attribute("Guid")?.Value ?? "";

                            // Skip if already imported (check by BCF GUID)
                            if (!string.IsNullOrEmpty(bcfGuid) &&
                                existingIssues.Any(i => i["bcf_guid"]?.ToString() == bcfGuid))
                            {
                                skipped++;
                                continue;
                            }

                            string nextId = BIMManagerEngine.GetNextIssueId(existingIssues, "BCF");
                            var issue = PlatformLinkEngine.ParseBcfTopicToIssue(markupDoc, nextId);
                            if (issue != null)
                            {
                                existingIssues.Add(issue);
                                imported++;
                            }
                        }
                        catch (Exception topicEx)
                        {
                            StingLog.Warn($"BCF Import: failed to parse topic {Path.GetFileName(topicDir)}: {topicEx.Message}");
                            skipped++;
                        }
                    }

                    // Save updated issues
                    if (imported > 0)
                    {
                        BIMManagerEngine.SaveJsonFile(issuesPath, existingIssues);
                    }
                }
                finally
                {
                    try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); }
                    catch { /* best-effort cleanup */ }
                }

                StingLog.Info($"PlatformLink: BCF import complete — {imported} imported, {skipped} skipped");

                var resultDlg = new TaskDialog("STING BCF Import — Complete");
                resultDlg.MainInstruction = $"BCF import: {imported} issues imported";
                resultDlg.MainContent =
                    $"Source: {Path.GetFileName(selectedBcf)}\n" +
                    $"Imported: {imported}\n" +
                    $"Skipped (duplicate/invalid): {skipped}\n" +
                    $"Total issues now: {existingIssues.Count}\n\n" +
                    "View imported issues with 'Issue Dashboard' in the BIM tab.";
                resultDlg.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BCFImportCommand failed", ex);
                TaskDialog.Show("STING Error", $"BCF import failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  5. PlatformSyncCommand — Sync status with external platforms
    // ═══════════════════════════════════════════════════════════════

    #region ── Platform Sync ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlatformSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                StingLog.Info("PlatformLink: Starting platform sync...");

                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                string syncConfigPath = Path.Combine(bimDir, "platform_sync.json");

                // Load or create sync config
                JObject syncConfig;
                if (File.Exists(syncConfigPath))
                {
                    syncConfig = BIMManagerEngine.LoadJsonFile(syncConfigPath);
                }
                else
                {
                    syncConfig = new JObject
                    {
                        ["platform_name"] = "STING BIM Manager",
                        ["project_name"] = doc.ProjectInformation?.Name ?? "Untitled",
                        ["project_number"] = doc.ProjectInformation?.Number ?? "",
                        ["webhook_url"] = "",
                        ["notification_email"] = "",
                        ["last_sync"] = DateTime.MinValue.ToString("o"),
                        ["sync_history"] = new JArray(),
                        ["auto_sync_enabled"] = false,
                        ["sync_interval_minutes"] = 60
                    };
                }

                // Determine last sync time
                DateTime lastSync = DateTime.MinValue;
                string lastSyncStr = syncConfig["last_sync"]?.ToString() ?? "";
                if (DateTime.TryParse(lastSyncStr, out DateTime parsed))
                    lastSync = parsed;

                // Build delta of changes since last sync
                var delta = PlatformLinkEngine.BuildSyncDelta(bimDir, lastSync);

                int changeCount = delta["total_changes"]?.ToObject<int>() ?? 0;

                // Export delta
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string deltaPath = Path.Combine(bimDir, $"sync_delta_{timestamp}.json");
                BIMManagerEngine.SaveJsonFile(deltaPath, delta);

                // Update sync config
                syncConfig["last_sync"] = DateTime.UtcNow.ToString("o");
                var history = syncConfig["sync_history"] as JArray ?? new JArray();
                history.Add(new JObject
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["changes_detected"] = changeCount,
                    ["delta_file"] = Path.GetFileName(deltaPath),
                    ["user"] = Environment.UserName
                });

                // Keep only last 50 sync entries
                while (history.Count > 50)
                    history.RemoveAt(0);

                syncConfig["sync_history"] = history;
                BIMManagerEngine.SaveJsonFile(syncConfigPath, syncConfig);

                // Webhook notification (log the intent — actual HTTP call would require System.Net.Http)
                string webhookUrl = syncConfig["webhook_url"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(webhookUrl))
                {
                    StingLog.Info($"PlatformLink: Webhook configured: {webhookUrl} — delta has {changeCount} changes");
                    // Note: actual HTTP POST would require additional dependencies or async patterns
                    // For now, the delta JSON can be consumed by external automation tools
                }

                // Auto-register
                if (changeCount > 0)
                {
                    BIMManagerEngine.AutoRegisterExport(doc, deltaPath, "DB",
                        $"Platform sync delta — {changeCount} changes since {lastSync:yyyy-MM-dd HH:mm}");
                }

                StingLog.Info($"PlatformLink: Sync complete — {changeCount} changes detected");

                var resultDlg = new TaskDialog("STING Platform Sync — Complete");
                resultDlg.MainInstruction = $"Platform sync: {changeCount} changes detected";
                var reportSb = new StringBuilder();
                reportSb.AppendLine($"Last sync: {(lastSync == DateTime.MinValue ? "Never (first sync)" : lastSync.ToString("yyyy-MM-dd HH:mm"))}");
                reportSb.AppendLine($"Current sync: {DateTime.Now:yyyy-MM-dd HH:mm}");
                reportSb.AppendLine($"Changes detected: {changeCount}");
                reportSb.AppendLine($"Delta exported: {Path.GetFileName(deltaPath)}");
                if (!string.IsNullOrEmpty(webhookUrl))
                    reportSb.AppendLine($"Webhook: {webhookUrl}");
                reportSb.AppendLine();

                if (changeCount > 0)
                {
                    reportSb.AppendLine("Changed files:");
                    var changes = delta["changes"] as JArray;
                    if (changes != null)
                    {
                        foreach (var change in changes.Take(15))
                            reportSb.AppendLine($"  - {change["file"]} ({change["change_type"]})");
                        if (changes.Count > 15)
                            reportSb.AppendLine($"  ... and {changes.Count - 15} more");
                    }
                }
                else
                {
                    reportSb.AppendLine("No changes since last sync.");
                }

                reportSb.AppendLine();
                reportSb.AppendLine($"Sync config: {Path.GetFileName(syncConfigPath)}");
                reportSb.AppendLine("Edit platform_sync.json to configure webhook URL and notifications.");

                resultDlg.MainContent = reportSb.ToString();
                resultDlg.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlatformSyncCommand failed", ex);
                TaskDialog.Show("STING Error", $"Platform sync failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  6. SharePointExportCommand — Export for SharePoint/Teams
    // ═══════════════════════════════════════════════════════════════

    #region ── SharePoint Export ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SharePointExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                StingLog.Info("PlatformLink: Starting SharePoint/Teams export...");

                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                var deliverables = PlatformLinkEngine.CollectDeliverables(bimDir, doc);

                if (deliverables.Count == 0)
                {
                    TaskDialog.Show("STING SharePoint Export",
                        "No BIM deliverables found.\n\nRun BIM Manager commands first to generate deliverables.");
                    return Result.Cancelled;
                }

                // Create ISO 19650 CDE folder structure
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string projectName = doc.ProjectInformation?.Name ?? "Project";
                string safeName = string.Concat(projectName.Split(Path.GetInvalidFileNameChars()));
                string rootDir = Path.Combine(bimDir, $"SharePoint_{safeName}_{timestamp}");
                Directory.CreateDirectory(rootDir);

                // CDE containers per ISO 19650-1 clause 12
                string wipDir = Path.Combine(rootDir, "WIP");
                string sharedDir = Path.Combine(rootDir, "SHARED");
                string publishedDir = Path.Combine(rootDir, "PUBLISHED");
                string archiveDir = Path.Combine(rootDir, "ARCHIVE");
                Directory.CreateDirectory(wipDir);
                Directory.CreateDirectory(sharedDir);
                Directory.CreateDirectory(publishedDir);
                Directory.CreateDirectory(archiveDir);

                // Sort deliverables into CDE containers and copy
                int copied = 0;
                foreach (var d in deliverables)
                {
                    if (!File.Exists(d.FilePath)) continue;

                    string targetDir = d.CDEState switch
                    {
                        "WIP" => wipDir,
                        "SHARED" => sharedDir,
                        "PUBLISHED" => publishedDir,
                        "ARCHIVE" => archiveDir,
                        _ => wipDir
                    };

                    string destFile = Path.Combine(targetDir, Path.GetFileName(d.FilePath));
                    if (File.Exists(destFile))
                        destFile = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(d.FilePath)}_{copied}{Path.GetExtension(d.FilePath)}");
                    File.Copy(d.FilePath, destFile, true);
                    copied++;
                }

                // Generate index.html dashboard
                string dashboard = PlatformLinkEngine.BuildSharePointDashboard(doc, deliverables, rootDir);
                File.WriteAllText(Path.Combine(rootDir, "index.html"), dashboard, Encoding.UTF8);

                // Generate metadata XML for SharePoint document library columns
                var metadataXml = PlatformLinkEngine.BuildSharePointMetadata(doc, deliverables);
                metadataXml.Save(Path.Combine(rootDir, "metadata.xml"));

                // Generate manifest
                var manifest = PlatformLinkEngine.BuildCDEManifest(doc, deliverables, rootDir);
                File.WriteAllText(Path.Combine(rootDir, "manifest.json"),
                    manifest.ToString(Formatting.Indented));

                // Auto-register
                BIMManagerEngine.AutoRegisterExport(doc, rootDir, "DB",
                    $"SharePoint/Teams export — {deliverables.Count} files in CDE structure");

                StingLog.Info($"PlatformLink: SharePoint export complete — {copied} files");

                // Count files per container
                int wipCount = deliverables.Count(d => d.CDEState == "WIP");
                int sharedCount = deliverables.Count(d => d.CDEState == "SHARED");
                int pubCount = deliverables.Count(d => d.CDEState == "PUBLISHED");
                int archCount = deliverables.Count(d => d.CDEState == "ARCHIVE");

                var resultDlg = new TaskDialog("STING SharePoint Export — Complete");
                resultDlg.MainInstruction = $"SharePoint/Teams export: {copied} files";
                resultDlg.MainContent =
                    $"CDE Folder Structure (ISO 19650-1 clause 12):\n" +
                    $"  WIP/       — {wipCount} files\n" +
                    $"  SHARED/    — {sharedCount} files\n" +
                    $"  PUBLISHED/ — {pubCount} files\n" +
                    $"  ARCHIVE/   — {archCount} files\n\n" +
                    $"Generated files:\n" +
                    $"  index.html   — Web dashboard for Teams/browser viewing\n" +
                    $"  metadata.xml — SharePoint document library column definitions\n" +
                    $"  manifest.json — Deliverable index with SHA-256 hashes\n\n" +
                    $"Location: {rootDir}\n\n" +
                    "Upload the entire folder to SharePoint or Teams Files tab.\n" +
                    "The metadata.xml can be used to create document library columns.";
                resultDlg.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SharePointExportCommand failed", ex);
                TaskDialog.Show("STING Error", $"SharePoint export failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion
}
