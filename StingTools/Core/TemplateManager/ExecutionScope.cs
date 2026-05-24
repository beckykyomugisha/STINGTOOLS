using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Scope at which a Template Manager op runs. Each op picks the values
    /// it actually supports via SupportedScopes.
    /// </summary>
    public enum ExecutionScopeKind
    {
        ActiveView,
        Selection,
        Project,
        Linked,
        Sheets
    }

    /// <summary>
    /// User-chosen scope for a Template Manager op. Resolved into an actual
    /// collection of Revit elements by Resolve(doc) so each command can read
    /// "what should I touch?" in one line instead of re-implementing the
    /// view/selection/project plumbing 30 times.
    /// </summary>
    public sealed class ExecutionScope
    {
        public ExecutionScopeKind Kind { get; set; } = ExecutionScopeKind.Project;
        public bool IncludeLinked { get; set; } = false;
        public List<ElementId> SelectedIds { get; set; } = new();
        public string DisciplineFilter { get; set; } = "";  // optional discipline code
        public string Note { get; set; } = "";

        public string Label =>
            (Kind switch
            {
                ExecutionScopeKind.ActiveView => "Active view",
                ExecutionScopeKind.Selection => $"Selection ({SelectedIds?.Count ?? 0})",
                ExecutionScopeKind.Project => "Project",
                ExecutionScopeKind.Linked => "Project + linked",
                ExecutionScopeKind.Sheets => "Sheets only",
                _ => Kind.ToString()
            }) + (string.IsNullOrEmpty(DisciplineFilter) ? "" : $"  ·  {DisciplineFilter}");

        /// <summary>
        /// Resolve to the actual element id set for the given category filter.
        /// Returns empty when scope kind isn't applicable.
        /// </summary>
        public IList<ElementId> ResolveIds(Document doc, ElementFilter filter = null)
        {
            if (doc == null) return Array.Empty<ElementId>();
            try
            {
                FilteredElementCollector coll = null;
                switch (Kind)
                {
                    case ExecutionScopeKind.ActiveView:
                        if (doc.ActiveView == null) return Array.Empty<ElementId>();
                        coll = new FilteredElementCollector(doc, doc.ActiveView.Id);
                        break;
                    case ExecutionScopeKind.Selection:
                        return SelectedIds?.ToList() ?? new List<ElementId>();
                    case ExecutionScopeKind.Sheets:
                        coll = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet));
                        break;
                    case ExecutionScopeKind.Project:
                    case ExecutionScopeKind.Linked:
                    default:
                        coll = new FilteredElementCollector(doc);
                        break;
                }
                if (filter != null && coll != null) coll = coll.WherePasses(filter);
                return coll?.ToElementIds()?.ToList() ?? new List<ElementId>();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ExecutionScope.ResolveIds: {ex.Message}");
                return Array.Empty<ElementId>();
            }
        }

        /// <summary>
        /// Iterate the linked documents (when IncludeLinked or Kind == Linked).
        /// Yields the host doc first, then every loaded RevitLinkInstance doc.
        /// </summary>
        public IEnumerable<Document> ResolveDocuments(Document host)
        {
            if (host == null) yield break;
            yield return host;
            if (!IncludeLinked && Kind != ExecutionScopeKind.Linked) yield break;

            FilteredElementCollector linkColl = null;
            try { linkColl = new FilteredElementCollector(host).OfClass(typeof(RevitLinkInstance)); }
            catch { yield break; }
            if (linkColl == null) yield break;
            foreach (RevitLinkInstance link in linkColl)
            {
                Document linked = null;
                try { linked = link.GetLinkDocument(); }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"ExecutionScope link: {ex.Message}"); }
                if (linked != null) yield return linked;
            }
        }
    }
}
