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
    //  Platform Integration — ACC, BCF, CDE, SharePoint
    //
    //  Commands:
    //    ACCPublishCommand       — Package BIM deliverables for ACC/CDE upload
    //    CDEPackageCommand       — ISO 19650 CDE-ready deliverable package
    //    BCFExportCommand        — Export issues as BCF 2.1
    //    BCFImportCommand        — Import BCF issues into STING tracker
    //    PlatformSyncCommand     — Delta sync with external platforms
    //    SharePointExportCommand — SharePoint/Teams folder structure export
    // ════════════════════════════════════════════════════════════════════════════

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
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string packageDir = Path.Combine(bimDir, $"ACC_PUBLISH_{timestamp}");
            Directory.CreateDirectory(packageDir);

            var report = new StringBuilder();
            report.AppendLine("ACC PUBLISH PACKAGE");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"Project: {projectName}");
            report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine();

            int fileCount = 0;

            // Copy BEP
            string bepPath = Path.Combine(bimDir, "project_bep.json");
            if (File.Exists(bepPath))
            {
                File.Copy(bepPath, Path.Combine(packageDir, "BEP.json"), true);
                fileCount++;
                report.AppendLine("  [OK] BEP (project_bep.json)");
            }

            // Copy dashboard
            string dashPath = Path.Combine(bimDir, "project_dashboard.json");
            if (File.Exists(dashPath))
            {
                File.Copy(dashPath, Path.Combine(packageDir, "Dashboard.json"), true);
                fileCount++;
                report.AppendLine("  [OK] Dashboard");
            }

            // Copy issues
            string issuesPath = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesPath))
            {
                File.Copy(issuesPath, Path.Combine(packageDir, "Issues.json"), true);
                fileCount++;
                report.AppendLine("  [OK] Issues log");
            }

            // Copy document register
            string docRegPath = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docRegPath))
            {
                File.Copy(docRegPath, Path.Combine(packageDir, "DocumentRegister.json"), true);
                fileCount++;
                report.AppendLine("  [OK] Document register");
            }

            // Copy transmittals
            string txPath = Path.Combine(bimDir, "transmittals.json");
            if (File.Exists(txPath))
            {
                File.Copy(txPath, Path.Combine(packageDir, "Transmittals.json"), true);
                fileCount++;
                report.AppendLine("  [OK] Transmittals");
            }

            // Copy COBie if exists
            string cobieDir = Path.Combine(bimDir, "COBie");
            var cobieDirs = Directory.GetDirectories(bimDir, "COBie*");
            if (cobieDirs.Length > 0)
            {
                string latestCobie = cobieDirs.OrderByDescending(d => d).First();
                string destCobie = Path.Combine(packageDir, "COBie");
                CopyDirectory(latestCobie, destCobie);
                fileCount++;
                report.AppendLine("  [OK] COBie data");
            }

            // Create manifest
            var manifest = new JObject
            {
                ["project"] = projectName,
                ["generated"] = DateTime.Now.ToString("O"),
                ["generator"] = "StingTools V2.1",
                ["iso_standard"] = "BS EN ISO 19650",
                ["file_count"] = fileCount,
                ["contents"] = new JArray(
                    Directory.GetFiles(packageDir, "*", SearchOption.AllDirectories)
                        .Select(f => Path.GetRelativePath(packageDir, f))
                        .Select(f => new JValue(f))
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
            report.AppendLine($"ZIP: {zipPath}");
            report.AppendLine();
            report.AppendLine("Upload this ZIP to ACC/BIM 360 or your CDE platform.");

            TaskDialog.Show("ACC Publish", report.ToString());
            StingLog.Info($"ACC publish: {fileCount} files → {zipPath}");
            return Result.Succeeded;
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }

    #endregion

    #region ── CDEPackageCommand ──

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
            string timestamp = DateTime.Now.ToString("yyyyMMdd");

            // Create CDE folder structure (ISO 19650 containers)
            string cdeRoot = Path.Combine(bimDir, $"CDE_PACKAGE_{timestamp}");
            string[] containers = { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
            foreach (string c in containers)
                Directory.CreateDirectory(Path.Combine(cdeRoot, c));

            // Place deliverables in appropriate containers
            var report = new StringBuilder();
            report.AppendLine("CDE DELIVERABLE PACKAGE");
            report.AppendLine(new string('═', 50));

            // BEP → SHARED
            string bepSrc = Path.Combine(bimDir, "project_bep.json");
            if (File.Exists(bepSrc))
            {
                string bepDest = Path.Combine(cdeRoot, "SHARED",
                    $"{projectName}-XX-XX-BEP-{timestamp}.json");
                File.Copy(bepSrc, bepDest, true);
                report.AppendLine($"  SHARED: BEP");
            }

            // Issues → WIP
            string issuesSrc = Path.Combine(bimDir, "issues.json");
            if (File.Exists(issuesSrc))
            {
                string issuesDest = Path.Combine(cdeRoot, "WIP",
                    $"{projectName}-XX-XX-ISSUES-{timestamp}.json");
                File.Copy(issuesSrc, issuesDest, true);
                report.AppendLine($"  WIP: Issues log");
            }

            // Document register → SHARED
            string docRegSrc = Path.Combine(bimDir, "document_register.json");
            if (File.Exists(docRegSrc))
            {
                string docRegDest = Path.Combine(cdeRoot, "SHARED",
                    $"{projectName}-XX-XX-DOC_REG-{timestamp}.json");
                File.Copy(docRegSrc, docRegDest, true);
                report.AppendLine($"  SHARED: Document register");
            }

            // Create index manifest
            var index = new JObject
            {
                ["project"] = projectName,
                ["cde_structure"] = "ISO 19650-1:2018 §12",
                ["containers"] = new JArray(containers),
                ["generated"] = DateTime.Now.ToString("O"),
                ["generator"] = "StingTools V2.1"
            };
            var files = new JArray();
            foreach (string file in Directory.GetFiles(cdeRoot, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(cdeRoot, file);
                string container = rel.Split(Path.DirectorySeparatorChar).FirstOrDefault() ?? "";
                files.Add(new JObject
                {
                    ["path"] = rel,
                    ["container"] = container,
                    ["suitability"] = container == "PUBLISHED" ? "S3" : "S2",
                    ["revision"] = "P01"
                });
            }
            index["files"] = files;
            File.WriteAllText(Path.Combine(cdeRoot, "CDE_INDEX.json"),
                index.ToString(Formatting.Indented));

            report.AppendLine();
            report.AppendLine($"Package: {cdeRoot}");

            TaskDialog.Show("CDE Package", report.ToString());
            StingLog.Info($"CDE package created: {cdeRoot}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── BCFExportCommand ──

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
            try
            {
                issues = JArray.Parse(File.ReadAllText(issuesPath));
            }
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

            // Create BCF 2.1 ZIP
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string bcfPath = Path.Combine(bimDir, $"STING_ISSUES_{timestamp}.bcfzip");
            string tempDir = Path.Combine(Path.GetTempPath(), $"sting_bcf_{timestamp}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // bcf.version
                var versionXml = new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement("Version",
                        new XAttribute("VersionId", "2.1"),
                        new XElement("DetailedVersion", "2.1")));
                versionXml.Save(Path.Combine(tempDir, "bcf.version"));

                // project.bcfp
                string projectName = Path.GetFileNameWithoutExtension(doc.PathName ?? "Unknown");
                var projectXml = new XDocument(
                    new XDeclaration("1.0", "UTF-8", null),
                    new XElement("ProjectExtension",
                        new XElement("Project",
                            new XAttribute("ProjectId", Guid.NewGuid().ToString()),
                            new XElement("Name", projectName))));
                projectXml.Save(Path.Combine(tempDir, "project.bcfp"));

                int exported = 0;
                foreach (JObject issue in issues)
                {
                    string issueId = issue["id"]?.ToString() ?? Guid.NewGuid().ToString();
                    string topicGuid = Guid.NewGuid().ToString();
                    string topicDir = Path.Combine(tempDir, topicGuid);
                    Directory.CreateDirectory(topicDir);

                    // markup.bcf
                    string title = issue["title"]?.ToString() ?? "Untitled Issue";
                    string description = issue["description"]?.ToString() ?? "";
                    string type = issue["type"]?.ToString() ?? "COMMENT";
                    string status = issue["status"]?.ToString() ?? "OPEN";
                    string priority = issue["priority"]?.ToString() ?? "MEDIUM";
                    string assignedTo = issue["assigned_to"]?.ToString() ?? "";
                    string createdDate = issue["created"]?.ToString() ?? DateTime.Now.ToString("O");

                    var markupXml = new XDocument(
                        new XDeclaration("1.0", "UTF-8", null),
                        new XElement("Markup",
                            new XElement("Topic",
                                new XAttribute("Guid", topicGuid),
                                new XAttribute("TopicType", MapIssueToBcfType(type)),
                                new XAttribute("TopicStatus", status),
                                new XElement("Title", title),
                                new XElement("Description", description),
                                new XElement("Priority", priority),
                                new XElement("AssignedTo", assignedTo),
                                new XElement("CreationDate", createdDate),
                                new XElement("CreationAuthor", "StingTools V2.1"),
                                new XElement("ModifiedDate", DateTime.Now.ToString("O")),
                                new XElement("ReferenceLink", $"STING-{issueId}")
                            ),
                            new XElement("Comment",
                                new XAttribute("Guid", Guid.NewGuid().ToString()),
                                new XElement("Date", createdDate),
                                new XElement("Author", "StingTools"),
                                new XElement("Comment", description),
                                new XElement("Topic", new XAttribute("Guid", topicGuid))
                            )
                        ));
                    markupXml.Save(Path.Combine(topicDir, "markup.bcf"));
                    exported++;
                }

                // Create ZIP
                if (File.Exists(bcfPath)) File.Delete(bcfPath);
                ZipFile.CreateFromDirectory(tempDir, bcfPath);

                TaskDialog.Show("BCF Export",
                    $"Exported {exported} issues as BCF 2.1.\n\n" +
                    $"File: {bcfPath}\n\n" +
                    "Compatible with Navisworks, Solibri, BIMcollab, Trimble Connect.");
                StingLog.Info($"BCF export: {exported} issues → {bcfPath}");
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }

            return Result.Succeeded;
        }

        private static string MapIssueToBcfType(string stingType)
        {
            switch (stingType?.ToUpperInvariant())
            {
                case "RFI": return "Request";
                case "CLASH": return "Clash";
                case "DESIGN": return "Fault";
                case "NCR": return "Fault";
                case "SNAGGING": return "Fault";
                case "CHANGE": return "Request";
                case "RISK": return "Remark";
                case "ACTION": return "Request";
                default: return "Comment";
            }
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

            // Open file dialog for .bcfzip
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

                // Load existing issues
                JArray existingIssues;
                if (File.Exists(issuesPath))
                    existingIssues = JArray.Parse(File.ReadAllText(issuesPath));
                else
                    existingIssues = new JArray();

                int imported = 0;

                // Process each topic directory
                foreach (string topicDir in Directory.GetDirectories(tempDir))
                {
                    string markupPath = Path.Combine(topicDir, "markup.bcf");
                    if (!File.Exists(markupPath)) continue;

                    try
                    {
                        var markupXml = XDocument.Load(markupPath);
                        var topic = markupXml.Root?.Element("Topic");
                        if (topic == null) continue;

                        string title = topic.Element("Title")?.Value ?? "Imported BCF Issue";
                        string description = topic.Element("Description")?.Value ?? "";
                        string bcfType = topic.Attribute("TopicType")?.Value ?? "Comment";
                        string status = topic.Attribute("TopicStatus")?.Value ?? "OPEN";
                        string priority = topic.Element("Priority")?.Value ?? "MEDIUM";
                        string assignedTo = topic.Element("AssignedTo")?.Value ?? "";

                        // Map BCF type back to STING type
                        string stingType = MapBcfTypeToSting(bcfType);

                        // Generate STING issue ID
                        int nextId = existingIssues.Count + 1;
                        string issueId = $"ISS-{nextId:D4}";

                        var newIssue = new JObject
                        {
                            ["id"] = issueId,
                            ["title"] = title,
                            ["description"] = description,
                            ["type"] = stingType,
                            ["status"] = status,
                            ["priority"] = priority,
                            ["assigned_to"] = assignedTo,
                            ["created"] = DateTime.Now.ToString("O"),
                            ["source"] = "BCF Import",
                            ["bcf_file"] = Path.GetFileName(dlg.FileName),
                            ["bcf_topic_guid"] = topic.Attribute("Guid")?.Value ?? ""
                        };

                        existingIssues.Add(newIssue);
                        imported++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BCF import topic '{topicDir}': {ex.Message}");
                    }
                }

                // Save updated issues
                File.WriteAllText(issuesPath, existingIssues.ToString(Formatting.Indented));

                TaskDialog.Show("BCF Import",
                    $"Imported {imported} issues from BCF file.\n\n" +
                    $"Source: {Path.GetFileName(dlg.FileName)}\n" +
                    $"Total issues: {existingIssues.Count}");
                StingLog.Info($"BCF import: {imported} issues from {dlg.FileName}");
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

        private static string MapBcfTypeToSting(string bcfType)
        {
            switch (bcfType?.ToLowerInvariant())
            {
                case "clash": return "CLASH";
                case "request": return "RFI";
                case "fault": return "NCR";
                case "remark": return "COMMENT";
                case "inquiry": return "RFI";
                default: return "COMMENT";
            }
        }
    }

    #endregion

    #region ── PlatformSyncCommand ──

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
            string deltaPath = Path.Combine(bimDir, $"sync_delta_{DateTime.Now:yyyyMMdd_HHmmss}.json");

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
                    ["auto_sync"] = false
                };

            string lastSync = syncConfig["last_sync"]?.ToString() ?? "";
            DateTime lastSyncDate = string.IsNullOrEmpty(lastSync)
                ? DateTime.MinValue
                : DateTime.TryParse(lastSync, out DateTime dt) ? dt : DateTime.MinValue;

            // Build delta: changes since last sync
            var delta = new JObject
            {
                ["sync_timestamp"] = DateTime.Now.ToString("O"),
                ["previous_sync"] = lastSync,
                ["generator"] = "StingTools V2.1"
            };

            // Check each data file for modifications since last sync
            var changes = new JArray();
            string[] dataFiles = { "project_bep.json", "issues.json", "document_register.json",
                                   "transmittals.json", "project_dashboard.json" };

            foreach (string fileName in dataFiles)
            {
                string filePath = Path.Combine(bimDir, fileName);
                if (!File.Exists(filePath)) continue;

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.LastWriteTime > lastSyncDate)
                {
                    changes.Add(new JObject
                    {
                        ["file"] = fileName,
                        ["modified"] = fileInfo.LastWriteTime.ToString("O"),
                        ["size_bytes"] = fileInfo.Length,
                        ["status"] = "MODIFIED"
                    });
                }
            }

            delta["changes"] = changes;
            delta["change_count"] = changes.Count;

            // Save delta
            File.WriteAllText(deltaPath, delta.ToString(Formatting.Indented));

            // Update sync timestamp
            syncConfig["last_sync"] = DateTime.Now.ToString("O");
            File.WriteAllText(syncConfigPath, syncConfig.ToString(Formatting.Indented));

            var report = new StringBuilder();
            report.AppendLine("PLATFORM SYNC");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"Previous sync: {(string.IsNullOrEmpty(lastSync) ? "Never" : lastSync)}");
            report.AppendLine($"Changes found: {changes.Count}");
            report.AppendLine();

            foreach (JObject change in changes)
                report.AppendLine($"  [{change["status"]}] {change["file"]}");

            report.AppendLine();
            report.AppendLine($"Delta exported: {deltaPath}");

            string webhookUrl = syncConfig["webhook_url"]?.ToString();
            if (!string.IsNullOrEmpty(webhookUrl))
                report.AppendLine($"Webhook: {webhookUrl} (manual POST required)");

            TaskDialog.Show("Platform Sync", report.ToString());
            StingLog.Info($"Platform sync: {changes.Count} changes since {lastSync}");
            return Result.Succeeded;
        }
    }

    #endregion

    #region ── SharePointExportCommand ──

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
            string timestamp = DateTime.Now.ToString("yyyyMMdd");
            string spRoot = Path.Combine(bimDir, $"SharePoint_{timestamp}");

            // Create SharePoint-compatible folder structure
            string[] folders = {
                "01_WIP",
                "02_SHARED",
                "03_PUBLISHED",
                "04_ARCHIVE",
                "05_BEP",
                "06_COBie",
                "07_Issues",
                "08_Transmittals"
            };

            foreach (string f in folders)
                Directory.CreateDirectory(Path.Combine(spRoot, f));

            int copied = 0;

            // Distribute files to folders
            CopyIfExists(bimDir, "project_bep.json", spRoot, "05_BEP", ref copied);
            CopyIfExists(bimDir, "issues.json", spRoot, "07_Issues", ref copied);
            CopyIfExists(bimDir, "transmittals.json", spRoot, "08_Transmittals", ref copied);
            CopyIfExists(bimDir, "document_register.json", spRoot, "02_SHARED", ref copied);
            CopyIfExists(bimDir, "project_dashboard.json", spRoot, "02_SHARED", ref copied);

            // Generate HTML dashboard for web viewing
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html><head>");
            html.AppendLine($"<title>{projectName} — BIM Dashboard</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: 'Segoe UI', sans-serif; margin: 40px; background: #f5f5f5; }");
            html.AppendLine(".card { background: white; border-radius: 8px; padding: 20px; margin: 12px 0; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            html.AppendLine("h1 { color: #582C83; } h2 { color: #1565C0; }");
            html.AppendLine("table { border-collapse: collapse; width: 100%; }");
            html.AppendLine("th, td { padding: 8px 12px; text-align: left; border-bottom: 1px solid #eee; }");
            html.AppendLine("th { background: #582C83; color: white; }");
            html.AppendLine(".badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 12px; color: white; }");
            html.AppendLine(".green { background: #4CAF50; } .amber { background: #FF9800; } .red { background: #F44336; }");
            html.AppendLine("</style>");
            html.AppendLine("</head><body>");
            html.AppendLine($"<h1>{projectName}</h1>");
            html.AppendLine($"<p>Generated by StingTools V2.1 — {DateTime.Now:yyyy-MM-dd HH:mm}</p>");

            // Add dashboard data if available
            string dashPath = Path.Combine(bimDir, "project_dashboard.json");
            if (File.Exists(dashPath))
            {
                try
                {
                    var dash = JObject.Parse(File.ReadAllText(dashPath));
                    html.AppendLine("<div class='card'>");
                    html.AppendLine("<h2>Project Status</h2>");
                    html.AppendLine("<table><tr><th>Metric</th><th>Value</th></tr>");

                    if (dash["element_count"] != null)
                        html.AppendLine($"<tr><td>Total Elements</td><td>{dash["element_count"]}</td></tr>");
                    if (dash["tag_completeness_pct"] != null)
                    {
                        double pct = dash["tag_completeness_pct"].Value<double>();
                        string badge = pct >= 80 ? "green" : pct >= 50 ? "amber" : "red";
                        html.AppendLine($"<tr><td>Tag Completeness</td><td><span class='badge {badge}'>{pct:F1}%</span></td></tr>");
                    }

                    html.AppendLine("</table></div>");
                }
                catch { }
            }

            // Add file index
            html.AppendLine("<div class='card'>");
            html.AppendLine("<h2>Deliverables</h2>");
            html.AppendLine("<table><tr><th>Folder</th><th>File</th></tr>");
            foreach (string folder in folders)
            {
                string folderPath = Path.Combine(spRoot, folder);
                foreach (string file in Directory.GetFiles(folderPath))
                    html.AppendLine($"<tr><td>{folder}</td><td>{Path.GetFileName(file)}</td></tr>");
            }
            html.AppendLine("</table></div>");
            html.AppendLine("</body></html>");

            File.WriteAllText(Path.Combine(spRoot, "index.html"), html.ToString());
            copied++;

            // Create metadata XML for SharePoint document library
            var metadataXml = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("DocumentLibrary",
                    new XElement("Project", projectName),
                    new XElement("Standard", "ISO 19650"),
                    new XElement("Generator", "StingTools V2.1"),
                    new XElement("Date", DateTime.Now.ToString("yyyy-MM-dd")),
                    new XElement("Columns",
                        new XElement("Column", new XAttribute("Name", "DocumentType"), new XAttribute("Type", "Text")),
                        new XElement("Column", new XAttribute("Name", "Suitability"), new XAttribute("Type", "Choice")),
                        new XElement("Column", new XAttribute("Name", "Revision"), new XAttribute("Type", "Text")),
                        new XElement("Column", new XAttribute("Name", "CDEContainer"), new XAttribute("Type", "Choice")),
                        new XElement("Column", new XAttribute("Name", "Discipline"), new XAttribute("Type", "Text"))
                    )
                ));
            metadataXml.Save(Path.Combine(spRoot, "metadata.xml"));
            copied++;

            TaskDialog.Show("SharePoint Export",
                $"SharePoint package created.\n\n" +
                $"Files: {copied}\n" +
                $"Location: {spRoot}\n\n" +
                "Upload the folder to SharePoint/Teams for team access.\n" +
                "index.html provides a web-viewable dashboard.");

            StingLog.Info($"SharePoint export: {copied} files → {spRoot}");
            return Result.Succeeded;
        }

        private static void CopyIfExists(string srcDir, string fileName, string destRoot, string destFolder, ref int count)
        {
            string src = Path.Combine(srcDir, fileName);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(destRoot, destFolder, fileName), true);
                count++;
            }
        }
    }

    #endregion
}
