// Revit API CI Stubs — Autodesk.Revit.UI
using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.ApplicationServices;

namespace Autodesk.Revit.UI
{
    // ── Result enum ───────────────────────────────────────────────────────────
    public enum Result { Succeeded, Cancelled, Failed }

    // ── Core command interfaces ────────────────────────────────────────────────
    public interface IExternalCommand
    {
        Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements);
    }

    public interface IExternalApplication
    {
        Result OnStartup(UIControlledApplication application);
        Result OnShutdown(UIControlledApplication application);
    }

    public interface IExternalEventHandler
    {
        void Execute(UIApplication app);
        string GetName();
    }

    public class ExternalEvent
    {
        public static ExternalEvent Create(IExternalEventHandler handler) => throw new NotImplementedException();
        public ExternalEventRequest Raise() => throw new NotImplementedException();
        public bool IsPending { get; }
    }
    public enum ExternalEventRequest { Accepted, Pending, Timeout, Denied }

    // ── CommandData ────────────────────────────────────────────────────────────
    public class ExternalCommandData
    {
        public UIApplication Application { get; }
        public View View { get; }
        public Autodesk.Revit.DB.JournalData JournalData { get; }
        public bool IsReadOnlyMode { get; }
    }

    // ── UIApplication ──────────────────────────────────────────────────────────
    public class UIApplication
    {
        public Application Application { get; }
        public UIDocument ActiveUIDocument { get; }
        public DockablePaneId MainWindowHandle { get; }
        public IntPtr MainWindowHandle_Ptr { get; }
        public Autodesk.Revit.ApplicationServices.Application Application2 { get; }
        public UIDocument OpenAndActivateDocument(string filePath) => throw new NotImplementedException();
        public void LoadAddIn(string fileName) => throw new NotImplementedException();
        public RibbonPanel GetRibbonPanel(string panelName) => throw new NotImplementedException();
        public IList<RibbonPanel> GetRibbonPanels() => throw new NotImplementedException();
        public IList<RibbonPanel> GetRibbonPanels(string tabName) => throw new NotImplementedException();
        public void OpenAndActivateDocument(ModelPath modelPath, OpenOptions openOptions, bool detachAndPreserveWorksets) => throw new NotImplementedException();
        public DockablePane GetDockablePane(DockablePaneId id) => throw new NotImplementedException();
        public event EventHandler<DB.Events.IdlingEventArgs> Idling;
        public event EventHandler<DB.Events.ViewActivatedEventArgs> ViewActivated;
        public event EventHandler<DB.Events.DocumentOpenedEventArgs> DocumentOpened;
        public event EventHandler<DB.Events.DocumentClosingEventArgs> DocumentClosing;
        public event EventHandler<DB.Events.DocumentChangedEventArgs> DocumentChanged;
    }

    public class UIControlledApplication
    {
        public ControlledApplication ControlledApplication { get; }
        public void CreateRibbonTab(string tabName) => throw new NotImplementedException();
        public RibbonPanel CreateRibbonPanel(string panelName) => throw new NotImplementedException();
        public RibbonPanel CreateRibbonPanel(string tabName, string panelName) => throw new NotImplementedException();
        public IList<RibbonPanel> GetRibbonPanels(string tabName) => throw new NotImplementedException();
        public void RegisterDockablePane(DockablePaneId id, string name, IDockablePaneProvider provider) => throw new NotImplementedException();
        public void RegisterDockablePane(DockablePaneId id, string name, IDockablePaneProvider provider, bool isVisibleByDefault) => throw new NotImplementedException();
        public event EventHandler<DB.Events.IdlingEventArgs> Idling;
        public event EventHandler<DB.Events.ViewActivatedEventArgs> ViewActivated;
        public event EventHandler<DB.Events.DocumentOpenedEventArgs> DocumentOpened;
        public event EventHandler<DB.Events.DocumentCreatedEventArgs> DocumentCreated;
        public event EventHandler<DB.Events.DocumentClosingEventArgs> DocumentClosing;
        public event EventHandler<DB.Events.DocumentClosedEventArgs> DocumentClosed;
        public event EventHandler<DB.Events.DocumentChangedEventArgs> DocumentChanged;
        public event EventHandler<DB.Events.FailuresProcessingEventArgs> FailuresProcessing;
    }

    // ── UIDocument ────────────────────────────────────────────────────────────
    public class UIDocument
    {
        public UIDocument(Document doc) { }
        public Document Document { get; }
        public UIApplication UIApplication { get; }
        public View ActiveView { get; set; }
        public Selection.Selection Selection { get; }
        public void ShowElements(Element element) => throw new NotImplementedException();
        public void ShowElements(ICollection<ElementId> elementIds) => throw new NotImplementedException();
        public void RefreshActiveView() => throw new NotImplementedException();
        public void RequestViewChange(View view) => throw new NotImplementedException();
        public void ActiveView_Set(View view) => throw new NotImplementedException();
        public bool IsValidObject { get; }
        public void PromptForFamilyInstancePlacement(FamilySymbol familySymbol) => throw new NotImplementedException();
    }

    // ── Dockable Pane ─────────────────────────────────────────────────────────
    public class DockablePaneId
    {
        public DockablePaneId(System.Guid guid) { }
    }
    public interface IDockablePaneProvider
    {
        void SetupDockablePane(DockablePaneProviderData data);
    }
    public class DockablePaneProviderData
    {
        public System.Windows.FrameworkElement FrameworkElement { get; set; }
        public DockablePaneState InitialState { get; set; }
        public bool VisibleByDefault { get; set; }
        public bool DefaultVisibility { get; set; }
        public bool EditorInteraction { get; set; }
    }
    public class DockablePaneState
    {
        public DockablePaneTabbed TabBehind { get; set; }
        public DockPosition DockPosition { get; set; }
    }
    public class DockablePaneTabbed
    {
        public DockablePaneTabbed(DockablePaneId dockablePane) { }
    }
    public enum DockPosition { Tabbed, Top, Bottom, Left, Right, Floating, None }
    public class DockablePane
    {
        public void Show() => throw new NotImplementedException();
        public void Hide() => throw new NotImplementedException();
        public bool IsShown() => throw new NotImplementedException();
    }

    // ── Ribbon ────────────────────────────────────────────────────────────────
    public abstract class RibbonItem
    {
        public string Name { get; }
        public string ItemText { get; set; }
        public string ToolTip { get; set; }
        public string LongDescription { get; set; }
        public BitmapImage LargeImage { get; set; }
        public BitmapImage Image { get; set; }
        public bool Enabled { get; set; }
        public bool Visible { get; set; }
        public RibbonItemData ItemData { get; }
    }
    public abstract class RibbonItemData
    {
        protected RibbonItemData(string name) { }
        public string Name { get; }
        public string Text { get; set; }
        public string ToolTip { get; set; }
        public string LongDescription { get; set; }
        public BitmapImage LargeImage { get; set; }
        public BitmapImage Image { get; set; }
    }

    public class PushButton : RibbonItem
    {
        public PushButtonData ItemData2 { get; }
        public bool AvailabilityClassName { get; set; }
    }
    public class PushButtonData : RibbonItemData
    {
        public PushButtonData(string name, string text, string assemblyName, string className) : base(name) { }
        public string AvailabilityClassName { get; set; }
    }

    public class PulldownButton : RibbonItem
    {
        public PushButton AddPushButton(PushButtonData pushButtonData) => throw new NotImplementedException();
        public IList<RibbonItem> GetItems() => throw new NotImplementedException();
    }
    public class PulldownButtonData : RibbonItemData
    {
        public PulldownButtonData(string name, string text) : base(name) { }
    }

    public class SplitButton : RibbonItem
    {
        public PushButton AddPushButton(PushButtonData data) => throw new NotImplementedException();
        public IList<PushButton> GetItems() => throw new NotImplementedException();
        public bool IsSynchronizedWithCurrentItem { get; set; }
    }
    public class SplitButtonData : RibbonItemData
    {
        public SplitButtonData(string name, string text) : base(name) { }
    }

    public class ToggleButton : RibbonItem { }
    public class ToggleButtonData : RibbonItemData { public ToggleButtonData(string name, string text, string assemblyName, string className) : base(name) { } }

    public class ComboBox : RibbonItem
    {
        public ComboBoxMember AddItem(ComboBoxMemberData member) => throw new NotImplementedException();
        public ComboBoxMember CurrentItem { get; }
        public IList<ComboBoxMember> GetItems() => throw new NotImplementedException();
    }
    public class ComboBoxData : RibbonItemData { public ComboBoxData(string name) : base(name) { } }
    public class ComboBoxMember : RibbonItem { }
    public class ComboBoxMemberData : RibbonItemData { public ComboBoxMemberData(string name, string text) : base(name) { } }

    public class TextBox : RibbonItem
    {
        public string Value { get; set; }
        public string PromptText { get; set; }
        public int Width { get; set; }
        public event EventHandler<TextBoxEnterPressedEventArgs> EnterPressed;
    }
    public class TextBoxData : RibbonItemData { public TextBoxData(string name) : base(name) { } public string PromptText { get; set; } public int Width { get; set; } }
    public class TextBoxEnterPressedEventArgs : EventArgs { public TextBox TextBox { get; } }

    public class Separator : RibbonItem { }

    public class RibbonPanel
    {
        public string Name { get; }
        public PushButton AddItem(RibbonItemData data) => throw new NotImplementedException();
        public IList<RibbonItem> AddStackedItems(RibbonItemData data1, RibbonItemData data2) => throw new NotImplementedException();
        public IList<RibbonItem> AddStackedItems(RibbonItemData data1, RibbonItemData data2, RibbonItemData data3) => throw new NotImplementedException();
        public SplitButton AddItem_Split(SplitButtonData data) => throw new NotImplementedException();
        public PulldownButton AddItem_Pulldown(PulldownButtonData data) => throw new NotImplementedException();
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
        public IList<RibbonItem> GetItems() => throw new NotImplementedException();
    }

    // ── TaskDialog ────────────────────────────────────────────────────────────
    public class TaskDialog
    {
        public TaskDialog(string title) { }
        public string MainInstruction { get; set; }
        public string MainContent { get; set; }
        public string ExpandedContent { get; set; }
        public string FooterText { get; set; }
        public string VerificationText { get; set; }
        public bool WasVerificationChecked { get; }
        public TaskDialogCommonButtons CommonButtons { get; set; }
        public TaskDialogResult DefaultButton { get; set; }
        public bool AllowCancellation { get; set; }
        public bool TitleAutoPrefix { get; set; }
        public void AddCommandLink(TaskDialogCommandLinkId id, string text) => throw new NotImplementedException();
        public void AddCommandLink(TaskDialogCommandLinkId id, string text, string description) => throw new NotImplementedException();
        public TaskDialogResult Show() => throw new NotImplementedException();
        public static void Show(string title, string content) => throw new NotImplementedException();
        public static TaskDialogResult Show(string title, string content, TaskDialogCommonButtons buttons) => throw new NotImplementedException();
        public static TaskDialogResult Show(string title, string content, TaskDialogCommonButtons buttons, TaskDialogResult defaultButton) => throw new NotImplementedException();
    }

    public enum TaskDialogCommonButtons
    {
        None = 0, Close = 1, Ok = 2, Yes = 4, No = 8, Retry = 16, Cancel = 32
    }
    public enum TaskDialogCommandLinkId { CommandLink1 = 1, CommandLink2 = 2, CommandLink3 = 3, CommandLink4 = 4 }
    public enum TaskDialogResult
    {
        None = 0, Ok = 2, Yes = 4, No = 8, Retry = 16, Close = 1, Cancel = 32,
        CommandLink1 = 1001, CommandLink2 = 1002, CommandLink3 = 1003, CommandLink4 = 1004
    }

    // ── ModelPath ─────────────────────────────────────────────────────────────
    public abstract class ModelPath { public string CentralServerPath { get; } }
    public class FilePath : ModelPath { public FilePath(string path) { } public string Path { get; } }
    public class ModelPathUtils { public static ModelPath ConvertUserVisiblePathToModelPath(string userVisible) => throw new NotImplementedException(); public static string ConvertModelPathToUserVisiblePath(ModelPath path) => throw new NotImplementedException(); }
    public class OpenOptions { public OpenOptions() { } public bool DetachFromCentralOption { get; set; } public bool OpenForeignOptionAll { get; set; } }
}

namespace Autodesk.Revit.DB
{
    // JournalData lives in DB namespace
    public class JournalData : System.Collections.IEnumerable
    {
        public System.Collections.IEnumerator GetEnumerator() => throw new NotImplementedException();
        public string get_Item(string key) => throw new NotImplementedException();
        public bool HasKey(string key) => throw new NotImplementedException();
        public int Length { get; }
    }
}

namespace Autodesk.Revit.UI.Selection
{
    public class Selection
    {
        public ICollection<ElementId> GetElementIds() => throw new NotImplementedException();
        public void SetElementIds(ICollection<ElementId> idSet) => throw new NotImplementedException();
        public Reference PickObject(ObjectType objectType) => throw new NotImplementedException();
        public Reference PickObject(ObjectType objectType, string statusPrompt) => throw new NotImplementedException();
        public Reference PickObject(ObjectType objectType, ISelectionFilter selectionFilter, string statusPrompt) => throw new NotImplementedException();
        public IList<Reference> PickObjects(ObjectType objectType) => throw new NotImplementedException();
        public IList<Reference> PickObjects(ObjectType objectType, string statusPrompt) => throw new NotImplementedException();
        public IList<Reference> PickObjects(ObjectType objectType, ISelectionFilter selectionFilter, string statusPrompt) => throw new NotImplementedException();
        public IList<Reference> PickObjects(ObjectType objectType, ISelectionFilter selectionFilter, string statusPrompt, IList<Reference> pPicked) => throw new NotImplementedException();
        public XY PickPoint() => throw new NotImplementedException();
        public XY PickPoint(string statusPrompt) => throw new NotImplementedException();
        public XYZ PickPoint2() => throw new NotImplementedException();
        public ElementId PickElementsByRectangle() => throw new NotImplementedException();
        public IList<Element> PickElementsByRectangle(string statusPrompt) => throw new NotImplementedException();
    }
    public class XY { public double X { get; } public double Y { get; } }
    public enum ObjectType { Nothing, Element, Edge, Face, Curve, PointOnElement, Element_WithLinkedElements, LinkedElement, Subelement, PointAtFace }
    public interface ISelectionFilter { bool AllowElement(Element element); bool AllowReference(Reference refer, XYZ point); }
}

namespace Autodesk.Revit.UI.Events
{
    public class ViewActivatedEventArgs : Autodesk.Revit.DB.Events.ViewActivatedEventArgs { }
    public class IdlingEventArgs : Autodesk.Revit.DB.Events.IdlingEventArgs { }
}
