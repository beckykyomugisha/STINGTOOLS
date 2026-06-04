// Revit API CI Stubs — Autodesk.Revit.ApplicationServices
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace Autodesk.Revit.ApplicationServices
{
    public enum LanguageType { Unknown, English_USA, German, Spanish, French, Italian, Dutch, Chinese_Simplified, Chinese_Traditional, Japanese, Korean, Russian, Czech, Polish, Hungarian, Brazilian_Portuguese, English_GB }
    public enum ProductType { Unknown, Revit, MEP, Structure, Architecture }
    public enum ExportLayerMapping { Auto, FromFile }

    public abstract class Application
    {
        public string VersionName { get; }
        public string VersionNumber { get; }
        public string VersionBuild { get; }
        public LanguageType Language { get; }
        public string Username { get; }
        public string RecordingJournalFilename { get; }
        public bool IsWorksharing { get; }
        // CS0246: the real Application.OpenSharedParameterFile() returns
        // DefinitionFile (there is no 'SharedParameterFile' type). The plugin
        // calls the no-arg overload and assigns the result to a DefinitionFile.
        public DefinitionFile OpenSharedParameterFile() => throw new NotImplementedException();
        public string SharedParametersFilename { get; set; }
        public DefinitionFile OpenSharedParameterFile(string filename) => throw new NotImplementedException();
        public FormatOptions GetUnits(UnitType unitType) => throw new NotImplementedException();
        public event EventHandler<DocumentOpenedEventArgs> DocumentOpened;
        public event EventHandler<DocumentClosingEventArgs> DocumentClosing;
        public event EventHandler<DocumentSavedEventArgs> DocumentSaved;
        public event EventHandler<DocumentSynchronizedWithCentralEventArgs> DocumentSynchronizedWithCentral;
    }

    public abstract class ControlledApplication
    {
        public string VersionName { get; }
        public string VersionNumber { get; }
        public LanguageType Language { get; }
        // CS0234: 'Autodesk.Revit.UI' is the RevitAPIUI assembly, which RevitAPI
        // does NOT (and must not) reference. The real ControlledApplication has no
        // ribbon API — ribbon creation is on UIControlledApplication (UI assembly,
        // used by the plugin's EnsureRibbonTab). These bogus methods are removed.
        public event EventHandler<DocumentOpenedEventArgs> DocumentOpened;
        public event EventHandler<DocumentClosingEventArgs> DocumentClosing;
    }
}
