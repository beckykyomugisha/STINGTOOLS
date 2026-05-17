using StingTools.Core;
// ClashExclusions.cs — persistent list of user-approved clashes that should not re-surface.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Clash
{
    public sealed class ClashExclusion
    {
        public string IdentityHash;    // from ClashIdentity.Compute
        public string Reason;
        public string Approver;
        public DateTime ApprovedUtc;
        public DateTime? ExpiresUtc;
    }

    /// <summary>F8: Per-run exclusion audit row written to an append-only JSONL.</summary>
    public sealed class ClashExclusionAudit
    {
        public DateTime AtUtc;
        public string IdentityHash;
        public string MatrixPairId;
        public string Reason;
        public string Approver;
        public string RunId;
        public string Outcome;   // "excluded" | "expired" | "missing"
    }

    public sealed class ClashExclusions
    {
        public List<ClashExclusion> Entries { get; set; } = new List<ClashExclusion>();
        [JsonIgnore] public string Path { get; set; }

        public static ClashExclusions Load(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    var e = JsonConvert.DeserializeObject<ClashExclusions>(File.ReadAllText(path)) ?? new ClashExclusions();
                    e.Path = path;
                    return e;
                }
                catch (Exception ex)
                {
                    // H9: Previously a bare catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); } — corrupt exclusions.json
                    // silently fell back to empty, then the next Save() would
                    // overwrite the user's approved-clash list with a blank file.
                    // Log loudly so the corruption is visible + user-diagnosable.
                    StingTools.Core.StingLog.Warn($"ClashExclusions.Load({path}) failed: {ex.Message}. Treating as empty.");
                }
            }
            return new ClashExclusions { Path = path };
        }

        public bool IsExcluded(string identityHash)
            => IsExcludedAudited(identityHash, matrixPairId: null, runId: null);

        /// <summary>
        /// F8: Audited exclusion check. Same return value as IsExcluded but
        /// appends a row to {dir}/clash_exclusions_audit.jsonl recording WHY
        /// (matrix pair, approver, reason) and WHEN. ISO 19650 stage-gate
        /// evidence requires this audit trail.
        /// </summary>
        public bool IsExcludedAudited(string identityHash, string matrixPairId, string runId)
        {
            var e = Entries.FirstOrDefault(x => x.IdentityHash == identityHash);
            string outcome;
            bool excluded = false;
            if (e == null) outcome = "missing";
            else if (e.ExpiresUtc.HasValue && e.ExpiresUtc.Value < DateTime.UtcNow) outcome = "expired";
            else { outcome = "excluded"; excluded = true; }

            // Only audit "excluded" outcomes — missing entries are the
            // overwhelming majority and would flood the audit log.
            if (excluded)
            {
                AppendAudit(new ClashExclusionAudit
                {
                    AtUtc = DateTime.UtcNow,
                    IdentityHash = identityHash,
                    MatrixPairId = matrixPairId,
                    Reason = e?.Reason,
                    Approver = e?.Approver,
                    RunId = runId,
                    Outcome = outcome,
                });
            }
            return excluded;
        }

        private void AppendAudit(ClashExclusionAudit row)
        {
            try
            {
                if (string.IsNullOrEmpty(Path)) return;
                string dir = System.IO.Path.GetDirectoryName(Path) ?? "";
                if (string.IsNullOrEmpty(dir)) return;
                string auditPath = System.IO.Path.Combine(dir, "clash_exclusions_audit.jsonl");
                File.AppendAllText(auditPath, JsonConvert.SerializeObject(row) + Environment.NewLine);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ClashExclusions.AppendAudit: {ex.Message}"); }
        }

        public void Add(string identityHash, string reason, string approver, TimeSpan? ttl = null)
        {
            Entries.RemoveAll(e => e.IdentityHash == identityHash);
            Entries.Add(new ClashExclusion
            {
                IdentityHash = identityHash,
                Reason = reason ?? "",
                Approver = approver ?? "",
                ApprovedUtc = DateTime.UtcNow,
                ExpiresUtc = ttl.HasValue ? (DateTime?)DateTime.UtcNow.Add(ttl.Value) : null
            });
            Save();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Path)) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                // H9: Disk-full / ACL / file-lock — user's approval action
                // silently vanished before. Log so the failure is visible.
                StingTools.Core.StingLog.Warn($"ClashExclusions.Save({Path}) failed: {ex.Message}");
            }
        }
    }
}
