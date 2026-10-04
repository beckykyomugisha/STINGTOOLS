using System;

namespace StingTools.Core
{
    /// <summary>
    /// Which <c>project_config.json</c> a project's tag configuration is loaded from.
    /// Revit-free, so the choice is unit-tested; the callers supply the two candidate
    /// paths and a file-exists probe.
    /// <para>
    /// Two places are legitimate, and before this class only one was read:
    /// </para>
    /// <list type="number">
    ///   <item><b>Beside the model</b> — where Configure → Save and the auto-tagger write it.
    ///         It wins, because it is the copy the user edits in place.</item>
    ///   <item><b>The project overlay</b> — <c>_BIM_COORD/project_config.json</c>, where an
    ///         owner pack (KUT: <c>project-templates/KUT/_BIM_COORD/</c>) deploys it beside
    ///         <c>owner_standards.json</c>, <c>lod_matrix.json</c> and the rest. Nothing read
    ///         it, so a deployed pack silently ran on the built-in defaults: on KUT, building
    ///         codes BLD1–BLD3 + EXT instead of BLD1–BLD6 + EXT, and sequence numbers not
    ///         grouped per building.</item>
    /// </list>
    /// <para>
    /// When both exist the overlay is <see cref="Choice.Shadowed"/>. That is reported, not
    /// resolved: the copy beside the model may be a deliberate edit, or it may be the
    /// auto-tagger having persisted defaults before the overlay was ever read, and only a
    /// person can tell which.
    /// </para>
    /// </summary>
    internal static class ProjectConfigLocator
    {
        public const string FileName = "project_config.json";

        /// <summary>The overlay location, relative to the project, as
        /// <c>ProjectFolderEngine.ResolveProjectOverridePath</c> takes it.</summary>
        public const string OverlayRelativePath = "_BIM_COORD/project_config.json";

        public sealed class Choice
        {
            /// <summary>The file to load, or null when the project has neither.</summary>
            public string Path { get; set; }

            /// <summary>"beside the model", "project overlay (_BIM_COORD)", or null.</summary>
            public string Source { get; set; }

            /// <summary>An overlay that exists but is not loaded because the copy beside the
            /// model takes precedence. Null when nothing is shadowed.</summary>
            public string Shadowed { get; set; }
        }

        public static Choice Choose(string besideModelPath, string overlayPath, Func<string, bool> exists)
        {
            if (exists == null) throw new ArgumentNullException(nameof(exists));
            bool beside = !string.IsNullOrEmpty(besideModelPath) && exists(besideModelPath);
            bool overlay = !string.IsNullOrEmpty(overlayPath) && exists(overlayPath);

            if (beside)
                return new Choice
                {
                    Path = besideModelPath,
                    Source = "beside the model",
                    Shadowed = overlay && !SamePath(besideModelPath, overlayPath) ? overlayPath : null
                };
            if (overlay)
                return new Choice { Path = overlayPath, Source = "project overlay (_BIM_COORD)" };
            return new Choice();
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b),
                          StringComparison.OrdinalIgnoreCase);
    }
}
