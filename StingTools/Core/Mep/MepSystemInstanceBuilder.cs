// StingTools — MEP System Instance Builder (Phase B).
//
// Walks the MEP connector graph (same traversal as MepSystemTracerCommand),
// groups elements into connected networks, and for each network:
//
//   1. If Revit has already formed it into a MechanicalSystem / PipingSystem
//      (the common case once equipment + ductwork are connected):
//        - assign the matching STING (Phase A) system TYPE via ChangeTypeId,
//        - give it a meaningful name  <abbreviation>-<NN>,
//        - stamp ParamRegistry.MEP_SYS_NAME on every member.
//      This is the reliable, high-value path — it makes the Phase A system
//      types + the 19 MEP colour filters actually get used.
//
//   2. If it is an ORPHAN network (no MEPSystem yet) but has a detectable
//      source-equipment connector, BEST-EFFORT create one via
//      doc.Create.NewMechanicalSystem / NewPipingSystem + MEPSystem.Add,
//      then type / name / stamp it. Networks that don't satisfy Revit's
//      "valid source + consistent flow direction + tree" requirement are
//      REPORTED, not forced — the members still get their STING params.
//
//   3. Final pass: MepCrossStampOrchestrator populates HVC_* / PLM_* / ELC_*
//      from the now-correct native flow/voltage parameters.
//
// CALLER OWNS THE ACTIVE TRANSACTION.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Mep
{
    public enum MepSystemBuildOutcome { Typed, Created, Stamped, Skipped, Failed }

    public sealed class MepSystemBuildRow
    {
        public string Network { get; set; } = "";
        public int Members { get; set; }
        public string Domain { get; set; } = "";
        public string Classification { get; set; } = "";
        public string SystemName { get; set; } = "";
        public MepSystemBuildOutcome Outcome { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class MepSystemBuildResult
    {
        public List<MepSystemBuildRow> Rows { get; } = new List<MepSystemBuildRow>();
        public List<string> Warnings { get; } = new List<string>();
        public int Networks { get; set; }
        public MepCrossStampResult CrossStamp { get; set; }

        public int Typed   => Rows.Count(r => r.Outcome == MepSystemBuildOutcome.Typed);
        public int Created => Rows.Count(r => r.Outcome == MepSystemBuildOutcome.Created);
        public int Stamped => Rows.Count(r => r.Outcome == MepSystemBuildOutcome.Stamped);
        public int Skipped => Rows.Count(r => r.Outcome == MepSystemBuildOutcome.Skipped);
        public int Failed  => Rows.Count(r => r.Outcome == MepSystemBuildOutcome.Failed);
    }

    public static class MepSystemInstanceBuilder
    {
        /// <summary>
        /// Build / type / name / stamp MEP systems for the connected networks
        /// reachable from <paramref name="seedIds"/> (or the whole project when
        /// seedIds is null/empty). Requires an open transaction.
        /// </summary>
        public static MepSystemBuildResult Build(
            Document doc, ICollection<ElementId> seedIds, bool attemptCreateOrphans)
        {
            var result = new MepSystemBuildResult();
            if (doc == null) { result.Warnings.Add("No document."); return result; }

            // Index STING (Phase A) system-type elements by classification.
            var rules = MepSystemTypeRegistry.Get(doc);
            var mechTypeByClass = BuildTypeIndex<MechanicalSystemType>(doc);
            var pipeTypeByClass = BuildTypeIndex<PipingSystemType>(doc);

            // Per-abbreviation name counter, primed from existing "<abbr>-NN" systems
            // so a second run continues the sequence rather than colliding.
            var nameSeq = SeedNameSeq(doc);

            // Seeds: explicit selection, else every duct + pipe in the project.
            var seeds = (seedIds != null && seedIds.Count > 0)
                ? seedIds.ToList()
                : new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(new[]
                    {
                        BuiltInCategory.OST_DuctCurves,
                        BuiltInCategory.OST_PipeCurves
                    }))
                    .WhereElementIsNotElementType()
                    .ToElementIds().ToList();

            var globalVisited = new HashSet<long>();
            foreach (var seed in seeds)
            {
                if (globalVisited.Contains(seed.Value)) continue;

                var network = new List<ElementId>();
                WalkConnectorGraph(doc, seed, globalVisited, network);
                if (network.Count == 0) continue;
                result.Networks++;

                try { ProcessNetwork(doc, network, rules, mechTypeByClass, pipeTypeByClass,
                                     nameSeq, attemptCreateOrphans, result); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Network @ {seed}: {ex.Message}");
                    StingLog.Warn($"MepSystemInstanceBuilder network {seed}: {ex.Message}");
                }
            }

            // HVC_/PLM_/ELC_ stamping from native flow params (reuses the
            // existing cross-stamp engine; runs in the same transaction).
            try { result.CrossStamp = MepCrossStampOrchestrator.AnalyseModel(doc); }
            catch (Exception ex) { result.Warnings.Add($"Cross-stamp: {ex.Message}"); }

            return result;
        }

        // ── per-network processing ──────────────────────────────────────────

        private static void ProcessNetwork(
            Document doc, List<ElementId> network, MepSystemTypeRules rules,
            Dictionary<MEPSystemClassification, ElementId> mechTypeByClass,
            Dictionary<MEPSystemClassification, ElementId> pipeTypeByClass,
            Dictionary<string, int> nameSeq, bool attemptCreateOrphans,
            MepSystemBuildResult result)
        {
            var els = network.Select(doc.GetElement).Where(e => e != null).ToList();

            // Domain: a connected network is single-domain (duct connectors don't
            // join pipe connectors). Decide by which MEPCurve kind is present.
            bool hasDuct = els.Any(e => e is Duct);
            bool hasPipe = els.Any(e => e is Pipe);
            if (!hasDuct && !hasPipe) return; // fittings-only fragment — skip silently

            string domain = hasDuct ? "duct" : "pipe";
            var row = new MepSystemBuildRow { Network = network[0].ToString(), Members = network.Count, Domain = domain };
            result.Rows.Add(row);

            // Existing MEPSystem? Any member MEPCurve that already belongs to one.
            // A fragmented network can carry more than one — retype/rename the first
            // (canonical) and warn so the user can merge the rest in the System Browser.
            var memberSystems = els.OfType<MEPCurve>()
                .Select(c => { try { return c.MEPSystem; } catch { return null; } })
                .Where(s => s != null)
                .GroupBy(s => s.Id).Select(g => g.First()).ToList();
            MEPSystem existing = memberSystems.FirstOrDefault();
            if (memberSystems.Count > 1)
                result.Warnings.Add($"{row.Network}: network spans {memberSystems.Count} MEP systems — " +
                                    "only the first was typed/named; merge the rest in the System Browser.");

            MEPSystem system = existing;
            bool created = false;

            if (system == null)
            {
                if (!attemptCreateOrphans)
                {
                    StampMembers(doc, els, null, null, row);
                    row.Outcome = MepSystemBuildOutcome.Stamped;
                    row.Note = "orphan network (no system) — members stamped; enable create to build a system";
                    return;
                }
                system = TryCreateSystem(doc, els, hasDuct, row, result.Warnings, out created);
                if (system == null)
                {
                    // Could not create — still stamp members so tags/params land.
                    StampMembers(doc, els, null, null, row);
                    row.Outcome = MepSystemBuildOutcome.Skipped;
                    if (string.IsNullOrEmpty(row.Note))
                        row.Note = "no valid source-equipment connector to anchor a system";
                    return;
                }
            }

            // Resolve classification + read whether the current type is already a
            // STING type (so we don't retype a correctly-assigned CHW system to LTHW
            // just because both are SupplyHydronic).
            MEPSystemClassification cls = MEPSystemClassification.UndefinedSystemClassification;
            var curTypeEl = doc.GetElement(system.GetTypeId()) as MEPSystemType;
            bool currentIsSting = (curTypeEl?.Name ?? "").StartsWith("STING", StringComparison.OrdinalIgnoreCase);
            try { if (curTypeEl != null) cls = curTypeEl.SystemClassification; }
            catch (Exception ex) { result.Warnings.Add($"{row.Network}: classification read: {ex.Message}"); }
            row.Classification = cls.ToString();

            // Assign the STING (Phase A) type for this classification — but only when
            // the current type isn't already a STING type (preserve service identity).
            var typeIndex = hasDuct ? mechTypeByClass : pipeTypeByClass;
            if (!currentIsSting && typeIndex.TryGetValue(cls, out var stingTypeId) && stingTypeId != system.GetTypeId())
            {
                try { system.ChangeTypeId(stingTypeId); currentIsSting = true; }
                catch (Exception ex) { result.Warnings.Add($"{row.Network}: ChangeTypeId: {ex.Message}"); }
            }

            // Abbreviation: from the system's (now-STING) type first, else the Phase A
            // def by classification, else a domain default.
            var def = rules.Enabled.FirstOrDefault(d =>
                string.Equals(d.Classification, cls.ToString(), StringComparison.OrdinalIgnoreCase) &&
                (hasDuct ? d.IsDuct : d.IsPipe));
            string abbr = null;
            try { abbr = (doc.GetElement(system.GetTypeId()) as MEPSystemType)?.Abbreviation; } catch { }
            if (string.IsNullOrWhiteSpace(abbr)) abbr = def?.Abbreviation;
            if (string.IsNullOrWhiteSpace(abbr)) abbr = hasDuct ? "DUCT" : "PIPE";

            // Idempotent naming: keep an existing "<abbr>-NN" name; otherwise mint the
            // next sequence number (the counter was primed from existing systems).
            string existingName = null; try { existingName = system.Name; } catch { }
            string sysName;
            if (!string.IsNullOrEmpty(existingName) &&
                Regex.IsMatch(existingName, $"^{Regex.Escape(abbr)}-\\d+$", RegexOptions.IgnoreCase))
            {
                sysName = existingName; // already STING-named — leave it
            }
            else
            {
                int seq = (nameSeq.TryGetValue(abbr, out var n) ? n : 0) + 1;
                nameSeq[abbr] = seq;
                sysName = $"{abbr}-{seq:D2}";
                TrySetSystemName(system, sysName, result.Warnings, row.Network);
            }
            row.SystemName = sysName;

            StampMembers(doc, els, sysName, def, row);

            row.Outcome = created ? MepSystemBuildOutcome.Created : MepSystemBuildOutcome.Typed;
            if (string.IsNullOrEmpty(row.Note))
                row.Note = created ? "system created + typed + named" : "existing system typed + named";
        }

        // ── orphan creation (best-effort) ───────────────────────────────────

        private static MEPSystem TryCreateSystem(
            Document doc, List<Element> els, bool isDuct, MepSystemBuildRow row,
            List<string> warnings, out bool created)
        {
            created = false;
            var wantedDomain = isDuct ? Domain.DomainHvac : Domain.DomainPiping;

            // Gather equipment / terminal connectors of the right domain that are
            // free to be added to a new system.
            var equipConns = new List<Connector>();
            foreach (var fi in els.OfType<FamilyInstance>())
            {
                ConnectorSet cs = null;
                try { cs = fi.MEPModel?.ConnectorManager?.Connectors; } catch { }
                if (cs == null) continue;
                foreach (Connector c in cs)
                {
                    try { if (c != null && c.Domain == wantedDomain) equipConns.Add(c); }
                    catch { }
                }
            }
            if (equipConns.Count < 2)
            {
                row.Note = "fewer than two equipment/terminal connectors — cannot anchor a system";
                return null;
            }

            // Base = a source connector (flow Out), else the first equipment connector.
            Connector baseConn = equipConns.FirstOrDefault(c =>
            {
                try { return c.Direction == FlowDirectionType.Out; } catch { return false; }
            }) ?? equipConns[0];

            var members = new ConnectorSet();
            foreach (var c in equipConns)
                if (!ReferenceEquals(c, baseConn)) { try { members.Insert(c); } catch { } }
            if (members.IsEmpty)
            {
                row.Note = "no non-base connectors to serve";
                return null;
            }

            try
            {
                MEPSystem result;
                if (isDuct)
                {
                    var dst = MapDuctSystemType(baseConn);
                    result = doc.Create.NewMechanicalSystem(baseConn, members, dst);
                }
                else
                {
                    var pst = MapPipeSystemType(baseConn);
                    result = doc.Create.NewPipingSystem(baseConn, members, pst);
                }
                created = result != null;
                PlaceSystemOnSourceWorkset(doc, result, baseConn); // workset integration — new object only
                return result;
            }
            catch (Exception ex)
            {
                row.Note = $"create failed: {ex.Message}";
                warnings.Add($"{row.Network}: NewMEPSystem: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Put a newly-created system object on the same workset as its source
        /// equipment so it lives with its network rather than the active workset.
        /// Members are never moved — that stays the coordinator's call.
        /// </summary>
        private static void PlaceSystemOnSourceWorkset(Document doc, MEPSystem system, Connector baseConn)
        {
            try
            {
                if (system == null || baseConn?.Owner == null || !doc.IsWorkshared) return;
                var src = baseConn.Owner.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                var dst = system.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (src != null && dst != null && !dst.IsReadOnly &&
                    src.StorageType == StorageType.Integer && dst.StorageType == StorageType.Integer)
                    dst.Set(src.AsInteger());
            }
            catch { /* non-critical */ }
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static void StampMembers(
            Document doc, List<Element> els, string sysName, MepSystemTypeDef def, MepSystemBuildRow row)
        {
            if (string.IsNullOrEmpty(sysName))
            {
                // Fall back to whatever Revit already calls the system.
                sysName = els.OfType<MEPCurve>()
                    .Select(c => { try { return c.MEPSystem?.Name; } catch { return null; } })
                    .FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";
            }
            if (string.IsNullOrEmpty(sysName)) return;
            foreach (var el in els)
            {
                try { ParameterHelpers.SetString(el, ParamRegistry.MEP_SYS_NAME, sysName, overwrite: true); }
                catch { }
                // Feed the ISO 19650 tag grammar: write the SYS / FUNC tokens from the
                // Phase A definition so AutoTag/BatchTag inherit them instead of falling
                // back to the generic category code. SetIfEmpty preserves manual edits.
                if (def != null)
                {
                    if (!string.IsNullOrWhiteSpace(def.StingSysCode))
                        try { ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, def.StingSysCode); } catch { }
                    if (!string.IsNullOrWhiteSpace(def.StingFuncCode))
                        try { ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, def.StingFuncCode); } catch { }
                }
            }
        }

        private static void TrySetSystemName(MEPSystem system, string name, List<string> warnings, string netId)
        {
            // System name is normally the computed abbreviation+number. Setting
            // RBS_SYSTEM_NAME_PARAM renames it where the model allows it.
            try
            {
                var p = system.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(name);
            }
            catch (Exception ex) { warnings.Add($"{netId}: name '{name}': {ex.Message}"); }
        }

        private static Dictionary<MEPSystemClassification, ElementId> BuildTypeIndex<T>(Document doc)
            where T : MEPSystemType
        {
            // Deterministic: the FIRST STING type per classification (ordered by name)
            // wins; a non-STING type only fills a classification that has no STING
            // type. Without the ordering FilteredElementCollector order is arbitrary,
            // so when several STING types share a classification (CHW/LTHW/CW Flow are
            // all SupplyHydronic) a re-run could type the same pipes differently.
            var dict = new Dictionary<MEPSystemClassification, ElementId>();
            var stingClaimed = new HashSet<MEPSystemClassification>();
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>()
                         .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                MEPSystemClassification cls;
                try { cls = t.SystemClassification; } catch { continue; }
                bool isSting = (t.Name ?? "").StartsWith("STING", StringComparison.OrdinalIgnoreCase);
                if (isSting)
                {
                    if (stingClaimed.Add(cls)) dict[cls] = t.Id;
                }
                else if (!dict.ContainsKey(cls)) dict[cls] = t.Id;
            }
            return dict;
        }

        /// <summary>
        /// Prime the per-abbreviation name counter from existing "&lt;abbr&gt;-NN"
        /// system names so re-runs continue the sequence instead of colliding.
        /// </summary>
        private static Dictionary<string, int> SeedNameSeq(Document doc)
        {
            var seq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rx = new Regex(@"^([A-Za-z0-9]+)-(\d+)$"); // allow digit-bearing abbreviations (DCW2, CO2, R410A)
            void Scan<T>() where T : Element
            {
                foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>())
                {
                    string name; try { name = s.Name; } catch { continue; }
                    if (string.IsNullOrEmpty(name)) continue;
                    var m = rx.Match(name);
                    if (!m.Success) continue;
                    string abbr = m.Groups[1].Value;
                    if (int.TryParse(m.Groups[2].Value, out int n) &&
                        (!seq.TryGetValue(abbr, out int cur) || n > cur)) seq[abbr] = n;
                }
            }
            try { Scan<MechanicalSystem>(); Scan<PipingSystem>(); } catch { }
            return seq;
        }

        // Map a connector's classification onto the DuctSystemType / PipeSystemType
        // enums NewMechanicalSystem / NewPipingSystem require. The enum member names
        // mostly mirror MEPSystemClassification, so parse-by-name with a safe default.
        private static DuctSystemType MapDuctSystemType(Connector baseConn)
        {
            try
            {
                var sys = baseConn.MEPSystem as MechanicalSystem;
                if (sys != null) return sys.SystemType;
            }
            catch { }
            // Supply by default for an "Out" source, return otherwise.
            try { return baseConn.Direction == FlowDirectionType.In ? DuctSystemType.ReturnAir : DuctSystemType.SupplyAir; }
            catch { return DuctSystemType.SupplyAir; }
        }

        private static PipeSystemType MapPipeSystemType(Connector baseConn)
        {
            try
            {
                var sys = baseConn.MEPSystem as PipingSystem;
                if (sys != null) return sys.SystemType;
            }
            catch { }
            try { return baseConn.Direction == FlowDirectionType.In ? PipeSystemType.ReturnHydronic : PipeSystemType.SupplyHydronic; }
            catch { return PipeSystemType.OtherPipe; }
        }

        // BFS over the connector graph — same shape as MepSystemTracerCommand.
        private static void WalkConnectorGraph(
            Document doc, ElementId start, HashSet<long> visited, List<ElementId> ordered)
        {
            var queue = new Queue<ElementId>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (!visited.Add(id.Value)) continue;
                ordered.Add(id);

                Element el = null;
                try { el = doc.GetElement(id); } catch { }
                if (el == null) continue;

                ConnectorSet set = ResolveConnectors(el);
                if (set == null) continue;
                foreach (Connector c in set)
                {
                    if (c == null) continue;
                    ConnectorSet others = null;
                    try { others = c.AllRefs; } catch { }
                    if (others == null) continue;
                    foreach (Connector other in others)
                    {
                        if (other?.Owner == null) continue;
                        if (other.Owner.Id.Value == id.Value) continue;
                        queue.Enqueue(other.Owner.Id);
                    }
                }
            }
        }

        private static ConnectorSet ResolveConnectors(Element el)
        {
            try
            {
                if (el is MEPCurve mep) return mep.ConnectorManager?.Connectors;
                if (el is FamilyInstance fi && fi.MEPModel != null) return fi.MEPModel.ConnectorManager?.Connectors;
            }
            catch { }
            return null;
        }
    }
}
