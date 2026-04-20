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

        // INotifyPropertyChanged contract event. Current DataGrid binding is
        // one-way (view is rebuilt on every tick) so no property mutations
        // raise it yet — keep the event so downstream two-way bindings can
        // subscribe later without breaking the interface.
#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067
    }
}
