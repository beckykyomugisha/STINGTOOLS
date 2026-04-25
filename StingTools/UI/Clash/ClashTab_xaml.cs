// ClashTab_xaml.cs — minimal Clash tab code-behind. Registered into the BCC tab host.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using StingTools.Core;
using StingTools.Core.Clash;

namespace StingTools.UI.Clash
{
    public sealed class ClashTab : UserControl
    {
        public ObservableCollection<ClashRowViewModel> Rows { get; } = new ObservableCollection<ClashRowViewModel>();
        private DataGrid _grid;

        public ClashTab()
        {
            _grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true };
            _grid.ItemsSource = Rows;
            Content = _grid;
        }

        public void Populate(ClashRunRecord run)
        {
            Rows.Clear();
            if (run?.Clashes == null) return;
            foreach (var c in run.Clashes)
            {
                Rows.Add(new ClashRowViewModel
                {
                    Id = c.Id,
                    GroupId = c.GroupId,
                    ElementA = $"{c.ElementA?.Category}:{c.ElementA?.ElementId}",
                    ElementB = $"{c.ElementB?.Category}:{c.ElementB?.ElementId}",
                    Severity = c.Severity,
                    State = c.State,
                    Assignee = "",
                    DueDate = null,
                    ResolutionHint = c.ResolutionHint
                });
            }
        }
    }
}
