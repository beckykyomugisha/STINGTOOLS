// StingTools — SwapParameterBridge (Item 2).
//
// Makes Element.ChangeTypeId swaps non-destructive even when the
// destination (manufacturer / legacy) family was authored WITHOUT STING
// shared parameters. Implements the design §4.3 bridge:
//
//   1. ENSURE-STAMP (once per dest family): if the family lacks the STING
//      shared params the source instances carry, inject them by GUID via
//      FamilyParamEngine.InjectSharedParams (additive only — geometry,
//      existing types and native params untouched) and reload.
//   2. SNAPSHOT (per instance, before ChangeTypeId): capture { guid → value }
//      for every STING shared param present on the instance, plus
//      { guid → value } for any native/legacy param named in the alias map.
//   3. (caller runs ChangeTypeId)
//   4. RESTORE (per instance, after ChangeTypeId): write the STING values
//      back (the params now exist on the dest), and write each aliased
//      native value into its mapped STING GUID ONLY when that GUID is empty
//      (never clobber a real value).
//
// Reversible + additive — never deletes a parameter or value. Default-on,
// toggleable via Enabled. Model-modifying — verify in Revit before merge.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Commands.Symbols
{
    internal static class SwapParameterBridge
    {
        /// <summary>Bridge on/off. Default true; set false for legacy swap behaviour.</summary>
        public static bool Enabled = true;

        // ── STING GUID set ──────────────────────────────────────────────

        /// <summary>The set of STING shared-parameter GUIDs (from ParamRegistry).</summary>
        public static HashSet<Guid> StingGuidSet()
        {
            var set = new HashSet<Guid>();
            try
            {
                foreach (var kv in ParamRegistry.AllParamGuids)
                    if (kv.Value != Guid.Empty) set.Add(kv.Value);
            }
            catch (Exception ex) { StingLog.Warn($"SwapParameterBridge.StingGuidSet: {ex.Message}"); }
            return set;
        }

        // ── Alias map ───────────────────────────────────────────────────

        /// <summary>
        /// native/legacy parameter NAME → STING shared-parameter GUID. Loaded
        /// from STING_PARAM_ALIAS_MAP.json (corporate) merged with
        /// &lt;project&gt;/_BIM_COORD/param_alias_map.json (project wins).
        /// Names that don't resolve to a STING GUID are dropped.
        /// </summary>
        public static Dictionary<string, Guid> LoadAliasMap(Document doc)
        {
            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string corp = StingToolsApp.FindDataFile("STING_PARAM_ALIAS_MAP.json");
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp)) ParseAliases(File.ReadAllText(corp), raw);
            }
            catch (Exception ex) { StingLog.Warn($"SwapParameterBridge alias corporate: {ex.Message}"); }
            try
            {
                string baseDir = null;
                try { if (!string.IsNullOrEmpty(doc?.PathName)) baseDir = Path.GetDirectoryName(doc.PathName); } catch { }
                if (!string.IsNullOrEmpty(baseDir))
                {
                    string ovr = Path.Combine(baseDir, "_BIM_COORD", "param_alias_map.json");
                    if (File.Exists(ovr)) ParseAliases(File.ReadAllText(ovr), raw);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SwapParameterBridge alias override: {ex.Message}"); }

            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
            {
                Guid g = ParamRegistry.GetGuid(kv.Value);
                if (g != Guid.Empty && !map.ContainsKey(kv.Key)) map[kv.Key] = g;
            }
            return map;
        }

        private static void ParseAliases(string json, Dictionary<string, string> into)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            JToken root;
            try { root = JToken.Parse(json); } catch (Exception ex) { StingLog.Warn($"alias parse: {ex.Message}"); return; }
            var obj = (root["aliases"] as JObject) ?? (root as JObject);
            if (obj == null) return;
            foreach (var prop in obj.Properties())
            {
                if (prop.Name.StartsWith("_") || prop.Name == "version"
                    || prop.Name == "description" || prop.Name == "aliases") continue;
                string target = prop.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(target)) into[prop.Name] = target;
            }
        }

        // ── Value snapshot ──────────────────────────────────────────────

        private struct ParamVal
        {
            public StorageType St;
            public string S;
            public int I;
            public double D;
            public ElementId E;
            public string ValueStr; // AsValueString — for cross-storage coercion
        }

        public sealed class Snapshot
        {
            internal readonly Dictionary<Guid, ParamVal> Sting = new Dictionary<Guid, ParamVal>();
            internal readonly Dictionary<Guid, ParamVal> Alias = new Dictionary<Guid, ParamVal>();
            public bool IsEmpty => Sting.Count == 0 && Alias.Count == 0;
        }

        /// <summary>Capture STING + aliased native values from an instance before the swap.</summary>
        public static Snapshot Capture(Element el, HashSet<Guid> stingGuids, Dictionary<string, Guid> aliasMap)
        {
            var snap = new Snapshot();
            if (el == null) return snap;

            // STING shared params present on the instance.
            try
            {
                foreach (Parameter p in el.Parameters)
                {
                    if (p == null || !p.IsShared) continue;
                    Guid g;
                    try { g = p.GUID; } catch { continue; }
                    if (g == Guid.Empty || !stingGuids.Contains(g)) continue;
                    if (!p.HasValue) continue;
                    if (!snap.Sting.ContainsKey(g)) snap.Sting[g] = Cap(p);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SwapParameterBridge.Capture STING {el.Id}: {ex.Message}"); }

            // Aliased native params.
            if (aliasMap != null && aliasMap.Count > 0)
            {
                foreach (var kv in aliasMap)
                {
                    try
                    {
                        var np = el.LookupParameter(kv.Key);
                        if (np == null || !np.HasValue) continue;
                        if (IsEffectivelyEmpty(np)) continue;
                        if (!snap.Alias.ContainsKey(kv.Value)) snap.Alias[kv.Value] = Cap(np);
                    }
                    catch (Exception ex) { StingLog.Warn($"SwapParameterBridge.Capture alias '{kv.Key}': {ex.Message}"); }
                }
            }
            return snap;
        }

        /// <summary>Restore STING values (always) + aliased values (only when target empty) after the swap.</summary>
        public static void Restore(Element el, Snapshot snap)
        {
            if (el == null || snap == null || snap.IsEmpty) return;

            foreach (var kv in snap.Sting)
            {
                try
                {
                    var p = el.get_Parameter(kv.Key);
                    if (p == null || p.IsReadOnly) continue;
                    SetInto(p, kv.Value);
                }
                catch (Exception ex) { StingLog.Warn($"SwapParameterBridge.Restore STING {el.Id}: {ex.Message}"); }
            }

            foreach (var kv in snap.Alias)
            {
                try
                {
                    var p = el.get_Parameter(kv.Key);
                    if (p == null || p.IsReadOnly) continue;
                    // Don't clobber a real value a STING param already carries
                    // (e.g. the seed's own MNT_HGT_MM that survived the swap).
                    if (!IsEffectivelyEmpty(p)) continue;
                    SetInto(p, kv.Value);
                }
                catch (Exception ex) { StingLog.Warn($"SwapParameterBridge.Restore alias {el.Id}: {ex.Message}"); }
            }
        }

        private static ParamVal Cap(Parameter p)
        {
            var v = new ParamVal { St = p.StorageType };
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:    v.S = p.AsString() ?? ""; break;
                    case StorageType.Integer:   v.I = p.AsInteger(); break;
                    case StorageType.Double:    v.D = p.AsDouble(); break;
                    case StorageType.ElementId: v.E = p.AsElementId(); break;
                }
            }
            catch { }
            try { v.ValueStr = p.AsValueString(); } catch { }
            return v;
        }

        private static bool SetInto(Parameter target, ParamVal v)
        {
            if (target == null || target.IsReadOnly) return false;
            try
            {
                switch (target.StorageType)
                {
                    case StorageType.String:
                        string s = v.St == StorageType.String
                            ? v.S
                            : (!string.IsNullOrEmpty(v.ValueStr) ? v.ValueStr : NumToString(v));
                        return target.Set(s ?? "");
                    case StorageType.Integer:
                        int iv = v.St == StorageType.Integer ? v.I
                               : v.St == StorageType.Double ? (int)Math.Round(v.D) : 0;
                        return target.Set(iv);
                    case StorageType.Double:
                        double dv = v.St == StorageType.Double ? v.D
                                  : v.St == StorageType.Integer ? v.I : 0.0;
                        return target.Set(dv);
                    case StorageType.ElementId:
                        return (v.St == StorageType.ElementId && v.E != null) && target.Set(v.E);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SwapParameterBridge.SetInto: {ex.Message}"); }
            return false;
        }

        private static string NumToString(ParamVal v)
        {
            switch (v.St)
            {
                case StorageType.Integer: return v.I.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.Double:  return v.D.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                default: return v.S ?? "";
            }
        }

        private static bool IsEffectivelyEmpty(Parameter p)
        {
            try
            {
                if (p == null || !p.HasValue) return true;
                switch (p.StorageType)
                {
                    case StorageType.String:    return string.IsNullOrWhiteSpace(p.AsString());
                    case StorageType.ElementId: return p.AsElementId() == null || p.AsElementId() == ElementId.InvalidElementId;
                    // numeric 0 is treated as "set" — a real 0 mounting height is rare
                    // but legitimate; only an unset value short-circuits the alias copy.
                    default: return false;
                }
            }
            catch { return true; }
        }

        // ── Ensure-stamp ────────────────────────────────────────────────

        /// <summary>
        /// For every distinct destination family in the plans, inject the STING
        /// shared params the source instances carry (by name → GUID) when the
        /// family lacks them. Runs OUTSIDE any transaction (EditFamily +
        /// reload). Returns the number of families that were stamped.
        /// </summary>
        public static int EnsureStampFamilies(Document doc, IEnumerable<SwapPlan> plans, HashSet<Guid> stingGuids)
        {
            if (doc == null || plans == null) return 0;
            var app = doc.Application;

            // Map: destFamilyId → set of STING param names the sources carry.
            var familyToNames = new Dictionary<ElementId, HashSet<string>>();
            var familyById    = new Dictionary<ElementId, Family>();

            foreach (var p in plans)
            {
                var winner = p?.Candidates?.FirstOrDefault();
                if (winner?.ResolvedTypeId == null || winner.ResolvedTypeId == ElementId.InvalidElementId) continue;
                Family fam = null;
                try { fam = (doc.GetElement(winner.ResolvedTypeId) as FamilySymbol)?.Family; } catch { }
                if (fam == null) continue;

                if (!familyToNames.TryGetValue(fam.Id, out var names))
                {
                    names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    familyToNames[fam.Id] = names;
                    familyById[fam.Id] = fam;
                }

                // STING param names present (with value) on this plan's sources.
                foreach (var id in p.InstanceIds)
                {
                    Element el = null;
                    try { el = doc.GetElement(id); } catch { }
                    if (el == null) continue;
                    foreach (Parameter prm in el.Parameters)
                    {
                        if (prm == null || !prm.IsShared || !prm.HasValue) continue;
                        Guid g; try { g = prm.GUID; } catch { continue; }
                        if (g == Guid.Empty || !stingGuids.Contains(g)) continue;
                        string nm = ParamRegistry.GetParamName(g);
                        if (!string.IsNullOrEmpty(nm)) names.Add(nm);
                    }
                }
            }

            int stamped = 0;
            foreach (var kv in familyToNames)
            {
                var fam = familyById[kv.Key];
                var wantNames = kv.Value;
                if (wantNames.Count == 0) continue;
                Document famDoc = null;
                try
                {
                    famDoc = doc.EditFamily(fam);
                    if (famDoc == null) continue;

                    var existing = new HashSet<string>(
                        famDoc.FamilyManager.GetParameters().Select(fp => fp.Definition.Name),
                        StringComparer.OrdinalIgnoreCase);
                    var missing = wantNames.Where(n => !existing.Contains(n)).ToList();
                    if (missing.Count == 0) { try { famDoc.Close(false); } catch { } continue; }

                    bool ok = false;
                    using (var tx = new Transaction(famDoc, "STING Stamp Swap Params"))
                    {
                        tx.Start();
                        try
                        {
                            FamilyParamEngine.InjectSharedParams(famDoc, app, missing);
                            tx.Commit();
                            ok = true;
                        }
                        catch (Exception ex)
                        {
                            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                            StingLog.Warn($"EnsureStamp tx '{fam.Name}': {ex.Message}");
                        }
                    }

                    if (ok)
                    {
                        try
                        {
                            if (doc.LoadFamily(famDoc.PathName, new BridgeReloadOptions(), out _)) stamped++;
                            else StingLog.Warn($"EnsureStamp: reload returned false for '{fam.Name}'.");
                        }
                        catch (Exception ex) { StingLog.Warn($"EnsureStamp reload '{fam.Name}': {ex.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"EnsureStamp '{fam?.Name}': {ex.Message}"); }
                finally { try { famDoc?.Close(false); } catch { } }
            }
            return stamped;
        }

        private sealed class BridgeReloadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = false; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = false; return true; }
        }
    }
}
