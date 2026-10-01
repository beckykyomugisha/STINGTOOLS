// AccProjectSettingsFile.cs — where the ACC operating settings live, and how they change.
//
// One home for the path and the writer, so the three callers (the clash pull, the publish
// suitability, and the BIM Coordination Center card that edits them) cannot disagree about
// which file they mean.
//
// PROJECT-SCOPED ON PURPOSE. A coordination model-set id belongs to a model, not to a
// coordinator: the same person working on KUT and another job needs a different one per
// project. Credentials stay machine-scoped in %APPDATA%\Planscape\acc_credentials.json.
// The path is resolved through StingPaths, never built by hand -
// "Never build a project path by hand. Resolve it through Core/StingPaths.cs".
//
// WRITES ONLY ON AN EXPLICIT CHOICE. Nothing here is called from a read path. Running a
// clash pull must not leave a settings file behind as a side effect - a file that appeared
// on its own is a configuration nobody decided.
//
// A MERGE, NEVER AN OVERWRITE. The BCC card writes one key; an escalation policy or a
// suitability code already in the file survives it. And a file that will not parse is
// REFUSED rather than replaced: overwriting it would destroy an Information Manager's work
// to fix a typo they can fix themselves.

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    internal static class AccProjectSettingsFile
    {
        /// <summary>The resolved path, or null when the document has never been saved (there
        /// is no project folder to resolve against). A null path loads as Absent, so an
        /// unsaved model prompts for everything - which is right.</summary>
        internal static string PathFor(Document doc)
        {
            try
            {
                string dir = StingPaths.MetaFile(doc, "_BIM_COORD", "acc");
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, AccOperatingPolicy.FileName);
            }
            catch (Exception ex) { StingLog.Warn("ACC settings path: " + ex.Message); return null; }
        }

        /// <summary>Load the policy for a document. Logs where the answers came from, so a
        /// run that prompted unexpectedly can be explained from the log rather than guessed at.</summary>
        internal static AccOperatingPolicy LoadFor(Document doc, string commandName)
        {
            var policy = AccOperatingPolicy.Load(PathFor(doc));
            if (policy.Source == AccPolicySource.Malformed)
                StingLog.Warn($"{commandName}: {policy.DescribeSource()}");
            else
                StingLog.Info($"{commandName}: {policy.DescribeSource()}");
            return policy;
        }

        /// <summary>IM-18: the machine credentials with THIS project's ACC container ids laid
        /// over them. Logs where the ids came from, and a deprecation warning when they came
        /// from the machine file, so a pull against the wrong container can be explained.</summary>
        internal static AccCredentials LoadCredentials(Document doc, string commandName)
        {
            var creds = AccIssueSync.LoadCredentials();
            AccOperatingPolicy policy = null;
            try { policy = AccOperatingPolicy.Load(PathFor(doc)); }
            catch (Exception ex) { StingLog.Warn($"{commandName}: ACC settings: {ex.Message}"); }
            AccProjectScope.Apply(creds, policy);
            string line = $"{commandName}: {AccProjectScope.Describe(creds)}";
            if (creds.ProjectScope == AccProjectScopeSource.ProjectSettings) StingLog.Info(line);
            else StingLog.Warn(line);
            return creds;
        }

        /// <summary>
        /// The one outcome for "ACC is not usable for this model" (A4). A MALFORMED settings file
        /// is named as the cause, with its load error, instead of "credentials not configured" -
        /// which pointed at the wrong file. Returns Failed when the file is malformed or the
        /// project runs unattended (a workflow step must not read a broken configuration as a
        /// skip); Cancelled only for an interactive project that simply has not been set up.
        /// </summary>
        internal static Result NotConfigured(AccOperatingPolicy policy, AccCredentials creds, string title, string nothingDone,
            bool allowPrompt = true)
        {
            string msg;
            if (policy != null && policy.Source == AccPolicySource.Malformed)
                msg = $"This project's ACC settings file could not be read, so ACC was not used. {nothingDone}\n\n" +
                      $"File: {policy.SettingsPath}\nProblem: {policy.LoadError}\n\n" +
                      "Every setting in it was discarded (never half-applied). Fix the file, or re-save it from " +
                      "BIM Coordination Center > ACC.";
            else
                msg = $"ACC is not set up for this project on this machine. {nothingDone}\n\n" +
                      "BIM Coordination Center > ACC: enter the APS Client ID, 'Sign in with Autodesk', then " +
                      "'Find my ACC project'.\n\n" + AccProjectScope.Describe(creds) + ".";
            // allowPrompt=false: a run with nobody watching (e.g. the webhook auto-import) logs,
            // whatever the project's prompt setting.
            if (allowPrompt) AccPullClashesCommand.Report(policy, title, msg);
            else StingLog.Info($"{title}: {msg}");
            StingLog.Warn($"{title}: not configured - " + (policy?.Source == AccPolicySource.Malformed ? policy.LoadError : AccProjectScope.Describe(creds)));
            return policy != null && (policy.Source == AccPolicySource.Malformed || policy.IsUnattended || !allowPrompt)
                ? Result.Failed : Result.Cancelled;
        }

        /// <summary>IM-18: record this project's ACC container ids in its own settings file.
        /// Empty strings clear them (back to the deprecated machine-file fallback).</summary>
        internal static bool SaveProjectScope(Document doc, string projectId, string coordContainerId, out string error)
            => SaveKeys(doc, out error,
                ("projectId", projectId ?? string.Empty),
                ("coordContainerId", coordContainerId ?? string.Empty));

        /// <summary>Record the project's ACC location as discovery found it: the project id AND
        /// its hub (needed to resolve top folders) and, when known, the hosting region.</summary>
        internal static bool SaveDiscoveredProject(Document doc, string projectId, string hubId, string region, out string error)
            => SaveDiscoveredProject(doc, projectId, hubId, region, out error, out _);

        /// <summary>P5: switching to a DIFFERENT ACC project also clears the previous project's
        /// container, model set, issue type, folders and review workflow
        /// (<see cref="AccOperatingPolicy.ProjectChangeEdits"/>); <paramref name="cleared"/> names them.</summary>
        internal static bool SaveDiscoveredProject(Document doc, string projectId, string hubId, string region,
            out string error, out List<string> cleared)
        {
            cleared = new List<string>();
            JObject existing = null;
            string path = PathFor(doc);
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text)) existing = JObject.Parse(text);
                }
            }
            catch (Exception ex)
            {
                // SaveTokens refuses to overwrite an unparsable file and says so; nothing else to do here.
                StingLog.Warn("ACC settings: could not read the current file before a project switch: " + ex.Message);
            }
            var edits = AccOperatingPolicy.ProjectChangeEdits(existing, projectId);
            var pairs = new List<(string, JToken)>
            {
                ("projectId", string.IsNullOrEmpty(projectId) ? JValue.CreateNull() : new JValue(projectId)),
                ("hubId", string.IsNullOrEmpty(hubId) ? JValue.CreateNull() : new JValue(hubId)),
                ("region", string.IsNullOrEmpty(AccIds.NormaliseRegion(region)) ? JValue.CreateNull() : new JValue(AccIds.NormaliseRegion(region))),
            };
            foreach (var kv in edits) { pairs.Add((kv.Key, kv.Value)); cleared.Add(kv.Key); }
            return SaveTokens(doc, out error, pairs.ToArray());
        }

        /// <summary>Record the project's default upload folder (empty clears it).</summary>
        internal static bool SaveFolderUrn(Document doc, string folderUrn, out string error)
            => SaveKeys(doc, out error, ("folderUrn", folderUrn ?? string.Empty));

        /// <summary>Record the issue type/subtype STING files issues under (empty clears both).</summary>
        internal static bool SaveIssueType(Document doc, string typeId, string subtypeId, out string error)
            => SaveKeys(doc, out error, ("issueTypeId", typeId ?? string.Empty), ("issueSubtypeId", subtypeId ?? string.Empty));

        /// <summary>Remember a coordination model set for this project. Merges into whatever
        /// else the file holds.</summary>
        internal static bool SaveCoordModelSet(Document doc, string setId, string setName, out string error)
            => SaveKeys(doc, out error,
                ("coordModelSetId", setId ?? string.Empty),
                ("coordModelSetName", setName ?? string.Empty));

        /// <summary>Forget the remembered coordination model set - back to prompting.</summary>
        internal static bool ClearCoordModelSet(Document doc, out string error)
            => SaveKeys(doc, out error, ("coordModelSetId", ""), ("coordModelSetName", ""));

        /// <summary>Set the suitability an unattended publish uses. Empty string clears it.</summary>
        internal static bool SavePublishSuitability(Document doc, string suitability, out string error)
            => SaveKeys(doc, out error, ("publishSuitability", suitability ?? string.Empty));

        /// <summary>Turn unattended mode on or off for this project.</summary>
        internal static bool SaveUnattended(Document doc, bool unattended, out string error)
            => SaveTokens(doc, out error, ("unattended", (JToken)unattended));

        /// <summary>Set (or clear, with nulls) the escalation policy. Both or neither -
        /// AccOperatingPolicy refuses half a policy, so writing half of one would produce a
        /// file that reads as "escalation off" for a reason the writer did not intend.</summary>
        internal static bool SaveEscalation(Document doc, int? maxCount, double? minScore, out string error)
        {
            if ((maxCount == null) != (minScore == null))
            {
                error = "an escalation policy needs BOTH a maximum count and a minimum score, " +
                        "or neither: a count alone escalates trivia, a score alone escalates hundreds.";
                return false;
            }
            return SaveTokens(doc, out error,
                ("escalateMaxCount", maxCount == null ? JValue.CreateNull() : new JValue(maxCount.Value)),
                ("escalateMinScore", minScore == null ? JValue.CreateNull() : new JValue(minScore.Value)));
        }

        private static bool SaveKeys(Document doc, out string error, params (string key, string value)[] pairs)
        {
            var tokens = new (string, JToken)[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
                tokens[i] = (pairs[i].key, string.IsNullOrEmpty(pairs[i].value)
                    ? (JToken)JValue.CreateNull()
                    : new JValue(pairs[i].value));
            return SaveTokens(doc, out error, tokens);
        }

        /// <summary>The one writer. A null token REMOVES its key, so "clear this setting"
        /// leaves no empty string behind that a future reader has to interpret.</summary>
        private static bool SaveTokens(Document doc, out string error, params (string key, JToken value)[] pairs)
        {
            error = null;
            string path = PathFor(doc);
            if (string.IsNullOrEmpty(path))
            {
                error = "this model has not been saved, so there is no project folder to write settings into.";
                return false;
            }

            JObject o;
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(text)) o = new JObject();
                    else
                    {
                        // Refuse rather than replace. See the header.
                        try { o = JObject.Parse(text); }
                        catch (Exception ex)
                        {
                            error = $"the existing settings file could not be parsed, so it was NOT overwritten " +
                                    $"({ex.Message}). Fix or delete {path} and try again.";
                            return false;
                        }
                    }
                }
                else o = new JObject();

                if (o["_comment"] == null)
                    o["_comment"] = "ACC operating settings for this project. Absent or unreadable means " +
                                    "every ACC command prompts, which is the safe default. Credentials are " +
                                    "separate and machine-scoped.";

                foreach (var (key, value) in pairs)
                {
                    if (value == null || value.Type == JTokenType.Null) o.Remove(key);
                    else o[key] = value;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(o, Formatting.Indented));
                StingLog.Info("ACC settings written: " + path);
                return true;
            }
            catch (Exception ex)
            {
                error = "the settings file could not be written: " + ex.Message;
                StingLog.Warn("ACC settings write: " + ex.Message);
                return false;
            }
        }
    }
}
