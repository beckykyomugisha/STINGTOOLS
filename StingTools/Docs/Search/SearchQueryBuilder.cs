// SearchQueryBuilder.cs — template engine v1.1 (S17).
//
// Small fluent builder + saved-search persistence. Search queries are strings
// passed to DocumentIndex.Search; the builder assembles facet constraints
// and produces both a human-friendly expression and a SearchFilters
// structure.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace Planscape.Docs.Search
{
    public class SearchQueryBuilder
    {
        private string _freeText;
        private readonly SearchFilters _filters = new SearchFilters();

        public SearchQueryBuilder WithText(string text)           { _freeText = text; return this; }
        public SearchQueryBuilder OfType(string type)             { _filters.Type = type; return this; }
        public SearchQueryBuilder OfRole(string role)             { _filters.Role = role; return this; }
        public SearchQueryBuilder WithStatus(string status)       { _filters.Status = status; return this; }
        public SearchQueryBuilder WithDirection(string dir)       { _filters.Direction = dir; return this; }
        public SearchQueryBuilder WithWorkflowState(string s)     { _filters.WorkflowState = s; return this; }
        public SearchQueryBuilder Between(DateTime? from, DateTime? to)
        { _filters.DateFrom = from; _filters.DateTo = to; return this; }
        public SearchQueryBuilder WithTag(string tag)
        { if (!string.IsNullOrEmpty(tag)) _filters.Tags.Add(tag); return this; }

        public string FreeText => _freeText ?? "";
        public SearchFilters Filters => _filters;

        public string AsExpression()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_freeText)) sb.Append(_freeText);
            AppendClause(sb, "type",           _filters.Type);
            AppendClause(sb, "role",           _filters.Role);
            AppendClause(sb, "status",         _filters.Status);
            AppendClause(sb, "direction",      _filters.Direction);
            AppendClause(sb, "workflow_state", _filters.WorkflowState);
            if (_filters.Tags != null)
                foreach (var t in _filters.Tags) AppendClause(sb, "tag", t);
            return sb.Length == 0 ? "*:*" : sb.ToString();
        }

        private static void AppendClause(StringBuilder sb, string field, string val)
        {
            if (string.IsNullOrEmpty(val)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"{field}:{val.ToLowerInvariant()}");
        }
    }

    public class SavedSearch
    {
        [JsonProperty("id")]       public string Id { get; set; }
        [JsonProperty("name")]     public string Name { get; set; }
        [JsonProperty("owner")]    public string Owner { get; set; }
        [JsonProperty("free_text")]public string FreeText { get; set; }
        [JsonProperty("filters")]  public SearchFilters Filters { get; set; } = new SearchFilters();
    }

    public static class SavedSearchStore
    {
        public static List<SavedSearch> LoadAll(Document doc)
        {
            string path = StorePath(doc);
            if (!File.Exists(path)) return new List<SavedSearch>();
            try
            {
                // S3.6.2 — version gate before deserialise.
                StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                    path, "planscape.saved-searches",
                    StingTools.Core.PluginSchemaVersion.CurrentSavedSearches);
                return JsonConvert.DeserializeObject<List<SavedSearch>>(File.ReadAllText(path)) ?? new List<SavedSearch>();
            }
            catch (Exception ex) { StingLog.Warn($"SavedSearchStore: load failed: {ex.Message}"); return new List<SavedSearch>(); }
        }

        public static void Save(Document doc, List<SavedSearch> list)
        {
            string path = StorePath(doc);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(list ?? new List<SavedSearch>(), Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static string StorePath(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "saved_searches.json");
        }

        private static string ResolveProjectRoot(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
