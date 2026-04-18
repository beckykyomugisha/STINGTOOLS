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
                    // H9: Previously a bare catch { } — corrupt exclusions.json
                    // silently fell back to empty, then the next Save() would
                    // overwrite the user's approved-clash list with a blank file.
                    // Log loudly so the corruption is visible + user-diagnosable.
                    StingTools.Core.StingLog.Warn($"ClashExclusions.Load({path}) failed: {ex.Message}. Treating as empty.");
                }
            }
            return new ClashExclusions { Path = path };
        }

        public bool IsExcluded(string identityHash)
        {
            var e = Entries.FirstOrDefault(x => x.IdentityHash == identityHash);
            if (e == null) return false;
            if (e.ExpiresUtc.HasValue && e.ExpiresUtc.Value < DateTime.UtcNow) return false;
            return true;
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
