// Revit API CI Stubs — Autodesk.Revit.Attributes
using System;

namespace Autodesk.Revit.Attributes
{
    public enum TransactionMode { Manual, Automatic, ReadOnly }
    public enum RegenerationOption { Manual, Automatic }
    public enum JournalingMode { NoCommandData, UsingCommandData }

    [AttributeUsage(AttributeTargets.Class)] public sealed class TransactionAttribute : Attribute { public TransactionAttribute(TransactionMode mode) { Mode = mode; } public TransactionMode Mode { get; } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class RegenerationAttribute : Attribute { public RegenerationAttribute(RegenerationOption opt) { Option = opt; } public RegenerationOption Option { get; } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class JournalingAttribute : Attribute { public JournalingAttribute(JournalingMode mode) { Mode = mode; } public JournalingMode Mode { get; } }
}
