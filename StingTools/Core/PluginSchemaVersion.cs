#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// S3.6 — single source of truth for the JSON file schemas the plugin
    /// reads/writes from disk. Every file picks a schema name + version;
    /// migrators upgrade old payloads to the current shape on read so a
    /// stale file from a 6-month-old plugin install never silently
    /// corrupts a project's state.
    ///
    /// Files governed by this layer:
    ///
    ///   • <c>_BIM_COORD/templates/manifest.json</c>           (TemplateEngine v1.1)
    ///   • <c>_BIM_COORD/doc_sequences.json</c>                 (DocumentIdentityGenerator)
    ///   • <c>_BIM_COORD/deliverables.json</c>                  (DeliverableLifecycle)
    ///   • <c>_BIM_COORD/transmittals.json</c>                  (TransmittalOrchestrator)
    ///   • <c>_BIM_COORD/workflow_state.json</c>                (WorkflowEngine)
    ///   • <c>_BIM_COORD/audit_log_YYYY_MM.jsonl</c>            (AuditLog)
    ///   • <c>_BIM_COORD/distribution_groups.json</c>           (DistributionGroups)
    ///   • <c>_BIM_COORD/saved_searches.json</c>                (SavedSearchStore)
    ///   • <c>&lt;rvt&gt;.sting_seq.json</c>                    (TagConfig SEQ sidecar)
    ///   • <c>&lt;project&gt;/_BIM_COORD/drawing_types.json</c> (DrawingTypeRegistry)
    ///   • <c>&lt;project&gt;/_BIM_COORD/aec_filters.json</c>   (AecFilterRegistry)
    ///
    /// Each gets a <c>$schema</c> key (file URL) and a <c>$schemaVersion</c>
    /// integer. <see cref="EnsureCurrent"/> reads the version, runs each
    /// registered migrator in order, and writes back the upgraded JSON
    /// atomically.
    /// </summary>
    public static class PluginSchemaVersion
    {
        public const int CurrentManifest         = 2;
        public const int CurrentDocSequences     = 1;
        public const int CurrentDeliverables     = 1;
        public const int CurrentTransmittals     = 1;
        public const int CurrentWorkflowState    = 1;
        public const int CurrentAuditLog         = 1;
        public const int CurrentDistribution     = 1;
        public const int CurrentSavedSearches    = 1;
        public const int CurrentSeqSidecar       = 1;
        public const int CurrentDrawingTypes     = 1;
        public const int CurrentAecFilters       = 1;

        public delegate JObject Migrator(JObject input, int fromVersion);

        /// <summary>
        /// S3.6.1 — fire-and-forget version gate every JSON-store Load method
        /// calls before its own deserialize. If the file's <c>$schemaVersion</c>
        /// is older than <paramref name="targetVersion"/> the registered
        /// migrators run and the file is rewritten in place; subsequent
        /// <c>File.ReadAllText</c> sees the upgraded payload.
        ///
        /// Lighter-touch than <see cref="EnsureCurrent"/>: no JObject return,
        /// no caller-side change to deserialization. Drop one call at the top
        /// of every Load and you're covered.
        /// </summary>
        public static void EnsureFileVersion(string filePath, string schemaName, int targetVersion, IReadOnlyList<Migrator>? migrators = null)
        {
            try { EnsureCurrent(filePath, schemaName, targetVersion, migrators); }
            catch (Exception ex) { StingLog.Warn($"PluginSchemaVersion.EnsureFileVersion failed for '{filePath}': {ex.Message}"); }
        }

        /// <summary>
        /// Read a plugin JSON file, bring it up to <paramref name="targetVersion"/>
        /// by running every registered migrator in version order, write back
        /// atomically. Returns the upgraded JObject so callers don't re-read.
        /// Idempotent — if the file is already current, returns it untouched.
        /// </summary>
        public static JObject EnsureCurrent(string filePath, string schemaName, int targetVersion, IReadOnlyList<Migrator>? migrators = null)
        {
            // S8.2.2 — span the migration so a slow / large schema upgrade
            // surfaces as a duration outlier in telemetry rather than as
            // mysterious 'plugin froze on project open' support tickets.
            return PluginTelemetry.Run(
                "schema.ensureCurrent:" + schemaName,
                () => EnsureCurrentImpl(filePath, schemaName, targetVersion, migrators));
        }

        private static JObject EnsureCurrentImpl(string filePath, string schemaName, int targetVersion, IReadOnlyList<Migrator>? migrators)
        {
            if (!File.Exists(filePath))
                return CreateGenesis(filePath, schemaName, targetVersion);

            JObject obj;
            try
            {
                var text = File.ReadAllText(filePath);
                obj = string.IsNullOrWhiteSpace(text)
                    ? CreateGenesis(filePath, schemaName, targetVersion)
                    : JObject.Parse(text);
            }
            catch (JsonReaderException)
            {
                StingLog.Warn($"Schema file {Path.GetFileName(filePath)} unparseable; quarantining and re-creating.");
                Quarantine(filePath);
                return CreateGenesis(filePath, schemaName, targetVersion);
            }

            int currentVersion = obj.Value<int?>("$schemaVersion") ?? 0;
            if (currentVersion == targetVersion) return obj;
            if (currentVersion > targetVersion)
            {
                StingLog.Warn(
                    $"Schema file {Path.GetFileName(filePath)} is at v{currentVersion} but plugin only knows v{targetVersion}. " +
                    $"Plugin is OLDER than the file — refusing to write so a newer version's data isn't downgraded.");
                return obj;
            }

            for (int v = currentVersion; v < targetVersion; v++)
            {
                var migrator = (migrators != null && v - currentVersion < migrators.Count) ? migrators[v - currentVersion] : null;
                if (migrator != null) obj = migrator(obj, v);
                obj["$schemaVersion"] = v + 1;
            }
            obj["$schema"] = schemaName;
            WriteAtomic(filePath, obj);
            StingLog.Info($"Schema file {Path.GetFileName(filePath)} upgraded {currentVersion} → {targetVersion}.");
            return obj;
        }

        private static JObject CreateGenesis(string filePath, string schemaName, int version)
        {
            var seed = new JObject
            {
                ["$schema"]        = schemaName,
                ["$schemaVersion"] = version,
                ["createdAt"]      = DateTime.UtcNow.ToString("o"),
            };
            WriteAtomic(filePath, seed);
            return seed;
        }

        private static void WriteAtomic(string filePath, JObject obj)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = filePath + ".tmp";
            File.WriteAllText(tmp, obj.ToString(Formatting.Indented));
            // Replace is atomic on Windows + POSIX.
            if (File.Exists(filePath))
                File.Replace(tmp, filePath, filePath + ".bak");
            else
                File.Move(tmp, filePath);
        }

        private static void Quarantine(string filePath)
        {
            try
            {
                var stamped = filePath + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(filePath, stamped);
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to quarantine {filePath}", ex);
            }
        }
    }
}
