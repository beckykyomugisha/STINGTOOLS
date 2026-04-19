// ClashRowViewModel.cs — one row in the BCC Clash tab grid.
using System;
using System.ComponentModel;

namespace StingTools.UI.Clash
{
    public sealed class ClashRowViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string GroupId { get; set; }
        public string ElementA { get; set; }
        public string ElementB { get; set; }
        public string Severity { get; set; }
        public string State { get; set; }
        public string Assignee { get; set; }
        public DateTime? DueDate { get; set; }
        public string ResolutionHint { get; set; }

        // Part of INotifyPropertyChanged contract — required even when this row
        // model is treated as immutable (never raised internally). WPF data
        // binding still relies on the event's presence.
#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067
    }
}
