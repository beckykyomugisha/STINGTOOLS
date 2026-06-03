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
        public DefinitionFile OpenSharedParameterFile() => throw new NotImplementedException();
        public string SharedParametersFilename { get; set; }
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
        // Ribbon creation lives on UIControlledApplication (RevitAPIUI); the real
        // ControlledApplication has no CreateRibbon* members and RevitAPI cannot
        // reference Autodesk.Revit.UI types (RevitAPIUI depends on RevitAPI).
        public event EventHandler<DocumentOpenedEventArgs> DocumentOpened;
        public event EventHandler<DocumentClosingEventArgs> DocumentClosing;
    }
}
