using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// The canonical project-member roster, for any dialog that needs to pick a person.
    ///
    /// The BIM Coordination Center already resolves this correctly — its Meetings
    /// tab seeds its attendee picker from <c>_data.TeamMembers</c>, which
    /// <c>WarningsManager.BuildCoordData</c> fills from
    /// <c>PlanscapeServerClient.GetProjectMembersAsync</c>. The problem was that
    /// every *other* dialog that needs a person either invented its own list of
    /// generic job titles ("BIM Coordinator", "Design Lead") or read a separate
    /// hand-edited JSON file, so the names on offer had no relationship to who is
    /// actually on the project — and none of them carried the server user id that
    /// an invite or an assignment actually needs.
    ///
    /// This is that same resolve-then-fallback logic, extracted so those dialogs
    /// share one source rather than growing a third and fourth copy of it.
    ///
    /// Resolution order, matching BuildCoordData:
    ///   1. the live server roster, when connected and the model is linked;
    ///   2. the deprecated per-model <c>team_members.json</c>, so a disconnected
    ///      user still sees the last known roster rather than an empty list;
    ///   3. empty — callers must cope, and must say so rather than silently
    ///      falling back to invented names.
    /// </summary>
    internal static class ProjectRoster
    {
        /// <summary>One person on the project. <see cref="ServerUserId"/> is the
        /// part that matters: it is what an assignment or invite is keyed on, and
        /// its absence is what made the old free-text pickers cosmetic.</summary>
        internal sealed class RosterMember
        {
            public string Name { get; set; } = "";
            public string Email { get; set; } = "";
            public string Company { get; set; } = "";
            public string Role { get; set; } = "";
            public string Discipline { get; set; } = "";
            public Guid? ServerUserId { get; set; }
            public Guid? ServerMemberId { get; set; }

            /// <summary>True when this row came from the server and can therefore
            /// be used for a real assignment/invite, rather than being a local
            /// leftover.</summary>
            public bool IsServerBacked => ServerUserId.HasValue && ServerUserId.Value != Guid.Empty;

            public string Display => string.IsNullOrWhiteSpace(Name) ? (Email ?? "Member") : Name;
        }

        // Short cache so a dialog that asks several times while building its UI
        // does not issue several HTTP calls. Deliberately brief — a roster edited
        // in the Access tab should show up in the next dialog opened, not after a
        // Revit restart.
        private static readonly object _lock = new object();
        private static List<RosterMember> _cache;
        private static string _cacheKey;
        private static DateTime _cachedAt;
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

        /// <summary>Drop the cache — call after the roster is known to have changed.</summary>
        internal static void Invalidate()
        {
            lock (_lock) { _cache = null; _cacheKey = null; }
        }

        /// <summary>
        /// The project roster, server-first. Never throws and never returns null;
        /// an empty list means "we genuinely do not know who is on this project",
        /// which callers must present as such.
        /// </summary>
        internal static List<RosterMember> Load(Document doc)
        {
            string key = doc?.PathName ?? "";
            lock (_lock)
            {
                if (_cache != null && _cacheKey == key && DateTime.UtcNow - _cachedAt < CacheFor)
                    return _cache;
            }

            var members = new List<RosterMember>();
            try
            {
                var client = BIMManager.PlanscapeServerClient.Instance;

                // Resolve the linked server project: live CurrentProjectId first,
                // else the per-model link file (same order as BuildCoordData).
                Guid pid = client.CurrentProjectId;
                if (pid == Guid.Empty && doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    try
                    {
                        var link = BIMManager.PlanscapeProjectLink.Load(
                            BIMManager.PlanscapeProjectLink.ConfigPathFor(doc));
                        if (link.IsLinked) { pid = link.ProjectId; client.CurrentProjectId = pid; }
                    }
                    catch (Exception lex)
                    {
                        StingLog.Warn($"ProjectRoster: project-link resolve failed: {lex.Message}");
                    }
                }

                if (client.IsConnected && pid != Guid.Empty)
                {
                    // Sync-over-async off the UI thread so opening a dialog on the
                    // Revit UI thread cannot deadlock.
                    var dtos = System.Threading.Tasks.Task
                        .Run(() => client.GetProjectMembersAsync(pid))
                        .GetAwaiter().GetResult();
                    if (dtos != null && dtos.Count > 0)
                    {
                        members = dtos.Select(m => new RosterMember
                        {
                            Name = m.DisplayName ?? m.Email ?? "Member",
                            Email = m.Email ?? "",
                            Role = string.IsNullOrWhiteSpace(m.ProjectRole) ? m.Iso19650Role : m.ProjectRole,
                            ServerUserId = m.UserId,
                            ServerMemberId = m.Id,
                        }).ToList();
                        StingLog.Info($"ProjectRoster: {members.Count} member(s) from server (project {pid}).");
                    }
                }

                // DEPRECATED offline fallback — the per-model team_members.json.
                // Only when the server is unreachable or the model is unlinked.
                if (members.Count == 0 && doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    try
                    {
                        string path = BIMManager.BIMManagerEngine.GetBIMManagerFilePath(doc, "team_members.json");
                        if (File.Exists(path))
                        {
                            var arr = JArray.Parse(File.ReadAllText(path));
                            foreach (var t in arr)
                            {
                                string name = (string)t["Name"] ?? (string)t["name"] ?? "";
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                members.Add(new RosterMember
                                {
                                    Name = name,
                                    Email = (string)t["Email"] ?? (string)t["email"] ?? "",
                                    Company = (string)t["Company"] ?? (string)t["company"] ?? "",
                                    Role = (string)t["Role"] ?? (string)t["role"] ?? "",
                                    Discipline = (string)t["Discipline"] ?? (string)t["discipline"] ?? "",
                                    // Deliberately no ServerUserId — these rows cannot
                                    // drive a real invite, and pretending otherwise is
                                    // how the old pickers misled people.
                                });
                            }
                            if (members.Count > 0)
                                StingLog.Info("ProjectRoster: server unreachable/unlinked — used team_members.json fallback.");
                        }
                    }
                    catch (Exception fex)
                    {
                        StingLog.Warn($"ProjectRoster: team_members.json fallback failed: {fex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectRoster: load failed: {ex.Message}");
            }

            members = members
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_lock)
            {
                _cache = members;
                _cacheKey = key;
                _cachedAt = DateTime.UtcNow;
            }
            return members;
        }

        /// <summary>
        /// The roster for an explicitly-known server project, for callers that
        /// hold a project id but no Document (the site-photos admin tab). Server
        /// only — there is no per-model JSON to fall back to without a Document.
        /// </summary>
        internal static List<RosterMember> LoadForProject(Guid projectId)
        {
            var members = new List<RosterMember>();
            if (projectId == Guid.Empty) return members;
            try
            {
                var client = BIMManager.PlanscapeServerClient.Instance;
                if (!client.IsConnected) return members;

                var dtos = System.Threading.Tasks.Task
                    .Run(() => client.GetProjectMembersAsync(projectId))
                    .GetAwaiter().GetResult();
                if (dtos != null)
                {
                    members = dtos.Select(m => new RosterMember
                    {
                        Name = m.DisplayName ?? m.Email ?? "Member",
                        Email = m.Email ?? "",
                        Role = string.IsNullOrWhiteSpace(m.ProjectRole) ? m.Iso19650Role : m.ProjectRole,
                        ServerUserId = m.UserId,
                        ServerMemberId = m.Id,
                    })
                    .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectRoster.LoadForProject({projectId}): {ex.Message}");
            }
            return members;
        }

        /// <summary>Display names only, for a plain combo box.</summary>
        internal static List<string> Names(Document doc) =>
            Load(doc).Select(m => m.Display).ToList();

        /// <summary>
        /// Resolve the linked Planscape project for this model — live
        /// CurrentProjectId first, then the per-model link file. Guid.Empty when
        /// the model is not linked to a server project.
        /// </summary>
        internal static Guid ResolveProjectId(Document doc)
        {
            try
            {
                var client = BIMManager.PlanscapeServerClient.Instance;
                Guid pid = client.CurrentProjectId;
                if (pid != Guid.Empty) return pid;
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return Guid.Empty;

                var link = BIMManager.PlanscapeProjectLink.Load(
                    BIMManager.PlanscapeProjectLink.ConfigPathFor(doc));
                if (link.IsLinked) { client.CurrentProjectId = link.ProjectId; return link.ProjectId; }
            }
            catch (Exception ex) { StingLog.Warn($"ProjectRoster.ResolveProjectId: {ex.Message}"); }
            return Guid.Empty;
        }

        /// <summary>Find a member by the display name a combo box hands back.</summary>
        internal static RosterMember Find(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return Load(doc).FirstOrDefault(m =>
                string.Equals(m.Display, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
