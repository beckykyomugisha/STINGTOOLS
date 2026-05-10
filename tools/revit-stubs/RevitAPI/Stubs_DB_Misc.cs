// Revit API CI Stubs — Autodesk.Revit.DB misc (ExtensibleStorage, Events, Analysis, ExternalService)
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Autodesk.Revit.DB.ExtensibleStorage
{
    public class Schema
    {
        public System.Guid GUID { get; }
        public string SchemaName { get; }
        public static Schema Lookup(System.Guid schemaId) => throw new NotImplementedException();
        public Field GetField(string name) => throw new NotImplementedException();
        public IList<Field> ListFields() => throw new NotImplementedException();
        public bool IsValid() => throw new NotImplementedException();
    }

    public class SchemaBuilder
    {
        public SchemaBuilder(System.Guid schemaGuid) { }
        public SchemaBuilder SetSchemaName(string name) => this;
        public SchemaBuilder SetReadAccessLevel(AccessLevel level) => this;
        public SchemaBuilder SetWriteAccessLevel(AccessLevel level) => this;
        public SchemaBuilder SetVendorId(string vendorId) => this;
        public SchemaBuilder SetApplicationGUID(System.Guid guid) => this;
        public FieldBuilder AddSimpleField(string name, Type type) => throw new NotImplementedException();
        public FieldBuilder AddSimpleField(string name, Type type, ForgeTypeId specId) => throw new NotImplementedException();
        public FieldBuilder AddArrayField(string name, Type type) => throw new NotImplementedException();
        public FieldBuilder AddMapField(string name, Type keyType, Type valueType) => throw new NotImplementedException();
        public Schema Finish() => throw new NotImplementedException();
    }

    public class FieldBuilder
    {
        public FieldBuilder SetSpec(ForgeTypeId spec) => this;
        public FieldBuilder SetDocumentation(string doc) => this;
        public FieldBuilder SetUnitType(ForgeTypeId unitType) => this;
    }

    public class Field
    {
        public string FieldName { get; }
        public Type ValueType { get; }
        public bool IsArray() => throw new NotImplementedException();
        public bool IsMap() => throw new NotImplementedException();
        public bool IsSimple() => throw new NotImplementedException();
    }

    public class Entity
    {
        public Entity() { }
        public Entity(Schema schema) { }
        public Schema Schema { get; }
        public bool IsValid() => throw new NotImplementedException();
        public T Get<T>(string fieldName) => throw new NotImplementedException();
        public T Get<T>(string fieldName, ForgeTypeId unitType) => throw new NotImplementedException();
        public T Get<T>(Field field) => throw new NotImplementedException();
        public void Set<T>(string fieldName, T value) => throw new NotImplementedException();
        public void Set<T>(string fieldName, T value, ForgeTypeId unitType) => throw new NotImplementedException();
        public void Set<T>(Field field, T value) => throw new NotImplementedException();
    }

    public enum AccessLevel { Public = 0, Vendor = 1, Application = 2 }

    public class ExtensibleStorageManager
    {
        public static bool SchemaExists(System.Guid guid) => throw new NotImplementedException();
        public static ISet<Schema> GetSchemas(bool includeNonReadable, bool includeNonWritable) => throw new NotImplementedException();
    }
}

namespace Autodesk.Revit.DB.Events
{
    public class DocumentChangedEventArgs : EventArgs
    {
        public ICollection<ElementId> GetModifiedElementIds() => throw new NotImplementedException();
        public ICollection<ElementId> GetAddedElementIds() => throw new NotImplementedException();
        public ICollection<ElementId> GetDeletedElementIds() => throw new NotImplementedException();
        public Document GetDocument() => throw new NotImplementedException();
        public ICollection<ElementId> GetTransactionNames() => throw new NotImplementedException();
    }
    public class DocumentOpenedEventArgs : EventArgs
    {
        public Document Document { get; }
        public Autodesk.Revit.ApplicationServices.Application Application { get; }
        public bool IsCancelled() => throw new NotImplementedException();
    }
    public class DocumentClosingEventArgs : EventArgs
    {
        public long DocumentId { get; }
        public void Cancel() => throw new NotImplementedException();
    }
    public class DocumentSavedEventArgs : EventArgs
    {
        public Document Document { get; }
    }
    public class DocumentSynchronizedWithCentralEventArgs : EventArgs
    {
        public Document Document { get; }
    }
    public class IdlingEventArgs : EventArgs
    {
        public void SetRaiseWithoutDelay() => throw new NotImplementedException();
    }
    public class ViewActivatedEventArgs : EventArgs
    {
        public View CurrentActiveView { get; }
        public View PreviousActiveView { get; }
        public Document Document { get; }
    }
    public class DocumentCreatedEventArgs : EventArgs { public Document Document { get; } }
    public class DocumentClosedEventArgs : EventArgs { public long DocumentId { get; } }
    public class FailuresProcessingEventArgs : EventArgs
    {
        public FailuresAccessor GetFailuresAccessor() => throw new NotImplementedException();
        public void SetProcessingResult(FailureProcessingResult result) => throw new NotImplementedException();
    }
}

namespace Autodesk.Revit.DB.Analysis
{
    public class EnergyAnalysisDetailModel : Element
    {
        public static EnergyAnalysisDetailModel Create(Document doc, EnergyAnalysisDetailModelOptions options) => throw new NotImplementedException();
        public IList<EnergyAnalysisSpace> GetAnalyticalSpaces() => throw new NotImplementedException();
    }
    public class EnergyAnalysisDetailModelOptions
    {
        public EnergyAnalysisDetailModelOptions() { }
        public bool SimplifyCurtainSystems { get; set; }
        public EnergyAnalysisDetailModelTier Tier { get; set; }
    }
    public enum EnergyAnalysisDetailModelTier { Final, SecondLevelBoundary, FirstLevelBoundaries }
    public class EnergyAnalysisSpace : Element { }
    public class EnergyAnalysisSurface : Element { }
}

namespace Autodesk.Revit.DB.ExternalService
{
    public class ExternalServiceId
    {
        public ExternalServiceId() { }
        public ExternalServiceId(System.Guid serviceGuid) { }
    }
    public interface IExternalService { string GetName(); string GetDescription(); string GetVendorId(); ExternalServiceId GetServiceId(); }
    public class ExternalServiceRegistry
    {
        public static ExternalServiceRegistry GetInstance() => throw new NotImplementedException();
        public ExternalService GetService(ExternalServiceId id) => throw new NotImplementedException();
    }
    public class ExternalService
    {
        public ExternalServiceId Id { get; }
        public void AddServer(IExternalServer server) => throw new NotImplementedException();
        public IList<System.Guid> GetServerIds() => throw new NotImplementedException();
        public IExternalServer GetServer(System.Guid serverId) => throw new NotImplementedException();
    }
    public interface IExternalServer { System.Guid GetServerId(); ExternalServiceId GetServiceId(); string GetName(); string GetDescription(); string GetVendorId(); }
}

namespace Autodesk.Revit.DB.DirectContext3D
{
    public interface IDirectContext3DServer : ExternalService.IExternalServer
    {
        bool UseInTransparentPass(View dBView);
        bool CanExecute(View dBView);
        Outline GetBoundingBox(View dBView);
        void RenderScene(View dBView, DisplayStyle displayStyle);
    }
}
