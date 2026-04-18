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

        // INotifyPropertyChanged contract. The DataGrid rebinds on
        // Rows.Clear/Add so per-property change notifications aren't needed
        // today — kept for forward-compat with inline editing, which will
        // raise this event. #pragma suppresses CS0067 (unused event).
#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067
    }
}
