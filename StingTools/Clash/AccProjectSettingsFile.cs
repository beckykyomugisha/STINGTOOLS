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
using System.IO;
using Autodesk.Revit.DB;
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
