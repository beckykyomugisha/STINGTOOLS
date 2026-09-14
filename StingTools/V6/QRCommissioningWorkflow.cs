using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>N-G16: QR-driven commissioning state machine with audit log.
    /// Transitions: NOT_STARTED → RECEIVED → INSTALLED → TESTED → COMMISSIONED → HANDOVER.
    /// Each step stamps COMM_STATE_TXT / COMM_DATE_TXT / COMM_OPERATIVE_TXT (+ witness + notes).</summary>
    public static class QRCommissioningWorkflow
    {
        /// <summary>The state ladder. Aliases the shared list rather than restating it —
        /// a second copy of the states is how the desktop and the API come to disagree
        /// about what "advance" means, and that disagreement is silent.</summary>
        public static string[] States => Planscape.Shared.Commissioning.CommissioningStates.All;

        public class ScanPayload
        {
            public string ElementUniqueId { get; set; }
            public string Operative { get; set; }
            public string Witness { get; set; }
            public string Notes { get; set; }
            public string RequestedState { get; set; }   // if null, advances by one step
        }

        public class AuditEntry
        {
            public string When { get; set; }
            public string ElementUniqueId { get; set; }
            public string ElementName { get; set; }
            public string FromState { get; set; }
            public string ToState { get; set; }
            public string Operative { get; set; }
            public string Witness { get; set; }
            public string Notes { get; set; }
        }

        public class TransitionResult
        {
            public bool Ok { get; set; }
            public string FromState { get; set; }
            public string ToState { get; set; }
            public string Reason { get; set; }
        }

        public static int IndexOfState(string state)
            => Math.Max(0, Planscape.Shared.Commissioning.CommissioningStates.RankOf(state));

        public static string NextState(string current)
            => Planscape.Shared.Commissioning.CommissioningStates.Next(current)
               ?? Planscape.Shared.Commissioning.CommissioningStates.All[0];

        /// <summary>Advance one element's commissioning state.
        ///
        /// The RULES now live in Planscape.Shared.Commissioning.CommissioningStateMachine,
        /// which the server's CommissioningController also calls. They used to live only
        /// here, which was fine while the desktop was the only way in — but the phone
        /// needed the same ladder, and two copies of a state machine drift invisibly: a
        /// desktop that refuses a skip-state transition and an API that allows it produce
        /// a handover record nobody can reconcile, months later, with no error anywhere.
        ///
        /// This method keeps what is genuinely Revit's: resolving the element, writing the
        /// COMM_* parameters, and appending to the on-disk audit trail.</summary>
        public static TransitionResult Advance(Document doc, ScanPayload scan)
        {
            if (scan == null || string.IsNullOrEmpty(scan.ElementUniqueId))
                return new TransitionResult { Ok = false, Reason = "Scan payload missing ElementUniqueId" };
            var el = doc.GetElement(scan.ElementUniqueId);
            if (el == null)
                return new TransitionResult { Ok = false, Reason = $"Element not found: {scan.ElementUniqueId}" };

            string current = ParameterHelpers.GetString(el, ParamRegistry.COMM_STATE_TXT);

            var decision = Planscape.Shared.Commissioning.CommissioningStateMachine.Decide(
                current,
                new Planscape.Shared.Commissioning.CommissioningRequest
                {
                    RequestedState = scan.RequestedState,
                    Operative = scan.Operative,
                    Witness = scan.Witness,
                    Notes = scan.Notes,
                });

            if (!decision.Ok)
                return new TransitionResult
                {
                    Ok = false,
                    FromState = decision.FromState,
                    ToState = decision.ToState,
                    Reason = decision.Reason,
                };

            string target = decision.ToState;
            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            ParameterHelpers.SetString(el, ParamRegistry.COMM_STATE_TXT, target, overwrite: true);
            ParameterHelpers.SetString(el, ParamRegistry.COMM_DATE_TXT, now, overwrite: true);
            ParameterHelpers.SetString(el, ParamRegistry.COMM_OPERATIVE_TXT, scan.Operative ?? "", overwrite: true);
            if (!string.IsNullOrEmpty(scan.Witness))
                ParameterHelpers.SetString(el, ParamRegistry.COMM_WITNESS_TXT, scan.Witness, overwrite: true);
            if (!string.IsNullOrEmpty(scan.Notes))
                ParameterHelpers.SetString(el, ParamRegistry.COMM_NOTES_TXT, scan.Notes, overwrite: true);

            AppendAudit(doc, new AuditEntry
            {
                When = now,
                ElementUniqueId = scan.ElementUniqueId,
                ElementName = el.Name,
                FromState = string.IsNullOrEmpty(current) ? "NOT_STARTED" : current,
                ToState = target,
                Operative = scan.Operative,
                Witness = scan.Witness,
                Notes = scan.Notes,
            });
            return new TransitionResult { Ok = true, FromState = current, ToState = target };
        }

        public static void AppendAudit(Document doc, AuditEntry entry)
        {
            try
            {
                string path = AuditLogPath(doc);
                var entries = ReadAudit(path);
                entries.Add(entry);
                // Keep last 10000 entries — prevents unbounded growth.
                if (entries.Count > 10000) entries = entries.Skip(entries.Count - 10000).ToList();
                File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"QRCommissioning audit append failed: {ex.Message}"); }
        }

        public static List<AuditEntry> ReadAudit(string path)
        {
            if (!File.Exists(path)) return new List<AuditEntry>();
            try { return JsonConvert.DeserializeObject<List<AuditEntry>>(File.ReadAllText(path)) ?? new List<AuditEntry>(); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return new List<AuditEntry>(); }
        }

        public static string AuditLogPath(Document doc)
        {
            string dir = OutputLocationHelper.GetOutputDirectory(doc);
            return Path.Combine(dir, "STING_Commissioning_Audit.json");
        }
    }
}
