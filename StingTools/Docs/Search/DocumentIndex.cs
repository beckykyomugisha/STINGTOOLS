// DocumentIndex.cs — template engine v1.1 (S17).
//
// Lucene.NET 4.8 wrapper. Reads document_register.json + optional
// deliverables.json; builds an index either in-memory (small projects) or on
// disk under _BIM_COORD/search_index/ for large projects.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Newtonsoft.Json.Linq;
using StingTools.Core;

// Lucene.Net and Revit / BCL share type names — explicit aliases avoid
// CS0104 ambiguity without fully-qualifying them at every use site.
using LuceneDocument  = Lucene.Net.Documents.Document;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace Planscape.Docs.Search
{
    public class IndexedDoc
    {
        public string Id { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
        public string Direction { get; set; }      // IN/OUT
        public string WorkflowState { get; set; }
        public string Date { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class SearchFilters
    {
        public string Type { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
        public string Direction { get; set; }
        public string WorkflowState { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class DocumentIndex : IDisposable
    {
        private const LuceneVersion Version = LuceneVersion.LUCENE_48;
        private readonly LuceneDirectory _dir;
        private readonly StandardAnalyzer _analyzer;
        private DirectoryReader _reader;

        private DocumentIndex(LuceneDirectory d)
        {
            _dir = d;
            _analyzer = new StandardAnalyzer(Version);
        }

        public static DocumentIndex Build(Autodesk.Revit.DB.Document revitDoc)
        {
            string root = ResolveProjectRoot(revitDoc);
            string indexDir = Path.Combine(root, "_BIM_COORD", "search_index");
            System.IO.Directory.CreateDirectory(indexDir);
            var dir = FSDirectory.Open(indexDir);

            var idx = new DocumentIndex(dir);
            idx.Rebuild(revitDoc);
            return idx;
        }

        public void Rebuild(Autodesk.Revit.DB.Document revitDoc)
        {
            var cfg = new IndexWriterConfig(Version, _analyzer) { OpenMode = OpenMode.CREATE };
            using (var writer = new IndexWriter(_dir, cfg))
            {
                foreach (var d in LoadAllRegistered(revitDoc))
                {
                    var ld = ToLuceneDoc(d);
                    writer.AddDocument(ld);
                }
                writer.Commit();
            }
            _reader = DirectoryReader.Open(_dir);
        }

        public void UpdateOne(IndexedDoc d)
        {
            if (d == null || string.IsNullOrEmpty(d.Id)) return;
            var cfg = new IndexWriterConfig(Version, _analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND };
            using (var writer = new IndexWriter(_dir, cfg))
            {
                writer.UpdateDocument(new Term("id", d.Id), ToLuceneDoc(d));
                writer.Commit();
            }
            _reader?.Dispose();
            _reader = DirectoryReader.Open(_dir);
        }

        public List<IndexedDoc> Search(string query, SearchFilters filters, int maxResults = 50)
        {
            if (_reader == null) _reader = DirectoryReader.Open(_dir);
            var searcher = new IndexSearcher(_reader);

            var parser = new QueryParser(Version, "fulltext", _analyzer);
            Query q;
            if (string.IsNullOrWhiteSpace(query) || query == "*" || query == "*:*")
                q = new MatchAllDocsQuery();
            else
            {
                try { q = parser.Parse(query); }
                catch { q = new TermQuery(new Term("fulltext", query.ToLowerInvariant())); }
            }

            var booleanQ = new BooleanQuery { { q, Occur.MUST } };
            AddFilter(booleanQ, "type",           filters?.Type);
            AddFilter(booleanQ, "role",           filters?.Role);
            AddFilter(booleanQ, "status",         filters?.Status);
            AddFilter(booleanQ, "direction",      filters?.Direction);
            AddFilter(booleanQ, "workflow_state", filters?.WorkflowState);
            if (filters?.Tags != null)
                foreach (var t in filters.Tags)
                    AddFilter(booleanQ, "tag", t);

            var hits = searcher.Search(booleanQ, maxResults).ScoreDocs;
            var list = new List<IndexedDoc>(hits.Length);
            foreach (var h in hits) list.Add(FromLuceneDoc(searcher.Doc(h.Doc)));

            if (filters?.DateFrom != null || filters?.DateTo != null)
                list = list.Where(d =>
                {
                    if (!DateTime.TryParse(d.Date, out var dt)) return true;
                    if (filters.DateFrom != null && dt < filters.DateFrom) return false;
                    if (filters.DateTo   != null && dt > filters.DateTo)   return false;
                    return true;
                }).ToList();

            return list;
        }

        private static void AddFilter(BooleanQuery booleanQ, string field, string val)
        {
            if (string.IsNullOrEmpty(val)) return;
            booleanQ.Add(new TermQuery(new Term(field, val.ToLowerInvariant())), Occur.MUST);
        }

        private static LuceneDocument ToLuceneDoc(IndexedDoc d)
        {
            var ld = new LuceneDocument();
            ld.Add(new StringField("id",            d.Id              ?? "", Field.Store.YES));
            ld.Add(new TextField  ("number",        d.Number          ?? "", Field.Store.YES));
            ld.Add(new TextField  ("title",         d.Title           ?? "", Field.Store.YES));
            ld.Add(new StringField("type",          (d.Type           ?? "").ToLowerInvariant(), Field.Store.YES));
            ld.Add(new StringField("role",          (d.Role           ?? "").ToLowerInvariant(), Field.Store.YES));
            ld.Add(new StringField("status",        (d.Status         ?? "").ToLowerInvariant(), Field.Store.YES));
            ld.Add(new StringField("direction",     (d.Direction      ?? "").ToLowerInvariant(), Field.Store.YES));
            ld.Add(new StringField("workflow_state",(d.WorkflowState  ?? "").ToLowerInvariant(), Field.Store.YES));
            ld.Add(new StringField("date",          d.Date            ?? "", Field.Store.YES));
            if (d.Tags != null) foreach (var t in d.Tags)
                ld.Add(new StringField("tag", t.ToLowerInvariant(), Field.Store.YES));
            ld.Add(new TextField("fulltext",
                $"{d.Number} {d.Title} {d.Type} {d.Role} {d.Status} {string.Join(' ', d.Tags ?? new List<string>())}",
                Field.Store.NO));
            return ld;
        }

        private static IndexedDoc FromLuceneDoc(LuceneDocument ld)
        {
            var d = new IndexedDoc
            {
                Id = ld.Get("id"),
                Number = ld.Get("number"),
                Title = ld.Get("title"),
                Type = ld.Get("type"),
                Role = ld.Get("role"),
                Status = ld.Get("status"),
                Direction = ld.Get("direction"),
                WorkflowState = ld.Get("workflow_state"),
                Date = ld.Get("date")
            };
            foreach (var f in ld.GetFields("tag")) d.Tags.Add(f.GetStringValue());
            return d;
        }

        private static IEnumerable<IndexedDoc> LoadAllRegistered(Autodesk.Revit.DB.Document revitDoc)
        {
            string root = ResolveProjectRoot(revitDoc);
            foreach (string fileName in new[] { "document_register.json", "deliverables.json" })
            {
                string path = Path.Combine(root, "_BIM_COORD", fileName);
                if (!File.Exists(path)) continue;
                JArray arr;
                try { arr = JArray.Parse(File.ReadAllText(path)); }
                catch (Exception ex) { StingLog.Warn($"DocumentIndex: {fileName} parse failed: {ex.Message}"); continue; }

                foreach (var row in arr.OfType<JObject>())
                {
                    var d = new IndexedDoc
                    {
                        Id       = row.Value<string>("DocNumber") ?? row.Value<string>("Code") ?? row.Value<string>("id"),
                        Number   = row.Value<string>("DocNumber") ?? row.Value<string>("number") ?? row.Value<string>("Code"),
                        Title    = row.Value<string>("Name")     ?? row.Value<string>("title")    ?? row.Value<string>("subject"),
                        Type     = row.Value<string>("Type")     ?? row.Value<string>("type"),
                        Role     = row.Value<string>("RoleCode") ?? row.Value<string>("role"),
                        Status   = row.Value<string>("Status")   ?? row.Value<string>("status"),
                        WorkflowState = row.Value<string>("WorkflowState") ?? row.Value<string>("workflow_state"),
                        Direction = row.Value<string>("direction") ?? "OUT",
                        Date     = row.Value<string>("DueDate")  ?? row.Value<string>("issue_date") ?? row.Value<string>("Timestamp")
                    };
                    var tags = row["Tags"] as JArray ?? row["tags"] as JArray;
                    if (tags != null) foreach (var t in tags) d.Tags.Add(t?.ToString() ?? "");
                    if (!string.IsNullOrEmpty(d.Id)) yield return d;
                }
            }
        }

        private static string ResolveProjectRoot(Autodesk.Revit.DB.Document doc)
        {
            // Folder consolidation: nest "_BIM_COORD" inside the unified
            // project root's _data folder rather than as a sibling of the .rvt.
            try
            {
                string consolidated = StingTools.Core.ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated)) return consolidated;
            }
            catch { /* fall through to legacy lookup */ }
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _analyzer?.Dispose();
            _dir?.Dispose();
        }
    }
}
