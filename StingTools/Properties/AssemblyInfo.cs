using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System;

[assembly: AssemblyTitle("StingTools")]
[assembly: AssemblyDescription("Unified STING Tools Revit Plugin — Docs, Tags, Temp")]
[assembly: AssemblyCompany("STING BIM")]
[assembly: AssemblyProduct("StingTools")]
[assembly: AssemblyCopyright("Copyright © STING BIM 2026")]
[assembly: ComVisible(false)]
[assembly: Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

// StingTools is a Revit plugin. Revit only runs on Windows, and the csproj
// already targets net8.0-windows. Declaring the assembly's supported platform
// at this level tells the CA1416 analyzer that every call site in this
// assembly is Windows-qualified, so calls into Windows-only APIs from
// Planscape.PluginSync (SyncScheduler, OfflineQueue) and System.Drawing etc
// don't need per-method [SupportedOSPlatform] annotations.
[assembly: SupportedOSPlatform("windows")]
