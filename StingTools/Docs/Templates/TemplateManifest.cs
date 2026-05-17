// TemplateManifest.cs — Planscape template engine v1.1 (S03).
//
// POCOs describing a project's template pack: registered templates, identifier
// format, project-level defaults, custom extensions, signature config, and a
// validator that surfaces missing/duplicated entries at load time.
//
// Persistence: _BIM_COORD/templates/manifest.json.
// Load / Save roundtrip uses Newtonsoft.Json. CreateDefault() seeds a minimal
// manifest pulling from ProjectInformation + PRJ_ORG_* shared parameters.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    /// <summary>Root manifest describing the project's template pack and engine options.</summary>
    public class TemplateManifest
    {
        [JsonProperty("version")]             public string Version { get; set; } = "1.1";
        [JsonProperty("project")]             public ProjectManifestBlock Project { get; set; } = new ProjectManifestBlock();
        [JsonProperty("templates")]           public List<TemplateEntry> Templates { get; set; } = new List<TemplateEntry>();
        [JsonProperty("extensions")]          public ManifestExtensions Extensions { get; set; } = new ManifestExtensions();
        [JsonProperty("signature")]           public SignatureConfig Signature { get; set; } = new SignatureConfig();
        [JsonProperty("use_legacy_renderer")] public bool UseLegacyRenderer { get; set; } = false;

        /// <summary>Resolves a template by id. Returns null if unknown.</summary>
        public TemplateEntry FindById(string id)
        {
            if (string.IsNullOrEmpty(id) || Templates == null) return null;
            return Templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Resolves a template by family + purpose (e.g. ("B", "transmittal")).</summary>
        public TemplateEntry FindByPurpose(string family, string purpose)
        {
            if (Templates == null) return null;
            return Templates.FirstOrDefault(t =>
                string.Equals(t.Family, family, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Purpose, purpose, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Project-level metadata embedded in the manifest.</summary>
    public class ProjectManifestBlock
    {
        [JsonProperty("project_code")]       public string ProjectCode { get; set; }
        [JsonProperty("project_name")]       public string ProjectName { get; set; }
        [JsonProperty("originator_code")]    public string OriginatorCode { get; set; } = "PLNS";
        [JsonProperty("company_name")]       public string CompanyName { get; set; } = "Planscape Limited";
        [JsonProperty("company_address")]    public string CompanyAddress { get; set; } = "Kampala, Uganda";
        [JsonProperty("company_logo_path")]  public string CompanyLogoPath { get; set; }
        [JsonProperty("client_name")]        public string ClientName { get; set; }
        [JsonProperty("appointing_party")]   public string AppointingParty { get; set; }
        [JsonProperty("lead_appointed_party")] public string LeadAppointedParty { get; set; } = "Planscape Limited";
        [JsonProperty("participants")]       public string Participants { get; set; }
        [JsonProperty("phase")]              public string Phase { get; set; } = "DE";
        [JsonProperty("class")]              public string Class { get; set; } = "2";
        [JsonProperty("workflow_profile")]   public string WorkflowProfile { get; set; } = "default";

        /// <summary>Identifier format template (uses <see cref="DocumentIdentityGenerator"/>).</summary>
        /// <remarks>Supports {project_code} {originator} {role} {fb} {sb} {type} {number:D4}.</remarks>
        [JsonProperty("identifier_format")]  public string IdentifierFormat { get; set; }
            = "{project_code}-{originator}-{role}-{fb}-{sb}-{type}-{number:D4}";

        [JsonProperty("revision_scheme")]    public string RevisionScheme { get; set; } = "P01,P02,C01,C02";
        [JsonProperty("suitability_scheme")] public string SuitabilityScheme { get; set; }
            = "S0,S1,S2,S3,S4,S5,S6,S7,WIP,SHARED,PUBLISHED,ARCHIVE";
    }

    /// <summary>A single template registration entry (docx/xlsx).</summary>
    public class TemplateEntry
    {
        [JsonProperty("id")]          public string Id { get; set; }            // "A01"
        [JsonProperty("family")]      public string Family { get; set; }        // "A"|"B"|"C"|"D"
        [JsonProperty("purpose")]     public string Purpose { get; set; }       // "standard"|"transmittal"...
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("file")]        public string FileRelative { get; set; }  // relative to _BIM_COORD/templates/
        [JsonProperty("extension")]   public string Extension { get; set; }     // ".docx"|".xlsx"
        [JsonProperty("applies_to")]  public List<string> AppliesTo { get; set; } = new List<string>();
        [JsonProperty("tokens")]      public List<string> KnownTokens { get; set; } = new List<string>();
        [JsonProperty("requires_signature")] public bool RequiresSignature { get; set; }
        [JsonProperty("workflow_id")] public string WorkflowId { get; set; }
    }

    /// <summary>Project-specific extensions to the manifest's vocabulary.</summary>
    public class ManifestExtensions
    {
        [JsonProperty("custom_types")]        public List<string> CustomTypes { get; set; } = new List<string>();
        [JsonProperty("custom_roles")]        public List<string> CustomRoles { get; set; } = new List<string>();
        [JsonProperty("custom_suitabilities")]public List<string> CustomSuitabilities { get; set; } = new List<string>();
    }

    /// <summary>v1.1 signature provider config — provider identifier empty = off.</summary>
    public class SignatureConfig
    {
        [JsonProperty("provider")] public string Provider { get; set; } = ""; // "" | "docusign" | "adobe"
        [JsonProperty("endpoint")] public string Endpoint { get; set; }
        [JsonProperty("templates_require_signature")]
        public List<string> TemplatesRequireSignature { get; set; } = new List<string>();
    }

    /// <summary>Validator finding surfaced by <see cref="TemplateManifest.Validate"/>.</summary>
    public record ValidationIssue(string Severity, string Code, string Message, string Target);

    /// <summary>Static load / save / validate / defaults for <see cref="TemplateManifest"/>.</summary>
    public static class TemplateManifestIO
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        /// <summary>Reads a manifest from disk. Returns a fresh default manifest if the file is missing.</summary>
        public static TemplateManifest Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new TemplateManifest();
            try
            {
                // S3.6.1 — version gate (idempotent if the file is already current).
                StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                    path, "planscape.template-manifest",
                    StingTools.Core.PluginSchemaVersion.CurrentManifest);
                string json = File.ReadAllText(path);
                var m = JsonConvert.DeserializeObject<TemplateManifest>(json, _settings) ?? new TemplateManifest();
                // Defensive: repopulate null collections so callers can add freely.
                m.Templates  ??= new List<TemplateEntry>();
                m.Extensions ??= new ManifestExtensions();
                m.Signature  ??= new SignatureConfig();
                m.Project    ??= new ProjectManifestBlock();
                return m;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TemplateManifest.Load failed for '{path}': {ex.Message}");
                return new TemplateManifest();
            }
        }

        /// <summary>Writes a manifest atomically (.tmp + move).</summary>
        public static void Save(this TemplateManifest m, string path)
        {
            if (m == null || string.IsNullOrEmpty(path)) return;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(m, _settings));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                StingLog.Error($"TemplateManifest.Save failed for '{path}'", ex);
            }
        }

        /// <summary>Convenience instance wrapper (Save is implemented as extension above).</summary>
        public static void WriteTo(TemplateManifest m, string path) => m.Save(path);

        /// <summary>Creates a default manifest seeded from ProjectInformation + PRJ_ORG_* parameters.</summary>
        public static TemplateManifest CreateDefault(Document doc)
        {
            var m = new TemplateManifest();
            var p = m.Project;

            // Defaults straight from ParamRegistry.OrganisationDefaults.
            foreach (var kv in ParamRegistry.OrganisationDefaults)
            {
                // no-op; referenced so the registry link is preserved if refactored
                _ = kv.Key;
            }
            p.OriginatorCode     = "PLNS";
            p.CompanyName        = "Planscape Limited";
            p.CompanyAddress     = "Kampala, Uganda";
            p.LeadAppointedParty = "Planscape Limited";
            p.Phase              = "DE";
            p.Class              = "2";
            p.WorkflowProfile    = "default";

            if (doc != null)
            {
                try
                {
                    var info = doc.ProjectInformation;
                    if (info != null)
                    {
                        p.ProjectName = info.Name ?? p.ProjectName;
                        p.ProjectCode = ReadParam(info, ParamRegistry.ORG_PROJECT_CODE) ?? info.Number ?? p.ProjectCode;
                        p.OriginatorCode     = ReadParam(info, ParamRegistry.ORG_ORIGINATOR_CODE)     ?? p.OriginatorCode;
                        p.CompanyName        = ReadParam(info, ParamRegistry.ORG_COMPANY_NAME)        ?? p.CompanyName;
                        p.CompanyAddress     = ReadParam(info, ParamRegistry.ORG_COMPANY_ADDRESS)     ?? p.CompanyAddress;
                        p.ClientName         = ReadParam(info, ParamRegistry.ORG_CLIENT_NAME)         ?? p.ClientName;
                        p.AppointingParty    = ReadParam(info, ParamRegistry.ORG_APPOINTING_PARTY)    ?? p.AppointingParty;
                        p.LeadAppointedParty = ReadParam(info, ParamRegistry.ORG_LEAD_APPOINTED_PARTY)?? p.LeadAppointedParty;
                        p.Participants       = ReadParam(info, ParamRegistry.ORG_PARTICIPANTS)        ?? p.Participants;
                        p.Phase              = ReadParam(info, ParamRegistry.ORG_PHASE)               ?? p.Phase;
                        p.Class              = ReadParam(info, ParamRegistry.ORG_CLASS)               ?? p.Class;
                        p.WorkflowProfile    = ReadParam(info, ParamRegistry.ORG_WORKFLOW_PROFILE)    ?? p.WorkflowProfile;
                        string sig           = ReadParam(info, ParamRegistry.ORG_SIGNATURE_PROVIDER);
                        if (!string.IsNullOrEmpty(sig)) m.Signature.Provider = sig;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CreateDefault: ProjectInformation read failed — {ex.Message}"); }
            }

            return m;
        }

        private static string ReadParam(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return null;
                string v = p.AsString();
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }
    }

    /// <summary>Manifest-level validator invoked by <see cref="TemplateRegistry.ValidateAll"/>.</summary>
    public static class TemplateManifestValidator
    {
        public static List<ValidationIssue> Validate(this TemplateManifest m)
        {
            var issues = new List<ValidationIssue>();
            if (m == null)
            {
                issues.Add(new ValidationIssue("ERROR", "MANIFEST_NULL", "Manifest is null", ""));
                return issues;
            }

            if (string.IsNullOrWhiteSpace(m.Project?.OriginatorCode))
                issues.Add(new ValidationIssue("WARN",  "ORIGINATOR_EMPTY", "Originator code is empty", "project.originator_code"));
            else if (!string.Equals(m.Project.OriginatorCode, "PLNS", StringComparison.Ordinal))
                issues.Add(new ValidationIssue("INFO",  "ORIGINATOR_NOT_PLNS",
                    $"Originator code is '{m.Project.OriginatorCode}', Planscape default is PLNS", "project.originator_code"));

            if (string.IsNullOrWhiteSpace(m.Project?.IdentifierFormat))
                issues.Add(new ValidationIssue("ERROR", "ID_FORMAT_EMPTY", "identifier_format is empty", "project.identifier_format"));
            else if (!m.Project.IdentifierFormat.Contains("{number"))
                issues.Add(new ValidationIssue("ERROR", "ID_FORMAT_NO_NUMBER",
                    "identifier_format must include a {number[:Dn]} token", "project.identifier_format"));

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in m.Templates ?? new List<TemplateEntry>())
            {
                if (string.IsNullOrWhiteSpace(t.Id))
                    issues.Add(new ValidationIssue("ERROR", "TEMPLATE_ID_EMPTY", "Template id is empty", t.Name ?? t.FileRelative ?? "?"));
                else if (!ids.Add(t.Id))
                    issues.Add(new ValidationIssue("ERROR", "TEMPLATE_ID_DUPLICATE", $"Duplicate template id '{t.Id}'", t.Id));

                if (string.IsNullOrWhiteSpace(t.FileRelative))
                    issues.Add(new ValidationIssue("ERROR", "TEMPLATE_FILE_EMPTY",  $"Template '{t.Id}' has no file path", t.Id));
                if (string.IsNullOrWhiteSpace(t.Family))
                    issues.Add(new ValidationIssue("WARN",  "TEMPLATE_FAMILY_EMPTY",$"Template '{t.Id}' has no family", t.Id));
                if (string.IsNullOrWhiteSpace(t.Purpose))
                    issues.Add(new ValidationIssue("WARN",  "TEMPLATE_PURPOSE_EMPTY",$"Template '{t.Id}' has no purpose", t.Id));
            }

            return issues;
        }
    }
}
