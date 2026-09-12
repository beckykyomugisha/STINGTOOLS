// ═════════════════════════════════════════════════════════════════════════════
//  WorkflowPresetOverride.cs — a project's own workflows, layered over the
//  corporate ones.
//
//  WHY. Workflows were the ONLY layered config in this codebase without a
//  project override. Drawing types, view style packs, PROD rules, PROD
//  exclusions, climate data, MEP sizing rules and material overrides all read
//  <project>/_BIM_COORD/… on top of a corporate baseline; presets read
//  StingToolsApp.DataPath and nothing else. Two things followed:
//
//    * A project could not tailor its own kickoff. Every project got the
//      corporate 26 steps, in the corporate order, or nothing.
//    * Workflow_CreatePreset wrote into the DEPLOYED data/ folder. That file
//      does survive a deploy — extract_plugin.sh copies with `cp -rf`, an
//      overlay rather than a wipe — but it is global to every project, and it is
//      lost the moment the deploy target moves to another worktree, which
//      CLAUDE.md records happening five times in two days.
//
//  ── THE RULES ───────────────────────────────────────────────────────────────
//  A project preset REPLACES the corporate one of the same name, whole. Not
//  merged step-by-step: a half-corporate half-project 26-step chain is a
//  sequence nobody wrote and nobody can read, and the failure mode of this
//  codebase is precisely a thing that looks authored and is not. Same rule
//  DrawingTypeRegistry uses — project entries win by id.
//
//  A project preset with a NEW name is added.
//
//  A STEP WHOSE TAG DOES NOT RESOLVE IS REPORTED, NOT DROPPED. Dropping it
//  silently would let a project ship a 12-step workflow that runs 11 and says
//  nothing — the same shape as a step keyed "tag" instead of "commandTag", which
//  Tier 1 of the wiring gate exists to catch. The preset still loads, because a
//  typo in step 7 is not a reason to withhold steps 1-6 from a user who can see
//  the note.
//
//  A preset with no steps is rejected outright: an empty override that replaced
//  a working corporate preset would remove a workflow and look like a rename.
//
//  Revit-free: it decides which preset wins and what to say about it. The Revit
//  half only reads files from a path StingPaths resolves.
// ═════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>One preset file found under a project's workflows folder.</summary>
    public sealed class ProjectPresetFile
    {
        /// <summary>File name only, for the note. Never a full path: these notes are
        /// shown to users and read into logs.</summary>
        public string FileName = "";
        /// <summary>Deserialised preset, or null when the file would not parse.</summary>
        public WorkflowPreset Preset;
        /// <summary>Why it would not parse, when <see cref="Preset"/> is null.</summary>
        public string ParseError = "";
    }

    public sealed class PresetOverrideResult
    {
        /// <summary>The list a caller should use: corporate, with project entries
        /// replacing same-named ones and new ones appended.</summary>
        public List<WorkflowPreset> Presets = new List<WorkflowPreset>();

        /// <summary>Everything worth saying, in the order it was found. Never empty
        /// when something was replaced, added or refused.</summary>
        public List<string> Notes = new List<string>();

        public int Replaced;
        public int Added;
        public int Refused;
        /// <summary>Steps in accepted project presets whose tag resolves to nothing.</summary>
        public int UnresolvableSteps;

        public bool ProjectHasSomethingToSay =>
            Replaced > 0 || Added > 0 || Refused > 0 || UnresolvableSteps > 0;
    }

    public static class WorkflowPresetOverride
    {
        /// <summary>
        /// Layer a project's presets over the corporate ones.
        /// </summary>
        /// <param name="corporate">The built-in and deployed-data presets, in order.</param>
        /// <param name="project">Files found under the project's workflows folder.</param>
        /// <param name="tagResolves">Whether a command tag has a ResolveCommand case.
        /// Null means "cannot be checked here" and suppresses only that check — it must
        /// not silently turn every step into a finding.</param>
        public static PresetOverrideResult Merge(
            IEnumerable<WorkflowPreset> corporate,
            IEnumerable<ProjectPresetFile> project,
            Func<string, bool> tagResolves)
        {
            var res = new PresetOverrideResult();
            res.Presets.AddRange((corporate ?? Enumerable.Empty<WorkflowPreset>())
                                 .Where(p => p != null));

            foreach (var f in project ?? Enumerable.Empty<ProjectPresetFile>())
            {
                if (f == null) continue;
                string where = string.IsNullOrWhiteSpace(f.FileName) ? "(unnamed file)" : f.FileName;

                if (f.Preset == null)
                {
                    res.Refused++;
                    res.Notes.Add(where + " was not read: "
                                + (string.IsNullOrWhiteSpace(f.ParseError) ? "unreadable" : f.ParseError));
                    continue;
                }

                string name = (f.Preset.Name ?? "").Trim();
                if (name.Length == 0)
                {
                    res.Refused++;
                    res.Notes.Add(where + " has no \"name\" — a preset with no name cannot "
                                + "replace one and cannot be chosen from a list.");
                    continue;
                }

                if (f.Preset.Steps == null || f.Preset.Steps.Count == 0)
                {
                    res.Refused++;
                    res.Notes.Add(where + " (\"" + name + "\") has no steps. Refused rather "
                                + "than allowed to replace a working preset with nothing.");
                    continue;
                }

                // Report unresolvable tags BEFORE accepting, so the note is attached to
                // the file that introduced them.
                if (tagResolves != null)
                {
                    foreach (var st in f.Preset.Steps)
                    {
                        string tag = (st?.CommandTag ?? "").Trim();
                        if (tag.Length == 0)
                        {
                            res.UnresolvableSteps++;
                            res.Notes.Add(where + " (\"" + name + "\"): a step has no "
                                        + "commandTag — it will do nothing.");
                            continue;
                        }
                        bool ok;
                        try { ok = tagResolves(tag); }
                        catch (Exception) { ok = true; }   // a broken check must not
                                                           // manufacture findings
                        if (!ok)
                        {
                            res.UnresolvableSteps++;
                            res.Notes.Add(where + " (\"" + name + "\"): command tag '" + tag
                                        + "' resolves to nothing — that step will fail. The "
                                        + "preset is still loaded; the step is not silently "
                                        + "removed.");
                        }
                    }
                }

                f.Preset.IsBuiltIn = false;

                int at = res.Presets.FindIndex(
                    p => string.Equals((p.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                {
                    int wasSteps = res.Presets[at].Steps?.Count ?? 0;
                    res.Presets[at] = f.Preset;
                    res.Replaced++;
                    res.Notes.Add("\"" + name + "\" comes from this project (" + where + "): "
                                + f.Preset.Steps.Count + " step(s), replacing the corporate "
                                + wasSteps + ".");
                }
                else
                {
                    res.Presets.Add(f.Preset);
                    res.Added++;
                    res.Notes.Add("\"" + name + "\" is a project workflow (" + where + "): "
                                + f.Preset.Steps.Count + " step(s).");
                }
            }

            return res;
        }

        /// <summary>One line for a log or a dialog. Empty when the project said nothing,
        /// so a caller can test it rather than printing "0 replaced, 0 added".</summary>
        public static string Summary(PresetOverrideResult r)
        {
            if (r == null || !r.ProjectHasSomethingToSay) return "";
            var bits = new List<string>();
            if (r.Replaced > 0) bits.Add(r.Replaced + " replaced");
            if (r.Added > 0) bits.Add(r.Added + " added");
            if (r.Refused > 0) bits.Add(r.Refused + " refused");
            if (r.UnresolvableSteps > 0) bits.Add(r.UnresolvableSteps + " step(s) resolve to nothing");
            return "Project workflows: " + string.Join(", ", bits) + ".";
        }
    }
}
