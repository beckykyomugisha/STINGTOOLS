// ClashTabFactory.cs — factory for the BCC Clash tab. Called by BIMCoordinationCenter during tab build.
using System.Windows.Controls;

namespace StingTools.UI.Clash
{
    public static class ClashTabFactory
    {
        public static TabItem Create()
        {
            var tab = new TabItem { Header = "Clash" };
            tab.Content = new ClashTab();
            return tab;
        }
    }
}
