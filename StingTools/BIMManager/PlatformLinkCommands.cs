using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Diagnostics;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Planscape.Shared.BCF;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

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

        // ── STING issue JObject ⇌ Planscape.Shared.BCF.CoordIssue adapters ──
        // Phase 95: convert the STING issues.json JObject shape into the pure-C#
        // CoordIssue model that BcfEngine understands. Kept as an adapter (not
        // baked into CoordIssue itself) so CoordIssue stays Newtonsoft-free and
        // usable from Planscape.Shared.
        internal static CoordIssue StingIssueToCoord(JToken issue)
        {
            if (issue == null) return new CoordIssue();

            // Preserve BCF GUID across round-trips — critical for dedup on re-import.
            string guid = issue["bcf_guid"]?.ToString();
            if (string.IsNullOrWhiteSpace(guid)) guid = Guid.NewGuid().ToString();

            var ci = new CoordIssue
            {
                Guid          = guid,
                Title         = issue["title"]?.ToString() ?? "Untitled",
                Description   = issue["description"]?.ToString(),
                Priority      = (issue["priority"]?.ToString() ?? "MEDIUM").ToUpperInvariant(),
                Type          = (issue["type"]?.ToString() ?? "COMMENT").ToUpperInvariant(),
                Status        = (issue["status"]?.ToString() ?? "OPEN").ToUpperInvariant(),
                Assignee      = issue["assigned_to"]?.ToString(),
                Author        = issue["raised_by"]?.ToString(),
                ReferenceLink = issue["issue_id"]?.ToString(),
                CreationDate  = ParseStingDate(issue["date_raised"]?.ToString()),
            };

            if (issue["comments"] is JArray comments)
            {
                foreach (var c in comments)
                {
                    string text = c["text"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    ci.Comments.Add(new CoordComment
                    {
                        Author = c["author"]?.ToString() ?? "",
                        Text   = text,
                        Date   = ParseStingDate(c["date"]?.ToString()),
                    });
                }
            }
            return ci;
        }

        /// <summary>Convert an imported <see cref="CoordIssue"/> back into a STING issue JObject.</summary>
        internal static JObject CoordToStingIssue(CoordIssue ci, string nextId)
        {
            if (ci == null) return null;

            string stingType = ci.Type ?? "COMMENT";
            string stingPriority = ci.Priority ?? "MEDIUM";
            string stingStatus = ci.Status ?? "OPEN";
            string created = ci.CreationDate == default
                ? DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                : ci.CreationDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            var issue = new JObject
            {
                ["issue_id"]         = nextId,
                ["type"]             = stingType,
                ["type_description"] = BIMManagerEngine.IssueTypes.TryGetValue(stingType, out var itDesc) ? itDesc : stingType,
                ["priority"]         = stingPriority,
                ["title"]            = ci.Title ?? "(untitled)",
                ["description"]      = ci.Description ?? "",
                ["status"]           = stingStatus,
                ["assigned_to"]      = ci.Assignee ?? "",
                ["discipline"]       = "",
                ["raised_by"]        = string.IsNullOrEmpty(ci.Author) ? Environment.UserName : ci.Author,
                ["date_raised"]      = created,
                ["date_due"]         = stingPriority == "CRITICAL" ? DateTime.Now.AddDays(1).ToString("yyyy-MM-dd") :
                                       stingPriority == "HIGH"     ? DateTime.Now.AddDays(3).ToString("yyyy-MM-dd") :
                                       stingPriority == "MEDIUM"   ? DateTime.Now.AddDays(7).ToString("yyyy-MM-dd") :
                                                                     DateTime.Now.AddDays(14).ToString("yyyy-MM-dd"),
                ["date_closed"]      = stingStatus == "CLOSED" ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : "",
                ["response"]         = "",
                ["element_ids"]      = new JArray(),
                ["view_name"]        = "",
                ["bcf_guid"]         = ci.Guid ?? "",
                ["import_source"]    = "BCF 2.1",
                ["comments"]         = new JArray(),
            };

            foreach (var c in ci.Comments ?? new List<CoordComment>())
            {
                if (c == null || string.IsNullOrEmpty(c.Text)) continue;
                ((JArray)issue["comments"]).Add(new JObject
                {
                    ["text"]   = c.Text,
                    ["author"] = c.Author ?? "",
                    ["date"]   = c.Date == default
                        ? DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                        : c.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                });
            }
            return issue;
        }

        private static DateTime ParseStingDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.UtcNow;
            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return DateTime.UtcNow;
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
                        new XElement("Description", issue["description"]?.ToString() ?? ""),
                        // CS-02 FIX: Preserve STING issue type in BCF extension element for lossless round-trip
                        new XElement("StingIssueType", type)
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

        // ── Create BCF viewpoint.bcfv XML with camera data ──
        internal static XDocument CreateBcfViewpoint(string guid,
            XYZ cameraPos = null, XYZ cameraDir = null, XYZ cameraUp = null, double viewSize = 50.0)
        {
            // Default camera: top-down orthogonal if no position supplied
            if (cameraPos == null) cameraPos = new XYZ(0, 0, 30);
            if (cameraDir == null) cameraDir = new XYZ(0, 0, -1);
            if (cameraUp == null) cameraUp = new XYZ(0, 1, 0);

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("VisualizationInfo",
                    new XAttribute("Guid", guid),
                    new XElement("OrthogonalCamera",
                        new XElement("CameraViewPoint",
                            new XElement("X", cameraPos.X.ToString("F6", CultureInfo.InvariantCulture)),
                            new XElement("Y", cameraPos.Y.ToString("F6", CultureInfo.InvariantCulture)),
                            new XElement("Z", cameraPos.Z.ToString("F6", CultureInfo.InvariantCulture))),
                        new XElement("CameraDirection",
                            new XElement("X", cameraDir.X.ToString("F6", CultureInfo.InvariantCulture)),
                            new XElement("Y", cameraDir.Y.ToString("F6", CultureInfo.InvariantCulture)),
                            new XElement("Z", cameraDir.Z.ToString("F6", CultureInfo.InvariantCulture))),
                        new XElement("CameraUpVector",
                            new XElement("X", cameraUp.X.ToString("F6", CultureInfo.InvariantCulture)),
                            new XElement("Y", cameraUp.Y.ToString("F6", CultureInfo.InvariantCulture)),
                            new XElement("Z", cameraUp.Z.ToString("F6", CultureInfo.InvariantCulture))),
                        new XElement("ViewToWorldScale", viewSize.ToString("F6", CultureInfo.InvariantCulture))
                    ),
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
        internal static JObject ParseBcfTopicToIssue(XDocument markup, string existingNextId, Document doc = null)
        {
            var topic = markup.Root?.Element("Topic");
            if (topic == null) return null;

            string bcfType = topic.Attribute("TopicType")?.Value ?? "Issue";
            // CS-02 FIX: Check for STING extension element first for lossless round-trip
            string stingExtType = topic.Element("StingIssueType")?.Value;
            string stingType = !string.IsNullOrEmpty(stingExtType) ? stingExtType
                : BcfToStingType.TryGetValue(bcfType, out string st) ? st : "COMMENT";

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
                ["type_description"] = BIMManagerEngine.IssueTypes.TryGetValue(stingType, out var itDesc) ? itDesc : stingType,
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
                ["revision"] = doc != null ? (PhaseAutoDetect.DetectProjectRevision(doc) ?? "P01") : "P01",
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
                catch (Exception rpEx) { StingLog.Warn($"GetRelativePath failed: {rpEx.Message}"); relPath = Path.GetFileName(d.FilePath); }

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
            catch (Exception ex)
            {
                Core.StingLog.Warn($"SHA256 compute failed for {filePath}: {ex.Message}");
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

        // ── BCF Bidirectional Sync ──
        // BIM-BCF-SYNC-01: Merges STING issues.json ↔ .bcfzip using bcf_guid as the
        // join key.  New STING issues are exported as new BCF topics; new BCF topics
        // are imported as new STING issues; issues that exist on both sides are
        // merged (most-recent-writer wins per field).

        internal static string RunBidirectionalBcfSync(Document doc, string bcfPath)
        {
            if (doc == null || string.IsNullOrEmpty(bcfPath))
                return "No document or BCF path.";

            // ── 1. Load STING issues ──
            string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
            var stingIssues = BIMManagerEngine.LoadJsonArray(issuesPath);
            var stingByGuid = new Dictionary<string, JToken>();
            foreach (var si in stingIssues)
            {
                string g = si["bcf_guid"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(g) && !stingByGuid.ContainsKey(g))
                    stingByGuid[g] = si;
            }

            // ── 2. Extract BCF archive ──
            string extractDir = Path.Combine(Path.GetTempPath(), $"STING_BCF_SYNC_{Guid.NewGuid():N}");
            var bcfTopics = new Dictionary<string, (XDocument markup, string dir)>();
            try
            {
                ZipFile.ExtractToDirectory(bcfPath, extractDir);
                foreach (string topicDir in Directory.GetDirectories(extractDir))
                {
                    string mp = Path.Combine(topicDir, "markup.bcf");
                    if (!File.Exists(mp)) continue;
                    try
                    {
                        var md = XDocument.Load(mp);
                        string guid = md.Root?.Element("Topic")?.Attribute("Guid")?.Value ?? "";
                        if (!string.IsNullOrEmpty(guid))
                            bcfTopics[guid] = (md, topicDir);
                    }
                    catch (Exception ex) { StingLog.Warn($"BCF sync: skip topic {Path.GetFileName(topicDir)}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); }
                catch (Exception cleanupEx) { StingLog.Warn($"BCF extract-dir cleanup suppressed: {cleanupEx.Message}"); }
                return $"Failed to extract BCF: {ex.Message}";
            }

            int importedNew = 0, updatedSting = 0, exportedNew = 0, updatedBcf = 0;

            // ── 3. Import / merge BCF → STING ──
            foreach (var kvp in bcfTopics)
            {
                string guid = kvp.Key;
                var markup = kvp.Value.markup;
                var topic = markup.Root?.Element("Topic");
                if (topic == null) continue;

                if (stingByGuid.TryGetValue(guid, out JToken existing))
                {
                    // Merge: BCF wins for fields it carries, STING keeps its extras
                    string bcfStatus = topic.Attribute("TopicStatus")?.Value ?? "";
                    string bcfTitle  = topic.Element("Title")?.Value ?? "";
                    string bcfAssign = topic.Element("AssignedTo")?.Value ?? "";
                    string bcfDesc   = topic.Element("Description")?.Value ?? "";

                    DateTime bcfMod = DateTime.MinValue;
                    DateTime.TryParse(topic.Element("ModifiedDate")?.Value, out bcfMod);
                    DateTime stingMod = DateTime.MinValue;
                    DateTime.TryParse(existing["date_raised"]?.ToString(), out stingMod);

                    // Most-recent-writer wins — only overwrite if BCF is newer
                    if (bcfMod > stingMod)
                    {
                        if (!string.IsNullOrEmpty(bcfTitle))
                            existing["title"] = bcfTitle;
                        if (!string.IsNullOrEmpty(bcfDesc))
                            existing["description"] = bcfDesc;
                        if (!string.IsNullOrEmpty(bcfAssign))
                            existing["assigned_to"] = bcfAssign;
                        if (bcfStatus == "Resolved" || bcfStatus == "Closed")
                            existing["status"] = "CLOSED";

                        // Merge any new BCF comments not already in STING
                        MergeBcfComments(markup, existing);
                        updatedSting++;
                    }
                }
                else
                {
                    // New BCF topic → import into STING
                    string nextId = BIMManagerEngine.GetNextIssueId(stingIssues, "BCF");
                    var newIssue = ParseBcfTopicToIssue(markup, nextId, doc);
                    if (newIssue != null)
                    {
                        stingIssues.Add(newIssue);
                        stingByGuid[guid] = newIssue;
                        importedNew++;
                    }
                }
            }

            // ── 4. Export new STING issues → BCF ──
            foreach (var si in stingIssues)
            {
                string existingGuid = si["bcf_guid"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(existingGuid) && bcfTopics.ContainsKey(existingGuid))
                {
                    // Already in BCF — check if STING is newer and update BCF markup
                    DateTime stingMod = DateTime.MinValue;
                    DateTime.TryParse(si["date_raised"]?.ToString(), out stingMod);
                    var (markup, tDir) = bcfTopics[existingGuid];
                    DateTime bcfMod = DateTime.MinValue;
                    DateTime.TryParse(markup.Root?.Element("Topic")?.Element("ModifiedDate")?.Value, out bcfMod);

                    if (stingMod > bcfMod)
                    {
                        // Overwrite the markup with STING data
                        var updated = CreateBcfMarkup(si, existingGuid);
                        updated.Save(Path.Combine(tDir, "markup.bcf"));
                        updatedBcf++;
                    }
                    continue;
                }

                // New STING issue with no BCF GUID — create a topic
                string newGuid = Guid.NewGuid().ToString();
                string topicDir = Path.Combine(extractDir, newGuid);
                Directory.CreateDirectory(topicDir);

                var newMarkup = CreateBcfMarkup(si, newGuid);
                newMarkup.Save(Path.Combine(topicDir, "markup.bcf"));

                var vp = CreateBcfViewpoint(Guid.NewGuid().ToString());
                vp.Save(Path.Combine(topicDir, "viewpoint.bcfv"));
                File.WriteAllBytes(Path.Combine(topicDir, "snapshot.png"), CreatePlaceholderPng());

                // Record the GUID back in STING so next sync recognises it
                si["bcf_guid"] = newGuid;
                exportedNew++;
            }

            // ── 5. Write updated BCF zip ──
            try
            {
                // Ensure bcf.version exists at root
                string versionPath = Path.Combine(extractDir, "bcf.version");
                if (!File.Exists(versionPath))
                    CreateBcfVersion().Save(versionPath);

                string projectPath = Path.Combine(extractDir, "project.bcfp");
                if (!File.Exists(projectPath))
                    CreateBcfProject(doc).Save(projectPath);

                // Replace original BCF file
                string backupPath = bcfPath + ".bak";
                if (File.Exists(bcfPath))
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Move(bcfPath, backupPath);
                }
                ZipFile.CreateFromDirectory(extractDir, bcfPath);
            }
            catch (Exception ex)
            {
                StingLog.Error("BCF sync: failed to write updated .bcfzip", ex);
            }

            // ── 6. Save updated STING issues ──
            BIMManagerEngine.SaveJsonFile(issuesPath, stingIssues);

            // ── 7. Save sync sidecar ──
            string sidecarPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "bcf_sync.json");
            var sidecar = new JObject
            {
                ["schema_version"] = 1,
                ["last_sync_utc"] = DateTime.UtcNow.ToString("o"),
                ["bcf_file"] = Path.GetFileName(bcfPath),
                ["user"] = Environment.UserName,
                ["imported_new"] = importedNew,
                ["updated_sting"] = updatedSting,
                ["exported_new"] = exportedNew,
                ["updated_bcf"] = updatedBcf,
                ["total_sting_issues"] = stingIssues.Count,
                ["total_bcf_topics"] = bcfTopics.Count + exportedNew
            };
            BIMManagerEngine.SaveJsonFile(sidecarPath, sidecar);

            // ── Cleanup ──
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); }
            catch (Exception cleanEx) { StingLog.Warn($"BCF sync cleanup: {cleanEx.Message}"); }

            StingLog.Info($"BCF sync complete: +{importedNew} imported, ↑{updatedSting} updated(STING), +{exportedNew} exported, ↑{updatedBcf} updated(BCF)");

            var sb = new StringBuilder();
            sb.AppendLine("BCF Bidirectional Sync Complete");
            sb.AppendLine("────────────────────────────────");
            sb.AppendLine($"BCF file:        {Path.GetFileName(bcfPath)}");
            sb.AppendLine($"New → STING:     {importedNew} issue(s) imported from BCF");
            sb.AppendLine($"Updated STING:   {updatedSting} existing issue(s) refreshed from BCF");
            sb.AppendLine($"New → BCF:       {exportedNew} issue(s) exported to BCF");
            sb.AppendLine($"Updated BCF:     {updatedBcf} existing topic(s) refreshed from STING");
            sb.AppendLine($"Total STING:     {stingIssues.Count} issues");
            sb.AppendLine($"Total BCF:       {bcfTopics.Count + exportedNew} topics");
            return sb.ToString();
        }

        /// <summary>Merge BCF comments into a STING issue, skipping duplicates by text+author match.</summary>
        private static void MergeBcfComments(XDocument markup, JToken stingIssue)
        {
            var bcfComments = markup.Root?.Elements("Comment");
            if (bcfComments == null) return;

            var existingComments = stingIssue["comments"] as JArray ?? new JArray();
            var existingSet = new HashSet<string>();
            foreach (var ec in existingComments)
                existingSet.Add($"{ec["author"]}|{ec["text"]}");

            foreach (var c in bcfComments)
            {
                string text = c.Element("Comment")?.Value ?? "";
                string author = c.Element("Author")?.Value ?? "";
                if (string.IsNullOrEmpty(text)) continue;

                string key = $"{author}|{text}";
                if (existingSet.Contains(key)) continue;

                string date = c.Element("Date")?.Value ?? "";
                if (DateTime.TryParse(date, out DateTime dt))
                    date = dt.ToString("yyyy-MM-dd HH:mm");

                existingComments.Add(new JObject
                {
                    ["text"] = text,
                    ["author"] = author,
                    ["date"] = date
                });
                existingSet.Add(key);
            }

            stingIssue["comments"] = existingComments;
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
        public string Category { get; set; }
        public string Suitability { get; set; }
        public string CDEState { get; set; }

        public DeliverableFile(string filePath, string description, string docType, string suitability, string cdeState)
        {
            FilePath = filePath;
            Description = description;
            DocType = docType;
            Category = docType; // default Category to DocType
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

                // Pack 0 — offline gate
                if (StingOfflineConfig.RefuseIfOffline("ACC Publish",
                    "CDE Package (BIM tab) creates a local ACC-ready bundle you can upload via the Autodesk web UI."))
                    return Result.Cancelled;

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

                // Phase 75: Create ISO 19650 CDE folder structure
                var cdeFolders = new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
                foreach (var f in cdeFolders) Directory.CreateDirectory(Path.Combine(packageDir, f));
                // Sub-folders per discipline
                var discFolders = new[] { "MODELS", "DRAWINGS", "SCHEDULES", "COBie", "REPORTS" };
                string cdeStatus = ParameterHelpers.GetString(doc.ProjectInformation, "ASS_CDE_STATUS_TXT") ?? "WIP";
                string targetCDE = cdeFolders.Contains(cdeStatus) ? cdeStatus : "WIP";
                foreach (var sf in discFolders) Directory.CreateDirectory(Path.Combine(packageDir, targetCDE, sf));

                // Copy deliverables to ISO 19650 folder structure
                int copied = 0;
                foreach (var d in deliverables)
                {
                    if (!File.Exists(d.FilePath)) continue;
                    // Route by file extension/category to appropriate sub-folder
                    string ext = Path.GetExtension(d.FilePath).ToLowerInvariant();
                    string subFolder = ext switch
                    {
                        ".rvt" or ".ifc" or ".nwc" or ".nwd" => "MODELS",
                        ".pdf" or ".dwg" or ".dxf" => "DRAWINGS",
                        ".xlsx" or ".csv" when (d.Category ?? "").Contains("Schedule") => "SCHEDULES",
                        ".xlsx" or ".csv" when (d.Category ?? "").Contains("COBie") => "COBie",
                        _ => "REPORTS"
                    };
                    string destDir = Path.Combine(packageDir, targetCDE, subFolder);
                    Directory.CreateDirectory(destDir);
                    string destFile = Path.Combine(destDir, Path.GetFileName(d.FilePath));
                    if (File.Exists(destFile))
                        destFile = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(d.FilePath)}_{copied}{Path.GetExtension(d.FilePath)}");
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

                // Show CDE package result in corporate WPF DataGrid dialog
                var resultRows = deliverables.Select(d => new
                {
                    FileName = Path.GetFileName(d.FilePath),
                    Category = d.Category ?? "—",
                    Status = File.Exists(d.FilePath) ? "Packaged" : "Missing",
                    NamingOk = PlatformLinkEngine.ValidateISO19650FileName(Path.GetFileName(d.FilePath), out _) ? "OK" : "Warning",
                    Size = File.Exists(d.FilePath) ? $"{new FileInfo(d.FilePath).Length / 1024.0:F0} KB" : "—"
                }).ToList();

                string statusLine = $"{copied} files packaged | {namingIssues.Count} naming warnings | {Path.GetFileName(packageDir)}";
                var resDlg = new UI.StingDataGridDialog("STING CDE Package — Complete", statusLine, 900, 480);
                resDlg.AddTextColumn("File Name", "FileName");
                resDlg.AddTextColumn("Category", "Category", 100);
                resDlg.AddTextColumn("Status", "Status", 80);
                resDlg.AddTextColumn("ISO 19650", "NamingOk", 80);
                resDlg.AddTextColumn("Size", "Size", 80);

                resDlg.AddActionButton("Open Folder", "OpenFolder");
                resDlg.AddActionButton("Close", "Cancel");

                resDlg.ActionClicked += action =>
                {
                    if (action == "OpenFolder")
                    {
                        try { Process.Start(new ProcessStartInfo { FileName = packageDir, UseShellExecute = true })?.Dispose(); }
                        catch (Exception ex) { StingLog.Warn($"Open folder: {ex.Message}"); }
                    }
                };

                resDlg.SetItems(resultRows);
                resDlg.SetStatus(statusLine);
                resDlg.ShowDialog();

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

                // Phase 95: delegate the BCF 2.1 ZIP assembly to BcfEngine, the
                // shared pure-C# serialiser that also runs server-side. No more
                // temp-directory shuffling — BcfEngine writes directly to disk
                // via ZipArchive. Snapshots are omitted from the shared engine
                // (the spec permits topics without snapshot.png); if a future
                // phase needs visual previews, the Revit-side ImageExport call
                // can be layered on top as an optional post-write step.
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string bcfPath = Path.Combine(bimDir, $"STING_Issues_{timestamp}.bcfzip");

                int exported;
                try
                {
                    var coordIssues = exportIssues
                        .Select(PlatformLinkEngine.StingIssueToCoord)
                        .Where(ci => ci != null)
                        .ToList();
                    exported = BcfEngine.Export(coordIssues, bcfPath);
                }
                catch (Exception zipEx)
                {
                    StingLog.Error("BcfEngine.Export failed", zipEx);
                    TaskDialog.Show("STING Error", $"BCF export failed: {zipEx.Message}");
                    return Result.Failed;
                }

                // Auto-register
                BIMManagerEngine.AutoRegisterExport(doc, bcfPath, "RP",
                    $"BCF 2.1 export — {exported} issues");

                long fileSize = new FileInfo(bcfPath).Length;
                string sizeStr = fileSize < 1024 * 1024
                    ? $"{fileSize / 1024.0:F1} KB"
                    : $"{fileSize / (1024.0 * 1024.0):F1} MB";

                StingLog.Info($"PlatformLink: BCF export complete — {exported} issues, {sizeStr}");

                // Reveal the containing folder so the coordinator can grab the .bcfzip
                // without leaving the dialog — mirrors Windows "Show in folder" UX.
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{bcfPath}\"") { UseShellExecute = true }); }
                catch (Exception openEx) { StingLog.Warn($"Open BCF folder: {openEx.Message}"); }

                var resultDlg = new TaskDialog("STING BCF Export — Complete");
                resultDlg.MainInstruction = $"BCF 2.1 export: {exported} issues";
                resultDlg.MainContent =
                    $"Issues exported: {exported}\n" +
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

                // If multiple BCF files, let user choose via searchable list
                string selectedBcf;
                if (bcfFiles.Count == 1)
                {
                    selectedBcf = bcfFiles[0];
                }
                else
                {
                    var bcfLabels = bcfFiles.Select(f =>
                    {
                        var fi = new FileInfo(f);
                        return $"{fi.Name} ({fi.Length / 1024.0:F0} KB, {fi.LastWriteTime:yyyy-MM-dd HH:mm})";
                    }).ToList();
                    string pick = StingListPicker.Show("BCF Import — Select File",
                        $"Found {bcfFiles.Count} BCF files. Select one to import:", bcfLabels);
                    if (pick == null) return Result.Cancelled;
                    int idx = bcfLabels.IndexOf(pick);
                    if (idx < 0 || idx >= bcfFiles.Count) return Result.Cancelled;
                    selectedBcf = bcfFiles[idx];
                }

                // Load existing issues
                string issuesPath = BIMManagerEngine.GetBIMManagerFilePath(doc, "issues.json");
                var existingIssues = BIMManagerEngine.LoadJsonArray(issuesPath);
                var existingGuids = new HashSet<string>(
                    existingIssues.Select(i => i["bcf_guid"]?.ToString() ?? "")
                                  .Where(g => !string.IsNullOrEmpty(g)),
                    StringComparer.OrdinalIgnoreCase);

                // Phase 95: delegate parsing to the shared BcfEngine. It never
                // throws, returns an empty list on malformed ZIPs, and ignores
                // viewpoints (stub camera data is not round-trippable).
                List<CoordIssue> parsed = BcfEngine.Import(selectedBcf);
                if (parsed.Count == 0)
                {
                    TaskDialog.Show("STING BCF Import",
                        $"No topics found in:\n{Path.GetFileName(selectedBcf)}\n\n" +
                        "The file may be malformed, empty, or not a valid BCF 2.1 archive.");
                    return Result.Cancelled;
                }

                // Build a review picker: users tick which topics to merge. Topics
                // already present in issues.json (matched by BCF GUID) are
                // pre-unselected with a "[duplicate]" detail tag so the coordinator
                // sees them but doesn't re-import by default.
                int duplicateCount = 0;
                var pickerItems = new List<StingListPicker.ListItem>();
                foreach (var ci in parsed)
                {
                    bool isDup = !string.IsNullOrEmpty(ci.Guid) && existingGuids.Contains(ci.Guid);
                    if (isDup) duplicateCount++;

                    string labelPrefix = isDup ? "[duplicate] " : "";
                    string label = $"{labelPrefix}{ci.Type} — {ci.Title ?? "(untitled)"}";
                    string detail = $"Priority: {ci.Priority}  |  Status: {ci.Status}  |  " +
                                    $"Author: {ci.Author ?? "?"}  |  GUID: {ci.Guid?.Substring(0, Math.Min(8, ci.Guid?.Length ?? 0))}";

                    pickerItems.Add(new StingListPicker.ListItem
                    {
                        Label      = label,
                        Detail     = detail,
                        Tag        = ci,
                        IsSelected = !isDup,   // default: import everything except duplicates
                    });
                }

                var selected = StingListPicker.Show(
                    "BCF Import — Review Topics",
                    $"{parsed.Count} topic(s) found in {Path.GetFileName(selectedBcf)}" +
                        (duplicateCount > 0 ? $"  ({duplicateCount} duplicate by GUID)" : "") +
                        "\nTick the topics you want to merge into this project's issues.json.",
                    pickerItems,
                    allowMultiSelect: true);

                if (selected == null || selected.Count == 0)
                {
                    StingLog.Info("PlatformLink: BCF import cancelled by user (no topics selected)");
                    return Result.Cancelled;
                }

                int imported = 0;
                int skipped = parsed.Count - selected.Count;

                foreach (var picked in selected)
                {
                    var ci = picked?.Tag as CoordIssue;
                    if (ci == null) continue;

                    // Skip duplicate GUIDs even if the coordinator accidentally
                    // re-ticked them — dedup is the non-negotiable half of BCF
                    // round-trip integrity.
                    if (!string.IsNullOrEmpty(ci.Guid) && existingGuids.Contains(ci.Guid))
                    {
                        skipped++;
                        continue;
                    }

                    string nextId = BIMManagerEngine.GetNextIssueId(existingIssues, "BCF");
                    var jo = PlatformLinkEngine.CoordToStingIssue(ci, nextId);
                    if (jo != null)
                    {
                        existingIssues.Add(jo);
                        if (!string.IsNullOrEmpty(ci.Guid)) existingGuids.Add(ci.Guid);
                        imported++;
                    }
                }

                if (imported > 0)
                    BIMManagerEngine.SaveJsonFile(issuesPath, existingIssues);

                StingLog.Info($"PlatformLink: BCF import complete — {imported} imported, {skipped} skipped (from {parsed.Count} topics in ZIP)");

                var resultDlg = new TaskDialog("STING BCF Import — Complete");
                resultDlg.MainInstruction = $"BCF import: {imported} issues imported";
                resultDlg.MainContent =
                    $"Source: {Path.GetFileName(selectedBcf)}\n" +
                    $"Topics in file: {parsed.Count}\n" +
                    $"Imported: {imported}\n" +
                    $"Skipped (duplicate/unselected): {skipped}\n" +
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
    //  4b. BCFSyncCommand — Bidirectional BCF 2.1 sync
    // ═══════════════════════════════════════════════════════════════

    #region ── BCF Sync ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BCFSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                var doc = ctx.Doc;

                // Scan BIM manager directory for .bcfzip files
                string bimDir = BIMManagerEngine.GetBIMManagerFilePath(doc, "");
                var bcfFiles = new List<string>();
                if (Directory.Exists(bimDir))
                {
                    bcfFiles.AddRange(Directory.GetFiles(bimDir, "*.bcfzip"));
                    bcfFiles.AddRange(Directory.GetFiles(bimDir, "*.bcf"));
                }

                // Also check project directory
                string docDir = Path.GetDirectoryName(doc.PathName) ?? "";
                if (!string.IsNullOrEmpty(docDir) && Directory.Exists(docDir))
                {
                    foreach (var f in Directory.GetFiles(docDir, "*.bcfzip"))
                        if (!bcfFiles.Contains(f)) bcfFiles.Add(f);
                    foreach (var f in Directory.GetFiles(docDir, "*.bcf"))
                        if (!bcfFiles.Contains(f)) bcfFiles.Add(f);
                }

                if (bcfFiles.Count == 0)
                {
                    TaskDialog.Show("STING BCF Sync",
                        "No .bcfzip files found in BIM Manager or project directory.\n\n" +
                        "Place a .bcfzip file in the project folder or STING_BIM_MANAGER directory and try again.");
                    return Result.Succeeded;
                }

                // Let user pick which BCF file to sync with
                string selectedBcf;
                if (bcfFiles.Count == 1)
                {
                    selectedBcf = bcfFiles[0];
                }
                else
                {
                    var displayNames = bcfFiles.Select(f => Path.GetFileName(f)).ToList();
                    string picked = Select.StingListPicker.Show("Select BCF File for Sync",
                        "Choose a .bcfzip file to synchronise with STING issues:",
                        displayNames);
                    if (string.IsNullOrEmpty(picked)) return Result.Cancelled;
                    int idx = displayNames.IndexOf(picked);
                    selectedBcf = idx >= 0 ? bcfFiles[idx] : bcfFiles[0];
                }

                // Run bidirectional sync
                string report = PlatformLinkEngine.RunBidirectionalBcfSync(doc, selectedBcf);

                TaskDialog.Show("STING BCF Sync", report);
                StingLog.Info("BCF bidirectional sync completed.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BCF sync failed", ex);
                TaskDialog.Show("STING Error", $"BCF sync failed: {ex.Message}");
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

                // Pack 0 — offline gate
                if (StingOfflineConfig.RefuseIfOffline("Platform Sync",
                    "Transmittal bundle (BIM tab) produces a file-based handover that can be shared manually."))
                    return Result.Cancelled;

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

        // ── Planscape Server Sync ──────────────────────────────────────────────
        /// <summary>
        /// Push all tagged elements in the document to the connected Planscape server.
        /// Called when the "Sync Now" button is pressed on the Planscape platform panel.
        /// Requires prior authentication via PlanscapeConnectCommand.
        /// </summary>
        internal static void SyncToPlanscapeServer(UIApplication app)
        {
            var client = PlanscapeServerClient.Instance;
            if (!client.IsConnected)
            {
                TaskDialog.Show("Planscape", "Not connected to Planscape server.\n\nUse the PLATFORM tab → Planscape → Connect to authenticate first.");
                return;
            }

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("Planscape", "No document open."); return; }

            // Load project ID from connection config
            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string cfgPath = Path.Combine(bimDir, "planscape_connection.json");
            Guid projectId = LoadPlanscapeProjectId(cfgPath);

            if (projectId == Guid.Empty)
            {
                TaskDialog.Show("Planscape", "No Planscape project linked.\n\nIn the Planscape connection settings, select or create a project on the server first.");
                return;
            }

            // Phase 91 — shared payload-build path (also used by PluginSyncTickBridge
            // on the scheduler's 5-min tick). Reads ASS_* parameters and maps them
            // onto Planscape.Shared.Models.TagElementSync.
            var payload = BuildPluginSyncPayload(doc, app, projectId);
            var tagSync = payload.TagElements ?? new List<Planscape.Shared.Models.TagElementSync>();

            if (tagSync.Count == 0)
            {
                TaskDialog.Show("Planscape", "No tagged elements found.\n\nRun Tag → Auto Tag or Batch Tag first to populate ASS_TAG_1 parameters.");
                return;
            }

            // Lazy-start the scheduler if the plugin connected after OnStartup.
            if (Planscape.PluginSync.SyncScheduler.Instance == null
                && !string.IsNullOrEmpty(client.ServerUrl)
                && !string.IsNullOrEmpty(client.AuthToken))
            {
                Planscape.PluginSync.SyncScheduler.Start(client.ServerUrl, client.AuthToken);
                PluginSyncTickBridge.EnsureWired();
                StingLog.Info($"Planscape: SyncScheduler lazy-started against {client.ServerUrl} (5-min tick)");
            }

            Planscape.Shared.Models.SyncResult sResult;
            try
            {
                sResult = Planscape.PluginSync.SyncScheduler.SyncNow(payload).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Planscape Sync Error", $"Sync failed: {ex.Message}");
                return;
            }

            if (!sResult.Success)
            {
                TaskDialog.Show("Planscape Sync Failed",
                    $"The scheduler could not reach the server:\n\n{sResult.ErrorMessage}\n\n" +
                    $"Payload was queued for automatic retry on the next 5-min sync tick.");
                return;
            }

            // Update sync timestamp (server-returned compliance metrics are now delivered
            // via the ComplianceHub — we store the request-side counts only).
            try
            {
                var cfg = File.Exists(cfgPath)
                    ? JObject.Parse(File.ReadAllText(cfgPath))
                    : new JObject();
                cfg["lastSyncAt"] = DateTime.UtcNow.ToString("o");
                cfg["lastSyncElements"] = tagSync.Count;
                File.WriteAllText(cfgPath, cfg.ToString(Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"Planscape sync timestamp update: {ex.Message}"); }

            TaskDialog.Show("Planscape Sync — Complete",
                $"Sync to Planscape server complete!\n\n" +
                $"Elements sent:    {tagSync.Count:N0}\n" +
                $"Drained payloads: {sResult.TagsCreated:N0}\n\n" +
                $"Server: {client.ServerUrl}\n" +
                $"User: {client.ConnectedUser}\n\n" +
                $"Compliance metrics will arrive on the ComplianceHub.");
        }

        /// <summary>
        /// Phase 91 — shared payload-build path. Iterates tagged elements in
        /// <paramref name="doc"/> and returns a <see cref="Planscape.Shared.Models.PluginSyncPayload"/>
        /// ready for enqueue/drain. Called by <see cref="SyncToPlanscapeServer"/>
        /// (Sync Now button) and by <see cref="PluginSyncTickBridge"/> on the
        /// scheduler's 5-min tick. Must run on the Revit API thread.
        /// </summary>
        internal static Planscape.Shared.Models.PluginSyncPayload BuildPluginSyncPayload(
            Document doc, UIApplication app, Guid projectId)
        {
            var client = PlanscapeServerClient.Instance;

            var elements = new List<TagElementPayload>();
            using (var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                foreach (Element el in collector)
                {
                    string tag1 = ParameterHelpers.GetString(el, "ASS_TAG_1") ?? "";
                    if (string.IsNullOrEmpty(tag1)) continue;

                    string disc = ParameterHelpers.GetString(el, "ASS_DISC") ?? "";
                    string loc  = ParameterHelpers.GetString(el, "ASS_LOC")  ?? "";
                    string zone = ParameterHelpers.GetString(el, "ASS_ZONE") ?? "";
                    string lvl  = ParameterHelpers.GetString(el, "ASS_LVL")  ?? "";
                    string sys  = ParameterHelpers.GetString(el, "ASS_SYS")  ?? "";
                    string func = ParameterHelpers.GetString(el, "ASS_FUNC") ?? "";
                    string prod = ParameterHelpers.GetString(el, "ASS_PROD") ?? "";
                    string seq  = ParameterHelpers.GetString(el, "ASS_SEQ")  ?? "";
                    string tag7 = ParameterHelpers.GetString(el, "ASS_TAG_7") ?? "";
                    string status = ParameterHelpers.GetString(el, "ASS_STATUS") ?? "";
                    string rev   = ParameterHelpers.GetString(el, "ASS_REV")    ?? "";
                    string cat   = ParameterHelpers.GetCategoryName(el);
                    string fam   = (el as FamilyInstance)?.Symbol?.FamilyName ?? "";

                    bool isComplete     = !string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(seq);
                    bool isFullyResolved = isComplete && !string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(lvl);

                    elements.Add(new TagElementPayload
                    {
                        RevitElementId  = el.Id.Value,
                        UniqueId        = el.UniqueId,
                        Disc            = disc, Loc = loc, Zone = zone, Lvl = lvl,
                        Sys = sys, Func = func, Prod = prod, Seq = seq,
                        Tag1 = tag1, Tag7 = string.IsNullOrEmpty(tag7) ? null : tag7,
                        CategoryName    = cat, FamilyName = fam,
                        Status          = string.IsNullOrEmpty(status) ? null : status,
                        Rev             = string.IsNullOrEmpty(rev) ? null : rev,
                        IsComplete      = isComplete, IsFullyResolved = isFullyResolved,
                        // INT-03 (Phase 91): per-element wall-clock timestamp from
                        // ASS_TAG_MODIFIED_DT audit trail, with DateTime.UtcNow
                        // fallback. Enables server-side delta detection.
                        LastModifiedUtc = ResolveElementLastModifiedUtc(el)
                    });
                }
            }

            string revitVer = app?.Application?.VersionNumber ?? "";
            string pluginVer = typeof(PlatformSyncCommand).Assembly.GetName().Version?.ToString() ?? "2.2.0";

            // Convert to the shared TagElementSync shape consumed by the scheduler.
            var tagSync = new List<Planscape.Shared.Models.TagElementSync>(elements.Count);
            foreach (var p in elements)
            {
                tagSync.Add(new Planscape.Shared.Models.TagElementSync
                {
                    RevitElementId  = p.RevitElementId,
                    UniqueId        = p.UniqueId ?? "",
                    Disc = p.Disc ?? "", Loc = p.Loc ?? "",
                    Zone = p.Zone ?? "", Lvl = p.Lvl ?? "",
                    Sys  = p.Sys ?? "",  Func = p.Func ?? "",
                    Prod = p.Prod ?? "", Seq  = p.Seq ?? "",
                    Tag1 = p.Tag1 ?? "", Tag7 = p.Tag7,
                    CategoryName = p.CategoryName ?? "",
                    FamilyName   = p.FamilyName ?? "",
                    Status       = p.Status,
                    Rev          = p.Rev,
                    IsComplete       = p.IsComplete,
                    IsFullyResolved  = p.IsFullyResolved,
                    // INT-03 (Phase 91): forward per-element timestamp into
                    // the Shared DTO so SyncClient → /api/tagsync/sync
                    // carries meaningful LastModifiedUtc on every element.
                    LastModifiedUtc  = p.LastModifiedUtc
                });
            }

            return new Planscape.Shared.Models.PluginSyncPayload
            {
                ProjectId     = projectId,
                UserName      = client.ConnectedUser ?? Environment.UserName,
                RevitVersion  = revitVer,
                PluginVersion = pluginVer,
                Timestamp     = DateTime.UtcNow,
                TagElements   = tagSync
            };
        }

        internal static Guid LoadPlanscapeProjectId(string cfgPath)
        {
            try
            {
                if (!File.Exists(cfgPath)) return Guid.Empty;
                var json = JObject.Parse(File.ReadAllText(cfgPath));
                string id = json["projectId"]?.Value<string>();
                return Guid.TryParse(id, out var g) ? g : Guid.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadPlanscapeProjectId: {ex.Message}");
                return Guid.Empty;
            }
        }

        /// <summary>
        /// Phase 91/INT-03 — resolve the wall-clock "last modified" time for a
        /// tagged element for the Planscape sync payload.
        /// Called from <see cref="BuildPluginSyncPayload"/>.
        /// Phase 100 fix: was previously only defined inside
        /// <c>PluginSyncTickBridge</c> (different class scope) so the call
        /// from <c>BuildPluginSyncPayload</c> produced CS0103. Duplicated here
        /// as a private static member of <c>PlatformSyncCommand</c> since the
        /// bridge never actually calls this method itself (it delegates to
        /// <c>BuildPluginSyncPayload</c> which owns the payload assembly).
        ///
        /// Priority chain:
        ///   1. <c>ASS_TAG_MODIFIED_DT</c> — STING audit-trail stamp written
        ///      by <c>TagPipelineHelper.RunFullPipeline</c> (Phase 77 #748).
        ///   2. <c>DateTime.UtcNow</c> — fallback so the server always sees a
        ///      non-null timestamp and can still last-write-wins-reconcile.
        /// </summary>
        private static DateTime ResolveElementLastModifiedUtc(Element el)
        {
            if (el == null) return DateTime.UtcNow;

            try
            {
                string stamp = ParameterHelpers.GetString(el, "ASS_TAG_MODIFIED_DT");
                if (!string.IsNullOrWhiteSpace(stamp)
                    && DateTime.TryParse(stamp,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal
                            | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveElementLastModifiedUtc: ASS_TAG_MODIFIED_DT parse failed on {el.Id.Value}: {ex.Message}");
            }

            return DateTime.UtcNow;
        }
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════
    //  Phase 91 — PluginSync Tick Bridge
    // ═══════════════════════════════════════════════════════════════
    #region ── PluginSync Tick Bridge ──

    /// <summary>
    /// Phase 91 (INT-01/INT-02) — bridges the <see cref="Planscape.PluginSync.SyncScheduler"/>
    /// 5-minute timer tick (which fires on a ThreadPool thread) to the Revit API
    /// thread via <see cref="ExternalEvent"/>. The scheduler invokes
    /// <c>SyncScheduler.OnTick</c> before each offline-queue drain; this bridge
    /// raises an external event so a handler can build a current-document
    /// payload on the Revit thread and enqueue it for the very next drain.
    ///
    /// Activates the previously-dead <c>Planscape.PluginSync</c> project — see
    /// CLAUDE.md § "DEAD CODE" note under Planscape.PluginSync.
    /// </summary>
    internal static class PluginSyncTickBridge
    {
        private static readonly object _lock = new object();
        private static bool _wired;
        private static ExternalEvent _tickEvent;
        private static SyncTickExternalEventHandler _tickHandler;

        /// <summary>Idempotent: first call creates the ExternalEvent and wires
        /// <see cref="Planscape.PluginSync.SyncScheduler.OnTick"/>. Subsequent
        /// calls are no-ops so the PlanscapeConnect, Sync Now, and OnStartup
        /// paths can all call this without stepping on each other.</summary>
        internal static void EnsureWired()
        {
            lock (_lock)
            {
                if (_wired) return;
                try
                {
                    _tickHandler = new SyncTickExternalEventHandler();
                    _tickEvent = ExternalEvent.Create(_tickHandler);
                    Planscape.PluginSync.SyncScheduler.OnTick = RaiseTick;
                    _wired = true;
                    StingLog.Info("PluginSyncTickBridge: wired — 5-min scheduler ticks will marshal to Revit API thread");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PluginSyncTickBridge.EnsureWired: {ex.Message}");
                }
            }
        }

        /// <summary>Fired by SyncScheduler on its Timer thread. MUST NOT touch
        /// Revit API — just raise the external event so the handler can run
        /// on the Revit API thread when it next goes idle.</summary>
        private static void RaiseTick()
        {
            try
            {
                var ev = _tickEvent;
                if (ev == null) { StingLog.Warn("PluginSyncTickBridge tick: ExternalEvent not created"); return; }
                StingLog.Info("PluginSyncTickBridge: 5-min tick — raising ExternalEvent to build payload on Revit thread");
                ev.Raise();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PluginSyncTickBridge.RaiseTick: {ex.Message}");
            }
        }

        /// <summary>Runs on the Revit API thread courtesy of ExternalEvent.
        /// Guards <c>app.ActiveUIDocument?.Document != null</c> per acceptance
        /// criteria — if no document is open the tick exits silently with a
        /// log line only (no TaskDialog, no exception). Builds a payload via
        /// <see cref="PlatformSyncCommand.BuildPluginSyncPayload"/> and enqueues
        /// it on the shared <see cref="Planscape.PluginSync.OfflineQueue"/>;
        /// the scheduler's drain step (already in progress on this tick, or
        /// the next one) will deliver it to the server.</summary>
        private sealed class SyncTickExternalEventHandler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                try
                {
                    // Guard (acceptance criterion 4) — no document, exit silently
                    var doc = app?.ActiveUIDocument?.Document;
                    if (doc == null)
                    {
                        StingLog.Info("PluginSyncTickBridge tick: no active document, skipping payload build");
                        return;
                    }

                    var client = PlanscapeServerClient.Instance;
                    if (!client.IsConnected)
                    {
                        StingLog.Info("PluginSyncTickBridge tick: Planscape client not authenticated, skipping payload build");
                        return;
                    }

                    string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                    string cfgPath = Path.Combine(bimDir, "planscape_connection.json");
                    Guid projectId = PlatformSyncCommand.LoadPlanscapeProjectId(cfgPath);
                    if (projectId == Guid.Empty)
                    {
                        StingLog.Info($"PluginSyncTickBridge tick: no Planscape project linked for {doc.Title}, skipping payload build");
                        return;
                    }

                    // Same payload-build path as SyncToPlanscapeServer (acceptance criterion 3)
                    var payload = PlatformSyncCommand.BuildPluginSyncPayload(doc, app, projectId);
                    int count = payload?.TagElements?.Count ?? 0;
                    if (count == 0)
                    {
                        StingLog.Info($"PluginSyncTickBridge tick: 0 tagged elements in {doc.Title}, nothing to enqueue");
                        return;
                    }

                    var queue = Planscape.PluginSync.OfflineQueue.Shared;
                    if (queue == null)
                    {
                        StingLog.Info("PluginSyncTickBridge tick: OfflineQueue.Shared is null (scheduler not started), skipping enqueue");
                        return;
                    }

                    queue.Enqueue(payload);
                    StingLog.Info($"PluginSyncTickBridge tick: enqueued payload with {count:N0} tagged elements for {doc.Title} (queue depth: {queue.Count})");
                }
                catch (Exception ex)
                {
                    // Must never crash — scheduler timer will keep firing and we need
                    // to keep logging silently per acceptance criterion 4.
                    StingLog.Warn($"PluginSyncTickBridge.Execute: {ex.Message}");
                }
            }

            public string GetName() => "STING PluginSync Tick";
        }

        // Phase 100: the duplicate ResolveElementLastModifiedUtc that used to
        // live here was unused inside PluginSyncTickBridge (nothing here calls
        // it) and was the cause of CS0103 from PlatformSyncCommand, because
        // private static is not cross-class visible. The canonical copy now
        // lives inside PlatformSyncCommand next to its sole caller
        // (BuildPluginSyncPayload). See PlatformSyncCommand for the
        // implementation and documentation.
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

                // Pack 0 — offline gate
                if (StingOfflineConfig.RefuseIfOffline("SharePoint / Teams Export",
                    "Document Package (BIM tab) writes the same deliverables to a local folder for manual upload."))
                    return Result.Cancelled;

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

    // ═══════════════════════════════════════════════════════════════════════════
    //  Planscape Server — Connect / Authenticate
    // ═══════════════════════════════════════════════════════════════════════════

    #region ── Planscape Connect ──

    /// <summary>
    /// Authenticates the current user with the Planscape server.
    /// The server URL, email, and project ID are passed via SetExtraParam before raising this command.
    /// Password is never written to disk — only held in memory for the Revit session.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlanscapeConnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                // Pack 0 — offline gate (this is the PlanscapeServerClient login entry point)
                if (StingOfflineConfig.RefuseIfOffline("Planscape Connect",
                    "Planscape login requires network access. Work offline with local BCF / transmittal flows."))
                    return Result.Cancelled;

                string serverUrl = StingCommandHandler.GetExtraParam("PlanscapeServerUrl") ?? "";
                string email     = StingCommandHandler.GetExtraParam("PlanscapeEmail")     ?? "";
                string password  = StingCommandHandler.GetExtraParam("PlanscapePassword")  ?? "";
                string projectId = StingCommandHandler.GetExtraParam("PlanscapeProjectId") ?? "";

                if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    TaskDialog.Show("Planscape Connect", "Please enter the server URL, email, and password.");
                    return Result.Cancelled;
                }

                // Blocking async call — safe in ExternalEvent context
                bool ok = PlanscapeServerClient.Instance
                    .LoginAsync(serverUrl.Trim(), email.Trim(), password)
                    .GetAwaiter().GetResult();

                if (!ok)
                {
                    TaskDialog.Show("Planscape Connect — Failed",
                        $"Authentication failed.\n\n{PlanscapeServerClient.Instance.LastError ?? "Unknown error"}\n\n" +
                        $"Please check:\n  • Server URL is reachable\n  • Email and password are correct\n  • Network connection is available");
                    return Result.Failed;
                }

                // Persist connection settings (no password stored)
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                    string cfgPath = Path.Combine(bimDir, "planscape_connection.json");
                    PlanscapeServerClient.Instance.SaveConnectionSettings(cfgPath, email.Trim());

                    // Save the project ID if provided
                    if (!string.IsNullOrWhiteSpace(projectId))
                    {
                        try
                        {
                            JObject cfg = File.Exists(cfgPath)
                                ? JObject.Parse(File.ReadAllText(cfgPath))
                                : new JObject();
                            cfg["projectId"] = projectId.Trim();
                            File.WriteAllText(cfgPath, cfg.ToString(Formatting.Indented));
                        }
                        catch { /* non-fatal */ }
                    }
                }

                var client = PlanscapeServerClient.Instance;

                // Phase 91 (INT-01/INT-02) — activate the previously-dead
                // Planscape.PluginSync.SyncScheduler so periodic background sync
                // starts running immediately after authentication. Guard per
                // acceptance criterion 1: only Start if Instance is null
                // (i.e. scheduler not already running). Start() is internally
                // idempotent too, so this is belt-and-braces.
                try
                {
                    if (Planscape.PluginSync.SyncScheduler.Instance == null)
                    {
                        Planscape.PluginSync.SyncScheduler.Start(client.ServerUrl, client.AuthToken);
                        StingLog.Info($"Planscape: SyncScheduler started against {client.ServerUrl} (5-min tick, offline queue enabled)");
                        PluginSyncTickBridge.EnsureWired();

                        // INT-07 — keep the dock-panel sync chip in step with each attempt.
                        if (Planscape.PluginSync.SyncScheduler.Instance != null)
                        {
                            Planscape.PluginSync.SyncScheduler.Instance.OnSyncComplete += _ =>
                            {
                                UI.StingDockPanel.LastInstance?.RefreshSyncIndicator();
                            };
                        }
                    }
                    else
                    {
                        StingLog.Info("Planscape: SyncScheduler already running, skipping start (re-auth refresh only)");
                        PluginSyncTickBridge.EnsureWired();
                    }
                }
                catch (Exception schEx)
                {
                    StingLog.Warn($"SyncScheduler start from PlanscapeConnect: {schEx.Message}");
                }

                TaskDialog.Show("Planscape — Connected",
                    $"✅ Successfully connected to Planscape!\n\n" +
                    $"Server:  {client.ServerUrl}\n" +
                    $"User:    {client.ConnectedUser}\n" +
                    $"Tier:    {client.TierName}\n" +
                    $"MIM:     {(client.MimEnabled ? "Enabled" : "Not enabled")}\n\n" +
                    "You can now use 'Sync Now' to push tagged elements to the server.");

                StingLog.Info($"Planscape: Connected — {client.ConnectedUser} @ {client.ServerUrl}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PlanscapeConnectCommand failed", ex);
                TaskDialog.Show("Planscape Connect Error", $"Connection error: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    #endregion
}
