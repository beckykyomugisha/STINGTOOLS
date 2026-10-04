// StingSeqLockStore — SEQ counters kept IN the model, and used as a worksharing lock.
//
// TAGACC-13. Without a Planscape server, SEQ numbers were allocated from the local model
// only, so two users tagging before a sync could hand out the same number; TAGACC-2
// finds and repairs that after the sync, but does not prevent it.
//
// Revit already has the lock needed: an element can be borrowed by one user at a time,
// and cannot be borrowed at all while central holds a newer version of it. So the
// counters live in one DataStorage element (Extensible Storage, a JSON map of
// seqKey → highest number used). Before a user hands out a NEW number, they must hold
// that element:
//
//   • borrowed by nobody and up to date   → borrow it, allocate, write the counters
//   • borrowed by someone else            → blocked until they synchronise
//   • changed in central since last sync  → blocked until Reload Latest / Synchronise
//
// Revit relinquishes borrowed elements on Synchronise with Central (the default), which
// is also when the counters reach everyone else. An element that already holds a SEQ is
// never blocked — only allocation is.
//
// TagConfig.SeqLockMode (SEQ_LOCK_MODE in project_config.json, "Tag Rules" button):
//   block (default) — no new number while the counter is unavailable; the element is
//                     deferred (auto-tagger) or reported (Batch Tag), and retried later
//   warn            — allocate anyway and log; TAGACC-2 repairs duplicates after sync
//   off             — ignore the lock
//
// One window is not closed: two users who both tag for the FIRST time before either has
// synced each create a counter element. Load merges every counter element found (max
// per key), so after that first sync the lock holds; the duplicates from that one window
// are repaired by TAGACC-2.
//
// Borrowing (WorksharingUtils.CheckoutElements) is not allowed while a transaction is
// open, so Reserve runs when the tag index is built — before any tagging transaction —
// and AllocationAllowed, called inside it, only reads the local checkout status.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json;

namespace StingTools.Core.Storage
{
    public static class StingSeqLockStore
    {
        public static readonly Guid SchemaGuid = new Guid("7C3E91A4-5B2D-4F86-A1D0-3E9B6C24F718");
        private const string SchemaName = "StingSeqCounterSchema";
        private const string FieldCounters = "CountersJson";
        private const string FieldWrittenBy = "WrittenBy";

        // Blocked answers are cached briefly so a batch does not ask central per element.
        private static readonly Dictionary<string, (DateTime At, string Reason)> _blocked =
            new Dictionary<string, (DateTime, string)>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        private static readonly TimeSpan BlockedTtl = TimeSpan.FromSeconds(30);

        public static Schema GetOrCreateSchema()
        {
            try
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null) return existing;
                var sb = new SchemaBuilder(SchemaGuid);
                sb.SetSchemaName(SchemaName);
                sb.SetVendorId(StingSchemaBuilder.VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(FieldCounters, typeof(string))
                    .SetDocumentation("JSON map of SEQ counter key to the highest sequence number allocated");
                sb.AddSimpleField(FieldWrittenBy, typeof(string))
                    .SetDocumentation("User and UTC time of the last write");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingSeqLockStore schema: {ex.Message}");
                return null;
            }
        }

        private static bool LockApplies(Document doc)
        {
            if (doc == null || !doc.IsWorkshared) return false;
            return !string.Equals(TagConfig.SeqLockMode, "off", StringComparison.OrdinalIgnoreCase);
        }

        private static string Key(Document doc)
        {
            try { return doc?.PathName ?? ""; } catch (Exception) { return ""; }
        }

        /// <summary>Every counter element in the model, lowest id first.</summary>
        public static List<DataStorage> Find(Document doc)
        {
            var result = new List<DataStorage>();
            if (doc == null) return result;
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return result;
            try
            {
                foreach (DataStorage ds in new FilteredElementCollector(doc).OfClass(typeof(DataStorage)))
                {
                    Entity e = ds.GetEntity(schema);
                    if (e != null && e.IsValid()) result.Add(ds);
                }
            }
            catch (Exception ex) { StingLog.Warn($"StingSeqLockStore.Find: {ex.Message}"); }
            return result.OrderBy(d => d.Id.Value).ToList();
        }

        /// <summary>The counters recorded in the model (max per key across counter elements).</summary>
        public static Dictionary<string, int> Load(Document doc)
        {
            var merged = new Dictionary<string, int>();
            var schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return merged;
            foreach (var ds in Find(doc))
            {
                try
                {
                    string json = ds.GetEntity(schema).Get<string>(FieldCounters);
                    var map = string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                    if (map == null) continue;
                    foreach (var kv in map)
                        if (!merged.TryGetValue(kv.Key, out int cur) || kv.Value > cur) merged[kv.Key] = kv.Value;
                }
                catch (Exception ex) { StingLog.Warn($"StingSeqLockStore.Load {ds.Id}: {ex.Message}"); }
            }
            return merged;
        }

        /// <summary>
        /// Borrow the counter element(s) ahead of a tagging run. Must be called with no
        /// transaction open. Returns false with <paramref name="reason"/> when another user
        /// holds them or central has a newer version.
        /// </summary>
        public static bool Reserve(Document doc, out string reason)
        {
            reason = null;
            if (!LockApplies(doc)) return true;
            if (doc.IsModifiable)
            {
                // Cannot borrow inside a transaction; answer from the local status instead.
                return AllocationAllowed(doc, out reason);
            }
            try
            {
                foreach (var ds in Find(doc))
                {
                    var status = WorksharingUtils.GetCheckoutStatus(doc, ds.Id, out string owner);
                    if (status == CheckoutStatus.OwnedByCurrentUser) continue;
                    if (status == CheckoutStatus.OwnedByOtherUser)
                    {
                        reason = $"the SEQ counter is borrowed by {owner} until they synchronise with central";
                        return Blocked(doc, reason);
                    }
                    var updates = WorksharingUtils.GetModelUpdatesStatus(doc, ds.Id);
                    if (updates == ModelUpdatesStatus.UpdatedInCentral || updates == ModelUpdatesStatus.DeletedInCentral)
                    {
                        reason = "new numbers were allocated in central since your last sync — Reload Latest or Synchronise first";
                        return Blocked(doc, reason);
                    }
                    var got = WorksharingUtils.CheckoutElements(doc, new List<ElementId> { ds.Id });
                    if (got == null || !got.Contains(ds.Id))
                    {
                        reason = "the SEQ counter could not be borrowed from central";
                        return Blocked(doc, reason);
                    }
                }
                lock (_lock) _blocked.Remove(Key(doc));
                return true;
            }
            catch (Exception ex)
            {
                reason = $"the SEQ counter could not be borrowed ({ex.Message})";
                StingLog.Warn($"StingSeqLockStore.Reserve: {ex.Message}");
                return Blocked(doc, reason);
            }
        }

        /// <summary>
        /// May this session hand out a NEW sequence number now? Reads only local status, so
        /// it is safe inside a transaction. True when the lock does not apply, when no
        /// counter element exists yet (the first allocation creates it, owned by this user),
        /// or when this user holds every counter element.
        /// </summary>
        public static bool AllocationAllowed(Document doc, out string reason)
        {
            reason = null;
            if (!LockApplies(doc)) return true;
            string key = Key(doc);
            lock (_lock)
            {
                if (_blocked.TryGetValue(key, out var b) && DateTime.UtcNow - b.At < BlockedTtl)
                {
                    reason = b.Reason;
                    return false;
                }
            }
            try
            {
                foreach (var ds in Find(doc))
                {
                    var status = WorksharingUtils.GetCheckoutStatus(doc, ds.Id, out string owner);
                    if (status == CheckoutStatus.OwnedByCurrentUser) continue;
                    reason = status == CheckoutStatus.OwnedByOtherUser
                        ? $"the SEQ counter is borrowed by {owner} until they synchronise with central"
                        : "the SEQ counter has not been borrowed for this run";
                    return Blocked(doc, reason);
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = $"the SEQ counter status could not be read ({ex.Message})";
                return Blocked(doc, reason);
            }
        }

        private static bool Blocked(Document doc, string reason)
        {
            lock (_lock) _blocked[Key(doc)] = (DateTime.UtcNow, reason);
            return false;
        }

        /// <summary>Forget cached answers (after a sync: the element was relinquished).</summary>
        public static void Invalidate(Document doc)
        {
            lock (_lock) _blocked.Remove(Key(doc));
        }

        /// <summary>
        /// Write the counters into the model (max-merged with what is there). Opens its own
        /// transaction when none is open. Skipped when another user holds the element —
        /// they will write theirs; ours are already reflected in the tags.
        /// </summary>
        public static void Save(Document doc, Dictionary<string, int> counters)
        {
            if (doc == null || counters == null || counters.Count == 0) return;
            if (!doc.IsWorkshared) return;   // only needed as a cross-user lock
            if (doc.IsReadOnly) return;
            try
            {
                var schema = GetOrCreateSchema();
                if (schema == null) return;
                var stores = Find(doc);
                DataStorage target = stores.FirstOrDefault();
                if (target != null)
                {
                    var status = WorksharingUtils.GetCheckoutStatus(doc, target.Id, out _);
                    if (status == CheckoutStatus.OwnedByOtherUser) return;
                }

                var merged = Load(doc);
                foreach (var kv in counters)
                    if (!merged.TryGetValue(kv.Key, out int cur) || kv.Value > cur) merged[kv.Key] = kv.Value;

                void Write()
                {
                    DataStorage ds = target ?? DataStorage.Create(doc);
                    var entity = new Entity(schema);
                    entity.Set(FieldCounters, JsonConvert.SerializeObject(merged));
                    entity.Set(FieldWrittenBy, $"{doc.Application.Username} {DateTime.UtcNow:O}");
                    ds.SetEntity(entity);
                }

                if (doc.IsModifiable) { Write(); return; }
                using (var tx = new Transaction(doc, "STING SEQ counters"))
                {
                    tx.Start();
                    Write();
                    if (!StingTx.TryCommit(tx, null, out string why)) StingLog.Warn(why);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingSeqLockStore.Save: {ex.Message}");
            }
        }
    }
}
