using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// A queued project + recipe pair to run as a multi-project batch. The
    /// dashboard exposes this on the Recipes tab — drop .rvt paths, pick
    /// a recipe, click Run. Actual execution is the dashboard's job (it
    /// needs ExternalEvent + OpenAndActivateDocument).
    /// </summary>
    public sealed class QueueEntry
    {
        public string ProjectPath { get; set; } = "";
        public string RecipeId { get; set; } = "";
        public string DisplayName => Path.GetFileNameWithoutExtension(ProjectPath ?? "");
        public DateTime QueuedUtc { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "Queued";  // Queued | Running | Done | Failed | Skipped
        public string Note { get; set; } = "";
    }

    public sealed class ProjectQueue
    {
        private readonly object _lock = new();
        private readonly List<QueueEntry> _entries = new();

        public IReadOnlyList<QueueEntry> Entries
        {
            get { lock (_lock) return _entries.ToList(); }
        }

        public int Count { get { lock (_lock) return _entries.Count; } }

        public void Enqueue(QueueEntry e)
        {
            if (e == null) return;
            if (string.IsNullOrEmpty(e.ProjectPath) || !File.Exists(e.ProjectPath))
            {
                e.Status = "Failed";
                e.Note = "Project file not found";
            }
            lock (_lock) _entries.Add(e);
        }

        public void EnqueueMany(IEnumerable<string> paths, string recipeId)
        {
            if (paths == null) return;
            foreach (var p in paths.Where(s => !string.IsNullOrEmpty(s)))
                Enqueue(new QueueEntry { ProjectPath = p, RecipeId = recipeId });
        }

        public QueueEntry Dequeue()
        {
            lock (_lock)
            {
                var next = _entries.FirstOrDefault(e => e.Status == "Queued");
                if (next != null) next.Status = "Running";
                return next;
            }
        }

        public void MarkDone(QueueEntry e, string note = "")
        {
            if (e == null) return;
            lock (_lock) { e.Status = "Done"; e.Note = note; }
        }

        public void MarkFailed(QueueEntry e, string note = "")
        {
            if (e == null) return;
            lock (_lock) { e.Status = "Failed"; e.Note = note; }
        }

        public void Clear() { lock (_lock) _entries.Clear(); }

        public void Remove(QueueEntry e) { lock (_lock) _entries.Remove(e); }
    }
}
