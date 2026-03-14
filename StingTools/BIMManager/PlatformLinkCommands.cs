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
    //  Platform Integration — ACC, BCF 2.1, CDE, Procore, Trimble, SharePoint
    //
    //  Commands (12):
    //    ACCPublishCommand            — Package BIM deliverables for ACC/CDE upload
    //    CDEPackageCommand            — ISO 19650 CDE-ready deliverable package
    //    BCFExportCommand             — Export issues as BCF 2.1
    //    BCFImportCommand             — Import BCF issues into STING tracker
    //    PlatformSyncCommand          — Delta sync with external platforms
    //    SharePointExportCommand      — SharePoint/Teams folder structure export
    //    ProcorePackageCommand        — Procore-compatible submittal/RFI export
    //    TrimbleConnectExportCommand  — Trimble Connect exchange package
    //    AconexPackageCommand         — Aconex-ready document transmittal
    //    ProjectWiseExportCommand     — Bentley ProjectWise structure export
    //    PlatformDashboardCommand     — Unified platform integration status
    //    WebhookPayloadCommand        — Generate webhook-ready JSON payloads
    //
    //  Engine:
    //    PlatformEngine               — Shared utilities for all platform commands
    // ════════════════════════════════════════════════════════════════════════════

    #region ── PlatformEngine (shared utilities) ──

    internal static class PlatformEngine
    {
        // ISO 19650 document naming: {Project}-{Originator}-{Volume}-{Level}-{DocType}-{Discipline}-{Number}
        internal static string BuildDocName(string project, string originator, string volume,
            string level, string docType, string discipline, string number)
        {
            return $"{project}-{originator}-{volume}-{level}-{docType}-{discipline}-{number}";
        }

        // Get project code from doc or fallback
        internal static string GetProjectCode(Document doc)
        {
            string name = Path.GetFileNameWithoutExtension(doc.PathName ?? "PROJ");
            // Try to extract a code (first segment before dash or underscore)
            string[] parts = name.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && parts[0].Length <= 8 ? parts[0].ToUpperInvariant() : "PROJ";
        }

        // Build model statistics summary
        internal static JObject BuildModelStats(Document doc)
        {
            var stats = new JObject();
            try
            {
                var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && knownCats.Contains(e.Category.Name))
                    .ToList();

                int tagged = 0, untagged = 0, complete = 0, incomplete = 0;
                var byCat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var byDisc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var el in allElements)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!byCat.ContainsKey(cat)) byCat[cat] = 0;
                    byCat[cat]++;

                    string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    if (!string.IsNullOrEmpty(disc))
                    {
                        if (!byDisc.ContainsKey(disc)) byDisc[disc] = 0;
                        byDisc[disc]++;
                    }

                    string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1))
                        untagged++;
                    else
                    {
                        tagged++;
                        if (TagConfig.TagIsComplete(tag1)) complete++;
                        else incomplete++;
                    }
                }

                stats["total_elements"] = allElements.Count;
                stats["tagged"] = tagged;
                stats["untagged"] = untagged;
                stats["complete_tags"] = complete;
                stats["incomplete_tags"] = incomplete;
                stats["completeness_pct"] = allElements.Count > 0
                    ? Math.Round(100.0 * complete / allElements.Count, 1) : 0;
                stats["by_category"] = JObject.FromObject(byCat);
                stats["by_discipline"] = JObject.FromObject(byDisc);

                // Sheets and views
                int sheetCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).GetElementCount();
                int viewCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).GetElementCount();
                stats["sheets"] = sheetCount;
                stats["views"] = viewCount;

                // Revisions
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .ToList();
                stats["revisions"] = revisions.Count;
                if (revisions.Count > 0)
                    stats["latest_revision"] = revisions[0].Description ?? $"Rev {revisions[0].SequenceNumber}";

                // Levels
                int levelCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).GetElementCount();
                stats["levels"] = levelCount;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlatformEngine.BuildModelStats: {ex.Message}");
            }
            return stats;
        }

        // Build compliance summary for platform exports
        internal static JObject BuildComplianceSummary(Document doc)
        {
            var summary = new JObject();
            try
            {
                var result = ComplianceScan.Scan(doc);
                if (result != null)
                {
                    summary["rag_status"] = result.RAGStatus;
                    summary["tagged_complete"] = result.TaggedComplete;
                    summary["tagged_incomplete"] = result.TaggedIncomplete;
                    summary["untagged"] = result.Untagged;
                    summary["completeness_pct"] = result.CompliancePercent;
                    if (result.TopIssues != null)
                        summary["top_issues"] = result.TopIssues;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlatformEngine.BuildComplianceSummary: {ex.Message}");
            }
            return summary;
        }

        // Copy directory recursively
        internal static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        // Copy file if exists, increment counter
        internal static void CopyIfExists(string srcDir, string fileName, string destRoot,
            string destFolder, ref int count)
        {
            string src = Path.Combine(srcDir, fileName);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(destRoot, destFolder, fileName), true);
                count++;
            }
        }

        // Generate SHA-256 hash for file integrity verification
        internal static string ComputeFileHash(string filePath)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return ""; }
        }

        // BCF type mapping: STING → BCF 2.1
        internal static string MapIssueToBcfType(string stingType)
        {
            return (stingType?.ToUpperInvariant()) switch
            {
                "RFI" => "Request",
                "CLASH" => "Clash",
                "DESIGN" => "Fault",
                "NCR" => "Fault",
                "SNAGGING" => "Fault",
                "CHANGE" => "Request",
                "RISK" => "Remark",
                "ACTION" => "Request",
                _ => "Comment"
            };
        }

        // BCF type mapping: BCF 2.1 → STING
        internal static string MapBcfTypeToSting(string bcfType)
        {
            return (bcfType?.ToLowerInvariant()) switch
            {
                "clash" => "CLASH",
                "request" => "RFI",
                "fault" => "NCR",
                "remark" => "COMMENT",
                "inquiry" => "RFI",
                "error" => "NCR",
                "warning" => "RISK",
                "issue" => "DESIGN",
                _ => "COMMENT"
            };
        }

        // Procore type mapping
        internal static string MapIssueToProcore(string stingType)
        {
            return (stingType?.ToUpperInvariant()) switch
            {
                "RFI" => "RFI",
                "CLASH" => "Coordination Issue",
                "DESIGN" => "Design Issue",
                "NCR" => "Deficiency",
                "SNAGGING" => "Punch List",
                "CHANGE" => "Change Order",
                "RISK" => "Safety Issue",
                "ACTION" => "Action Item",
                _ => "Observation"
            };
        }
    }

    #endregion

    #region ── ACCPublishCommand ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ACCPublishCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("ACC Publish", "Save the project before publishing.");
                return Result.Failed;
            }

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName);
            string projectCode = PlatformEngine.GetProjectCode(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string packageDir = Path.Combine(bimDir, $"ACC_PUBLISH_{timestamp}");
            Directory.CreateDirectory(packageDir);

            var report = new StringBuilder();
            report.AppendLine("ACC / BIM 360 PUBLISH PACKAGE");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"Project: {projectName}");
            report.AppendLine($"Code: {projectCode}");
            report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"Standard: BS EN ISO 19650");
            report.AppendLine();

            int fileCount = 0;

            // Copy all BIM Manager deliverables
            string[] deliverables = {
                "project_bep.json", "project_dashboard.json", "issues.json",
                "document_register.json", "transmittals.json", "model_health.json"
            };
            string[] labels = {
                "BEP", "Dashboard", "Issues log", "Document register",
                "Transmittals", "Model Health"
            };

            for (int i = 0; i < deliverables.Length; i++)
            {
                string src = Path.Combine(bimDir, deliverables[i]);
                if (File.Exists(src))
                {
                    File.Copy(src, Path.Combine(packageDir, deliverables[i]), true);
                    fileCount++;
                    report.AppendLine($"  [OK] {labels[i]}");
                }
            }

            // Copy COBie data
            var cobieDirs = Directory.GetDirectories(bimDir, "COBie*");
            if (cobieDirs.Length > 0)
            {
                string latestCobie = cobieDirs.OrderByDescending(d => d).First();
                PlatformEngine.CopyDirectory(latestCobie, Path.Combine(packageDir, "COBie"));
                fileCount++;
                report.AppendLine("  [OK] COBie data");
            }

            // Copy revision snapshots
            foreach (var snap in Directory.GetFiles(bimDir, "revision_snapshot_*.json"))
            {
                File.Copy(snap, Path.Combine(packageDir, Path.GetFileName(snap)), true);
                fileCount++;
            }

            // Copy Excel exports
            string exportDir = OutputLocationHelper.GetOutputDirectory(doc);
            if (Directory.Exists(exportDir))
            {
                foreach (var xlsx in Directory.GetFiles(exportDir, "STING_*.xlsx").OrderByDescending(f => File.GetLastWriteTime(f)).Take(3))
                {
                    File.Copy(xlsx, Path.Combine(packageDir, Path.GetFileName(xlsx)), true);
                    fileCount++;
                    report.AppendLine($"  [OK] Excel: {Path.GetFileName(xlsx)}");
                }
            }

            // Build model statistics
            var modelStats = PlatformEngine.BuildModelStats(doc);
            var compliance = PlatformEngine.BuildComplianceSummary(doc);

            // Create ACC manifest with model context
            var manifest = new JObject
            {
                ["schema_version"] = "2.0",
                ["project"] = projectName,
                ["project_code"] = projectCode,
                ["generated"] = DateTime.Now.ToString("O"),
                ["generator"] = "StingTools V2.1",
                ["iso_standard"] = "BS EN ISO 19650",
                ["platform_target"] = "ACC / BIM 360",
                ["file_count"] = fileCount,
                ["model_statistics"] = modelStats,
                ["compliance"] = compliance,
                ["contents"] = new JArray(
                    Directory.GetFiles(packageDir, "*", SearchOption.AllDirectories)
                        .Select(f => new JObject
                        {
                            ["path"] = Path.GetRelativePath(packageDir, f),
                            ["size_bytes"] = new FileInfo(f).Length,
                            ["hash_sha256"] = PlatformEngine.ComputeFileHash(f)
                        })
                )
            };
            File.WriteAllText(Path.Combine(packageDir, "manifest.json"),
                manifest.ToString(Formatting.Indented));
            fileCount++;

            // Create ZIP archive
            string zipPath = packageDir + ".zip";
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(packageDir, zipPath);

            report.AppendLine();
            report.AppendLine($"Package: {fileCount} files");
            report.AppendLine($"ZIP: {Path.GetFileName(zipPath)} ({new FileInfo(zipPath).Length / 1024} KB)");
            report.AppendLine();
            report.AppendLine("Upload to ACC > Project Files or Docs module.");

            TaskDialog.Show("ACC Publish", report.ToString());
            StingLog.Info($"ACC publish: {fileCount} files → {zipPath}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── CDEPackageCommand (Enhanced) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CDEPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("CDE Package", "Save the project first.");
                return Result.Failed;
            }

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName);
            string projectCode = PlatformEngine.GetProjectCode(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd");

            // Create CDE folder structure per ISO 19650-1:2018 §12
            string cdeRoot = Path.Combine(bimDir, $"CDE_PACKAGE_{timestamp}");
            string[] containers = { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
            foreach (string c in containers)
                Directory.CreateDirectory(Path.Combine(cdeRoot, c));

            // Sub-folders per discipline
            string[] disciplines = { "Architecture", "Structure", "Mechanical", "Electrical", "Plumbing", "Fire", "General" };
            foreach (string c in containers)
                foreach (string d in disciplines)
                    Directory.CreateDirectory(Path.Combine(cdeRoot, c, d));

            var report = new StringBuilder();
            report.AppendLine("ISO 19650 CDE DELIVERABLE PACKAGE");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"Project: {projectName} ({projectCode})");
            report.AppendLine($"Structure: {containers.Length} containers × {disciplines.Length} disciplines");
            report.AppendLine();

            // Place deliverables with ISO 19650 naming convention
            // BEP → SHARED/General
            string bepSrc = Path.Combine(bimDir, "project_bep.json");
            if (File.Exists(bepSrc))
            {
                string bepDest = Path.Combine(cdeRoot, "SHARED", "General",
                    $"{projectCode}-STG-ZZ-XX-BEP-G-0001.json");
                File.Copy(bepSrc, bepDest, true);
                report.AppendLine("  SHARED/General: BEP");
            }

            // Issues → WIP/General
            string issuesSrc = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesSrc))
            {
                string issuesDest = Path.Combine(cdeRoot, "WIP", "General",
                    $"{projectCode}-STG-ZZ-XX-ISS-G-0001.json");
                File.Copy(issuesSrc, issuesDest, true);
                report.AppendLine("  WIP/General: Issues log");
            }

            // Document register → SHARED/General
            string docRegSrc = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docRegSrc))
            {
                string docRegDest = Path.Combine(cdeRoot, "SHARED", "General",
                    $"{projectCode}-STG-ZZ-XX-REG-G-0001.json");
                File.Copy(docRegSrc, docRegDest, true);
                report.AppendLine("  SHARED/General: Document register");
            }

            // Transmittals → SHARED/General
            string txSrc = Path.Combine(bimDir, "transmittals.json");
            if (File.Exists(txSrc))
            {
                string txDest = Path.Combine(cdeRoot, "SHARED", "General",
                    $"{projectCode}-STG-ZZ-XX-TXL-G-0001.json");
                File.Copy(txSrc, txDest, true);
                report.AppendLine("  SHARED/General: Transmittals");
            }

            // Build comprehensive CDE index with suitability codes
            var index = new JObject
            {
                ["schema_version"] = "2.0",
                ["project"] = projectName,
                ["project_code"] = projectCode,
                ["cde_structure"] = "ISO 19650-1:2018 §12",
                ["containers"] = new JArray(containers),
                ["disciplines"] = new JArray(disciplines),
                ["generated"] = DateTime.Now.ToString("O"),
                ["generator"] = "StingTools V2.1"
            };

            var files = new JArray();
            foreach (string file in Directory.GetFiles(cdeRoot, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "CDE_INDEX.json") continue;
                string rel = Path.GetRelativePath(cdeRoot, file);
                string[] pathParts = rel.Split(Path.DirectorySeparatorChar);
                string container = pathParts.Length > 0 ? pathParts[0] : "";

                string suitability = container switch
                {
                    "WIP" => "S0 — WIP",
                    "SHARED" => "S2 — Information",
                    "PUBLISHED" => "S3 — Costing/Procurement",
                    "ARCHIVE" => "S7 — As-built",
                    _ => "S0"
                };

                files.Add(new JObject
                {
                    ["path"] = rel,
                    ["container"] = container,
                    ["discipline"] = pathParts.Length > 1 ? pathParts[1] : "",
                    ["suitability_code"] = suitability,
                    ["revision"] = "P01",
                    ["hash_sha256"] = PlatformEngine.ComputeFileHash(file)
                });
            }
            index["files"] = files;
            index["model_statistics"] = PlatformEngine.BuildModelStats(doc);

            File.WriteAllText(Path.Combine(cdeRoot, "CDE_INDEX.json"),
                index.ToString(Formatting.Indented));

            report.AppendLine();
            report.AppendLine($"Package: {cdeRoot}");
            report.AppendLine($"Files: {files.Count}");

            TaskDialog.Show("CDE Package", report.ToString());
            StingLog.Info($"CDE package created: {cdeRoot}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── BCFExportCommand (Enhanced BCF 2.1) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BCFExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string issuesPath = Path.Combine(bimDir, "issues.json");

            if (!File.Exists(issuesPath))
            {
                TaskDialog.Show("BCF Export", "No issues found. Raise issues first using the Issue Tracker.");
                return Result.Failed;
            }

            JArray issues;
            try { issues = JArray.Parse(File.ReadAllText(issuesPath)); }
            catch (Exception ex)
            {
                TaskDialog.Show("BCF Export", $"Failed to read issues: {ex.Message}");
                return Result.Failed;
            }

            if (issues.Count == 0)
            {
                TaskDialog.Show("BCF Export", "No issues to export.");
                return Result.Succeeded;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string bcfPath = Path.Combine(bimDir, $"STING_ISSUES_{timestamp}.bcfzip");
            string tempDir = Path.Combine(Path.GetTempPath(), $"sting_bcf_{timestamp}");
            Directory.CreateDirectory(tempDir);

            try
            {
                string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Unknown");

                // bcf.version — BCF 2.1
                new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement("Version",
                        new XAttribute("VersionId", "2.1"),
                        new XElement("DetailedVersion", "2.1"))
                ).Save(Path.Combine(tempDir, "bcf.version"));

                // project.bcfp
                string projectGuid = Guid.NewGuid().ToString();
                new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement("ProjectExtension",
                        new XElement("Project",
                            new XAttribute("ProjectId", projectGuid),
                            new XElement("Name", projectName)),
                        new XElement("ExtensionSchema",
                            new XElement("TopicType", new JArray("Clash", "Request", "Fault", "Remark", "Comment", "Error").Select(t => new XElement("Type", t))),
                            new XElement("TopicStatus", new string[] { "OPEN", "IN_PROGRESS", "RESPONDED", "CLOSED", "VOID" }.Select(s => new XElement("Status", s))),
                            new XElement("Priority", new string[] { "CRITICAL", "HIGH", "MEDIUM", "LOW", "INFO" }.Select(p => new XElement("Priority", p)))
                        ))
                ).Save(Path.Combine(tempDir, "project.bcfp"));

                int exported = 0;
                foreach (JObject issue in issues)
                {
                    string issueId = issue["id"]?.ToString() ?? Guid.NewGuid().ToString();
                    string topicGuid = Guid.NewGuid().ToString();
                    string topicDir = Path.Combine(tempDir, topicGuid);
                    Directory.CreateDirectory(topicDir);

                    string title = issue["title"]?.ToString() ?? "Untitled Issue";
                    string description = issue["description"]?.ToString() ?? "";
                    string type = issue["type"]?.ToString() ?? "COMMENT";
                    string status = issue["status"]?.ToString() ?? "OPEN";
                    string priority = issue["priority"]?.ToString() ?? "MEDIUM";
                    string assignedTo = issue["assigned_to"]?.ToString() ?? "";
                    string createdDate = issue["created"]?.ToString() ?? DateTime.Now.ToString("O");
                    string discipline = issue["discipline"]?.ToString() ?? "";

                    // Build markup with full BCF 2.1 compliance
                    var topicElement = new XElement("Topic",
                        new XAttribute("Guid", topicGuid),
                        new XAttribute("TopicType", PlatformEngine.MapIssueToBcfType(type)),
                        new XAttribute("TopicStatus", status),
                        new XElement("Title", title),
                        new XElement("Description", description),
                        new XElement("Priority", priority),
                        new XElement("AssignedTo", assignedTo),
                        new XElement("CreationDate", createdDate),
                        new XElement("CreationAuthor", "StingTools V2.1"),
                        new XElement("ModifiedDate", DateTime.Now.ToString("O")),
                        new XElement("ReferenceLink", $"STING-{issueId}"));

                    // Add labels for discipline/type
                    if (!string.IsNullOrEmpty(discipline))
                        topicElement.Add(new XElement("Labels", new XElement("Label", discipline)));
                    topicElement.Add(new XElement("Labels", new XElement("Label", type)));

                    // Add element references (component links for BCF)
                    var elementIds = issue["element_ids"] as JArray;
                    if (elementIds != null && elementIds.Count > 0)
                    {
                        var viewpoint = new XElement("Viewpoints",
                            new XElement("ViewPoint",
                                new XAttribute("Guid", Guid.NewGuid().ToString())));
                        topicElement.Add(viewpoint);

                        // Write viewpoint with component selections
                        var vpXml = new XDocument(
                            new XDeclaration("1.0", "UTF-8", null),
                            new XElement("VisualizationInfo",
                                new XAttribute("Guid", Guid.NewGuid().ToString()),
                                new XElement("Components",
                                    new XElement("Selection",
                                        elementIds.Select(eid =>
                                            new XElement("Component",
                                                new XAttribute("IfcGuid", eid.ToString()),
                                                new XElement("OriginatingSystem", "Revit"),
                                                new XElement("AuthoringToolId", eid.ToString())))))));
                        vpXml.Save(Path.Combine(topicDir, "viewpoint.bcfv"));
                    }

                    // Build comments from issue history
                    var commentsElements = new List<XElement>();
                    var comments = issue["comments"] as JArray;
                    if (comments != null)
                    {
                        foreach (JObject comment in comments)
                        {
                            commentsElements.Add(new XElement("Comment",
                                new XAttribute("Guid", Guid.NewGuid().ToString()),
                                new XElement("Date", comment["date"]?.ToString() ?? DateTime.Now.ToString("O")),
                                new XElement("Author", comment["author"]?.ToString() ?? "StingTools"),
                                new XElement("Comment", comment["text"]?.ToString() ?? ""),
                                new XElement("Topic", new XAttribute("Guid", topicGuid))));
                        }
                    }

                    if (commentsElements.Count == 0)
                    {
                        commentsElements.Add(new XElement("Comment",
                            new XAttribute("Guid", Guid.NewGuid().ToString()),
                            new XElement("Date", createdDate),
                            new XElement("Author", "StingTools V2.1"),
                            new XElement("Comment", description),
                            new XElement("Topic", new XAttribute("Guid", topicGuid))));
                    }

                    var markupXml = new XDocument(
                        new XDeclaration("1.0", "UTF-8", null),
                        new XElement("Markup", topicElement, commentsElements));
                    markupXml.Save(Path.Combine(topicDir, "markup.bcf"));
                    exported++;
                }

                if (File.Exists(bcfPath)) File.Delete(bcfPath);
                ZipFile.CreateFromDirectory(tempDir, bcfPath);

                TaskDialog.Show("BCF Export",
                    $"Exported {exported} issues as BCF 2.1.\n\n" +
                    $"File: {Path.GetFileName(bcfPath)}\n" +
                    $"Size: {new FileInfo(bcfPath).Length / 1024} KB\n\n" +
                    "Compatible with:\n" +
                    "  Navisworks, Solibri, BIMcollab, Trimble Connect,\n" +
                    "  Tekla BIMsight, SimpleBIM, Dalux, Procore.");
                StingLog.Info($"BCF export: {exported} issues → {bcfPath}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── BCFImportCommand ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BCFImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import BCF File",
                Filter = "BCF Files (*.bcfzip)|*.bcfzip|All Files (*.*)|*.*",
                DefaultExt = ".bcfzip"
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string issuesPath = Path.Combine(bimDir, "issues.json");
            string tempDir = Path.Combine(Path.GetTempPath(), $"sting_bcf_import_{DateTime.Now:yyyyMMddHHmmss}");

            try
            {
                ZipFile.ExtractToDirectory(dlg.FileName, tempDir);

                JArray existingIssues = File.Exists(issuesPath)
                    ? JArray.Parse(File.ReadAllText(issuesPath))
                    : new JArray();

                // Build dedup set from existing BCF GUIDs
                var existingGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JObject existing in existingIssues)
                {
                    string guid = existing["bcf_topic_guid"]?.ToString();
                    if (!string.IsNullOrEmpty(guid)) existingGuids.Add(guid);
                }

                int imported = 0, skipped = 0;

                foreach (string topicDir in Directory.GetDirectories(tempDir))
                {
                    string markupPath = Path.Combine(topicDir, "markup.bcf");
                    if (!File.Exists(markupPath)) continue;

                    try
                    {
                        var markupXml = XDocument.Load(markupPath);
                        var topic = markupXml.Root?.Element("Topic");
                        if (topic == null) continue;

                        string topicGuid = topic.Attribute("Guid")?.Value ?? "";

                        // Skip duplicates
                        if (!string.IsNullOrEmpty(topicGuid) && existingGuids.Contains(topicGuid))
                        {
                            skipped++;
                            continue;
                        }

                        string title = topic.Element("Title")?.Value ?? "Imported BCF Issue";
                        string description = topic.Element("Description")?.Value ?? "";
                        string bcfType = topic.Attribute("TopicType")?.Value ?? "Comment";
                        string status = topic.Attribute("TopicStatus")?.Value ?? "OPEN";
                        string priority = topic.Element("Priority")?.Value ?? "MEDIUM";
                        string assignedTo = topic.Element("AssignedTo")?.Value ?? "";

                        string stingType = PlatformEngine.MapBcfTypeToSting(bcfType);
                        int nextId = existingIssues.Count + 1;

                        // Import comments from BCF
                        var bcfComments = new JArray();
                        foreach (var commentEl in markupXml.Root.Elements("Comment"))
                        {
                            bcfComments.Add(new JObject
                            {
                                ["date"] = commentEl.Element("Date")?.Value ?? "",
                                ["author"] = commentEl.Element("Author")?.Value ?? "BCF Import",
                                ["text"] = commentEl.Element("Comment")?.Value ?? ""
                            });
                        }

                        // Extract element IDs from viewpoint if present
                        var elementIds = new JArray();
                        string vpPath = Path.Combine(topicDir, "viewpoint.bcfv");
                        if (File.Exists(vpPath))
                        {
                            try
                            {
                                var vpXml = XDocument.Load(vpPath);
                                foreach (var comp in vpXml.Descendants("Component"))
                                {
                                    string authId = comp.Element("AuthoringToolId")?.Value;
                                    if (!string.IsNullOrEmpty(authId))
                                        elementIds.Add(authId);
                                }
                            }
                            catch { }
                        }

                        var newIssue = new JObject
                        {
                            ["id"] = $"ISS-{nextId:D4}",
                            ["title"] = title,
                            ["description"] = description,
                            ["type"] = stingType,
                            ["status"] = status,
                            ["priority"] = priority,
                            ["assigned_to"] = assignedTo,
                            ["created"] = DateTime.Now.ToString("O"),
                            ["source"] = "BCF Import",
                            ["bcf_file"] = Path.GetFileName(dlg.FileName),
                            ["bcf_topic_guid"] = topicGuid,
                            ["element_ids"] = elementIds,
                            ["comments"] = bcfComments
                        };

                        existingIssues.Add(newIssue);
                        existingGuids.Add(topicGuid);
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BCF import topic '{Path.GetFileName(topicDir)}': {ex.Message}");
                    }
                }

                File.WriteAllText(issuesPath, existingIssues.ToString(Formatting.Indented));

                var resultMsg = $"Imported {imported} issues from BCF file.\n";
                if (skipped > 0) resultMsg += $"Skipped {skipped} duplicates (already imported).\n";
                resultMsg += $"\nSource: {Path.GetFileName(dlg.FileName)}\nTotal issues: {existingIssues.Count}";

                TaskDialog.Show("BCF Import", resultMsg);
                StingLog.Info($"BCF import: {imported} new, {skipped} skipped from {dlg.FileName}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BCF Import", $"Failed to import BCF: {ex.Message}");
                StingLog.Error("BCF import failed", ex);
                return Result.Failed;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── PlatformSyncCommand (Enhanced) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlatformSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string syncConfigPath = Path.Combine(bimDir, "platform_sync.json");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string deltaPath = Path.Combine(bimDir, $"sync_delta_{timestamp}.json");

            // Load or create sync config
            JObject syncConfig;
            if (File.Exists(syncConfigPath))
                syncConfig = JObject.Parse(File.ReadAllText(syncConfigPath));
            else
                syncConfig = new JObject
                {
                    ["last_sync"] = "",
                    ["platform"] = "manual",
                    ["webhook_url"] = "",
                    ["auto_sync"] = false,
                    ["sync_history"] = new JArray()
                };

            string lastSync = syncConfig["last_sync"]?.ToString() ?? "";
            DateTime lastSyncDate = string.IsNullOrEmpty(lastSync)
                ? DateTime.MinValue
                : DateTime.TryParse(lastSync, out DateTime dt) ? dt : DateTime.MinValue;

            // Build comprehensive delta
            var delta = new JObject
            {
                ["sync_timestamp"] = DateTime.Now.ToString("O"),
                ["previous_sync"] = lastSync,
                ["generator"] = "StingTools V2.1",
                ["project"] = Path.GetFileNameWithoutExtension(doc.PathName ?? "")
            };

            // File-level changes
            var fileChanges = new JArray();
            string[] dataFiles = {
                "project_bep.json", "issues.json", "document_register.json",
                "transmittals.json", "project_dashboard.json", "model_health.json"
            };

            foreach (string fileName in dataFiles)
            {
                string filePath = Path.Combine(bimDir, fileName);
                if (!File.Exists(filePath)) continue;

                var fileInfo = new FileInfo(filePath);
                bool modified = fileInfo.LastWriteTime > lastSyncDate;
                fileChanges.Add(new JObject
                {
                    ["file"] = fileName,
                    ["modified"] = fileInfo.LastWriteTime.ToString("O"),
                    ["size_bytes"] = fileInfo.Length,
                    ["status"] = modified ? "MODIFIED" : "UNCHANGED",
                    ["hash"] = modified ? PlatformEngine.ComputeFileHash(filePath) : ""
                });
            }
            delta["file_changes"] = fileChanges;

            // Model-level changes (element count diff)
            var modelStats = PlatformEngine.BuildModelStats(doc);
            delta["model_snapshot"] = modelStats;

            // Issue summary delta
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
            {
                try
                {
                    var issues = JArray.Parse(File.ReadAllText(issuesPath));
                    int open = issues.Count(i => (i["status"]?.ToString() ?? "OPEN") == "OPEN");
                    int closed = issues.Count(i => (i["status"]?.ToString()) == "CLOSED");
                    int newSinceSync = issues.Count(i =>
                    {
                        string created = i["created"]?.ToString() ?? "";
                        return DateTime.TryParse(created, out DateTime cd) && cd > lastSyncDate;
                    });

                    delta["issues_summary"] = new JObject
                    {
                        ["total"] = issues.Count,
                        ["open"] = open,
                        ["closed"] = closed,
                        ["new_since_last_sync"] = newSinceSync
                    };
                }
                catch { }
            }

            int modifiedCount = fileChanges.Count(c => c["status"]?.ToString() == "MODIFIED");
            delta["change_count"] = modifiedCount;

            // Save delta
            File.WriteAllText(deltaPath, delta.ToString(Formatting.Indented));

            // Update sync history
            var history = syncConfig["sync_history"] as JArray ?? new JArray();
            history.Add(new JObject
            {
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["changes"] = modifiedCount,
                ["delta_file"] = Path.GetFileName(deltaPath)
            });
            // Keep last 50 entries
            while (history.Count > 50) history.RemoveAt(0);
            syncConfig["sync_history"] = history;
            syncConfig["last_sync"] = DateTime.Now.ToString("O");
            File.WriteAllText(syncConfigPath, syncConfig.ToString(Formatting.Indented));

            var report = new StringBuilder();
            report.AppendLine("PLATFORM SYNC");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"Previous sync: {(string.IsNullOrEmpty(lastSync) ? "Never" : lastSync)}");
            report.AppendLine($"Files changed: {modifiedCount} of {dataFiles.Length}");
            report.AppendLine();

            foreach (JObject change in fileChanges)
            {
                string status = change["status"]?.ToString() ?? "";
                string marker = status == "MODIFIED" ? "[MODIFIED]" : "[unchanged]";
                report.AppendLine($"  {marker} {change["file"]}");
            }

            report.AppendLine();
            report.AppendLine($"Delta export: {Path.GetFileName(deltaPath)}");
            report.AppendLine($"Sync history: {history.Count} entries");

            TaskDialog.Show("Platform Sync", report.ToString());
            StingLog.Info($"Platform sync: {modifiedCount} changes since {lastSync}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── SharePointExportCommand (Enhanced) ──

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SharePointExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Project");
            string projectCode = PlatformEngine.GetProjectCode(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd");
            string spRoot = Path.Combine(bimDir, $"SharePoint_{timestamp}");

            // SharePoint/Teams-compatible folder structure per ISO 19650
            string[] folders = {
                "01_WIP", "02_SHARED", "03_PUBLISHED", "04_ARCHIVE",
                "05_BEP", "06_COBie", "07_Issues", "08_Transmittals",
                "09_Excel_Data", "10_Revisions"
            };

            foreach (string f in folders)
                Directory.CreateDirectory(Path.Combine(spRoot, f));

            int copied = 0;

            // Distribute files
            PlatformEngine.CopyIfExists(bimDir, "project_bep.json", spRoot, "05_BEP", ref copied);
            PlatformEngine.CopyIfExists(bimDir, "issues.json", spRoot, "07_Issues", ref copied);
            PlatformEngine.CopyIfExists(bimDir, "transmittals.json", spRoot, "08_Transmittals", ref copied);
            PlatformEngine.CopyIfExists(bimDir, "document_register.json", spRoot, "02_SHARED", ref copied);
            PlatformEngine.CopyIfExists(bimDir, "project_dashboard.json", spRoot, "02_SHARED", ref copied);
            PlatformEngine.CopyIfExists(bimDir, "model_health.json", spRoot, "02_SHARED", ref copied);

            // Copy Excel exports
            string exportDir = OutputLocationHelper.GetOutputDirectory(doc);
            if (Directory.Exists(exportDir))
            {
                foreach (var xlsx in Directory.GetFiles(exportDir, "STING_*.xlsx")
                    .OrderByDescending(f => File.GetLastWriteTime(f)).Take(5))
                {
                    File.Copy(xlsx, Path.Combine(spRoot, "09_Excel_Data", Path.GetFileName(xlsx)), true);
                    copied++;
                }
            }

            // Copy revision snapshots
            foreach (var snap in Directory.GetFiles(bimDir, "revision_snapshot_*.json"))
            {
                File.Copy(snap, Path.Combine(spRoot, "10_Revisions", Path.GetFileName(snap)), true);
                copied++;
            }

            // Generate corporate HTML dashboard
            var modelStats = PlatformEngine.BuildModelStats(doc);
            var compliance = PlatformEngine.BuildComplianceSummary(doc);

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html lang='en'><head>");
            html.AppendLine("<meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.AppendLine($"<title>{projectName} — BIM Dashboard</title>");
            html.AppendLine("<style>");
            html.AppendLine("* { box-sizing: border-box; margin: 0; padding: 0; }");
            html.AppendLine("body { font-family: 'Segoe UI', -apple-system, sans-serif; background: #f0f2f5; color: #333; }");
            html.AppendLine(".header { background: linear-gradient(135deg, #582C83, #7B1FA2); color: white; padding: 30px 40px; }");
            html.AppendLine(".header h1 { font-size: 24px; font-weight: 300; } .header .code { opacity: 0.8; font-size: 14px; }");
            html.AppendLine(".container { max-width: 1200px; margin: 0 auto; padding: 20px; }");
            html.AppendLine(".grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin: 16px 0; }");
            html.AppendLine(".card { background: white; border-radius: 12px; padding: 24px; box-shadow: 0 2px 8px rgba(0,0,0,0.08); }");
            html.AppendLine(".card h2 { font-size: 16px; color: #582C83; margin-bottom: 12px; border-bottom: 2px solid #E1BEE7; padding-bottom: 8px; }");
            html.AppendLine(".metric { font-size: 32px; font-weight: 700; color: #582C83; } .metric-label { font-size: 12px; color: #888; text-transform: uppercase; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 8px; }");
            html.AppendLine("th { background: #582C83; color: white; padding: 10px 12px; text-align: left; font-size: 13px; }");
            html.AppendLine("td { padding: 8px 12px; border-bottom: 1px solid #eee; font-size: 13px; }");
            html.AppendLine("tr:hover { background: #f8f4fc; }");
            html.AppendLine(".badge { display: inline-block; padding: 3px 10px; border-radius: 12px; font-size: 12px; font-weight: 600; color: white; }");
            html.AppendLine(".green { background: #4CAF50; } .amber { background: #FF9800; } .red { background: #F44336; } .blue { background: #2196F3; }");
            html.AppendLine(".footer { text-align: center; padding: 20px; color: #999; font-size: 12px; }");
            html.AppendLine("</style></head><body>");

            html.AppendLine($"<div class='header'><h1>{projectName}</h1>");
            html.AppendLine($"<div class='code'>Project Code: {projectCode} | ISO 19650 | Generated: {DateTime.Now:yyyy-MM-dd HH:mm}</div></div>");

            html.AppendLine("<div class='container'>");

            // Key metrics cards
            html.AppendLine("<div class='grid'>");
            html.AppendLine($"<div class='card'><div class='metric-label'>Total Elements</div><div class='metric'>{modelStats["total_elements"] ?? 0}</div></div>");
            html.AppendLine($"<div class='card'><div class='metric-label'>Tag Completeness</div><div class='metric'>{modelStats["completeness_pct"] ?? 0}%</div></div>");
            html.AppendLine($"<div class='card'><div class='metric-label'>Sheets</div><div class='metric'>{modelStats["sheets"] ?? 0}</div></div>");
            html.AppendLine($"<div class='card'><div class='metric-label'>Revisions</div><div class='metric'>{modelStats["revisions"] ?? 0}</div></div>");
            html.AppendLine("</div>");

            // Compliance status
            string ragStatus = compliance["rag_status"]?.ToString() ?? "Unknown";
            string ragClass = ragStatus == "Green" ? "green" : ragStatus == "Amber" ? "amber" : "red";
            html.AppendLine("<div class='card'><h2>Compliance Status</h2>");
            html.AppendLine($"<p>Overall: <span class='badge {ragClass}'>{ragStatus}</span></p>");
            html.AppendLine($"<p>Complete: {compliance["tagged_complete"] ?? 0} | Incomplete: {compliance["tagged_incomplete"] ?? 0} | Untagged: {compliance["untagged"] ?? 0}</p>");
            html.AppendLine("</div>");

            // File index
            html.AppendLine("<div class='card'><h2>Deliverables</h2>");
            html.AppendLine("<table><tr><th>Folder</th><th>File</th><th>Size</th></tr>");
            foreach (string folder in folders)
            {
                string folderPath = Path.Combine(spRoot, folder);
                if (!Directory.Exists(folderPath)) continue;
                foreach (string file in Directory.GetFiles(folderPath))
                {
                    long size = new FileInfo(file).Length;
                    string sizeStr = size < 1024 ? $"{size} B" : $"{size / 1024} KB";
                    html.AppendLine($"<tr><td>{folder}</td><td>{Path.GetFileName(file)}</td><td>{sizeStr}</td></tr>");
                }
            }
            html.AppendLine("</table></div>");

            html.AppendLine($"<div class='footer'>StingTools V2.1 — BS EN ISO 19650 Compliant BIM Management</div>");
            html.AppendLine("</div></body></html>");

            File.WriteAllText(Path.Combine(spRoot, "index.html"), html.ToString());
            copied++;

            // SharePoint metadata XML
            var metadataXml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("DocumentLibrary",
                    new XElement("Project", projectName),
                    new XElement("ProjectCode", projectCode),
                    new XElement("Standard", "BS EN ISO 19650"),
                    new XElement("Generator", "StingTools V2.1"),
                    new XElement("Date", DateTime.Now.ToString("yyyy-MM-dd")),
                    new XElement("ModelStats",
                        new XElement("Elements", modelStats["total_elements"]?.ToString() ?? "0"),
                        new XElement("Completeness", $"{modelStats["completeness_pct"] ?? 0}%"),
                        new XElement("Sheets", modelStats["sheets"]?.ToString() ?? "0")),
                    new XElement("Columns",
                        new XElement("Column", new XAttribute("Name", "DocumentType"), new XAttribute("Type", "Text")),
                        new XElement("Column", new XAttribute("Name", "Suitability"), new XAttribute("Type", "Choice"),
                            new XAttribute("Choices", "S0|S1|S2|S3|S4|S6|S7")),
                        new XElement("Column", new XAttribute("Name", "Revision"), new XAttribute("Type", "Text")),
                        new XElement("Column", new XAttribute("Name", "CDEContainer"), new XAttribute("Type", "Choice"),
                            new XAttribute("Choices", "WIP|SHARED|PUBLISHED|ARCHIVE")),
                        new XElement("Column", new XAttribute("Name", "Discipline"), new XAttribute("Type", "Text")))));
            metadataXml.Save(Path.Combine(spRoot, "metadata.xml"));
            copied++;

            TaskDialog.Show("SharePoint Export",
                $"SharePoint/Teams package created.\n\n" +
                $"Files: {copied}\nFolders: {folders.Length}\n" +
                $"Location: {spRoot}\n\n" +
                "Upload to SharePoint/Teams for team access.\n" +
                "index.html provides a corporate web dashboard.");

            StingLog.Info($"SharePoint export: {copied} files → {spRoot}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── ProcorePackageCommand ──

    /// <summary>
    /// Export BIM data as Procore-compatible submittal and RFI packages.
    /// Creates JSON structures matching Procore's REST API schema for bulk import.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProcorePackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Project");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outDir = Path.Combine(bimDir, $"Procore_{timestamp}");
            Directory.CreateDirectory(outDir);

            int fileCount = 0;
            var report = new StringBuilder();
            report.AppendLine("PROCORE EXPORT PACKAGE");
            report.AppendLine(new string('═', 50));

            // Export issues as Procore RFIs
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
            {
                try
                {
                    var issues = JArray.Parse(File.ReadAllText(issuesPath));
                    var procoreRfis = new JArray();
                    var procorePunchList = new JArray();

                    foreach (JObject issue in issues)
                    {
                        string type = issue["type"]?.ToString() ?? "COMMENT";
                        var procoreItem = new JObject
                        {
                            ["subject"] = issue["title"]?.ToString() ?? "",
                            ["question"] = issue["description"]?.ToString() ?? "",
                            ["status"] = MapStatusToProcore(issue["status"]?.ToString()),
                            ["priority"] = issue["priority"]?.ToString()?.ToLowerInvariant() ?? "normal",
                            ["assignee"] = new JObject { ["name"] = issue["assigned_to"]?.ToString() ?? "" },
                            ["created_at"] = issue["created"]?.ToString() ?? DateTime.Now.ToString("O"),
                            ["custom_fields"] = new JObject
                            {
                                ["sting_id"] = issue["id"]?.ToString() ?? "",
                                ["sting_type"] = type,
                                ["discipline"] = issue["discipline"]?.ToString() ?? "",
                                ["procore_type"] = PlatformEngine.MapIssueToProcore(type)
                            }
                        };

                        if (type == "SNAGGING" || type == "NCR")
                            procorePunchList.Add(procoreItem);
                        else
                            procoreRfis.Add(procoreItem);
                    }

                    if (procoreRfis.Count > 0)
                    {
                        File.WriteAllText(Path.Combine(outDir, "procore_rfis.json"),
                            procoreRfis.ToString(Formatting.Indented));
                        fileCount++;
                        report.AppendLine($"  [OK] RFIs: {procoreRfis.Count}");
                    }

                    if (procorePunchList.Count > 0)
                    {
                        File.WriteAllText(Path.Combine(outDir, "procore_punch_list.json"),
                            procorePunchList.ToString(Formatting.Indented));
                        fileCount++;
                        report.AppendLine($"  [OK] Punch List: {procorePunchList.Count}");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Procore issue export: {ex.Message}"); }
            }

            // Export document register as submittals
            string docRegPath = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docRegPath))
            {
                try
                {
                    var docs = JArray.Parse(File.ReadAllText(docRegPath));
                    var submittals = new JArray();
                    foreach (JObject docItem in docs)
                    {
                        submittals.Add(new JObject
                        {
                            ["title"] = docItem["title"]?.ToString() ?? docItem["name"]?.ToString() ?? "",
                            ["specification_section"] = docItem["type"]?.ToString() ?? "",
                            ["status"] = "open",
                            ["revision"] = docItem["revision"]?.ToString() ?? "0",
                            ["custom_fields"] = new JObject
                            {
                                ["sting_doc_id"] = docItem["id"]?.ToString() ?? "",
                                ["suitability"] = docItem["suitability"]?.ToString() ?? ""
                            }
                        });
                    }

                    File.WriteAllText(Path.Combine(outDir, "procore_submittals.json"),
                        submittals.ToString(Formatting.Indented));
                    fileCount++;
                    report.AppendLine($"  [OK] Submittals: {submittals.Count}");
                }
                catch (Exception ex) { StingLog.Warn($"Procore submittal export: {ex.Message}"); }
            }

            // Export model summary
            var summary = PlatformEngine.BuildModelStats(doc);
            summary["platform"] = "Procore";
            summary["project"] = projectName;
            File.WriteAllText(Path.Combine(outDir, "model_summary.json"),
                summary.ToString(Formatting.Indented));
            fileCount++;

            report.AppendLine();
            report.AppendLine($"Package: {fileCount} files → {outDir}");
            report.AppendLine("Import via Procore API or manual upload.");

            TaskDialog.Show("Procore Package", report.ToString());
            StingLog.Info($"Procore export: {fileCount} files → {outDir}");
            return Result.Succeeded;
        }

        private static string MapStatusToProcore(string stingStatus)
        {
            return (stingStatus?.ToUpperInvariant()) switch
            {
                "OPEN" => "open",
                "IN_PROGRESS" => "draft",
                "RESPONDED" => "open",
                "CLOSED" => "closed",
                "VOID" => "void",
                _ => "open"
            };
        }
    }

    #endregion

    #region ── TrimbleConnectExportCommand ──

    /// <summary>
    /// Export BIM data for Trimble Connect integration.
    /// Creates a structured package compatible with Trimble Connect's TODO and clash APIs.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrimbleConnectExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outDir = Path.Combine(bimDir, $"TrimbleConnect_{timestamp}");
            Directory.CreateDirectory(outDir);

            int fileCount = 0;
            var report = new StringBuilder();
            report.AppendLine("TRIMBLE CONNECT EXPORT");
            report.AppendLine(new string('═', 50));

            // Export issues as Trimble Connect TODOs
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
            {
                try
                {
                    var issues = JArray.Parse(File.ReadAllText(issuesPath));
                    var todos = new JArray();
                    foreach (JObject issue in issues)
                    {
                        todos.Add(new JObject
                        {
                            ["type"] = "todo",
                            ["title"] = issue["title"]?.ToString() ?? "",
                            ["description"] = issue["description"]?.ToString() ?? "",
                            ["status"] = issue["status"]?.ToString()?.ToLowerInvariant() ?? "active",
                            ["priority"] = MapPriorityToTrimble(issue["priority"]?.ToString()),
                            ["assignedTo"] = issue["assigned_to"]?.ToString() ?? "",
                            ["createdDate"] = issue["created"]?.ToString() ?? "",
                            ["label"] = issue["type"]?.ToString() ?? "",
                            ["sourceId"] = issue["id"]?.ToString() ?? ""
                        });
                    }

                    File.WriteAllText(Path.Combine(outDir, "trimble_todos.json"),
                        todos.ToString(Formatting.Indented));
                    fileCount++;
                    report.AppendLine($"  [OK] TODOs: {todos.Count}");
                }
                catch (Exception ex) { StingLog.Warn($"Trimble TODO export: {ex.Message}"); }
            }

            // Also generate BCF for Trimble (it supports BCF natively)
            // Copy latest BCF if exists
            var bcfFiles = Directory.GetFiles(bimDir, "*.bcfzip");
            if (bcfFiles.Length > 0)
            {
                string latest = bcfFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                File.Copy(latest, Path.Combine(outDir, Path.GetFileName(latest)), true);
                fileCount++;
                report.AppendLine($"  [OK] BCF: {Path.GetFileName(latest)}");
            }

            // Model summary
            var summary = PlatformEngine.BuildModelStats(doc);
            summary["platform"] = "Trimble Connect";
            File.WriteAllText(Path.Combine(outDir, "model_summary.json"),
                summary.ToString(Formatting.Indented));
            fileCount++;

            report.AppendLine($"\nPackage: {fileCount} files → {outDir}");
            report.AppendLine("Upload to Trimble Connect project.");

            TaskDialog.Show("Trimble Connect Export", report.ToString());
            StingLog.Info($"Trimble Connect export: {fileCount} files");
            return Result.Succeeded;
        }

        private static int MapPriorityToTrimble(string priority)
        {
            return (priority?.ToUpperInvariant()) switch
            {
                "CRITICAL" => 1,
                "HIGH" => 2,
                "MEDIUM" => 3,
                "LOW" => 4,
                _ => 3
            };
        }
    }

    #endregion

    #region ── AconexPackageCommand ──

    /// <summary>
    /// Export document transmittal data in Aconex-compatible format.
    /// Aconex uses a specific transmittal schema with mail/document metadata.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AconexPackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Project");
            string projectCode = PlatformEngine.GetProjectCode(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outDir = Path.Combine(bimDir, $"Aconex_{timestamp}");
            Directory.CreateDirectory(outDir);

            int fileCount = 0;
            var report = new StringBuilder();
            report.AppendLine("ACONEX EXPORT PACKAGE");
            report.AppendLine(new string('═', 50));

            // Export transmittals in Aconex mail format
            string txPath = Path.Combine(bimDir, "transmittals.json");
            if (File.Exists(txPath))
            {
                try
                {
                    var transmittals = JArray.Parse(File.ReadAllText(txPath));
                    var aconexMails = new JArray();
                    foreach (JObject tx in transmittals)
                    {
                        aconexMails.Add(new JObject
                        {
                            ["mail_type"] = "Transmittal",
                            ["subject"] = $"{projectCode} - {tx["reason"]?.ToString() ?? "Document Transmittal"}",
                            ["to_organization"] = tx["recipient_org"]?.ToString() ?? "",
                            ["to_role"] = tx["recipient_role"]?.ToString() ?? "",
                            ["mail_number"] = tx["transmittal_id"]?.ToString() ?? "",
                            ["status"] = tx["status"]?.ToString() ?? "For Information",
                            ["confidential"] = false,
                            ["reason_for_issue"] = tx["reason"]?.ToString() ?? "",
                            ["created_date"] = tx["date"]?.ToString() ?? "",
                            ["documents"] = tx["documents"] ?? new JArray(),
                            ["custom_fields"] = new JObject
                            {
                                ["suitability"] = tx["suitability"]?.ToString() ?? "",
                                ["revision"] = tx["revision"]?.ToString() ?? "P01"
                            }
                        });
                    }

                    File.WriteAllText(Path.Combine(outDir, "aconex_transmittals.json"),
                        aconexMails.ToString(Formatting.Indented));
                    fileCount++;
                    report.AppendLine($"  [OK] Transmittals: {aconexMails.Count}");
                }
                catch (Exception ex) { StingLog.Warn($"Aconex transmittal export: {ex.Message}"); }
            }

            // Export document register in Aconex document metadata format
            string docRegPath = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docRegPath))
            {
                try
                {
                    var docs = JArray.Parse(File.ReadAllText(docRegPath));
                    var aconexDocs = new JArray();
                    foreach (JObject docItem in docs)
                    {
                        aconexDocs.Add(new JObject
                        {
                            ["document_number"] = docItem["id"]?.ToString() ?? "",
                            ["title"] = docItem["title"]?.ToString() ?? docItem["name"]?.ToString() ?? "",
                            ["revision"] = docItem["revision"]?.ToString() ?? "0",
                            ["document_type"] = docItem["type"]?.ToString() ?? "",
                            ["discipline"] = docItem["discipline"]?.ToString() ?? "",
                            ["status_code"] = docItem["suitability"]?.ToString() ?? "S0",
                            ["originator"] = "STG"
                        });
                    }

                    File.WriteAllText(Path.Combine(outDir, "aconex_documents.json"),
                        aconexDocs.ToString(Formatting.Indented));
                    fileCount++;
                    report.AppendLine($"  [OK] Documents: {aconexDocs.Count}");
                }
                catch (Exception ex) { StingLog.Warn($"Aconex document export: {ex.Message}"); }
            }

            report.AppendLine($"\nPackage: {fileCount} files → {outDir}");
            report.AppendLine("Import via Aconex Bulk Upload or API.");

            TaskDialog.Show("Aconex Package", report.ToString());
            StingLog.Info($"Aconex export: {fileCount} files");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── ProjectWiseExportCommand ──

    /// <summary>
    /// Export BIM data for Bentley ProjectWise integration.
    /// Creates structured folder hierarchy and metadata per PW conventions.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectWiseExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Project");
            string projectCode = PlatformEngine.GetProjectCode(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outDir = Path.Combine(bimDir, $"ProjectWise_{timestamp}");

            // ProjectWise-style folder hierarchy
            string[] folders = {
                $"{projectCode}\\Documents\\BEP",
                $"{projectCode}\\Documents\\Transmittals",
                $"{projectCode}\\Documents\\Issues",
                $"{projectCode}\\Models\\Current",
                $"{projectCode}\\Models\\Archive",
                $"{projectCode}\\Reports\\Compliance",
                $"{projectCode}\\Reports\\COBie",
                $"{projectCode}\\Coordination\\BCF"
            };

            foreach (string f in folders)
                Directory.CreateDirectory(Path.Combine(outDir, f));

            int fileCount = 0;
            var report = new StringBuilder();
            report.AppendLine("BENTLEY PROJECTWISE EXPORT");
            report.AppendLine(new string('═', 50));

            PlatformEngine.CopyIfExists(bimDir, "project_bep.json", outDir,
                $"{projectCode}\\Documents\\BEP", ref fileCount);
            PlatformEngine.CopyIfExists(bimDir, "transmittals.json", outDir,
                $"{projectCode}\\Documents\\Transmittals", ref fileCount);
            PlatformEngine.CopyIfExists(bimDir, "issues.json", outDir,
                $"{projectCode}\\Documents\\Issues", ref fileCount);

            // Copy BCF files to coordination folder
            foreach (var bcf in Directory.GetFiles(bimDir, "*.bcfzip"))
            {
                File.Copy(bcf, Path.Combine(outDir, $"{projectCode}\\Coordination\\BCF",
                    Path.GetFileName(bcf)), true);
                fileCount++;
            }

            // Generate PW attribute exchange XML
            var pwXml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("ProjectWiseAttributeExchange",
                    new XAttribute("version", "1.0"),
                    new XElement("Project",
                        new XElement("Name", projectName),
                        new XElement("Code", projectCode),
                        new XElement("Standard", "BS EN ISO 19650")),
                    new XElement("Environment",
                        new XElement("Creator", "StingTools V2.1"),
                        new XElement("Date", DateTime.Now.ToString("yyyy-MM-dd")),
                        new XElement("Schema", "DocumentAttribute")),
                    new XElement("Attributes",
                        new XElement("Attribute", new XAttribute("Name", "DocumentNumber"), new XAttribute("Type", "String")),
                        new XElement("Attribute", new XAttribute("Name", "Revision"), new XAttribute("Type", "String")),
                        new XElement("Attribute", new XAttribute("Name", "Suitability"), new XAttribute("Type", "String")),
                        new XElement("Attribute", new XAttribute("Name", "CDEStatus"), new XAttribute("Type", "String")),
                        new XElement("Attribute", new XAttribute("Name", "Discipline"), new XAttribute("Type", "String")),
                        new XElement("Attribute", new XAttribute("Name", "Originator"), new XAttribute("Type", "String")))));

            pwXml.Save(Path.Combine(outDir, $"{projectCode}\\pw_attributes.xml"));
            fileCount++;

            report.AppendLine($"  Structure: {folders.Length} folders");
            report.AppendLine($"  Files: {fileCount}");
            report.AppendLine($"\nPackage: {outDir}");
            report.AppendLine("Map to ProjectWise datasource via PW Explorer.");

            TaskDialog.Show("ProjectWise Export", report.ToString());
            StingLog.Info($"ProjectWise export: {fileCount} files");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── PlatformDashboardCommand ──

    /// <summary>
    /// Unified platform integration dashboard showing sync status across all platforms.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlatformDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            var report = new StringBuilder();
            report.AppendLine("PLATFORM INTEGRATION DASHBOARD");
            report.AppendLine(new string('═', 50));
            report.AppendLine();

            // Platform sync status
            string syncPath = Path.Combine(bimDir, "platform_sync.json");
            if (File.Exists(syncPath))
            {
                try
                {
                    var sync = JObject.Parse(File.ReadAllText(syncPath));
                    string lastSync = sync["last_sync"]?.ToString() ?? "Never";
                    int historyCount = (sync["sync_history"] as JArray)?.Count ?? 0;
                    report.AppendLine($"SYNC STATUS");
                    report.AppendLine($"  Last sync: {lastSync}");
                    report.AppendLine($"  Sync history: {historyCount} entries");
                }
                catch { }
            }
            else
            {
                report.AppendLine("SYNC STATUS: Never synced");
            }
            report.AppendLine();

            // Data file inventory
            report.AppendLine("DATA FILES");
            string[] files = { "project_bep.json", "issues.json", "document_register.json",
                "transmittals.json", "project_dashboard.json", "model_health.json" };
            foreach (string f in files)
            {
                string path = Path.Combine(bimDir, f);
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    report.AppendLine($"  [EXISTS] {f} ({info.Length / 1024} KB, modified {info.LastWriteTime:yyyy-MM-dd HH:mm})");
                }
                else
                {
                    report.AppendLine($"  [MISSING] {f}");
                }
            }
            report.AppendLine();

            // Export packages
            report.AppendLine("EXPORT PACKAGES");
            string[] patterns = { "ACC_PUBLISH_*", "CDE_PACKAGE_*", "Procore_*",
                "TrimbleConnect_*", "Aconex_*", "ProjectWise_*", "SharePoint_*" };
            foreach (string pattern in patterns)
            {
                var dirs = Directory.GetDirectories(bimDir, pattern);
                var zips = Directory.GetFiles(bimDir, pattern + ".zip");
                int total = dirs.Length + zips.Length;
                if (total > 0)
                {
                    string latest = dirs.Concat(zips).OrderByDescending(f =>
                        Directory.Exists(f) ? Directory.GetLastWriteTime(f) : File.GetLastWriteTime(f))
                        .First();
                    string latestName = Path.GetFileName(latest);
                    report.AppendLine($"  {pattern.Replace("_*", "")}: {total} packages (latest: {latestName})");
                }
            }

            // BCF files
            var bcfFiles = Directory.GetFiles(bimDir, "*.bcfzip");
            if (bcfFiles.Length > 0)
                report.AppendLine($"  BCF: {bcfFiles.Length} files");

            // Delta sync files
            var deltaFiles = Directory.GetFiles(bimDir, "sync_delta_*.json");
            if (deltaFiles.Length > 0)
                report.AppendLine($"  Sync deltas: {deltaFiles.Length} files");

            report.AppendLine();

            // Model summary
            var stats = PlatformEngine.BuildModelStats(doc);
            report.AppendLine("MODEL SUMMARY");
            report.AppendLine($"  Elements: {stats["total_elements"]}");
            report.AppendLine($"  Tag completeness: {stats["completeness_pct"]}%");
            report.AppendLine($"  Sheets: {stats["sheets"]}  Views: {stats["views"]}");
            report.AppendLine($"  Revisions: {stats["revisions"]}  Levels: {stats["levels"]}");

            TaskDialog.Show("Platform Dashboard", report.ToString());
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── WebhookPayloadCommand ──

    /// <summary>
    /// Generate webhook-ready JSON payloads for integration with CI/CD, Power Automate,
    /// Zapier, or custom platform connectors.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WebhookPayloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) return Result.Failed;
            Document doc = ctx.Doc;

            string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
            string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Project");
            string projectCode = PlatformEngine.GetProjectCode(doc);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Build comprehensive webhook payload
            var payload = new JObject
            {
                ["event"] = "model_update",
                ["timestamp"] = DateTime.Now.ToString("O"),
                ["source"] = "StingTools V2.1",
                ["project"] = new JObject
                {
                    ["name"] = projectName,
                    ["code"] = projectCode,
                    ["file"] = doc.PathName ?? ""
                }
            };

            // Model statistics
            payload["model"] = PlatformEngine.BuildModelStats(doc);

            // Compliance
            payload["compliance"] = PlatformEngine.BuildComplianceSummary(doc);

            // Issues summary
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
            {
                try
                {
                    var issues = JArray.Parse(File.ReadAllText(issuesPath));
                    var byStatus = issues.GroupBy(i => i["status"]?.ToString() ?? "UNKNOWN")
                        .ToDictionary(g => g.Key, g => g.Count());
                    var byType = issues.GroupBy(i => i["type"]?.ToString() ?? "UNKNOWN")
                        .ToDictionary(g => g.Key, g => g.Count());

                    payload["issues"] = new JObject
                    {
                        ["total"] = issues.Count,
                        ["by_status"] = JObject.FromObject(byStatus),
                        ["by_type"] = JObject.FromObject(byType)
                    };
                }
                catch { }
            }

            // Latest revision info
            try
            {
                var revisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .ToList();

                if (revisions.Count > 0)
                {
                    var latest = revisions[0];
                    payload["latest_revision"] = new JObject
                    {
                        ["sequence"] = latest.SequenceNumber,
                        ["description"] = latest.Description ?? "",
                        ["date"] = latest.RevisionDate ?? "",
                        ["issued"] = latest.Issued
                    };
                }
            }
            catch { }

            // Save payload
            string payloadPath = Path.Combine(bimDir, $"webhook_payload_{timestamp}.json");
            File.WriteAllText(payloadPath, payload.ToString(Formatting.Indented));

            // Also save a "latest" version for easy polling
            string latestPath = Path.Combine(bimDir, "webhook_payload_latest.json");
            File.WriteAllText(latestPath, payload.ToString(Formatting.Indented));

            var report = new StringBuilder();
            report.AppendLine("WEBHOOK PAYLOAD GENERATED");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"Event: model_update");
            report.AppendLine($"Payload: {payloadPath}");
            report.AppendLine($"Latest: {latestPath}");
            report.AppendLine();
            report.AppendLine("Integration targets:");
            report.AppendLine("  Power Automate — use HTTP trigger with JSON body");
            report.AppendLine("  Zapier — use Webhook catch trigger");
            report.AppendLine("  Azure Logic Apps — use HTTP Request trigger");
            report.AppendLine("  Custom API — POST to your endpoint");
            report.AppendLine();
            report.AppendLine("Payload includes: model stats, compliance, issues, revision info.");

            TaskDialog.Show("Webhook Payload", report.ToString());
            StingLog.Info($"Webhook payload: {payloadPath}");
            return Result.Succeeded;
        }
    }

    #endregion
}
