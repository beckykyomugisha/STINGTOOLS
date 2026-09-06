// RevitStubs.cs — the minimum Autodesk.Revit.DB surface needed for the linked
// Clash sources to COMPILE against net8.0, with no Revit DLLs on the test host.
//
// Why this file exists (#553)
// ---------------------------
// This project has never referenced the Revit API. It <Compile Include>s selected
// plugin sources on the assumption they stay Revit-free. That assumption was
// unenforced, and c98500b5a ("Fix missing using directives across 163 files")
// broke it on 2026-05-17 by adding `using Autodesk.Revit.DB;` to five Clash files
// at once. Nothing in that commit was wrong for the plugin — it simply had no way
// to know those files were load-bearing for a Revit-free test project.
//
// The result was silence, not failure: a test project that does not compile
// reports no red and no count, so 56 clash tests went offline for two and a half
// months while the coverage headline kept counting them.
//
// Four of the five files only needed the NAMESPACE to resolve — they import it and
// use nothing from it. Only ClashPersistence.CanonicalPath genuinely takes a
// Document, and only to hand it to OutputLocationHelper.
//
// Design rule for anything added here
// -----------------------------------
// Types exist so the code compiles. Behaviour THROWS. A stub that returned an
// empty collection or a default value would let a test pass by measuring nothing,
// which is the exact failure mode this suite is supposed to catch elsewhere. If a
// test ever needs real behaviour from one of these, that is the signal to give
// this project a real Revit reference — not to teach the stub to fake it.
//
// This file is not the safety net. The safety net is the CI job that builds every
// test project and fails on any that do not compile; a shim can always drift again.

using System;

namespace Autodesk.Revit.DB
{
    /// <summary>
    /// Opaque stand-in for the Revit document handle.
    ///
    /// Deliberately has no members. The linked Clash sources only ever pass a
    /// Document through to another function — none of them read anything off it —
    /// so an opaque token is the whole truthful surface. Adding properties here
    /// would be inventing a document, and any clash test that needed a real one
    /// would be testing this file rather than the kernel.
    /// </summary>
    public sealed class Document
    {
        internal Document() => throw new NotSupportedException(
            "Autodesk.Revit.DB.Document is a compile-time stub for StingTools.Clash.Tests " +
            "and cannot be instantiated. If a clash test genuinely needs a Revit Document, " +
            "give this project a real Revit API reference instead of extending the stub.");
    }
}

namespace StingTools.Core
{
    /// <summary>
    /// Compile-time stub for the plugin's output-folder resolver, reached only by
    /// <c>ClashPersistence.CanonicalPath</c>. The real one walks StingPaths and the
    /// project folder tree, neither of which exists on a test host.
    ///
    /// It THROWS rather than returning a temp path. CanonicalPath's real body is
    /// <c>GetOutputDirectory(doc) ?? Path.GetTempPath()</c>, so a stub returning
    /// null would silently hand back the temp directory and a test asserting "the
    /// canonical path is where the writer writes" would pass while proving nothing
    /// about the canonical path at all. No clash test calls CanonicalPath today; if
    /// one ever needs to, it needs a real Revit reference, not a friendlier stub.
    /// </summary>
    internal static class OutputLocationHelper
    {
        public static string GetOutputDirectory(Autodesk.Revit.DB.Document doc = null)
            => throw new NotSupportedException(
                "OutputLocationHelper is a compile-time stub for StingTools.Clash.Tests. " +
                "Resolving a real project output directory needs the Revit API and a " +
                "project folder tree; returning a temp path here would make " +
                "ClashPersistence.CanonicalPath look correct while measuring nothing.");
    }
}
