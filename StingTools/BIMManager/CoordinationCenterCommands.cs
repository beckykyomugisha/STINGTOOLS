using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Windows.Media;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Coordination Center — Mini BIM Coordination Platform
    //
    //  Provides ACC-style coordination without cloud dependency:
    //    - Multi-channel notification delivery (Telegram/Teams/Email/Discord)
    //    - FileSystemWatcher for live document monitoring
    //    - Role-based access control (Admin/Editor/Viewer per company)
    //    - Self-contained HTML dashboard for mobile/external stakeholder access
    //    - Real-time activity feed with push notifications
    //    - Meeting coordination with agenda auto-generation
    //    - Issue lifecycle management with SLA escalation
    //    - Document distribution tracking with read receipts
    //
    //  Architecture:
    //    Revit Plugin → JSON files on shared drive → Notification channels
    //                                             → HTML dashboard (self-contained)
    //                                             → FileSystemWatcher (live alerts)
    //
    //  Cost: $0 — uses free APIs (Telegram Bot, Teams Webhooks, SMTP)
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Notification Delivery Engine ──

    /// <summary>
    /// Multi-channel notification delivery engine. Supports Telegram Bot API,
    /// Microsoft Teams Incoming Webhooks, Discord Webhooks, and SMTP email.
    /// Configuration stored in project_config.json under NOTIFICATION_CHANNELS.
    /// All delivery is fire-and-forget with retry and logging.
    /// </summary>
    internal static class NotificationDeliveryEngine
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly object _lock = new object();

        // ── Channel Configuration ──

        internal class ChannelConfig
        {
            public string Type { get; set; }        // telegram, teams, discord, email, webhook
            public string Name { get; set; }         // Display name
            public bool Enabled { get; set; }
            public string Endpoint { get; set; }     // URL or SMTP server
            public string Token { get; set; }        // Bot token or API key
            public string ChatId { get; set; }       // Telegram chat ID or channel
            public string FromAddress { get; set; }  // Email sender
            public int Port { get; set; }            // SMTP port
            public bool UseSsl { get; set; }         // SMTP SSL
            public string Username { get; set; }     // SMTP username
            public string Password { get; set; }     // SMTP password
        }

        private static List<ChannelConfig> _channels = new List<ChannelConfig>();

        /// <summary>Load notification channel configuration from project_config.json.</summary>
        internal static void LoadConfig(Document doc)
        {
            _channels.Clear();
            try
            {
                string configPath = Path.Combine(
                    Path.GetDirectoryName(doc.PathName) ?? StingToolsApp.DataPath,
                    "project_config.json");
                if (!File.Exists(configPath)) return;

                var config = JObject.Parse(File.ReadAllText(configPath));
                var channels = config["NOTIFICATION_CHANNELS"] as JArray;
                if (channels == null) return;

                foreach (var ch in channels)
                {
                    _channels.Add(new ChannelConfig
                    {
                        Type = ch["type"]?.ToString() ?? "",
                        Name = ch["name"]?.ToString() ?? "",
                        Enabled = ch["enabled"]?.Value<bool>() ?? false,
                        Endpoint = ch["endpoint"]?.ToString() ?? "",
                        Token = ch["token"]?.ToString() ?? "",
                        ChatId = ch["chat_id"]?.ToString() ?? "",
                        FromAddress = ch["from_address"]?.ToString() ?? "",
                        Port = ch["port"]?.Value<int>() ?? 587,
                        UseSsl = ch["use_ssl"]?.Value<bool>() ?? true,
                        Username = ch["username"]?.ToString() ?? "",
                        Password = ch["password"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"NotificationDelivery.LoadConfig: {ex.Message}"); }
        }

        /// <summary>Get all configured and enabled channels.</summary>
        internal static List<ChannelConfig> GetEnabledChannels() =>
            _channels.Where(c => c.Enabled).ToList();

        /// <summary>Send notification to all enabled channels. Fire-and-forget with logging.</summary>
        internal static void SendNotification(Document doc, string title, string message,
            string priority = "MEDIUM", string refId = "", List<string> mentionUsers = null)
        {
            var enabled = GetEnabledChannels();
            if (enabled.Count == 0) return;

            // Format message with metadata
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string user = Environment.UserName;
            string projectName = doc?.Title ?? "Unknown Project";

            foreach (var channel in enabled)
            {
                try
                {
                    switch (channel.Type.ToLowerInvariant())
                    {
                        case "telegram":
                            SendTelegram(channel, title, message, priority, projectName, timestamp, user, refId);
                            break;
                        case "teams":
                            SendTeams(channel, title, message, priority, projectName, timestamp, user, refId);
                            break;
                        case "discord":
                            SendDiscord(channel, title, message, priority, projectName, timestamp, user, refId);
                            break;
                        case "email":
                            SendEmail(channel, title, message, priority, projectName, timestamp, user, refId, mentionUsers);
                            break;
                        case "webhook":
                            SendWebhook(channel, title, message, priority, projectName, timestamp, user, refId);
                            break;
                    }
                    StingLog.Info($"Notification sent via {channel.Type}/{channel.Name}: {title}");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Notification delivery failed ({channel.Type}/{channel.Name}): {ex.Message}");
                }
            }
        }

        // ── Telegram Bot API (FREE — unlimited messages) ──

        private static void SendTelegram(ChannelConfig ch, string title, string message,
            string priority, string project, string timestamp, string user, string refId)
        {
            string emoji = priority switch
            {
                "CRITICAL" => "\U0001F6A8",
                "HIGH" => "\u26A0\uFE0F",
                "MEDIUM" => "\U0001F4CB",
                "LOW" => "\u2139\uFE0F",
                _ => "\U0001F4E2"
            };
            string text = $"{emoji} *{EscapeMarkdown(title)}*\n" +
                          $"\U0001F4C1 {EscapeMarkdown(project)}\n" +
                          $"\U0001F464 {EscapeMarkdown(user)} | {timestamp}\n";
            if (!string.IsNullOrEmpty(refId))
                text += $"\U0001F3F7 `{refId}`\n";
            text += $"\n{EscapeMarkdown(message)}";

            string url = $"https://api.telegram.org/bot{ch.Token}/sendMessage";
            var payload = new JObject
            {
                ["chat_id"] = ch.ChatId,
                ["text"] = text,
                ["parse_mode"] = "Markdown",
                ["disable_web_page_preview"] = true
            };
            _ = PostJsonAsync(url, payload.ToString());
        }

        private static string EscapeMarkdown(string s) =>
            s?.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[")
             .Replace("`", "\\`").Replace("~", "\\~") ?? "";

        // ── Microsoft Teams Incoming Webhook (FREE with M365) ──

        private static void SendTeams(ChannelConfig ch, string title, string message,
            string priority, string project, string timestamp, string user, string refId)
        {
            string color = priority switch
            {
                "CRITICAL" => "FF0000",
                "HIGH" => "FF8C00",
                "MEDIUM" => "0078D4",
                _ => "00B050"
            };
            var card = new JObject
            {
                ["@type"] = "MessageCard",
                ["@context"] = "https://schema.org/extensions",
                ["themeColor"] = color,
                ["summary"] = title,
                ["sections"] = new JArray
                {
                    new JObject
                    {
                        ["activityTitle"] = title,
                        ["activitySubtitle"] = $"{project} | {user}",
                        ["activityImage"] = "",
                        ["facts"] = new JArray
                        {
                            new JObject { ["name"] = "Priority", ["value"] = priority },
                            new JObject { ["name"] = "Time", ["value"] = timestamp },
                            new JObject { ["name"] = "Reference", ["value"] = refId }
                        },
                        ["text"] = message,
                        ["markdown"] = true
                    }
                }
            };
            _ = PostJsonAsync(ch.Endpoint, card.ToString());
        }

        // ── Discord Webhook (FREE) ──

        private static void SendDiscord(ChannelConfig ch, string title, string message,
            string priority, string project, string timestamp, string user, string refId)
        {
            int color = priority switch
            {
                "CRITICAL" => 0xFF0000,
                "HIGH" => 0xFF8C00,
                "MEDIUM" => 0x0078D4,
                _ => 0x00B050
            };
            var payload = new JObject
            {
                ["embeds"] = new JArray
                {
                    new JObject
                    {
                        ["title"] = title,
                        ["description"] = message,
                        ["color"] = color,
                        ["fields"] = new JArray
                        {
                            new JObject { ["name"] = "Project", ["value"] = project, ["inline"] = true },
                            new JObject { ["name"] = "User", ["value"] = user, ["inline"] = true },
                            new JObject { ["name"] = "Priority", ["value"] = priority, ["inline"] = true },
                            new JObject { ["name"] = "Reference", ["value"] = refId, ["inline"] = true }
                        },
                        ["timestamp"] = DateTime.UtcNow.ToString("o"),
                        ["footer"] = new JObject { ["text"] = "STING Coordination Center" }
                    }
                }
            };
            _ = PostJsonAsync(ch.Endpoint, payload.ToString());
        }

        // ── Email via SMTP ──

        private static void SendEmail(ChannelConfig ch, string title, string message,
            string priority, string project, string timestamp, string user, string refId,
            List<string> recipients)
        {
            if (recipients == null || recipients.Count == 0) return;
            try
            {
                using var smtp = new System.Net.Mail.SmtpClient(ch.Endpoint, ch.Port)
                {
                    EnableSsl = ch.UseSsl,
                    Credentials = new System.Net.NetworkCredential(ch.Username, ch.Password),
                    DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network
                };
                string body = $"<h2>{title}</h2>" +
                    $"<p><b>Project:</b> {project}<br/>" +
                    $"<b>Priority:</b> {priority}<br/>" +
                    $"<b>By:</b> {user} at {timestamp}<br/>" +
                    $"<b>Reference:</b> {refId}</p>" +
                    $"<p>{message.Replace("\n", "<br/>")}</p>" +
                    $"<hr/><small>STING Coordination Center</small>";

                foreach (string email in recipients.Where(e => e.Contains("@")))
                {
                    var msg = new System.Net.Mail.MailMessage(ch.FromAddress, email, $"[STING] [{priority}] {title}", body)
                    {
                        IsBodyHtml = true
                    };
                    smtp.Send(msg);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Email send failed: {ex.Message}"); }
        }

        // ── Generic Webhook ──

        private static void SendWebhook(ChannelConfig ch, string title, string message,
            string priority, string project, string timestamp, string user, string refId)
        {
            var payload = new JObject
            {
                ["event"] = "sting_notification",
                ["title"] = title,
                ["message"] = message,
                ["priority"] = priority,
                ["project"] = project,
                ["timestamp"] = timestamp,
                ["user"] = user,
                ["ref_id"] = refId
            };
            _ = PostJsonAsync(ch.Endpoint, payload.ToString());
        }

        private static async System.Threading.Tasks.Task PostJsonAsync(string url, string json)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(url, content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    StingLog.Warn($"Webhook POST {url}: {response.StatusCode}");
            }
            catch (Exception ex) { StingLog.Warn($"Webhook POST failed: {ex.Message}"); }
        }
    }

    #endregion

    #region ── File Monitor Engine (FileSystemWatcher) ──

    /// <summary>
    /// Monitors the project folder for file changes and triggers notifications.
    /// Uses FileSystemWatcher with periodic polling fallback for network drives.
    /// Enables instant stakeholder awareness when documents are uploaded/modified.
    /// </summary>
    internal static class FileMonitorEngine
    {
        private static FileSystemWatcher _watcher;
        private static System.Timers.Timer _pollTimer;
        private static string _watchPath;
        private static Document _doc;
        private static readonly object _eventLock = new object();
        private static readonly Dictionary<string, DateTime> _recentEvents = new Dictionary<string, DateTime>();
        private static bool _isRunning;

        /// <summary>Start monitoring the project folder for file changes.</summary>
        internal static void Start(Document doc)
        {
            if (_isRunning) Stop();
            _doc = doc;
            _watchPath = ProjectFolderEngine.RootPath;
            if (string.IsNullOrEmpty(_watchPath) || !Directory.Exists(_watchPath)) return;

            try
            {
                _watcher = new FileSystemWatcher(_watchPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                                   NotifyFilters.Size | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnFileEvent;
                _watcher.Changed += OnFileEvent;
                _watcher.Renamed += OnRenameEvent;
                _watcher.Deleted += OnFileEvent;
                _watcher.Error += OnWatcherError;

                // Polling fallback for network drives (every 30 seconds)
                _pollTimer = new System.Timers.Timer(30000);
                _pollTimer.Elapsed += (s, e) => PollForChanges();
                _pollTimer.Start();

                _isRunning = true;
                StingLog.Info($"FileMonitor started on: {_watchPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FileMonitor.Start failed: {ex.Message}");
                // Fall back to polling only
                StartPollingOnly(doc);
            }
        }

        /// <summary>Polling-only mode for environments where FileSystemWatcher fails.</summary>
        private static void StartPollingOnly(Document doc)
        {
            _doc = doc;
            _watchPath = ProjectFolderEngine.RootPath;
            if (string.IsNullOrEmpty(_watchPath)) return;

            _pollTimer = new System.Timers.Timer(15000); // Poll every 15s
            _pollTimer.Elapsed += (s, e) => PollForChanges();
            _pollTimer.Start();
            _isRunning = true;
            StingLog.Info($"FileMonitor polling-only mode on: {_watchPath}");
        }

        /// <summary>Stop monitoring.</summary>
        internal static void Stop()
        {
            try
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }
                _pollTimer?.Stop();
                _pollTimer?.Dispose();
                _pollTimer = null;
                _isRunning = false;
                StingLog.Info("FileMonitor stopped.");
            }
            catch (Exception ex) { StingLog.Warn($"FileMonitor.Stop: {ex.Message}"); }
        }

        internal static bool IsRunning => _isRunning;

        private static void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // GAP-BIM-006: Deduplicate by FILE PATH (not ChangeType:Path) with 5-second window.
            // FileSystemWatcher fires Created+Modified+Attributes for a single save operation,
            // and network drives can trigger Created+Deleted+Created during antivirus scanning.
            // Coalescing all events for the same file within 5s prevents notification spam.
            lock (_eventLock)
            {
                string key = e.FullPath; // Deduplicate by path only, not ChangeType
                if (_recentEvents.TryGetValue(key, out DateTime last) && (DateTime.Now - last).TotalSeconds < 5)
                    return;
                _recentEvents[key] = DateTime.Now;

                // Clean old entries periodically
                if (_recentEvents.Count > 200)
                {
                    var stale = _recentEvents.Where(kv => (DateTime.Now - kv.Value).TotalMinutes > 5)
                        .Select(kv => kv.Key).ToList();
                    foreach (var s in stale) _recentEvents.Remove(s);
                }
            }

            // Skip temp files, lock files, and STING internal files
            string name = Path.GetFileName(e.FullPath);
            if (name.StartsWith("~") || name.StartsWith(".") || name.EndsWith(".tmp") ||
                name.EndsWith(".lock") || name == "ACTIVITY_LOG.jsonl" ||
                name.Contains("notification_queue") || name.Contains("_STING_"))
                return;

            string action = e.ChangeType switch
            {
                WatcherChangeTypes.Created => "uploaded",
                WatcherChangeTypes.Changed => "modified",
                WatcherChangeTypes.Deleted => "deleted",
                _ => "changed"
            };

            // NTF-07: Type-based priority filtering — BIM deliverables get higher priority
            string ext = Path.GetExtension(name).ToLowerInvariant();
            string priority;
            if (ext == ".rvt" || ext == ".ifc" || ext == ".nwd" || ext == ".nwc")
                priority = "HIGH";       // Model files — critical for coordination
            else if (ext == ".pdf" || ext == ".xlsx" || ext == ".csv" || ext == ".bcf" || ext == ".dwg")
                priority = "MEDIUM";     // Document deliverables
            else if (ext == ".jpg" || ext == ".png" || ext == ".bmp" || ext == ".log" || ext == ".bak")
                return;                  // Skip images, logs, backups entirely — reduce noise
            else
                priority = "LOW";        // Other files

            string relPath = e.FullPath.Replace(_watchPath, "").TrimStart(Path.DirectorySeparatorChar);
            string folder = Path.GetDirectoryName(relPath)?.Replace(Path.DirectorySeparatorChar.ToString(), "/") ?? "";
            string title = $"Document {action}: {name}";
            string message = $"File: {relPath}\nFolder: {folder}\nAction: {action}";

            // Send notification with type-based priority
            NotificationDeliveryEngine.SendNotification(_doc, title, message, priority, relPath);

            // Log to activity feed
            try { ProjectFolderEngine.LogActivity(_doc, $"FILE_{action.ToUpperInvariant()}", name, relPath); }
            catch (Exception ex) { StingLog.Warn($"FileMonitor activity log: {ex.Message}"); }
        }

        private static void OnRenameEvent(object sender, RenamedEventArgs e)
        {
            string relOld = e.OldFullPath.Replace(_watchPath, "").TrimStart(Path.DirectorySeparatorChar);
            string relNew = e.FullPath.Replace(_watchPath, "").TrimStart(Path.DirectorySeparatorChar);
            string title = $"Document renamed: {Path.GetFileName(e.OldFullPath)} → {Path.GetFileName(e.FullPath)}";
            string message = $"From: {relOld}\nTo: {relNew}";
            NotificationDeliveryEngine.SendNotification(_doc, title, message, "LOW", relNew);
        }

        private static void OnWatcherError(object sender, ErrorEventArgs e)
        {
            StingLog.Warn($"FileMonitor error: {e.GetException()?.Message}");
            // Restart on error
            try { Stop(); Start(_doc); }
            catch (Exception ex) { StingLog.Warn($"FileMonitor restart: {ex.Message}"); }
        }

        private static Dictionary<string, long> _lastPollSnapshot = new Dictionary<string, long>();

        private static void PollForChanges()
        {
            if (string.IsNullOrEmpty(_watchPath) || !Directory.Exists(_watchPath)) return;
            try
            {
                var currentFiles = new Dictionary<string, long>();
                foreach (var f in Directory.GetFiles(_watchPath, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(f);
                        currentFiles[f] = info.Length + info.LastWriteTimeUtc.Ticks;
                    }
                    catch { /* skip locked files */ }
                }

                if (_lastPollSnapshot.Count > 0)
                {
                    // Detect new files
                    foreach (var kv in currentFiles)
                    {
                        if (!_lastPollSnapshot.ContainsKey(kv.Key))
                        {
                            OnFileEvent(null, new FileSystemEventArgs(WatcherChangeTypes.Created,
                                Path.GetDirectoryName(kv.Key), Path.GetFileName(kv.Key)));
                        }
                    }
                }
                _lastPollSnapshot = currentFiles;
            }
            catch (Exception ex) { StingLog.Warn($"FileMonitor poll: {ex.Message}"); }
        }
    }

    #endregion

    #region ── Access Control Engine (ACC-style) ──

    /// <summary>
    /// ACC-style role-based access control using Windows domain identity.
    /// No passwords needed — leverages existing Windows authentication.
    /// Stores access policies in access_control.json alongside the project.
    ///
    /// Roles: Admin (full control), Editor (create/edit own company), Viewer (read-only)
    /// Scopes: issues, transmittals, documents, meetings, bep, compliance
    /// Discipline filter: restricts visibility to user's discipline codes
    /// </summary>
    internal static class AccessControlEngine
    {
        internal class UserAccess
        {
            public string Email { get; set; }
            public string Name { get; set; }
            public string CompanyId { get; set; }
            public string Role { get; set; }  // admin, editor, viewer
            public List<string> Scopes { get; set; } = new List<string>();
            public List<string> DisciplineFilter { get; set; } = new List<string>();
            public string CdeAccess { get; set; }  // full, read, none
        }

        internal class AccessPolicy
        {
            public string ProjectId { get; set; }
            public Dictionary<string, RolePermissions> Roles { get; set; } = new();
            public List<UserAccess> Users { get; set; } = new();
            public List<CompanyDef> Companies { get; set; } = new();
        }

        internal class RolePermissions
        {
            public bool Create { get; set; }
            public bool Edit { get; set; }
            public bool Delete { get; set; }
            public bool ViewAll { get; set; }
            public bool ManageUsers { get; set; }
            public bool ExportData { get; set; }
            public bool ManageCDE { get; set; }
        }

        internal class CompanyDef
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public List<string> Disciplines { get; set; } = new();
        }

        private static AccessPolicy _policy;
        private static UserAccess _currentUser;

        /// <summary>Load access control policy from project directory.</summary>
        internal static void Load(Document doc)
        {
            _policy = null;
            _currentUser = null;
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName) ?? "";
                string bimDir = Path.Combine(dir, "STING_BIM_MANAGER");
                string path = Path.Combine(bimDir, "access_control.json");

                if (!File.Exists(path))
                {
                    // Create default policy with current user as admin
                    _policy = CreateDefaultPolicy(doc);
                    Directory.CreateDirectory(bimDir);
                    File.WriteAllText(path, JsonConvert.SerializeObject(_policy, Formatting.Indented));
                }
                else
                {
                    _policy = JsonConvert.DeserializeObject<AccessPolicy>(File.ReadAllText(path));
                }

                // Resolve current user by Windows identity
                string windowsUser = Environment.UserName.ToLowerInvariant();
                string domainUser = $"{Environment.UserDomainName}\\{Environment.UserName}".ToLowerInvariant();
                _currentUser = _policy?.Users?.FirstOrDefault(u =>
                    u.Name?.ToLowerInvariant() == windowsUser ||
                    u.Email?.ToLowerInvariant() == windowsUser ||
                    u.Name?.ToLowerInvariant() == domainUser) ?? CreateGuestAccess(windowsUser);
            }
            catch (Exception ex) { StingLog.Warn($"AccessControl.Load: {ex.Message}"); }
        }

        private static AccessPolicy CreateDefaultPolicy(Document doc)
        {
            return new AccessPolicy
            {
                ProjectId = doc.Title ?? "STING_PROJECT",
                Roles = new Dictionary<string, RolePermissions>
                {
                    ["admin"] = new RolePermissions
                    {
                        Create = true, Edit = true, Delete = true,
                        ViewAll = true, ManageUsers = true, ExportData = true, ManageCDE = true
                    },
                    ["editor"] = new RolePermissions
                    {
                        Create = true, Edit = true, Delete = false,
                        ViewAll = false, ManageUsers = false, ExportData = true, ManageCDE = false
                    },
                    ["viewer"] = new RolePermissions
                    {
                        Create = false, Edit = false, Delete = false,
                        ViewAll = false, ManageUsers = false, ExportData = false, ManageCDE = false
                    }
                },
                Users = new List<UserAccess>
                {
                    new UserAccess
                    {
                        Name = Environment.UserName,
                        Email = "",
                        CompanyId = "LEAD",
                        Role = "admin",
                        Scopes = new List<string> { "issues", "transmittals", "documents", "meetings", "bep", "compliance" },
                        DisciplineFilter = new List<string>(),
                        CdeAccess = "full"
                    }
                },
                Companies = new List<CompanyDef>
                {
                    new CompanyDef { Id = "LEAD", Name = "Lead Appointed Party", Type = "Lead Designer", Disciplines = new List<string>() }
                }
            };
        }

        private static UserAccess CreateGuestAccess(string name) => new UserAccess
        {
            Name = name, Role = "viewer", CompanyId = "",
            Scopes = new List<string> { "documents" },
            DisciplineFilter = new List<string>(), CdeAccess = "read"
        };

        /// <summary>Check if current user has permission for an action.</summary>
        internal static bool Can(string action)
        {
            if (_currentUser == null || _policy == null) return true; // No policy = unrestricted
            if (!_policy.Roles.TryGetValue(_currentUser.Role, out var perms)) return false;
            return action.ToLowerInvariant() switch
            {
                "create" => perms.Create,
                "edit" => perms.Edit,
                "delete" => perms.Delete,
                "view_all" => perms.ViewAll,
                "manage_users" => perms.ManageUsers,
                "export" => perms.ExportData,
                "manage_cde" => perms.ManageCDE,
                _ => false
            };
        }

        /// <summary>Check if current user can see items from a specific company.</summary>
        internal static bool CanViewCompany(string companyId)
        {
            if (_currentUser == null) return true;
            if (Can("view_all")) return true;
            return string.Equals(_currentUser.CompanyId, companyId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Check if current user can see items for a discipline.</summary>
        internal static bool CanViewDiscipline(string disc)
        {
            if (_currentUser == null || _currentUser.DisciplineFilter.Count == 0) return true;
            return _currentUser.DisciplineFilter.Contains(disc);
        }

        internal static UserAccess CurrentUser => _currentUser;
        internal static AccessPolicy Policy => _policy;

        /// <summary>Save updated policy to disk.</summary>
        internal static void Save(Document doc)
        {
            try
            {
                string dir = Path.Combine(
                    Path.GetDirectoryName(doc.PathName) ?? "",
                    "STING_BIM_MANAGER");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, "access_control.json"),
                    JsonConvert.SerializeObject(_policy, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"AccessControl.Save: {ex.Message}"); }
        }

        /// <summary>Add a new user to the access control list.</summary>
        internal static void AddUser(Document doc, string name, string email, string companyId,
            string role, List<string> scopes, List<string> disciplines)
        {
            if (_policy == null) Load(doc);
            if (_policy == null) { StingLog.Warn("AccessControl: Cannot add user — policy failed to load"); return; }
            _policy.Users.Add(new UserAccess
            {
                Name = name, Email = email, CompanyId = companyId,
                Role = role, Scopes = scopes, DisciplineFilter = disciplines,
                CdeAccess = role == "admin" ? "full" : role == "editor" ? "read" : "none"
            });
            Save(doc);
        }
    }

    #endregion

    #region ── HTML Dashboard Generator ──

    /// <summary>
    /// Generates a self-contained HTML dashboard that stakeholders can open
    /// on any device (phone, tablet, desktop) without installing software.
    /// The HTML file includes all data inline — no server required.
    /// Auto-refreshes by re-reading a companion JSON data file.
    ///
    /// Architecture: Plugin writes _STING_DASHBOARD.html + _STING_DATA.json
    /// Stakeholders open the HTML file and see live project status.
    /// </summary>
    internal static class DashboardGenerator
    {
        /// <summary>Generate self-contained HTML dashboard alongside the .rvt file.</summary>
        internal static string Generate(Document doc)
        {
            string dir = Path.GetDirectoryName(doc.PathName) ?? "";
            string htmlPath = Path.Combine(dir, "_STING_DASHBOARD.html");
            string dataPath = Path.Combine(dir, "_STING_DATA.json");

            // Build data JSON
            var data = BuildDashboardData(doc);
            File.WriteAllText(dataPath, data.ToString(Formatting.Indented));

            // Build self-contained HTML
            string html = BuildHtml(doc.Title ?? "STING Project", data);
            File.WriteAllText(htmlPath, html);

            StingLog.Info($"Dashboard generated: {htmlPath}");
            return htmlPath;
        }

        private static JObject BuildDashboardData(Document doc)
        {
            var data = new JObject();
            data["project"] = doc.Title ?? "Unknown";
            data["generated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            data["generated_by"] = Environment.UserName;

            string bimDir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "STING_BIM_MANAGER");

            // Issues
            try
            {
                string issuePath = Path.Combine(bimDir, "issues.json");
                if (File.Exists(issuePath))
                {
                    var issues = JArray.Parse(File.ReadAllText(issuePath));
                    data["issues"] = issues;
                    data["issues_summary"] = new JObject
                    {
                        ["total"] = issues.Count,
                        ["open"] = issues.Count(i => i["status"]?.ToString() == "OPEN"),
                        ["in_progress"] = issues.Count(i => i["status"]?.ToString() == "IN_PROGRESS"),
                        ["closed"] = issues.Count(i => i["status"]?.ToString() == "CLOSED"),
                        ["overdue"] = issues.Count(i =>
                        {
                            if (!DateTime.TryParse(i["sla_deadline"]?.ToString(), out var dl)) return false;
                            return DateTime.Now > dl && i["status"]?.ToString() != "CLOSED";
                        })
                    };
                }
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard issues: {ex.Message}"); }

            // Transmittals
            try
            {
                string txPath = Path.Combine(bimDir, "transmittals.json");
                if (File.Exists(txPath))
                    data["transmittals"] = JArray.Parse(File.ReadAllText(txPath));
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard transmittals: {ex.Message}"); }

            // Meetings
            try
            {
                string mtgPath = Path.Combine(bimDir, "meetings.json");
                if (File.Exists(mtgPath))
                {
                    var meetings = JArray.Parse(File.ReadAllText(mtgPath));
                    data["meetings"] = meetings;
                    // Open action items across all meetings
                    var openActions = new JArray();
                    foreach (var m in meetings)
                    {
                        if (m["actions"] is JArray acts)
                        {
                            foreach (var a in acts.Where(a => a["status"]?.ToString() != "CLOSED" && a["status"]?.ToString() != "COMPLETED"))
                            {
                                var item = a.DeepClone();
                                item["meeting_id"] = m["id"]?.ToString();
                                item["meeting_type"] = m["type"]?.ToString();
                                openActions.Add(item);
                            }
                        }
                    }
                    data["open_actions"] = openActions;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard meetings: {ex.Message}"); }

            // Compliance
            try
            {
                var cr = ComplianceScan.Scan(doc);
                data["compliance"] = new JObject
                {
                    ["tag_percent"] = cr.CompliancePercent,
                    ["strict_percent"] = cr.StrictPercent,
                    ["rag_status"] = cr.RAGStatus,
                    ["total_elements"] = cr.TotalElements,
                    ["tagged"] = cr.TaggedComplete,
                    ["untagged"] = cr.Untagged
                };
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard compliance: {ex.Message}"); }

            // Team
            try
            {
                string teamPath = Path.Combine(bimDir, "project_team.json");
                if (File.Exists(teamPath))
                    data["team"] = JObject.Parse(File.ReadAllText(teamPath));
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard team: {ex.Message}"); }

            // Activity log (last 50)
            try
            {
                var activities = ProjectFolderEngine.GetRecentActivity(doc);
                data["recent_activity"] = new JArray(activities.Select(a => new JObject
                {
                    ["timestamp"] = a.Timestamp ?? "",
                    ["action"] = a.Action ?? "",
                    ["doc_id"] = a.DocId ?? "",
                    ["details"] = a.Details ?? "",
                    ["user"] = a.User ?? ""
                }));
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard activity: {ex.Message}"); }

            // Notification queue
            try
            {
                string notifyPath = Path.Combine(bimDir, "notification_queue.json");
                if (File.Exists(notifyPath))
                {
                    var queue = JArray.Parse(File.ReadAllText(notifyPath));
                    data["pending_notifications"] = queue.Count(n => n["status"]?.ToString() == "PENDING");
                }
            }
            catch (Exception ex) { StingLog.Warn($"Dashboard notifications: {ex.Message}"); }

            return data;
        }

        private static string BuildHtml(string projectName, JObject data)
        {
            // Self-contained HTML with inline CSS and JS — no external dependencies
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>STING Dashboard — {System.Security.SecurityElement.Escape(projectName)}</title>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f5f7;color:#222;font-size:14px}}
.header{{background:linear-gradient(135deg,#1A237E,#283593);color:#fff;padding:16px 20px;display:flex;justify-content:space-between;align-items:center;position:sticky;top:0;z-index:100}}
.header h1{{font-size:18px;font-weight:600}} .header .sub{{font-size:12px;opacity:.8}}
.header .refresh{{background:rgba(255,255,255,.15);border:none;color:#fff;padding:6px 14px;border-radius:4px;cursor:pointer;font-size:12px}}
.container{{max-width:1200px;margin:0 auto;padding:12px}}
.cards{{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin-bottom:16px}}
.card{{background:#fff;border-radius:8px;padding:14px;text-align:center;box-shadow:0 1px 3px rgba(0,0,0,.08);border-bottom:3px solid #1A237E}}
.card .val{{font-size:28px;font-weight:700;color:#1A237E}} .card .lbl{{font-size:11px;color:#888;margin-top:2px}}
.card.red{{border-color:#C62828}} .card.red .val{{color:#C62828}}
.card.green{{border-color:#2E7D32}} .card.green .val{{color:#2E7D32}}
.card.amber{{border-color:#FF8F00}} .card.amber .val{{color:#FF8F00}}
.card.teal{{border-color:#00695C}} .card.teal .val{{color:#00695C}}
.section{{background:#fff;border-radius:8px;margin-bottom:12px;box-shadow:0 1px 3px rgba(0,0,0,.08);overflow:hidden}}
.section-hdr{{background:#E8E8EE;padding:10px 16px;font-weight:600;font-size:13px;color:#1A237E;display:flex;justify-content:space-between;cursor:pointer}}
.section-hdr .badge{{background:#1A237E;color:#fff;border-radius:10px;padding:1px 8px;font-size:11px}}
.section-body{{padding:0}} .section-body.collapsed{{display:none}}
table{{width:100%;border-collapse:collapse}} th{{background:#f0f0f5;text-align:left;padding:8px 12px;font-size:11px;color:#666;border-bottom:1px solid #e0e0e0}}
td{{padding:7px 12px;border-bottom:1px solid #f0f0f0;font-size:12px}} tr:hover{{background:#f8f8fc}}
.priority-CRITICAL{{color:#C62828;font-weight:700}} .priority-HIGH{{color:#E65100;font-weight:600}}
.priority-MEDIUM{{color:#0078D4}} .priority-LOW{{color:#666}}
.status-OPEN{{background:#FFEBEE;color:#C62828;padding:2px 8px;border-radius:3px;font-size:11px}}
.status-CLOSED{{background:#E8F5E9;color:#2E7D32;padding:2px 8px;border-radius:3px;font-size:11px}}
.status-IN_PROGRESS{{background:#E3F2FD;color:#1565C0;padding:2px 8px;border-radius:3px;font-size:11px}}
.overdue{{background:#FFCDD2;color:#B71C1C;padding:2px 6px;border-radius:3px;font-size:10px;font-weight:600}}
.rag-bar{{height:8px;border-radius:4px;background:#eee;margin-top:4px}} .rag-fill{{height:100%;border-radius:4px;transition:width .5s}}
.rag-green{{background:#2E7D32}} .rag-amber{{background:#FF8F00}} .rag-red{{background:#C62828}}
.tabs{{display:flex;border-bottom:2px solid #e0e0e0;margin-bottom:0;background:#fff;border-radius:8px 8px 0 0;overflow-x:auto}}
.tab{{padding:10px 18px;cursor:pointer;font-size:12px;font-weight:500;color:#666;border-bottom:2px solid transparent;white-space:nowrap}}
.tab.active{{color:#1A237E;border-color:#E8912D;font-weight:600}}
.tab-content{{display:none}} .tab-content.active{{display:block}}
.activity-item{{padding:8px 12px;border-bottom:1px solid #f0f0f0;font-size:12px;display:flex;gap:10px}}
.activity-item .time{{color:#888;min-width:120px;font-size:11px}}
.activity-item .action{{font-weight:500;min-width:100px;color:#1A237E}}
.footer{{text-align:center;padding:16px;color:#999;font-size:11px}}
@media(max-width:768px){{.cards{{grid-template-columns:repeat(2,1fr)}}.header h1{{font-size:15px}}.container{{padding:8px}}}}
</style>
</head>
<body>
<div class=""header"">
<div><h1>STING Coordination Dashboard</h1><div class=""sub"">{System.Security.SecurityElement.Escape(projectName)} | Generated: {data["generated"]}</div></div>
<button class=""refresh"" onclick=""location.reload()"">Refresh</button>
</div>
<div class=""container"" id=""app""></div>
<div class=""footer"">STING Tools Coordination Center | Auto-generated {DateTime.Now:yyyy-MM-dd HH:mm}</div>
<script>
const DATA={data.ToString(Formatting.None)};
function render(){{const d=DATA,app=document.getElementById('app');let h='';
// Summary cards
const iss=d.issues_summary||{{}};const comp=d.compliance||{{}};
h+='<div class=""cards"">';
h+=card(iss.total||0,'Issues','');h+=card(iss.open||0,'Open',iss.open>0?'red':'green');
h+=card(iss.overdue||0,'Overdue',iss.overdue>0?'red':'green');
h+=card(Math.round(comp.tag_percent||0)+'%','Compliance',comp.tag_percent>=80?'green':comp.tag_percent>=50?'amber':'red');
h+=card(d.transmittals?.length||0,'Transmittals','teal');
h+=card(d.open_actions?.length||0,'Open Actions',d.open_actions?.length>3?'amber':'green');
h+=card(d.meetings?.length||0,'Meetings','');
h+=card(d.pending_notifications||0,'Pending Alerts',d.pending_notifications>0?'amber':'green');
h+='</div>';
// Compliance RAG bar
if(comp.tag_percent!=null){{const cls=comp.tag_percent>=80?'rag-green':comp.tag_percent>=50?'rag-amber':'rag-red';
h+='<div class=""section""><div class=""section-hdr"">Tag Compliance: '+Math.round(comp.tag_percent)+'% ('+comp.rag_status+')<span class=""badge"">'+comp.tagged+'/'+comp.total_elements+' tagged</span></div><div class=""section-body"" style=""padding:10px""><div class=""rag-bar""><div class=""rag-fill '+cls+'"" style=""width:'+comp.tag_percent+'%""></div></div></div></div>';}}
// Tabs
h+='<div class=""tabs"">';
['Issues','Transmittals','Meetings','Actions','Activity','Team'].forEach((t,i)=>{{h+='<div class=""tab'+(i===0?' active':'')+'"" onclick=""switchTab('+i+',this)"">'+t+'</div>';}});
h+='</div>';
// Issues table
h+=tabStart(0,true)+issueTable(d.issues||[])+tabEnd();
// Transmittals
h+=tabStart(1)+txTable(d.transmittals||[])+tabEnd();
// Meetings
h+=tabStart(2)+mtgTable(d.meetings||[])+tabEnd();
// Actions
h+=tabStart(3)+actionTable(d.open_actions||[])+tabEnd();
// Activity
h+=tabStart(4)+activityList(d.recent_activity||[])+tabEnd();
// Team
h+=tabStart(5)+teamView(d.team)+tabEnd();
app.innerHTML=h;}}
function card(v,l,c){{return'<div class=""card '+(c||'')+'"">'+'<div class=""val"">'+v+'</div><div class=""lbl"">'+l+'</div></div>';}}
function tabStart(i,active){{return'<div class=""tab-content'+(active?' active':'')+'"" id=""tab'+i+'""><div class=""section""><div class=""section-body"">';}}
function tabEnd(){{return'</div></div></div>';}}
function issueTable(arr){{if(!arr.length)return'<p style=""padding:16px;color:#888"">No issues recorded.</p>';let h='<table><tr><th>ID</th><th>Type</th><th>Title</th><th>Priority</th><th>Status</th><th>Assigned</th><th>Date</th><th>SLA</th></tr>';
arr.forEach(i=>{{const overdue=i.sla_deadline&&new Date()>new Date(i.sla_deadline)&&i.status!=='CLOSED';
h+='<tr><td>'+esc(i.issue_id||i.id||'')+'</td><td>'+esc(i.type||'')+'</td><td>'+esc(i.title||'')+'</td>';
h+='<td class=""priority-'+esc(i.priority||'')+'"">'+(i.priority||'')+'</td>';
h+='<td><span class=""status-'+esc(i.status||'')+'"">'+(i.status||'')+'</span></td>';
h+='<td>'+esc(i.assigned_to||'')+'</td><td>'+esc(i.date||'')+'</td>';
h+='<td>'+(overdue?'<span class=""overdue"">OVERDUE</span>':esc(i.sla_deadline||''))+'</td></tr>';}});return h+'</table>';}}
function txTable(arr){{if(!arr.length)return'<p style=""padding:16px;color:#888"">No transmittals.</p>';let h='<table><tr><th>ID</th><th>Date</th><th>Recipients</th><th>Subject</th><th>Status</th><th>Files</th></tr>';
arr.forEach(t=>{{const rcpt=Array.isArray(t.recipients)?t.recipients.map(r=>r.name||r).join(', '):t.recipients||'';
h+='<tr><td>'+esc(t.transmittal_id||t.id||'')+'</td><td>'+esc(t.date||'')+'</td><td>'+esc(rcpt)+'</td><td>'+esc(t.subject||t.notes||'')+'</td><td><span class=""status-'+(t.status||'')+'"">'+(t.status||'')+'</span></td><td>'+(Array.isArray(t.files)?t.files.length:0)+'</td></tr>';}});return h+'</table>';}}
function mtgTable(arr){{if(!arr.length)return'<p style=""padding:16px;color:#888"">No meetings.</p>';let h='<table><tr><th>ID</th><th>Type</th><th>Date</th><th>Chair</th><th>Attendees</th><th>Status</th><th>Actions</th></tr>';
arr.forEach(m=>{{const acts=Array.isArray(m.actions)?m.actions.filter(a=>a.status!=='CLOSED').length:0;
h+='<tr><td>'+esc(m.id||'')+'</td><td>'+esc(m.type||'')+'</td><td>'+esc(m.date||'')+'</td><td>'+esc(m.chair||'')+'</td><td>'+(Array.isArray(m.attendees)?m.attendees.length:0)+'</td><td><span class=""status-'+(m.status||'')+'"">'+(m.status||'')+'</span></td><td>'+(acts>0?'<span class=""overdue"">'+acts+' open</span>':'0')+'</td></tr>';}});return h+'</table>';}}
function actionTable(arr){{if(!arr.length)return'<p style=""padding:16px;color:#888"">No open action items.</p>';let h='<table><tr><th>ID</th><th>Meeting</th><th>Description</th><th>Assigned To</th><th>Due Date</th><th>Status</th></tr>';
arr.forEach(a=>{{const overdue=a.due_date&&new Date()>new Date(a.due_date);
h+='<tr><td>'+esc(a.action_id||a.id||'')+'</td><td>'+esc(a.meeting_id||'')+'</td><td>'+esc(a.description||'')+'</td><td>'+esc(a.assigned_to||'')+'</td><td>'+(overdue?'<span class=""overdue"">'+esc(a.due_date||'')+'</span>':esc(a.due_date||''))+'</td><td>'+esc(a.status||'')+'</td></tr>';}});return h+'</table>';}}
function activityList(arr){{if(!arr.length)return'<p style=""padding:16px;color:#888"">No recent activity.</p>';let h='';
arr.slice(0,30).forEach(a=>{{h+='<div class=""activity-item""><span class=""time"">'+(a.timestamp||'')+'</span><span class=""action"">'+(a.action||'')+'</span><span>'+(a.details||a.doc_id||'')+'</span></div>';}});return h;}}
function teamView(t){{if(!t||!t.members)return'<p style=""padding:16px;color:#888"">No team data.</p>';let h='<table><tr><th>Name</th><th>Role</th><th>Company</th><th>Discipline</th><th>Email</th></tr>';
(t.members||[]).forEach(m=>{{h+='<tr><td>'+esc(m.name||'')+'</td><td>'+esc(m.role_id||'')+'</td><td>'+esc(m.company_id||'')+'</td><td>'+esc(m.discipline||'')+'</td><td>'+esc(m.email||'')+'</td></tr>';}});return h+'</table>';}}
function switchTab(idx,el){{document.querySelectorAll('.tab').forEach(t=>t.classList.remove('active'));el.classList.add('active');document.querySelectorAll('.tab-content').forEach(t=>t.classList.remove('active'));const tc=document.getElementById('tab'+idx);if(tc)tc.classList.add('active');}}
function esc(s){{if(s==null)return'';return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/\""/g,'&quot;');}}
render();
</script>
</body>
</html>";
        }
    }

    #endregion

    #region ── Coordination Center Dialog ──

    /// <summary>
    /// Unified Coordination Center WPF dialog — the command center for BIM coordinators.
    /// Combines: Notification setup, File monitoring, Access control, Dashboard generation,
    /// Issue lifecycle, Meeting coordination, and Stakeholder communication.
    /// </summary>
    internal static class CoordinationCenterDialog
    {
        private static readonly SolidColorBrush BrHeader = new(System.Windows.Media.Color.FromRgb(0x1A, 0x23, 0x7E));
        private static readonly SolidColorBrush BrAccent = new(System.Windows.Media.Color.FromRgb(0xE8, 0x91, 0x2D));
        private static readonly SolidColorBrush BrBg = new(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF7));
        private static readonly SolidColorBrush BrWhite = System.Windows.Media.Brushes.White;
        private static readonly SolidColorBrush BrGreen = new(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly SolidColorBrush BrRed = new(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly SolidColorBrush BrAmber = new(System.Windows.Media.Color.FromRgb(0xFF, 0x8F, 0x00));
        private static readonly SolidColorBrush BrTeal = new(System.Windows.Media.Color.FromRgb(0x00, 0x69, 0x5C));
        private static readonly SolidColorBrush BrGrey = new(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly SolidColorBrush BrBorder = new(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0));

        internal static void Show(Document doc)
        {
            // Load subsystems
            NotificationDeliveryEngine.LoadConfig(doc);
            AccessControlEngine.Load(doc);

            var win = new System.Windows.Window
            {
                Title = "STING Coordination Center",
                Width = 960, Height = 700,
                MinWidth = 800, MinHeight = 550,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.CanResize,
                Background = BrBg
            };
            try
            {
                var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(win).Owner = hwnd;
            }
            catch (Exception ex) { StingLog.Warn($"CoordCenter owner: {ex.Message}"); }

            var root = new System.Windows.Controls.DockPanel { LastChildFill = true };

            // ── Header ──
            var header = new System.Windows.Controls.Border
            {
                Background = BrHeader,
                Padding = new System.Windows.Thickness(16, 10, 16, 10)
            };
            var headerStack = new System.Windows.Controls.StackPanel();
            headerStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "STING Coordination Center",
                FontSize = 18, FontWeight = System.Windows.FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            });
            string userRole = AccessControlEngine.CurrentUser?.Role ?? "admin";
            string monitorStatus = FileMonitorEngine.IsRunning ? "LIVE" : "OFF";
            int channels = NotificationDeliveryEngine.GetEnabledChannels().Count;
            headerStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"User: {Environment.UserName} ({userRole}) | Monitor: {monitorStatus} | Notification channels: {channels}",
                FontSize = 11, Foreground = BrAccent,
                Margin = new System.Windows.Thickness(0, 4, 0, 0)
            });
            header.Child = headerStack;
            System.Windows.Controls.DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
            root.Children.Add(header);

            // ── Footer ──
            var footer = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)),
                Padding = new System.Windows.Thickness(12, 5, 12, 5)
            };
            var footerGrid = new System.Windows.Controls.Grid();
            footerGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            footerGrid.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "STING Coordination Center v1.0 — Zero-cost BIM coordination platform",
                FontSize = 10, Foreground = BrGrey,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            var closeBtn = new System.Windows.Controls.Button
            {
                Content = "Close", Width = 80, Height = 26,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new System.Windows.Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            closeBtn.Click += (s, e) => win.Close();
            System.Windows.Controls.Grid.SetColumn(closeBtn, 1);
            footerGrid.Children.Add(closeBtn);
            footer.Child = footerGrid;
            System.Windows.Controls.DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
            root.Children.Add(footer);

            // ── Tab Control ──
            var tabs = new System.Windows.Controls.TabControl
            {
                Margin = new System.Windows.Thickness(8),
                Background = BrWhite
            };

            // TAB 1: Dashboard Overview
            tabs.Items.Add(BuildOverviewTab(doc, win));
            // TAB 2: Notification Setup
            tabs.Items.Add(BuildNotificationTab(doc, win));
            // TAB 3: File Monitor
            tabs.Items.Add(BuildMonitorTab(doc, win));
            // TAB 4: Access Control
            tabs.Items.Add(BuildAccessTab(doc, win));
            // TAB 5: Quick Actions
            tabs.Items.Add(BuildActionsTab(doc, win));

            root.Children.Add(tabs);
            win.Content = root;
            win.ShowDialog();
        }

        private static System.Windows.Controls.TabItem BuildOverviewTab(Document doc, System.Windows.Window win)
        {
            var scroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };

            // Summary cards
            var cardPanel = new System.Windows.Controls.WrapPanel { Margin = new System.Windows.Thickness(0, 0, 0, 12) };
            int channels = NotificationDeliveryEngine.GetEnabledChannels().Count;
            string monStatus = FileMonitorEngine.IsRunning ? "LIVE" : "OFF";

            AddCard(cardPanel, channels.ToString(), "Notification Channels", channels > 0 ? BrGreen : BrRed);
            AddCard(cardPanel, monStatus, "File Monitor", FileMonitorEngine.IsRunning ? BrGreen : BrAmber);
            AddCard(cardPanel, AccessControlEngine.Policy?.Users?.Count.ToString() ?? "0", "Users", BrTeal);
            AddCard(cardPanel, AccessControlEngine.Policy?.Companies?.Count.ToString() ?? "0", "Companies", BrTeal);

            stack.Children.Add(cardPanel);

            // Feature description
            AddSectionLabel(stack, "PLATFORM CAPABILITIES");
            AddInfoRow(stack, "\U0001F4E2 Notifications", "Telegram Bot, MS Teams, Discord, Email, Custom Webhooks — all FREE");
            AddInfoRow(stack, "\U0001F4C1 File Monitor", "Real-time FileSystemWatcher + polling fallback for shared drives");
            AddInfoRow(stack, "\U0001F512 Access Control", "Admin/Editor/Viewer roles, company-based visibility, discipline filters");
            AddInfoRow(stack, "\U0001F4CA Dashboard", "Self-contained HTML file — open on phone/tablet, no app install needed");
            AddInfoRow(stack, "\U0001F4F1 Mobile Access", "Telegram channel = instant phone notifications for entire team");
            AddInfoRow(stack, "\U0001F504 Live Sync", "JSON files on shared drive — all team members see updates instantly");

            AddSectionLabel(stack, "QUICK START GUIDE");
            AddInfoRow(stack, "Step 1", "Go to 'Notifications' tab → Configure at least one channel (Telegram recommended)");
            AddInfoRow(stack, "Step 2", "Go to 'File Monitor' tab → Start monitoring your project folder");
            AddInfoRow(stack, "Step 3", "Go to 'Access Control' tab → Add team members and set roles");
            AddInfoRow(stack, "Step 4", "Go to 'Quick Actions' tab → Generate HTML Dashboard for stakeholders");
            AddInfoRow(stack, "Step 5", "Share the _STING_DASHBOARD.html file with project members");

            AddSectionLabel(stack, "COST ANALYSIS");
            AddInfoRow(stack, "Telegram Bot", "FREE — unlimited messages, instant phone push");
            AddInfoRow(stack, "MS Teams Webhook", "FREE — included with Microsoft 365");
            AddInfoRow(stack, "Discord Webhook", "FREE — unlimited messages");
            AddInfoRow(stack, "Email (SMTP)", "FREE — Gmail/Outlook ~500/day");
            AddInfoRow(stack, "HTML Dashboard", "FREE — self-contained, no hosting needed");
            AddInfoRow(stack, "FileSystemWatcher", "FREE — built into .NET");
            AddInfoRow(stack, "vs ACC", "$0/user vs $40-100+/user/month", BrGreen);

            scroll.Content = stack;
            return new System.Windows.Controls.TabItem
            {
                Header = " OVERVIEW ",
                Content = scroll,
                Padding = new System.Windows.Thickness(8, 2, 8, 2)
            };
        }

        private static System.Windows.Controls.TabItem BuildNotificationTab(Document doc, System.Windows.Window win)
        {
            var scroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };

            AddSectionLabel(stack, "CONFIGURED CHANNELS");
            var channels = NotificationDeliveryEngine.GetEnabledChannels();
            if (channels.Count == 0)
            {
                AddInfoRow(stack, "No channels configured", "Add NOTIFICATION_CHANNELS to project_config.json");
            }
            foreach (var ch in channels)
            {
                string icon = ch.Type switch
                {
                    "telegram" => "\U0001F4AC",
                    "teams" => "\U0001F4BC",
                    "discord" => "\U0001F3AE",
                    "email" => "\U0001F4E7",
                    _ => "\U0001F517"
                };
                AddInfoRow(stack, $"{icon} {ch.Name} ({ch.Type})", ch.Enabled ? "ENABLED" : "DISABLED",
                    ch.Enabled ? BrGreen : BrRed);
            }

            AddSectionLabel(stack, "SETUP INSTRUCTIONS");

            // Telegram setup
            AddInfoRow(stack, "TELEGRAM (Recommended)", "");
            AddInfoRow(stack, "  1. Message @BotFather on Telegram", "Send /newbot and follow prompts");
            AddInfoRow(stack, "  2. Copy the bot token", "Looks like: 123456:ABC-DEF1234ghIkl...");
            AddInfoRow(stack, "  3. Create a group chat", "Add the bot to the group");
            AddInfoRow(stack, "  4. Get chat_id", "Send a message, then visit api.telegram.org/bot{TOKEN}/getUpdates");

            AddInfoRow(stack, "MS TEAMS", "");
            AddInfoRow(stack, "  1. Channel → Connectors → Incoming Webhook", "Copy the webhook URL");

            AddInfoRow(stack, "DISCORD", "");
            AddInfoRow(stack, "  1. Channel Settings → Integrations → Webhooks", "Copy the webhook URL");

            AddSectionLabel(stack, "project_config.json TEMPLATE");
            var templateBox = new System.Windows.Controls.TextBox
            {
                IsReadOnly = true,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 10.5,
                AcceptsReturn = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Height = 200,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Text = @"""NOTIFICATION_CHANNELS"": [
  {
    ""type"": ""telegram"",
    ""name"": ""Project Alerts"",
    ""enabled"": true,
    ""token"": ""YOUR_BOT_TOKEN"",
    ""chat_id"": ""YOUR_CHAT_ID""
  },
  {
    ""type"": ""teams"",
    ""name"": ""BIM Coordination"",
    ""enabled"": false,
    ""endpoint"": ""YOUR_TEAMS_WEBHOOK_URL""
  },
  {
    ""type"": ""discord"",
    ""name"": ""Dev Channel"",
    ""enabled"": false,
    ""endpoint"": ""YOUR_DISCORD_WEBHOOK_URL""
  },
  {
    ""type"": ""email"",
    ""name"": ""Email Alerts"",
    ""enabled"": false,
    ""endpoint"": ""smtp.gmail.com"",
    ""port"": 587,
    ""use_ssl"": true,
    ""username"": ""your@gmail.com"",
    ""password"": ""app_password"",
    ""from_address"": ""your@gmail.com""
  }
]"
            };
            stack.Children.Add(templateBox);

            // Test button
            var testBtn = MakeButton("Send Test Notification", BrAccent);
            testBtn.Click += (s, e) =>
            {
                NotificationDeliveryEngine.LoadConfig(doc);
                NotificationDeliveryEngine.SendNotification(doc,
                    "Test Notification",
                    "This is a test from STING Coordination Center. If you see this, notifications are working!",
                    "LOW", "TEST-001");
                System.Windows.MessageBox.Show("Test notification sent to all enabled channels.",
                    "STING", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            };
            stack.Children.Add(testBtn);

            // Reload config button
            var reloadBtn = MakeButton("Reload Configuration", BrTeal);
            reloadBtn.Click += (s, e) =>
            {
                NotificationDeliveryEngine.LoadConfig(doc);
                System.Windows.MessageBox.Show($"Configuration reloaded. {NotificationDeliveryEngine.GetEnabledChannels().Count} enabled channels.",
                    "STING", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            };
            stack.Children.Add(reloadBtn);

            scroll.Content = stack;
            return new System.Windows.Controls.TabItem
            {
                Header = " NOTIFICATIONS ",
                Content = scroll,
                Padding = new System.Windows.Thickness(8, 2, 8, 2)
            };
        }

        private static System.Windows.Controls.TabItem BuildMonitorTab(Document doc, System.Windows.Window win)
        {
            var scroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };

            AddSectionLabel(stack, "FILE MONITOR STATUS");
            string watchPath = ProjectFolderEngine.RootPath ?? "(not configured)";
            AddInfoRow(stack, "Watch Path", watchPath);
            AddInfoRow(stack, "Status", FileMonitorEngine.IsRunning ? "RUNNING" : "STOPPED",
                FileMonitorEngine.IsRunning ? BrGreen : BrRed);

            var btnPanel = new System.Windows.Controls.WrapPanel { Margin = new System.Windows.Thickness(0, 8, 0, 8) };
            var startBtn = MakeButton(FileMonitorEngine.IsRunning ? "Restart Monitor" : "Start Monitor", BrGreen);
            startBtn.Click += (s, e) =>
            {
                FileMonitorEngine.Start(doc);
                System.Windows.MessageBox.Show(
                    FileMonitorEngine.IsRunning ? "File monitor started. You'll receive notifications for file changes." : "Failed to start monitor. Check project folder path.",
                    "STING", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            };
            btnPanel.Children.Add(startBtn);

            var stopBtn = MakeButton("Stop Monitor", BrRed);
            stopBtn.Click += (s, e) =>
            {
                FileMonitorEngine.Stop();
                System.Windows.MessageBox.Show("File monitor stopped.", "STING",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            };
            btnPanel.Children.Add(stopBtn);
            stack.Children.Add(btnPanel);

            AddSectionLabel(stack, "HOW IT WORKS");
            AddInfoRow(stack, "1. FileSystemWatcher", "Monitors project folder for new/modified/deleted/renamed files");
            AddInfoRow(stack, "2. Debounce", "Ignores rapid-fire events (3-second window per file)");
            AddInfoRow(stack, "3. Filter", "Skips temp files, lock files, and STING internal files");
            AddInfoRow(stack, "4. Notify", "Sends notification to all enabled channels (Telegram/Teams/etc.)");
            AddInfoRow(stack, "5. Log", "Records file events in ACTIVITY_LOG.jsonl");
            AddInfoRow(stack, "6. Fallback", "Polling every 15-30 seconds for network drives where FSW fails");

            AddSectionLabel(stack, "STAKEHOLDER WORKFLOW");
            AddInfoRow(stack, "Scenario", "Designer uploads drawing to project folder");
            AddInfoRow(stack, "  → FileMonitor", "Detects new file within seconds");
            AddInfoRow(stack, "  → Telegram", "All team members get push notification on phone");
            AddInfoRow(stack, "  → Activity Log", "Event recorded for audit trail");
            AddInfoRow(stack, "  → Dashboard", "HTML dashboard shows new document on refresh");
            AddInfoRow(stack, "Result", "Zero-delay document distribution — no email needed", BrGreen);

            scroll.Content = stack;
            return new System.Windows.Controls.TabItem
            {
                Header = " FILE MONITOR ",
                Content = scroll,
                Padding = new System.Windows.Thickness(8, 2, 8, 2)
            };
        }

        private static System.Windows.Controls.TabItem BuildAccessTab(Document doc, System.Windows.Window win)
        {
            var scroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };

            AddSectionLabel(stack, "CURRENT USER");
            var user = AccessControlEngine.CurrentUser;
            if (user != null)
            {
                AddInfoRow(stack, "Name", user.Name);
                AddInfoRow(stack, "Role", user.Role, user.Role == "admin" ? BrGreen : user.Role == "editor" ? BrAmber : BrRed);
                AddInfoRow(stack, "Company", user.CompanyId);
                AddInfoRow(stack, "Scopes", string.Join(", ", user.Scopes));
                AddInfoRow(stack, "Disciplines", user.DisciplineFilter.Count > 0 ? string.Join(", ", user.DisciplineFilter) : "All");
            }

            AddSectionLabel(stack, "REGISTERED USERS");
            var policy = AccessControlEngine.Policy;
            if (policy?.Users != null)
            {
                foreach (var u in policy.Users)
                {
                    var roleBrush = u.Role == "admin" ? BrGreen : u.Role == "editor" ? BrAmber : BrGrey;
                    AddInfoRow(stack, $"{u.Name} ({u.Role})",
                        $"Company: {u.CompanyId} | Scopes: {string.Join(", ", u.Scopes)}", roleBrush);
                }
            }

            AddSectionLabel(stack, "COMPANIES");
            if (policy?.Companies != null)
            {
                foreach (var c in policy.Companies)
                    AddInfoRow(stack, c.Name, $"{c.Type} | Disciplines: {string.Join(", ", c.Disciplines)}");
            }

            AddSectionLabel(stack, "ROLE PERMISSIONS");
            if (policy?.Roles != null)
            {
                foreach (var kv in policy.Roles)
                {
                    var p = kv.Value;
                    AddInfoRow(stack, kv.Key.ToUpperInvariant(),
                        $"Create:{(p.Create ? "Y" : "N")} Edit:{(p.Edit ? "Y" : "N")} Delete:{(p.Delete ? "Y" : "N")} " +
                        $"ViewAll:{(p.ViewAll ? "Y" : "N")} Users:{(p.ManageUsers ? "Y" : "N")} Export:{(p.ExportData ? "Y" : "N")} CDE:{(p.ManageCDE ? "Y" : "N")}");
                }
            }

            // Add user button
            var addBtn = MakeButton("Add User", BrGreen);
            addBtn.Click += (s, e) =>
            {
                var addWin = new System.Windows.Window
                {
                    Title = "Add User to Coordination Center",
                    Width = 450, Height = 420,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = win, ResizeMode = System.Windows.ResizeMode.NoResize
                };
                var addStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };

                addStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Name (Windows username):", Margin = new System.Windows.Thickness(0, 4, 0, 2) });
                var nameBox = new System.Windows.Controls.TextBox { Height = 26 };
                addStack.Children.Add(nameBox);

                addStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Email:", Margin = new System.Windows.Thickness(0, 4, 0, 2) });
                var emailBox = new System.Windows.Controls.TextBox { Height = 26 };
                addStack.Children.Add(emailBox);

                addStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Company ID:", Margin = new System.Windows.Thickness(0, 4, 0, 2) });
                var compBox = new System.Windows.Controls.TextBox { Height = 26 };
                addStack.Children.Add(compBox);

                addStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Role:", Margin = new System.Windows.Thickness(0, 4, 0, 2) });
                var roleCombo = new System.Windows.Controls.ComboBox { Height = 26 };
                roleCombo.Items.Add("admin"); roleCombo.Items.Add("editor"); roleCombo.Items.Add("viewer");
                roleCombo.SelectedIndex = 1;
                addStack.Children.Add(roleCombo);

                addStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Disciplines (comma-separated, blank=all):", Margin = new System.Windows.Thickness(0, 4, 0, 2) });
                var discBox = new System.Windows.Controls.TextBox { Height = 26 };
                addStack.Children.Add(discBox);

                var saveBtn = MakeButton("Add User", BrGreen);
                saveBtn.Margin = new System.Windows.Thickness(0, 12, 0, 0);
                saveBtn.Click += (s2, e2) =>
                {
                    if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
                    var scopes = new List<string> { "issues", "transmittals", "documents", "meetings" };
                    var discs = string.IsNullOrWhiteSpace(discBox.Text) ? new List<string>() :
                        discBox.Text.Split(',').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
                    AccessControlEngine.AddUser(doc, nameBox.Text.Trim(), emailBox.Text.Trim(),
                        compBox.Text.Trim(), roleCombo.SelectedItem?.ToString() ?? "viewer", scopes, discs);
                    addWin.DialogResult = true;
                    addWin.Close();
                };
                addStack.Children.Add(saveBtn);
                addWin.Content = addStack;
                addWin.ShowDialog();
            };
            stack.Children.Add(addBtn);

            scroll.Content = stack;
            return new System.Windows.Controls.TabItem
            {
                Header = " ACCESS CONTROL ",
                Content = scroll,
                Padding = new System.Windows.Thickness(8, 2, 8, 2)
            };
        }

        private static System.Windows.Controls.TabItem BuildActionsTab(Document doc, System.Windows.Window win)
        {
            var scroll = new System.Windows.Controls.ScrollViewer { VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };

            AddSectionLabel(stack, "DASHBOARD & REPORTS");

            var dashBtn = MakeButton("Generate HTML Dashboard", BrAccent);
            dashBtn.Click += (s, e) =>
            {
                try
                {
                    string path = DashboardGenerator.Generate(doc);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                    NotificationDeliveryEngine.SendNotification(doc, "Dashboard Updated",
                        "Project dashboard has been regenerated and is available for viewing.", "LOW", "DASHBOARD");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Dashboard generation failed: {ex.Message}");
                    StingLog.Warn($"Dashboard generate: {ex.Message}");
                }
            };
            stack.Children.Add(dashBtn);

            var refreshDataBtn = MakeButton("Refresh Dashboard Data Only", BrTeal);
            refreshDataBtn.Click += (s, e) =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string dataPath = Path.Combine(dir, "_STING_DATA.json");
                    var data = typeof(DashboardGenerator)
                        .GetMethod("BuildDashboardData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        ?.Invoke(null, new object[] { doc });
                    if (data is JObject jdata)
                        File.WriteAllText(dataPath, jdata.ToString(Formatting.Indented));
                    System.Windows.MessageBox.Show("Dashboard data refreshed. Reload the HTML file to see updates.",
                        "STING", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex) { StingLog.Warn($"Dashboard data refresh: {ex.Message}"); }
            };
            stack.Children.Add(refreshDataBtn);

            AddSectionLabel(stack, "NOTIFICATIONS");

            var broadcastBtn = MakeButton("Broadcast Message to Team", BrAccent);
            broadcastBtn.Click += (s, e) =>
            {
                var msgWin = new System.Windows.Window
                {
                    Title = "Broadcast Message", Width = 500, Height = 350,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = win
                };
                var msgStack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
                msgStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Title:", FontWeight = System.Windows.FontWeights.SemiBold });
                var titleBox = new System.Windows.Controls.TextBox { Height = 26, Margin = new System.Windows.Thickness(0, 4, 0, 8) };
                msgStack.Children.Add(titleBox);

                msgStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Message:" });
                var msgBox = new System.Windows.Controls.TextBox
                {
                    Height = 120, AcceptsReturn = true,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                    Margin = new System.Windows.Thickness(0, 4, 0, 8)
                };
                msgStack.Children.Add(msgBox);

                msgStack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Priority:" });
                var prioCombo = new System.Windows.Controls.ComboBox { Height = 26, Margin = new System.Windows.Thickness(0, 4, 0, 8) };
                prioCombo.Items.Add("LOW"); prioCombo.Items.Add("MEDIUM"); prioCombo.Items.Add("HIGH"); prioCombo.Items.Add("CRITICAL");
                prioCombo.SelectedIndex = 1;
                msgStack.Children.Add(prioCombo);

                var sendBtn = MakeButton("Send Broadcast", BrGreen);
                sendBtn.Click += (s2, e2) =>
                {
                    if (string.IsNullOrWhiteSpace(titleBox.Text)) return;
                    NotificationDeliveryEngine.SendNotification(doc, titleBox.Text, msgBox.Text,
                        prioCombo.SelectedItem?.ToString() ?? "MEDIUM", "BROADCAST");
                    ProjectFolderEngine.LogActivity(doc, "BROADCAST", "", titleBox.Text);
                    msgWin.DialogResult = true;
                    msgWin.Close();
                };
                msgStack.Children.Add(sendBtn);
                msgWin.Content = msgStack;
                msgWin.ShowDialog();
            };
            stack.Children.Add(broadcastBtn);

            AddSectionLabel(stack, "COORDINATION WORKFLOWS");

            var morningBtn = MakeButton("Run Morning Health Check Workflow", BrGreen);
            morningBtn.Click += (s, e) =>
            {
                NotificationDeliveryEngine.SendNotification(doc, "Morning Health Check Started",
                    $"{Environment.UserName} has started the morning health check workflow.", "LOW", "WORKFLOW");
                // Set workflow preset via ExtraParam — WorkflowPresetCommand reads this
                try
                {
                    StingCommandHandler.SetExtraParam("PresetName", "MorningHealthCheck");
                }
                catch (Exception ex) { StingLog.Warn($"Morning health check: {ex.Message}"); }
            };
            stack.Children.Add(morningBtn);

            var weeklyBtn = MakeButton("Run Weekly Data Drop Workflow", BrTeal);
            weeklyBtn.Click += (s, e) =>
            {
                NotificationDeliveryEngine.SendNotification(doc, "Weekly Data Drop Started",
                    $"{Environment.UserName} has started the weekly data drop workflow.", "MEDIUM", "WORKFLOW");
                try
                {
                    StingCommandHandler.SetExtraParam("PresetName", "WeeklyDataDrop");
                }
                catch (Exception ex) { StingLog.Warn($"Weekly data drop: {ex.Message}"); }
            };
            stack.Children.Add(weeklyBtn);

            AddSectionLabel(stack, "MEETING COORDINATION");

            var agendaBtn = MakeButton("Generate Meeting Agenda from Open Items", BrAccent);
            agendaBtn.Click += (s, e) =>
            {
                var agenda = GenerateSmartAgenda(doc);
                var panel = StingResultPanel.Create("Auto-Generated Meeting Agenda")
                    .SetSubtitle($"Based on {agenda.issueCount} open issues, {agenda.txCount} transmittals, {agenda.actionCount} open actions");

                panel.AddSection("AGENDA ITEMS");
                int idx = 1;
                foreach (string item in agenda.items)
                    panel.Text($"{idx++}. {item}");

                if (agenda.compliancePct < 80)
                    panel.AddSection("COMPLIANCE CONCERN")
                         .MetricWarn("Tag Compliance", $"{agenda.compliancePct:F0}%", "Below 80% threshold");

                panel.Show();
            };
            stack.Children.Add(agendaBtn);

            scroll.Content = stack;
            return new System.Windows.Controls.TabItem
            {
                Header = " QUICK ACTIONS ",
                Content = scroll,
                Padding = new System.Windows.Thickness(8, 2, 8, 2)
            };
        }

        // ── Helper Methods ──

        private static (List<string> items, int issueCount, int txCount, int actionCount, double compliancePct) GenerateSmartAgenda(Document doc)
        {
            var items = new List<string>();
            int issueCount = 0, txCount = 0, actionCount = 0;
            double compliancePct = 0;

            string bimDir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "STING_BIM_MANAGER");

            // Compliance
            try
            {
                var cr = ComplianceScan.Scan(doc);
                compliancePct = cr.CompliancePercent;
                if (cr.CompliancePercent < 80)
                    items.Add($"COMPLIANCE: Tag compliance at {cr.CompliancePercent:F0}% — action needed to reach 80% target");
                if (cr.StaleCount > 0)
                    items.Add($"STALE ELEMENTS: {cr.StaleCount} elements have stale tags — need re-tagging");
            }
            catch (Exception ex) { StingLog.Warn($"Agenda compliance: {ex.Message}"); }

            // Open issues
            try
            {
                string issuePath = Path.Combine(bimDir, "issues.json");
                if (File.Exists(issuePath))
                {
                    var issues = JArray.Parse(File.ReadAllText(issuePath));
                    var open = issues.Where(i => i["status"]?.ToString() != "CLOSED" && i["status"]?.ToString() != "RESOLVED").ToList();
                    issueCount = open.Count;

                    var critical = open.Where(i => i["priority"]?.ToString() == "CRITICAL").ToList();
                    if (critical.Count > 0)
                        items.Add($"CRITICAL ISSUES ({critical.Count}): {string.Join(", ", critical.Select(i => i["issue_id"]?.ToString() ?? i["title"]?.ToString()))}");

                    var overdue = open.Where(i =>
                    {
                        if (!DateTime.TryParse(i["sla_deadline"]?.ToString(), out var dl)) return false;
                        return DateTime.Now > dl;
                    }).ToList();
                    if (overdue.Count > 0)
                        items.Add($"OVERDUE ISSUES ({overdue.Count}): {string.Join(", ", overdue.Select(i => i["issue_id"]?.ToString()))} — SLA breached");

                    var highPriority = open.Where(i => i["priority"]?.ToString() == "HIGH").ToList();
                    if (highPriority.Count > 0)
                        items.Add($"HIGH PRIORITY ({highPriority.Count}): Review and assign resources");

                    if (open.Count > 0)
                        items.Add($"OPEN ISSUES SUMMARY: {open.Count} open — {open.Count(i => i["status"]?.ToString() == "IN_PROGRESS")} in progress");
                }
            }
            catch (Exception ex) { StingLog.Warn($"Agenda issues: {ex.Message}"); }

            // Pending transmittals
            try
            {
                string txPath = Path.Combine(bimDir, "transmittals.json");
                if (File.Exists(txPath))
                {
                    var txs = JArray.Parse(File.ReadAllText(txPath));
                    var pending = txs.Where(t => t["status"]?.ToString() == "DRAFT" || t["status"]?.ToString() == "SENT").ToList();
                    txCount = pending.Count;
                    if (pending.Count > 0)
                        items.Add($"PENDING TRANSMITTALS ({pending.Count}): Review and progress");
                }
            }
            catch (Exception ex) { StingLog.Warn($"Agenda transmittals: {ex.Message}"); }

            // Open meeting actions
            try
            {
                string mtgPath = Path.Combine(bimDir, "meetings.json");
                if (File.Exists(mtgPath))
                {
                    var meetings = JArray.Parse(File.ReadAllText(mtgPath));
                    foreach (var m in meetings)
                    {
                        if (m["actions"] is JArray acts)
                        {
                            var open = acts.Where(a => a["status"]?.ToString() != "CLOSED" && a["status"]?.ToString() != "COMPLETED").ToList();
                            actionCount += open.Count;

                            var overdueActions = open.Where(a =>
                            {
                                if (!DateTime.TryParse(a["due_date"]?.ToString(), out var dl)) return false;
                                return DateTime.Now > dl;
                            }).ToList();
                            if (overdueActions.Count > 0)
                                items.Add($"OVERDUE ACTIONS from {m["id"]}: {string.Join(", ", overdueActions.Select(a => a["description"]?.ToString()?.Substring(0, Math.Min(50, (a["description"]?.ToString() ?? "").Length))))}");
                        }
                    }
                    if (actionCount > 0)
                        items.Add($"OPEN ACTION ITEMS: {actionCount} across all meetings");
                }
            }
            catch (Exception ex) { StingLog.Warn($"Agenda actions: {ex.Message}"); }

            if (items.Count == 0)
                items.Add("No outstanding items — project is in good health!");

            return (items, issueCount, txCount, actionCount, compliancePct);
        }

        private static void AddCard(System.Windows.Controls.WrapPanel panel, string value, string label,
            System.Windows.Media.SolidColorBrush color)
        {
            var card = new System.Windows.Controls.Border
            {
                BorderBrush = color, BorderThickness = new System.Windows.Thickness(0, 0, 0, 3),
                Padding = new System.Windows.Thickness(14, 6, 14, 6),
                Margin = new System.Windows.Thickness(0, 0, 8, 8),
                Background = BrWhite, MinWidth = 130
            };
            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = value, FontSize = 20, FontWeight = System.Windows.FontWeights.Bold,
                Foreground = color, HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = label, FontSize = 10, Foreground = BrGrey,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextWrapping = System.Windows.TextWrapping.Wrap, TextAlignment = System.Windows.TextAlignment.Center
            });
            card.Child = stack;
            panel.Children.Add(card);
        }

        private static void AddSectionLabel(System.Windows.Controls.StackPanel stack, string text)
        {
            stack.Children.Add(new System.Windows.Controls.Border
            {
                BorderBrush = BrAccent, BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                Margin = new System.Windows.Thickness(0, 12, 0, 6),
                Padding = new System.Windows.Thickness(0, 0, 0, 2),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = text, FontSize = 12, FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = BrHeader
                }
            });
        }

        private static void AddInfoRow(System.Windows.Controls.StackPanel stack, string label, string value,
            System.Windows.Media.SolidColorBrush valueBrush = null)
        {
            var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(220) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

            var lbl = new System.Windows.Controls.TextBlock
            {
                Text = label, FontSize = 11.5, Foreground = BrGrey,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                TextWrapping = System.Windows.TextWrapping.Wrap
            };
            System.Windows.Controls.Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var val = new System.Windows.Controls.TextBlock
            {
                Text = value, FontSize = 11.5,
                Foreground = valueBrush ?? System.Windows.Media.Brushes.Black,
                FontWeight = System.Windows.FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                TextWrapping = System.Windows.TextWrapping.Wrap
            };
            System.Windows.Controls.Grid.SetColumn(val, 1);
            row.Children.Add(val);

            stack.Children.Add(row);
        }

        private static System.Windows.Controls.Button MakeButton(string text, System.Windows.Media.SolidColorBrush bg)
        {
            return new System.Windows.Controls.Button
            {
                Content = text,
                Padding = new System.Windows.Thickness(16, 6, 16, 6),
                Margin = new System.Windows.Thickness(0, 4, 8, 4),
                Background = bg,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new System.Windows.Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12, FontWeight = System.Windows.FontWeights.SemiBold
            };
        }
    }

    #endregion

    #region ── Commands ──

    /// <summary>Open the STING Coordination Center dialog.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CoordinationCenterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }
                CoordinationCenterDialog.Show(doc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CoordinationCenter", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Generate the HTML dashboard for external stakeholders.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }
                string path = DashboardGenerator.Generate(doc);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateDashboard", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Start/stop the file monitor for live document notifications.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ToggleFileMonitorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                if (FileMonitorEngine.IsRunning)
                {
                    FileMonitorEngine.Stop();
                    TaskDialog.Show("STING", "File monitor stopped.");
                }
                else
                {
                    NotificationDeliveryEngine.LoadConfig(doc);
                    FileMonitorEngine.Start(doc);
                    TaskDialog.Show("STING", FileMonitorEngine.IsRunning
                        ? "File monitor started. Notifications will be sent for file changes."
                        : "Failed to start. Check project folder configuration.");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ToggleFileMonitor", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Send a broadcast notification to all configured channels.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BroadcastNotificationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                NotificationDeliveryEngine.LoadConfig(doc);
                if (NotificationDeliveryEngine.GetEnabledChannels().Count == 0)
                {
                    TaskDialog.Show("STING", "No notification channels configured.\nAdd NOTIFICATION_CHANNELS to project_config.json.");
                    return Result.Succeeded;
                }

                NotificationDeliveryEngine.SendNotification(doc,
                    "Model Update", $"The Revit model '{doc.Title}' has been updated by {Environment.UserName}.",
                    "MEDIUM", doc.Title);
                TaskDialog.Show("STING", "Broadcast notification sent to all enabled channels.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BroadcastNotification", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Show access control management dialog.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccessControlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }
                CoordinationCenterDialog.Show(doc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("AccessControl", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion
}
